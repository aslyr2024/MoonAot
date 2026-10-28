using System.Text;
using MoonAot.TinySearch.Storage;

namespace MoonAot.TinySearch.Ntfs;

/// <summary>
/// 直接顺序读取 $MFT 原始数据并解析 FILE 记录 —— 与 Everything 相同的索引原理，
/// 百万级文件秒级完成（内存映射式顺序读 + $FILE_NAME 属性取大小/时间）。
/// 产出紧凑 EntryRecord（struct）字典，只存文件名不存路径。
/// </summary>
public static class MftScanner
{
    private const int ChunkSize = 8 * 1024 * 1024;

    /// <returns>FRN(48bit) → EntryRecord；失败返回 null（权限不足/非 NTFS 等）。</returns>
    internal static Dictionary<long, EntryRecord>? Scan(NtfsVolume vol, CancellationToken ct,
        IProgress<int>? progress = null)
    {
        // 1. 读 $MFT 自身记录，解析其 $DATA 运行列表（runlist）定位 MFT 在磁盘上的位置
        byte[]? rec0 = vol.ReadFileRecord(0);
        if (rec0 == null) { Console.WriteLine("[mft] 读取 $MFT 记录0失败"); return null; }
        var extents = ParseDataRunlist(rec0, vol.BytesPerFileRecord);
        if (extents == null || extents.Count == 0) { Console.WriteLine("[mft] 解析 $MFT runlist 失败"); return null; }

        int recordSize = vol.BytesPerFileRecord;
        int clusterSize = vol.BytesPerCluster;
        long validLen = vol.MftValidDataLength;
        var result = new Dictionary<long, EntryRecord>(capacity: 200_000);
        byte[] buffer = new byte[ChunkSize];

        long filePos = 0;
        int lastPct = -1;
        while (filePos < validLen)
        {
            ct.ThrowIfCancellationRequested();
            // 2. 定位 filePos 所在 extent，换算磁盘绝对偏移
            var ext = FindExtent(extents, filePos / (long)clusterSize);
            if (ext == null) break;
            long extentFileStart = ext.Value.VcnStart * (long)clusterSize;
            long extentFileEnd = extentFileStart + ext.Value.LengthClusters * (long)clusterSize;
            long diskStart = (ext.Value.Lcn + (filePos - extentFileStart) / clusterSize) * (long)clusterSize
                             + (filePos - extentFileStart) % clusterSize;
            long chunkLen = Math.Min(Math.Min(ChunkSize, extentFileEnd - filePos), validLen - filePos);
            // 对齐到记录边界，保证不截断记录
            chunkLen -= chunkLen % recordSize;
            if (chunkLen <= 0) { filePos += recordSize; continue; }

            if (!vol.ReadAbsolute(diskStart, buffer, (int)chunkLen)) break;

            // 3. 逐条解析 FILE 记录
            for (long recOff = 0; recOff + recordSize <= chunkLen; recOff += recordSize)
            {
                long frn = (filePos + recOff) / recordSize;
                var item = ParseRecord(buffer, (int)recOff, recordSize, frn);
                if (item != null)
                    result[frn] = item.Value;
            }
            filePos += chunkLen;

            if (progress != null)
            {
                int pct = (int)(filePos * 100 / validLen);
                if (pct != lastPct) { lastPct = pct; progress.Report(pct); }
            }
        }
        progress?.Report(100);
        return result;
    }

