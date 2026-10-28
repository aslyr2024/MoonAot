using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>
/// 封面与图片缩略图服务（GDI+，NativeAOT 已验证）。
/// 书架直接使用封面主文件（≤48KB），不再单独保存 320×480 缩略图；
/// 媒体库仍按路径生成小缩略图。
/// </summary>
public static class ThumbnailService
{
    /// <summary>封面体积上限（字节）：48KB，确保按十进制也低于 50KB。</summary>
    public const int MaxCoverBytes = 48 * 1024;
    /// <summary>无封面标记版本；旧版迁移过来的 "1" 标记会自动失效重试。</summary>
    public const string MarkerVersion = "2";

    private static readonly SemaphoreSlim Gate = new(3, 3);
    private static readonly ImageCodecInfo? JpegEncoder = FindJpegEncoder();

    private static ImageCodecInfo? FindJpegEncoder()
    {
        foreach (var c in ImageCodecInfo.GetImageEncoders())
            if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
        return null;
    }

    /// <summary>
    /// 取某本书的封面文件；书架与详情共用同一份 ≤48KB 主封面，没有封面时返回 null。
    /// 不会在这里提取封面（避免滚动书架时逐本解析电子书）。
    /// </summary>
    public static string? GetBookThumb(BookRecord book)
    {
        var cover = DbStore.Instance.GetCoverFile(book.Id);
        if (!string.IsNullOrEmpty(cover) && File.Exists(cover)) return cover;
        if (!string.IsNullOrEmpty(book.CoverFile) && File.Exists(book.CoverFile)) return book.CoverFile;
        return null;
    }

    private static bool HasValidNoCoverMarker(string id)
    {
        var marker = DbStore.Instance.NoCoverMarker(id);
        if (!File.Exists(marker)) return false;
        try
        {
            if (string.Equals(File.ReadAllText(marker).Trim(), MarkerVersion, StringComparison.Ordinal))
                return true;
            File.Delete(marker);
        }
        catch { }
        return false;
    }

    private static void TryWriteMarker(string id)
    {
        try { File.WriteAllText(DbStore.Instance.NoCoverMarker(id), MarkerVersion); } catch { }
    }

    /// <summary>标记该书没有可用封面，避免启动/补封面时反复解析损坏文件。</summary>
    public static void MarkNoCover(string id) => TryWriteMarker(id);

    /// <summary>是否已有“无封面”标记（重建封面会清除）。</summary>
    public static bool HasNoCoverMarker(string id) => HasValidNoCoverMarker(id);

    /// <summary>
    /// 保存从书里提取的封面：优先转成 ≤48KB 的 JPEG；GDI+ 无法解码的格式（如 AVIF）保留原始字节。
    /// 返回最终封面路径。
    /// </summary>
    public static string? SaveExtractedCover(string bookId, byte[] data, string ext)
    {
        try
        {
            var jpgPath = DbStore.Instance.CoverPathFor(bookId, ".jpg");
            var rawPath = jpgPath + ".src" + (ext.StartsWith('.') ? ext : "." + ext);
            WriteFileAtomic(rawPath, data);
            var ok = CompressFileToJpeg(rawPath, jpgPath);
            try { File.Delete(rawPath); } catch { }
            if (ok) return jpgPath;

            var fallback = DbStore.Instance.CoverPathFor(bookId, ext);
            WriteFileAtomic(fallback, data);
            return fallback;
        }
        catch (Exception ex)
        {
            Log.Warn($"保存封面失败 {bookId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>把任意封面图压缩为不超过 maxBytes 的 JPEG（最长边 maxWidth/maxHeight）。</summary>
    public static bool StoreCoverJpeg(Image img, string outPath, int maxWidth = 800, int maxHeight = 1100)
    {
        try
        {
            double s = Math.Min(1d, Math.Min((double)maxWidth / img.Width, (double)maxHeight / img.Height));
            int w = Math.Max(1, (int)Math.Round(img.Width * s));
            int h = Math.Max(1, (int)Math.Round(img.Height * s));
            using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.FromArgb(18, 20, 24));
                g.DrawImage(img, new Rectangle(0, 0, w, h));
            }
            SaveJpegMax(bmp, outPath, MaxCoverBytes);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"压缩封面失败 {outPath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>把磁盘上的图片压缩为 ≤48KB 的 JPEG；源格式无法解码时返回 false。</summary>
    public static bool CompressFileToJpeg(string sourcePath, string outPath)
    {
        using var img = LoadImage(sourcePath);
        if (img == null) return false;
        return StoreCoverJpeg(img, outPath);
    }

    /// <summary>按质量/尺寸逐级压缩，直到落盘体积不超过 maxBytes。</summary>
    public static void SaveJpegMax(Bitmap bmp, string path, int maxBytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        Bitmap current = bmp;
        bool own = false;
        try
        {
            for (int resize = 0; resize < 4; resize++)
            {
                for (long q = 88; q >= 38; q -= 6)
                {
                    var bytes = EncodeJpeg(current, q);
                    if (bytes.Length <= maxBytes)
                    {
                        WriteFileAtomic(path, bytes);
                        return;
                    }
                }
                int w = Math.Max(48, (int)(current.Width * 0.82));
                int h = Math.Max(48, (int)(current.Height * 0.82));
                if (w == current.Width && h == current.Height) break;
                var next = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(next))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.FromArgb(18, 20, 24));
                    g.DrawImage(current, 0, 0, w, h);
                }
                if (own) current.Dispose();
                current = next;
                own = true;
            }
            WriteFileAtomic(path, EncodeJpeg(current, 38));
        }
        finally
        {
            if (own) current.Dispose();
        }
    }

