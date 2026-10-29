using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MoonAot.Services;
using MoonAot.Store;

namespace MoonAot.Server;

/// <summary>
/// 内嵌本地 HTTP 服务器（HttpListener，127.0.0.1 随机端口，免管理员，NativeAOT 友好）。
/// 为书架 Vue 前端、readest 阅读器与媒体播放器提供：
///   /shelf/*            书架前端（Vue 构建产物）
///   /reader.html        阅读器（readest 静态构建）
///   /book/{id}          原始书籍（支持 Range）
///   /txt/{id}           TXT/MD 转 UTF-8
///   /thumb/{id}         封面缩略图（解决大量书籍卡顿）
///   /media /image       音视频/图片流（Range）
///   /api/*              书库、笔记、设置、导入、媒体、压缩包、任务等 JSON API
/// </summary>
public sealed partial class MoonServer
{
    public static MoonServer Instance { get; } = new();

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public DateTime StartedAt { get; } = DateTime.Now;

    /// <summary>书库数据变化（导入/进度保存），宿主据此刷新书架。</summary>
    public event Action? LibraryChanged;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private long _lastNotifyTicks;

    private readonly ConcurrentDictionary<string, UploadSession> _uploads = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (long Ticks, string Text)> _txtCache = new(StringComparer.Ordinal);
    private static List<string>? _fontCache;
    private static string? _webViewVersion;

    private sealed class UploadSession
    {
        public string TempFile = "";
        public long Expected;
        public long Received;
        public string Name = "";
        public DateTime Created = DateTime.Now;
        public readonly object Gate = new();
    }

