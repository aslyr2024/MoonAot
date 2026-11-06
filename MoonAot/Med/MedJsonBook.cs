using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MoonAot.Services;

namespace MoonAot.Med;

/// <summary>
/// PaddleOCR-VL 导出的 JSON 图书导入器（通用格式）。
/// 每页：prunedResult.parsing_res_list（块） + markdown.text（正文） + markdown.images（图片相对路径 → 远程 URL）。
/// 图片下载到本地 data/medical/jsonbooks/{bookId}/imgs/，章节 HTML 落到 chapters/，
/// 之后与 EPUB 共用同一套 FTS5 / 概念对齐 / 双链 / 章节精读管线。
/// 原始 JSON 只读，不修改。
/// </summary>
public static class MedJsonBook
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Regex ImgRe = new(
        @"(<img\b[^>]*?\bsrc\s*=\s*)(?<q>['""])(?<src>.*?)\k<q>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SplitRe = new(@"\n{2,}", RegexOptions.Compiled);
    private static readonly Regex HeadingRe = new(@"^#{1,6}\s", RegexOptions.Compiled);

    public static string BooksRoot => Path.Combine(MedDatabase.Instance.DataDir, "medical", "jsonbooks");

    public static string BookDir(string bookId) => Path.Combine(BooksRoot, bookId);

    /// <summary>把章节里的相对图片路径解析成本地文件，阻止目录穿越。</summary>
    public static string? ResolveImage(string bookId, string rel)
    {
        var dir = Path.GetFullPath(BookDir(bookId));
        var safe = MedText.ResolveAssetPath("", rel ?? "");
        if (safe.Length == 0 || safe.Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(dir, safe.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return null;
        if (File.Exists(full)) return full;
        // 兼容早期导入：图片曾落到 <bookId>/imgs/imgs/<rel>（下载时多拼了一层目录）
        var legacy = Path.GetFullPath(Path.Combine(dir, "imgs", safe.Replace('/', Path.DirectorySeparatorChar)));
        if (legacy.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && File.Exists(legacy)) return legacy;
        return null;
    }

    public static MedBookDraft Parse(string path, string bookId, Action<int, int, string>? progress = null)
    {
        var draft = new MedBookDraft
        {
            Title = Path.GetFileNameWithoutExtension(path).Replace("_by_PaddleOCR-VL-1.6", ""),
            Volume = "",
        };
        var dir = BookDir(bookId);
        var imgDir = Path.Combine(dir, "imgs");
        var chapterDir = Path.Combine(dir, "chapters");
        Directory.CreateDirectory(imgDir);
        Directory.CreateDirectory(chapterDir);

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("不是 PaddleOCR-VL 页面数组 JSON");
        var pages = doc.RootElement.EnumerateArray().ToList();
        draft.DocCount = pages.Count;

        // 1) 收集全书远程图片并并发下载（同相对路径只下一次，已存在直接跳过，支持断点续传）
        var downloads = new List<(string Url, string Dest)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            var images = Prop(Prop(page, "markdown"), "images");
            if (images.ValueKind != JsonValueKind.Object) continue;
            foreach (var p in images.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String) continue;
                var url = p.Value.GetString() ?? "";
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = MedText.ResolveAssetPath("", p.Name);
                if (rel.Length == 0) continue;
                // rel 本身已含 imgs/ 前缀，直接落在 BookDir 下，避免多拼一层目录
                var dest = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!seen.Add(dest)) continue;
                downloads.Add((url, dest));
            }
        }
        var (imgOk, imgFail, imgErrors) = DownloadAll(downloads, 10, progress);
        draft.ImageCount = imgOk;
        draft.Messages.Add($"图片本地化 {imgOk}/{downloads.Count} 张" + (imgFail > 0 ? $"，失败 {imgFail} 张" : ""));
        if (imgErrors.Count > 0) draft.Messages.Add("失败示例：" + string.Join(" | ", imgErrors.Take(3).Select(Path.GetFileName)));

        // 2) 逐页生成章节 HTML + 检索 chunk（data-p 与 chunk 的 PStart/PEnd 对齐）
        for (int pi = 0; pi < pages.Count; pi++)
        {
            var page = pages[pi];
            var text = Str(Prop(page, "markdown"), "text");
            var html = BuildPageHtml(text, bookId, out var blocks, out int pageImages);
            var file = "ch_" + pi.ToString("D5") + ".html";
            File.WriteAllText(Path.Combine(chapterDir, file), html, new UTF8Encoding(false));

            var cid = MedEpub.NewId("c");
            var ch = new MedChapterDraft
            {
                Id = cid, Level = 1, SpineIndex = pi, Href = file, Anchor = "", Title = PageTitle(page, pi),
                TitleNorm = "", Ord = pi, ImgCount = pageImages, DocIndex = pi,
                BlockStart = 0, BlockEnd = blocks.Count,
            };
            ch.TitleNorm = MedText.NormalizeTitle(ch.Title);
            var chunkDrafts = MedText.BuildChunks(blocks, 0, blocks.Count);
            int k = 0;
            foreach (var cd in chunkDrafts)
            {
                draft.Chunks.Add(new MedChunkRow
                {
                    Id = MedEpub.NewId("k"), ChapterId = cid, Ord = k++, Text = cd.Text, Norm = cd.Norm,
                    PStart = cd.PStart, PEnd = cd.PEnd, CharStart = cd.CharStart, CharEnd = cd.CharEnd,
                    ImgRefs = cd.ImgRefs,
                });
                draft.CharCount += cd.Text.Length;
            }
            draft.Chapters.Add(ch);
            if ((pi & 63) == 0) progress?.Invoke(pi, pages.Count, "解析第 " + (pi + 1) + "/" + pages.Count + " 页");
        }
        draft.AnchorTotal = draft.AnchorResolved = draft.Chapters.Count;
        draft.Messages.Add($"按页建章节 {draft.Chapters.Count} 章 / {draft.Chunks.Count} 段 / {draft.CharCount} 字符");
        return draft;
    }

    private static string BuildPageHtml(string markdown, string bookId, out List<MedBlock> blocks, out int imageCount)
    {
        blocks = new List<MedBlock>();
        imageCount = 0;
        var sb = new StringBuilder();
        int p = 0;
        foreach (var raw in SplitRe.Split(markdown ?? ""))
        {
            var seg = raw.Trim();
            if (seg.Length == 0) continue;
            p++;
            var refs = new List<string>();
            var rendered = ImgRe.Replace(seg, m =>
            {
                var rel = MedText.ResolveAssetPath("", WebUtility.HtmlDecode(m.Groups["src"].Value));
                if (rel.Length == 0) return m.Value;
                refs.Add(rel);
                var url = "/medapi/asset?book=" + Uri.EscapeDataString(bookId) + "&path=" + Uri.EscapeDataString(rel);
                return m.Groups[1].Value + m.Groups["q"].Value + url + m.Groups["q"].Value;
            });
            if (HeadingRe.IsMatch(seg))
                sb.Append("<h2 data-p=\"").Append(p).Append("\">")
                  .Append(WebUtility.HtmlEncode(seg.TrimStart('#').Trim())).Append("</h2>\n");
            else if (seg.Contains("<img", StringComparison.OrdinalIgnoreCase) ||
                     seg.Contains("<table", StringComparison.OrdinalIgnoreCase) ||
                     seg.Contains("<div", StringComparison.OrdinalIgnoreCase))
                sb.Append("<div data-p=\"").Append(p).Append("\">").Append(rendered).Append("</div>\n");
            else
                sb.Append("<p data-p=\"").Append(p).Append("\">").Append(rendered).Append("</p>\n");

            var plain = MedText.HtmlToText(seg);
            if (plain.Length > 0)
                blocks.Add(new MedBlock
                {
                    Kind = MedBlockKind.Paragraph, Text = plain, Norm = MedText.NormalizeText(plain),
                    PIndex = p, Pos = 0,
                });
            foreach (var rel in refs)
            {
                imageCount++;
                blocks.Add(new MedBlock { Kind = MedBlockKind.Image, Src = rel, PIndex = p, Pos = 0 });
            }
        }
        return sb.ToString();
    }

    private static string PageTitle(JsonElement page, int index)
    {
        var list = Prop(Prop(page, "prunedResult"), "parsing_res_list");
        if (list.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in list.EnumerateArray())
            {
                var label = Str(b, "block_label");
                if (label is not ("doc_title" or "paragraph_title")) continue;
                var t = Str(b, "block_content").TrimStart('#', ' ', '\t').Trim();
                if (t.Length > 0) return t.Length > 120 ? t[..120] : t;
            }
        }
        return "第 " + (index + 1) + " 页";
    }

    private static JsonElement Prop(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    private static string Str(JsonElement e, string name)
        => Prop(e, name).ValueKind == JsonValueKind.String ? Prop(e, name).GetString() ?? "" : "";

    /// <summary>并发下载 + 重试 + 跳过已存在文件；返回成功/失败数。</summary>
    public static (int Ok, int Fail, List<string> Errors) DownloadAll(
        List<(string Url, string Dest)> items, int parallel = 10, Action<int, int, string>? progress = null)
    {
        if (items.Count == 0) return (0, 0, new List<string>());
        int done = 0, ok = 0, fail = 0;
        var errors = new ConcurrentBag<string>();
        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(parallel, 1, 24) }, it =>
        {
            bool success = false;
            try { success = File.Exists(it.Dest) && new FileInfo(it.Dest).Length > 0; } catch { }
            for (int attempt = 0; attempt < 3 && !success; attempt++)
            {
                try
                {
                    var bytes = Http.GetByteArrayAsync(it.Url).GetAwaiter().GetResult();
                    if (bytes.Length == 0) throw new IOException("empty image");
                    Directory.CreateDirectory(Path.GetDirectoryName(it.Dest) ?? "");
                    var tmp = it.Dest + "." + Environment.CurrentManagedThreadId + ".part";
                    File.WriteAllBytes(tmp, bytes);
                    File.Move(tmp, it.Dest, true);
                    success = true;
                }
                catch { Thread.Sleep(120 * (attempt + 1)); }
            }
            if (success) Interlocked.Increment(ref ok);
            else { Interlocked.Increment(ref fail); errors.Add(it.Dest); }
            var d = Interlocked.Increment(ref done);
            if ((d & 63) == 0) progress?.Invoke(d, items.Count, "图片本地化 " + d + "/" + items.Count);
        });
        return (ok, fail, errors.Take(20).ToList());
    }
}
