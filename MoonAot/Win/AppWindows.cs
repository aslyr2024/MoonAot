using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonAot.Server;
using MoonAot.Services;
using MoonAot.Store;

namespace MoonAot.Win;

/// <summary>窗口管理：书架、阅读器、媒体窗口。所有方法均在 UI 线程调用（消息回调/启动流程）。</summary>
public static class AppWindows
{
    private static readonly object Lock = new();
    private static readonly List<ReaderWindow> Readers = new();
    private static MediaViewerWindow? Viewer;
    private static BrowserWindow? BrowserWin;
    private static bool _closing;

    /// <summary>可由内置浏览器直接渲染的文件类型。</summary>
    private static readonly HashSet<string> BrowserExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml", ".mht", ".mhtml",
    };

    public static void EnsureShelf()
    {
        lock (Lock)
        {
            var shelf = ShelfWindow.Active;
            if (shelf != null && !shelf.IsDisposed)
            {
                try { shelf.SetForeground(); } catch { }
                return;
            }
            CreateShelf();
        }
    }

    private static void CreateShelf()
    {
        var state = DbStore.Instance.AppSettings.ShelfWindow;
        var win = new ShelfWindow();
        win.ApplySavedState(state);
        ShowWindow(win, state);
    }

    private static void ShowWindow(Window win, WindowState state)
    {
        try
        {
            if (state.Maximized) win.Show(SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED);
            else win.Show();
            win.SetForeground();
        }
        catch (Exception ex) { Log.Error("显示窗口失败", ex); }
    }

    public static void OpenReader(string bookId, string title)
    {
        lock (Lock)
        {
            if (_closing) return;
            var existing = Readers.FirstOrDefault(r => r.BookId == bookId && !r.IsDisposed);
            if (existing != null)
            {
                try { existing.SetForeground(); } catch { }
                return;
            }
            Readers.RemoveAll(r => r.IsDisposed);
            var state = DbStore.Instance.AppSettings.ReaderWindow;
            var win = new ReaderWindow(bookId, title);
            win.ApplySavedState(state);
            ShowWindow(win, state);
            Readers.Add(win);
        }
    }

    /// <summary>↑↓ 切书时关闭目标书的其他窗口，避免同一本书多开。</summary>
    public static void CloseOtherReader(ReaderWindow except, string bookId)
    {
        lock (Lock)
        {
            foreach (var r in Readers.Where(r => !ReferenceEquals(r, except) && r.BookId == bookId).ToList())
            {
                try { r.Close(); } catch { }
            }
        }
    }

    public static void OnReaderClosed(ReaderWindow win)
    {
        lock (Lock)
        {
            Readers.Remove(win);
            if (!_closing && Readers.Count == 0)
            {
                try { ShelfWindow.Active?.SetForeground(); } catch { }
            }
        }
    }

    public static void OpenViewer(string kind, string path, System.Collections.Generic.Dictionary<string, string>? extra = null)
    {
        if (string.IsNullOrEmpty(kind)) return;
        lock (Lock)
        {
            if (_closing) return;
            if (Viewer != null && !Viewer.IsDisposed)
            {
                Viewer.NavigateTo(kind, path, extra);
                try { Viewer.SetForeground(); } catch { }
                try { Viewer.Show(); } catch { }
                return;
            }
            var state = DbStore.Instance.AppSettings.ViewWindow;
            var win = new MediaViewerWindow(kind, path, extra);
            win.ApplySavedState(state);
            ShowWindow(win, state);
            Viewer = win;
        }
    }

    public static void OnViewerClosed(MediaViewerWindow win)
    {
        lock (Lock)
        {
            if (ReferenceEquals(Viewer, win)) Viewer = null;
        }
    }

    /// <summary>打开内置浏览器；已有窗口时导航到新地址。</summary>
    public static void OpenBrowser(string? url = null)
    {
        lock (Lock)
        {
            if (_closing) return;
            if (BrowserWin != null && !BrowserWin.IsDisposed)
            {
                if (!string.IsNullOrWhiteSpace(url)) BrowserWin.NavigateTo(url);
                try { BrowserWin.SetForeground(); } catch { }
                try { BrowserWin.Show(); } catch { }
                return;
            }
            var state = DbStore.Instance.AppSettings.BrowserWindow;
            var win = new BrowserWindow(url);
            win.ApplySavedState(state);
            ShowWindow(win, state);
            BrowserWin = win;
        }
    }

    public static void OnBrowserClosed(BrowserWindow win)
    {
        lock (Lock)
        {
            if (ReferenceEquals(BrowserWin, win)) BrowserWin = null;
        }
    }

    /// <summary>书架关闭时关闭所有子窗口，让应用退出。</summary>
    public static void CloseChildren()
    {
        lock (Lock)
        {
            _closing = true;
            foreach (var r in Readers.ToList()) { try { r.Close(); } catch { } }
            Readers.Clear();
            if (Viewer != null) { try { Viewer.Close(); } catch { } Viewer = null; }
            if (BrowserWin != null) { try { BrowserWin.Close(); } catch { } BrowserWin = null; }
            _closing = false;
        }
    }

    public static void CloseAll()
    {
        lock (Lock)
        {
            _closing = true;
            foreach (var r in Readers.ToList()) { try { r.Close(); } catch { } }
            Readers.Clear();
            if (Viewer != null) { try { Viewer.Close(); } catch { } Viewer = null; }
            if (BrowserWin != null) { try { BrowserWin.Close(); } catch { } BrowserWin = null; }
            var shelf = ShelfWindow.Active;
            if (shelf != null && !shelf.IsDisposed) { try { shelf.Close(); } catch { } }
            _closing = false;
        }
    }

    public static void NotifyLibraryChanged()
    {
        try { ShelfWindow.Active?.NotifyLibraryChanged(); } catch { }
    }

    /// <summary>从命令行/资源管理器打开一个路径。</summary>
    public static void OpenTarget(string path)
    {
        try
        {
            if (Directory.Exists(path)) { FsService.OpenPath(path); return; }
            if (!File.Exists(path)) return;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (BookFormatEx.IsBookExt(ext))
            {
                var book = LibraryService.Instance.ImportFile(path, out var err);
                if (book != null) OpenReader(book.Id, book.Title);
                else Log.Warn("导入失败: " + err);
                return;
            }
            var kind = FileKinds.KindOf(path);
            if (kind is "audio" or "video" or "image" or "zip")
            {
                OpenViewer(kind, path);
                return;
            }
            if (kind == "zim")
            {
                try
                {
                    Services.Zim.ZimService.Instance.Open(path);
                    EnsureShelf();
                    ShelfWindow.Active?.NavigateRoute("#/wiki");
                }
                catch (Exception ex)
                {
                    Log.Warn("打开 ZIM 失败: " + ex.Message);
                    FsService.OpenPath(path);
                }
                return;
            }
            if (kind is "code" or "doc" or "sheet" or "slide" or "model" or "cad")
            {
                OpenViewer(kind, path);
                return;
            }
            if (BrowserExts.Contains(ext))
            {
                OpenBrowser(new Uri(path).AbsoluteUri);
                return;
            }
            FsService.OpenPath(path);
        }
        catch (Exception ex) { Log.Error("打开命令行目标失败", ex); }
    }
}

/// <summary>应用启动与退出。</summary>
public static class AppHost
{
    public static void Run(string? target = null)
    {
        MoonServer.Instance.Start();
        LibraryService.Instance.Changed += AppWindows.NotifyLibraryChanged;
        MoonServer.Instance.LibraryChanged += AppWindows.NotifyLibraryChanged;
        LibraryService.Instance.StartCoverHousekeeping(() =>
            LibraryService.Instance.StartMissingCoverBackfillIfNeeded());

        var app = new Application();
        try
        {
            AppWindows.EnsureShelf();
            if (!string.IsNullOrEmpty(target)) AppWindows.OpenTarget(target);
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Error("应用运行失败", ex);
        }
        finally
        {
            AppWindows.CloseAll();
            MoonServer.Instance.Stop();
            DbStore.Instance.FlushAll();
            MediaStore.Instance.FlushAll();
            try { app.Dispose(); } catch { }
        }
    }
}
