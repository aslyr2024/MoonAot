using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>ZIP/CBZ 只读浏览：按中央目录列出、按需解压，不整包解压。</summary>
public static class ZipService
{
    public static ZipList List(string zipPath, string? prefix, int limit = 8000)
    {
        var result = new ZipList { Path = zipPath, Prefix = NormalizePrefix(prefix) };
        var zip = ZipLite.Open(zipPath);
        if (zip == null) return result;

        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ZipItem>();
        foreach (var e in zip.Entries)
        {
            var name = e.Name.Replace('\\', '/').TrimStart('/');
            if (name.Length == 0) continue;
            if (name.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith("Thumbs.db", StringComparison.OrdinalIgnoreCase)) continue;
            if (result.Prefix.Length > 0 && !name.StartsWith(result.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = result.Prefix.Length > 0 ? name[result.Prefix.Length..] : name;
            if (rest.Length == 0) continue;
            if (e.IsDirectory)
            {
                var segDir = rest.TrimEnd('/').Split('/')[0];
                if (segDir.Length > 0) dirs.Add(segDir);
                continue;
            }
            int slash = rest.IndexOf('/');
            if (slash > 0)
            {
                dirs.Add(rest[..slash]);
                continue;
            }
            files.Add(new ZipItem
            {
                Name = rest,
                Path = name,
                IsDir = false,
                Size = e.Size,
                CompressedSize = e.CompressedSize,
            });
            if (files.Count >= limit) break;
        }

        result.Items = dirs.OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new ZipItem
            {
                Name = d,
                Path = result.Prefix + d + "/",
                IsDir = true,
            })
            .Concat(files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        result.Total = result.Items.Count;
        return result;
    }

    public static byte[]? ReadEntry(string zipPath, string entryName, out string mime)
    {
        mime = FileKinds.MimeOf(entryName);
        var zip = ZipLite.Open(zipPath);
        if (zip == null) return null;
        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        var entry = zip.Find(normalized);
        if (entry == null || entry.IsDirectory) return null;
        if (entry.Size > 512L * 1024 * 1024) return null;
        return zip.Read(entry);
    }

    /// <summary>解压单个条目到临时目录（音视频在压缩包内时需要真实文件路径给播放器）。</summary>
    public static string? ExtractToTemp(string zipPath, string entryName, out string error)
    {
        error = "";
        try
        {
            var zip = ZipLite.Open(zipPath);
            if (zip == null) { error = "无法打开压缩包"; return null; }
            var normalized = entryName.Replace('\\', '/').TrimStart('/');
            var entry = zip.Find(normalized);
            if (entry == null || entry.IsDirectory) { error = "条目不存在"; return null; }
            if (entry.Size > 2L * 1024 * 1024 * 1024) { error = "条目过大"; return null; }

            var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(zipPath + "|" + normalized + "|" + entry.Size)))[..16].ToLowerInvariant();
            var dir = Path.Combine(Path.GetTempPath(), "MoonAot", "zip", hash);
            Directory.CreateDirectory(dir);
            var fileName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "extracted.bin";
            foreach (var c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
            var target = Path.Combine(dir, fileName);
            if (File.Exists(target) && new FileInfo(target).Length == entry.Size) return target;

            var data = zip.Read(entry);
            if (data == null) { error = "解压失败（加密或不支持的压缩方式）"; return null; }
            var tmp = target + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, target, true);
            return target;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string NormalizePrefix(string? prefix)
    {
        var p = (prefix ?? "").Replace('\\', '/').Trim('/');
        return p.Length == 0 ? "" : p + "/";
    }
}
