using System.Collections.Generic;
using System.Text;

namespace MoonAot.TinySearch.Storage;

/// <summary>
/// 单盘内存索引（Everything 风格）：FRN → 紧凑 EntryRecord（struct）。
/// 不存完整路径：GetPath 沿 ParentFrn 链按需重建，无任何路径缓存，大幅降低常驻内存。
/// 被排除目录（如 $Recycle.Bin）的记录仍保留（Visible=false），保证路径链可回溯。
/// 读多写少：搜索/遍历并发读（读锁），USN/扫描结果批量写（写锁）。
///
/// 存储后端双模式：
///  - NTFS：FRN 即 MFT 记录号（连续整数），用 <see cref="_arr"/> 直接按下标存取，
///    零字典开销（这是 Everything 真正的内存模型，比 Dictionary 省约 1/3 常驻）。
///    实时变更中可能出现的"越界 FRN"（MFT 增长后的新记录号）落入 <see cref="_overflow"/> 小字典，
///    正常规模下 overflow 几乎为空，不抵消数组收益。
///  - 非 NTFS（exFAT/FAT32）：FRN 为路径 FNV 哈希（稀疏、非连续），只能用 <see cref="_dict"/>。
/// </summary>
public sealed class EntryStore : IDisposable
{
    public const long NtfsRootFrn = 5;

    /// <summary>盘符，如 "C"</summary>
    public readonly string Drive;

    /// <summary>根 FRN：NTFS=5；非 NTFS=根路径哈希（根直属子项的 ParentFrn 值）。根本身无记录。</summary>
    public readonly long RootFrn;

    /// <summary>NTFS 主存储：下标=FRN。长度=初始最大 FRN+1，空槽 Name.Length==0。</summary>
    private EntryRecord[]? _arr;

    /// <summary>NTFS 越界 FRN（大于 _arr 长度）或 非 NTFS 全量存储。</summary>
    private Dictionary<long, EntryRecord>? _dict;

    /// <summary>是否数组模式（仅 NTFS）。</summary>
    private readonly bool _arrayMode;

    private int _count;

    private readonly ReaderWriterLockSlim _rw = new();

    private EntryStore(string drive, long rootFrn, bool arrayMode)
    {
        Drive = drive;
        RootFrn = rootFrn;
        _arrayMode = arrayMode;
    }

    /// <summary>
    /// 整盘批量装载（全量扫描结果一次性灌入；发布前单线程访问，不走锁）。
    /// useArray=true（NTFS）：FRN 连续 → 按下标建数组，零字典开销；
    /// useArray=false（非 NTFS）：FRN 为稀疏哈希 → 保留字典。
    /// </summary>
    public static EntryStore FromScan(string drive, long rootFrn, Dictionary<long, EntryRecord> items, bool useArray = false)
    {
        if (useArray)
        {
            long max = -1;
            foreach (var kv in items) if (kv.Key > max) max = kv.Key;
            var arr = max >= 0 ? new EntryRecord[max + 1] : Array.Empty<EntryRecord>();
            foreach (var kv in items) arr[kv.Key] = kv.Value;
            return new EntryStore(drive, rootFrn, true) { _arr = arr, _count = items.Count };
        }
        return new EntryStore(drive, rootFrn, false) { _dict = items, _count = items.Count };
    }

    public int Count
    {
        get { _rw.EnterReadLock(); try { return _count; } finally { _rw.ExitReadLock(); } }
    }

    // ---------- 底层存取（无锁，由调用方加锁） ----------

    private bool TryRead(long frn, out EntryRecord rec)
    {
        if (_arrayMode && _arr != null)
        {
            if (frn >= 0 && frn < _arr.Length)
            {
                rec = _arr[frn];
                return !string.IsNullOrEmpty(rec.Name); // 空槽视为不存在
            }
            // 越界 FRN（MFT 增长后的新记录号）落在 overflow 字典
            if (_dict != null) return _dict.TryGetValue(frn, out rec);
            rec = default;
            return false;
        }
        return _dict!.TryGetValue(frn, out rec);
    }

