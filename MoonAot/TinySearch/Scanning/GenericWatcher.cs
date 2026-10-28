using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MoonAot.TinySearch.Storage;

namespace MoonAot.TinySearch.Scanning;

/// <summary>非 NTFS 盘的 FileSystemWatcher 实时监控（事件防抖后批量回调）。</summary>
public sealed class GenericWatcher : IDisposable
{
    public event Action<List<string>, List<string>>? Changed;
    public event Action? Overflowed;

    private readonly string _drive;
    private readonly AppSettings _settings;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly object _gate = new();
    private readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _gone = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _flushTimer;

    public GenericWatcher(string drive, AppSettings settings)
    {
        _drive = drive;
        _settings = settings;
    }

    public void Start()
    {
        foreach (var root in GenericScanner.RootsFor(_drive, _settings))
        {
            try
            {
                var w = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.Size | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true,
                };
                w.Created += (_, e) => Mark(e.FullPath, false);
                w.Changed += (_, e) => Mark(e.FullPath, false);
                w.Renamed += (_, e) => { Mark(e.OldFullPath, true); Mark(e.FullPath, false); };
                w.Deleted += (_, e) => Mark(e.FullPath, true);
                w.Error += (_, _) => Overflowed?.Invoke();
                _watchers.Add(w);
            }
            catch { }
        }
    }

    private void Mark(string path, bool deleted)
    {
        if (PathFilter.IsExcluded(path, _settings)) return;
        lock (_gate)
        {
            if (deleted) { _gone.Add(path); _dirty.Remove(path); }
            else { _dirty.Add(path); _gone.Remove(path); }
            _flushTimer ??= new Timer(Flush, null, 600, Timeout.Infinite);
            _flushTimer.Change(600, Timeout.Infinite);
        }
    }

    private void Flush(object? state)
    {
        List<string> dirty, gone;
        lock (_gate)
        {
            dirty = _dirty.ToList();
            gone = _gone.ToList();
            _dirty.Clear();
            _gone.Clear();
        }
        if (dirty.Count > 0 || gone.Count > 0)
        {
            try { Changed?.Invoke(dirty, gone); } catch { }
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        lock (_gate)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
        }
    }
}
