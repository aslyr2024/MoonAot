using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>
/// 书籍元数据解析：EPUB（OPF）、MOBI/AZW3（PalmDOC+EXTH）、PDF（Info/XMP 粗略）、TXT/MD（文件名/首行）。
/// 使用 XDocument 与正则，NativeAOT 安全。
/// </summary>
public static class MetadataService
{
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";

    /// <summary>解析并填充书籍元数据。fast=true 时跳过 TXT/MD 正文探测（批量导入 10 万本时避免无谓 I/O）。</summary>
    public static void Enrich(BookRecord book, bool fast = false)
    {
        try
        {
            if (!File.Exists(book.FilePath)) return;
            switch (book.Format)
            {
                case BookFormatEx.Epub: EnrichEpub(book); break;
                case BookFormatEx.Mobi:
                case BookFormatEx.Azw3: EnrichMobi(book); break;
                case BookFormatEx.Pdf: EnrichPdf(book); break;
                case BookFormatEx.Md:
                    if (fast) book.Title = CleanFileNameTitle(Path.GetFileNameWithoutExtension(book.FilePath));
                    else EnrichMarkdown(book);
                    break;
                default:
                    if (fast) book.Title = CleanFileNameTitle(Path.GetFileNameWithoutExtension(book.FilePath));
                    else EnrichTxt(book);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"解析元数据失败 {book.FilePath}: {ex.Message}");
        }
    }

    // ==================== EPUB ====================
    private static void EnrichEpub(BookRecord book)
    {
        var zip = ZipLite.Open(book.FilePath);
        if (zip == null) return;
        var opfPath = ResolveOpfPath(zip);
        if (opfPath == null) return;
        var entry = zip.Find(opfPath);
        if (entry == null) return;
        var bytes = zip.Read(entry);
        if (bytes == null) return;

        XDocument doc;
        try { doc = XDocument.Parse(Encoding.UTF8.GetString(bytes)); }
        catch { return; }
        var metadata = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
        if (metadata == null) return;

        var title = Value(metadata, "title");
        var creator = Value(metadata, "creator");
        var language = Value(metadata, "language");
        var publisher = Value(metadata, "publisher");
        var description = Value(metadata, "description");
        var date = Value(metadata, "date");

        if (!string.IsNullOrWhiteSpace(title)) book.Title = Clean(title, 300);
        if (!string.IsNullOrWhiteSpace(creator)) book.Author = Clean(creator, 200);
        if (!string.IsNullOrWhiteSpace(language)) book.Language = Clean(language, 32);
        if (!string.IsNullOrWhiteSpace(publisher)) book.Publisher = Clean(publisher, 200);
        if (!string.IsNullOrWhiteSpace(description)) book.Description = Clean(description, 1000);
        foreach (var meta in metadata.Descendants().Where(e => e.Name.LocalName == "meta"))
        {
            var name = (string?)meta.Attribute("name") ?? (string?)meta.Attribute("property");
            var content = (string?)meta.Attribute("content");
            if (string.IsNullOrEmpty(content)) content = meta.Value;
            if (string.IsNullOrWhiteSpace(content)) continue;
            if (name is "calibre:series" or "belongs-to-collection")
                book.Category ??= Clean(content, 100);
            else if (name == "calibre:timestamp" && string.IsNullOrEmpty(date))
                date = content;
        }
        _ = date;
    }

