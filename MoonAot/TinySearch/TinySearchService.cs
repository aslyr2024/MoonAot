using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MoonAot.TinySearch.Ntfs;
using MoonAot.TinySearch.Scanning;
using MoonAot.TinySearch.Search;
using MoonAot.TinySearch.Storage;
using TinyAppSettings = MoonAot.TinySearch.Storage.AppSettings;
using MoonAot.Store;
using System.Text.Json;

namespace MoonAot.TinySearch;

public class DriveStatusInfo
{
    public string Letter { get; set; } = "";
    public string Mode { get; set; } = "";
    public string State { get; set; } = "";
    public long Count { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>全盘索引服务：NTFS 优先读 MFT，其他文件系统递归扫描；内存索引 + Everything 风格查询。</summary>
public static class TinySearchService
{
    private static readonly object Gate = new();
    private static MemoryIndex? _mem;
    private static SearchService? _search;
    private static TinyAppSettings _settings = LoadSettings();
    private static readonly ConcurrentDictionary<string, DriveStatusInfo> StatusMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, UsnWatcher> UsnWatchers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, GenericWatcher> GenericWatchers = new(StringComparer.OrdinalIgnoreCase);
    private static int _monitorStarted;
    private static Timer? _rescanTimer;
    private static long _lastFullScanTicks = DateTime.UtcNow.Ticks;
    private static bool _started;
    private static int _indexing;

    public static bool IsStarted => _started;
    public static TinyAppSettings Settings => _settings;
    public static long Version => _mem?.Version ?? 0;
    public static long Total => _mem?.TotalCount() ?? 0;
    public static IReadOnlyDictionary<string, DriveStatusInfo> Status => StatusMap;
    public static SearchService? SearchService => _search;

    private static string SettingsPath => Path.Combine(DbStore.Instance.DataDir, "tiny-settings.json");

    private static TinyAppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), Info<TinyAppSettings>.I);
                if (s != null) return s;
            }
        }
        catch { }
        return new TinyAppSettings();
    }

    private static void SaveSettings(TinyAppSettings s)
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, Info<TinyAppSettings>.I)); } catch { }
    }

    public static bool EnsureStarted()
    {
        lock (Gate)
        {
            if (_started) return true;
            try
            {
                _mem = new MemoryIndex();
                _search = new SearchService(_mem);
                _started = true;
            }
            catch { return false; }
        }
        foreach (var drive in EnumerateDrives()) _ = Task.Run(() => IndexDriveSafe(drive));
        if (Interlocked.Exchange(ref _monitorStarted, 1) == 0)
        {
            _ = Task.Run(DriveMonitorLoop);
            _rescanTimer = new Timer(_ => CheckScheduledRescan(), null, 60_000, 60_000);
        }
        return true;
    }

    /// <summary>每 5 秒轮询：U 盘/移动硬盘插入自动索引，拔出自动清理。</summary>
    private static void DriveMonitorLoop()
    {
        while (true)
        {
            try
            {
                var present = EnumerateDrives().ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var letter in present)
                    if (!StatusMap.ContainsKey(letter))
                        _ = Task.Run(() => IndexDriveSafe(letter));
                foreach (var letter in StatusMap.Keys.ToList())
                    if (!present.Contains(letter)) RemoveDrive(letter);
            }
            catch { }
            Thread.Sleep(5000);
        }
    }

    private static void RemoveDrive(string drive)
    {
        StopUsnWatcher(drive);
        StopGenericWatcher(drive);
        StatusMap.TryRemove(drive, out _);
        _mem?.RemoveDrive(drive);
    }

    /// <summary>定时全盘重扫：距上次全扫超过设定间隔则触发（间隔 ≤0 = 关闭）。</summary>
    private static void CheckScheduledRescan()
    {
        try
        {
            var hours = _settings.RescanIntervalHours;
            if (hours <= 0) return;
            var last = new DateTime(Interlocked.Read(ref _lastFullScanTicks), DateTimeKind.Utc);
            if (DateTime.UtcNow - last < TimeSpan.FromHours(hours)) return;
            RescanAll();
        }
        catch { }
    }

    private static List<string> EnumerateDrives()
    {
        var list = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                var letter = d.Name.TrimEnd('\\', ':').ToUpperInvariant();
                if (_settings.EnabledDrives.Count > 0 &&
                    !_settings.EnabledDrives.Contains(letter, StringComparer.OrdinalIgnoreCase)) continue;
                list.Add(letter);
            }
            catch { }
        }
        return list;
    }

    private static void IndexDriveSafe(string drive)
    {
        Interlocked.Increment(ref _indexing);
        try { IndexDrive(drive); }
        catch (Exception ex)
        {
            StatusMap[drive] = new DriveStatusInfo { Letter = drive, State = "error", Message = ex.Message };
        }
        finally { Interlocked.Decrement(ref _indexing); }
    }

    private static void IndexDrive(string drive)
    {
        var st = new DriveStatusInfo { Letter = drive, State = "scanning", Message = "准备中" };
        StatusMap[drive] = st;
        var ct = CancellationToken.None;
        bool isNtfs = false;
        try { isNtfs = string.Equals(new DriveInfo(drive).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase); } catch { }

        if (isNtfs && _settings.IncludedFolders.Count == 0)
        {
            try
            {
                using var vol = NtfsVolume.Open(drive);
                if (vol != null)
                {
                    var journal = vol.QueryJournal();
                    if (journal != null)
                    {
                        st.Mode = "ntfs";
                        st.Message = "读取 MFT";
                        long scanStartUsn = journal.Value.NextUsn;
                        var items = MftScanner.Scan(vol, ct, new Progress<int>(p => st.Message = $"读取 MFT {p}%"));
                        if (items != null)
                        {
                            st.Message = "构建内存索引";
                            var store = EntryStore.FromScan(drive, EntryStore.NtfsRootFrn, items, useArray: true);
                            store.ApplyVisibilityRules(ResolveRuleFrns(store, drive, _settings.ExcludedFolders), false, new List<long>());
                            _mem?.SetStore(drive, store);
                            st.State = "ready";
                            st.Count = store.Count;
                            st.Message = "就绪";
                            StartUsnWatcher(drive, store, journal.Value.UsnJournalID, scanStartUsn);
                            return;
                        }
                    }
                }
                st.Message = "无卷访问权限，回退普通扫描";
            }
            catch (Exception ex)
            {
                st.Message = "MFT 失败，回退普通扫描：" + ex.Message;
            }
        }

        try
        {
            st.Mode = "generic";
            st.Message = "递归扫描";
            var store = GenericScanner.ScanStore(drive, _settings, ct, new Progress<int>(p =>
            {
                st.Message = p < 0 ? $"已扫描 {-p:N0} 个文件" : $"扫描 {p}%";
            }));
            _mem?.SetStore(drive, store);
            st.State = "ready";
            st.Count = store.Count;
            st.Message = "就绪（普通扫描）";
            StartGenericWatcher(drive, store);
        }
        catch (Exception ex)
        {
            st.State = "error";
            st.Message = ex.Message;
        }
    }

    /// <summary>把排除/包含规则路径解析为本盘 FRN（按盘符过滤；解析失败或不在本盘的规则忽略）。</summary>
    private static List<long> ResolveRuleFrns(EntryStore store, string drive, List<string> paths)
    {
        var result = new List<long>();
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || p.Length < 2 || char.ToUpperInvariant(p[0]) != drive[0]) continue;
            var f = store.TryResolveFrn(p);
            if (f != 0) result.Add(f);
        }
        return result;
    }

    private static void StartUsnWatcher(string drive, EntryStore store, ulong journalId, long startUsn)
    {
        StopUsnWatcher(drive);
        StopGenericWatcher(drive);
        var keep = PathFilter.Keep(_settings);
        var watcher = new UsnWatcher(drive, store, journalId, startUsn);
        watcher.Changed += (ups, dels) => ApplyNtfsChanges(drive, store, keep, ups, dels);
        watcher.Wrapped += () => _ = Task.Run(() => Rescan(drive));
        UsnWatchers[drive] = watcher;
        watcher.Start();
    }

    private static void StopUsnWatcher(string drive)
    {
        if (UsnWatchers.TryRemove(drive, out var w))
        {
            try { w.Dispose(); } catch { }
        }
    }

    private static void StartGenericWatcher(string drive, EntryStore store)
    {
        StopUsnWatcher(drive);
        StopGenericWatcher(drive);
        var settings = _settings;
        var watcher = new GenericWatcher(drive, settings);
        watcher.Changed += (dirty, gone) => ApplyGenericChanges(drive, store, dirty, gone);
        watcher.Overflowed += () => _ = Task.Run(() => Rescan(drive));
        GenericWatchers[drive] = watcher;
        watcher.Start();
    }

    private static void StopGenericWatcher(string drive)
    {
        if (GenericWatchers.TryRemove(drive, out var w))
        {
            try { w.Dispose(); } catch { }
        }
    }

    private static void ApplyGenericChanges(string drive, EntryStore store, List<string> dirty, List<string> gone)
    {
        try
        {
            var settings = _settings;
            var delFrns = new List<long>(gone.Count);
            foreach (var p in gone) delFrns.Add(GenericScanner.PathHash(p));

            foreach (var p in dirty)
            {
                if (PathFilter.IsExcluded(p, settings) || !PathFilter.IsIncluded(p, settings))
                {
                    delFrns.Add(GenericScanner.PathHash(p));
                    continue;
                }
                try
                {
                    if (Directory.Exists(p))
                    {
                        var di = new DirectoryInfo(p);
                        store.UpsertRecord(GenericScanner.PathHash(p), new EntryRecord
                        {
                            ParentFrn = GenericScanner.PathHash(di.Parent?.FullName ?? drive + ":\\"),
                            Name = di.Name,
                            ModifiedUtc = di.LastWriteTimeUtc.Ticks,
                            Attributes = (uint)di.Attributes,
                            IsDirectory = true,
                            Visible = true,
                        });
                    }
                    else if (File.Exists(p))
                    {
                        var fi = new FileInfo(p);
                        store.UpsertRecord(GenericScanner.PathHash(p), new EntryRecord
                        {
                            ParentFrn = GenericScanner.PathHash(fi.DirectoryName ?? drive + ":\\"),
                            Name = fi.Name,
                            Size = fi.Length,
                            ModifiedUtc = fi.LastWriteTimeUtc.Ticks,
                            Attributes = (uint)fi.Attributes,
                            Visible = true,
                        });
                    }
                    else delFrns.Add(GenericScanner.PathHash(p));
                }
                catch { }
            }

            store.RemoveBatch(delFrns);
            if (delFrns.Count > 0 || dirty.Count > 0)
            {
                _mem?.BumpVersion();
                if (StatusMap.TryGetValue(drive, out var st) && st.State == "ready")
                    st.Count = store.Count;
            }
        }
        catch { }
    }

    private static void ApplyNtfsChanges(string drive, EntryStore store, Func<string, bool> keep,
        List<EntryChange> upserts, List<ulong> deletes)
    {
        try
        {
            // 1. 刷新 upsert 可见性（Watcher 已写入记录；此处按重建路径的排除规则定 Visible）
            var renamedDirs = new List<long>();
            foreach (var ch in upserts)
            {
                long frn = (long)ch.Frn;
                if (!store.TryGet(frn, out var rec)) continue;
                var path = store.GetPath(frn);
                var vis = path != null && keep(path);
                if (rec.Visible != vis) { rec.Visible = vis; store.UpsertRecord(frn, rec); }
                if (rec.IsDirectory) renamedDirs.Add(frn);
            }

            // 2. 删除项若是目录，连带删除其整棵子树
            var toDelete = new List<long>(deletes.Count);
            foreach (var d in deletes) toDelete.Add((long)d);
            foreach (var f in toDelete.ToList())
                if (store.TryGet(f, out var r) && r.IsDirectory)
                    toDelete.AddRange(store.GetDescendants(f));
            store.RemoveBatch(toDelete.Distinct());

            // 3. 目录移动/改名：子树路径已变，可能跨排除区，刷新后代可见性
            foreach (var dir in renamedDirs)
            {
                var updates = new List<(long Frn, bool Vis)>();
                store.ForEachDescendant(dir, (f, rec) =>
                {
                    var path = store.GetPath(f);
                    var vis = path != null && keep(path);
                    if (rec.Visible != vis) updates.Add((f, vis));
                });
                foreach (var (f, vis) in updates)
                {
                    if (store.TryGet(f, out var rec)) { rec.Visible = vis; store.UpsertRecord(f, rec); }
                }
            }

            if (toDelete.Count > 0 || upserts.Count > 0)
            {
                _mem?.BumpVersion();
                if (StatusMap.TryGetValue(drive, out var st) && st.State == "ready")
                    st.Count = store.Count;
            }
        }
        catch { }
    }

    public static SearchResult? Search(string? query, string? sort, bool desc, int offset, int limit)
    {
        EnsureStarted();
        return _search?.Search(query ?? "", sort ?? "size", desc, offset, limit);
    }

    public static IEnumerable<FileEntry> Enumerate(string? query, string? sort, bool desc)
    {
        EnsureStarted();
        return _search?.Enumerate(query ?? "", sort ?? "size", desc) ?? Enumerable.Empty<FileEntry>();
    }

    /// <summary>
    /// 直接用全盘索引枚举某文件夹下的指定类型文件（打开文件夹/播放列表用），
    /// 避免再次遍历磁盘。索引未就绪或该路径不在索引中时返回 false，调用方回退文件系统扫描。
    /// </summary>
    public static bool TryEnumerateFiles(string folder, HashSet<string> extensions, int limit, out List<FileEntry> files)
    {
        files = new List<FileEntry>();
        if (extensions.Count == 0 || limit <= 0) return false;
        EnsureStarted();
        var mem = _mem;
        if (mem == null) return false;
        string full;
        try { full = Path.GetFullPath(folder.Trim().Trim('"')); }
        catch { return false; }
        var drive = Path.GetPathRoot(full)?.TrimEnd('\\', ':').ToUpperInvariant() ?? "";
        if (drive.Length != 1) return false;
        if (!StatusMap.TryGetValue(drive, out var st) || st.State != "ready") return false;
        var store = mem.GetStore(drive);
        if (store == null) return false;
        long rootFrn = store.TryResolveFrn(full);
        if (rootFrn == 0) return false;
        if (rootFrn != store.RootFrn && store.TryGet(rootFrn, out var rootRec) && !rootRec.Visible)
            return false;

        var exts = new HashSet<string>(extensions.Select(e => e.TrimStart('.')), StringComparer.OrdinalIgnoreCase);
        var found = new List<FileEntry>();
        store.ForEachDescendant(rootFrn, (frn, rec) =>
        {
            if (rec.IsDirectory || !rec.Visible) return;
            if (found.Count >= limit) return;
            if (!exts.Contains(rec.GetExtension())) return;
            var path = store.GetPath(frn);
            if (string.IsNullOrEmpty(path)) return;
            found.Add(new FileEntry
            {
                DriveLetter = drive,
                Frn = frn,
                ParentFrn = rec.ParentFrn,
                Name = rec.Name,
                FullPath = path,
                Size = rec.Size,
                Extension = rec.GetExtension(),
                IsDirectory = false,
                ModifiedUtc = rec.ModifiedUtc,
                Attributes = rec.Attributes,
            });
        });
        files = found;
        return true;
    }

    public static void RescanAll()
    {
        EnsureStarted();
        foreach (var drive in UsnWatchers.Keys.ToList()) StopUsnWatcher(drive);
        foreach (var drive in GenericWatchers.Keys.ToList()) StopGenericWatcher(drive);
        lock (Gate)
        {
            _mem = new MemoryIndex();
            _search = new SearchService(_mem);
            StatusMap.Clear();
        }
        Interlocked.Exchange(ref _lastFullScanTicks, DateTime.UtcNow.Ticks);
        foreach (var drive in EnumerateDrives()) _ = Task.Run(() => IndexDriveSafe(drive));
    }

    public static void Rescan(string drive)
    {
        EnsureStarted();
        drive = drive.ToUpperInvariant();
        StopUsnWatcher(drive);
        StopGenericWatcher(drive);
        StatusMap.TryRemove(drive, out _);
        _ = Task.Run(() => IndexDriveSafe(drive));
    }

    public static void UpdateSettings(TinyAppSettings s)
    {
        _settings = s ?? new TinyAppSettings();
        SaveSettings(_settings);
        RescanAll();
    }

    public static List<object> AvailableDrives()
    {
        var list = new List<object>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                list.Add(new { letter = d.Name.TrimEnd('\\', ':').ToUpperInvariant(), name = d.Name, format = d.DriveFormat, type = d.DriveType.ToString() });
            }
            catch { }
        }
        return list;
    }
}
