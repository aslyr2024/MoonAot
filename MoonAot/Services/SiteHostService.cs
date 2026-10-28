using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonAot.Server;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>把本地文件夹临时托管到内置 HTTP 服务的 /site/{id}/ 下，供内置浏览器预览。</summary>
public sealed class SiteHostService
{
    public static SiteHostService Instance { get; } = new();

    private sealed class Entry
    {
        public string Id = "";
        public string Folder = "";
        public long StartedAt;
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _items = new(StringComparer.Ordinal);

    public SiteHostInfo Start(string folder)
    {
        var full = Path.GetFullPath(folder.Trim().Trim('"'));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("文件夹不存在");
        var id = Guid.NewGuid().ToString("N")[..12];
        var entry = new Entry { Id = id, Folder = full, StartedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds() };
        lock (_lock) _items[id] = entry;
        return ToInfo(entry);
    }

    public bool Stop(string id)
    {
        lock (_lock) return _items.Remove(id);
    }

    public List<SiteHostInfo> List()
    {
        lock (_lock) return _items.Values.OrderByDescending(x => x.StartedAt).Select(ToInfo).ToList();
    }

    private static SiteHostInfo ToInfo(Entry e) => new()
    {
        Id = e.Id,
        Path = e.Folder,
        Url = MoonServer.Instance.BaseUrl + "/site/" + e.Id + "/",
        StartedAt = e.StartedAt,
    };

    /// <summary>把 /site/{id}/{rel} 解析成磁盘文件；失败返回 false。</summary>
    public bool Resolve(string id, string rel, out string file)
    {
        file = "";
        Entry? entry;
        lock (_lock) _items.TryGetValue(id, out entry);
        if (entry == null) return false;
        try
        {
            var clean = (rel ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(entry.Folder, clean));
            var root = Path.GetFullPath(entry.Folder);
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                return false;
            if (Directory.Exists(full) || string.IsNullOrEmpty(clean))
            {
                var index = Path.Combine(full, "index.html");
                if (File.Exists(index)) { file = index; return true; }
                return false;
            }
            if (File.Exists(full)) { file = full; return true; }
            return false;
        }
        catch { return false; }
    }
}
