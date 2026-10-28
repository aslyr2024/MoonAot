using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MoonAot.Services.Zim;

/// <summary>
/// ZIM 文件条目（目录项 / Directory Entry）。
/// </summary>
public sealed class ZimEntry
{
    /// <summary>在 URL 指针表中的下标（即“条目号”）</summary>
    public int Index;
    /// <summary>命名空间字符：新格式 ZIM 内容统一为 'C'，元数据 'M'，索引 'X'，well-known 'W'</summary>
    public char Namespace;
    /// <summary>条目 URL（不含命名空间前缀），例如 "北京市"</summary>
    public string Url = "";
    /// <summary>条目标题；ZIM 中标题为空表示与 URL 相同</summary>
    public string Title = "";
    /// <summary>是否为重定向条目</summary>
    public bool IsRedirect;
    /// <summary>重定向目标条目号（仅 IsRedirect 时有效）</summary>
    public int RedirectIndex;
    /// <summary>所在簇号（非重定向时有效）</summary>
    public int ClusterNumber;
    /// <summary>簇内 blob 号（非重定向时有效）</summary>
    public int BlobNumber;
    /// <summary>MIME 类型在 MIME 列表中的下标</summary>
    public ushort MimeIndex;
    /// <summary>完整路径，形如 "C/北京市"</summary>
    public string FullPath => Namespace + "/" + Url;
}

/// <summary>
/// ZIM（openZIM）文件只读解析器 —— 功能对标 kiwix-js 的 zimfile / zimDirEntry / zimArchive。
///
/// 支持：
///   * ZIM 5/6 头部、MIME 列表、URL 指针表、簇指针表；
///   * 目录项解析（普通条目 / 重定向条目）；
///   * 簇解压：未压缩(0/1)、xz(4)、zstd(5)，支持扩展簇（64 位偏移）；
///   * 按 (namespace, url) 二分查找、重定向解析；
///   * 标题顺序索引（新格式：X/listing/titleOrdered/v1；旧格式：头部 titlePtrPos）做前缀搜索；
///   * 簇 LRU 缓存，避免反复解压同一簇。
///
/// 线程安全：底层用 SafeFileHandle + RandomAccess（无共享文件指针），缓存加锁，可被 Kestrel 并发调用。
/// </summary>
public sealed class ZimFile : IDisposable
{
    // ===== 头部字段 =====
    /// <summary>ZIM 魔数（0x44D495A）</summary>
    private const uint Magic = 72173914;

    private readonly SafeFileHandle _handle;
    private readonly long _fileSize;

    /// <summary>ZIM 文件绝对路径</summary>
    public string FilePath { get; }
    /// <summary>主版本号（5 / 6）</summary>
    public ushort MajorVersion { get; private set; }
    /// <summary>次版本号（&gt;=1 表示新命名空间方案，内容全部位于 C 命名空间）</summary>
    public ushort MinorVersion { get; private set; }
    /// <summary>条目总数</summary>
    public int EntryCount { get; private set; }
    /// <summary>簇总数</summary>
    public int ClusterCount { get; private set; }
    /// <summary>URL 指针表偏移</summary>
    private long _urlPtrPos;
    /// <summary>标题指针表偏移（旧格式；新格式为 0xFFFF... 表示不存在）</summary>
    private long _titlePtrPos;
    /// <summary>簇指针表偏移</summary>
    private long _clusterPtrPos;
    /// <summary>MIME 列表偏移</summary>
    private long _mimeListPos;
    /// <summary>校验和偏移（同时用作最后一个簇的结束边界）</summary>
    private long _checksumPos;
    /// <summary>主页条目号（0xFFFFFFFF 表示无）</summary>
    public uint MainPageIndex { get; private set; }
    /// <summary>MIME 类型表</summary>
    public string[] MimeTypes { get; private set; } = Array.Empty<string>();