    /// <summary>解析单条 FILE 记录；无效/未使用/扩展记录返回 null。</summary>
    private static EntryRecord? ParseRecord(byte[] buf, int off, int recordSize, long frn)
    {
        if (frn < 24) return null; // 0-23 为 NTFS 元文件
        if (BitConverter.ToUInt32(buf, off) != 0x454C4946) return null; // "FILE"

        ushort flags = BitConverter.ToUInt16(buf, off + 0x16);
        if ((flags & 0x01) == 0) return null; // 未使用
        if (BitConverter.ToInt64(buf, off + 0x20) != 0) return null; // 扩展记录（属性列表外置），跳过

        // USA 修复（仅在校验匹配时应用；系统 API 返回的记录可能已修复）
        if (!ApplyFixup(buf, off, recordSize)) return null;

        bool isDir = (flags & 0x02) != 0;
        int pos = off + BitConverter.ToUInt16(buf, off + 0x14);
        int end = off + recordSize;

        EntryRecord best = new();
        bool hasBest = false;
        int bestNs = -1;
        long dataSize = -1;     // 权威大小：未命名 $DATA 属性
        long siMtime = -1;      // 权威修改时间：$STANDARD_INFORMATION
        uint siAttrs = 0;
        while (pos + 8 <= end)
        {
            uint type = BitConverter.ToUInt32(buf, pos);
            if (type == 0xFFFFFFFF) break;
            uint attrLen = BitConverter.ToUInt32(buf, pos + 4);
            if (attrLen < 16 || pos + attrLen > end + 8) break;

            switch (type)
            {
                case 0x10 when buf[pos + 8] == 0: // $STANDARD_INFORMATION 常驻
                {
                    int vOff = pos + BitConverter.ToUInt16(buf, pos + 0x14);
                    uint vLen = BitConverter.ToUInt32(buf, pos + 0x10);
                    if (vLen >= 0x24 && vOff + vLen <= end)
                    {
                        siMtime = FileTimeToTicks(BitConverter.ToInt64(buf, vOff + 0x08));
                        siAttrs = BitConverter.ToUInt32(buf, vOff + 0x20);
                    }
                    break;
                }
                case 0x30 when buf[pos + 8] == 0: // $FILE_NAME 常驻
                {
                    int vOff = pos + BitConverter.ToUInt16(buf, pos + 0x14);
                    uint vLen = BitConverter.ToUInt32(buf, pos + 0x10);
                    if (vLen >= 0x42 && vOff + vLen <= end)
                    {
                        int ns = buf[vOff + 0x41];
                        // 命名空间优先 Win32(1)/Win32&DOS(3)
                        if (!hasBest || (bestNs != 1 && bestNs != 3 && (ns == 1 || ns == 3)))
                        {
                            int nameLen = buf[vOff + 0x40];
                            if (vOff + 0x42 + nameLen * 2 <= end)
                            {
                                best = new EntryRecord
                                {
                                    ParentFrn = (long)UsnNative.MaskFrn(BitConverter.ToUInt64(buf, vOff)),
                                    Name = Encoding.Unicode.GetString(buf, vOff + 0x42, nameLen * 2),
                                    IsDirectory = isDir,
                                    Attributes = BitConverter.ToUInt32(buf, vOff + 0x38),
                                    ModifiedUtc = FileTimeToTicks(BitConverter.ToInt64(buf, vOff + 0x10)),
                                    Size = BitConverter.ToInt64(buf, vOff + 0x30), // 兜底，可能被 $DATA 覆盖
                                };
                                hasBest = true;
                                bestNs = ns;
                            }
                        }
                    }
                    break;
                }
                case 0x80: // $DATA（仅取未命名主流）
                {
                    int nameLen = buf[pos + 9];
                    if (nameLen == 0)
                    {
                        if (buf[pos + 8] == 0) // 常驻：大小 = 值长度
                        {
                            dataSize = BitConverter.ToUInt32(buf, pos + 0x10);
                        }
                        else if (pos + 0x38 <= end) // 非常驻：RealSize @ 0x30
                        {
                            dataSize = BitConverter.ToInt64(buf, pos + 0x30);
                        }
                    }
                    break;
                }
            }
            pos += (int)attrLen;
        }
        if (!hasBest || best.Name.Length == 0 || best.Name == "$Extend") return null;
        best.Size = isDir ? 0 : (dataSize >= 0 ? dataSize : best.Size);
        if (siMtime > 0) best.ModifiedUtc = siMtime;
        if (siAttrs != 0) best.Attributes = siAttrs;
        return best;
    }

    private static long FileTimeToTicks(long fileTime)
    {
        try
        {
            if (fileTime <= 0) return 0;
            return DateTime.FromFileTimeUtc(fileTime).Ticks;
        }
        catch { return 0; }
    }

