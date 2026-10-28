using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>
/// 按键精灵引擎（NativeAOT 版）：
///   - 全局热键（RegisterHotKey）触发宏；
///   - “拦截键”模式用低层键盘/鼠标钩子吞掉触发键并替换为宏（AutoHotkey 式 remap）；
///   - 全局录制（低层钩子，F12 停止），把真实按键/鼠标点击转成步骤；
///   - SendInput 扫描码回放，适合 KOF 等格斗游戏的一键连招。
/// 钩子必须在带消息循环的 UI 线程安装，所以所有安装/注册动作都通过 ShelfWindow 的
/// WM_APP 消息回到 UI 线程执行；回放本身在后台线程，避免卡住界面。
/// </summary>
public static class MacroEngine
{
    public const uint WmHotkey = 0x0312;
    public const int WmAppReload = 0x8001;
    public const int WmAppRecordStart = 0x8002;
    public const int WmAppRecordStop = 0x8003;
    public const int WmAppStopPlayback = 0x8004;

    /// <summary>录制过程中每记录一步就推送一次（已在 UI 线程或由宿主切回 UI 线程）。</summary>
    public static event Action<MacroStep>? StepRecorded;
    /// <summary>录制开始/结束。参数：是否录制中、结束原因、当前完整步骤快照。</summary>
    public static event Action<bool, string?, List<MacroStep>>? RecordingChanged;

    public static bool IsRecording => _recording;
    public static bool IsRunning => _running;

    #region Win32 常量与结构

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const int INPUT_KEYBOARD = 1;
    private const int INPUT_MOUSE = 0;
    private const long InjectMagic = 0x4D4F4F4E; // 'MOON'

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint XBUTTON1 = 1;
    private const uint XBUTTON2 = 2;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int x;
        public int y;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

    #endregion

    #region 状态

    private static readonly object Gate = new();
    private static readonly HookProc KbProc = KbHook;
    private static readonly HookProc MsProc = MsHook;

    private static IntPtr _hwnd;
    private static IntPtr _kbHook;
    private static IntPtr _msHook;

    private static volatile bool _recording;
    private static volatile bool _running;
    private static volatile bool _globalEnabled = true;

    private static Dictionary<string, MacroRecord> _remapKb = new(StringComparer.Ordinal);
    private static MouseBinding[] _mouseBindings = Array.Empty<MouseBinding>();
    private static string _lastKbTrigger = "";
    private static long _lastKbTriggerMs;
    private static readonly Dictionary<int, MacroRecord> HotkeyMap = new();
    private static readonly Dictionary<string, string> RegistrationErrors = new(StringComparer.Ordinal);
    private static CancellationTokenSource? _playCts;
    private static int _nextHotkeyId;
    private const int HotkeyIdBase = 0x4D00;

    private sealed class MouseBinding
    {
        public string Button = "";
        public MacroRecord Macro = new();
        public bool Swallow;
    }

    #endregion

    #region 键表

    private static readonly Dictionary<string, ushort> KeyMap = BuildKeyMap();
    private static readonly Dictionary<ushort, string> VkNames = BuildVkNames();

    private static Dictionary<string, ushort> BuildKeyMap()
    {
        var m = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 26; i++) m[((char)('a' + i)).ToString()] = (ushort)(0x41 + i);
        for (int i = 0; i <= 9; i++) m[i.ToString()] = (ushort)(0x30 + i);
        for (int i = 1; i <= 24; i++) m["f" + i] = (ushort)(0x70 + i - 1);

        void Add(string name, ushort vk) => m[name] = vk;

        Add("space", 0x20); Add("空格", 0x20);
        Add("enter", 0x0D); Add("return", 0x0D); Add("回车", 0x0D);
        Add("esc", 0x1B); Add("escape", 0x1B); Add("退出", 0x1B);
        Add("tab", 0x09); Add("backspace", 0x08); Add("bs", 0x08);
        Add("delete", 0x2E); Add("del", 0x2E);
        Add("insert", 0x2D); Add("ins", 0x2D);
        Add("home", 0x24); Add("end", 0x23);
        Add("pgup", 0x21); Add("pageup", 0x21); Add("prior", 0x21);
        Add("pgdn", 0x22); Add("pagedown", 0x22); Add("next", 0x22);
        Add("up", 0x26); Add("上", 0x26);
        Add("down", 0x28); Add("下", 0x28);
        Add("left", 0x25); Add("左", 0x25);
        Add("right", 0x27); Add("右", 0x27);

