using MoonAot.TinySearch.Storage;

namespace MoonAot.TinySearch.Scanning;

/// <summary>非 NTFS 盘（exFAT/FAT32 U盘等）的递归扫描；FRN 用路径 FNV 哈希模拟。
/// 产出紧凑 EntryStore（struct 记录，只存文件名不存路径）。</summary>
public static class GenericScanner
{
    public static EntryStore ScanStore(string drive, AppSettings settings, CancellationToken ct,
        IProgress<int>? progress = null)
    {
        var roots = RootsFor(drive, settings);
        var items = new Dictionary<long, EntryRecord>();
        long rootFrn = PathHash(drive + ":\\");

        // 包含模式下根可能不是盘根（如 D:\Books）：补齐从盘根到该目录的祖先记录，
        // 保证 GetPath 能沿 ParentFrn 链重建完整路径。
        foreach (var root in roots)
        {
            var norm = root.TrimEnd('\\');
            var chain = new List<string>();
            var cur = norm;
            while (!string.IsNullOrEmpty(cur) && cur.Length > 2)
            {
                chain.Add(cur);
                var parent = Path.GetDirectoryName(cur) ?? "";
                if (parent.Length <= 2) break;
                cur = parent;
            }
            foreach (var dir in chain)
            {
                var h = PathHash(dir);
                if (items.ContainsKey(h)) continue;
                var parent = Path.GetDirectoryName(dir) ?? "";
                items[h] = new EntryRecord
                {
                    ParentFrn = parent.Length <= 2 ? rootFrn : PathHash(parent),
                    Name = Path.GetFileName(dir),
                    IsDirectory = true,
                    Visible = true,
                };
            }
        }

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            Enumerate(root, drive, settings, items, ct, progress);
        }
        return EntryStore.FromScan(drive, rootFrn, items);
    }

    public static List<string> RootsFor(string drive, AppSettings settings)
    {
        if (settings.IncludedFolders.Count > 0)
        {
            return settings.IncludedFolders
                .Where(p => p.Length >= 1 && char.ToUpperInvariant(p[0]) == drive[0])
                .Where(Directory.Exists)
                .ToList();
        }
        var root = drive + ":\\";
        return Directory.Exists(root) ? new List<string> { root } : new List<string>();
    }

    private static void Enumerate(string root, string drive, AppSettings settings,
        Dictionary<long, EntryRecord> output, CancellationToken ct, IProgress<int>? progress)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        int n = 0;
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            if (PathFilter.IsExcluded(dir, settings) && dir != root) continue;

            IEnumerable<string> files, dirs;
            try
            {
                files = Directory.EnumerateFiles(dir);
                dirs = Directory.EnumerateDirectories(dir);
            }
            catch { continue; }

            long dirHash = PathHash(dir);
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(f);
                    output[PathHash(f)] = new EntryRecord
                    {
                        ParentFrn = dirHash,
                        Name = fi.Name,
                        Size = fi.Length,
                        ModifiedUtc = SafeTicks(() => fi.LastWriteTimeUtc),
                        Attributes = (uint)fi.Attributes,
                        Visible = true,
                    };
                }
                catch { }
                if (++n % 5000 == 0) progress?.Report(-n); // 无法预知总数，报负值=纯计数
            }
            foreach (var d in dirs)
            {
                try
                {
                    var di = new DirectoryInfo(d);
                    if (PathFilter.IsExcluded(di.FullName, settings)) continue;
                    output[PathHash(di.FullName)] = new EntryRecord
                    {
                        ParentFrn = dirHash,
                        Name = di.Name,
                        ModifiedUtc = SafeTicks(() => di.LastWriteTimeUtc),
                        Attributes = (uint)di.Attributes,
                        IsDirectory = true,
                        Visible = true,
                    };
                    pending.Push(di.FullName);
                }
                catch { }
            }
        }
        progress?.Report(100);
    }

    private static long SafeTicks(Func<DateTime> f)
    {
        try { return f().Ticks; } catch { return 0; }
    }

    /// <summary>FNV-1a 64bit 路径哈希（非 NTFS 盘的伪 FRN）。</summary>
    public static long PathHash(string path)
    {
        const ulong prime = 1099511628211UL;
        ulong hash = 14695981039346656037UL;
        foreach (char c in path)
        {
            hash ^= char.ToUpperInvariant(c);
            hash *= prime;
        }
        return (long)(hash & 0x7FFFFFFFFFFFFFFF);
    }
}

/// <summary>排除/包含路径过滤（NTFS 与通用扫描共用）。</summary>
public static class PathFilter
{
    public static bool IsExcluded(string path, AppSettings s)
    {
        foreach (var ex in s.ExcludedFolders)
            if (UnderPath(path, ex)) return true;
        return false;
    }

    public static bool IsIncluded(string path, AppSettings s)
    {
        if (s.IncludedFolders.Count == 0) return true;
        foreach (var inc in s.IncludedFolders)
            if (UnderPath(path, inc)) return true;
        return false;
    }

    private static bool UnderPath(string path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath)) return false;
        basePath = basePath.TrimEnd('\\');
        if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return false;
        return path.Length == basePath.Length || path[basePath.Length] == '\\';
    }

    /// <summary>组合过滤：包含规则 AND 非排除。</summary>
    public static Func<string, bool> Keep(AppSettings s) =>
        p => !IsExcluded(p, s) && IsIncluded(p, s);
}
