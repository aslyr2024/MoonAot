using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace MoonAot.Services;

/// <summary>
/// 封面提取（无第三方依赖）：
///  - EPUB：container.xml → OPF manifest，按 cover-image / meta cover / 图片项 顺序挑选，并校验图片魔数；
///  - PDF：扫描前 8MB 的内嵌 JPEG；
///  - MOBI/AZW3：EXTH 201/202 缩略图，失败时回退 FirstImageRecord。
/// </summary>
public static class CoverService
{
    public static byte[]? Extract(string path, string format, out string ext)
    {
        ext = ".jpg";
        try
        {
            switch (format)
            {
                case "EPUB": return TryEpub(path, out ext);
                case "PDF": return TryPdf(path, out ext);
                case "MOBI":
                case "AZW3": return TryMobiThumbnail(path, out ext);
            }
        }
        catch { }
        return null;
    }

    // ==================== EPUB ====================
    private sealed class OpfItem
    {
        public string Id = "", Href = "", MediaType = "", Properties = "";
    }

    private static byte[]? TryEpub(string path, out string ext)
    {
        ext = ".jpg";
        var zip = ZipLite.Open(path);
        if (zip == null) return null;

        var container = zip.Find("META-INF/container.xml");
        if (container == null) return null;
        var containerBytes = zip.Read(container);
        if (containerBytes == null) return null;
        var rootFile = ExtractXmlAttr(containerBytes, "rootfile", "full-path");
        if (rootFile == null) return null;

        var opfEntry = zip.Find(rootFile);
        if (opfEntry == null)
        {
            var fileName = Path.GetFileName(rootFile.Replace('\\', '/'));
            opfEntry = zip.Entries.FirstOrDefault(e => !e.IsDirectory &&
                e.NameLower.EndsWith("/" + fileName.ToLowerInvariant()));
        }
        if (opfEntry == null) return null;
        var opfBytes = zip.Read(opfEntry);
        if (opfBytes == null) return null;
        var opfText = Encoding.UTF8.GetString(opfBytes);

        var items = new List<OpfItem>();
        foreach (Match m in Regex.Matches(opfText, @"<item\s[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = m.Value;
            var item = new OpfItem
            {
                Id = GetAttr(tag, "id") ?? "",
                Href = GetAttr(tag, "href") ?? "",
                MediaType = (GetAttr(tag, "media-type") ?? "").ToLowerInvariant(),
                Properties = GetAttr(tag, "properties") ?? "",
            };
            if (item.Href.Length > 0) items.Add(item);
        }

        var candidates = new List<OpfItem>();
        // 1) EPUB3 properties="cover-image"
        candidates.AddRange(items.Where(i => i.Properties.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("cover-image", StringComparer.OrdinalIgnoreCase)));

        // 2) EPUB2 <meta name="cover" content="id">
        var metaCover = Regex.Match(opfText,
            @"<meta\s[^>]*name\s*=\s*['""]cover['""][^>]*>", RegexOptions.IgnoreCase);
        if (metaCover.Success)
        {
            var content = GetAttr(metaCover.Value, "content");
            if (!string.IsNullOrEmpty(content))
            {
                var byId = items.FirstOrDefault(i => string.Equals(i.Id, content, StringComparison.Ordinal));
                if (byId != null) candidates.Add(byId);
                else if (content.Contains('/')) // 少数书 content 直接是 href
                {
                    var byHref = items.FirstOrDefault(i => string.Equals(i.Href, content, StringComparison.OrdinalIgnoreCase));
                    if (byHref != null) candidates.Add(byHref);
                }
            }
        }

        // 3) id/href 含 cover 的图片
        candidates.AddRange(items.Where(i =>
            (i.MediaType.StartsWith("image/", StringComparison.Ordinal) ||
             IsImageExt(Path.GetExtension(i.Href))) &&
            ((i.Id + i.Href).Contains("cover", StringComparison.OrdinalIgnoreCase))));

        // 4) 所有图片（最后兜底，取尺寸足够大的第一张）
        candidates.AddRange(items.Where(i =>
            i.MediaType.StartsWith("image/", StringComparison.Ordinal) ||
            IsImageExt(Path.GetExtension(i.Href))));

        var opfDir = Path.GetDirectoryName(rootFile.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in candidates)
        {
            if (string.IsNullOrEmpty(item.MediaType) || !item.MediaType.StartsWith("image/", StringComparison.Ordinal))
            {
                // 未标 media-type 或不是图片时，按扩展名再判一次
                if (!IsImageExt(Path.GetExtension(item.Href))) continue;
            }
            var fullPath = NormalizeZipPath(string.IsNullOrEmpty(opfDir) ? item.Href : opfDir + "/" + item.Href);
            if (!tried.Add(fullPath)) continue;

            var entry = zip.Find(fullPath);
            entry ??= zip.Entries.FirstOrDefault(e => !e.IsDirectory &&
                e.NameLower.EndsWith("/" + fullPath.ToLowerInvariant()));
            entry ??= zip.Entries.FirstOrDefault(e => !e.IsDirectory &&
                Path.GetFileName(e.NameLower) == Path.GetFileName(fullPath.ToLowerInvariant()));
            if (entry == null || entry.IsDirectory) continue;

            var data = zip.Read(entry);
            if (data == null || data.Length < 256 || !IsImageMagic(data)) continue;
            // 兜底项要求至少 8KB，避免抓到小图标
            if (item != candidates[0] && data.Length < 8 * 1024) continue;
            ext = DetectImageExt(data, Path.GetExtension(entry.NameLower));
            return data;
        }
        return null;
    }

    private static string NormalizeZipPath(string p)
    {
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();
        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
            stack.Add(part);
        }
        return string.Join('/', stack);
    }

