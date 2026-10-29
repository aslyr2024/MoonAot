using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MoonAot.Server;
using MoonAot.Services;
using MoonAot.Store;

namespace MoonAot.Win;

/// <summary>书架窗口：承载 Vue 前端，处理原生文件/文件夹导入、打开阅读器/播放器、窗口控制。</summary>
public sealed class ShelfWindow : WebViewWindowBase
{
    public static new ShelfWindow? Active { get; private set; }

    private CancellationTokenSource? _importCts;
    private long _lastProgressMilliseconds;
    private string? _pendingRoute;

    public ShelfWindow() : base("静读天下")
    {
        Active = this;
        MacroEngine.StepRecorded += OnMacroStepRecorded;
        MacroEngine.RecordingChanged += OnMacroRecordingChanged;
        Closing += (_, _) => AppWindows.CloseChildren();
    }

    protected override bool EnableDefaultContextMenu => false;
    protected override WindowState GetSavedState(AppSettings s) => s.ShelfWindow;

    protected override void OnWebViewCreated()
    {
        Navigate(MoonServer.Instance.BaseUrl + "/shelf/");
        try { MacroEngine.Attach(Handle.Value); } catch (Exception ex) { Log.Warn("宏引擎启动失败: " + ex.Message); }
    }

    /// <summary>切换到书架前端的指定路由（页面未加载完时等 ready 后补发）。</summary>
    public void NavigateRoute(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return;
        _pendingRoute = hash;
        RunScript("try{location.hash=" + JsonStr(hash) + "}catch(e){}");
    }

    // ==================== 按键精灵 ====================

    private void OnMacroStepRecorded(MacroStep step)
    {
        try
        {
            var json = JsonSerializer.Serialize(step, Info<MacroStep>.I);
            RunScript("try{window.__host&&window.__host.onMacroRecordStep&&window.__host.onMacroRecordStep(" +
                      json + ")}catch(e){}");
        }
        catch { }
    }

    private void OnMacroRecordingChanged(bool recording, string? reason, List<MacroStep> steps)
    {
        try
        {
            var json = JsonSerializer.Serialize(steps, Info<List<MacroStep>>.I);
            RunScript("try{window.__host&&window.__host.onMacroRecording&&window.__host.onMacroRecording({recording:" +
                      (recording ? "true" : "false") + ",reason:" + JsonStr(reason) + ",steps:" + json + "})}catch(e){}");
        }
        catch { }
    }

    private void SendMacroState()
    {
        RunScript("try{window.__host&&window.__host.onMacroState&&window.__host.onMacroState({recording:" +
                  (MacroEngine.IsRecording ? "true" : "false") + ",running:" +
                  (MacroEngine.IsRunning ? "true" : "false") + "})}catch(e){}");
    }

