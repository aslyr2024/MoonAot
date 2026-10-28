using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenCC.NET;
using MoonAot.Services.Zim;

namespace MoonAot.Server;

/// <summary>维基百科（离线 ZIM）页面与 API。</summary>
public sealed partial class MoonServer
{
    private static readonly OpenChineseConverter ZimConverter = new();

    private void ServeWikiAsset(HttpListenerContext ctx)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "wiki", "index.html");
        if (!File.Exists(file)) { SendText(ctx, 404, "wiki asset not found"); return; }
        ServeFile(ctx, file, "no-cache");
    }

    private void HandleZimApi(HttpListenerContext ctx, string action, string query)
    {
        action = (action ?? "").Trim('/');
        var zim = ZimService.Instance.Current;
        switch (action)
        {
            case "info":
                {
                    if (zim == null)
                    {
                        SendJsonString(ctx, 200, "{\"open\":false,\"recent\":" +
                            JsonArray(ZimService.Instance.GetRecent()) + "}");
                        return;
                    }
                    var main = zim.GetMainPage();
                    var sb = new StringBuilder(1024);
                    sb.Append("{\"open\":true,\"file\":").Append(JsonStr(zim.FilePath))
                      .Append(",\"fileName\":").Append(JsonStr(Path.GetFileName(zim.FilePath)))
                      .Append(",\"name\":").Append(JsonStr(FirstNonEmpty(zim.GetMetadata("Title"), Path.GetFileNameWithoutExtension(zim.FilePath))))
                      .Append(",\"description\":").Append(JsonStr(zim.GetMetadata("Description")))
                      .Append(",\"language\":").Append(JsonStr(zim.GetMetadata("Language")))
                      .Append(",\"date\":").Append(JsonStr(zim.GetMetadata("Date")))
                      .Append(",\"creator\":").Append(JsonStr(zim.GetMetadata("Creator")))
                      .Append(",\"entryCount\":").Append(zim.EntryCount)
                      .Append(",\"clusterCount\":").Append(zim.ClusterCount)
                      .Append(",\"version\":").Append(JsonStr($"{zim.MajorVersion}.{zim.MinorVersion}"))
                      .Append(",\"mainPath\":").Append(JsonStr(main?.Url ?? ""))
                      .Append(",\"recent\":").Append(JsonArray(ZimService.Instance.GetRecent()))
                      .Append('}');
                    SendJsonString(ctx, 200, sb.ToString());
                    return;
                }
            case "open":
                {
                    var path = QueryValue(query, "path");
                    if (string.IsNullOrWhiteSpace(path) && ctx.Request.HttpMethod == "POST")
                    {
                        using var body = ReadJson(ctx.Request);
                        path = body == null ? null : GetString(body.RootElement, "path");
                    }
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        SendJsonString(ctx, 200, "{\"ok\":false,\"error\":\"文件不存在\"}");
                        return;
                    }
                    try
                    {
                        var z = ZimService.Instance.Open(path!);
                        var main = z.GetMainPage();
                        SendJsonString(ctx, 200, "{\"ok\":true,\"mainPath\":" + JsonStr(main?.Url ?? "") + "}");
                    }
                    catch (Exception ex)
                    {
                        SendJsonString(ctx, 200, "{\"ok\":false,\"error\":" + JsonStr(ex.Message) + "}");
                    }
                    return;
                }
            case "close":
                ZimService.Instance.Close();
                SendJsonString(ctx, 200, "{\"ok\":true}");
                return;
            case "search":
                {
                    var q = QueryValue(query, "q");
                    var limit = Math.Clamp(QueryInt(query, "limit", 25), 1, 100);
                    if (zim == null || string.IsNullOrWhiteSpace(q))
                    {
                        SendJsonString(ctx, 200, "[]");
                        return;
                    }
                    var sb = new StringBuilder("[");
                    var hits = zim.SearchByTitlePrefix(q!.Trim(), limit);
                    for (int i = 0; i < hits.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        var e = hits[i];
                        sb.Append("{\"title\":").Append(JsonStr(string.IsNullOrEmpty(e.Title) ? e.Url : e.Title))
                          .Append(",\"path\":").Append(JsonStr(e.Url))
                          .Append(",\"redirect\":").Append(e.IsRedirect ? "true" : "false").Append('}');
                    }
                    sb.Append(']');
                    SendJsonString(ctx, 200, sb.ToString());
                    return;
                }
            case "random":
                {
                    var e = zim?.GetRandomArticle();
                    SendJsonString(ctx, 200, "{\"path\":" + JsonStr(e?.Url ?? "") +
                                              ",\"title\":" + JsonStr(e == null ? "" : (string.IsNullOrEmpty(e.Title) ? e.Url : e.Title)) + "}");
                    return;
                }
            case "convert":
                {
                    using var body = ReadJson(ctx.Request);
                    var texts = new List<string>();
                    var dir = "t2s";
                    if (body != null)
                    {
                        var root = body.RootElement;
                        if (root.TryGetProperty("dir", out var d) && d.ValueKind == JsonValueKind.String)
                            dir = d.GetString() ?? "t2s";
                        if (root.TryGetProperty("texts", out var arr) && arr.ValueKind == JsonValueKind.Array)
                            foreach (var t in arr.EnumerateArray())
                                if (t.ValueKind == JsonValueKind.String) texts.Add(t.GetString() ?? "");
                    }
                    var sb = new StringBuilder("{\"texts\":[");
                    for (int i = 0; i < texts.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        string converted;
                        try
                        {
                            converted = dir == "s2t"
                                ? ZimConverter.ToTraditionalFromSimplified(texts[i])
                                : ZimConverter.ToSimplifiedFromTraditional(texts[i]);
                        }
                        catch { converted = texts[i]; }
                        sb.Append(JsonStr(converted));
                    }
                    sb.Append("]}");
                    SendJsonString(ctx, 200, sb.ToString());
                    return;
                }
        }
        SendText(ctx, 404, "unknown zim api");
    }

    private void HandleZimContent(HttpListenerContext ctx, string tail)
    {
        var zim = ZimService.Instance.Current;
        if (zim == null) { SendText(ctx, 503, "尚未打开 ZIM 文件"); return; }
        if (string.IsNullOrEmpty(tail))
        {
            var mp = zim.GetMainPage();
            if (mp == null) { SendText(ctx, 404, "no main page"); return; }
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = "/zim/" + Uri.EscapeDataString(mp.Url);
            ctx.Response.OutputStream.Close();
            return;
        }
        var key = Uri.UnescapeDataString(tail.Split('#')[0]);
        var entry = zim.FindByPath(key);
        if (entry == null)
            entry = zim.FindByPath(key.Replace(' ', '_')) ?? zim.FindByPath(key.Replace('_', ' '));
        if (entry == null) { SendZimNotFound(ctx, key); return; }
        if (entry.IsRedirect)
        {
            var target = zim.GetEntry(entry.RedirectIndex);
            if (target == null) { SendZimNotFound(ctx, key); return; }
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = "/zim/" + Uri.EscapeDataString(target.Url);
            ctx.Response.OutputStream.Close();
            return;
        }
        var data = zim.ReadContent(entry);
        if (data == null) { SendZimNotFound(ctx, key); return; }
        var mime = zim.GetMime(entry.MimeIndex);
        if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !mime.Contains("charset", StringComparison.OrdinalIgnoreCase))
            mime += "; charset=utf-8";
        WriteBody(ctx, 200, data, mime, false, null, "public, max-age=86400");
    }

    private void SendZimNotFound(HttpListenerContext ctx, string key)
    {
        var html = "<!DOCTYPE html><meta charset=\"utf-8\"><style>body{font:15px/1.7 system-ui,'Microsoft YaHei';padding:48px;color:#444}code{background:#f2f2f5;padding:2px 6px;border-radius:4px}</style>" +
                   "<h2>条目不存在</h2><p>ZIM 中找不到：<code>" +
                   WebUtility.HtmlEncode(key) + "</code></p>";
        SendHtml(ctx, 404, html);
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}
