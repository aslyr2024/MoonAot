using System;
using System.IO;

namespace MoonAot.Services.Zim;

/// <summary>
/// ZIM 簇解压编解码器：zstd（现代 ZIM 默认）与 xz/lzma2（老 ZIM）。
/// zstd 用 ZstdSharp（纯托管移植，AOT / 单文件发布友好），xz 用 SharpCompress。
/// </summary>
internal static class ZimCodec
{
    /// <summary>解压 zstd 数据流（簇内容长度未知，流式读到结尾）。</summary>
    public static byte[] DecompressZstd(byte[] payload)
    {
        using var src = new MemoryStream(payload, writable: false);
        using var ds = new ZstdSharp.DecompressionStream(src);
        return ReadAll(ds, payload.Length * 4);
    }

    /// <summary>解压 xz（LZMA2）数据流。</summary>
    public static byte[] DecompressXz(byte[] payload)
    {
        using var src = new MemoryStream(payload, writable: false);
        using var xz = new SharpCompress.Compressors.Xz.XZStream(src);
        return ReadAll(xz, payload.Length * 6);
    }

    /// <summary>
    /// 读干一个只进流。ZIM 簇尾部常带有填充/多帧，读到 0 字节即停止，
    /// 不能用 CopyTo 之后再判断长度（部分解压器会抛尾部异常，此处吞掉并保留已读数据）。
    /// </summary>
    private static byte[] ReadAll(Stream s, int capacityHint)
    {
        var ms = new MemoryStream(Math.Max(capacityHint, 64 * 1024));
        var buf = new byte[128 * 1024];
        try
        {
            while (true)
            {
                int n = s.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                ms.Write(buf, 0, n);
            }
        }
        catch when (ms.Length > 0)
        {
            // 尾部脏数据：已解出的部分仍然有效（簇偏移表在头部，不影响定位）
        }
        return ms.ToArray();
    }
}
