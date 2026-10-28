using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MoonAot.Services;

/// <summary>
/// 极简 ZIP 读取器（只读，按需解压），用于 EPUB 封面提取。
/// 解析中央目录，支持 Store(0)/Deflate(8) 两种压缩方式，支持 UTF-8 与 GBK 文件名。
/// </summary>
public sealed class ZipLite
{
    public sealed class ZipEntry
    {
        public string Name = "";
        public string NameLower = "";
        public bool IsDirectory;
        public long Size;
        public long CompressedSize;
        public ushort Method;
        public long LocalHeaderOffset;
    }

    private readonly string _path;
    private readonly List<ZipEntry> _entries;
    private readonly Encoding _gbk;

    private static readonly byte[] EocdSig = { 0x50, 0x4B, 0x05, 0x06 };
    private static readonly byte[] CentralSig = { 0x50, 0x4B, 0x01, 0x02 };
    private static readonly byte[] LocalSig = { 0x50, 0x4B, 0x03, 0x04 };

    private ZipLite(string path, List<ZipEntry> entries, Encoding gbk)
    {
        _path = path;
        _entries = entries;
        _gbk = gbk;
    }

    public IReadOnlyList<ZipEntry> Entries => _entries;

    /// <summary>打开 zip 并构建中央目录索引。失败返回 null。</summary>
    public static ZipLite? Open(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var eocd = FindEocd(fs);
            if (eocd < 0) return null;

            Span<byte> eocdBuf = stackalloc byte[22];
            fs.Position = eocd;
            if (fs.Read(eocdBuf) != 22) return null;
            // ⚠ EOCD「中央目录条目总数」只读 2 字节（偏移 10-11）
            int entriesTotal = BinaryPrimitives.ReadUInt16LittleEndian(eocdBuf[10..]);
            long cdSize = BinaryPrimitives.ReadUInt32LittleEndian(eocdBuf[12..]);
            long cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocdBuf[16..]);

            var list = new List<ZipEntry>(entriesTotal);
            Encoding gbk = Encoding.GetEncoding("GB18030");

            fs.Position = cdOffset;
            var buf = new byte[46];
            for (int i = 0; i < entriesTotal; i++)
            {
                if (fs.Read(buf) != 46) break;
                if (buf[0] != CentralSig[0] || buf[1] != CentralSig[1] || buf[2] != CentralSig[2] || buf[3] != CentralSig[3]) break;

                int method = BitConverter.ToUInt16(buf, 10);
                long compressedSize = BitConverter.ToUInt32(buf, 20);
                long size = BitConverter.ToUInt32(buf, 24);
                int nameLen = BitConverter.ToUInt16(buf, 28);
                int extraLen = BitConverter.ToUInt16(buf, 30);
                int commentLen = BitConverter.ToUInt16(buf, 32);
                int flags = BitConverter.ToUInt16(buf, 8);
                long localOffset = BitConverter.ToUInt32(buf, 42);

                var nameBytes = new byte[nameLen];
                if (fs.Read(nameBytes) != nameLen) break;
                fs.Seek(extraLen + commentLen, SeekOrigin.Current);

                var name = (flags & 0x800) != 0
                    ? Encoding.UTF8.GetString(nameBytes)
                    : SafeGbk(gbk, nameBytes);

                list.Add(new ZipEntry
                {
                    Name = name,
                    NameLower = name.Replace('\\', '/').ToLowerInvariant(),
                    IsDirectory = name.EndsWith("/", StringComparison.Ordinal),
                    Size = size,
                    CompressedSize = compressedSize,
                    Method = (ushort)method,
                    LocalHeaderOffset = localOffset,
                });
            }
            return new ZipLite(path, list, gbk);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeGbk(Encoding gbk, byte[] bytes)
    {
        try { return gbk.GetString(bytes); }
        catch { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>查找 EOCD 签名位置（向前扫描注释区，最多 64KB）。</summary>
    private static long FindEocd(FileStream fs)
    {
        long fileLen = fs.Length;
        long maxScan = Math.Min(fileLen, 22 + 0xFFFF);
        int bufLen = (int)Math.Min(maxScan, 0x10000 + 64);
        var buf = new byte[bufLen];
        fs.Seek(fileLen - bufLen, SeekOrigin.Begin);
        int read = fs.Read(buf);
        for (int i = read - 22; i >= 0; i--)
        {
            if (buf[i] == EocdSig[0] && buf[i + 1] == EocdSig[1] && buf[i + 2] == EocdSig[2] && buf[i + 3] == EocdSig[3])
                return fileLen - bufLen + i;
        }
        return -1;
    }

    /// <summary>按名称（忽略大小写）查找条目。</summary>
    public ZipEntry? Find(string name)
    {
        var nl = name.Replace('\\', '/').ToLowerInvariant();
        return _entries.FirstOrDefault(e => e.NameLower == nl);
    }

    /// <summary>读取条目原始字节（自动解压）。失败返回 null。</summary>
    public byte[]? Read(ZipEntry entry)
    {
        try
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[30];
            fs.Position = entry.LocalHeaderOffset;
            if (fs.Read(header) != 30) return null;
            if (header[0] != LocalSig[0] || header[1] != LocalSig[1] || header[2] != LocalSig[2] || header[3] != LocalSig[3]) return null;
            int nameLen = BitConverter.ToUInt16(header, 26);
            int extraLen = BitConverter.ToUInt16(header, 28);
            fs.Seek(nameLen + extraLen, SeekOrigin.Current);

            int flags = BitConverter.ToUInt16(header, 6);
            if ((flags & 0x1) != 0) return null; // 加密条目不支持

            if (entry.Method == 0)
            {
                var data = new byte[(int)entry.Size];
                int got = 0;
                while (got < entry.Size)
                {
                    int n = fs.Read(data, got, (int)(entry.Size - got));
                    if (n <= 0) break;
                    got += n;
                }
                return data;
            }
            if (entry.Method == 8)
            {
                // Deflate：去掉zlib 包装？zip 本身就是raw deflate
                using var ds = new DeflateStream(fs, CompressionMode.Decompress);
                using var ms = new MemoryStream((int)Math.Max(entry.Size, 64));
                ds.CopyTo(ms, 4096);
                var arr = ms.ToArray();
                if ((uint)arr.Length < (uint)entry.Size)
                {
                    // 大小以实际解压结果为准
                    return arr;
                }
                return arr;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