    private static byte[] EncodeJpeg(Bitmap bmp, long quality)
    {
        using var ms = new MemoryStream();
        using var ps = new EncoderParameters(1);
        ps.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        if (JpegEncoder != null) bmp.Save(ms, JpegEncoder, ps);
        else bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    /// <summary>任意图片（媒体库/漫画）缩略图，按修改时间失效。返回 null 表示无法解码。</summary>
    public static string? GetPathThumb(string path, int width = 320, int height = 320)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            var key = HashPath(path + "|" + fi.LastWriteTimeUtc.Ticks + "|" + width + "x" + height);
            var dir = Path.Combine(DbStore.Instance.ThumbsDir, "path");
            Directory.CreateDirectory(dir);
            var outPath = Path.Combine(dir, key + ".jpg");
            if (File.Exists(outPath)) return outPath;
            if (!Gate.Wait(5000)) return null;
            try
            {
                if (File.Exists(outPath)) return outPath;
                using var img = LoadImage(path);
                if (img == null) return null;
                using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.FromArgb(18, 20, 24));
                    double s = Math.Min((double)width / img.Width, (double)height / img.Height);
                    int w = Math.Max(1, (int)Math.Round(img.Width * s));
                    int h = Math.Max(1, (int)Math.Round(img.Height * s));
                    int x = (width - w) / 2;
                    int y = (height - h) / 2;
                    g.DrawImage(img, new Rectangle(x, y, w, h));
                }
                SaveJpeg(bmp, outPath, 84);
                return outPath;
            }
            finally { Gate.Release(); }
        }
        catch (Exception ex)
        {
            Log.Warn($"生成图片缩略图失败 {path}: {ex.Message}");
            return null;
        }
    }

    public static Image? LoadImage(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
            // 复制到内存流：避免 GDI+ 持有文件句柄导致文件无法移动/删除
            using var ms = new MemoryStream((int)Math.Min(fs.Length, 64L * 1024 * 1024));
            fs.CopyTo(ms);
            ms.Position = 0;
            var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            // 转成 32bpp ARGB 副本，脱离底层流
            var copy = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy)) g.DrawImage(img, 0, 0, img.Width, img.Height);
            img.Dispose();
            return copy;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveJpeg(Bitmap bmp, string path, long quality)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
        using (var ps = new EncoderParameters(1))
        {
            ps.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            if (JpegEncoder != null) bmp.Save(tmp, JpegEncoder, ps);
            else bmp.Save(tmp, ImageFormat.Jpeg);
        }
        File.Move(tmp, path, true);
    }

    private static void WriteFileAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, true);
    }

    private static string HashPath(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant()[..24];
}
