using System;
using System.Collections.Concurrent;
using System.Threading;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>后台任务登记表（重建封面、清理缺失文件等），供前端轮询进度。</summary>
public static class Jobs
{
    private static readonly ConcurrentDictionary<string, JobStatus> Items = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> Cancels = new(StringComparer.Ordinal);

    public static JobStatus Start(string kind, int total)
    {
        var job = new JobStatus
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = kind,
            Running = true,
            Total = total,
            StartedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
        };
        Items[job.Id] = job;
        Cancels[job.Id] = new CancellationTokenSource();
        // 只保留最近 20 个任务
        if (Items.Count > 20)
        {
            foreach (var old in System.Linq.Enumerable.Take(Items.Values, Math.Max(0, Items.Count - 20)))
            {
                if (!old.Running) Items.TryRemove(old.Id, out _);
            }
        }
        return job;
    }

    public static CancellationToken TokenOf(string id) =>
        Cancels.TryGetValue(id, out var cts) ? cts.Token : CancellationToken.None;

    public static void Update(string id, int done, int total, string? message = null)
    {
        if (!Items.TryGetValue(id, out var job)) return;
        job.Done = done;
        job.Total = total;
        if (message != null) job.Message = message;
    }

    public static void Finish(string id, string? message = null)
    {
        if (!Items.TryGetValue(id, out var job)) return;
        job.Running = false;
        job.FinishedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (message != null) job.Message = message;
        Cancels.TryRemove(id, out _);
    }

    public static JobStatus? Get(string id) => Items.TryGetValue(id, out var j) ? j : null;

    public static JobStatus? Latest(string kind)
    {
        JobStatus? best = null;
        foreach (var j in Items.Values)
        {
            if (!string.Equals(j.Kind, kind, StringComparison.Ordinal)) continue;
            if (best == null || j.StartedAt > best.StartedAt) best = j;
        }
        return best;
    }

    public static bool Cancel(string id)
    {
        if (Cancels.TryGetValue(id, out var cts))
        {
            try { cts.Cancel(); } catch { }
            return true;
        }
        return false;
    }
}
