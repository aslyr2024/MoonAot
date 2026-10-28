using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonAot.Store;

namespace MoonAot.Services.Zim;

/// <summary>
/// 百科（ZIM）会话服务：全局持有当前打开的 ZIM 文件，供本地 Kestrel 路由 /zim/* 使用。
/// 同时记录最近打开过的文件列表（写入 %LOCALAPPDATA%\MoonReader\zim_recent.txt）。
/// </summary>
public sealed class ZimService
{
    /// <summary>单例</summary>
    public static ZimService Instance { get; } = new();

    private readonly object _lock = new();
    private ZimFile? _current;

    /// <summary>当前打开的 ZIM（未打开为 null）</summary>
    public ZimFile? Current { get { lock (_lock) return _current; } }
    /// <summary>是否已打开 ZIM</summary>
    public bool IsOpen => Current != null;

    /// <summary>打开 ZIM 文件（会关闭上一个）。失败抛异常。</summary>
    public ZimFile Open(string path)
    {
        var zim = ZimFile.Open(path);
        ZimFile? old;
        lock (_lock) { old = _current; _current = zim; }
        try { old?.Dispose(); } catch { }
        AddRecent(path);
        return zim;
    }

    /// <summary>关闭当前 ZIM。</summary>
    public void Close()
    {
        ZimFile? old;
        lock (_lock) { old = _current; _current = null; }
        try { old?.Dispose(); } catch { }
    }

    // ===== 最近打开 =====

    private static string RecentFile => Path.Combine(DbStore.Instance.DataDir, "zim_recent.txt");

    /// <summary>最近打开过且仍存在的 ZIM 文件路径（最多 10 条，最新在前）。</summary>
    public List<string> GetRecent()
    {
        try
        {
            if (!File.Exists(RecentFile)) return new List<string>();
            return File.ReadAllLines(RecentFile)
                       .Where(l => !string.IsNullOrWhiteSpace(l) && File.Exists(l))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .Take(10).ToList();
        }
        catch { return new List<string>(); }
    }

    private void AddRecent(string path)
    {
        try
        {
            var list = GetRecent();
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, path);
            Directory.CreateDirectory(Path.GetDirectoryName(RecentFile)!);
            File.WriteAllLines(RecentFile, list.Take(10));
        }
        catch { }
    }
}
