using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using MoonAot.Services;
using MoonAot.Store;
using MoonAot.TinySearch;
using MoonAot.Win;

namespace MoonAot.Server;

/// <summary>全盘搜索（TinyEverything 核心）的静态资源与 API。</summary>
public sealed partial class MoonServer
{
    private static readonly byte[] TransparentPng =
    {
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
        0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,
        0x89,0x00,0x00,0x00,0x0A,0x49,0x44,0x41,0x54,0x78,0x9C,0x63,0x00,0x01,0x00,0x00,
        0x05,0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,
        0x42,0x60,0x82,
    };

    private void ServeTinyAsset(HttpListenerContext ctx, string rel)
    {
        try
        {
            rel = (rel ?? "").Replace('\\', '/').TrimStart('/');
            if (rel.Length == 0 || rel.EndsWith('/')) rel += "index.html";
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Assets", "tiny"));
            var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                SendText(ctx, 404, "tiny asset not found");
                return;
            }
            ServeFile(ctx, full, "no-cache");
        }
        catch { SendText(ctx, 404, "tiny asset error"); }
    }

    private void HandleTinyApi(HttpListenerContext ctx, string action, string query)
    {
        switch (action)
        {
            case "search":
                {
                    TinySearchService.EnsureStarted();
                    var q = QueryValue(query, "q");
                    var sort = QueryValue(query, "sort");
                    var dir = QueryValue(query, "dir");
                    int offset = QueryInt(query, "offset", 0);
                    int limit = Math.Clamp(QueryInt(query, "limit", 200), 1, 1000);
                    var result = TinySearchService.Search(q, sort,
                        !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase), offset, limit);
                    if (result == null) { SendText(ctx, 503, "index not ready"); return; }
                    var sb = new StringBuilder(4096);
                    sb.Append("{\"total\":").Append(result.Total)
                      .Append(",\"elapsedMs\":").Append(result.ElapsedMs)
                      .Append(",\"version\":").Append(TinySearchService.Version)
                      .Append(",\"items\":[");
                    for (int i = 0; i < result.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        var e = result.Items[i];
                        sb.Append("{\"name\":").Append(JsonStr(e.Name))
                          .Append(",\"path\":").Append(JsonStr(e.FullPath))
                          .Append(",\"size\":").Append(e.Size)
                          .Append(",\"ext\":").Append(JsonStr(e.Extension))
                          .Append(",\"isDir\":").Append(e.IsDirectory ? "true" : "false")
                          .Append(",\"mtime\":").Append(e.ModifiedUtc).Append('}');
                    }
                    sb.Append("]}");
                    SendJsonString(ctx, 200, sb.ToString());
                    return;
                }
            case "status":
                {
                    TinySearchService.EnsureStarted();
                    var sb = new StringBuilder(1024);
                    sb.Append("{\"version\":").Append(TinySearchService.Version)
                      .Append(",\"total\":").Append(TinySearchService.Total)
                      .Append(",\"drives\":[");
                    bool first = true;
                    foreach (var d in TinySearchService.Status.Values.OrderBy(x => x.Letter))
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"letter\":").Append(JsonStr(d.Letter))
                          .Append(",\"mode\":").Append(JsonStr(d.Mode))
                          .Append(",\"state\":").Append(JsonStr(d.State))
                          .Append(",\"count\":").Append(d.Count)
                          .Append(",\"message\":").Append(JsonStr(d.Message)).Append('}');
                    }
                    sb.Append("]}");
                    SendJsonString(ctx, 200, sb.ToString());
                    return;
                }
            case "drives":
                {
                    var sb = new StringBuilder(512);
                    sb.Append('[');
                    bool first = true;
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                        }
                        catch { continue; }
                        if (!first) sb.Append(',');
                        first = false;
                        string label = "", format = "";
                        try { label = d.VolumeLabel; } catch { }
                        try { format = d.DriveFormat; } catch { }
                        sb.Append("{\"letter\":").Append(JsonStr(d.Name.TrimEnd('\\', ':').ToUpperInvariant()))
                          .Append(",\"label\":").Append(JsonStr(label))
                          .Append(",\"format\":").Append(JsonStr(format))
                          .Append(",\"removable\":").Append(d.DriveType == DriveType.Removable ? "true" : "false").Append('}');
                    }
                    sb.Append(']');
                    SendJsonString(ctx, 200, sb.ToString());
                    return;
                }
            case "settings":
                {
                    if (ctx.Request.HttpMethod == "POST")
                    {
                        using var body = ReadJson(ctx.Request);
                        var s = new MoonAot.TinySearch.Storage.AppSettings();
                        if (body != null)
                        {
                            var root = body.RootElement;
                            s.ExcludedFolders = ReadStringList(root, "excludedFolders") ?? s.ExcludedFolders;
                            s.IncludedFolders = ReadStringList(root, "includedFolders") ?? s.IncludedFolders;
                            s.EnabledDrives = ReadStringList(root, "enabledDrives") ?? s.EnabledDrives;
                            if (root.TryGetProperty("rescanIntervalHours", out var h) && h.TryGetInt32(out var hv))
                                s.RescanIntervalHours = hv;
                        }
                        TinySearchService.UpdateSettings(s);
                        SendJson(ctx, 200, new OkDto { Ok = true });
                        return;
                    }
                    var st = TinySearchService.Settings;
                    var sb = new StringBuilder(512);
                    sb.Append("{\"excludedFolders\":").Append(JsonArray(st.ExcludedFolders))
                      .Append(",\"includedFolders\":").Append(JsonArray(st.IncludedFolders))
                      .Append(",\"enabledDrives\":").Append(JsonArray(st.EnabledDrives))
                      .Append(",\"rescanIntervalHours\":").Append(st.RescanIntervalHours).Append('}');
                    SendJsonString(ctx, 200, sb.ToString());
                    return;
                }
            case "rescan":
                {
                    using var body = ReadJson(ctx.Request);
                    var drive = body == null ? null : GetString(body.RootElement, "drive");
                    if (string.IsNullOrWhiteSpace(drive)) TinySearchService.RescanAll();
                    else TinySearchService.Rescan(drive!);
                    SendJson(ctx, 200, new OkDto { Ok = true });
                    return;
                }
            case "open":
                {
                    using var body = ReadJson(ctx.Request);
                    var path = body == null ? null : GetString(body.RootElement, "path");
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        SendJson(ctx, 400, new OkDto { Ok = false, Error = "path required" });
                        return;
                    }
                    try
                    {
                        var shelf = ShelfWindow.Active;
                        if (shelf != null)
                            shelf.RunScript("try{window.__host&&window.__host.openTarget(" + JsonStr(path) + ")}catch(e){}");
                        else Process.Start(new ProcessStartInfo(path!) { UseShellExecute = true });
                        SendJson(ctx, 200, new OkDto { Ok = true });
                    }
                    catch (Exception ex) { SendJson(ctx, 400, new OkDto { Ok = false, Error = ex.Message }); }
                    return;
                }
            case "reveal":
                {
                    using var body = ReadJson(ctx.Request);
                    var path = body == null ? null : GetString(body.RootElement, "path");
                    var ok = !string.IsNullOrEmpty(path) && FsService.RevealPath(path!);
                    SendJson(ctx, ok ? 200 : 400, new OkDto { Ok = ok });
                    return;
                }
            case "openall":
                {
                    using var body = ReadJson(ctx.Request);
                    var q = body == null ? null : GetString(body.RootElement, "q");
                    var sort = body == null ? null : GetString(body.RootElement, "sort");
                    var dir = body == null ? null : GetString(body.RootElement, "dir");
                    int audio = 0, video = 0, image = 0;
                    string? firstMusic = null, firstVideo = null, firstImage = null;
                    try
                    {
                        int n = 0;
                        foreach (var e in TinySearchService.Enumerate(q, sort,
                                     !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (e.IsDirectory) continue;
                            switch (FileKinds.KindOf(e.FullPath))
                            {
                                case "audio": audio++; firstMusic ??= e.FullPath; break;
                                case "video": video++; firstVideo ??= e.FullPath; break;
                                case "image": image++; firstImage ??= e.FullPath; break;
                            }
                            if (++n > 10000) break;
                        }
                    }
                    catch { }
                    var shelf = ShelfWindow.Active;
                    void OpenViewer(string kind, string? p)
                    {
                        if (string.IsNullOrEmpty(p) || shelf == null) return;
                        shelf.RunScript("try{window.__host&&window.__host.openViewer(" +
                                        JsonStr(kind) + "," + JsonStr(p) + ")}catch(e){}");
                    }
                    OpenViewer("music", firstMusic);
                    OpenViewer("video", firstVideo);
                    OpenViewer("image", firstImage);
                    SendJsonString(ctx, 200,
                        "{\"ok\":true,\"audio\":" + audio + ",\"video\":" + video + ",\"image\":" + image + "}");
                    return;
                }
            case "icon":
                WriteBody(ctx, 200, TransparentPng, "image/png", false, null, "public, max-age=86400");
                return;
            default:
                SendText(ctx, 404, "unknown tiny api");
                return;
        }
    }

    private static List<string>? ReadStringList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var e in arr.EnumerateArray())
            if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString() ?? "");
        return list;
    }

    private static string JsonArray(List<string>? items)
    {
        var sb = new StringBuilder("[");
        if (items != null)
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(JsonStr(items[i]));
            }
        return sb.Append(']').ToString();
    }
}
