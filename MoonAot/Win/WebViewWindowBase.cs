using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MoonAot.Services;
using MoonAot.Store;

namespace MoonAot.Win;

/// <summary>
/// WebView2 宿主窗口基类（NativeAOT，无 UI 框架依赖）。
/// 统一处理：共享 WebView2 环境、消息桥、F11 全屏、Esc、窗口尺寸记忆、外部链接。
/// </summary>
public abstract class WebViewWindowBase : Window
{
    private static readonly SemaphoreSlim EnvGate = new(1, 1);
    private static IComObject<ICoreWebView2Environment>? _sharedEnv;
    private static string? _initError;

    protected IComObject<ICoreWebView2Controller>? Controller;
    protected IComObject<ICoreWebView2>? Web;
    private CoreWebView2Events? _events;
    private CoreWebView2ControllerEvents? _controllerEvents;
    private IComObject<ICoreWebView2Controller>? _toolbarController;
    private IComObject<ICoreWebView2>? _toolbarWeb;
    private CoreWebView2Events? _toolbarEvents;

    protected CoreWebView2Events? WebEvents => _events;
    /// <summary>顶部工具栏 WebView 高度（0 = 不创建）。</summary>
    protected virtual int ToolbarHeight => 0;
    protected virtual string? ToolbarUrl => null;
    protected virtual RECT GetContentBounds() => ClientRect;

    private bool _isFullscreen;
    private RECT _savedRect;
    private long _savedStyle;

    private static string ProfileDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonAot", "WebView");

    static WebViewWindowBase()
    {
        try
        {
            Directory.CreateDirectory(ProfileDir);
            WebView2Utilities.Initialize(typeof(WebViewWindowBase).Assembly);
        }
        catch (Exception ex)
        {
            _initError = ex.Message;
            Log.Error("WebView2 初始化失败", ex);
        }
    }

    protected WebViewWindowBase(string? title = null) : base(title)
    {
        Closing += (_, _) => SaveWindowState();
        ApplyAppIcon();
        InitializeWebViewAsync();
    }