    private void Write(long frn, EntryRecord rec)
    {
        if (_arrayMode && _arr != null)
        {
            bool wasEmpty = frn < 0 || frn >= _arr.Length || string.IsNullOrEmpty(_arr[frn].Name);
            if (frn >= 0 && frn < _arr.Length)
            {
                _arr[frn] = rec;
            }
            else
            {
                // 越界 FRN（MFT 增长后的新记录号）：落入 overflow 字典
                (_dict ??= new Dictionary<long, EntryRecord>())[frn] = rec;
            }
            if (wasEmpty && !string.IsNullOrEmpty(rec.Name)) _count++;
            else if (!wasEmpty && rec.Name.Length == 0) _count--;
            return;
        }
        bool had = _dict!.ContainsKey(frn);
        _dict[frn] = rec;
        if (!had) _count++;
    }

    private void ForAll(Action<long, EntryRecord> visit)
    {
        if (_arrayMode && _arr != null)
        {
            var arr = _arr;
            for (long i = 0; i < arr.Length; i++)
            {
                var r = arr[i];
                if (!string.IsNullOrEmpty(r.Name)) visit(i, r);
            }
            if (_dict != null) foreach (var kv in _dict) visit(kv.Key, kv.Value);
        }
        else
        {
            foreach (var kv in _dict!) visit(kv.Key, kv.Value);
        }
    }

    // ---------- 读 ----------

    public bool TryGet(long frn, out EntryRecord rec)
    {
        _rw.EnterReadLock();
        try { return TryRead(frn, out rec); }
        finally { _rw.ExitReadLock(); }
    }

    public string? GetPath(long frn)
    {
        if (_rw.IsReadLockHeld || _rw.IsWriteLockHeld) return GetPathNoLock(frn);
        _rw.EnterReadLock();
        try { return GetPathNoLock(frn); }
        finally { _rw.ExitReadLock(); }
    }

    private string? GetPathNoLock(long frn)
    {
        if (frn == RootFrn) return Drive + @":\";
        if (!TryRead(frn, out var rec) || rec.Name.Length == 0) return null;

        var names = new List<string>(8);
        long cur = frn;
        int guard = 0;
        while (true)
        {
            if (guard++ > 4096) return null;
            if (!TryRead(cur, out var r) || string.IsNullOrEmpty(r.Name)) return null;
            names.Add(r.Name);
            long p = r.ParentFrn;
            if (p == RootFrn) break;   // 父即根：根本身无记录，到此为止
            if (p == 0 || p == cur) return null;
            cur = p;
        }

        var sb = new StringBuilder(96);
        sb.Append(Drive).Append(':').Append('\\');
        for (int i = names.Count - 1; i >= 0; i--)
        {
            if (i < names.Count - 1) sb.Append('\\');
            sb.Append(names[i]);
        }
        return sb.ToString();
    }

    /// <summary>只读遍历全部记录（读锁内回调，回调中可安全调用 GetPath/TryGet）。</summary>
    public void ForEach(Action<long, EntryRecord> visit)
    {
        _rw.EnterReadLock();
        try { ForAll(visit); }
        finally { _rw.ExitReadLock(); }
    }

    /// <summary>直接子项（非递归）。O(N) 单遍扫描，无邻接表，零额外内存。</summary>
    public void ForEachChild(long parentFrn, Action<long, EntryRecord> visit)
    {
        _rw.EnterReadLock();
        try { ForAll((f, r) => { if (r.ParentFrn == parentFrn) visit(f, r); }); }
        finally { _rw.ExitReadLock(); }
    }

    /// <summary>全部后代（含多级，逐层扫描收集）。目录改名/删除时用，结构变更低频，O(N×深度) 可接受。</summary>
    public void ForEachDescendant(long frn, Action<long, EntryRecord> visit)
    {
        var seen = new HashSet<long>();
        var frontier = new HashSet<long> { frn };
        var all = AllFrns();
        _rw.EnterReadLock();
        try
        {
            while (frontier.Count > 0)
            {
                var next = new HashSet<long>();
                foreach (var f in all)
                {
                    if (!TryRead(f, out var r) || string.IsNullOrEmpty(r.Name)) continue;
                    if (frontier.Contains(r.ParentFrn) && seen.Add(f)) { visit(f, r); next.Add(f); }
                }
                frontier = next;
            }
        }
        finally { _rw.ExitReadLock(); }
    }

    /// <summary>收集全部后代 FRN（ForEachDescendant 的列表版，删除子树用）。</summary>
    public List<long> GetDescendants(long frn)
    {
        var result = new List<long>();
        ForEachDescendant(frn, (f, _) => result.Add(f));
        return result;
    }

