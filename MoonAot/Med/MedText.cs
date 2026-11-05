using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using OpenCC.NET;

namespace MoonAot.Med;

public enum MedBlockKind { Heading, Paragraph, Image }

public sealed class MedBlock
{
    public MedBlockKind Kind;
    public int Level;
    public string Id = "";
    public string Text = "";
    public string Norm = "";
    public string Src = "";
    public int PIndex;
    public int Pos;
}

public sealed class MedChunkDraft
{
    public string Text = "";
    public string Norm = "";
    public int PStart;
    public int PEnd;
    public int CharStart;
    public int CharEnd;
    public string ImgRefs = "";
}

/// <summary>
/// EPUB XHTML 的轻量解析与文本归一。
/// 这批书由 ebooklib 从 OCR 生成，结构规整（h1-h4 / p / div>img），
/// 因此用顺序标签扫描而不是引入 HTML 解析库，NativeAOT 下零依赖且可控。
/// </summary>
public static class MedText
{
    private static readonly OpenChineseConverter Converter = new();

    private static readonly Regex TagRe = new(
        @"<(?<tag>h[1-4]|p|img)\b(?<attrs>[^>]*?)/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IdRe = new(@"\bid\s*=\s*(['""])(?<v>.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SrcRe = new(@"\bsrc\s*=\s*(['""])(?<v>.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyTagRe = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex TagNameRe = new(@"^</?(?<n>[a-zA-Z][\w:.-]*)", RegexOptions.Compiled);
    private static readonly Regex SpaceRe = new(@"\s+", RegexOptions.Compiled);

    public static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = AnyTagRe.Replace(html, " ");
        s = WebUtility.HtmlDecode(s);
        return SpaceRe.Replace(s, " ").Trim();
    }

    /// <summary>标题/锚点归一：NFKC → 只保留字母数字汉字 → 小写。原 NCX 的 92% 断链靠它重建。</summary>
    public static string NormalizeTitle(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = WebUtility.HtmlDecode(AnyTagRe.Replace(text, ""));
        s = s.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>检索用归一文本：NFKC + 繁转简 + 空白压缩 + 小写。</summary>
    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text.Normalize(NormalizationForm.FormKC);
        try { s = Converter.ToSimplifiedFromTraditional(s); } catch { }
        s = SpaceRe.Replace(s, " ");
        return s.Trim().ToLowerInvariant();
    }

    public static string HtmlDecode(string s) => WebUtility.HtmlDecode(s ?? "");

    /// <summary>
    /// 顺序扫描全部标签，把所有裸文本片段都收进 blocks（不只 p：约 21% 正文在 div/li/td 里）。
    /// p 的编号与 BuildChapterHtml 注入的 data-p 一致，保证检索结果能滚回原段落。
    /// </summary>
    public static List<MedBlock> ParseBlocks(string xhtml)
    {
        var list = new List<MedBlock>();
        if (string.IsNullOrEmpty(xhtml)) return list;
        var s = Regex.Replace(xhtml, @"<head\b[^>]*>[\s\S]*?</head>", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<script\b[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        int pIndex = 0, pos = 0;

        void AddText(string raw, int at)
        {
            var t = HtmlToText(raw);
            if (t.Length == 0) return;
            list.Add(new MedBlock
            {
                Kind = MedBlockKind.Paragraph, Text = t, Norm = NormalizeText(t), PIndex = pIndex, Pos = at,
            });
        }

        foreach (Match m in AnyTagRe.Matches(s))
        {
            if (m.Index > pos) AddText(s[pos..m.Index], pos);
            var tag = m.Value;
            var nm = TagNameRe.Match(tag);
            var name = nm.Success ? nm.Groups["n"].Value.ToLowerInvariant() : "";
            bool closing = tag.StartsWith("</", StringComparison.Ordinal);
            pos = m.Index + m.Length;
            if (closing) continue;
            if (name == "p") { pIndex++; continue; }
            if (name == "img")
            {
                var sm = SrcRe.Match(tag);
                if (sm.Success) list.Add(new MedBlock
                {
                    Kind = MedBlockKind.Image, Src = HtmlDecode(sm.Groups["v"].Value).Trim(),
                    PIndex = pIndex, Pos = m.Index,
                });
                continue;
            }
            if (name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '4')
            {
                var close = "</" + name + ">";
                int end = s.IndexOf(close, pos, StringComparison.OrdinalIgnoreCase);
                if (end < 0) end = s.Length;
                var title = HtmlToText(s[pos..end]);
                var im = IdRe.Match(tag);
                list.Add(new MedBlock
                {
                    Kind = MedBlockKind.Heading, Level = name[1] - '0',
                    Id = im.Success ? HtmlDecode(im.Groups["v"].Value) : "",
                    Text = title, Norm = NormalizeTitle(title), PIndex = pIndex, Pos = m.Index,
                });
                pos = Math.Min(s.Length, end + close.Length);
            }
        }
        if (pos < s.Length) AddText(s[pos..], pos);
        return list;
    }

    /// <summary>把 heading 之间的段落聚合成 ≤maxChars 的检索单元，保留段落序号与图片引用。</summary>
    public static List<MedChunkDraft> BuildChunks(IReadOnlyList<MedBlock> blocks, int start, int end, int maxChars = 800)
    {
        var result = new List<MedChunkDraft>();
        var sb = new StringBuilder();
        var imgs = new List<string>();
        int pStart = 0, pEnd = 0, charCursor = 0;
        end = Math.Min(end, blocks.Count);

        void Flush()
        {
            var raw = SpaceRe.Replace(sb.ToString(), " ").Trim();
            if (raw.Length == 0 && imgs.Count == 0) return;
            var norm = NormalizeText(raw);
            result.Add(new MedChunkDraft
            {
                Text = raw,
                Norm = norm,
                PStart = pStart,
                PEnd = pEnd,
                CharStart = charCursor,
                CharEnd = charCursor + raw.Length,
                ImgRefs = string.Join("\n", imgs),
            });
            charCursor += raw.Length;
            sb.Clear();
            imgs.Clear();
            pStart = 0;
        }

        for (int i = start; i < end; i++)
        {
            var b = blocks[i];
            if (b.Kind == MedBlockKind.Heading)
            {
                // 章节内若还有未被目录收录的标题，其文字也要进入索引，避免丢内容
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(b.Text);
                continue;
            }
            if (b.Kind == MedBlockKind.Image)
            {
                if (!string.IsNullOrEmpty(b.Src)) imgs.Add(b.Src);
                if (imgs.Count >= 12) Flush();
                continue;
            }
            if (pStart == 0) pStart = b.PIndex;
            pEnd = b.PIndex;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(b.Text);
            if (sb.Length >= maxChars) Flush();
        }
        Flush();
        return result;
    }

    /// <summary>把 EPUB 章节 XHTML 清洗为可直接 v-html 的片段：图片改写成本地 API、段落注入 data-p、标题注入 data-anchor。</summary>
    public static string BuildChapterHtml(string xhtml, string href, string bookId)
    {
        if (string.IsNullOrEmpty(xhtml)) return "";
        var s = xhtml;
        var body = Regex.Match(s, @"<body\b[^>]*>(?<v>[\s\S]*)</body>", RegexOptions.IgnoreCase);
        if (body.Success) s = body.Groups["v"].Value;
        else
        {
            s = Regex.Replace(s, @"<\?xml[\s\S]*?\?>", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<head\b[^>]*>[\s\S]*?</head>", "", RegexOptions.IgnoreCase);
        }
        s = Regex.Replace(s, @"<script\b[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<link\b[^>]*/?>", "", RegexOptions.IgnoreCase);

        var dir = Path.GetDirectoryName(href.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        s = Regex.Replace(s, @"<img\b(?<pre>[^>]*?)\bsrc\s*=\s*(?<q>['""])(?<src>.*?)\k<q>(?<post>[^>]*?)/?>", mm =>
        {
            var raw = HtmlDecode(mm.Groups["src"].Value).Trim();
            var resolved = ResolveAssetPath(dir, raw);
            var url = "/medapi/asset?book=" + Uri.EscapeDataString(bookId) + "&path=" + Uri.EscapeDataString(resolved);
            return "<img" + mm.Groups["pre"].Value + " src=\"" + url + "\" loading=\"lazy\"" + mm.Groups["post"].Value + ">";
        }, RegexOptions.IgnoreCase);

        int p = 0;
        s = Regex.Replace(s, @"<p\b(?<attrs>[^>]*)>", mm =>
        {
            p++;
            return "<p data-p=\"" + p + "\"" + mm.Groups["attrs"].Value + ">";
        }, RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"<(h[1-4])\b(?<attrs>[^>]*)>", mm =>
        {
            var im = IdRe.Match(mm.Groups["attrs"].Value);
            var id = im.Success ? HtmlDecode(im.Groups["v"].Value) : "";
            return "<" + mm.Groups[1].Value + (id.Length > 0 ? " data-anchor=\"" + WebUtility.HtmlEncode(id) + "\"" : "") +
                   mm.Groups["attrs"].Value + ">";
        }, RegexOptions.IgnoreCase);
        return s;
    }

    /// <summary>把图片相对路径解析成 EPUB 包内路径，禁止越界。</summary>
    public static string ResolveAssetPath(string baseDir, string raw)
    {
        var s = raw.Replace('\\', '/');
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
        var combined = s.StartsWith('/') ? s.TrimStart('/') : (baseDir.Length > 0 ? baseDir.TrimEnd('/') + "/" + s : s);
        var parts = new List<string>();
        foreach (var seg in combined.Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(seg);
        }
        return string.Join("/", parts);
    }

    public static string Snippet(string text, string q, int radius = 90)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var terms = SplitTerms(q);
        int at = -1;
        foreach (var t in terms)
        {
            at = text.IndexOf(t, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) break;
        }
        if (at < 0) at = 0;
        int start = Math.Max(0, at - radius / 2);
        int len = Math.Min(radius, text.Length - start);
        return (start > 0 ? "…" : "") + text.Substring(start, len) + (start + len < text.Length ? "…" : "");
    }

    public static List<string> SplitTerms(string q)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(q)) return list;
        foreach (var part in q.Split(new[] { ' ', '\t', '　', ',', '，', '+', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim().Trim('"', '\'');
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }

    public static string EscapeHtml(string s) => WebUtility.HtmlEncode(s ?? "");
}