        // 修饰键使用左侧物理键码：通用码不更新物理键状态，部分程序检测不到。
        Add("ctrl", 0xA2); Add("control", 0xA2); Add("lctrl", 0xA2); Add("rctrl", 0xA3);
        Add("shift", 0xA0); Add("lshift", 0xA0); Add("rshift", 0xA1);
        Add("alt", 0xA4); Add("menu", 0xA4); Add("lalt", 0xA4); Add("ralt", 0xA5);
        Add("win", 0x5B); Add("lwin", 0x5B); Add("rwin", 0x5C); Add("windows", 0x5B);

        Add("capslock", 0x14); Add("numlock", 0x90); Add("scrolllock", 0x91);
        Add("printscreen", 0x2C); Add("prtsc", 0x2C); Add("snapshot", 0x2C);
        Add("pause", 0x13); Add("apps", 0x5D);

        for (int i = 0; i <= 9; i++)
        {
            Add("numpad" + i, (ushort)(0x60 + i));
            Add("np" + i, (ushort)(0x60 + i));
            Add("小键盘" + i, (ushort)(0x60 + i));
        }
        Add("numpadmultiply", 0x6A); Add("numpad*", 0x6A);
        Add("numpadadd", 0x6B); Add("numpadplus", 0x6B);
        Add("numpadsubtract", 0x6D); Add("numpadminus", 0x6D);
        Add("numpaddecimal", 0x6E); Add("numpaddot", 0x6E);
        Add("numpaddivide", 0x6F); Add("numpad/", 0x6F);