    private static string? ResolveOpfPath(ZipLite zip)
    {
        var container = zip.Find("META-INF/container.xml");
        if (container == null) return null;
        var bytes = zip.Read(container);
        if (bytes == null) return null;
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(bytes));
            var rootfile = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile");
            var fullPath = (string?)rootfile?.Attribute("full-path");
            return string.IsNullOrWhiteSpace(fullPath) ? null : fullPath;
        }
        catch { return null; }
    }

    private static string? Value(XElement metadata, string localName)
    {
        var el = metadata.Descendants().FirstOrDefault(e => e.Name.LocalName == localName && e.Name.Namespace == Dc)
                 ?? metadata.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
        return el?.Value;
    }

    // ==================== MOBI / AZW3 ====================
    private static void EnrichMobi(BookRecord book)
    {
        using var fs = new FileStream(book.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var head = new byte[78];
        if (fs.Read(head, 0, head.Length) != head.Length) return;
        var pdbName = Encoding.ASCII.GetString(head, 0, 32).TrimEnd('\0', ' ');
        int recordCount = (head[76] << 8) | head[77]; // PalmDB 记录数是 2 字节，不是 4 字节
        if (recordCount <= 0 || recordCount > 100000) return;

        var offsets = new long[Math.Min(recordCount, 64)];
        var ofs = new byte[offsets.Length * 8];
        if (fs.Read(ofs, 0, ofs.Length) != ofs.Length) return;
        for (int i = 0; i < offsets.Length; i++) offsets[i] = BE32(ofs, i * 8);

        long rec0Start = offsets[0];
        long rec0End = offsets.Length > 1 ? offsets[1] : Math.Min(fs.Length, rec0Start + 256 * 1024);
        int rec0Len = (int)Math.Min(Math.Max(1, rec0End - rec0Start), 1024 * 1024);
        var rec0 = new byte[rec0Len];
        fs.Position = rec0Start;
        int got = 0;
        while (got < rec0Len)
        {
            int n = fs.Read(rec0, got, rec0Len - got);
            if (n <= 0) break;
            got += n;
        }
        if (got < 100) return;

        if (Encoding.ASCII.GetString(rec0, 16, 4) != "MOBI") return;
        int mobiHeaderLen = (int)BE32(rec0, 20);
        int encoding = (int)BE32(rec0, 28);
        var enc = encoding == 65001 ? Encoding.UTF8 : Encoding.Latin1;

        // 全名
        string? fullName = null;
        int nameOfs = (int)BE32(rec0, 0x54);
        int nameLen = (int)BE32(rec0, 0x58);
        if (nameOfs > 0 && nameLen > 0 && nameOfs + nameLen <= rec0.Length)
            fullName = SafeDecode(enc, rec0, nameOfs, nameLen);

        string? exthAuthor = null, exthTitle = null, language = null, publisher = null, description = null;
        int exthFlags = (int)BE32(rec0, 0x80);
        if ((exthFlags & 0x40) != 0)
        {
            int exthStart = 16 + mobiHeaderLen;
            if (exthStart + 12 <= rec0.Length && Encoding.ASCII.GetString(rec0, exthStart, 4) == "EXTH")
            {
                int count = (int)BE32(rec0, exthStart + 8);
                int pos = exthStart + 12;
                for (int i = 0; i < count && pos + 8 <= rec0.Length; i++)
                {
                    int type = (int)BE32(rec0, pos);
                    int len = (int)BE32(rec0, pos + 4);
                    if (len < 8 || pos + len > rec0.Length) break;
                    int dataLen = len - 8;
                    if (dataLen > 0)
                    {
                        var s = SafeDecode(enc, rec0, pos + 8, dataLen);
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            s = s.TrimEnd('\0');
                            switch (type)
                            {
                                case 100: exthAuthor = s; break;
                                case 503: exthTitle = s; break;
                                case 524: language = s; break;
                                case 101: publisher = s; break;
                                case 103: description = s; break;
                                case 105: book.Category ??= Clean(s, 100); break;
                            }
                        }
                    }
                    pos += len;
                }
            }
        }

        var title = !string.IsNullOrWhiteSpace(exthTitle) ? exthTitle
            : !string.IsNullOrWhiteSpace(fullName) ? fullName
            : pdbName;
        if (!string.IsNullOrWhiteSpace(title)) book.Title = Clean(title, 300);
        if (!string.IsNullOrWhiteSpace(exthAuthor)) book.Author = Clean(exthAuthor, 200);
        if (!string.IsNullOrWhiteSpace(language)) book.Language = Clean(language, 32);
        if (!string.IsNullOrWhiteSpace(publisher)) book.Publisher = Clean(publisher, 200);
        if (!string.IsNullOrWhiteSpace(description)) book.Description = Clean(description, 1000);
    }

    private static string SafeDecode(Encoding enc, byte[] buf, int offset, int count)
    {
        try { return enc.GetString(buf, offset, count); }
        catch { return ""; }
    }

    // ==================== PDF ====================
    private static void EnrichPdf(BookRecord book)
    {
        try
        {
            using var fs = new FileStream(book.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int len = (int)Math.Min(fs.Length, 2 * 1024 * 1024);
            var buf = new byte[len];
            int got = 0;
            while (got < len)
            {
                int n = fs.Read(buf, got, len - got);
                if (n <= 0) break;
                got += n;
            }
            var text = Encoding.Latin1.GetString(buf, 0, got);

            var title = PdfInfoString(text, "Title") ?? XmpValue(text, "title");
            var author = PdfInfoString(text, "Author") ?? XmpValue(text, "creator");
            if (!string.IsNullOrWhiteSpace(title)) book.Title = Clean(title, 300);
            if (!string.IsNullOrWhiteSpace(author)) book.Author = Clean(author, 200);
        }
        catch { }
    }

    private static string? PdfInfoString(string text, string key)
    {
        var m = Regex.Match(text, "/" + key + @"\s*\((?<v>(?:\\.|[^\\()])*)\)", RegexOptions.CultureInvariant);
        if (!m.Success) return null;
        var raw = m.Groups["v"].Value;
        var bytes = new List<byte>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\\' && i + 1 < raw.Length)
            {
                char n = raw[++i];
                switch (n)
                {
                    case 'n': bytes.Add((byte)'\n'); break;
                    case 'r': bytes.Add((byte)'\r'); break;
                    case 't': bytes.Add((byte)'\t'); break;
                    case 'b': bytes.Add(8); break;
                    case 'f': bytes.Add(12); break;
                    case '(': bytes.Add((byte)'('); break;
                    case ')': bytes.Add((byte)')'); break;
                    case '\\': bytes.Add((byte)'\\'); break;
                    default:
                        if (n >= '0' && n <= '7')
                        {
                            int val = n - '0';
                            for (int k = 0; k < 2 && i + 1 < raw.Length && raw[i + 1] >= '0' && raw[i + 1] <= '7'; k++)
                                val = val * 8 + (raw[++i] - '0');
                            bytes.Add((byte)val);
                        }
                        else bytes.Add((byte)n);
                        break;
                }
            }
            else bytes.Add((byte)c);
        }
        if (bytes.Count >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes.ToArray(), 2, bytes.Count - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
        catch { return Encoding.Latin1.GetString(bytes.ToArray()); }
    }

    private static string? XmpValue(string text, string tag)
    {
        var m = Regex.Match(text,
            "<dc:" + tag + @"[^>]*>[\s\S]{0,400}?<rdf:li[^>]*>(?<v>[^<]{1,300})</rdf:li>",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return System.Net.WebUtility.HtmlDecode(m.Groups["v"].Value);
    }

    // ==================== TXT / MD ====================
    private static void EnrichTxt(BookRecord book)
    {
        var name = Path.GetFileNameWithoutExtension(book.FilePath);
        name = CleanFileNameTitle(name);
        if (!string.IsNullOrWhiteSpace(name)) book.Title = name;

        try
        {
            var fi = new FileInfo(book.FilePath);
            if (fi.Length == 0 || fi.Length > 8L * 1024 * 1024) return;
            var bytes = new byte[Math.Min(fi.Length, 8192)];
            using var fs = File.OpenRead(book.FilePath);
            int got = fs.Read(bytes, 0, bytes.Length);
            var text = TextCodec.Decode(bytes.AsSpan(0, got).ToArray());
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int idx = 0;
            while (idx < lines.Length && string.IsNullOrWhiteSpace(lines[idx])) idx++;
            if (idx >= lines.Length) return;
            var first = lines[idx].Trim();
            if (first.Length is > 0 and <= 60 && !LooksLikeBody(first))
            {
                book.Title = CleanFileNameTitle(first);
                var second = idx + 1 < lines.Length ? lines[idx + 1].Trim() : "";
                var am = Regex.Match(second, @"^(?:作者|著者|Author)\s*[:：]\s*(?<a>.{1,40})$", RegexOptions.IgnoreCase);
                if (am.Success) book.Author = Clean(am.Groups["a"].Value, 120);
            }
            if (string.IsNullOrWhiteSpace(book.Author))
            {
                var m = Regex.Match(first, @"^\s*[\[（(【](?<a>[^\]）)】]{1,30})[\]）)】]\s*(?<t>.+)$");
                if (m.Success)
                {
                    book.Author = Clean(m.Groups["a"].Value, 120);
                    book.Title = CleanFileNameTitle(m.Groups["t"].Value);
                }
            }
        }
        catch { }
    }

    private static void EnrichMarkdown(BookRecord book)
    {
        try
        {
            using var fs = File.OpenRead(book.FilePath);
            var buf = new byte[Math.Min(fs.Length, 16384)];
            int got = fs.Read(buf, 0, buf.Length);
            var text = TextCodec.Decode(buf.AsSpan(0, got).ToArray());
            var lines = text.Replace("\r\n", "\n").Split('\n');
            bool inFront = false;
            foreach (var raw in lines.Take(200))
            {
                var line = raw.Trim();
                if (line == "---") { inFront = !inFront; continue; }
                if (inFront)
                {
                    var m = Regex.Match(line, @"^(?:author|作者)\s*:\s*(?<v>.+)$", RegexOptions.IgnoreCase);
                    if (m.Success) book.Author = Clean(m.Groups["v"].Value, 120);
                    continue;
                }
                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    book.Title = Clean(line[2..], 300);
                    break;
                }
                if (line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal)) break;
            }
        }
        catch { }
        if (string.IsNullOrWhiteSpace(book.Title))
            book.Title = CleanFileNameTitle(Path.GetFileNameWithoutExtension(book.FilePath));
    }

    private static bool LooksLikeBody(string line) =>
        line.Contains('。') || line.Contains('，') || line.Length > 40 ||
        Regex.IsMatch(line, @"^第[一二三四五六七八九十百千0-9]+[章节回卷]");

    /// <summary>清理文件名式标题：去扩展名噪声、前导序号、书名号外包装、作者前缀。</summary>
    public static string CleanFileNameTitle(string name)
    {
        var s = (name ?? "").Trim();
        s = Regex.Replace(s, @"^\s*\d{1,5}\s*[-_.、 ]\s*", "");
        s = Regex.Replace(s, @"\s*[\[（(【]\s*(?:精校|全本|完结|TXT|txt|EPUB|epub|PDF|pdf|校对版?|典藏版?|完结版?)\s*[\]）)】]\s*", "");
        var m = Regex.Match(s, @"^[\[（(【]\s*(?<a>[^\]）)】]{1,30})\s*[\]）)】]\s*(?<t>.+)$");
        if (m.Success && m.Groups["t"].Value.Trim().Length > 0)
        {
            s = m.Groups["t"].Value.Trim();
        }
        if (s.StartsWith("《", StringComparison.Ordinal) && s.EndsWith("》", StringComparison.Ordinal) && s.Length > 2)
            s = s[1..^1];
        s = s.Replace('_', ' ').Trim();
        return s;
    }

    /// <summary>去 HTML 标签/实体、压缩空白，并截断。</summary>
    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = Regex.Replace(text, @"<[^>]{1,200}>", " ");
        s = System.Net.WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length > max) s = s[..max].TrimEnd() + "…";
        return s;
    }

    private static int BE32(byte[] b, int off) =>
        (b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3];
}
