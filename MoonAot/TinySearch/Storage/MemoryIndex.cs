using System.Collections.Concurrent;

namespace MoonAot.TinySearch.Storage;

/// <summary>
/// 纯内存文件索引：每盘一个 EntryStore（FRN → 紧凑 struct 记录），
/// 不落 SQLite；索引变更时版本号递增，供搜索结果缓存失效判断。
/// </summary>
public sealed class MemoryIndex
{
    private readonly ConcurrentDictionary<string, EntryStore> _stores = new(StringComparer.OrdinalIgnoreCase);
    private long _version;

    /// <summary>索引版本号（任何变更递增）。搜索缓存据此判断是否需要重算。</summary>
    public long Version => Interlocked.Read(ref _version);
    public long BumpVersion() => Interlocked.Increment(ref _version);

    public int DriveCount => _stores.Count;

    public long TotalCount()
    {
        long n = 0;
        foreach (var s in _stores.Values) n += s.Count;
        return n;
    }

    public int Count(string drive) => _stores.TryGetValue(drive, out var s) ? s.Count : 0;

    /// <summary>整盘原子替换（扫描完成后发布；旧存储整体丢弃）。</summary>
    public void SetStore(string drive, EntryStore store)
    {
        _stores[drive] = store;
        BumpVersion();
    }

    public void RemoveDrive(string drive)
    {
        if (_stores.TryRemove(drive, out _)) BumpVersion();
    }

    public EntryStore? GetStore(string drive) => _stores.TryGetValue(drive, out var s) ? s : null;

    /// <summary>各盘存储的稳定快照（一次搜索全程使用，结果缓存也持有它）。</summary>
    public EntryStore[] StoresSnapshot() => _stores.Values.ToArray();
}