    private static bool IsImageExt(string ext) => ext.ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".avif";

    // ==================== PDF ====================
    private static byte[]? TryPdf(string path, out string ext)
    {
        ext = ".jpg";
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var len = Math.Min(fs.Length, 8L * 1024 * 1024);
            var buf = new byte[len];
            int got = 0;
            while (got < len)
            {
                int n = fs.Read(buf, got, (int)(len - got));
                if (n <= 0) break;
                got += n;
            }
            for (int i = 0; i < got - 3; i++)
            {
                if (buf[i] != 0xFF || buf[i + 1] != 0xD8 || buf[i + 2] != 0xFF) continue;
                for (int j = i + 3; j < got - 1; j++)
                {
                    if (buf[j] != 0xFF || buf[j + 1] != 0xD9) continue;
                    int size = j + 2 - i;
                    if (size < 8 * 1024) break;
                    var data = new byte[size];
                    Array.Copy(buf, i, data, 0, size);
                    return data;
                }
            }
        }
        catch { }
        return null;
    }

    // ==================== MOBI ====================
    private static byte[]? TryMobiThumbnail(string path, out string ext)
    {
        ext = ".jpg";
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var head = new byte[78];
            if (fs.Read(head, 0, head.Length) != head.Length) return null;
            int recordCount = (head[76] << 8) | head[77]; // PalmDB 记录数是 2 字节，不是 4 字节
            if (recordCount <= 0 || recordCount > 200000) return null;

            var offsets = new long[Math.Min(recordCount, 1024)];
            var ofs = new byte[offsets.Length * 8];
            if (fs.Read(ofs, 0, ofs.Length) != ofs.Length) return null;
            for (int i = 0; i < offsets.Length; i++) offsets[i] = BE32(ofs, i * 8);

            long rec0Start = offsets[0];
            long rec0End = offsets.Length > 1 ? offsets[1] : Math.Min(fs.Length, rec0Start + 256 * 1024);
            int rec0Len = (int)Math.Min(Math.Max(1, rec0End - rec0Start), 512 * 1024);
            var rec0 = new byte[rec0Len];
            fs.Position = rec0Start;
            int got = 0;
            while (got < rec0Len)
            {
                int n = fs.Read(rec0, got, rec0Len - got);
                if (n <= 0) break;
                got += n;
            }
            if (got < 128) return null;
            if (Encoding.ASCII.GetString(rec0, 16, 4) != "MOBI") return null;

            int mobiHeaderLen = (int)BE32(rec0, 20);
            // FirstImageRecord 在 MOBI 头 0x5C，相对记录 0 的偏移是 16+0x5C=108
            int firstImageIndex = (int)BE32(rec0, 108);
            long? thumbOffset = null;
            long thumbLen = 0;

            // ⚠ EXTH flags 位于记录偏移 0x80（MOBI 头 0x70 + PalmDOC 16）
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
                        int recLen = (int)BE32(rec0, pos + 4);
                        if (recLen < 8 || pos + recLen > rec0.Length) break;
                        int dataLen = recLen - 8;
                        if (dataLen >= 4)
                        {
                            long val = BE32(rec0, pos + 8);
                            if (type == 201) thumbOffset = val;
                            else if (type == 202) thumbLen = val;
                        }
                        pos += recLen;
                    }
                }
            }

            if (thumbOffset is > 0 && thumbLen > 0)
            {
                // 官方 KindleUnpack 中 201 是文件绝对偏移；部分书籍给的是记录号，两种都尝试
                if (thumbOffset.Value + thumbLen <= fs.Length)
                {
                    fs.Position = thumbOffset.Value;
                    var data = new byte[thumbLen];
                    if (fs.Read(data, 0, data.Length) == data.Length && IsImageMagic(data))
                    {
                        ext = DetectImageExt(data, ".jpg");
                        return data;
                    }
                }
                if (thumbOffset.Value < recordCount)
                {
                    int idx = (int)thumbOffset.Value;
                    long start = offsets[idx];
                    long end = idx + 1 < offsets.Length ? offsets[idx + 1] : fs.Length;
                    long len = Math.Min(end - start, thumbLen > 0 ? thumbLen : end - start);
                    if (len > 0 && len < 32L * 1024 * 1024)
                    {
                        fs.Position = start;
                        var data = new byte[len];
                        if (fs.Read(data, 0, data.Length) == data.Length && IsImageMagic(data))
                        {
                            ext = DetectImageExt(data, ".jpg");
                            return data;
                        }
                    }
                }
            }

            // 回退：FirstImageRecord 起找第一张图片
            if (firstImageIndex > 0 && firstImageIndex < offsets.Length)
            {
                for (int idx = firstImageIndex; idx < offsets.Length && idx < firstImageIndex + 16; idx++)
                {
                    long start = offsets[idx];
                    long end = idx + 1 < offsets.Length ? offsets[idx + 1] : fs.Length;
                    long len = end - start;
                    if (len <= 0 || len > 32L * 1024 * 1024) continue;
                    var data = new byte[len];
                    fs.Position = start;
                    if (fs.Read(data, 0, data.Length) != data.Length) continue;
                    if (!IsImageMagic(data)) continue;
                    ext = DetectImageExt(data, ".jpg");
                    return data;
                }
            }
        }
        catch { }
        return null;
    }

    private static long BE32(byte[] buf, int off) =>
        (long)((uint)buf[off] << 24 | (uint)buf[off + 1] << 16 | (uint)buf[off + 2] << 8 | buf[off + 3]);

    // ==================== 工具 ====================
    public static bool IsImageMagic(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F') return true;
        if (data.Length >= 12 && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P') return true;
        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M') return true;
        if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P') return true;
        return false;
    }

    public static string DetectImageExt(byte[] data, string fallback)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return ".png";
        if (data.Length >= 12 && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P') return ".webp";
        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F') return ".gif";
        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M') return ".bmp";
        return string.IsNullOrEmpty(fallback) ? ".jpg" : fallback;
    }

    private static string? ExtractXmlAttr(byte[] bytes, string tag, string attr)
    {
        try
        {
            var doc = new XmlDocument();
            doc.Load(XmlReader.Create(new MemoryStream(bytes)));
            var nodes = doc.GetElementsByTagName(tag);
            foreach (XmlNode n in nodes)
            {
                var v = n.Attributes?[attr]?.Value;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { }
        return null;
    }

    private static string? GetAttr(string tag, string name)
    {
        var m = Regex.Match(tag, name + @"\s*=\s*['""]([^'""]*)['""]", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