    /// <summary>是否为新命名空间格式（内容位于 C 命名空间）</summary>
    public bool IsNewNamespaceScheme => MajorVersion >= 6 && MinorVersion >= 1;
    /// <summary>内容命名空间：新格式 'C'，旧格式 'A'</summary>
    public char ContentNamespace => IsNewNamespaceScheme ? 'C' : 'A';

    // ===== 缓存 =====
    /// <summary>簇解压结果 LRU 缓存</summary>
    private readonly Dictionary<int, byte[]> _clusterCache = new();
    private readonly LinkedList<int> _clusterLru = new();
    private readonly object _cacheLock = new();
    /// <summary>簇缓存容量上限（字节）</summary>
    private const long ClusterCacheLimit = 96L * 1024 * 1024;
    private long _clusterCacheBytes;

    /// <summary>标题顺序索引（uint32 条目号数组的原始字节），懒加载</summary>
    private byte[]? _titleIndex;
    private readonly object _titleIndexLock = new();
    /// <summary>标题索引条目数</summary>
    public int TitleIndexCount => (_titleIndex?.Length ?? 0) / 4;

    /// <summary>元数据缓存（M 命名空间）</summary>
    private readonly Dictionary<string, string> _metaCache = new(StringComparer.OrdinalIgnoreCase);

