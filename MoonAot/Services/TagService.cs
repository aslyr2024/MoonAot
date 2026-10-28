using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>音频标签读取：MP3 ID3v2（标题/艺术家/专辑/内嵌封面）、FLAC Vorbis Comment + Picture。</summary>
public static class TagService
{
    public static AudioMetaDto ReadMeta(string path)
    {
        var meta = new AudioMetaDto();
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp3") ReadId3(path, meta);
            else if (ext == ".flac") ReadFlacMeta(path, meta);
        }
        catch (Exception ex)
        {
            meta.Error = ex.Message;
        }
        if (string.IsNullOrWhiteSpace(meta.Title) || string.IsNullOrWhiteSpace(meta.Artist))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var (a, t) = SplitName(name);
            if (string.IsNullOrWhiteSpace(meta.Title)) meta.Title = t;
            if (string.IsNullOrWhiteSpace(meta.Artist)) meta.Artist = a;
        }
        meta.Ok = true;
        meta.HasCover = FindCoverFile(path) != null || HasEmbeddedCover(path);
        return meta;
    }

    public static (string Artist, string Title) SplitName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ("", "");
        var s = name.Trim();
        foreach (var sep in new[] { " - ", " – ", " — ", "-" })
        {
            int idx = s.IndexOf(sep, StringComparison.Ordinal);
            if (idx <= 0 || idx >= s.Length - sep.Length) continue;
            var left = s[..idx].Trim();
            var right = s[(idx + sep.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            // 常见命名：艺术家 - 标题
            return (left, right);
        }
        return ("", s);
    }

    public static string? FindCoverFile(string audioPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(audioPath);
            if (dir == null) return null;
            var baseName = Path.GetFileNameWithoutExtension(audioPath);
            foreach (var name in new[] { baseName + ".jpg", baseName + ".png", "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "album.jpg" })
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
        }
        catch { }
        return null;
    }

    public static bool HasEmbeddedCover(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp3")
            {
                using var fs = File.OpenRead(path);
                var head = new byte[10];
                if (fs.Read(head, 0, 10) != 10 || head[0] != 'I' || head[1] != 'D' || head[2] != '3') return false;
                int size = SyncSafe(head, 6);
                var buf = new byte[Math.Min(size, 8 * 1024 * 1024)];
                fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
                return ContainsFrame(buf, "APIC");
            }
            if (ext == ".flac")
            {
                return ReadFlacPicture(path) != null;
            }
        }
        catch { }
        return false;
    }

    private static bool ContainsFrame(byte[] buf, string id)
    {
        var needle = Encoding.ASCII.GetBytes(id);
        for (int i = 0; i + needle.Length <= buf.Length; i++)
        {
            if (buf[i] != needle[0]) continue;
            bool ok = true;
            for (int j = 1; j < needle.Length; j++)
                if (buf[i + j] != needle[j]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    /// <summary>取音频封面字节（内嵌优先，其次同目录 cover/folder 图）。</summary>
    public static (byte[] Data, string Mime)? ReadCover(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp3")
            {
                var apic = ReadId3Apic(path);
                if (apic != null) return apic;
            }
            else if (ext == ".flac")
            {
                var pic = ReadFlacPicture(path);
                if (pic != null) return pic;
            }
            var file = FindCoverFile(path);
            if (file != null)
            {
                var data = File.ReadAllBytes(file);
                return (data, FileKinds.MimeOf(file));
            }
        }
        catch { }
        return null;
    }

    // ==================== ID3v2 ====================
    private static void ReadId3(string path, AudioMetaDto meta)
    {
        using var fs = File.OpenRead(path);
        var head = new byte[10];
        if (fs.Read(head, 0, 10) != 10) return;
        if (head[0] != 'I' || head[1] != 'D' || head[2] != '3') return;
        int version = head[3];
        int flags = head[5];
        int tagSize = SyncSafe(head, 6);
        if (tagSize <= 0 || tagSize > 64 * 1024 * 1024) return;
        var buf = new byte[tagSize];
        int got = 0;
        while (got < tagSize)
        {
            int n = fs.Read(buf, got, tagSize - got);
            if (n <= 0) break;
            got += n;
        }
        int pos = 0;
        if ((flags & 0x40) != 0 && version >= 3 && pos + 4 <= got)
        {
            int ext = version == 4 ? SyncSafe(buf, pos) : BE32(buf, pos);
            pos += ext;
        }
        while (pos + 10 <= got)
        {
            if (buf[pos] == 0) break;
            var id = Encoding.ASCII.GetString(buf, pos, 4);
            if (id.Length < 4 || id == "\0\0\0\0") break;
            int size = version == 4 ? SyncSafe(buf, pos + 4) : BE32(buf, pos + 4);
            if (size <= 0 || pos + 10 + size > got) break;
            int data = pos + 10;
            try
            {
                if (id == "TIT2") meta.Title = DecodeTextFrame(buf, data, size);
                else if (id == "TPE1") meta.Artist = DecodeTextFrame(buf, data, size);
                else if (id == "TALB") meta.Album = DecodeTextFrame(buf, data, size);
            }
            catch { }
            pos = data + size;
        }
    }

    private static (byte[] Data, string Mime)? ReadId3Apic(string path)
    {
        using var fs = File.OpenRead(path);
        var head = new byte[10];
        if (fs.Read(head, 0, 10) != 10) return null;
        if (head[0] != 'I' || head[1] != 'D' || head[2] != '3') return null;
        int version = head[3];
        int flags = head[5];
        int tagSize = SyncSafe(head, 6);
        if (tagSize <= 0 || tagSize > 32 * 1024 * 1024) return null;
        var buf = new byte[tagSize];
        int got = 0;
        while (got < tagSize)
        {
            int n = fs.Read(buf, got, tagSize - got);
            if (n <= 0) break;
            got += n;
        }
        int pos = 0;
        if ((flags & 0x40) != 0 && version >= 3 && pos + 4 <= got)
            pos += version == 4 ? SyncSafe(buf, pos) : BE32(buf, pos);
        while (pos + 10 <= got)
        {
            if (buf[pos] == 0) break;
            var id = Encoding.ASCII.GetString(buf, pos, 4);
            int size = version == 4 ? SyncSafe(buf, pos + 4) : BE32(buf, pos + 4);
            if (size <= 0 || pos + 10 + size > got) break;
            int data = pos + 10;
            if (id == "APIC" && size > 4)
            {
                int p = data;
                int enc = buf[p++];
                int mimeSearch = Math.Min(size - (p - data), 256);
                if (mimeSearch <= 0) { pos = data + size; continue; }
                int mimeEnd = Array.IndexOf(buf, (byte)0, p, mimeSearch);
                if (mimeEnd < 0) { pos = data + size; continue; }
                var mime = Encoding.Latin1.GetString(buf, p, mimeEnd - p);
                p = mimeEnd + 1;
                if (p >= data + size) { pos = data + size; continue; }
                p++; // picture type
                // 跳过描述（按编码的终止符）
                if (enc is 1 or 2)
                {
                    while (p + 1 < data + size && !(buf[p] == 0 && buf[p + 1] == 0)) p += 2;
                    p += 2;
                }
                else
                {
                    while (p < data + size && buf[p] != 0) p++;
                    p++;
                }
                if (p < data + size && data + size - p > 512)
                {
                    var bytes = new byte[data + size - p];
                    Array.Copy(buf, p, bytes, 0, bytes.Length);
                    if (string.IsNullOrWhiteSpace(mime) || !mime.Contains('/'))
                        mime = CoverService.DetectImageExt(bytes, ".jpg") switch
                        {
                            ".png" => "image/png",
                            ".webp" => "image/webp",
                            ".gif" => "image/gif",
                            _ => "image/jpeg",
                        };
                    return (bytes, mime);
                }
            }
            pos = data + size;
        }
        return null;
    }

    private static string DecodeTextFrame(byte[] buf, int offset, int size)
    {
        if (size < 1) return "";
        int enc = buf[offset];
        var text = DecodeEncoded(buf, offset + 1, size - 1, enc);
        return CleanTag(text);
    }

    private static string DecodeEncoded(byte[] buf, int offset, int count, int enc)
    {
        if (count <= 0) return "";
        switch (enc)
        {
            case 1:
            {
                if (count >= 2 && buf[offset] == 0xFF && buf[offset + 1] == 0xFE)
                    return Encoding.Unicode.GetString(buf, offset + 2, count - 2);
                if (count >= 2 && buf[offset] == 0xFE && buf[offset + 1] == 0xFF)
                    return Encoding.BigEndianUnicode.GetString(buf, offset + 2, count - 2);
                return Encoding.Unicode.GetString(buf, offset, count);
            }
            case 2: return Encoding.BigEndianUnicode.GetString(buf, offset, count);
            case 3: return Encoding.UTF8.GetString(buf, offset, count);
            default:
            {
                var latin = Encoding.Latin1.GetString(buf, offset, count);
                if (latin.Any(c => c > 0x7F))
                {
                    try
                    {
                        var gb = Encoding.GetEncoding("GB18030").GetString(buf, offset, count);
                        if (gb.Any(c => c >= 0x4E00 && c <= 0x9FFF)) return gb;
                    }
                    catch { }
                }
                return latin;
            }
        }
    }

    private static string CleanTag(string s) => s.Replace("\0", "").Trim();

    private static int SyncSafe(byte[] b, int off) =>
        ((b[off] & 0x7F) << 21) | ((b[off + 1] & 0x7F) << 14) | ((b[off + 2] & 0x7F) << 7) | (b[off + 3] & 0x7F);

    private static int BE32(byte[] b, int off) =>
        (b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3];

    // ==================== FLAC ====================
    private static void ReadFlacMeta(string path, AudioMetaDto meta)
    {
        using var fs = File.OpenRead(path);
        var head = new byte[4];
        if (fs.Read(head, 0, 4) != 4 || head[0] != 'f' || head[1] != 'L' || head[2] != 'a' || head[3] != 'C') return;
        bool last = false;
        while (!last)
        {
            var h = new byte[4];
            if (fs.Read(h, 0, 4) != 4) break;
            last = (h[0] & 0x80) != 0;
            int type = h[0] & 0x7F;
            int len = (h[1] << 16) | (h[2] << 8) | h[3];
            if (len < 0 || len > 16 * 1024 * 1024) break;
            var data = new byte[len];
            int got = 0;
            while (got < len)
            {
                int n = fs.Read(data, got, len - got);
                if (n <= 0) break;
                got += n;
            }
            if (got < len) break;
            if (type == 4) ParseVorbisComment(data, meta);
            if (last) break;
        }
    }

    private static void ParseVorbisComment(byte[] data, AudioMetaDto meta)
    {
        try
        {
            int pos = 0;
            int vendorLen = LE32(data, pos); pos += 4 + vendorLen;
            if (pos + 4 > data.Length) return;
            int count = LE32(data, pos); pos += 4;
            for (int i = 0; i < count && pos + 4 <= data.Length; i++)
            {
                int len = LE32(data, pos); pos += 4;
                if (len < 0 || pos + len > data.Length) break;
                var kv = Encoding.UTF8.GetString(data, pos, len);
                pos += len;
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                var key = kv[..eq].ToUpperInvariant();
                var val = kv[(eq + 1)..].Trim();
                if (key == "TITLE") meta.Title = val;
                else if (key == "ARTIST") meta.Artist = val;
                else if (key == "ALBUM") meta.Album = val;
            }
        }
        catch { }
    }

    private static (byte[] Data, string Mime)? ReadFlacPicture(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[4];
            if (fs.Read(head, 0, 4) != 4 || head[0] != 'f' || head[1] != 'L' || head[2] != 'a' || head[3] != 'C') return null;
            bool last = false;
            while (!last)
            {
                var h = new byte[4];
                if (fs.Read(h, 0, 4) != 4) break;
                last = (h[0] & 0x80) != 0;
                int type = h[0] & 0x7F;
                int len = (h[1] << 16) | (h[2] << 8) | h[3];
                if (len < 0 || len > 32 * 1024 * 1024) return null;
                var data = new byte[len];
                int got = 0;
                while (got < len)
                {
                    int n = fs.Read(data, got, len - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (type == 6 && got == len)
                {
                    int pos = 0;
                    if (pos + 8 > data.Length) return null;
                    pos += 4; // picture type
                    int mimeLen = (int)BE32(data, pos); pos += 4;
                    if (mimeLen < 0 || pos + mimeLen > data.Length) return null;
                    var mime = Encoding.ASCII.GetString(data, pos, mimeLen); pos += mimeLen;
                    int descLen = (int)BE32(data, pos); pos += 4;
                    if (descLen < 0 || pos + descLen > data.Length) return null;
                    pos += descLen;
                    pos += 16; // width/height/depth/colors
                    if (pos + 4 > data.Length) return null;
                    int picLen = (int)BE32(data, pos); pos += 4;
                    if (picLen <= 0 || pos + picLen > data.Length) return null;
                    var bytes = new byte[picLen];
                    Array.Copy(data, pos, bytes, 0, picLen);
                    if (string.IsNullOrWhiteSpace(mime)) mime = "image/jpeg";
                    return (bytes, mime);
                }
                if (last) break;
            }
        }
        catch { }
        return null;
    }

    private static int LE32(byte[] b, int off) =>
        b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24);
}