        Add("plus", 0xBB); Add("=", 0xBB);
        Add("minus", 0xBD); Add("-", 0xBD);
        Add("comma", 0xBC); Add(",", 0xBC);
        Add("period", 0xBE); Add(".", 0xBE);
        Add("slash", 0xBF); Add("/", 0xBF);
        Add("backslash", 0xDC); Add("\\", 0xDC);
        Add("semicolon", 0xBA); Add(";", 0xBA);
        Add("quote", 0xDE); Add("'", 0xDE);
        Add("lbracket", 0xDB); Add("[", 0xDB);
        Add("rbracket", 0xDD); Add("]", 0xDD);
        Add("backquote", 0xC0); Add("`", 0xC0); Add("tilde", 0xC0);
        return m;
    }

    private static Dictionary<ushort, string> BuildVkNames()
    {
        var d = new Dictionary<ushort, string>();
        for (int i = 0; i < 26; i++) d[(ushort)(0x41 + i)] = ((char)('A' + i)).ToString();
        for (int i = 0; i <= 9; i++) d[(ushort)(0x30 + i)] = i.ToString();
        for (int i = 1; i <= 24; i++) d[(ushort)(0x70 + i - 1)] = "F" + i;
        d[0x20] = "SPACE"; d[0x0D] = "ENTER"; d[0x1B] = "ESC"; d[0x09] = "TAB";
        d[0x08] = "BACKSPACE"; d[0x2E] = "DELETE"; d[0x2D] = "INSERT";
        d[0x24] = "HOME"; d[0x23] = "END"; d[0x21] = "PGUP"; d[0x22] = "PGDN";
        d[0x26] = "UP"; d[0x28] = "DOWN"; d[0x25] = "LEFT"; d[0x27] = "RIGHT";
        d[0x10] = "SHIFT"; d[0x11] = "CTRL"; d[0x12] = "ALT";
        d[0xA0] = "SHIFT"; d[0xA1] = "SHIFT"; d[0xA2] = "CTRL"; d[0xA3] = "CTRL";
        d[0xA4] = "ALT"; d[0xA5] = "ALT";
        d[0x5B] = "WIN"; d[0x5C] = "WIN";
        d[0x14] = "CAPSLOCK"; d[0x90] = "NUMLOCK"; d[0x91] = "SCROLLLOCK";
        d[0x2C] = "PRINTSCREEN"; d[0x13] = "PAUSE"; d[0x5D] = "APPS";
        for (int i = 0; i <= 9; i++) d[(ushort)(0x60 + i)] = "NUMPAD" + i;
        d[0x6A] = "NUMPADMULTIPLY"; d[0x6B] = "NUMPADADD";
        d[0x6D] = "NUMPADSUBTRACT"; d[0x6E] = "NUMPADDECIMAL"; d[0x6F] = "NUMPADDIVIDE";
        d[0xBB] = "PLUS"; d[0xBD] = "MINUS"; d[0xBC] = "COMMA"; d[0xBE] = "PERIOD";
        d[0xBF] = "SLASH"; d[0xDC] = "BACKSLASH"; d[0xBA] = "SEMICOLON"; d[0xDE] = "QUOTE";
        d[0xDB] = "LBRACKET"; d[0xDD] = "RBRACKET"; d[0xC0] = "BACKQUOTE";
        return d;
    }

    public static ushort KeyVk(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var key = name.Trim();
        if (KeyMap.TryGetValue(key, out var vk)) return vk;
        if (key.StartsWith("VK_0X", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(key.AsSpan(5), System.Globalization.NumberStyles.HexNumber, null, out var raw))
            return raw;
        return 0;
    }

    private static string VkName(int vk)
    {
        if (VkNames.TryGetValue((ushort)vk, out var name)) return name;
        return "VK_0x" + vk.ToString("X2");
    }

    private static bool IsModifier(int vk) => vk is
        0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static string CanonicalModifier(string token)
    {
        return token.Trim().ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" or "LCTRL" or "RCTRL" => "CTRL",
            "SHIFT" or "LSHIFT" or "RSHIFT" => "SHIFT",
            "ALT" or "MENU" or "LALT" or "RALT" => "ALT",
            "WIN" or "LWIN" or "RWIN" or "WINDOWS" => "WIN",
            _ => "",
        };
    }

    /// <summary>把触发键写法统一成 CTRL+SHIFT+1 / XBUTTON1 之类，用于比较和注册。</summary>
    public static string NormalizeTrigger(string? trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger)) return "";
        var mods = new List<string>();
        var keys = new List<string>();
        foreach (var raw in trigger.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var mod = CanonicalModifier(raw);
            if (mod.Length > 0) { if (!mods.Contains(mod)) mods.Add(mod); }
            else keys.Add(NormalizeKeyName(raw));
        }
        if (keys.Count == 1)
        {
            var k = keys[0];
            if (k is "MOUSEBACK" or "SIDEBUTTON1") k = "XBUTTON1";
            else if (k is "MOUSEFORWARD" or "SIDEBUTTON2") k = "XBUTTON2";
            else if (k is "MIDDLEMOUSE" or "MOUSEMID") k = "MOUSEMIDDLE";
            keys[0] = k;
        }
        var ordered = new List<string>();
        foreach (var order in new[] { "CTRL", "SHIFT", "ALT", "WIN" })
            if (mods.Contains(order)) ordered.Add(order);
        ordered.AddRange(keys.Where(k => !mods.Contains(k)));
        return string.Join('+', ordered);
    }

    private static string NormalizeKeyName(string raw)
    {
        var key = raw.Trim().ToUpperInvariant();
        return key switch
        {
            "RETURN" => "ENTER",
            "ESCAPE" => "ESC",
            "BS" => "BACKSPACE",
            "DEL" => "DELETE",
            "INS" => "INSERT",
            "PAGEUP" or "PRIOR" => "PGUP",
            "PAGEDOWN" or "NEXT" => "PGDN",
            "MENU" => "ALT",
            "CONTROL" => "CTRL",
            "WINDOWS" => "WIN",
            _ => key,
        };
    }

    private static string ChordFor(int vk)
    {
        var name = VkName(vk);
        if (IsModifier(vk)) return name;
        var mods = ModifiersDown();
        return mods.Count == 0 ? name : string.Join('+', mods) + "+" + name;
    }

    private static List<string> ModifiersDown()
    {
        var list = new List<string>(4);
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) list.Add("CTRL");
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) list.Add("SHIFT");
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) list.Add("ALT");
        if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0) list.Add("WIN");
        return list;
    }

    #endregion

    #region 生命周期

    /// <summary>绑定宿主窗口。钩子必须在窗口所属线程（有消息循环）安装，所以通过消息回到 UI 线程执行。</summary>
    public static void Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        _hwnd = hwnd;
        PostMessageW(hwnd, WmAppReload, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>关闭窗口时调用：注销热键并卸载钩子。</summary>
    public static void Detach()
    {
        UnregisterAll();
        UninstallHooks();
        _hwnd = IntPtr.Zero;
        _remapKb = new Dictionary<string, MacroRecord>(StringComparer.Ordinal);
        _mouseBindings = Array.Empty<MouseBinding>();
    }

    /// <summary>数据库里的宏变化后请求 UI 线程重载（线程安全）。</summary>
    public static void RequestReload()
    {
        var hwnd = _hwnd;
        if (hwnd == IntPtr.Zero) return;
        PostMessageW(hwnd, WmAppReload, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>UI 线程收到 WM_APP 后调用；返回 true 表示已处理。</summary>
    public static bool HandleAppMessage(int msg)
    {
        switch (msg)
        {
            case WmAppReload:
                ReloadNow();
                return true;
            case WmAppRecordStart:
                StartRecordingNow();
                return true;
            case WmAppRecordStop:
                StopRecordingNow("已停止", fromHook: false);
                return true;
            case WmAppStopPlayback:
                StopPlayback();
                return true;
            default:
                return false;
        }
    }

    /// <summary>WM_HOTKEY：id → 宏。</summary>
    public static void HandleHotKey(int id)
    {
        MacroRecord? rec = null;
        lock (Gate)
        {
            if (HotkeyMap.TryGetValue(id, out var m)) rec = m;
        }
        if (rec != null) TryStartPlayback(rec, 0, test: false);
    }

    #endregion

    #region 注册 / 热键

    private static void ReloadNow()
    {
        if (_hwnd == IntPtr.Zero) return;
        var macros = new List<MacroRecord>();
        _globalEnabled = MacroStore.Instance.GetGlobalEnabled();
        if (_globalEnabled)
        {
            try { macros = MacroStore.Instance.ListEnabled(); }
            catch (Exception ex) { Log.Error("读取宏失败", ex); }
        }

        UnregisterAll();
        lock (Gate) RegistrationErrors.Clear();

        var remap = new Dictionary<string, MacroRecord>(StringComparer.Ordinal);
        var bindings = new List<MouseBinding>();
        var seen = new Dictionary<string, MacroRecord>(StringComparer.Ordinal);
        _nextHotkeyId = 0;

        foreach (var m in macros)
        {
            var trigger = NormalizeTrigger(m.Trigger);
            if (trigger.Length == 0)
            {
                SetError(m.Id, "未设置触发键");
                continue;
            }
            if (seen.TryGetValue(trigger, out var other))
            {
                SetError(m.Id, $"与“{other.Name}”的触发键重复");
                continue;
            }
            seen[trigger] = m;

            var mouseButton = MouseButtonOf(trigger);
            bool isMouse = mouseButton != null;
            if (m.TriggerMode == "remap")
            {
                if (isMouse)
                    bindings.Add(new MouseBinding { Button = mouseButton!, Macro = m, Swallow = true });
                else
                    remap[trigger] = m;
                continue;
            }

            // 热键模式
            if (isMouse)
            {
                // RegisterHotKey 不支持鼠标侧键：靠低层钩子触发，但把原事件放行。
                bindings.Add(new MouseBinding { Button = mouseButton!, Macro = m, Swallow = false });
                continue;
            }
            if (!TryParseHotkey(trigger, out var mods, out var vk, out var error))
            {
                SetError(m.Id, error ?? "无法注册该热键");
                continue;
            }
            int id = HotkeyIdBase + (_nextHotkeyId++);
            if (!RegisterHotKey(_hwnd, id, mods, vk))
            {
                int code = Marshal.GetLastWin32Error();
                SetError(m.Id, code == 1409
                    ? "热键已被其他程序占用"
                    : "热键注册失败（错误 " + code + "）");
                continue;
            }
            HotkeyMap[id] = m;
        }

        _remapKb = remap;
        _mouseBindings = bindings.ToArray();
        EnsureHooks();
    }

    private static void SetError(string id, string message)
    {
        lock (Gate) RegistrationErrors[id] = message;
        Log.Warn("宏 " + id + " 注册失败: " + message);
    }

    /// <summary>给 API 返回的列表补充注册错误（不改变数据库）。</summary>
    public static void Decorate(List<MacroRecord> macros)
    {
        if (macros == null || macros.Count == 0) return;
        var seen = new Dictionary<string, MacroRecord>(StringComparer.Ordinal);
        foreach (var m in macros)
        {
            m.HotkeyError = null;
            if (!m.Enabled) continue;
            var trigger = NormalizeTrigger(m.Trigger);
            if (trigger.Length == 0) { m.HotkeyError = "未设置触发键"; continue; }
            if (seen.TryGetValue(trigger, out var other))
            {
                m.HotkeyError = $"与“{other.Name}”的触发键重复";
                continue;
            }
            seen[trigger] = m;
            if (m.TriggerMode == "hotkey" && MouseButtonOf(trigger) == null &&
                !TryParseHotkey(trigger, out _, out _, out var error))
                m.HotkeyError = error;
        }
        lock (Gate)
        {
            foreach (var m in macros)
            {
                if (m.HotkeyError == null && RegistrationErrors.TryGetValue(m.Id, out var err))
                    m.HotkeyError = err;
            }
        }
    }

    private static bool TryParseHotkey(string trigger, out uint mods, out uint vk, out string? error)
    {
        mods = 0;
        vk = 0;
        error = null;
        var parts = trigger.Split('+', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            switch (p)
            {
                case "CTRL": mods |= MOD_CONTROL; continue;
                case "SHIFT": mods |= MOD_SHIFT; continue;
                case "ALT": mods |= MOD_ALT; continue;
                case "WIN": mods |= MOD_WIN; continue;
            }
            var code = KeyVk(p);
            if (code == 0)
            {
                error = "无法识别的按键：" + p;
                return false;
            }
            if (vk != 0)
            {
                error = "全局热键只能包含一个主键（组合键请用 Ctrl+Shift+X 形式）";
                return false;
            }
            if (IsModifier(code))
            {
                error = "全局热键需要一个非修饰键作为主键";
                return false;
            }
            vk = code;
        }
        if (vk == 0) { error = "缺少主键"; return false; }
        mods |= MOD_NOREPEAT;
        return true;
    }

    private static string? MouseButtonOf(string normalizedTrigger) => normalizedTrigger switch
    {
        "XBUTTON1" => "x1",
        "XBUTTON2" => "x2",
        "MOUSEMIDDLE" => "middle",
        _ => null,
    };

    private static void UnregisterAll()
    {
        lock (Gate)
        {
            foreach (var id in HotkeyMap.Keys)
            {
                try { if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, id); } catch { }
            }
            HotkeyMap.Clear();
        }
    }

    #endregion

    #region 钩子

    private static void EnsureHooks()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool need = _recording || _remapKb.Count > 0 || _mouseBindings.Length > 0;
        if (need)
        {
            if (_kbHook == IntPtr.Zero)
            {
                _kbHook = SetWindowsHookExW(WH_KEYBOARD_LL, KbProc, GetModuleHandleW(null), 0);
                if (_kbHook == IntPtr.Zero) Log.Warn("键盘钩子安装失败，错误码=" + Marshal.GetLastWin32Error());
            }
            if (_msHook == IntPtr.Zero)
            {
                _msHook = SetWindowsHookExW(WH_MOUSE_LL, MsProc, GetModuleHandleW(null), 0);
                if (_msHook == IntPtr.Zero) Log.Warn("鼠标钩子安装失败，错误码=" + Marshal.GetLastWin32Error());
            }
        }
        else
        {
            UninstallHooks();
        }
    }

    private static void UninstallHooks()
    {
        if (_kbHook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_kbHook); } catch { }
            _kbHook = IntPtr.Zero;
        }
        if (_msHook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_msHook); } catch { }
            _msHook = IntPtr.Zero;
        }
    }

    /// <summary>只认“本引擎注入”的标记：录制时允许其它自动化工具注入的按键，避免把宏自己的回放录进去。</summary>
    private static bool IsOwnInjection(KBDLLHOOKSTRUCT kb) => kb.dwExtraInfo.ToInt64() == InjectMagic;
    private static bool IsOwnInjection(MSLLHOOKSTRUCT ms) => ms.dwExtraInfo.ToInt64() == InjectMagic;

    private static IntPtr KbHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool up = msg is WM_KEYUP or WM_SYSKEYUP;
            if (down || up)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (_recording)
                {
                    if (!IsOwnInjection(kb))
                    {
                        // F12 是录制停止键
                        if (down && kb.vkCode == 0x7B)
                        {
                            StopRecordingNow("F12", fromHook: true);
                            return (IntPtr)1;
                        }
                        RecordKey((int)kb.vkCode, down);
                    }
                    return CallNextHookEx(_kbHook, nCode, wParam, lParam);
                }
                if (down && !IsOwnInjection(kb) && _globalEnabled && !_running)
                {
                    var chord = ChordFor((int)kb.vkCode);
                    if (_remapKb.TryGetValue(chord, out var macro))
                    {
                        long now = Environment.TickCount64;
                        // 按住不放时系统会持续发 keydown，防抖避免连发
                        if (chord == _lastKbTrigger && now - _lastKbTriggerMs < 250)
                            return (IntPtr)1;
                        _lastKbTrigger = chord;
                        _lastKbTriggerMs = now;
                        TryStartPlayback(macro, 0, test: false);
                        return (IntPtr)1; // 吞掉原按键
                    }
                }
            }
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private static IntPtr MsHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            string? button = msg switch
            {
                WM_LBUTTONDOWN or WM_LBUTTONUP => "left",
                WM_RBUTTONDOWN or WM_RBUTTONUP => "right",
                WM_MBUTTONDOWN or WM_MBUTTONUP => "middle",
                WM_XBUTTONDOWN or WM_XBUTTONUP => ((ms.mouseData >> 16) & 0xFFFF) == XBUTTON2 ? "x2" : "x1",
                _ => null,
            };
            if (msg == WM_MOUSEWHEEL)
            {
                if (_recording && !IsOwnInjection(ms))
                    RecordWheel((short)((ms.mouseData >> 16) & 0xFFFF), ms.x, ms.y);
                return CallNextHookEx(_msHook, nCode, wParam, lParam);
            }
            if (button != null)
            {
                bool down = msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN;
                if (_recording)
                {
                    if (!IsOwnInjection(ms))
                    {
                        if (down) RecordMouseDown(button, ms.x, ms.y);
                        else RecordMouseUp(button);
                    }
                    return CallNextHookEx(_msHook, nCode, wParam, lParam);
                }
                if (down && !IsOwnInjection(ms) && _globalEnabled && !_running)
                {
                    foreach (var b in _mouseBindings)
                    {
                        if (b.Button != button) continue;
                        TryStartPlayback(b.Macro, 0, test: false);
                        if (b.Swallow) return (IntPtr)1;
                        break;
                    }
                }
            }
        }
        return CallNextHookEx(_msHook, nCode, wParam, lParam);
    }

    #endregion

    #region 录制

    private static readonly List<MacroStep> Recorded = new();
    private static long _lastEventEndMs;
    private static MacroStep? _pendingKeyStep;
    private static long _pendingKeyDownMs;
    private static readonly HashSet<int> PendingKeys = new();
    private static string? _pendingMouseButton;
    private static MacroStep? _pendingMouseStep;
    private static long _pendingMouseDownMs;

    /// <summary>请求开始录制（线程安全，实际在 UI 线程启动）。</summary>
    public static bool RequestStartRecording()
    {
        if (_hwnd == IntPtr.Zero) return false;
        if (_recording) return true;
        PostMessageW(_hwnd, WmAppRecordStart, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    public static bool RequestStopRecording()
    {
        if (_hwnd == IntPtr.Zero) return false;
        if (!_recording) return true;
        PostMessageW(_hwnd, WmAppRecordStop, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private static void StartRecordingNow()
    {
        if (_hwnd == IntPtr.Zero || _recording) return;
        Recorded.Clear();
        _pendingKeyStep = null;
        PendingKeys.Clear();
        _pendingMouseButton = null;
        _pendingMouseStep = null;
        _lastEventEndMs = Environment.TickCount64;
        _recording = true;
        EnsureHooks();
        Log.Info("宏录制开始");
        RecordingChanged?.Invoke(true, null, SnapshotRecorded());
    }

    private static void StopRecordingNow(string reason, bool fromHook)
    {
        if (!_recording) return;
        FinalizePending();
        _recording = false;
        Log.Info("宏录制结束：" + reason + "，共 " + Recorded.Count + " 步");
        if (fromHook) PostMessageW(_hwnd, WmAppReload, IntPtr.Zero, IntPtr.Zero);
        else EnsureHooks();
        RecordingChanged?.Invoke(false, reason, SnapshotRecorded());
    }

    private static void FinalizePending()
    {
        long now = Environment.TickCount64;
        if (_pendingKeyStep != null)
        {
            _pendingKeyStep.HoldMs = (int)Math.Clamp(now - _pendingKeyDownMs, 20, 5000);
            _pendingKeyStep = null;
                PendingKeys.Clear();
            _lastEventEndMs = now;
        }
        if (_pendingMouseStep != null)
        {
            _pendingMouseStep.HoldMs = (int)Math.Clamp(now - _pendingMouseDownMs, 20, 5000);
            _pendingMouseStep = null;
            _pendingMouseButton = null;
            _lastEventEndMs = now;
        }
    }

    private static void RecordKey(int vk, bool down)
    {
        if (IsModifier(vk)) return;
        long now = Environment.TickCount64;
        if (down)
        {
            // 60ms 内按下的第二个键视为同时按（S+D 和弦），并入同一步
            if (_pendingKeyStep != null && PendingKeys.Count > 0 && now - _pendingKeyDownMs <= 60)
            {
                var name = VkName(vk);
                if (!_pendingKeyStep.Keys.Contains(name)) _pendingKeyStep.Keys.Add(name);
                PendingKeys.Add(vk);
                return;
            }
            if (_pendingKeyStep != null) FinalizePending();

            int delay = ClampDelay(now - _lastEventEndMs);
            if (Recorded.Count > 0) Recorded[^1].DelayMs = delay;

            var keys = ModifiersDown();
            keys.Add(VkName(vk));
            var step = new MacroStep
            {
                Kind = "key",
                Keys = keys,
                HoldMs = 40,
                DelayMs = 0,
            };
            Recorded.Add(step);
            _pendingKeyStep = step;
            _pendingKeyDownMs = now;
            PendingKeys.Clear();
            PendingKeys.Add(vk);
            StepRecorded?.Invoke(CopyStep(step));
        }
        else
        {
            PendingKeys.Remove(vk);
            if (_pendingKeyStep != null && PendingKeys.Count == 0) FinalizePending();
        }
    }

    private static void RecordMouseDown(string button, int x, int y)
    {
        long now = Environment.TickCount64;
        if (_pendingKeyStep != null) FinalizePending();
        if (_pendingMouseStep != null) FinalizePending();

        int delay = ClampDelay(now - _lastEventEndMs);
        if (Recorded.Count > 0) Recorded[^1].DelayMs = delay;

        var step = new MacroStep
        {
            Kind = "mouse_click",
            X = x,
            Y = y,
            Button = button,
            HoldMs = 40,
        };
        Recorded.Add(step);
        _pendingMouseStep = step;
        _pendingMouseButton = button;
        _pendingMouseDownMs = now;
        StepRecorded?.Invoke(CopyStep(step));
    }

    private static void RecordMouseUp(string button)
    {
        if (_pendingMouseButton == button) FinalizePending();
    }

    private static void RecordWheel(int delta, int x, int y)
    {
        long now = Environment.TickCount64;
        if (_pendingKeyStep != null) FinalizePending();
        int delay = ClampDelay(now - _lastEventEndMs);
        if (Recorded.Count > 0) Recorded[^1].DelayMs = delay;
        var step = new MacroStep
        {
            Kind = "mouse_wheel",
            X = x,
            Y = y,
            Wheel = delta,
        };
        Recorded.Add(step);
        _lastEventEndMs = now;
        StepRecorded?.Invoke(CopyStep(step));
    }

    private static int ClampDelay(long ms) => (int)Math.Clamp(ms, 0, 600_000);

    private static List<MacroStep> SnapshotRecorded()
    {
        var list = new List<MacroStep>(Recorded.Count);
        foreach (var s in Recorded) list.Add(CopyStep(s));
        return list;
    }

    private static MacroStep CopyStep(MacroStep s) => new()
    {
        Kind = s.Kind,
        Keys = new List<string>(s.Keys),
        HoldMs = s.HoldMs,
        DelayMs = s.DelayMs,
        X = s.X,
        Y = s.Y,
        Button = s.Button,
        Wheel = s.Wheel,
        Text = s.Text,
    };

    #endregion

    #region 回放

    /// <summary>测试运行（会先等待 leadInMs 便于切到游戏窗口）。</summary>
    public static bool RunTest(string id, int? leadInMs)
    {
        var macro = MacroStore.Instance.Get(id);
        if (macro == null) return false;
        return TryStartPlayback(macro, leadInMs ?? macro.LeadInMs, test: true);
    }

    public static void StopPlayback()
    {
        try { _playCts?.Cancel(); } catch { }
    }

    private static bool TryStartPlayback(MacroRecord macro, int leadIn, bool test)
    {
        if (!test && (!_globalEnabled || _recording)) return false;
        lock (Gate)
        {
            if (_running) return false;
            _running = true;
        }
        var snapshot = macro.Clone();
        var cts = new CancellationTokenSource();
        _playCts = cts;
        _ = Task.Run(() => PlaybackAsync(snapshot, Math.Clamp(leadIn, 0, 60_000), cts));
        return true;
    }

    private static void PlaybackAsync(MacroRecord macro, int leadIn, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        timeBeginPeriod(1);
        try
        {
            if (leadIn > 0) Wait(leadIn, ct);
            else Wait(30, ct); // 给触发键抬起留时间，避免目标窗口丢掉第一下
            bool scan = macro.SendMode != "virtual";
            int repeat = Math.Clamp(macro.Repeat, 1, 999);
            for (int r = 0; r < repeat; r++)
            {
                if (r > 0) Wait(macro.RepeatDelayMs, ct);
                foreach (var step in macro.Steps)
                {
                    ct.ThrowIfCancellationRequested();
                    PlayStep(step, scan, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error("宏回放失败：" + macro.Name, ex); }
        finally
        {
            timeEndPeriod(1);
            lock (Gate)
            {
                if (ReferenceEquals(_playCts, cts)) _playCts = null;
                _running = false;
            }
            cts.Dispose();
        }
    }

    private static void PlayStep(MacroStep step, bool scan, CancellationToken ct)
    {
        switch (step.Kind)
        {
            case "delay":
                Wait(step.DelayMs, ct);
                return;
            case "mouse_move":
                MoveTo(step.X, step.Y);
                Wait(step.DelayMs, ct);
                return;
            case "mouse_click":
                if (step.X >= 0 && step.Y >= 0) MoveTo(step.X, step.Y);
                MouseDown(step.Button ?? "left");
                Wait(Math.Max(10, step.HoldMs), ct);
                MouseUp(step.Button ?? "left");
                Wait(step.DelayMs + 6, ct);
                return;
            case "mouse_wheel":
                if (step.X >= 0 && step.Y >= 0) MoveTo(step.X, step.Y);
                SendMouse(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)step.Wheel));
                Wait(step.DelayMs + 6, ct);
                return;
            case "text":
                SendText(step.Text ?? "");
                Wait(step.DelayMs, ct);
                return;
            default:
                PressChord(step, scan, ct);
                return;
        }
    }

    private static void PressChord(MacroStep step, bool scan, CancellationToken ct)
    {
        var mods = new List<ushort>();
        var keys = new List<ushort>();
        foreach (var name in step.Keys)
        {
            var vk = KeyVk(name);
            if (vk == 0) continue;
            var target = IsModifier(vk) ? mods : keys;
            if (!target.Contains(vk)) target.Add(vk);
        }
        mods.AddRange(keys);
        if (mods.Count == 0) return;

        foreach (var vk in mods) SendKeyVk(vk, up: false, scan);
        Wait(Math.Max(10, step.HoldMs), ct);
        for (int i = mods.Count - 1; i >= 0; i--) SendKeyVk(mods[i], up: true, scan);
        Wait(step.DelayMs + 6, ct);
    }

    private static void Wait(int ms, CancellationToken ct)
    {
        if (ms <= 0) { ct.ThrowIfCancellationRequested(); return; }
        if (ct.WaitHandle.WaitOne(ms)) throw new OperationCanceledException(ct);
    }

    private static void SendKeyVk(ushort vk, bool up, bool scan)
    {
        if (vk == 0) return;
        uint flags = up ? KEYEVENTF_KEYUP : 0u;
        ushort wVk = vk;
        ushort wScan = 0;
        if (scan)
        {
            wScan = ScanOf(vk);
            if (wScan != 0)
            {
                wVk = 0;
                flags |= KEYEVENTF_SCANCODE;
            }
        }
        if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = wVk, wScan = wScan, dwFlags = flags, time = 0, dwExtraInfo = (IntPtr)InjectMagic,
                },
            },
        };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 1)
            Log.Warn("SendInput 按键失败，错误码=" + Marshal.GetLastWin32Error());
    }

    private static void SendText(string text)
    {
        foreach (var ch in text)
        {
            var down = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE, dwExtraInfo = (IntPtr)InjectMagic,
                    },
                },
            };
            var up = down;
            up.u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
        }
    }

    private static void MoveTo(int x, int y)
    {
        int vx = GetSystemMetrics(76);   // SM_XVIRTUALSCREEN
        int vy = GetSystemMetrics(77);   // SM_YVIRTUALSCREEN
        int vw = Math.Max(1, GetSystemMetrics(78)); // SM_CXVIRTUALSCREEN
        int vh = Math.Max(1, GetSystemMetrics(79)); // SM_CYVIRTUALSCREEN
        int nx = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1));
        int ny = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1));
        SendMouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, nx, ny, 0);
    }

    private static void MouseDown(string button)
    {
        switch (button)
        {
            case "right": SendMouse(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0); break;
            case "middle": SendMouse(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0); break;
            case "x1": SendMouse(MOUSEEVENTF_XDOWN, 0, 0, XBUTTON1); break;
            case "x2": SendMouse(MOUSEEVENTF_XDOWN, 0, 0, XBUTTON2); break;
            default: SendMouse(MOUSEEVENTF_LEFTDOWN, 0, 0, 0); break;
        }
    }

    private static void MouseUp(string button)
    {
        switch (button)
        {
            case "right": SendMouse(MOUSEEVENTF_RIGHTUP, 0, 0, 0); break;
            case "middle": SendMouse(MOUSEEVENTF_MIDDLEUP, 0, 0, 0); break;
            case "x1": SendMouse(MOUSEEVENTF_XUP, 0, 0, XBUTTON1); break;
            case "x2": SendMouse(MOUSEEVENTF_XUP, 0, 0, XBUTTON2); break;
            default: SendMouse(MOUSEEVENTF_LEFTUP, 0, 0, 0); break;
        }
    }

    private static void SendMouse(uint flags, int dx, int dy, uint mouseData)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = dx, dy = dy, mouseData = mouseData, dwFlags = flags, time = 0,
                    dwExtraInfo = (IntPtr)InjectMagic,
                },
            },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static ushort ScanOf(ushort vk)
    {
        switch (vk)
        {
            case 0x90: return 0x45; // NumLock
            case 0x2C: return 0x37; // PrintScreen
            case 0x13: return 0x45; // Pause
            default:
                uint s = MapVirtualKeyW(vk, 0);
                return s != 0 ? (ushort)s : (ushort)0;
        }
    }

    private static bool IsExtendedKey(ushort vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2D or 0x2E or 0x2F
        or 0x5B or 0x5C or 0x5D
        or 0xA1 or 0xA3 or 0xA5
        or 0x6F or 0x90 or 0x2C;

    #endregion
}
