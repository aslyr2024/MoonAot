using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MoonAot.Store;
using MoonAot.TinySearch;

namespace MoonAot.Services;

/// <summary>
/// 图片列表扫描（支持子文件夹）。递归扫描大目录时在后台进行，前端轮询 Scanning 状态，
/// 避免像旧版那样同步递归导致界面假死。结果按文件夹缓存 5 分钟。
/// </summary>
public static class ImageScanService
{
    private sealed class ScanState
    {
        public string Key = "";
        public string Folder = "";
        public bool Recursive;
        public readonly List<ImageItem> Items = new();
        public readonly Dictionary<string, int> IndexMap = new(StringComparer.OrdinalIgnoreCase);
        public bool Done;
        public string Phase = "scanning";
        public int Scanned;
        public string? CurrentDir;
        public long StartedAt = Environment.TickCount64;
        public long LastAccess = Environment.TickCount64;
        public CancellationTokenSource Cts = new();
    }

    private static readonly object Lock = new();
    private static readonly Dictionary<string, ScanState> States = new(StringComparer.OrdinalIgnoreCase);

    public static ImagePage Get(string folder, bool recursive, int offset, int limit, string? current, bool refresh = false)
    {
        var page = new ImagePage();
        string full;
        try { full = Path.GetFullPath(folder.Trim().Trim('"')); }
        catch { page.Message = "路径无效"; return page; }
        if (!Directory.Exists(full)) { page.Message = "文件夹不存在"; return page; }
        page.Path = full;
        if (limit <= 0) limit = 120;
        if (limit > 2000) limit = 2000;

        var key = full + "|" + (recursive ? "r" : "f");
        ScanState state;
        lock (Lock)
        {
            if (refresh && States.TryGetValue(key, out var old))
            {
                try { old.Cts.Cancel(); } catch { }
                States.Remove(key);
            }
            if (!States.TryGetValue(key, out state!))
            {
                state = new ScanState { Key = key, Folder = full, Recursive = recursive };
                States[key] = state;
                var s = state;
                _ = System.Threading.Tasks.Task.Run(() => Scan(s));
                EvictOldLocked();
            }
            state.LastAccess = Environment.TickCount64;
        }

        lock (state.Items)
        {
            page.Scanning = !state.Done;
            page.Message = state.Phase;
            page.Total = state.Items.Count;
            if (!string.IsNullOrEmpty(current))
            {
                if (state.IndexMap.TryGetValue(current, out var curIdx)) page.Index = curIdx;
                else if (state.Items.Count > 200_000)
                {
                    for (int i = 0; i < state.Items.Count; i++)
                    {
                        if (string.Equals(state.Items[i].Path, current, StringComparison.OrdinalIgnoreCase))
                        { page.Index = i; break; }
                    }
                }
            }
            if (offset < 0)
            {
                offset = page.Index >= 0 ? Math.Max(0, page.Index - limit / 2) : 0;
            }
            if (offset > Math.Max(0, state.Items.Count - 1) && state.Done)
                offset = Math.Max(0, state.Items.Count - limit);
            page.Offset = offset;
            page.Limit = limit;
            int end = Math.Min(state.Items.Count, offset + limit);
            for (int i = offset; i < end; i++) page.Items.Add(state.Items[i]);
        }
        return page;
    }

    private static void Scan(ScanState s)
    {
        try
        {
            if (s.Recursive && TinySearchService.TryEnumerateFiles(s.Folder, FileKinds.Image, 10_000_000, out var indexed))
            {
                lock (s.Items)
                {
                    foreach (var f in indexed)
                        s.Items.Add(new ImageItem { Name = f.Name, Path = f.FullPath, Size = f.Size });
                    s.Items.Sort((a, b) =>
                    {
                        var c = string.Compare(Path.GetDirectoryName(a.Path), Path.GetDirectoryName(b.Path),
                            StringComparison.CurrentCultureIgnoreCase);
                        return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                    });
                    s.IndexMap.Clear();
                    if (s.Items.Count <= 200_000)
                        for (int i = 0; i < s.Items.Count; i++) s.IndexMap[s.Items[i].Path] = i;
                    s.Done = true;
                    s.Phase = "done";
                    s.Scanned = indexed.Count;
                }
                Log.Info($"图片索引枚举完成 {s.Folder}：{indexed.Count} 张");
                return;
            }

            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = s.Recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden,
                MaxRecursionDepth = 32,
            };
            int scanned = 0;
            foreach (var file in Directory.EnumerateFiles(s.Folder, "*", opts))
            {
                if (s.Cts.IsCancellationRequested) return;
                scanned++;
                if (scanned % 200 == 0)
                {
                    var dir = Path.GetDirectoryName(file) ?? "";
                    lock (s.Items)
                    {
                        s.Scanned = scanned;
                        s.CurrentDir = dir;
                        s.Phase = "已扫描 " + scanned + " 个文件";
                    }
                }
                if (!FileKinds.Image.Contains(Path.GetExtension(file))) continue;
                long size = 0;
                try { size = new FileInfo(file).Length; } catch { }
                var item = new ImageItem { Name = Path.GetFileName(file), Path = file, Size = size };
                lock (s.Items) s.Items.Add(item);
                if (s.Items.Count >= 10_000_000) break;
            }
            lock (s.Items)
            {
                s.Phase = "正在排序…";
                s.Items.Sort((a, b) =>
                {
                    var c = string.Compare(Path.GetDirectoryName(a.Path), Path.GetDirectoryName(b.Path),
                        StringComparison.CurrentCultureIgnoreCase);
                    return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                });
                s.IndexMap.Clear();
                if (s.Items.Count <= 200_000)
                    for (int i = 0; i < s.Items.Count; i++) s.IndexMap[s.Items[i].Path] = i;
                s.Done = true;
                s.Phase = "done";
                s.Scanned = scanned;
            }
            Log.Info($"图片扫描完成 {s.Folder}（递归={s.Recursive}）：{s.Items.Count} 张 / {scanned} 文件");
        }
        catch (Exception ex)
        {
            lock (s.Items)
            {
                s.Done = true;
                s.Phase = "扫描失败：" + ex.Message;
            }
            Log.Warn("图片扫描失败: " + ex.Message);
        }
    }

    private static void EvictOldLocked()
    {
        if (States.Count <= 8) return;
        var oldest = States.Values.Where(x => x.Done).OrderBy(x => x.LastAccess).Take(States.Count - 6).ToList();
        foreach (var s in oldest)
        {
            try { s.Cts.Cancel(); } catch { }
            States.Remove(s.Key);
        }
    }
}