    private List<long> AllFrns()
    {
        var list = new List<long>(_count);
        ForAll((f, _) => list.Add(f));
        return list;
    }

    /// <summary>绝对路径 → FRN（自根逐段匹配名字，忽略大小写）。找不到返回 0。</summary>
    public long TryResolveFrn(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        var norm = path.TrimEnd('\\');
        if (norm.Length < 2 || norm[1] != ':') return 0;
        // 盘根："D:" 或 "D:\"（去尾斜杠后长度恰为 2）→ 直接返回根 FRN，
        // 否则 "D:" 会被下方 <=2 的误判挡掉，导致选整块盘时秒级索引失效、退化成慢速递归。
        if (norm.Length == 2) return RootFrn;
        var segs = norm.Split('\\');
        long cur = RootFrn;
        _rw.EnterReadLock();
        try
        {
            for (int i = 1; i < segs.Length; i++)
            {
                var seg = segs[i];
                long found = 0;
                ForAll((f, r) =>
                {
                    if (found == 0 && r.ParentFrn == cur && !string.IsNullOrEmpty(r.Name) &&
                        string.Equals(r.Name, seg, StringComparison.OrdinalIgnoreCase))
                        found = f;
                });
                if (found == 0) return 0;
                cur = found;
            }
            return cur;
        }
        finally { _rw.ExitReadLock(); }
    }

    // ---------- 写（USN 实时变更 / 装载后可见性刷新） ----------

    public void UpsertRecord(long frn, in EntryRecord rec)
    {
        _rw.EnterWriteLock();
        try { Write(frn, rec); }
        finally { _rw.ExitWriteLock(); }
    }

    public bool TryRemove(long frn)
    {
        _rw.EnterWriteLock();
        try
        {
            if (_arrayMode && _arr != null && frn >= 0 && frn < _arr.Length)
            {
                if (string.IsNullOrEmpty(_arr[frn].Name)) return false;
                _arr[frn] = default;
                _count--;
                return true;
            }
            if (_dict != null && _dict.TryGetValue(frn, out var r) && !string.IsNullOrEmpty(r.Name))
            {
                _dict.Remove(frn);
                _count--;
                return true;
            }
            return false;
        }
        finally { _rw.ExitWriteLock(); }
    }

    public void RemoveBatch(IEnumerable<long> frns)
    {
        _rw.EnterWriteLock();
        try
        {
            foreach (var f in frns)
            {
                if (_arrayMode && _arr != null && f >= 0 && f < _arr.Length)
                {
                    if (!string.IsNullOrEmpty(_arr[f].Name)) { _arr[f] = default; _count--; }
                }
                else if (_dict != null && _dict.TryGetValue(f, out var r) && !string.IsNullOrEmpty(r.Name))
                {
                    _dict.Remove(f);
                    _count--;
                }
            }
        }
        finally { _rw.ExitWriteLock(); }
    }

    /// <summary>
    /// 全量装载后、对外发布前：按排除/包含规则的 FRN 标记可见性（默认全可见或全不可见后按子树覆盖）。
    /// 规则路径先经 TryResolveFrn 解析为 FRN，整棵子树一次性标记——全程零路径字符串构建，
    /// 避免百万级条目逐条重建完整路径的瞬时内存尖峰。
    /// </summary>
    public void ApplyVisibilityRules(List<long> excludedFrns, bool includeMode, List<long> includedFrns)
    {
        bool defVis = !includeMode;
        var keys = new long[_count];
        int n = 0;
        ForAll((f, _) => { keys[n++] = f; });
        for (int i = 0; i < n; i++)
        {
            if (!TryRead(keys[i], out var rec)) continue;
            rec.Visible = defVis;
            Write(keys[i], rec);
        }
        // 先标包含（子树置可见），再标排除（覆盖为不可见）——与字符串前缀规则语义一致
        foreach (var f in includedFrns) MarkSubtree(f, true);
        foreach (var f in excludedFrns) MarkSubtree(f, false);
    }

    private void MarkSubtree(long frn, bool vis)
    {
        if (!TryRead(frn, out var rec)) return;
        rec.Visible = vis;
        Write(frn, rec);
        var desc = GetDescendants(frn);
        foreach (var f in desc)
        {
            if (TryRead(f, out var r)) { r.Visible = vis; Write(f, r); }
        }
    }

    public void Dispose() => _rw.Dispose();
}
