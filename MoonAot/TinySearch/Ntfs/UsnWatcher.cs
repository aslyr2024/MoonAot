using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MoonAot.TinySearch.Storage;

namespace MoonAot.TinySearch.Ntfs;

/// <summary>
/// USN 日志实时监控：轮询 FSCTL_READ_USN_JOURNAL，把文件新建/删除/重命名/修改
/// 以 (upserts, deletes) 批量回调出去，延迟约 0.5 秒。
/// </summary>
public sealed class UsnWatcher : IDisposable
{
    public event Action<List<EntryChange>, List<ulong>>? Changed;
    public event Action? Wrapped;

    private readonly string _drive;
    private readonly EntryStore _store;
    private readonly ulong _journalId;
    private long _nextUsn;
    private NtfsVolume? _vol;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private DateTime _lastJournalCheck = DateTime.MinValue;

    public long NextUsn => Interlocked.Read(ref _nextUsn);

    public UsnWatcher(string drive, EntryStore store, ulong journalId, long startUsn)
    {
        _drive = drive;
        _store = store;
        _journalId = journalId;
        _nextUsn = startUsn;
    }

    public void Start() => _loop = Task.Run(Loop);

    private void Loop()
    {
        var ct = _cts.Token;
        const int BufSize = 64 * 1024;
        IntPtr inBuf = Marshal.AllocHGlobal(Marshal.SizeOf<UsnNative.READ_USN_JOURNAL_DATA>());
        IntPtr outBuf = Marshal.AllocHGlobal(BufSize);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _vol ??= NtfsVolume.Open(_drive);
                if (_vol == null) { Thread.Sleep(3000); continue; }

                // 定期检测日志回绕（写入量超出日志容量时会丢记录）
                if ((DateTime.UtcNow - _lastJournalCheck).TotalSeconds > 10)
                {
                    _lastJournalCheck = DateTime.UtcNow;
                    var jd = _vol.QueryJournal();
                    if (jd == null || jd.Value.UsnJournalID != _journalId || NextUsn < jd.Value.LowestValidUsn)
                    {
                        Wrapped?.Invoke();
                        return;
                    }
                }

                var input = new UsnNative.READ_USN_JOURNAL_DATA
                {
                    StartUsn = NextUsn,
                    ReasonMask = 0xFFFFFFFF,
                    ReturnOnlyOnClose = 0,
                    Timeout = 0,
                    BytesToWaitFor = 0,
                    UsnJournalID = _journalId,
                };
                Marshal.StructureToPtr(input, inBuf, false);

                bool ok = UsnNative.DeviceIoControl(_vol.Handle, UsnNative.FSCTL_READ_USN_JOURNAL,
                    inBuf, (uint)Marshal.SizeOf(input), outBuf, BufSize, out uint returned, IntPtr.Zero);
                if (!ok) { Thread.Sleep(1000); continue; }
                if (returned <= 8) { Thread.Sleep(400); continue; }

                long next = Marshal.ReadInt64(outBuf);
                ParseRecords(outBuf + 8, returned - 8, ct);
                Interlocked.Exchange(ref _nextUsn, next);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(inBuf);
            Marshal.FreeHGlobal(outBuf);
        }
    }

    private void ParseRecords(IntPtr basePtr, uint length, CancellationToken ct)
    {
        var upserts = new Dictionary<ulong, EntryChange>();
        var deletes = new HashSet<ulong>();
        var needStat = new HashSet<ulong>();

        int off = 0;
        while (off + Marshal.SizeOf<UsnNative.USN_RECORD_V2>() <= length)
        {
            ct.ThrowIfCancellationRequested();
            var rec = Marshal.PtrToStructure<UsnNative.USN_RECORD_V2>(basePtr + off);
            if (rec.RecordLength < 60 || off + rec.RecordLength > length) break;
            int recordStart = off;
            off += (int)rec.RecordLength;
            if (rec.MajorVersion != 2) continue;

            ulong frn = UsnNative.MaskFrn(rec.FileReferenceNumber);
            ulong pfrn = UsnNative.MaskFrn(rec.ParentFileReferenceNumber);
            bool isDir = (rec.FileAttributes & UsnNative.FILE_ATTRIBUTE_DIRECTORY) != 0;
            uint reason = rec.Reason;

            if ((reason & (UsnNative.USN_REASON_FILE_DELETE | UsnNative.USN_REASON_RENAME_OLD_NAME)) != 0)
            {
                _store.TryRemove((long)frn);
                upserts.Remove(frn);
                deletes.Add(frn);
                continue;
            }

            const uint interesting =
                UsnNative.USN_REASON_FILE_CREATE | UsnNative.USN_REASON_RENAME_NEW_NAME |
                UsnNative.USN_REASON_DATA_OVERWRITE | UsnNative.USN_REASON_DATA_EXTEND |
                UsnNative.USN_REASON_DATA_TRUNCATION | UsnNative.USN_REASON_BASIC_INFO_CHANGE;
            if ((reason & interesting) == 0) continue;
            bool closed = (reason & UsnNative.USN_REASON_CLOSE) != 0;
            bool isCreateOrRename = (reason & (UsnNative.USN_REASON_FILE_CREATE | UsnNative.USN_REASON_RENAME_NEW_NAME)) != 0;
            if (!closed && !isCreateOrRename) continue;

            string name = rec.FileNameLength > 0
                ? Marshal.PtrToStringUni(basePtr + recordStart + rec.FileNameOffset, rec.FileNameLength / 2) ?? ""
                : "";
            if (name.Length == 0) continue;

            var item = new EntryRecord
            {
                ParentFrn = (long)pfrn,
                Name = name,
                IsDirectory = isDir,
                Attributes = rec.FileAttributes,
                ModifiedUtc = FileTimeTicks(rec.TimeStamp),
                Size = 0,
            };
            if (_store.TryGet((long)frn, out var prev))
            {
                item.Size = prev.Size;
                if (prev.ModifiedUtc > item.ModifiedUtc) item.ModifiedUtc = prev.ModifiedUtc;
                item.Visible = prev.Visible;
            }
            _store.UpsertRecord((long)frn, item);
            deletes.Remove(frn);
            upserts[frn] = new EntryChange(frn, item);
            if (!isDir && closed) needStat.Add(frn);
        }

        foreach (var frn in needStat)
        {
            if (!upserts.TryGetValue(frn, out var ch)) continue;
            var path = _store.GetPath((long)frn);
            if (path == null) continue;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists)
                {
                    var rec2 = ch.Record;
                    rec2.Size = fi.Length;
                    rec2.ModifiedUtc = fi.LastWriteTimeUtc.Ticks;
                    _store.UpsertRecord((long)frn, rec2);
                    upserts[frn] = new EntryChange(frn, rec2);
                }
            }
            catch { }
        }

        if (upserts.Count > 0 || deletes.Count > 0)
            Changed?.Invoke(upserts.Values.ToList(), deletes.ToList());
    }

    private static long FileTimeTicks(long fileTime)
    {
        try { return fileTime <= 0 ? 0 : DateTime.FromFileTimeUtc(fileTime).Ticks; }
        catch { return 0; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1500); } catch { }
        _vol?.Dispose();
        _cts.Dispose();
    }
}