    private ZimFile(string path)
    {
        FilePath = path;
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                  FileOptions.RandomAccess);
        _fileSize = RandomAccess.GetLength(_handle);
    }

    /// <summary>打开并解析 ZIM 文件头。失败抛 InvalidDataException。</summary>
    public static ZimFile Open(string path)
    {
        var zim = new ZimFile(path);
        try { zim.ReadHeader(); }
        catch { zim.Dispose(); throw; }
        return zim;
    }

    // ===================== 低层读取 =====================

    private byte[] Read(long offset, int length)
    {
        if (offset < 0 || length <= 0 || offset >= _fileSize) return Array.Empty<byte>();
        if (offset + length > _fileSize) length = (int)(_fileSize - offset);
        var buf = new byte[length];
        int got = 0;
        while (got < length)
        {
            int n = RandomAccess.Read(_handle, buf.AsSpan(got), offset + got);
            if (n <= 0) break;
            got += n;
        }
        if (got != length) Array.Resize(ref buf, got);
        return buf;
    }

    private uint ReadUInt32(long offset) => BinaryPrimitives.ReadUInt32LittleEndian(Read(offset, 4));
    private ulong ReadUInt64(long offset) => BinaryPrimitives.ReadUInt64LittleEndian(Read(offset, 8));

    private void ReadHeader()
    {
        var h = Read(0, 80);
        if (h.Length < 80) throw new InvalidDataException("文件过小，不是有效的 ZIM 文件");
        if (BinaryPrimitives.ReadUInt32LittleEndian(h) != Magic)
            throw new InvalidDataException("魔数不匹配，不是有效的 ZIM 文件");

        MajorVersion = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(4));
        MinorVersion = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(6));
        // 8..24: UUID
        EntryCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(24));
        ClusterCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(28));
        _urlPtrPos = (long)BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(32));
        _titlePtrPos = (long)BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(40));
        _clusterPtrPos = (long)BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(48));
        _mimeListPos = (long)BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(56));
        MainPageIndex = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(64));
        _checksumPos = (long)BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(72));
        if (_checksumPos <= 0 || _checksumPos > _fileSize) _checksumPos = _fileSize;

        if (EntryCount <= 0 || _urlPtrPos <= 0 || _clusterPtrPos <= 0)
            throw new InvalidDataException("ZIM 头部字段异常");

        ReadMimeList();
    }

    private void ReadMimeList()
    {
        var list = new List<string>();
        long pos = _mimeListPos;
        var buf = Read(pos, 8192);
        int p = 0;
        while (p < buf.Length)
        {
            int end = Array.IndexOf(buf, (byte)0, p);
            if (end < 0) break;
            if (end == p) break;                       // 空字符串 → 列表结束
            list.Add(Encoding.UTF8.GetString(buf, p, end - p));
            p = end + 1;
            if (list.Count > 4096) break;
        }
        MimeTypes = list.ToArray();
    }

    /// <summary>根据 MIME 下标取 MIME 字符串</summary>
    public string GetMime(ushort index)
        => index < MimeTypes.Length ? MimeTypes[index] : "application/octet-stream";

    // ===================== 目录项 =====================

    /// <summary>读取第 index 个目录项（按 URL 顺序）。</summary>
    public ZimEntry? GetEntry(int index)
    {
        if (index < 0 || index >= EntryCount) return null;
        long off = (long)ReadUInt64(_urlPtrPos + 8L * index);
        if (off <= 0 || off >= _fileSize) return null;

        // 目录项长度不定（URL/标题为变长字符串），先读一块，不够再扩读
        int size = 512;
        byte[] b = Read(off, size);
        while (true)
        {
            var e = TryParseDirent(b, index);
            if (e != null) return e;
            if (size >= 65536 || off + size >= _fileSize) return null;
            size *= 4;
            b = Read(off, size);
        }
    }

    /// <summary>解析目录项字节；字符串未截断完整时返回 null（提示调用方扩读）。</summary>
    private ZimEntry? TryParseDirent(byte[] b, int index)
    {
        if (b.Length < 16) return null;
        ushort mime = BinaryPrimitives.ReadUInt16LittleEndian(b);
        char ns = (char)b[3];
        var e = new ZimEntry { Index = index, MimeIndex = mime, Namespace = ns };

        int p;
        if (mime == 0xFFFF)                       // 重定向
        {
            e.IsRedirect = true;
            e.RedirectIndex = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8));
            p = 12;
        }
        else if (mime == 0xFFFE || mime == 0xFFFD) // linktarget / deleted：无内容
        {
            e.ClusterNumber = -1;
            p = 12;
        }
        else
        {
            e.ClusterNumber = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8));
            e.BlobNumber = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12));
            p = 16;
        }

        int u = Array.IndexOf(b, (byte)0, p);
        if (u < 0) return null;
        e.Url = Encoding.UTF8.GetString(b, p, u - p);
        int t = Array.IndexOf(b, (byte)0, u + 1);
        if (t < 0) return null;
        e.Title = t == u + 1 ? e.Url : Encoding.UTF8.GetString(b, u + 1, t - u - 1);
        return e;
    }

    /// <summary>仅读取用于二分比较的 (namespace, urlBytes)，避免构造完整对象。</summary>
    private (char Ns, byte[] Url)? GetKey(int index)
    {
        long off = (long)ReadUInt64(_urlPtrPos + 8L * index);
        if (off <= 0 || off >= _fileSize) return null;
        int size = 256;
        while (true)
        {
            var b = Read(off, size);
            if (b.Length < 16) return null;
            ushort mime = BinaryPrimitives.ReadUInt16LittleEndian(b);
            char ns = (char)b[3];
            int p = mime is 0xFFFF or 0xFFFE or 0xFFFD ? 12 : 16;
            int u = Array.IndexOf(b, (byte)0, p);
            if (u >= 0)
            {
                var url = new byte[u - p];
                Array.Copy(b, p, url, 0, url.Length);
                return (ns, url);
            }
            if (size >= 65536) return null;
            size *= 4;
        }
    }

    /// <summary>UTF-8 字节序比较（与 libzim 的排序一致）</summary>
    private static int CompareBytes(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
        }
        return a.Length.CompareTo(b.Length);
    }

    /// <summary>按 (命名空间, URL) 二分查找条目；未找到返回 null。</summary>
    public ZimEntry? FindEntry(char ns, string url)
    {
        var target = Encoding.UTF8.GetBytes(url);
        int lo = 0, hi = EntryCount;
        while (lo < hi)
        {
            int mid = (int)(((long)lo + hi) / 2);
            var k = GetKey(mid);
            if (k == null) { hi = mid; continue; }
            int cmp = k.Value.Ns.CompareTo(ns);
            if (cmp == 0) cmp = CompareBytes(k.Value.Url, target);
            if (cmp < 0) lo = mid + 1; else hi = mid;
        }
        if (lo >= EntryCount) return null;
        var e = GetEntry(lo);
        if (e == null || e.Namespace != ns || e.Url != url) return null;
        return e;
    }

    /// <summary>
    /// 按“路径”查找条目，兼容多种写法：
    ///   "C/北京市"（带命名空间）、"北京市"（不带，按内容命名空间）、
    ///   旧格式 "A/xxx"、"I/xxx"、"-/xxx" 等。找不到时按常用命名空间逐一回退。
    /// </summary>
    public ZimEntry? FindByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        path = path.TrimStart('/');
        if (path.Length == 0) return null;

        // 形如 "X/rest"：单字符命名空间
        if (path.Length > 1 && path[1] == '/')
        {
            var hit = FindEntry(path[0], path[2..]);
            if (hit != null) return hit;
        }
        // 直接按内容命名空间
        var e = FindEntry(ContentNamespace, path);
        if (e != null) return e;

        // 回退：其它常见命名空间（兼容旧格式 ZIM 的相对链接）
        foreach (var ns in new[] { 'C', 'A', 'I', '-', 'J', 'M', 'W', 'X' })
        {
            if (ns == ContentNamespace) continue;
            e = FindEntry(ns, path);
            if (e != null) return e;
        }
        return null;
    }

    /// <summary>解析重定向链，返回最终的内容条目（最多 10 跳，防环）。</summary>
    public ZimEntry? Resolve(ZimEntry? entry)
    {
        int guard = 0;
        while (entry != null && entry.IsRedirect && guard++ < 10)
            entry = GetEntry(entry.RedirectIndex);
        return entry is { IsRedirect: false } ? entry : null;
    }

    /// <summary>主页条目（W/mainPage 重定向已解析）。</summary>
    public ZimEntry? GetMainPage()
    {
        ZimEntry? e = null;
        if (MainPageIndex != uint.MaxValue && MainPageIndex < EntryCount)
            e = GetEntry((int)MainPageIndex);
        e ??= FindEntry('W', "mainPage");
        return Resolve(e);
    }

    // ===================== 簇 / Blob =====================

    /// <summary>取簇 n 的文件区间 [start, end)。</summary>
    private (long Start, long End) ClusterRange(int n)
    {
        long start = (long)ReadUInt64(_clusterPtrPos + 8L * n);
        long end = n + 1 < ClusterCount ? (long)ReadUInt64(_clusterPtrPos + 8L * (n + 1)) : _checksumPos;
        if (end <= start || end > _fileSize) end = _fileSize;
        return (start, end);
    }

    /// <summary>读取并解压整个簇（带 LRU 缓存）。</summary>
    private byte[] GetCluster(int n)
    {
        if (n < 0 || n >= ClusterCount) return Array.Empty<byte>();
        lock (_cacheLock)
        {
            if (_clusterCache.TryGetValue(n, out var hit))
            {
                _clusterLru.Remove(n); _clusterLru.AddLast(n);
                return hit;
            }
        }

        var (start, end) = ClusterRange(n);
        if (start <= 0 || end <= start) return Array.Empty<byte>();

        byte compressionInfo = Read(start, 1) is { Length: 1 } ib ? ib[0] : (byte)0;
        int comp = compressionInfo & 0x0F;
        var payload = Read(start + 1, (int)Math.Min(end - start - 1, int.MaxValue));

        byte[] raw;
        try
        {
            raw = comp switch
            {
                0 or 1 => payload,                              // 未压缩
                4 => ZimCodec.DecompressXz(payload),            // xz / lzma2
                5 => ZimCodec.DecompressZstd(payload),          // zstd（现代 ZIM 默认）
                2 or 3 => throw new NotSupportedException($"不支持的 ZIM 簇压缩类型：{comp}（zlib/bzip2 已废弃）"),
                _ => throw new NotSupportedException($"未知的 ZIM 簇压缩类型：{comp}")
            };
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"簇 {n} 解压失败：{ex.Message}", ex);
        }

        lock (_cacheLock)
        {
            if (!_clusterCache.ContainsKey(n))
            {
                _clusterCache[n] = raw;
                _clusterLru.AddLast(n);
                _clusterCacheBytes += raw.LongLength;
                while (_clusterCacheBytes > ClusterCacheLimit && _clusterLru.First != null)
                {
                    int victim = _clusterLru.First.Value;
                    _clusterLru.RemoveFirst();
                    if (_clusterCache.Remove(victim, out var old)) _clusterCacheBytes -= old.LongLength;
                }
            }
        }
        return raw;
    }

    /// <summary>簇内偏移表读取（普通簇 4 字节 / 扩展簇 8 字节）。</summary>
    private static long OffsetAt(byte[] raw, int i, bool extended)
        => extended
            ? (long)BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(i * 8))
            : BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4));

    /// <summary>读取条目内容（自动解析重定向）。返回 null 表示无内容。</summary>
    public byte[]? ReadContent(ZimEntry entry)
    {
        var e = Resolve(entry);
        if (e == null || e.ClusterNumber < 0) return null;

        var (start, _) = ClusterRange(e.ClusterNumber);
        byte info = Read(start, 1) is { Length: 1 } ib ? ib[0] : (byte)0;
        bool extended = (info & 0x10) != 0;

        var raw = GetCluster(e.ClusterNumber);
        int psz = extended ? 8 : 4;
        if (raw.Length < psz * 2) return null;

        long first = OffsetAt(raw, 0, extended);
        int blobCount = (int)(first / psz) - 1;
        if (e.BlobNumber < 0 || e.BlobNumber >= blobCount) return null;

        long s = OffsetAt(raw, e.BlobNumber, extended);
        long t = OffsetAt(raw, e.BlobNumber + 1, extended);
        if (s < 0 || t < s || t > raw.LongLength) return null;

        var data = new byte[t - s];
        Array.Copy(raw, s, data, 0, data.Length);
        return data;
    }

    // ===================== 标题索引与搜索 =====================

    /// <summary>加载标题顺序索引：新格式取 X/listing/titleOrdered/v1，旧格式取头部 titlePtrPos。</summary>
    private byte[] GetTitleIndex()
    {
        if (_titleIndex != null) return _titleIndex;
        lock (_titleIndexLock)
        {
            if (_titleIndex != null) return _titleIndex;

            byte[]? data = null;
            var listing = FindEntry('X', "listing/titleOrdered/v1");
            if (listing != null) data = ReadContent(listing);

            if (data == null && _titlePtrPos > 0 && (ulong)_titlePtrPos != ulong.MaxValue
                && _titlePtrPos < _fileSize)
                data = Read(_titlePtrPos, EntryCount * 4);

            _titleIndex = data ?? Array.Empty<byte>();
            return _titleIndex;
        }
    }

    /// <summary>标题索引第 i 项对应的条目号。</summary>
    private int TitleIndexEntry(byte[] idx, int i)
        => (int)BinaryPrimitives.ReadUInt32LittleEndian(idx.AsSpan(i * 4));

    /// <summary>取用于排序/匹配的标题（标题为空时用 URL）。</summary>
    private static string SortKey(ZimEntry e) => string.IsNullOrEmpty(e.Title) ? e.Url : e.Title;

    /// <summary>
    /// 标题前缀搜索（等价于 kiwix-js 的 searchDirEntriesFromPrefix）。
    /// 在标题顺序索引上二分定位下界，再顺序扫描收集前缀匹配项。
    /// </summary>
    public List<ZimEntry> SearchByTitlePrefix(string prefix, int limit = 25)
    {
        var result = new List<ZimEntry>();
        if (string.IsNullOrWhiteSpace(prefix)) return result;

        var idx = GetTitleIndex();
        var seen = new HashSet<int>();

        // 中文无大小写；英文尝试原样 + 首字母大写两种前缀，贴近 kiwix 行为
        foreach (var p in Variants(prefix))
        {
            if (result.Count >= limit) break;
            if (idx.Length >= 4) CollectFromTitleIndex(idx, p, limit, result, seen);
            else CollectFromUrlList(p, limit, result, seen);
        }
        return result;
    }

    private static IEnumerable<string> Variants(string prefix)
    {
        yield return prefix;
        if (prefix.Length > 0 && char.IsLower(prefix[0]))
            yield return char.ToUpperInvariant(prefix[0]) + prefix[1..];
        if (prefix.Contains(' ')) yield return prefix.Replace(' ', '_');
    }

    private void CollectFromTitleIndex(byte[] idx, string prefix, int limit,
                                       List<ZimEntry> result, HashSet<int> seen)
    {
        int count = idx.Length / 4;
        var target = Encoding.UTF8.GetBytes(prefix);

        int lo = 0, hi = count;
        while (lo < hi)
        {
            int mid = (int)(((long)lo + hi) / 2);
            var e = GetEntry(TitleIndexEntry(idx, mid));
            if (e == null) { hi = mid; continue; }
            int cmp = CompareBytes(Encoding.UTF8.GetBytes(SortKey(e)), target);
            if (cmp < 0) lo = mid + 1; else hi = mid;
        }

        for (int i = lo; i < count && result.Count < limit; i++)
        {
            var e = GetEntry(TitleIndexEntry(idx, i));
            if (e == null) continue;
            var key = SortKey(e);
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) break;
            if (e.Namespace != ContentNamespace) continue;
            if (seen.Add(e.Index)) result.Add(e);
        }
    }

    /// <summary>无标题索引时的兜底：在 URL 顺序表上做前缀搜索。</summary>
    private void CollectFromUrlList(string prefix, int limit, List<ZimEntry> result, HashSet<int> seen)
    {
        var target = Encoding.UTF8.GetBytes(prefix.Replace(' ', '_'));
        int lo = 0, hi = EntryCount;
        while (lo < hi)
        {
            int mid = (int)(((long)lo + hi) / 2);
            var k = GetKey(mid);
            if (k == null) { hi = mid; continue; }
            int cmp = k.Value.Ns.CompareTo(ContentNamespace);
            if (cmp == 0) cmp = CompareBytes(k.Value.Url, target);
            if (cmp < 0) lo = mid + 1; else hi = mid;
        }
        for (int i = lo; i < EntryCount && result.Count < limit; i++)
        {
            var e = GetEntry(i);
            if (e == null || e.Namespace != ContentNamespace) break;
            if (!e.Url.StartsWith(prefix.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)) break;
            if (seen.Add(e.Index)) result.Add(e);
        }
    }

    /// <summary>随机取一个 HTML 文章条目。</summary>
    public ZimEntry? GetRandomArticle()
    {
        var rnd = Random.Shared;
        for (int i = 0; i < 60; i++)
        {
            var e = GetEntry(rnd.Next(EntryCount));
            e = Resolve(e);
            if (e == null || e.Namespace != ContentNamespace) continue;
            if (GetMime(e.MimeIndex).StartsWith("text/html", StringComparison.OrdinalIgnoreCase)) return e;
        }
        return GetMainPage();
    }

    /// <summary>读取 M 命名空间元数据（Title / Description / Language / Date / Creator …）。</summary>
    public string GetMetadata(string name)
    {
        lock (_metaCache)
        {
            if (_metaCache.TryGetValue(name, out var v)) return v;
        }
        string value = "";
        try
        {
            var e = FindEntry('M', name);
            if (e != null)
            {
                var d = ReadContent(e);
                if (d != null) value = Encoding.UTF8.GetString(d);
            }
        }
        catch { }
        lock (_metaCache) _metaCache[name] = value;
        return value;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_cacheLock)
        {
            _clusterCache.Clear(); _clusterLru.Clear(); _clusterCacheBytes = 0;
        }
        _titleIndex = null;
        _handle.Dispose();
    }
}
