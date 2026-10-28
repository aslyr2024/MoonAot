using System;
using System.IO;
using System.Text.Json;
using MoonAot.Server;
using MoonAot.Services;
using MoonAot.Store;

namespace MoonAot.Win;

/// <summary>媒体窗口：图片浏览器 / 音乐 / 视频 / 压缩包，统一加载 Vue 前端的内嵌播放器路由。</summary>
public sealed class MediaViewerWindow : WebViewWindowBase
{
    public string Kind { get; private set; }
    public string CurrentPath { get; private set; }

    private System.Collections.Generic.Dictionary<string, string> _extra = new(StringComparer.Ordinal);

    public MediaViewerWindow(string kind, string path, System.Collections.Generic.Dictionary<string, string>? extra = null)
        : base(ViewerTitle(kind, path))
    {
        Kind = kind;
        CurrentPath = path ?? "";
        if (extra != null) _extra = new System.Collections.Generic.Dictionary<string, string>(extra, StringComparer.Ordinal);
        Closing += (_, _) => AppWindows.OnViewerClosed(this);
    }

    private static string ViewerTitle(string kind, string? path)
    {
        if (!string.IsNullOrEmpty(path)) return Path.GetFileName(path);
        return kind switch
        {
            "image" => "图片浏览器",
            "music" or "audio" => "音乐播放器",
            "video" => "视频播放器",
            "zip" => "压缩包浏览",
            _ => "播放器",
        };
    }

    protected override bool InterceptEscape => true;
    protected override bool EnableDefaultContextMenu => false;
    protected override WindowState GetSavedState(AppSettings s) => s.ViewWindow;

    protected override void OnWebViewCreated()
    {
        NavigateViewer();
    }

    public void NavigateTo(string kind, string path, System.Collections.Generic.Dictionary<string, string>? extra = null)
    {
        if (string.IsNullOrEmpty(kind)) return;
        Kind = kind;
        CurrentPath = path ?? "";
        _extra = extra != null
            ? new System.Collections.Generic.Dictionary<string, string>(extra, StringComparer.Ordinal)
            : new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        try { Text = ViewerTitle(kind, CurrentPath); } catch { }
        NavigateViewer();
    }

    private void NavigateViewer()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(MoonServer.Instance.BaseUrl).Append("/shelf/#/view/").Append(Uri.EscapeDataString(Kind));
        bool hasQuery = false;
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            sb.Append("?path=").Append(Uri.EscapeDataString(CurrentPath));
            hasQuery = true;
        }
        foreach (var (k, v) in _extra)
        {
            sb.Append(hasQuery ? '&' : '?').Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v));
            hasQuery = true;
        }
        Navigate(sb.ToString());
    }

    protected override void OnEscape() => Close();

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
                case "setTitle":
                    if (root.TryGetProperty("title", out var ti) && ti.ValueKind == JsonValueKind.String)
                    {
                        var title = ti.GetString();
                        if (!string.IsNullOrWhiteSpace(title)) Text = title;
                    }
                    return;
                case "navigate":
                    {
                        var kind = root.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                            ? k.GetString() : null;
                        var path = root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
                            ? p.GetString() : null;
                        if (!string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(path)) NavigateTo(kind, path);
                        return;
                    }
                case "pickFolder":
                    {
                        var requestId = root.TryGetProperty("requestId", out var ri) && ri.ValueKind == JsonValueKind.String
                            ? ri.GetString() ?? "" : "";
                        var title = root.TryGetProperty("title", out var ti0) && ti0.ValueKind == JsonValueKind.String
                            ? ti0.GetString() : null;
                        string? folder = null;
                        try { folder = FolderPicker.Show(Handle.Value, title ?? "选择文件夹"); } catch { }
                        ResolveDialog(requestId, folder);
                        return;
                    }
                case "pickFile":
                    {
                        var requestId = root.TryGetProperty("requestId", out var ri) && ri.ValueKind == JsonValueKind.String
                            ? ri.GetString() ?? "" : "";
                        var kind = root.TryGetProperty("kind", out var ki) && ki.ValueKind == JsonValueKind.String
                            ? ki.GetString() ?? "any" : "any";
                        var title = root.TryGetProperty("title", out var ti2) && ti2.ValueKind == JsonValueKind.String
                            ? ti2.GetString() : null;
                        string? file = null;
                        try { file = FilePicker.PickFile(Handle.Value, kind, title); } catch { }
                        ResolveDialog(requestId, file);
                        return;
                    }
                case "toggleFullscreen":
                    ToggleFullscreen();
                    return;
                case "reveal":
                    {
                        var p = root.TryGetProperty("path", out var pv) && pv.ValueKind == JsonValueKind.String ? pv.GetString() : null;
                        if (!string.IsNullOrEmpty(p)) FsService.RevealPath(p);
                        return;
                    }
                case "openPath":
                    {
                        var p = root.TryGetProperty("path", out var pv) && pv.ValueKind == JsonValueKind.String ? pv.GetString() : null;
                        if (!string.IsNullOrEmpty(p)) FsService.OpenPath(p);
                        return;
                    }
                case "openTarget":
                    {
                        var p = root.TryGetProperty("path", out var pv) && pv.ValueKind == JsonValueKind.String ? pv.GetString() : null;
                        if (!string.IsNullOrEmpty(p)) AppWindows.OpenTarget(p);
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
