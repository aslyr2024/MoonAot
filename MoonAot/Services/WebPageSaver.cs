using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>
/// 网站工具后端：HTTP 调试、单页离线保存（HTML + 同页引用的静态资源）。
/// </summary>
public static class WebPageSaver
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly HashSet<string> AssetExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".mjs", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".ico", ".bmp", ".avif",
        ".woff", ".woff2", ".ttf", ".otf", ".mp4", ".webm", ".mp3", ".wav", ".json", ".xml", ".wasm", ".map",
    };

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36");
        return client;
    }

    public static SiteFetchResult Fetch(SiteFetchRequest req)
    {
        var result = new SiteFetchResult();
        try
        {
            if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                result.Error = "请输入 http/https 地址";
                return result;
            }
            using var msg = new HttpRequestMessage(new HttpMethod(
                string.IsNullOrWhiteSpace(req.Method) ? "GET" : req.Method.ToUpperInvariant()), uri);
            if (req.Headers != null)
                foreach (var (k, v) in req.Headers)
                    if (!string.IsNullOrWhiteSpace(k) && k != "Content-Type") msg.Headers.TryAddWithoutValidation(k, v);
            if (!string.IsNullOrEmpty(req.Body))
            {
                msg.Content = new StringContent(req.Body, Encoding.UTF8,
                    req.Headers != null && req.Headers.TryGetValue("Content-Type", out var ct) ? ct : "application/json");
            }
            using var resp = Http.Send(msg, HttpCompletionOption.ResponseContentRead);
            result.Status = (int)resp.StatusCode;
            result.ContentType = resp.Content.Headers.ContentType?.ToString();
            foreach (var h in resp.Headers) result.Headers[h.Key] = string.Join(", ", h.Value);
            foreach (var h in resp.Content.Headers) result.Headers[h.Key] = string.Join(", ", h.Value);
            var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            result.Length = bytes.Length;
            var text = Decode(bytes, resp.Content.Headers.ContentType?.CharSet);
            result.Body = text.Length > 200_000 ? text[..200_000] + "\n\n…（内容已截断）" : text;
            result.Ok = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        return result;
    }

    public static SiteDownloadResult SavePage(string url, string folder)
    {
        var result = new SiteDownloadResult();
        try
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                result.Error = "请输入 http/https 地址";
                return result;
            }
            var root = Path.GetFullPath(folder.Trim().Trim('"'));
            Directory.CreateDirectory(root);
            var assetsDir = Path.Combine(root, "assets");
            Directory.CreateDirectory(assetsDir);

            byte[] htmlBytes;
            string? charset;
            using (var resp = Http.GetAsync(uri).GetAwaiter().GetResult())
            {
                if (!resp.IsSuccessStatusCode)
                {
                    result.Error = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                    return result;
                }
                charset = resp.Content.Headers.ContentType?.CharSet;
                htmlBytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
            var html = Decode(htmlBytes, charset);
            long bytes = htmlBytes.Length;
            int files = 0;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var links = new Regex(@"(?<pre>\b(?:href|src)\s*=\s*[""'])(?<u>[^""']+)(?<post>[""'])",
                RegexOptions.IgnoreCase);
            int assetCount = 0;
            long assetBytes = 0;
            foreach (Match m in links.Matches(html))
            {
                var raw = m.Groups["u"].Value;
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith('#') || raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                    raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (map.ContainsKey(raw)) continue;
                if (!Uri.TryCreate(uri, raw, out var abs) || abs.Scheme is not ("http" or "https")) continue;
                var ext = Path.GetExtension(abs.AbsolutePath);
                if (!AssetExt.Contains(ext)) continue;
                if (assetCount >= 200 || assetBytes > 150L * 1024 * 1024) break;
                try
                {
                    using var resp = Http.GetAsync(abs).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) continue;
                    var data = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    if (data.Length > 40L * 1024 * 1024) continue;
                    var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(abs.AbsoluteUri)))[..10].ToLowerInvariant();
                    var name = Sanitize(Path.GetFileName(abs.AbsolutePath));
                    if (string.IsNullOrEmpty(name)) name = "file" + ext;
                    var local = hash + "_" + name;
                    File.WriteAllBytes(Path.Combine(assetsDir, local), data);
                    map[raw] = "assets/" + local;
                    assetCount++;
                    assetBytes += data.Length;
                    bytes += data.Length;
                    files++;
                }
                catch { }
            }

            html = links.Replace(html, m =>
            {
                var raw = m.Groups["u"].Value;
                return map.TryGetValue(raw, out var local)
                    ? m.Groups["pre"].Value + local + m.Groups["post"].Value
                    : m.Value;
            });

            var entry = EntryName(uri);
            var entryPath = Path.Combine(root, entry);
            File.WriteAllText(entryPath, html, new UTF8Encoding(false));
            files++;
            result.Ok = true;
            result.Files = files;
            result.Bytes = bytes;
            result.Entry = entry;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        return result;
    }

    /// <summary>递归抓取同源页面（最多 maxPages 页）并把静态资源改写为本地路径。</summary>
    public static SiteDownloadResult SaveSite(string url, string folder, int maxPages, int maxDepth)
    {
        var result = new SiteDownloadResult();
        try
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var start) || start.Scheme is not ("http" or "https"))
            {
                result.Error = "请输入 http/https 地址";
                return result;
            }
            maxPages = Math.Clamp(maxPages, 1, 200);
            maxDepth = Math.Clamp(maxDepth, 0, 6);
            if (maxPages <= 1 && maxDepth == 0) return SavePage(url, folder);

            var root = Path.GetFullPath(folder.Trim().Trim('"'));
            var assetsDir = Path.Combine(root, "assets");
            Directory.CreateDirectory(assetsDir);

            var pages = new List<(string Local, string Html, Uri Uri)>();
            var pageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var assetMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(Uri Uri, int Depth)>();
            queue.Enqueue((start, 0));
            long totalBytes = 0;
            int assetFiles = 0;

            var hrefRegex = new Regex(@"\bhref\s*=\s*[""'](?<u>[^""']+)", RegexOptions.IgnoreCase);

            while (queue.Count > 0 && pages.Count < maxPages)
            {
                var (uri, depth) = queue.Dequeue();
                var canonical = StripFragment(uri);
                if (!seen.Add(canonical)) continue;

                string html;
                long pageBytes;
                try
                {
                    using var resp = Http.GetAsync(uri).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) continue;
                    var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    if (bytes.Length > 5 * 1024 * 1024) bytes = bytes.AsSpan(0, 5 * 1024 * 1024).ToArray();
                    html = Decode(bytes, resp.Content.Headers.ContentType?.CharSet);
                    pageBytes = bytes.Length;
                }
                catch { continue; }

                var local = pages.Count == 0 ? EntryName(uri) : PageLocal(uri, pages.Count, used);
                used.Add(local);
                pageMap[canonical] = local;

                // 收集同源页面链接
                if (depth < maxDepth && pages.Count + queue.Count < maxPages)
                {
                    foreach (Match m in hrefRegex.Matches(html))
                    {
                        var raw = m.Groups["u"].Value.Trim();
                        if (raw.Length == 0 || raw.StartsWith('#') || raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                            raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!Uri.TryCreate(uri, raw, out var abs)) continue;
                        if (!SameOrigin(start, abs)) continue;
                        var ext = Path.GetExtension(abs.AbsolutePath);
                        if (AssetExt.Contains(ext)) continue;
                        var key = StripFragment(abs);
                        if (seen.Contains(key)) continue;
                        queue.Enqueue((abs, depth + 1));
                        if (pages.Count + queue.Count >= maxPages) break;
                    }
                }

                html = RewriteAssets(html, uri, assetsDir, assetMap, ref totalBytes, ref assetFiles, 1000);
                pages.Add((local, html, uri));
                totalBytes += pageBytes;
            }

            // 页面链接改写到本地页面（第二轮，保证目标页已映射）
            var written = new List<string>();
            foreach (var (local, html, pageUri) in pages)
            {
                var rewritten = hrefRegex.Replace(html, m =>
                {
                    var raw = m.Groups["u"].Value.Trim();
                    if (raw.Length == 0 || raw.StartsWith('#')) return m.Value;
                    if (!Uri.TryCreate(pageUri, raw, out var abs)) return m.Value;
                    if (!pageMap.TryGetValue(StripFragment(abs), out var target)) return m.Value;
                    var rel = RelativeLocal(local, target);
                    return "href=\"" + rel + "\"";
                });
                var full = Path.Combine(root, local.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, rewritten, new UTF8Encoding(false));
                if (written.Count < 100) written.Add(local);
            }

            result.Ok = true;
            result.Files = pages.Count + assetFiles;
            result.Pages = pages.Count;
            result.Bytes = totalBytes;
            result.Entry = pages.Count > 0 ? pages[0].Local : null;
            result.Written = written;
        }
        catch (Exception ex) { result.Error = ex.Message; }
        return result;
    }

    private static string StripFragment(Uri uri)
    {
        var b = new UriBuilder(uri) { Fragment = "" };
        return b.Uri.AbsoluteUri;
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        a.Scheme.Equals(b.Scheme, StringComparison.OrdinalIgnoreCase) &&
        a.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase) &&
        a.Port == b.Port;

    private static string PageLocal(Uri uri, int index, HashSet<string> used)
    {
        var name = uri.AbsolutePath.Trim('/').Replace('/', '_');
        if (name.Length == 0) name = "page";
        name = Sanitize(name);
        if (name.Length > 70) name = name[..70];
        if (Uri.UnescapeDataString(name) != name) name = Sanitize(Uri.UnescapeDataString(name));
        if (Path.GetExtension(name).Length == 0) name += ".html";
        var candidate = "pages/" + name;
        int n = 2;
        while (used.Contains(candidate)) candidate = "pages/" + Path.GetFileNameWithoutExtension(name) + "_" + (n++) + ".html";
        return candidate;
    }

    private static string RelativeLocal(string fromLocal, string toLocal)
    {
        var fromDir = Path.GetDirectoryName(fromLocal.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        var rel = Path.GetRelativePath(string.IsNullOrEmpty(fromDir) ? "." : fromDir, toLocal.Replace('/', Path.DirectorySeparatorChar));
        return rel.Replace('\\', '/');
    }

    private static string RewriteAssets(string html, Uri baseUri, string assetsDir, Dictionary<string, string> map,
        ref long totalBytes, ref int files, int maxFiles)
    {
        var links = new Regex(@"(?<pre>\b(?:href|src)\s*=\s*[""'])(?<u>[^""']+)(?<post>[""'])", RegexOptions.IgnoreCase);
        foreach (Match m in links.Matches(html))
        {
            var raw = m.Groups["u"].Value.Trim();
            if (raw.Length == 0 || raw.StartsWith('#') || raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Uri.TryCreate(baseUri, raw, out var abs) || abs.Scheme is not ("http" or "https")) continue;
            var ext = Path.GetExtension(abs.AbsolutePath);
            if (!AssetExt.Contains(ext)) continue;
            var absKey = "abs:" + abs.AbsoluteUri;
            if (map.TryGetValue(absKey, out var existing)) { map[raw] = existing; continue; }
            if (files >= maxFiles || totalBytes > 300L * 1024 * 1024) break;
            try
            {
                using var resp = Http.GetAsync(abs).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) continue;
                var data = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                if (data.Length > 40L * 1024 * 1024) continue;
                var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(abs.AbsoluteUri)))[..10].ToLowerInvariant();
                var name = Sanitize(Path.GetFileName(abs.AbsolutePath));
                if (string.IsNullOrEmpty(name)) name = "file" + ext;
                var local = "assets/" + hash + "_" + name;
                File.WriteAllBytes(Path.Combine(assetsDir, hash + "_" + name), data);
                map[raw] = local;
                map[absKey] = local;
                totalBytes += data.Length;
                files++;
            }
            catch { }
        }
        return links.Replace(html, m =>
        {
            var raw = m.Groups["u"].Value.Trim();
            if (map.TryGetValue(raw, out var local)) return m.Groups["pre"].Value + local + m.Groups["post"].Value;
            if (Uri.TryCreate(baseUri, raw, out var abs) && map.TryGetValue("abs:" + abs.AbsoluteUri, out var l2))
                return m.Groups["pre"].Value + l2 + m.Groups["post"].Value;
            return m.Value;
        });
    }

    private static string EntryName(Uri uri)
    {
        var name = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(name)) name = "index.html";
        if (Path.GetExtension(name).Length == 0) name += ".html";
        return Sanitize(name);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        var s = sb.ToString().Trim();
        return s.Length > 120 ? s[..120] : s;
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes); } catch { }
        }
        return Encoding.UTF8.GetString(bytes);
    }
}