    private const uint WM_SETICON = 0x0080;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, int count);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>使用 exe 内嵌图标作为窗口/任务栏图标（ApplicationIcon 已指向 jingxin_reading.ico）。</summary>
    private void ApplyAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            if (ExtractIconExW(exe, 0, out var large, out var small, 1) <= 0) return;
            var hwnd = Handle.Value;
            if (large != IntPtr.Zero) SendMessageW(hwnd, WM_SETICON, new IntPtr(1), large);
            if (small != IntPtr.Zero) SendMessageW(hwnd, WM_SETICON, IntPtr.Zero, small);
        }
        catch { }
    }

    protected abstract void OnWebViewCreated();
    protected abstract void OnWebMessageReceived(string message);

    /// <summary>是否允许浏览器默认右键菜单（阅读器需要，书架关闭以使用自定义菜单）。</summary>
    protected virtual bool EnableDefaultContextMenu => true;
    /// <summary>是否拦截 Esc（阅读器/播放器关闭，书架交给前端处理）。</summary>
    protected virtual bool InterceptEscape => false;
    protected virtual bool EnableDevTools => false;

    public bool IsFullscreen => _isFullscreen;

    protected static async Task<IComObject<ICoreWebView2Environment>?> GetEnvironmentAsync()    {
        if (_sharedEnv != null) return _sharedEnv;
        await EnvGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_sharedEnv != null) return _sharedEnv;
            _sharedEnv = await WebView2.Functions.CreateCoreWebView2EnvironmentWithOptionsAsync(null, ProfileDir, null)
                .ConfigureAwait(true);
            return _sharedEnv;
        }
        finally { EnvGate.Release(); }
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            if (_initError != null)
            {
                Text = Text + " — WebView2 不可用: " + _initError;
                return;
            }
            var env = await GetEnvironmentAsync().ConfigureAwait(true);
            if (env == null) throw new InvalidOperationException("创建 WebView2 环境失败");
            if (IsDisposed) return;

            var controller = await env.CreateCoreWebView2ControllerAsync(Handle).ConfigureAwait(true);
            if (controller == null) throw new InvalidOperationException("创建 WebView2 控制器失败");
            Controller = controller;
            controller.Bounds = GetContentBounds();
            try { controller.IsVisible = true; } catch { }
            Web = controller.CoreWebView2 ?? throw new InvalidOperationException("CoreWebView2 为空");

            ConfigureSettings();
            _events = new CoreWebView2Events(Web);
            _events.WebMessageReceived += OnWebMessage;
            _events.NewWindowRequested += OnNewWindowRequested;
            _controllerEvents = new CoreWebView2ControllerEvents(controller);
            _controllerEvents.AcceleratorKeyPressed += OnAcceleratorKeyPressed;

            if (ToolbarHeight > 0 && !string.IsNullOrEmpty(ToolbarUrl))
            {
                try { await InitializeToolbarAsync(env).ConfigureAwait(true); }
                catch (Exception ex) { Log.Warn("创建工具栏失败: " + ex.Message); }
            }

            OnWebViewCreated();
        }
        catch (Exception ex)
        {
            _initError = ex.Message;
            Log.Error("初始化 WebView 失败", ex);
            try { Text = Text + " — WebView 初始化失败: " + ex.Message; } catch { }
        }
    }

    private async Task InitializeToolbarAsync(IComObject<ICoreWebView2Environment> env)
    {
        var controller = await env.CreateCoreWebView2ControllerAsync(Handle).ConfigureAwait(true);
        if (controller == null) return;
        _toolbarController = controller;
        controller.Bounds = ToolbarBounds;
        try { controller.IsVisible = true; } catch { }
        var web = controller.CoreWebView2;
        if (web == null) return;
        _toolbarWeb = web;
        try
        {
            var st = web.Settings;
            if (st != null)
            {
                var s = st.Object;
                s.IsStatusBarEnabled = false;
                s.AreDevToolsEnabled = false;
                s.AreDefaultContextMenusEnabled = false;
                s.IsZoomControlEnabled = false;
                s.IsSwipeNavigationEnabled = false;
                s.IsPinchZoomEnabled = false;
            }
        }
        catch { }
        _toolbarEvents = new CoreWebView2Events(web);
        _toolbarEvents.WebMessageReceived += OnToolbarWebMessage;
        web.Navigate(ToolbarUrl!);
    }

    private RECT ToolbarBounds
    {
        get
        {
            var r = ClientRect;
            r.bottom = r.top + ToolbarHeight;
            return r;
        }
    }

    private void OnToolbarWebMessage(object? sender, ICoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = e.TryGetWebMessageAsString();
            if (!string.IsNullOrEmpty(msg)) OnToolbarMessageReceived(msg);
        }
        catch (Exception ex) { Log.Warn("处理工具栏消息失败: " + ex.Message); }
    }

    protected virtual void OnToolbarMessageReceived(string message) { }

    /// <summary>在工具栏 WebView 执行 JS（线程安全）。</summary>
    protected void SendToToolbar(string js)
    {
        if (IsDisposed || IsDisposing) return;
        if (IsRunningAsUIThread)
        {
            try { _toolbarWeb?.ExecuteScript(js); } catch { }
            return;
        }
        try { _ = RunTaskOnUIThread(() => { try { _toolbarWeb?.ExecuteScript(js); } catch { } }); } catch { }
    }

    private void ConfigureSettings()
    {
        try
        {
            var st = Web?.Settings;
            if (st == null) return;
            var s = st.Object;
            s.IsStatusBarEnabled = false;
            s.AreDevToolsEnabled = EnableDevTools || Debugger.IsAttached;
            s.AreDefaultContextMenusEnabled = EnableDefaultContextMenu;
            s.IsZoomControlEnabled = false;
            s.IsSwipeNavigationEnabled = false;
            s.IsPinchZoomEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = true;
            s.IsBuiltInErrorPageEnabled = true;
        }
        catch (Exception ex) { Log.Warn("配置 WebView 设置失败: " + ex.Message); }
    }

    protected void Navigate(string url)
    {
        try { Web?.Navigate(url); }
        catch (Exception ex) { Log.Warn("导航失败 " + url + ": " + ex.Message); }
    }

    /// <summary>在页面执行 JS（任意线程安全，自动切回 UI 线程）。</summary>
    public void RunScript(string js)
    {
        if (IsDisposed || IsDisposing) return;
        if (IsRunningAsUIThread)
        {
            Execute(js);
            return;
        }
        try
        {
            _ = RunTaskOnUIThread(() => Execute(js));
        }
        catch { }
    }

    private void Execute(string js)
    {
        try { Web?.ExecuteScript(js); } catch { }
    }

    /// <summary>把 JSON 字符串作为参数安全地传给 JS（source-gen，AOT 安全）。</summary>
    public static string JsonStr(string? value) => value == null ? "null" : JsonSerializer.Serialize(value!, Info<string>.I);

    public void NotifyLibraryChanged() => RunScript("try{window.__host&&window.__host.onLibraryChanged&&window.__host.onLibraryChanged()}catch(e){}");

    protected void NotifyImportProgress(int done, int total, string? current)
    {
        RunScript("try{window.__host&&window.__host.onImportProgress&&window.__host.onImportProgress({done:" +
                  done + ",total:" + total + ",current:" + JsonStr(current) + "})}catch(e){}");
    }

    protected void NotifyImportFinished(bool ok, int imported, int skipped, int failed, string? message)
    {
        RunScript("try{window.__host&&window.__host.onImportFinished&&window.__host.onImportFinished({ok:" +
                  (ok ? "true" : "false") + ",imported:" + imported + ",skipped:" + skipped +
                  ",failed:" + failed + ",message:" + JsonStr(message) + "})}catch(e){}");
    }

    protected void ResolveDialog(string requestId, string? path)
    {
        RunScript("try{window.__host&&window.__host.dialogResult&&window.__host.dialogResult(" +
                  JsonStr(requestId) + "," + JsonStr(path) + ")}catch(e){}");
    }

    private void OnWebMessage(object? sender, ICoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = e.TryGetWebMessageAsString();
            if (!string.IsNullOrEmpty(msg)) OnWebMessageReceived(msg);
        }
        catch (Exception ex) { Log.Warn("处理 Web 消息失败: " + ex.Message); }
    }

    private void OnNewWindowRequested(object? sender, ICoreWebView2NewWindowRequestedEventArgs e)
    {
        try
        {
            var uri = e.Uri;
            e.Handled = true;
            if (!string.IsNullOrEmpty(uri)) HandleNewWindow(uri);
        }
        catch { }
    }

    /// <summary>新窗口/新标签链接。默认用系统浏览器打开外部网址，浏览器窗口会覆盖为同窗口导航。</summary>
    protected virtual void HandleNewWindow(string uri)
    {
        if (!(uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
            return;
        if (!uri.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !uri.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private void OnAcceleratorKeyPressed(object? sender, ICoreWebView2AcceleratorKeyPressedEventArgs e)
    {
        try
        {
            if (e.KeyEventKind is not (COREWEBVIEW2_KEY_EVENT_KIND.COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN
                or COREWEBVIEW2_KEY_EVENT_KIND.COREWEBVIEW2_KEY_EVENT_KIND_SYSTEM_KEY_DOWN))
                return;
            if (HandleKey((int)e.VirtualKey)) e.Handled = true;
        }
        catch { }
    }

    protected override LRESULT? WindowProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg is 0x0100 or 0x0104) // WM_KEYDOWN / WM_SYSKEYDOWN
        {
            int vk = unchecked((int)wParam.Value.ToUInt64());
            if (HandleKey(vk)) return LRESULT.Null;
        }
        return base.WindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>统一按键处理：F11 全屏，Esc 由子类决定，其余交给子类。</summary>
    protected virtual bool HandleKey(int vk)
    {
        if (vk == 0x7A) // F11
        {
            ToggleFullscreen();
            return true;
        }
        if (vk == 0x1B) // Esc
        {
            if (_isFullscreen) { ExitFullscreen(); return true; }
            if (InterceptEscape) { OnEscape(); return true; }
        }
        return false;
    }

    protected virtual void OnEscape() { }

    protected override bool OnFocusChanged(bool setOrKill)
    {
        if (setOrKill)
        {
            try
            {
                if (Controller != null)
                {
                    Controller.MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON.COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC, false);
                    return true;
                }
            }
            catch { }
        }
        return base.OnFocusChanged(setOrKill);
    }

    protected override bool OnResized(WindowResizedType type, SIZE size)
    {
        ApplyContentBounds();
        return base.OnResized(type, size);
    }

    /// <summary>重新应用内容区与工具栏的位置（工具栏高度动态变化时调用）。</summary>
    protected void ApplyContentBounds()
    {
        try { if (Controller != null) Controller.Bounds = GetContentBounds(); } catch { }
        try { if (_toolbarController != null) _toolbarController.Bounds = ToolbarBounds; } catch { }
    }

    // ==================== 全屏 ====================

    private const int GWL_STYLE = -16;
    private const long WS_POPUP = 0x80000000L;
    private const long WS_VISIBLE = 0x10000000L;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    public void ToggleFullscreen()
    {
        if (_isFullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        try
        {
            var hwnd = Handle.Value;
            if (!GetWindowRect(hwnd, out _savedRect)) return;
            _savedStyle = GetWindowLongPtrW(hwnd, GWL_STYLE).ToInt64();

            var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (mon == IntPtr.Zero || !GetMonitorInfoW(mon, ref mi)) return;

            SetWindowLongPtrW(hwnd, GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE));
            SetWindowPos(hwnd, IntPtr.Zero,
                mi.rcMonitor.left, mi.rcMonitor.top,
                mi.rcMonitor.Width, mi.rcMonitor.Height,
                SWP_FRAMECHANGED | SWP_NOZORDER | SWP_SHOWWINDOW);
            _isFullscreen = true;
        }
        catch (Exception ex) { Log.Warn("进入全屏失败: " + ex.Message); }
    }

    private void ExitFullscreen()
    {
        try
        {
            var hwnd = Handle.Value;
            SetWindowLongPtrW(hwnd, GWL_STYLE, new IntPtr(_savedStyle));
            int w = _savedRect.Width, h = _savedRect.Height;
            SetWindowPos(hwnd, IntPtr.Zero, _savedRect.left, _savedRect.top, w, h,
                SWP_FRAMECHANGED | SWP_NOZORDER | SWP_SHOWWINDOW);
            _isFullscreen = false;
            try { if (Controller != null) Controller.Bounds = ClientRect; } catch { }
            SetFocus();
        }
        catch (Exception ex) { Log.Warn("退出全屏失败: " + ex.Message); }
    }

    // ==================== 尺寸记忆 ====================

    protected virtual WindowState GetSavedState(AppSettings s) => s.ReaderWindow;

    private void SaveWindowState()
    {
        try
        {
            if (_isFullscreen) ExitFullscreen();
            var cr = ClientRect;
            var wr = WindowRect;
            bool zoomed = IsZoomed;
            DbStore.Instance.UpdateAppSettingsDeferred(a =>
            {
                var target = GetSavedState(a);
                if (zoomed) { target.Maximized = true; return; }
                target.Width = Math.Max(400, cr.Width);
                target.Height = Math.Max(300, cr.Height);
                target.X = wr.left;
                target.Y = wr.top;
                target.HasPosition = true;
                target.Maximized = false;
            });
        }
        catch { }
    }

    /// <summary>应用保存的窗口尺寸/位置（在 Show 之前调用）。</summary>
    public void ApplySavedState(WindowState state)
    {
        try
        {
            int w = Math.Max(400, state.Width), h = Math.Max(300, state.Height);
            if (state.HasPosition)
            {
                ResizeAndMove(state.X, state.Y, w, h);
                var wr = WindowRect;
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                var mon = MonitorFromWindow(Handle.Value, MONITOR_DEFAULTTONEAREST);
                // 位置落在屏幕外时回退到居中
                if (mon != IntPtr.Zero && GetMonitorInfoW(mon, ref mi))
                {
                    var work = mi.rcWork;
                    if (wr.right < work.left + 40 || wr.left > work.right - 40 ||
                        wr.bottom < work.top + 40 || wr.top > work.bottom - 40)
                    {
                        ResizeClient(w, h);
                        Center();
                    }
                }
            }
            else
            {
                ResizeClient(w, h);
                Center();
            }
        }
        catch
        {
            try { ResizeClient(1100, 800); Center(); } catch { }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var toolbarEvents = Interlocked.Exchange(ref _toolbarEvents, null);
            var toolbarWeb = Interlocked.Exchange(ref _toolbarWeb, null);
            var toolbarController = Interlocked.Exchange(ref _toolbarController, null);
            try { toolbarEvents?.Dispose(); } catch { }
            try { toolbarWeb?.Dispose(); } catch { }
            try { toolbarController?.Object.Close(); } catch { }
            try { toolbarController?.Dispose(); } catch { }
            var controllerEvents = Interlocked.Exchange(ref _controllerEvents, null);
            var events = Interlocked.Exchange(ref _events, null);
            var controller = Interlocked.Exchange(ref Controller, null);
            var web = Interlocked.Exchange(ref Web, null);
            try { controllerEvents?.Dispose(); } catch { }
            try { events?.Dispose(); } catch { }
            try { web?.Dispose(); } catch { }
            try { controller?.Object.Close(); } catch { }
            try { controller?.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}
