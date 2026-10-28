using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using MoonAot.Server;
using MoonAot.Services;
using MoonAot.Store;

namespace MoonAot.Win;

/// <summary>阅读器窗口：加载 readest 静态前端，支持 F11/Esc/↑↓ 切书与侧栏钉住持久化。</summary>
public sealed class ReaderWindow : WebViewWindowBase
{
    public string BookId { get; private set; }

    private static readonly string[] Empty = Array.Empty<string>();

    public ReaderWindow(string bookId, string title) : base(string.IsNullOrWhiteSpace(title) ? "阅读" : title)
    {
        BookId = bookId;
        DbStore.Instance.UpdateProgress(bookId, -1, null);
        Closing += (_, _) => AppWindows.OnReaderClosed(this);
    }

    protected override bool InterceptEscape => true;
    protected override WindowState GetSavedState(AppSettings s) => s.ReaderWindow;

    protected override void OnWebViewCreated()
    {
        NavigateReader();
    }

    private void NavigateReader()
    {
        Navigate($"{MoonServer.Instance.BaseUrl}/reader.html?ids={Uri.EscapeDataString(BookId)}");
    }

    /// <summary>在当前窗口切换到另一本书（↑↓ 或前端请求）。</summary>
    public void OpenBook(string bookId, string title)
    {
        if (string.IsNullOrEmpty(bookId)) return;
        var book = DbStore.Instance.GetBook(bookId);
        if (book == null) return;
        BookId = bookId;
        Text = string.IsNullOrWhiteSpace(title) ? book.Title : title;
        DbStore.Instance.UpdateProgress(bookId, -1, null);
        AppWindows.CloseOtherReader(this, bookId);
        NavigateReader();
    }

    protected override void OnEscape() => Close();

    protected override bool HandleKey(int vk)
    {
        if (base.HandleKey(vk)) return true;
        if (vk is 0x26 or 0x28) // ↑ ↓
        {
            if (HasModifier()) return false;
            SwitchBook(vk == 0x26 ? -1 : 1);
            return true;
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private static bool HasModifier()
    {
        try
        {
            return (GetKeyState(0x11) & 0x8000) != 0 || // Ctrl
                   (GetKeyState(0x12) & 0x8000) != 0 || // Alt
                   (GetKeyState(0x10) & 0x8000) != 0;   // Shift
        }
        catch { return false; }
    }

    private void SwitchBook(int dir)
    {
        try
        {
            var ids = DbStore.Instance.TitleOrder();
            if (ids.Length == 0) return;
            int idx = Array.IndexOf(ids, BookId);
            if (idx < 0) return;
            int next = idx + dir;
            if (next < 0 || next >= ids.Length) return;
            var book = DbStore.Instance.GetBook(ids[next]);
            if (book == null) return;
            OpenBook(book.Id, book.Title);
        }
        catch { }
    }

    protected override void OnWebMessageReceived(string message)
    {
        try
        {
            if (string.Equals(message, "close", StringComparison.OrdinalIgnoreCase))
            {
                Close();
                return;
            }
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            string? type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            switch (type)
            {
                case "close":
                    Close();
                    return;
                case "toggleShelf":
                    AppWindows.EnsureShelf();
                    return;
                case "prevBook":
                    SwitchBook(-1);
                    return;
                case "nextBook":
                    SwitchBook(1);
                    return;
                case "progress":
                    {
                        var id = root.TryGetProperty("bookId", out var bid) && bid.ValueKind == JsonValueKind.String
                            ? bid.GetString() : BookId;
                        if (id != null && root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)
                        {
                            var value = v.GetDouble();
                            if (value is >= 0 and <= 1) DbStore.Instance.UpdateProgress(id, value, null);
                        }
                        return;
                    }
                case "sidebarPin":
                    {
                        bool pinned = root.TryGetProperty("pinned", out var p) && p.ValueKind == JsonValueKind.True;
                        DbStore.Instance.UpdateAppSettings(s =>
                            s.ReaderSettingsJson = SettingsJson.SetBool(s.ReaderSettingsJson,
                                "globalReadSettings", "isSideBarPinned", pinned));
                        return;
                    }
            }
        }
        catch
        {
            if (string.Equals(message, "close", StringComparison.OrdinalIgnoreCase)) Close();
        }
    }
}
