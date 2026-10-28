using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>
/// EPUB 兼容修复：部分电子书（如当当网合集）的 container.xml 中 OPF 路径大小写
/// 与压缩包内实际条目不一致（ops/Content.opf vs OPS/Content.opf）。
/// Windows 上很多阅读器能容忍，但 Zip 规范区分大小写，readest/foliate 会卡在
/// “Failed to load package document”。这里在首次打开时生成一份修正副本供阅读器读取，
/// 原始文件保持不变。
/// </summary>
public static class EpubRepairService
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.Ordinal);

    private static string CacheDir
    {
        get
        {
            var dir = Path.Combine(DbStore.Instance.DataDir, "cache", "epub");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>返回阅读器应读取的文件路径；无需修复时返回原路径。</summary>
    public static string ResolveReadablePath(BookRecord book)
    {
        try
        {
            if (book.Format != BookFormatEx.Epub && !book.FilePath.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                return book.FilePath;
            var source = book.FilePath;
            if (!File.Exists(source)) return source;

            var cache = Path.Combine(CacheDir, book.Id + ".epub");
            var srcTime = File.GetLastWriteTimeUtc(source);
            if (File.Exists(cache) && File.GetLastWriteTimeUtc(cache) >= srcTime)
                return cache;

            var gate = Locks.GetOrAdd(book.Id, _ => new object());
            lock (gate)
            {
                if (File.Exists(cache) && File.GetLastWriteTimeUtc(cache) >= srcTime)
                    return cache;

                using var zip = ZipFile.OpenRead(source);
                var container = FindEntry(zip, "META-INF/container.xml");
                if (container == null) return source;
                string xml;
                using (var sr = new StreamReader(container.Open(), Encoding.UTF8))
                    xml = sr.ReadToEnd();

                var m = Regex.Match(xml, "full-path\\s*=\\s*[\"']([^\"']+)[\"']");
                if (!m.Success) return source;
                var declared = m.Groups[1].Value;
                var actual = FindEntry(zip, declared)?.FullName;
                if (actual == null) return source; // 没有对应 OPF，交给阅读器按原样处理
                if (string.Equals(actual, declared, StringComparison.Ordinal)) return source;

                // 仅修正 rootfile 的 full-path，保留 container 其余内容
                var fixedXml = xml.Replace(declared, actual, StringComparison.Ordinal);

                var tmp = cache + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
                File.Copy(source, tmp, true);
                try
                {
                    using (var update = ZipFile.Open(tmp, ZipArchiveMode.Update))
                    {
                        var e = FindEntry(update, "META-INF/container.xml");
                        if (e == null) { try { File.Delete(tmp); } catch { } return source; }
                        var name = e.FullName;
                        e.Delete();
                        var created = update.CreateEntry(name, CompressionLevel.Optimal);
                        using var w = new StreamWriter(created.Open(), new UTF8Encoding(false));
                        w.Write(fixedXml);
                    }
                    File.Move(tmp, cache, true);
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }

                Log.Info($"EPUB 路径大小写已修复：{Path.GetFileName(source)}（{declared} → {actual}）");
                return cache;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("EPUB 兼容修复失败 " + book.FilePath + ": " + ex.Message);
            return book.FilePath;
        }
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string name)
    {
        foreach (var e in zip.Entries)
            if (string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }
}