    /// <summary>
    /// USA fixup：校验每个扇区尾 2 字节是否等于 USN。
    /// 全部匹配 → 原地修复并返回 true；全部不匹配（记录已被系统修复）→ 直接返回 true；
    /// 部分匹配（损坏）→ 返回 false。
    /// </summary>
    private static bool ApplyFixup(byte[] buf, int off, int recordSize)
    {
        ushort usaOff = BitConverter.ToUInt16(buf, off + 0x04);
        ushort usaCnt = BitConverter.ToUInt16(buf, off + 0x06);
        if (usaCnt < 1 || usaCnt > recordSize / 512 + 1 || off + usaOff + usaCnt * 2 > off + recordSize) return false;

        int matched = 0;
        for (int i = 1; i < usaCnt; i++)
        {
            int tail = off + i * 512 - 2;
            if (tail + 2 > off + recordSize) break;
            if (buf[tail] == buf[off + usaOff] && buf[tail + 1] == buf[off + usaOff + 1]) matched++;
        }
        if (matched == 0) return true; // 已修复的记录（FSCTL 返回）
        for (int i = 1; i < usaCnt; i++)
        {
            int tail = off + i * 512 - 2;
            if (tail + 2 > off + recordSize) break;
            buf[tail] = buf[off + usaOff + 2 * i];
            buf[tail + 1] = buf[off + usaOff + 2 * i + 1];
        }
        return true;
    }

    private readonly struct Extent
    {
        public readonly long VcnStart; public readonly long Lcn; public readonly long LengthClusters;
        public Extent(long v, long l, long c) { VcnStart = v; Lcn = l; LengthClusters = c; }
    }

    /// <summary>解析 $MFT 记录 0 的非常驻 $DATA 属性 runlist。</summary>
    private static List<Extent>? ParseDataRunlist(byte[] rec, int recordSize)
    {
        if (rec.Length < recordSize || BitConverter.ToUInt32(rec, 0) != 0x454C4946)
        {
            Console.WriteLine($"[mft] rec0 签名异常 len={rec.Length} sig={BitConverter.ToUInt32(rec, 0):X8}");
            return null;
        }
        // fixup：FSCTL_GET_NTFS_FILE_RECORD 返回的记录已被系统修复，仅在校验匹配时才应用
        ApplyFixup(rec, 0, recordSize);
        int pos = BitConverter.ToUInt16(rec, 0x14);
        while (pos + 8 <= recordSize)
        {
            uint type = BitConverter.ToUInt32(rec, pos);
            if (type == 0xFFFFFFFF) break;
            uint attrLen = BitConverter.ToUInt32(rec, pos + 4);
            if (attrLen < 16) break;
            if (type == 0x80 && rec[pos + 8] != 0) // $DATA 非常驻
            {
                int runOff = pos + BitConverter.ToUInt16(rec, pos + 0x20);
                var list = new List<Extent>();
                long lcn = 0, vcn = 0;
                int i = runOff;
                while (i < recordSize && rec[i] != 0)
                {
                    int lenSize = rec[i] & 0xF, offSize = rec[i] >> 4;
                    i++;
                    if (lenSize > 8 || offSize > 8 || i + lenSize + offSize > recordSize) return null;
                    long runLen = 0, runOff2 = 0;
                    for (int k = 0; k < lenSize; k++) runLen |= (long)rec[i + k] << (8 * k);
                    i += lenSize;
                    for (int k = 0; k < offSize; k++) runOff2 |= (long)rec[i + k] << (8 * k);
                    if (offSize > 0 && (rec[i + offSize - 1] & 0x80) != 0) // 符号扩展
                        runOff2 |= -1L << (offSize * 8);
                    i += offSize;
                    lcn += runOff2;
                    list.Add(new Extent(vcn, lcn, runLen));
                    vcn += runLen;
                }
                return list.Count > 0 ? list : null;
            }
            pos += (int)attrLen;
        }
        return null;
    }

    private static Extent? FindExtent(List<Extent> extents, long vcn)
    {
        // 顺序查找即可（MFT extent 通常只有 1-3 个）
        foreach (var e in extents)
            if (vcn >= e.VcnStart && vcn < e.VcnStart + e.LengthClusters) return e;
        return null;
    }
}