    protected override LRESULT? WindowProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (msg == MacroEngine.WmHotkey)
            {
                MacroEngine.HandleHotKey((int)wParam.Value.ToUInt64());
                return LRESULT.Null;
            }
            if (msg >= 0x8000 && MacroEngine.HandleAppMessage((int)msg)) return LRESULT.Null;
        }
        catch (Exception ex) { Log.Warn("处理宏消息失败: " + ex.Message); }
        return base.WindowProc(hwnd, msg, wParam, lParam);
    }

    protected override void OnWebMessageReceived(string message)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(message);
            var root = doc.RootElement;
            string? type = root.TryGetProperty("type", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String
                ? t.GetString() : null;
            switch (type)
            {
                case "ready":
                    if (_pendingRoute != null)
                    {
                        var r = _pendingRoute;
                        _pendingRoute = null;
                        RunScript("try{location.hash=" + JsonStr(r) + "}catch(e){}");
                    }
                    return;
                case "openBook":
                    {
                        var id = GetString(root, "bookId");
                        if (string.IsNullOrEmpty(id)) return;
                        var book = DbStore.Instance.GetBook(id);
                        if (book == null) return;
                        LibraryQuery? nav = null;
                        if (root.TryGetProperty("nav", out var nv) && nv.ValueKind == JsonValueKind.Object)
                        {
                            nav = new LibraryQuery
                            {
                                Q = GetString(nv, "q"),
                                Sort = GetString(nv, "sort") ?? "recent",
                                Desc = !(nv.TryGetProperty("desc", out var dv) && dv.ValueKind == JsonValueKind.False),
                                Filter = GetString(nv, "filter") ?? "all",
                                Format = GetString(nv, "format"),
                                Author = GetString(nv, "author"),
                                Folder = GetString(nv, "folder"),
                                Category = GetString(nv, "category"),
                            };
                        }
                        AppWindows.OpenReader(book.Id, book.Title, nav);
                        return;
                    }
                case "openViewer":
                    {
                        var kind = GetString(root, "kind");
                        var path = GetString(root, "path") ?? "";
                        var extra = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
                        if (root.TryGetProperty("extra", out var ex) && ex.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            foreach (var p in ex.EnumerateObject())
                                if (p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                                    extra[p.Name] = p.Value.GetString()!;
                        }
                        if (!string.IsNullOrEmpty(kind))
                            AppWindows.OpenViewer(kind, path, extra);
                        return;
                    }
                case "importFolder":
                    ImportFolderHost();
                    return;
                case "importFiles":
                    ImportFilesHost();
                    return;
                case "importPaths":
                    {
                        var paths = new System.Collections.Generic.List<string>();
                        if (root.TryGetProperty("paths", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var p in arr.EnumerateArray())
                                if (p.ValueKind == System.Text.Json.JsonValueKind.String) paths.Add(p.GetString()!);
                        }
                        ImportPathsHost(paths);
                        return;
                    }
                case "cancelImport":
                    CancelImport();
                    return;
                case "openTarget":
                    {
                        var path = GetString(root, "path");
                        if (!string.IsNullOrEmpty(path)) AppWindows.OpenTarget(path);
                        return;
                    }
                case "pickFolder":
                    {
                        var requestId = GetString(root, "requestId") ?? "";
                        var title = GetString(root, "title") ?? "选择文件夹";
                        string? folder = null;
                        try { folder = FolderPicker.Show(Handle.Value, title); } catch { }
                        ResolveDialog(requestId, folder);
                        return;
                    }
                case "reveal":
                    FsService.RevealPath(GetString(root, "path") ?? "");
                    return;
                case "openPath":
                    FsService.OpenPath(GetString(root, "path") ?? "");
                    return;
                case "openBrowser":
                    AppWindows.OpenBrowser(GetString(root, "url"));
                    return;
                case "openDataFolder":
                    FsService.OpenPath(DbStore.Instance.DataDir);
                    return;
                case "setTitle":
                    {
                        var title = GetString(root, "title");
                        if (!string.IsNullOrWhiteSpace(title)) Text = title;
                        return;
                    }
                case "toggleFullscreen":
                    ToggleFullscreen();
                    return;
                case "window":
                    {
                        var action = GetString(root, "action");
                        switch (action)
                        {
                            case "minimize": Show(SHOW_WINDOW_CMD.SW_MINIMIZE); return;
                            case "maximize": Show(SHOW_WINDOW_CMD.SW_MAXIMIZE); return;
                            case "restore": Show(SHOW_WINDOW_CMD.SW_RESTORE); return;
                            case "close": Close(); return;
                        }
                        return;
                    }
                case "pickFile":
                    {
                        var requestId = GetString(root, "requestId") ?? "";
                        var kind = GetString(root, "kind") ?? "any";
                        var title = GetString(root, "title");
                        string? file = null;
                        try { file = FilePicker.PickFile(Handle.Value, kind, title); } catch { }
                        ResolveDialog(requestId, file);
                        return;
                    }
                case "macroRecordStart":
                    MacroEngine.RequestStartRecording();
                    SendMacroState();
                    return;
                case "macroRecordStop":
                    MacroEngine.RequestStopRecording();
                    SendMacroState();
                    return;
                case "macroState":
                    SendMacroState();
                    return;
                case "refresh":
                    NotifyLibraryChanged();
                    return;
                case "close":
                    Close();
                    return;
            }
        }
        catch
        {
            if (string.Equals(message, "close", StringComparison.OrdinalIgnoreCase)) Close();
        }
    }

    private static string? GetString(System.Text.Json.JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

    // ==================== 原生导入 ====================

    private void ImportFilesHost()
    {
        if (_importCts != null) return;
        string[] files;
        try { files = FilePicker.PickBooks(Handle.Value); }
        catch { files = Array.Empty<string>(); }
        if (files.Length == 0) return;
        StartImport(cancelled =>
        {
            return LibraryService.Instance.ImportPaths(files, Progress, cancelled);
        });
    }

    private void ImportFolderHost()
    {
        if (_importCts != null) return;
        string? folder;
        try
        {
            folder = FolderPicker.Show(Handle.Value,
                "选择要导入的文件夹（递归扫描 TXT/EPUB/PDF/MOBI/AZW3/MD）");
        }
        catch { folder = null; }
        if (string.IsNullOrEmpty(folder)) return;
        StartImport(cancelled => LibraryService.Instance.ImportFolder(folder, Progress, cancelled));
    }

    private void ImportPathsHost(System.Collections.Generic.IEnumerable<string> paths)
    {
        if (_importCts != null) return;
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0) return;
        StartImport(cancelled => LibraryService.Instance.ImportPaths(list, Progress, cancelled));
    }

    private void StartImport(Func<CancellationToken, ImportResultDto> work)
    {
        var cts = new CancellationTokenSource();
        _importCts = cts;
        _lastProgressMilliseconds = 0;
        NotifyImportProgress(0, 0, "正在准备…");
        _ = Task.Run(() =>
        {
            ImportResultDto result;
            try
            {
                result = work(cts.Token);
            }
            catch (Exception ex)
            {
                Log.Error("导入失败", ex);
                result = new ImportResultDto { Ok = false, Error = ex.Message };
            }
            finally
            {
                Interlocked.Exchange(ref _importCts, null);
                cts.Dispose();
            }
            NotifyImportFinished(result.Ok, result.Imported, result.Skipped, result.Failed, result.Error);
            NotifyLibraryChanged();
        });
    }

    private void Progress(int done, int total, string current)
    {
        var now = Environment.TickCount64;
        if (done < total && now - Interlocked.Read(ref _lastProgressMilliseconds) < 120) return;
        Interlocked.Exchange(ref _lastProgressMilliseconds, now);
        NotifyImportProgress(done, total, current);
    }

    public void CancelImport()
    {
        try { _importCts?.Cancel(); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelImport();
            MacroEngine.StepRecorded -= OnMacroStepRecorded;
            MacroEngine.RecordingChanged -= OnMacroRecordingChanged;
            if (ReferenceEquals(Active, this))
            {
                try { MacroEngine.Detach(); } catch { }
                Active = null;
            }
        }
        base.Dispose(disposing);
    }
}