    private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "Assets");
    private static string NnReaderDir => Path.Combine(AssetsDir, "nnreader");
    private static string ShelfDir => Path.Combine(AssetsDir, "shelf");
    private static string ReaderAssetsDir => Path.Combine(AssetsDir, "reader");
    private static string VendorDir => Path.Combine(AssetsDir, "vendor");
    private static string UploadDir => Path.Combine(DbStore.Instance.DataDir, "uploads");

    // ==================== 生命周期 ====================

    public void Start(int port = 0)
    {
        if (_listener != null) return;
        try
        {
            Directory.CreateDirectory(UploadDir);
            _listener = new HttpListener();
            var actualPort = port > 0 ? port : GetFreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{actualPort}/");
            _listener.Start();
            Port = actualPort;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                    catch { return; }
                    _ = Task.Run(() =>
                    {
                        try { Handle(ctx); }
                        catch (Exception ex)
                        {
                            Log.Error("请求处理异常", ex);
                            TryError(ctx, 500, ex.Message);
                        }
                    }, CancellationToken.None);
                }
            }, CancellationToken.None);
            Log.Info($"HTTP 服务已启动: {BaseUrl}");
            WriteDevUrl();
        }
        catch (Exception ex)
        {
            Log.Error("启动 HTTP 服务失败（端口 " + port + "）", ex);
            _listener = null;
            throw;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
        _cts = null;
    }

    private void WriteDevUrl()
    {
        try
        {
            var path = Path.Combine(DbStore.Instance.DataDir, "dev-url.txt");
            File.WriteAllText(path, BaseUrl + Environment.NewLine + DateTime.Now.ToString("s"));
        }
        catch { }
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((System.Net.IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }

    public void NotifyChangedThrottled()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastNotifyTicks);
        if (now - last < 1500) return;
        if (Interlocked.CompareExchange(ref _lastNotifyTicks, now, last) != last) return;
        try { LibraryChanged?.Invoke(); } catch { }
    }

    // ==================== 路由 ====================

    private void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string rawPath = req.Url?.AbsolutePath ?? "/";
        string path;
        try { path = Uri.UnescapeDataString(rawPath).Replace('\\', '/'); }
        catch { path = rawPath; }
        string query = req.Url?.Query ?? "";

        try
        {
            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Headers["Access-Control-Allow-Origin"] = "*";
                res.Headers["Access-Control-Allow-Methods"] = "GET,POST,PUT,DELETE,OPTIONS";
                res.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                res.OutputStream.Close();
                return;
            }
            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                HandleApi(ctx, path, query);
                return;
            }
            if (path.StartsWith("/tiny/api/", StringComparison.Ordinal))
            {
                HandleTinyApi(ctx, path["/tiny/api/".Length..], query);
                return;
            }
            if (path.StartsWith("/zimapi/", StringComparison.Ordinal))
            {
                HandleZimApi(ctx, path["/zimapi/".Length..], query);
                return;
            }
            if (req.HttpMethod != "GET" && req.HttpMethod != "HEAD")
            {
                SendText(ctx, 405, "method not allowed");
                return;
            }
            HandleGet(ctx, path, query);
        }
        finally
        {
            try { req.InputStream?.Close(); } catch { }
        }
    }

    private void HandleGet(HttpListenerContext ctx, string path, string query)
    {
        var req = ctx.Request;
        if (path == "/") { Redirect(ctx, BaseUrl + "/shelf/"); return; }
        if (path == "/health") { SendText(ctx, 200, "ok"); return; }
        if (path == "/empty-font.css")
        {
            // 阅读器自带的远程字体样式（Google Fonts / jsDelivr / imagekit）在受限网络会长时间阻塞，
            // 统一替换成本地空样式，字体立即回退到系统字体。
            WriteBody(ctx, 200, Array.Empty<byte>(), "text/css; charset=utf-8", false, null, "public, max-age=31536000");
            return;
        }
        if (path == "/library") { SendHtml(ctx, 200, AutoCloseHtml); return; }
        if (path == "/api/defaultcover" || path == "/defaultcover")
        {
            var def = LibraryService.Instance.DefaultCoverPath();
            if (def != null) ServeFile(ctx, def, "public, max-age=3600");
            else SendText(ctx, 404, "no default cover");
            return;
        }

        if (path == "/wiki" || path == "/wiki/" || path.StartsWith("/wiki/", StringComparison.Ordinal))
        {
            ServeWikiAsset(ctx);
            return;
        }
        if (path.StartsWith("/zimapi/", StringComparison.Ordinal))
        {
            HandleZimApi(ctx, path["/zimapi/".Length..], query);
            return;
        }
        if (path == "/zim" || path.StartsWith("/zim/", StringComparison.Ordinal))
        {
            HandleZimContent(ctx, path.Length > 5 ? path[5..] : "");
            return;
        }
        if (path == "/tiny" || path == "/tiny/")
        {
            ServeTinyAsset(ctx, "index.html");
            return;
        }
        if (path.StartsWith("/tiny/", StringComparison.Ordinal))
        {
            if (path.StartsWith("/tiny/api/", StringComparison.Ordinal))
            {
                HandleTinyApi(ctx, path["/tiny/api/".Length..], query);
                return;
            }
            ServeTinyAsset(ctx, path["/tiny/".Length..]);
            return;
        }
        if (path.StartsWith("/site/", StringComparison.Ordinal))
        {
            var rest = path["/site/".Length..];
            var slash = rest.IndexOf('/');
            var id = slash < 0 ? rest : rest[..slash];
            var rel = slash < 0 ? "" : rest[(slash + 1)..];
            if (SiteHostService.Instance.Resolve(id, rel, out var hosted))
                ServeFile(ctx, hosted, "no-cache");
            else
                SendText(ctx, 404, "site file not found");
            return;
        }
        if (path.StartsWith("/book/", StringComparison.Ordinal) || path.StartsWith("/mobi/", StringComparison.Ordinal))
        {
            ServeBookFile(ctx, path, query);
            return;
        }
        if (path.StartsWith("/txt/", StringComparison.Ordinal) || path.StartsWith("/md/", StringComparison.Ordinal))
        {
            var id = path[(path.LastIndexOf('/') + 1)..];
            ServeTextBook(ctx, id);
            return;
        }
        if (path.StartsWith("/mdimg/", StringComparison.Ordinal))
        {
            ServeMdImg(ctx, path["/mdimg/".Length..].Trim('/'), QueryValue(query, "rel"));
            return;
        }
        if (path.StartsWith("/cover/", StringComparison.Ordinal))
        {
            ServeCover(ctx, path["/cover/".Length..].Trim('/'));
            return;
        }
        if (path.StartsWith("/thumb/", StringComparison.Ordinal))
        {
            ServeThumb(ctx, path["/thumb/".Length..].Trim('/'));
            return;
        }
        if (path == "/thumbpath")
        {
            var p = QueryValue(query, "path");
            if (string.IsNullOrEmpty(p)) { SendText(ctx, 400, "path required"); return; }
            int w = QueryInt(query, "w", 320), h = QueryInt(query, "h", 320);
            var f = ThumbnailService.GetPathThumb(p, Math.Clamp(w, 48, 1600), Math.Clamp(h, 48, 1600));
            if (f == null && BrowserDecodableImage(p))
            {
                ServeFile(ctx, p, "public, max-age=3600");
                return;
            }
            if (f == null) SendText(ctx, 404, "no thumb");
            else ServeFile(ctx, f, "public, max-age=86400");
            return;
        }
        if (path == "/image" || path == "/media")
        {
            var p = QueryValue(query, "path");
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) { SendText(ctx, 404, "not found"); return; }
            ServeFile(ctx, p, "no-cache");
            return;
        }
        if (path == "/siblings")
        {
            var p = QueryValue(query, "path");
            var kind = QueryValue(query, "kind");
            if (string.IsNullOrEmpty(kind)) kind = "media";
            var list = FsService.Siblings(p, kind);
            SendJson(ctx, 200, list);
            return;
        }

        // 旧版图片浏览器兼容接口
        if (path == "/imglist")
        {
            var folder = QueryValue(query, "path");
            var recursive = QueryBool(query, "recursive");
            var page = ImageScanService.Get(folder, recursive, -1, 1, null);
            SendJsonString(ctx, 200,
                "{\"total\":" + page.Total + ",\"state\":\"" + (page.Scanning ? "scanning" : "done") + "\"}");
            return;
        }
        if (path == "/imglist/page")
        {
            var folder = QueryValue(query, "path");
            var recursive = QueryBool(query, "recursive");
            int offset = QueryInt(query, "offset", 0), take = Math.Clamp(QueryInt(query, "take", 200), 1, 5000);
            var page = ImageScanService.Get(folder, recursive, offset, take, null);
            var sb = new StringBuilder();
            sb.Append("{\"offset\":").Append(page.Offset).Append(",\"total\":").Append(page.Total).Append(",\"files\":[");
            for (int i = 0; i < page.Items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(JsonStr(page.Items[i].Name))
                  .Append(",\"path\":").Append(JsonStr(page.Items[i].Path)).Append('}');
            }
            sb.Append("]}");
            SendJsonString(ctx, 200, sb.ToString());
            return;
        }
        if (path == "/imglist/progress")
        {
            var folder = QueryValue(query, "path");
            var recursive = QueryBool(query, "recursive");
            var page = ImageScanService.Get(folder, recursive, -1, 1, null);
            var p = new ScanProgress
            {
                Phase = page.Scanning ? "scanning" : "done",
                Files = page.Total,
                Scanned = page.Total,
                ElapsedMs = 0,
            };
            SendJson(ctx, 200, p);
            return;
        }

        // 旧版压缩包接口
        if (path == "/zip/entries")
        {
            var z = QueryValue(query, "path");
            var list = ZipService.List(z, QueryValue(query, "prefix"));
            SendJson(ctx, 200, list.Items);
            return;
        }
        if (path == "/zip/entry")
        {
            var z = QueryValue(query, "path");
            var entry = QueryValue(query, "entry");
            var data = ZipService.ReadEntry(z, entry, out var mime);
            if (data == null) { SendText(ctx, 404, "entry not found"); return; }
            ServeBytesRange(ctx, data, mime, "no-cache");
            return;
        }
        if (path == "/zip/last")
        {
            var z = QueryValue(query, "path");
            var last = MediaStore.Instance.GetZipLast(z);
            SendJsonString(ctx, 200, "{\"path\":" + JsonStr(z) + ",\"entry\":" + JsonStr(last) + "}");
            return;
        }
        if (path == "/lyrics")
        {
            // 旧接口：track/artist
            var track = QueryValue(query, "track");
            var artist = QueryValue(query, "artist");
            var src = QueryValue(query, "path");
            var dto = !string.IsNullOrEmpty(src)
                ? LyricsService.Get(src)
                : LyricsService.Get(track + ".mp3", track, artist);
            SendJson(ctx, 200, dto);
            return;
        }

        // 静态资源
        ServeStatic(ctx, path);
    }

    // ==================== 静态资源 ====================

    private static string AutoCloseHtml =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><title>返回书架</title></head><body>" +
        "<script>try{if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(JSON.stringify({type:'close'}));}}catch(e){}" +
        "try{window.close();}catch(e){}</script></body></html>";

    private void ServeStatic(HttpListenerContext ctx, string path)
    {
        string? local = null;
        string cache = "no-cache";

        if (path.Equals("/shelf", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/shelf/", StringComparison.OrdinalIgnoreCase))
        {
            var rel = path.Length <= "/shelf/".Length ? "" : path["/shelf/".Length..];
            if (string.IsNullOrEmpty(rel) || rel.EndsWith('/')) rel += "index.html";
            local = ResolveUnder(ShelfDir, rel);
        }
        else if (path.StartsWith("/assets/vendor/", StringComparison.OrdinalIgnoreCase))
        {
            local = ResolveUnder(VendorDir, path["/assets/vendor/".Length..]);
            cache = "public, max-age=86400";
        }
        else if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            local = ResolveUnder(ReaderAssetsDir, path["/assets/".Length..]);
            cache = "public, max-age=86400";
        }
        else
        {
            var rel = path.TrimStart('/');
            if (string.IsNullOrEmpty(rel)) rel = "index.html";
            // Next.js 导出的 /reader、/import、/offline 等路由
            if (!Path.HasExtension(rel))
            {
                var html = ResolveUnder(NnReaderDir, rel + ".html");
                if (html != null && File.Exists(html)) local = html;
                else
                {
                    var index = ResolveUnder(NnReaderDir, rel + "/index.html");
                    if (index != null && File.Exists(index)) local = index;
                    else local = ResolveUnder(NnReaderDir, rel);
                }
            }
            else local = ResolveUnder(NnReaderDir, rel);
        }

        if (local == null || !File.Exists(local))
        {
            SendText(ctx, 404, "not found");
            return;
        }
        var ext = Path.GetExtension(local).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".svg" or ".ico" or ".ttf" or ".otf" or ".woff" or ".woff2" or ".wasm")
            cache = "public, max-age=86400";
        ServeFile(ctx, local, cache);
    }

    private static string? ResolveUnder(string root, string rel)
    {
        try
        {
            var rootFull = Path.GetFullPath(root);
            var full = Path.GetFullPath(Path.Combine(rootFull, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
                return null;
            return full;
        }
        catch { return null; }
    }

    // ==================== 书籍 / 文本 / 封面 ====================

    private void ServeBookFile(HttpListenerContext ctx, string path, string query)
    {
        var id = path[(path.LastIndexOf('/') + 1)..];
        if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(query)) id = QueryValue(query, "id");
        var book = DbStore.Instance.GetBook(id);
        if (book == null || !File.Exists(book.FilePath)) { SendText(ctx, 404, "book not found"); return; }
        var readable = EpubRepairService.ResolveReadablePath(book);
        ServeFile(ctx, readable, "no-cache", "application/octet-stream");
    }

    private void ServeTextBook(HttpListenerContext ctx, string id)
    {
        var book = DbStore.Instance.GetBook(id);
        if (book == null || !File.Exists(book.FilePath)) { SendText(ctx, 404, "book not found"); return; }
        var fi = new FileInfo(book.FilePath);
        string text;
        long ticks = fi.LastWriteTimeUtc.Ticks;
        if (fi.Length <= 8L * 1024 * 1024 && _txtCache.TryGetValue(book.FilePath, out var cached) && cached.Ticks == ticks)
        {
            text = cached.Text;
        }
        else
        {
            text = TextCodec.ReadTxt(book.FilePath);
            if (fi.Length <= 8L * 1024 * 1024)
            {
                if (_txtCache.Count > 4) _txtCache.Clear();
                _txtCache[book.FilePath] = (ticks, text);
            }
        }
        WriteBody(ctx, 200, Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", true, null, "no-cache");
    }

    private void ServeMdImg(HttpListenerContext ctx, string id, string? rel)
    {
        var book = DbStore.Instance.GetBook(id);
        if (book == null || string.IsNullOrWhiteSpace(rel)) { SendText(ctx, 404, "not found"); return; }
        var dir = Path.GetDirectoryName(book.FilePath) ?? "";
        string full;
        try { full = Path.GetFullPath(Path.Combine(dir, rel)); }
        catch { SendText(ctx, 404, "not found"); return; }
        if (!full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full))
        {
            SendText(ctx, 404, "not found");
            return;
        }
        ServeFile(ctx, full, "public, max-age=3600");
    }

    private void ServeCover(HttpListenerContext ctx, string id)
    {
        var book = DbStore.Instance.GetBook(id);
        if (book == null) { SendText(ctx, 404, "book not found"); return; }
        var cover = LibraryService.Instance.GetCoverPath(book);
        cover ??= LibraryService.Instance.DefaultCoverPath();
        if (cover == null || !File.Exists(cover)) { SendText(ctx, 404, "no cover"); return; }
        ServeFile(ctx, cover, "public, max-age=86400");
    }

    private void ServeThumb(HttpListenerContext ctx, string id)
    {
        var book = DbStore.Instance.GetBook(id);
        if (book == null) { SendText(ctx, 404, "book not found"); return; }
        var thumb = ThumbnailService.GetBookThumb(book);
        if (thumb != null && File.Exists(thumb)) { ServeFile(ctx, thumb, "public, max-age=86400"); return; }
        // GDI+ 解不了 AVIF/WebP/SVG：直接把原封面交给 WebView2 解码（浏览器原生支持）。
        var cover = LibraryService.Instance.GetCoverPath(book);
        if (cover != null && BrowserDecodableImage(cover))
        {
            ServeFile(ctx, cover, "public, max-age=3600");
            return;
        }
        SendText(ctx, 404, "no thumb");
    }

    private static bool BrowserDecodableImage(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".avif" or ".webp" or ".svg" or ".svgz";

    // ==================== API ====================

    private void HandleApi(HttpListenerContext ctx, string path, string query)
    {
        var req = ctx.Request;
        var seg = path["/api/".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        string s0 = seg.Length > 0 ? seg[0] : "";
        string s1 = seg.Length > 1 ? seg[1] : "";
        try
        {
            switch (s0)
            {
                case "library":
                    if (s1 == "page") { HandleLibraryPage(ctx, query); return; }
                    if (s1 == "recent") { HandleRecent(ctx, query); return; }
                    if (s1 == "facets") { SendJson(ctx, 200, DbStore.Instance.GetFacets()); return; }
                    if (s1 == "stats") { SendJson(ctx, 200, LibraryService.Instance.BuildStats()); return; }
                    if (s1 == "importFolder" && req.HttpMethod == "POST") { HandleImportFolder(ctx); return; }
                    if (s1 == "importPaths" && req.HttpMethod == "POST") { HandleImportPaths(ctx); return; }
                    if (s1 == "purge-missing" && req.HttpMethod == "POST")
                    {
                        var job = LibraryService.Instance.StartPurgeMissing();
                        SendJson(ctx, 200, job);
                        return;
                    }
                    if (s1 == "rebuild-covers" && req.HttpMethod == "POST")
                    {
                        var job = LibraryService.Instance.StartRebuildCovers();
                        SendJson(ctx, 200, job);
                        return;
                    }
                    if (s1 == "repair-metadata" && req.HttpMethod == "POST")
                    {
                        SendJson(ctx, 200, LibraryService.Instance.StartRepairMetadata());
                        return;
                    }
                    if (s1 == "cleanup-legacy" && req.HttpMethod == "POST")
                    {
                        var ok = LibraryService.Instance.CleanupLegacyJson(out var files, out var bytes, out var err);
                        SendJson(ctx, ok ? 200 : 400, new OkDto { Ok = ok, Count = files, Message = bytes.ToString(), Error = err });
                        return;
                    }
                    if (s1 == "default-cover")
                    {
                        var def = LibraryService.Instance.DefaultCoverPath();
                        if (def == null) { SendText(ctx, 404, "no default cover"); return; }
                        ServeFile(ctx, def, "public, max-age=3600");
                        return;
                    }
                    if (s1 == "full") { SendLibraryFull(ctx, null); return; }
                    HandleLibraryCompat(ctx, query);
                    return;

                case "books": HandleBooks(ctx, seg, req); return;
                case "appsettings": HandleAppSettings(ctx, req); return;
                case "settings": HandleSettings(ctx, req); return;
                case "appinfo": SendJson(ctx, 200, BuildAppInfo()); return;
                case "fonts": HandleFonts(ctx); return;
                case "notes": HandleNotes(ctx, seg, req); return;
                case "bookconfig": HandleBookConfig(ctx, seg, req); return;
                case "pdfprefs": HandlePdfPrefs(ctx, seg, req); return;
                case "import": HandleImport(ctx, seg, req, query); return;
                case "jobs": HandleJobs(ctx, seg, req); return;
                case "fs": HandleFs(ctx, seg, req, query); return;
                case "images": HandleImages(ctx, query); return;
                case "siblings": HandleSiblingsApi(ctx, query); return;
                case "media": HandleMedia(ctx, seg, req, query); return;
                case "filetext": HandleFileText(ctx, req, query); return;
                case "imageedit": HandleImageEdit(ctx, req); return;
                case "macros": HandleMacros(ctx, seg, req, query); return;
                case "favorites": HandleFavorites(ctx, seg, req, query); return;
                case "tools": HandleTools(ctx, seg, req, query); return;
                case "sites": HandleSites(ctx, seg, req, query); return;
                case "audio": HandleAudio(ctx, seg, query); return;
                case "lyrics": HandleLyricsApi(ctx, query); return;
                case "zip": HandleZipApi(ctx, seg, req, query); return;
                case "defaultcover":
                    if (req.HttpMethod == "POST") { HandleDefaultCover(ctx, req); return; }
                    var dc = LibraryService.Instance.DefaultCoverPath();
                    if (dc != null) ServeFile(ctx, dc, "public, max-age=3600");
                    else SendText(ctx, 404, "no default cover");
                    return;
                case "rebuild-covers":
                    if (req.HttpMethod != "POST") { SendText(ctx, 405, "method"); return; }
                    SendJson(ctx, 200, LibraryService.Instance.StartRebuildCovers());
                    return;
                default:
                    SendText(ctx, 404, "unknown api");
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Error("API 失败 " + path, ex);
            TryError(ctx, 500, ex.Message);
        }
    }

    // ---------- 书库列表 ----------

    private LibraryQuery ParseQuery(string query) => new()
    {
        Q = QueryValue(query, "q"),
        Sort = string.IsNullOrEmpty(QueryValue(query, "sort")) ? "recent" : QueryValue(query, "sort"),
        Desc = !query.Contains("desc=0", StringComparison.OrdinalIgnoreCase),
        Format = QueryValue(query, "format"),
        Author = QueryValue(query, "author"),
        Category = QueryValue(query, "category"),
        Folder = QueryValue(query, "folder"),
        Filter = string.IsNullOrEmpty(QueryValue(query, "filter")) ? "all" : QueryValue(query, "filter"),
    };

    private void HandleLibraryPage(HttpListenerContext ctx, string query)
    {
        var q = ParseQuery(query);
        int offset = Math.Max(0, QueryInt(query, "offset", 0));
        int limit = Math.Clamp(QueryInt(query, "limit", 120), 1, 500);
        bool needTotal = QueryInt(query, "count", 1) != 0;
        var page = DbStore.Instance.Query(q, offset, limit, needTotal);
        var etag = "W/\"lib-" + page.Version + "-" + HashShort(q.Signature) + "\"";
        WriteJsonWithEtag(ctx, 200, page, etag);
    }

    private void HandleRecent(HttpListenerContext ctx, string query)
    {
        int limit = Math.Clamp(QueryInt(query, "limit", 12), 1, 60);
        bool inProgress = QueryBool(query, "inProgress");
        var list = DbStore.Instance.Recent(limit, inProgress);
        SendJson(ctx, 200, list);
    }

    /// <summary>
    /// readest 兼容的 /api/library。阅读器只按 ids 找一本书，
    /// 因此从 Referer 中提取 ids 只返回需要的书，避免 11 万本书时把几十 MB JSON 塞给阅读器。
    /// </summary>
    private void HandleLibraryCompat(HttpListenerContext ctx, string query)
    {
        if (ctx.Request.HttpMethod != "GET" && ctx.Request.HttpMethod != "HEAD")
        {
            SendText(ctx, 405, "method not allowed");
            return;
        }
        var ids = ExtractReaderIds(ctx.Request, query);
        if (ids.Length > 0) { SendLibraryFull(ctx, ids); return; }
        if (QueryBool(query, "full") || !string.IsNullOrEmpty(QueryValue(query, "all")))
        {
            SendLibraryFull(ctx, null);
            return;
        }
        // 没有 Referer/ids 时，默认也返回完整数组（兼容旧调用方），使用流式 + gzip
        SendLibraryFull(ctx, null);
    }

    private static string[] ExtractReaderIds(HttpListenerRequest req, string query)
    {
        var ids = QueryValue(query, "ids");
        if (string.IsNullOrEmpty(ids))
        {
            var referer = req.Headers["Referer"];
            if (!string.IsNullOrEmpty(referer))
            {
                var q = referer.IndexOf('?');
                if (q >= 0) ids = QueryValue("?" + referer[(q + 1)..], "ids");
            }
        }
        if (string.IsNullOrEmpty(ids)) return Array.Empty<string>();
        return ids.Split(new[] { ',', '+', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .Distinct(StringComparer.Ordinal)
            .Take(100)
            .ToArray();
    }

    private void SendLibraryFull(HttpListenerContext ctx, string[]? ids)
    {
        if (ids != null)
        {
            var books = DbStore.Instance.GetBooks(ids);
            SendJson(ctx, 200, books.Select(ToNnBook).ToList());
            return;
        }

        // 全量导出流式写入（10 万本书约 20MB），避免一次性构造巨大字符串
        var res = ctx.Response;
        var req = ctx.Request;
        try
        {
            res.StatusCode = 200;
            res.ContentType = "application/json; charset=utf-8";
            res.Headers["Cache-Control"] = "no-store";
            res.Headers["Access-Control-Allow-Origin"] = "*";
            if (req.HttpMethod == "HEAD")
            {
                res.ContentLength64 = 0;
                res.OutputStream.Close();
                return;
            }
            bool gzip = AcceptsGzip(req);
            if (gzip)
            {
                res.Headers["Content-Encoding"] = "gzip";
                res.Headers["Vary"] = "Accept-Encoding";
            }
            res.SendChunked = true;
            var target = gzip
                ? (Stream)new GZipStream(res.OutputStream, CompressionLevel.Fastest, leaveOpen: true)
                : res.OutputStream;
            using (var writer = new Utf8JsonWriter(target))
            {
                writer.WriteStartArray();
                DbStore.Instance.StreamNnBooks(writer, BaseUrl);
                writer.WriteEndArray();
                writer.Flush();
            }
            target.Flush();
            res.OutputStream.Close();
        }
        catch { try { res.Abort(); } catch { } }
    }

    private NnBookDto ToNnBook(BookRecord b) => new()
    {
        Hash = b.Id,
        Format = BookFormatEx.NnFormat(b.Format),
        Title = b.Title,
        Author = b.Author,
        CreatedAt = new DateTimeOffset(b.AddedAt).ToUnixTimeMilliseconds(),
        UpdatedAt = new DateTimeOffset(b.LastReadAt ?? b.AddedAt).ToUnixTimeMilliseconds(),
        Progress = b.Progress > 0 ? new[] { Math.Round(b.Progress * 100, 2), 100.0 } : null,
        CoverImageUrl = BaseUrl + "/cover/" + b.Id,
    };

    // ---------- 书籍操作 ----------

    private void HandleBooks(HttpListenerContext ctx, string[] seg, HttpListenerRequest req)
    {
        if (seg.Length < 2 || string.IsNullOrEmpty(seg[1])) { SendText(ctx, 400, "book id required"); return; }
        var id = seg[1];
        var action = seg.Length > 2 ? seg[2] : "";

        if (req.HttpMethod == "GET")
        {
            var detail = DbStore.Instance.BuildDetail(id, DbStore.Instance.CountNotes);
            if (detail == null) { SendText(ctx, 404, "book not found"); return; }
            SendJson(ctx, 200, detail);
            return;
        }
        if (req.HttpMethod == "DELETE")
        {
            var ok = LibraryService.Instance.RemoveBook(id, out var err);
            SendJson(ctx, ok ? 200 : 404, new OkDto { Ok = ok, Error = err });
            return;
        }
        if (req.HttpMethod == "POST")
        {
            using var body = ReadJson(req);
            if (body == null) { SendText(ctx, 400, "body required"); return; }
            var root = body.RootElement;
            if (action == "favorite")
            {
                bool fav = root.TryGetProperty("favorite", out var f) && f.ValueKind == JsonValueKind.True;
                DbStore.Instance.UpdateBook(id, b => b.Favorite = fav);
                SendJson(ctx, 200, new OkDto { Ok = true });
                return;
            }
            if (action == "reveal")
            {
                var book = DbStore.Instance.GetBook(id);
                var ok = book != null && FsService.RevealPath(book.FilePath);
                SendJson(ctx, ok ? 200 : 404, new OkDto { Ok = ok });
                return;
            }
            string? title = GetString(root, "title");
            string? author = GetString(root, "author");
            string? category = GetString(root, "category");
            string? description = GetString(root, "description");
            string? cover = GetString(root, "coverDataUrl");
            if (root.TryGetProperty("favorite", out var favEl) && favEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                DbStore.Instance.UpdateBook(id, b => b.Favorite = favEl.GetBoolean());
            }
            if (title != null || author != null || category != null || description != null)
            {
                DbStore.Instance.UpdateBook(id, b =>
                {
                    if (title != null && title.Trim().Length > 0) b.Title = title.Trim();
                    if (author != null) b.Author = author.Trim();
                    if (category != null) b.Category = category.Trim();
                    if (description != null) b.Description = description.Trim();
                });
            }
            if (!string.IsNullOrEmpty(cover))
            {
                if (!LibraryService.Instance.ReplaceCoverFromDataUrl(id, cover, out var err))
                {
                    SendJson(ctx, 400, new OkDto { Ok = false, Error = err });
                    return;
                }
            }
            SendJson(ctx, 200, new OkDto { Ok = true });
            return;
        }
        SendText(ctx, 405, "method not allowed");
    }

    // ---------- 设置 ----------

    private void HandleAppSettings(HttpListenerContext ctx, HttpListenerRequest req)
    {
        if (req.HttpMethod == "GET")
        {
            var s = DbStore.Instance.AppSettings;
            SendJson(ctx, 200, s);
            return;
        }
        if (req.HttpMethod != "POST") { SendText(ctx, 405, "method"); return; }
        using var body = ReadJson(req);
        if (body == null) { SendText(ctx, 400, "body required"); return; }
        var root = body.RootElement;
        DbStore.Instance.UpdateAppSettings(s =>
        {
            var theme = GetString(root, "appTheme");
            if (theme is "dark" or "light" or "forest" or "violet" or "warm") s.AppTheme = theme;
            var sort = GetString(root, "sortMode");
            if (sort is "added" or "recent" or "title" or "author" or "size" or "format") s.SortMode = sort;
            if (root.TryGetProperty("sortDesc", out var sd) && sd.ValueKind is JsonValueKind.True or JsonValueKind.False) s.SortDesc = sd.GetBoolean();
            if (root.TryGetProperty("showCovers", out var sc) && sc.ValueKind is JsonValueKind.True or JsonValueKind.False) s.ShowCovers = sc.GetBoolean();
            var view = GetString(root, "libraryView");
            if (view is "grid" or "list") s.LibraryView = view;
            if (root.TryGetProperty("gridSize", out var gs) && gs.TryGetInt32(out var gv)) s.GridSize = Math.Clamp(gv, 96, 320);
            var rs = GetString(root, "readerSettingsJson");
            if (!string.IsNullOrWhiteSpace(rs)) s.ReaderSettingsJson = rs;
            if (root.TryGetProperty("importCovers", out var ic) && ic.ValueKind is JsonValueKind.True or JsonValueKind.False) s.ImportCovers = ic.GetBoolean();
            if (root.TryGetProperty("useDefaultCover", out var udc) && udc.ValueKind is JsonValueKind.True or JsonValueKind.False) s.UseDefaultCover = udc.GetBoolean();
            if (root.TryGetProperty("autoRefreshLibrary", out var ar) && ar.ValueKind is JsonValueKind.True or JsonValueKind.False) s.AutoRefreshLibrary = ar.GetBoolean();
            if (root.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
            {
                if (media.TryGetProperty("imageSlideSeconds", out var v) && v.TryGetDouble(out var slidev)) s.Media.ImageSlideSeconds = Math.Clamp(slidev, 0.1, 60);
                if (media.TryGetProperty("comicMode", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Media.ComicMode = v.GetBoolean();
                var cw = GetString(media, "comicWidth");
                if (cw is "full" or "2/3" or "1/2" or "default") s.Media.ComicWidth = cw;
                if (media.TryGetProperty("comicAutoPage", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Media.ComicAutoPage = v.GetBoolean();
                if (media.TryGetProperty("recursiveImages", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Media.RecursiveImages = v.GetBoolean();
                if (media.TryGetProperty("musicBackgroundSeconds", out v) && v.TryGetDouble(out var mbs)) s.Media.MusicBackgroundSeconds = Math.Clamp(mbs, 0.1, 60);
                if (media.TryGetProperty("musicShowLyrics", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Media.MusicShowLyrics = v.GetBoolean();
                if (media.TryGetProperty("videoAutoResume", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Media.VideoAutoResume = v.GetBoolean();
                var tr = GetString(media, "imageTransition");
                if (tr is "none" or "fade" or "slide" or "zoom") s.Media.ImageTransition = tr;
                if (media.TryGetProperty("imageBackgroundMusic", out v) && v.ValueKind == JsonValueKind.String)
                {
                    var bgm = v.GetString();
                    s.Media.ImageBackgroundMusic = string.IsNullOrWhiteSpace(bgm) ? null : bgm;
                }
                if (media.TryGetProperty("imageSlideLoop", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Media.ImageSlideLoop = v.GetBoolean();
                if (media.TryGetProperty("musicVolume", out v) && v.TryGetDouble(out var mv)) s.Media.MusicVolume = Math.Clamp(mv, 0, 1);
                var mm = GetString(media, "musicMode");
                if (mm is "order" or "repeat" or "shuffle") s.Media.MusicMode = mm;
                var mt = GetString(media, "musicTab");
                if (mt is "list" or "lyrics") s.Media.MusicTab = mt;
                if (media.TryGetProperty("videoVolume", out v) && v.TryGetDouble(out var vv)) s.Media.VideoVolume = Math.Clamp(vv, 0, 1);
                if (media.TryGetProperty("videoShowList", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Media.VideoShowList = v.GetBoolean();
                if (media.TryGetProperty("musicBackgroundFolder", out v) && v.ValueKind == JsonValueKind.String)
                {
                    var bgf = v.GetString();
                    s.Media.MusicBackgroundFolder = string.IsNullOrWhiteSpace(bgf) ? null : bgf;
                }
                if (media.TryGetProperty("musicBackgroundIndex", out v) && v.TryGetInt32(out var bgi)) s.Media.MusicBackgroundIndex = Math.Max(0, bgi);
                if (media.TryGetProperty("musicBackgroundRandom", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Media.MusicBackgroundRandom = v.GetBoolean();
                var bgfit = GetString(media, "musicBackgroundFit");
                if (bgfit is "cover" or "contain" or "stretch" or "center") s.Media.MusicBackgroundFit = bgfit;
                if (media.TryGetProperty("musicBackgroundBlur", out v) && v.TryGetInt32(out var bgblur)) s.Media.MusicBackgroundBlur = Math.Clamp(bgblur, 0, 10);
                var fm = GetString(media, "imageFitMode");
                if (fm is "contain" or "width" or "height" or "center" or "actual") s.Media.ImageFitMode = fm;
                if (media.TryGetProperty("imageBackgroundMusicFolder", out v) && v.ValueKind == JsonValueKind.String)
                {
                    var bmf = v.GetString();
                    s.Media.ImageBackgroundMusicFolder = string.IsNullOrWhiteSpace(bmf) ? null : bmf;
                }
                if (media.TryGetProperty("imageBackgroundMusicIndex", out v) && v.TryGetInt32(out var bmi)) s.Media.ImageBackgroundMusicIndex = Math.Max(0, bmi);
                if (media.TryGetProperty("imageLastFolder", out v) && v.ValueKind == JsonValueKind.String)
                {
                    var ilf = v.GetString();
                    s.Media.ImageLastFolder = string.IsNullOrWhiteSpace(ilf) ? null : ilf;
                }
            }
        });
        SendJson(ctx, 200, new OkDto { Ok = true });
    }

    private void HandleSettings(HttpListenerContext ctx, HttpListenerRequest req)
    {
        if (req.HttpMethod == "GET")
        {
            var current = DbStore.Instance.AppSettings.ReaderSettingsJson;
            if (string.IsNullOrWhiteSpace(current)) current = DbStore.DefaultReaderSettings;
            SendJsonString(ctx, 200, "{\"json\":" + JsonStr(current) + "}");
            return;
        }
        if (req.HttpMethod != "POST") { SendText(ctx, 405, "method"); return; }
        using var body = ReadJson(req);
        if (body == null) { SendText(ctx, 400, "body required"); return; }
        var json = GetString(body.RootElement, "json");
        if (string.IsNullOrWhiteSpace(json)) { SendText(ctx, 400, "json required"); return; }
        try { using var check = JsonDocument.Parse(json); }
        catch { SendText(ctx, 400, "invalid json"); return; }
        DbStore.Instance.UpdateAppSettings(s => s.ReaderSettingsJson = json);
        SendJson(ctx, 200, new OkDto { Ok = true });
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL3000",
        Justification = "AOT/单文件下 Assembly.Location 为空，此处正是用它判断发布形态")]
    private AppInfoDto BuildAppInfo()
    {
        if (_webViewVersion == null)
        {
            try { _webViewVersion = WebView2Utilities.GetAvailableCoreWebView2BrowserVersionString() ?? ""; }
            catch { _webViewVersion = ""; }
        }
        // 单文件/AOT 下 Assembly.Location 为空；这里刻意用它判断发布形态
#pragma warning disable IL3000
        var isAot = string.IsNullOrEmpty(typeof(MoonServer).Assembly.Location);
#pragma warning restore IL3000
        return new AppInfoDto
        {
            Version = "1.0.0",
            DataDir = DbStore.Instance.DataDir,
            BaseUrl = BaseUrl,
            BookCount = DbStore.Instance.Count,
            IsAot = isAot,
            Os = Environment.OSVersion.ToString(),
            WebViewVersion = string.IsNullOrEmpty(_webViewVersion) ? null : _webViewVersion,
            StartedAt = new DateTimeOffset(StartedAt).ToUnixTimeMilliseconds(),
        };
    }

    private void HandleFonts(HttpListenerContext ctx)
    {
        if (_fontCache == null)
        {
            var list = new List<string>();
            try
            {
                using var col = new System.Drawing.Text.InstalledFontCollection();
                foreach (var f in col.Families)
                {
                    try { list.Add(f.Name); } catch { }
                }
            }
            catch (Exception ex) { Log.Warn("枚举系统字体失败: " + ex.Message); }
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            _fontCache = list;
        }
        var embedded = new List<string> { "FZKeBenFangSong", "FZPingXianYaSong", "FZYouHei" };
        var dto = new FontsDto
        {
            Fonts = _fontCache.Where(f => f != "FZKeBenFangSong" && f != "FZPingXianYaSong" && f != "FZYouHei").ToList(),
            Embedded = embedded,
        };
        SendJson(ctx, 200, dto);
    }

    // ---------- 笔记 ----------

    private void HandleNotes(HttpListenerContext ctx, string[] seg, HttpListenerRequest req)
    {
        if (req.HttpMethod == "GET")
        {
            var all = BuildNotes(seg.Length > 1 ? seg[1] : null);
            SendJson(ctx, 200, all);
            return;
        }
        if (seg.Length < 2) { SendText(ctx, 400, "note id required"); return; }
        var noteId = seg[1];
        if (req.HttpMethod == "PUT")
        {
            using var body = ReadJson(req);
            var text = body == null ? null : GetString(body.RootElement, "note");
            if (text == null) { SendText(ctx, 400, "note required"); return; }
            var ok = MutateNote(noteId, text, remove: false);
            SendJson(ctx, ok ? 200 : 404, new OkDto { Ok = ok });
            return;
        }
        if (req.HttpMethod == "DELETE")
        {
            var ok = MutateNote(noteId, null, remove: true);
            SendJson(ctx, ok ? 200 : 404, new OkDto { Ok = ok });
            return;
        }
        SendText(ctx, 405, "method not allowed");
    }

    private List<NoteView> BuildNotes(string? bookFilter)
        => DbStore.Instance.BuildNotes(bookFilter);

    private bool MutateNote(string noteId, string? newText, bool remove)
    {
        bool configOk = false;
        var bookId = DbStore.Instance.FindNoteBook(noteId);
        if (!string.IsNullOrEmpty(bookId))
        {
            var cfg = DbStore.Instance.GetConfig(bookId);
            if (cfg?.ConfigJson != null)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize(cfg.ConfigJson, Info<BookConfigDto>.I);
                    var note = dto?.Booknotes?.FirstOrDefault(n => n.Id == noteId);
                    if (dto != null && note != null)
                    {
                        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                        if (remove)
                        {
                            using var doc = JsonDocument.Parse(now.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            note.DeletedAt = doc.RootElement.Clone();
                        }
                        else
                        {
                            note.Note = newText ?? "";
                        }
                        note.UpdatedAt = now;
                        dto.UpdatedAt = now;
                        cfg.ConfigJson = JsonSerializer.Serialize(dto, Info<BookConfigDto>.I);
                        DbStore.Instance.PutConfig(cfg);
                        configOk = true;
                    }
                }
                catch { }
            }
        }
        else
        {
            // 极少数旧数据尚未进 notes 表：退回扫描配置
            foreach (var cfg in DbStore.Instance.AllConfigs())
            {
                if (string.IsNullOrEmpty(cfg.ConfigJson)) continue;
                try
                {
                    var dto = JsonSerializer.Deserialize(cfg.ConfigJson, Info<BookConfigDto>.I);
                    var note = dto?.Booknotes?.FirstOrDefault(n => n.Id == noteId);
                    if (dto == null || note == null) continue;
                    var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    if (remove)
                    {
                        using var doc = JsonDocument.Parse(now.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        note.DeletedAt = doc.RootElement.Clone();
                    }
                    else note.Note = newText ?? "";
                    note.UpdatedAt = now;
                    dto.UpdatedAt = now;
                    cfg.ConfigJson = JsonSerializer.Serialize(dto, Info<BookConfigDto>.I);
                    DbStore.Instance.PutConfig(cfg);
                    configOk = true;
                    break;
                }
                catch { }
            }
        }
        var rowOk = DbStore.Instance.UpdateNoteRow(noteId, newText, remove);
        return configOk || rowOk;
    }

    // ---------- 阅读配置 ----------

    private void HandleBookConfig(HttpListenerContext ctx, string[] seg, HttpListenerRequest req)
    {
        if (req.HttpMethod == "GET")
        {
            var id = seg.Length > 1 ? seg[1] : QueryValue(ctx.Request.Url?.Query ?? "", "bookId");
            if (string.IsNullOrEmpty(id)) { SendText(ctx, 400, "bookId required"); return; }
            var cfg = DbStore.Instance.GetConfig(id);
            if (cfg == null)
            {
                SendJsonString(ctx, 200, "{\"bookId\":" + JsonStr(id) + ",\"configJson\":\"{}\"}");
                return;
            }
            var sb = new StringBuilder();
            sb.Append("{\"bookId\":").Append(JsonStr(cfg.BookId))
              .Append(",\"configJson\":").Append(JsonStr(cfg.ConfigJson ?? "{}"))
              .Append(",\"progress\":").Append(JsonStr(cfg.Progress))
              .Append(",\"location\":").Append(JsonStr(cfg.Location)).Append('}');
            SendJsonString(ctx, 200, sb.ToString());
            return;
        }
        if (req.HttpMethod != "POST") { SendText(ctx, 405, "method"); return; }
        using var body = ReadJson(req);
        if (body == null) { SendText(ctx, 400, "body required"); return; }
        var root = body.RootElement;
        var bookId = GetString(root, "bookId");
        if (string.IsNullOrEmpty(bookId)) { SendText(ctx, 400, "bookId required"); return; }

        var cfg2 = DbStore.Instance.GetConfig(bookId) ?? new ConfigRecord { BookId = bookId };
        var configJson = GetString(root, "configJson");
        if (configJson != null) cfg2.ConfigJson = configJson;
        var explicitProgress = GetString(root, "progress");
        if (explicitProgress != null) cfg2.Progress = explicitProgress;
        var explicitLocation = GetString(root, "location");
        if (explicitLocation != null) cfg2.Location = explicitLocation;
        var viewSettings = GetString(root, "viewSettings");
        if (viewSettings != null) cfg2.ViewSettings = viewSettings;
        cfg2.UpdatedAt = DateTime.Now;
        DbStore.Instance.PutConfig(cfg2);

        double progress = -1;
        string? location = explicitLocation;
        if (!string.IsNullOrEmpty(cfg2.ConfigJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(cfg2.ConfigJson);
                var r = doc.RootElement;
                if (r.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
                {
                    double cur = ToDouble(p[0]);
                    double total = ToDouble(p[1]);
                    if (total > 0) progress = Math.Clamp(cur / total, 0, 1);
                }
                if (string.IsNullOrEmpty(location) && r.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.String)
                    location = loc.GetString();
            }
            catch { }
        }
        DbStore.Instance.UpdateProgress(bookId, progress, location);
        var delta = DbStore.Instance.NoteConfigSeen(bookId);
        if (delta > 0) DbStore.Instance.AddReadingSeconds(bookId, delta);
        NotifyChangedThrottled();
        SendJson(ctx, 200, new OkDto { Ok = true });
    }

    private static double ToDouble(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number) return e.GetDouble();
        if (e.ValueKind == JsonValueKind.String &&
            double.TryParse(e.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    private void HandlePdfPrefs(HttpListenerContext ctx, string[] seg, HttpListenerRequest req)
    {
        if (req.HttpMethod == "GET")
        {
            var id = seg.Length > 1 ? seg[1] : QueryValue(req.Url?.Query ?? "", "bookId");
            var prefs = string.IsNullOrEmpty(id) ? null : DbStore.Instance.GetConfig(id);
            var sb = new StringBuilder();
            sb.Append("{\"bookId\":").Append(JsonStr(id))
              .Append(",\"pdfLayout\":").Append(JsonStr(prefs?.PdfLayout))
              .Append(",\"pdfZoom\":").Append(JsonStr(prefs?.PdfZoom))
              .Append(",\"pdfAutoPage\":").Append(prefs?.PdfAutoPage == true ? "true" : "false").Append('}');
            SendJsonString(ctx, 200, sb.ToString());
            return;
        }
        if (req.HttpMethod != "POST") { SendText(ctx, 405, "method"); return; }
        using var body = ReadJson(req);
        if (body == null) { SendText(ctx, 400, "body required"); return; }
        var r = body.RootElement;
        var bookId = GetString(r, "bookId");
        if (string.IsNullOrEmpty(bookId)) { SendText(ctx, 400, "bookId required"); return; }
        var cfg = DbStore.Instance.GetConfig(bookId) ?? new ConfigRecord { BookId = bookId };
        var layout = GetString(r, "pdfLayout");
        var zoom = GetString(r, "pdfZoom");
        if (layout != null) cfg.PdfLayout = layout;
        if (zoom != null) cfg.PdfZoom = zoom;
        if (r.TryGetProperty("pdfAutoPage", out var ap) && ap.ValueKind is JsonValueKind.True or JsonValueKind.False)
            cfg.PdfAutoPage = ap.GetBoolean();
        cfg.UpdatedAt = DateTime.Now;
        DbStore.Instance.PutConfig(cfg);
        SendJson(ctx, 200, new OkDto { Ok = true });
    }

    // ---------- 导入 ----------

    private void HandleImportFolder(HttpListenerContext ctx)
    {
        using var body = ReadJson(ctx.Request);
        var path = body == null ? null : GetString(body.RootElement, "path");
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) { SendText(ctx, 400, "文件夹不存在"); return; }
        var job = Jobs.Start("import", 0);
        _ = Task.Run(() =>
        {
            var token = Jobs.TokenOf(job.Id);
            var result = LibraryService.Instance.ImportFolder(path,
                (done, total, cur) => Jobs.Update(job.Id, done, total, cur), token);
            Jobs.Update(job.Id, job.Total, job.Total);
            Jobs.Finish(job.Id, $"导入 {result.Imported} 本，跳过 {result.Skipped} 本，失败 {result.Failed} 本");
            NotifyChangedThrottled();
        });
        SendJson(ctx, 200, job);
    }

    private void HandleImportPaths(HttpListenerContext ctx)
    {
        using var body = ReadJson(ctx.Request);
        if (body == null) { SendText(ctx, 400, "body required"); return; }
        var paths = new List<string>();
        if (body.RootElement.TryGetProperty("paths", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in arr.EnumerateArray())
                if (p.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(p.GetString())) paths.Add(p.GetString()!);
        }
        var job = Jobs.Start("import", paths.Count);
        _ = Task.Run(() =>
        {
            var token = Jobs.TokenOf(job.Id);
            var result = LibraryService.Instance.ImportPaths(paths,
                (done, total, cur) => Jobs.Update(job.Id, done, total, cur), token);
            Jobs.Update(job.Id, job.Total, job.Total);
            Jobs.Finish(job.Id, $"导入 {result.Imported} 本，跳过 {result.Skipped} 本，失败 {result.Failed} 本");
            NotifyChangedThrottled();
        });
        SendJson(ctx, 200, job);
    }

    private void HandleImport(HttpListenerContext ctx, string[] seg, HttpListenerRequest req, string query)
    {
        var kind = seg.Length > 1 ? seg[1] : "";
        if (kind == "begin" && req.HttpMethod == "POST")
        {
            using var body = ReadJson(req);
            var name = body == null ? null : GetString(body.RootElement, "name");
            long size = 0;
            if (body != null && body.RootElement.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv)) size = sv;
            if (size > 8L * 1024 * 1024 * 1024) { SendText(ctx, 400, "文件过大"); return; }
            var id = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(UploadDir);
            _uploads[id] = new UploadSession
            {
                TempFile = Path.Combine(UploadDir, id + ".tmp"),
                Expected = size,
                Name = name ?? "book.txt",
            };
            // 清理 1 小时前的上传会话与临时文件
            foreach (var old in _uploads.Where(kv => (DateTime.Now - kv.Value.Created).TotalHours > 1).ToList())
            {
                _uploads.TryRemove(old.Key, out _);
                try { File.Delete(old.Value.TempFile); } catch { }
            }
            SendJsonString(ctx, 200, "{\"uploadId\":\"" + id + "\"}");
            return;
        }
        if (kind == "chunk" && seg.Length > 2 && (req.HttpMethod == "PUT" || req.HttpMethod == "POST"))
        {
            var uploadId = seg[2];
            if (!_uploads.TryGetValue(uploadId, out var session)) { SendText(ctx, 404, "upload not found"); return; }
            var raw = QueryValue(query, "offset");
            long pos = long.TryParse(raw, out var p) ? p : -1;
            lock (session.Gate)
            {
                if (pos < 0) pos = session.Received;
                using var fs = new FileStream(session.TempFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                fs.Seek(pos, SeekOrigin.Begin);
                req.InputStream.CopyTo(fs, 81920);
                session.Received = fs.Position;
            }
            SendJsonString(ctx, 200, "{\"received\":" + session.Received + "}");
            return;
        }
        if (kind == "finish" && seg.Length > 2 && req.HttpMethod == "POST")
        {
            var uploadId = seg[2];
            if (!_uploads.TryRemove(uploadId, out var session)) { SendText(ctx, 404, "upload not found"); return; }
            long len = 0;
            try { len = new FileInfo(session.TempFile).Length; } catch { }
            if (len <= 0)
            {
                try { File.Delete(session.TempFile); } catch { }
                SendJson(ctx, 400, new OkDto { Ok = false, Error = "上传内容为空" });
                return;
            }
            var book = LibraryService.Instance.ImportUploaded(session.Name, session.Expected > 0 ? session.Expected : len,
                session.TempFile, out var err);
            if (book == null)
            {
                try { File.Delete(session.TempFile); } catch { }
                SendJson(ctx, 400, new OkDto { Ok = false, Error = err });
                return;
            }
            NotifyChangedThrottled();
            SendJsonString(ctx, 200, "{\"ok\":true,\"id\":" + JsonStr(book.Id) + "}");
            return;
        }
        SendText(ctx, 404, "unknown import route");
    }

    private void HandleDefaultCover(HttpListenerContext ctx, HttpListenerRequest req)
    {
        using var body = ReadJson(req);
        var dataUrl = body == null ? null : GetString(body.RootElement, "dataUrl");
        if (string.IsNullOrEmpty(dataUrl)) { SendText(ctx, 400, "dataUrl required"); return; }
        try
        {
            var applied = LibraryService.Instance.SetDefaultCoverFromDataUrl(dataUrl);
            SendJson(ctx, 200, new OkDto { Ok = true, Applied = applied });
        }
        catch (Exception ex)
        {
            SendJson(ctx, 400, new OkDto { Ok = false, Error = ex.Message });
        }
    }

    // ---------- 任务 ----------

    private void HandleJobs(HttpListenerContext ctx, string[] seg, HttpListenerRequest req)
    {
        if (seg.Length >= 2 && seg[1] == "latest")
        {
            var kind = QueryValue(ctx.Request.Url?.Query ?? "", "kind");
            var job = string.IsNullOrEmpty(kind) ? null : Jobs.Latest(kind);
            if (job == null) SendJsonString(ctx, 200, "null");
            else SendJson(ctx, 200, job);
            return;
        }
        if (seg.Length >= 3 && seg[2] == "cancel" && req.HttpMethod == "POST")
        {
            var ok = Jobs.Cancel(seg[1]);
            SendJson(ctx, 200, new OkDto { Ok = ok });
            return;
        }
        if (seg.Length >= 2)
        {
            var job = Jobs.Get(seg[1]);
            if (job == null) { SendText(ctx, 404, "job not found"); return; }
            SendJson(ctx, 200, job);
            return;
        }
        SendText(ctx, 400, "job id required");
    }

    // ---------- 文件系统 / 媒体 ----------

    private void HandleFs(HttpListenerContext ctx, string[] seg, HttpListenerRequest req, string query)
    {
        var action = seg.Length > 1 ? seg[1] : "";
        if (action == "list" && req.HttpMethod == "GET")
        {
            var path = QueryValue(query, "path");
            var kind = QueryValue(query, "kind");
            int limit = Math.Clamp(QueryInt(query, "limit", 4000), 1, 20000);
            SendJson(ctx, 200, FsService.List(path, string.IsNullOrEmpty(kind) ? "all" : kind, limit));
            return;
        }
        if (action == "scan" && req.HttpMethod == "GET")
        {
            var path = QueryValue(query, "path");
            var kind = QueryValue(query, "kind");
            int limit = Math.Clamp(QueryInt(query, "limit", 1000000), 1, 10_000_000);
            SendJson(ctx, 200, FsService.Scan(path, string.IsNullOrEmpty(kind) ? "media" : kind, limit));
            return;
        }
        if (action == "roots" && req.HttpMethod == "GET")
        {
            SendJson(ctx, 200, FsService.Roots());
            return;
        }
        if (req.HttpMethod == "POST")
        {
            using var body = ReadJson(req);
            var path = body == null ? null : GetString(body.RootElement, "path");
            if (string.IsNullOrEmpty(path)) { SendText(ctx, 400, "path required"); return; }
            switch (action)
            {
                case "open":
                    SendJson(ctx, 200, new OkDto { Ok = FsService.OpenPath(path) });
                    return;
                case "reveal":
                    SendJson(ctx, 200, new OkDto { Ok = FsService.RevealPath(path) });
                    return;
                case "remember":
                    FsService.RememberFolder(path);
                    SendJson(ctx, 200, new OkDto { Ok = true });
                    return;
            }
        }
        SendText(ctx, 404, "unknown fs route");
    }

    private void HandleImages(HttpListenerContext ctx, string query)
    {
        var path = QueryValue(query, "path");
        if (string.IsNullOrEmpty(path)) { SendText(ctx, 400, "path required"); return; }
        bool recursive = QueryBool(query, "recursive");
        int offset = QueryInt(query, "offset", -1);
        int limit = Math.Clamp(QueryInt(query, "limit", 120), 1, 2000);
        var current = QueryValue(query, "current");
        if (current.Length == 0) current = null;
        bool refresh = QueryBool(query, "refresh");
        SendJson(ctx, 200, ImageScanService.Get(path, recursive, offset, limit, current, refresh));
    }

    private void HandleSiblingsApi(HttpListenerContext ctx, string query)
    {
        var path = QueryValue(query, "path");
        var kind = QueryValue(query, "kind");
        if (string.IsNullOrEmpty(kind)) kind = "media";
        SendJson(ctx, 200, FsService.Siblings(path, kind));
    }

    private void HandleMedia(HttpListenerContext ctx, string[] seg, HttpListenerRequest req, string query)
    {
        var action = seg.Length > 1 ? seg[1] : "";
        if (action == "progress")
        {
            if (req.HttpMethod == "GET")
            {
                var p = QueryValue(query, "path");
                if (!string.IsNullOrEmpty(p))
                {
                    var item = MediaStore.Instance.GetProgress(p);
                    if (item == null) SendJsonString(ctx, 200, "null");
                    else SendJson(ctx, 200, item);
                }
                else SendJson(ctx, 200, MediaStore.Instance.AllProgress());
                return;
            }
            if (req.HttpMethod == "POST")
            {
                using var body = ReadJson(req);
                if (body == null) { SendText(ctx, 400, "body required"); return; }
                var r = body.RootElement;
                var path = GetString(r, "path");
                if (string.IsNullOrEmpty(path)) { SendText(ctx, 400, "path required"); return; }
                double pos = GetDouble(r, "position");
                double dur = GetDouble(r, "duration");
                double scale = 1, rotation = 0;
                if (r.TryGetProperty("scale", out var sv) && sv.TryGetDouble(out var svd)) scale = svd;
                if (r.TryGetProperty("rotation", out var rv) && rv.TryGetDouble(out var rvd)) rotation = rvd;
                MediaStore.Instance.UpsertProgress(path, GetString(r, "kind") ?? "audio", pos, dur,
                    GetString(r, "title"), GetString(r, "artist"), scale, rotation);
                SendJson(ctx, 200, new OkDto { Ok = true });
                return;
            }
            if (req.HttpMethod == "DELETE")
            {
                var p = QueryValue(query, "path");
                if (!string.IsNullOrEmpty(p)) MediaStore.Instance.RemoveProgress(p);
                SendJson(ctx, 200, new OkDto { Ok = true });
                return;
            }
        }
        if (action == "playlists") { HandlePlaylists(ctx, seg, req); return; }
        SendText(ctx, 404, "unknown media route");
    }

    private void HandlePlaylists(HttpListenerContext ctx, string[] seg, HttpListenerRequest req)
    {
        var id = seg.Length > 2 ? seg[2] : "";
        if (req.HttpMethod == "GET")
        {
            if (string.IsNullOrEmpty(id)) { SendJson(ctx, 200, MediaStore.Instance.GetAllPlaylists()); return; }
            var pl = MediaStore.Instance.GetPlaylist(id);
            if (pl == null) { SendText(ctx, 404, "playlist not found"); return; }
            SendJson(ctx, 200, pl);
            return;
        }
        if (req.HttpMethod == "POST" && (string.IsNullOrEmpty(id) || id == "last"))
        {
            using var body = ReadJson(req);
            if (body == null) { SendText(ctx, 400, "body required"); return; }
            if (id == "last")
            {
                var lastId = GetString(body.RootElement, "id");
                MediaStore.Instance.SetLastPlaylist(lastId);
                SendJson(ctx, 200, new OkDto { Ok = true });
                return;
            }
            var rec = ReadPlaylist(body.RootElement, null);
            if (rec == null) { SendText(ctx, 400, "invalid playlist"); return; }
            var saved = MediaStore.Instance.SavePlaylist(rec);
            SendJson(ctx, 200, saved);
            return;
        }
        if (req.HttpMethod == "PUT" && !string.IsNullOrEmpty(id))
        {
            using var body = ReadJson(req);
            if (body == null) { SendText(ctx, 400, "body required"); return; }
            var existing = MediaStore.Instance.GetPlaylist(id);
            if (existing == null) { SendText(ctx, 404, "playlist not found"); return; }
            var rec = ReadPlaylist(body.RootElement, existing);
            if (rec == null) { SendText(ctx, 400, "invalid playlist"); return; }
            SendJson(ctx, 200, MediaStore.Instance.SavePlaylist(rec));
            return;
        }
        if (req.HttpMethod == "DELETE" && !string.IsNullOrEmpty(id))
        {
            SendJson(ctx, 200, new OkDto { Ok = MediaStore.Instance.DeletePlaylist(id) });
            return;
        }
        SendText(ctx, 405, "method");
    }

    private static PlaylistRecord? ReadPlaylist(JsonElement r, PlaylistRecord? existing)
    {
        var rec = existing ?? new PlaylistRecord();
        var name = GetString(r, "name");
        if (name != null) rec.Name = name.Trim().Length > 0 ? name.Trim() : rec.Name;
        var kind = GetString(r, "kind");
        if (kind is "audio" or "video") rec.Kind = kind;
        if (r.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            rec.Items = new List<string>();
            foreach (var it in items.EnumerateArray())
            {
                if (it.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(it.GetString()))
                {
                    rec.Items.Add(it.GetString()!);
                    if (rec.Items.Count >= 10000) break;
                }
            }
        }
        if (r.TryGetProperty("current", out var cur) && cur.TryGetInt32(out var c))
            rec.Current = Math.Max(0, c);
        return rec;
    }

    // ==================== 按键精灵 ====================

    private static MacroListDto MacroSnapshot()
    {
        var list = MacroStore.Instance.List();
        MacroEngine.Decorate(list);
        return new MacroListDto
        {
            Items = list,
            GlobalEnabled = MacroStore.Instance.GetGlobalEnabled(),
            Recording = MacroEngine.IsRecording,
            Running = MacroEngine.IsRunning,
        };
    }

    private void HandleMacros(HttpListenerContext ctx, string[] seg, HttpListenerRequest req, string query)
    {
        if (seg.Length >= 2)
        {
            switch (seg[1])
            {
                case "state" when req.HttpMethod == "GET":
                    SendJson(ctx, 200, MacroSnapshot());
                    return;
                case "stop" when req.HttpMethod == "POST":
                    MacroEngine.StopPlayback();
                    SendJson(ctx, 200, new OkDto { Ok = true });
                    return;
                case "record" when req.HttpMethod == "POST":
                    {
                        bool ok = seg.Length >= 3 && seg[2] == "stop"
                            ? MacroEngine.RequestStopRecording()
                            : MacroEngine.RequestStartRecording();
                        SendJson(ctx, ok ? 200 : 400, new OkDto
                        {
                            Ok = ok,
                            Error = ok ? null : "按键精灵未初始化（无界面模式不支持录制）",
                        });
                        return;
                    }
                case "global" when req.HttpMethod == "POST":
                    {
                        using var body = ReadJson(req);
                        if (body == null) { SendText(ctx, 400, "body required"); return; }
                        bool enabled = true;
                        if (body.RootElement.TryGetProperty("enabled", out var e))
                            enabled = e.ValueKind == JsonValueKind.True ||
                                      (e.ValueKind == JsonValueKind.Number && e.GetInt32() != 0);
                        MacroStore.Instance.SetGlobalEnabled(enabled);
                        MacroEngine.RequestReload();
                        SendJson(ctx, 200, MacroSnapshot());
                        return;
                    }
            }
        }

        if (seg.Length == 1)
        {
            if (req.HttpMethod == "GET") { SendJson(ctx, 200, MacroSnapshot()); return; }
            if (req.HttpMethod == "POST")
            {
                using var body = ReadJson(req);
                if (body == null) { SendText(ctx, 400, "body required"); return; }
                MacroRecord? rec;
                try { rec = JsonSerializer.Deserialize(body.RootElement, Info<MacroRecord>.I); }
                catch (JsonException) { rec = null; }
                if (rec == null) { SendText(ctx, 400, "invalid macro"); return; }
                var saved = MacroStore.Instance.Save(rec);
                MacroEngine.RequestReload();
                var all = MacroStore.Instance.List();
                MacroEngine.Decorate(all);
                var result = all.FirstOrDefault(x => x.Id == saved.Id) ?? saved;
                SendJson(ctx, 200, result);
                return;
            }
            SendText(ctx, 405, "method");
            return;
        }

        var id = seg[1];
        if (req.HttpMethod == "GET")
        {
            var rec = MacroStore.Instance.Get(id);
            if (rec == null) { SendText(ctx, 404, "macro not found"); return; }
            SendJson(ctx, 200, rec);
            return;
        }
        if (req.HttpMethod == "DELETE")
        {
            var ok = MacroStore.Instance.Delete(id);
            MacroEngine.RequestReload();
            SendJson(ctx, ok ? 200 : 404, new OkDto { Ok = ok });
            return;
        }
        if (req.HttpMethod == "POST" && seg.Length >= 3)
        {
            switch (seg[2])
            {
                case "run":
                    {
                        int? leadIn = null;
                        if (int.TryParse(QueryValue(query, "leadIn"), out var li)) leadIn = Math.Clamp(li, 0, 60_000);
                        var ok = MacroEngine.RunTest(id, leadIn);
                        SendJson(ctx, ok ? 200 : 409, new OkDto
                        {
                            Ok = ok,
                            Error = ok ? null : "已有宏正在执行，或按键精灵已暂停",
                        });
                        return;
                    }
                case "enabled":
                    {
                        using var body = ReadJson(req);
                        bool enabled = true;
                        if (body != null && body.RootElement.TryGetProperty("enabled", out var e))
                            enabled = e.ValueKind == JsonValueKind.True ||
                                      (e.ValueKind == JsonValueKind.Number && e.GetInt32() != 0);
                        var ok = MacroStore.Instance.SetEnabled(id, enabled);
                        MacroEngine.RequestReload();
                        SendJson(ctx, ok ? 200 : 404, new OkDto { Ok = ok });
                        return;
                    }
            }
        }
        SendText(ctx, 405, "method");
    }

    // ==================== 媒体库收藏夹 ====================

    private void HandleFavorites(HttpListenerContext ctx, string[] seg, HttpListenerRequest req, string query)
    {
        if (req.HttpMethod == "GET")
        {
            SendJson(ctx, 200, FavoriteStore.Instance.List());
            return;
        }
        if (req.HttpMethod == "POST")
        {
            using var body = ReadJson(req);
            if (body == null) { SendText(ctx, 400, "body required"); return; }
            var path = GetString(body.RootElement, "path");
            if (string.IsNullOrWhiteSpace(path)) { SendText(ctx, 400, "path required"); return; }
            try
            {
                FavoriteStore.Instance.Add(path, GetString(body.RootElement, "name"));
                SendJson(ctx, 200, FavoriteStore.Instance.List());
            }
            catch (Exception ex)
            {
                SendJson(ctx, 400, new OkDto { Ok = false, Error = ex.Message });
            }
            return;
        }
        if (req.HttpMethod == "DELETE")
        {
            var path = QueryValue(query, "path");
            var ok = !string.IsNullOrWhiteSpace(path) && FavoriteStore.Instance.Remove(path);
            SendJson(ctx, ok ? 200 : 404, new OkDto { Ok = ok });
            return;
        }
        SendText(ctx, 405, "method");
    }

    // ==================== 工具箱 ====================

    private void HandleTools(HttpListenerContext ctx, string[] seg, HttpListenerRequest req, string query)
    {
        var action = seg.Length > 1 ? seg[1] : "";
        switch (action)
        {
            case "system":
                SendJson(ctx, 200, ToolsService.SystemInfo());
                return;
            case "hash":
                {
                    var path = QueryValue(query, "path");
                    var algo = QueryValue(query, "algo");
                    SendJson(ctx, 200, ToolsService.HashFile(path, string.IsNullOrEmpty(algo) ? "sha256" : algo));
                    return;
                }
            case "processes":
                {
                    if (seg.Length >= 4 && seg[3] == "kill" && req.HttpMethod == "POST")
                    {
                        string err = "";
                        var ok = int.TryParse(seg[2], out var pid) && ToolsService.KillProcess(pid, out err);
                        SendJson(ctx, ok ? 200 : 400, new OkDto { Ok = ok, Error = ok ? null : err });
                        return;
                    }
                    SendJson(ctx, 200, ToolsService.Processes(QueryValue(query, "q")));
                    return;
                }
            case "ports":
                {
                    if (seg.Length >= 4 && seg[3] == "kill" && req.HttpMethod == "POST")
                    {
                        string err = "";
                        var ok = int.TryParse(seg[2], out var port) && ToolsService.KillByPort(port, out err);
                        SendJson(ctx, ok ? 200 : 400, new OkDto { Ok = ok, Error = ok ? null : err });
                        return;
                    }
                    SendJson(ctx, 200, ToolsService.Ports());
                    return;
                }
            case "screenshot":
                {
                    var file = ToolsService.ScreenshotFull(out var err);
                    SendJson(ctx, file == null ? 400 : 200, new OkDto { Ok = file != null, Path = file, Error = err });
                    return;
                }
            case "power":
                {
                    using var body = ReadJson(req);
                    var powerAction = body == null ? null : GetString(body.RootElement, "action");
                    int delaySeconds = 0;
                    if (body != null && body.RootElement.TryGetProperty("delaySeconds", out var ds) && ds.TryGetInt32(out var dsv))
                        delaySeconds = dsv;
                    string err = "";
                    var ok = !string.IsNullOrEmpty(powerAction) && ToolsService.Power(powerAction!, delaySeconds, out err);
                    SendJson(ctx, ok ? 200 : 400, new OkDto { Ok = ok, Error = ok ? null : err });
                    return;
                }
            case "archive":
                {
                    using var body = ReadJson(req);
                    if (body == null) { SendText(ctx, 400, "body required"); return; }
                    var root = body.RootElement;
                    if (seg.Length >= 3 && seg[2] == "compress")
                    {
                        var folder = GetString(root, "folder");
                        var zip = GetString(root, "zip");
                        string err = "";
                        var ok = !string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(zip) &&
                                 ToolsService.CompressFolder(folder!, zip!, out err);
                        SendJson(ctx, ok ? 200 : 400, new OkDto { Ok = ok, Path = zip, Error = ok ? null : err });
                        return;
                    }
                    if (seg.Length >= 3 && seg[2] == "extract")
                    {
                        var zip = GetString(root, "zip");
                        var dest = GetString(root, "dest");
                        string err = "";
                        var ok = !string.IsNullOrEmpty(zip) && !string.IsNullOrEmpty(dest) &&
                                 ToolsService.ExtractZip(zip!, dest!, out err);
                        SendJson(ctx, ok ? 200 : 400, new OkDto { Ok = ok, Path = dest, Error = ok ? null : err });
                        return;
                    }
                    SendText(ctx, 404, "unknown archive route");
                    return;
                }
        }
        SendText(ctx, 404, "unknown tools route");
    }

    // ==================== 网站工具 ====================

    private void HandleSites(HttpListenerContext ctx, string[] seg, HttpListenerRequest req, string query)
    {
        var action = seg.Length > 1 ? seg[1] : "";
        if (action == "hosts")
        {
            var id = seg.Length > 2 ? seg[2] : "";
            if (req.HttpMethod == "GET")
            {
                if (string.IsNullOrEmpty(id))
                    SendJson(ctx, 200, new SiteHostList { Items = SiteHostService.Instance.List() });
                else
                {
                    var item = SiteHostService.Instance.List().FirstOrDefault(x => x.Id == id);
                    if (item == null) SendText(ctx, 404, "host not found");
                    else SendJson(ctx, 200, item);
                }
                return;
            }
            if (req.HttpMethod == "POST")
            {
                using var body = ReadJson(req);
                var path = body == null ? null : GetString(body.RootElement, "path");
                if (string.IsNullOrWhiteSpace(path)) { SendText(ctx, 400, "path required"); return; }
                try { SendJson(ctx, 200, SiteHostService.Instance.Start(path)); }
                catch (Exception ex) { SendJson(ctx, 400, new OkDto { Ok = false, Error = ex.Message }); }
                return;
            }
            if (req.HttpMethod == "DELETE")
            {
                var ok = !string.IsNullOrEmpty(id) && SiteHostService.Instance.Stop(id);
                SendJson(ctx, ok ? 200 : 404, new OkDto { Ok = ok });
                return;
            }
            SendText(ctx, 405, "method");
            return;
        }
        if (action == "fetch" && req.HttpMethod == "POST")
        {
            using var body = ReadJson(req);
            if (body == null) { SendText(ctx, 400, "body required"); return; }
            SiteFetchRequest? request = null;
            try { request = JsonSerializer.Deserialize(body.RootElement, Info<SiteFetchRequest>.I); } catch { }
            if (request == null) { SendText(ctx, 400, "invalid request"); return; }
            SendJson(ctx, 200, WebPageSaver.Fetch(request));
            return;
        }
        if (action == "download" && req.HttpMethod == "POST")
        {
            using var body = ReadJson(req);
            if (body == null) { SendText(ctx, 400, "body required"); return; }
            SiteDownloadRequest? request = null;
            try { request = JsonSerializer.Deserialize(body.RootElement, Info<SiteDownloadRequest>.I); } catch { }
            if (request == null || string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.Folder))
            {
                SendText(ctx, 400, "url and folder required");
                return;
            }
            var result = (request.MaxPages > 1 || request.MaxDepth > 0)
                ? WebPageSaver.SaveSite(request.Url, request.Folder, request.MaxPages, request.MaxDepth)
                : WebPageSaver.SavePage(request.Url, request.Folder);
            SendJson(ctx, result.Ok ? 200 : 400, result);
            return;
        }
        SendText(ctx, 404, "unknown sites route");
    }

    private void HandleAudio(HttpListenerContext ctx, string[] seg, string query)    {
        var action = seg.Length > 1 ? seg[1] : "";
        var path = QueryValue(query, "path");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { SendText(ctx, 404, "file not found"); return; }
        if (action == "meta")
        {
            SendJson(ctx, 200, TagService.ReadMeta(path));
            return;
        }
        if (action == "cover")
        {
            var cover = TagService.ReadCover(path);
            if (cover == null) { SendText(ctx, 404, "no cover"); return; }
            ServeBytesRange(ctx, cover.Value.Data, cover.Value.Mime, "public, max-age=3600");
            return;
        }
        SendText(ctx, 404, "unknown audio route");
    }

    private void HandleLyricsApi(HttpListenerContext ctx, string query)
    {
        var path = QueryValue(query, "path");
        var title = QueryValue(query, "title");
        var artist = QueryValue(query, "artist");
        if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(title))
        {
            SendText(ctx, 400, "path or title required");
            return;
        }
        var dto = !string.IsNullOrEmpty(path)
            ? LyricsService.Get(path, string.IsNullOrEmpty(title) ? null : title, string.IsNullOrEmpty(artist) ? null : artist)
            : LyricsService.Get(title + ".mp3", title, artist);
        SendJson(ctx, 200, dto);
    }

    private void HandleZipApi(HttpListenerContext ctx, string[] seg, HttpListenerRequest req, string query)
    {
        var action = seg.Length > 1 ? seg[1] : "";
        if (action == "list" && req.HttpMethod == "GET")
        {
            var path = QueryValue(query, "path");
            if (!File.Exists(path)) { SendText(ctx, 404, "zip not found"); return; }
            SendJson(ctx, 200, ZipService.List(path, QueryValue(query, "prefix")));
            return;
        }
        if (action == "entries" && req.HttpMethod == "GET")
        {
            var path = QueryValue(query, "path");
            if (!File.Exists(path)) { SendText(ctx, 404, "zip not found"); return; }
            SendJson(ctx, 200, ZipService.List(path, QueryValue(query, "prefix")).Items);
            return;
        }
        if (action == "entry" && req.HttpMethod == "GET")
        {
            var path = QueryValue(query, "path");
            var entry = QueryValue(query, "entry");
            var data = ZipService.ReadEntry(path, entry, out var mime);
            if (data == null) { SendText(ctx, 404, "entry not found"); return; }
            ServeBytesRange(ctx, data, mime, "no-cache");
            return;
        }
        if (action == "extract" && req.HttpMethod == "POST")
        {
            using var body = ReadJson(req);
            var path = body == null ? null : GetString(body.RootElement, "path");
            var entry = body == null ? null : GetString(body.RootElement, "entry");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(entry)) { SendText(ctx, 400, "path/entry required"); return; }
            var file = ZipService.ExtractToTemp(path, entry, out var err);
            SendJson(ctx, file == null ? 400 : 200, new TempFileDto { Ok = file != null, Path = file, Error = err });
            return;
        }
        if (action == "last")
        {
            var z = QueryValue(query, "path");
            if (req.HttpMethod == "GET")
            {
                SendJsonString(ctx, 200, "{\"path\":" + JsonStr(z) + ",\"entry\":" +
                    JsonStr(MediaStore.Instance.GetZipLast(z)) + "}");
                return;
            }
            if (req.HttpMethod == "POST")
            {
                using var body = ReadJson(req);
                var entry = body == null ? null : GetString(body.RootElement, "entry");
                MediaStore.Instance.SetZipLast(z, entry);
                SendJson(ctx, 200, new OkDto { Ok = true });
                return;
            }
        }
        SendText(ctx, 404, "unknown zip route");
    }

    // ==================== 发送工具 ====================

    private static void SendJson<T>(HttpListenerContext ctx, int status, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Info<T>.I);
        WriteBody(ctx, status, bytes, "application/json; charset=utf-8", true, null, "no-store");
    }

    private static void SendJsonString(HttpListenerContext ctx, int status, string json)
    {
        WriteBody(ctx, status, Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", true, null, "no-store");
    }

    private static void WriteJsonWithEtag<T>(HttpListenerContext ctx, int status, T value, string etag)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Info<T>.I);
        WriteBody(ctx, status, bytes, "application/json; charset=utf-8", true, etag, "no-cache");
    }

    private static void SendText(HttpListenerContext ctx, int status, string text)
        => WriteBody(ctx, status, Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", true, null, "no-store");

    private static void SendHtml(HttpListenerContext ctx, int status, string html)
        => WriteBody(ctx, status, Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", true, null, "no-store");

    private static bool AcceptsGzip(HttpListenerRequest req)
    {
        var ae = req.Headers["Accept-Encoding"];
        return !string.IsNullOrEmpty(ae) && ae.Contains("gzip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompressible(string contentType) =>
        contentType.Contains("json") || contentType.Contains("text") ||
        contentType.Contains("javascript") || contentType.Contains("xml") || contentType.Contains("svg");

    private static void WriteBody(HttpListenerContext ctx, int status, byte[] body, string contentType,
        bool gzipAllowed, string? etag, string? cacheControl)
    {
        var res = ctx.Response;
        var req = ctx.Request;
        try
        {
            res.StatusCode = status;
            res.ContentType = contentType;
            res.Headers["Access-Control-Allow-Origin"] = "*";
            if (!string.IsNullOrEmpty(cacheControl)) res.Headers["Cache-Control"] = cacheControl;
            if (!string.IsNullOrEmpty(etag))
            {
                res.Headers["ETag"] = etag;
                var inm = req.Headers["If-None-Match"];
                if (!string.IsNullOrEmpty(inm) && inm.Contains(etag, StringComparison.Ordinal))
                {
                    res.StatusCode = 304;
                    res.OutputStream.Close();
                    return;
                }
            }

            byte[] outBytes = body;
            if (gzipAllowed && body.Length > 1024 && AcceptsGzip(req) && IsCompressible(contentType))
            {
                using var ms = new MemoryStream(body.Length / 2 + 64);
                using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                    gz.Write(body, 0, body.Length);
                outBytes = ms.ToArray();
                res.Headers["Content-Encoding"] = "gzip";
                res.Headers["Vary"] = "Accept-Encoding";
            }
            res.ContentLength64 = outBytes.Length;
            if (req.HttpMethod != "HEAD" && outBytes.Length > 0)
                res.OutputStream.Write(outBytes, 0, outBytes.Length);
            res.OutputStream.Close();
        }
        catch { try { res.Abort(); } catch { } }
    }

    private static void TryError(HttpListenerContext ctx, int status, string message)
    {
        try { SendJson(ctx, status, new OkDto { Ok = false, Error = message }); } catch { }
    }

    private static void Redirect(HttpListenerContext ctx, string url)
    {
        try
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers[HttpResponseHeader.Location] = url;
            ctx.Response.OutputStream.Close();
        }
        catch { }
    }

    // ==================== 文件流（Range） ====================

    private static void ServeFile(HttpListenerContext ctx, string path, string cacheControl, string? contentType = null)
    {
        var res = ctx.Response;
        var req = ctx.Request;
        FileStream? fs = null;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) { SendText(ctx, 404, "not found"); return; }
            long len = fi.Length;
            string etag = "W/\"" + fi.LastWriteTimeUtc.Ticks.ToString("x") + "-" + len.ToString("x") + "\"";
            res.Headers["ETag"] = etag;
            res.Headers["Accept-Ranges"] = "bytes";
            res.Headers["Cache-Control"] = cacheControl;
            var inm = req.Headers["If-None-Match"];
            if (!string.IsNullOrEmpty(inm) && inm.Contains(etag, StringComparison.Ordinal))
            {
                res.StatusCode = 304;
                res.OutputStream.Close();
                return;
            }
            res.ContentType = contentType ?? FileKinds.MimeOf(path);

            var range = req.Headers["Range"];
            var (start, end, isRange) = ParseRange(range, len);
            if (isRange && (start > end || start >= len))
            {
                res.StatusCode = 416;
                res.Headers["Content-Range"] = $"bytes */{len}";
                res.OutputStream.Close();
                return;
            }
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (isRange)
            {
                res.StatusCode = 206;
                res.Headers["Content-Range"] = $"bytes {start}-{end}/{len}";
                res.ContentLength64 = end - start + 1;
                fs.Seek(start, SeekOrigin.Begin);
                if (req.HttpMethod != "HEAD") CopyRange(fs, res.OutputStream, end - start + 1);
            }
            else
            {
                res.StatusCode = 200;
                res.ContentLength64 = len;
                if (req.HttpMethod != "HEAD") fs.CopyTo(res.OutputStream, 81920);
            }
            res.OutputStream.Close();
        }
        catch (IOException) { }
        catch (HttpListenerException) { }
        catch (Exception ex)
        {
            Log.Warn("发送文件失败 " + path + ": " + ex.Message);
            try { res.Abort(); } catch { }
        }
        finally { fs?.Dispose(); }
    }

    private static void ServeBytesRange(HttpListenerContext ctx, byte[] data, string mime, string cacheControl)
    {
        var res = ctx.Response;
        try
        {
            res.ContentType = mime;
            res.Headers["Accept-Ranges"] = "bytes";
            res.Headers["Cache-Control"] = cacheControl;
            res.Headers["Access-Control-Allow-Origin"] = "*";
            var (start, end, isRange) = ParseRange(ctx.Request.Headers["Range"], data.Length);
            if (isRange && (start > end || start >= data.Length))
            {
                res.StatusCode = 416;
                res.Headers["Content-Range"] = $"bytes */{data.Length}";
                res.OutputStream.Close();
                return;
            }
            if (isRange)
            {
                res.StatusCode = 206;
                res.Headers["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
                res.ContentLength64 = end - start + 1;
                if (ctx.Request.HttpMethod != "HEAD")
                    res.OutputStream.Write(data, (int)start, (int)(end - start + 1));
            }
            else
            {
                res.StatusCode = 200;
                res.ContentLength64 = data.Length;
                if (ctx.Request.HttpMethod != "HEAD") res.OutputStream.Write(data, 0, data.Length);
            }
            res.OutputStream.Close();
        }
        catch { try { res.Abort(); } catch { } }
    }

    private static (long start, long end, bool isRange) ParseRange(string? range, long len)
    {
        if (string.IsNullOrEmpty(range) || len <= 0) return (0, Math.Max(0, len - 1), false);
        var m = System.Text.RegularExpressions.Regex.Match(range, @"bytes=(\d*)-(\d*)");
        if (!m.Success) return (0, len - 1, false);
        bool hasStart = m.Groups[1].Success && m.Groups[1].Value.Length > 0;
        bool hasEnd = m.Groups[2].Success && m.Groups[2].Value.Length > 0;
        if (!hasStart && !hasEnd) return (0, len - 1, false);
        long start, end;
        if (!hasStart)
        {
            long suffix = long.TryParse(m.Groups[2].Value, out var sfx) ? sfx : 0;
            if (suffix <= 0) return (0, len - 1, false);
            start = Math.Max(0, len - suffix);
            end = len - 1;
        }
        else
        {
            long.TryParse(m.Groups[1].Value, out start);
            end = hasEnd && long.TryParse(m.Groups[2].Value, out var e2) ? e2 : len - 1;
            if (end >= len) end = len - 1;
        }
        if (start < 0) start = 0;
        return (start, end, true);
    }

    private static void CopyRange(Stream src, Stream dst, long count)
    {
        var buf = new byte[81920];
        while (count > 0)
        {
            int want = (int)Math.Min(buf.Length, count);
            int n = src.Read(buf, 0, want);
            if (n <= 0) break;
            dst.Write(buf, 0, n);
            count -= n;
        }
    }

    // ==================== 工具 ====================

    private static JsonDocument? ReadJson(HttpListenerRequest req)
    {
        try
        {
            using var ms = new MemoryStream();
            req.InputStream.CopyTo(ms, 81920);
            if (ms.Length == 0) return null;
            return JsonDocument.Parse(ms.ToArray());
        }
        catch { return null; }
    }

    private static string? GetString(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        return e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static double GetDouble(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var d)) return d;
        return 0;
    }

    private static long GetLong(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return 0;
    }

    private static string QueryValue(string query, string name)
    {
        if (string.IsNullOrEmpty(query)) return "";
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 0) continue;
            try
            {
                if (Uri.UnescapeDataString(kv[0].Replace('+', ' ')) == name)
                    return kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
            }
            catch { }
        }
        return "";
    }

    private static int QueryInt(string query, string name, int fallback) =>
        int.TryParse(QueryValue(query, name), out var n) ? n : fallback;

    private static bool QueryBool(string query, string name)
    {
        var v = QueryValue(query, name);
        return v is "1" or "true" or "yes" or "on";
    }

    private static string HashShort(string text)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    private static string JsonStr(string? value) => value == null ? "null" : JsonSerializer.Serialize(value!, Info<string>.I);
}
