using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MoonAot.TinySearch.Ntfs;

/// <summary>已打开的 NTFS 卷句柄：读取卷几何信息、MFT 原始数据。</summary>
internal sealed class NtfsVolume : IDisposable
{
    public SafeFileHandle Handle { get; }
    public int BytesPerCluster { get; }
    public int BytesPerFileRecord { get; }
    public long MftValidDataLength { get; }
    public long MftStartLcn { get; }

    private NtfsVolume(SafeFileHandle h, UsnNative.NTFS_VOLUME_DATA_BUFFER d)
    {
        Handle = h;
        BytesPerCluster = (int)d.BytesPerCluster;
        BytesPerFileRecord = (int)d.BytesPerFileRecordSegment;
        MftValidDataLength = d.MftValidDataLength;
        MftStartLcn = d.MftStartLcn;
    }

    /// <summary>需要管理员/卷访问权限；失败返回 null（诊断信息写入 Console）。</summary>
    public static NtfsVolume? Open(string driveLetter)
    {
        var h = UsnNative.CreateFile(UsnNative.VolumePath(driveLetter),
            UsnNative.GENERIC_READ, UsnNative.FILE_SHARE_READ | UsnNative.FILE_SHARE_WRITE,
            IntPtr.Zero, UsnNative.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            Console.WriteLine($"[ntfs] CreateFile {driveLetter} 失败 win32={Marshal.GetLastWin32Error()}");
            return null;
        }

        int outSize = Marshal.SizeOf<UsnNative.NTFS_VOLUME_DATA_BUFFER>();
        IntPtr outBuf = Marshal.AllocHGlobal(outSize);
        try
        {
            bool ok = UsnNative.DeviceIoControl(h, UsnNative.FSCTL_GET_NTFS_VOLUME_DATA,
                IntPtr.Zero, 0, outBuf, (uint)outSize, out _, IntPtr.Zero);
            if (!ok)
            {
                Console.WriteLine($"[ntfs] GET_NTFS_VOLUME_DATA {driveLetter} 失败 win32={Marshal.GetLastWin32Error()}");
                h.Dispose(); return null;
            }
            var data = Marshal.PtrToStructure<UsnNative.NTFS_VOLUME_DATA_BUFFER>(outBuf);
            if (data.BytesPerFileRecordSegment == 0 || data.BytesPerCluster == 0) { h.Dispose(); return null; }
            return new NtfsVolume(h, data);
        }
        finally { Marshal.FreeHGlobal(outBuf); }
    }

    /// <summary>通过 FSCTL 读取一条 MFT 记录（用于引导 $MFT 本身）。</summary>
    public byte[]? ReadFileRecord(ulong frn)
    {
        int inSize = Marshal.SizeOf<UsnNative.NTFS_FILE_RECORD_INPUT_BUFFER>();
        int outSize = 12 + BytesPerFileRecord + 16;
        IntPtr inBuf = Marshal.AllocHGlobal(inSize);
        IntPtr outBuf = Marshal.AllocHGlobal(outSize);
        try
        {
            var input = new UsnNative.NTFS_FILE_RECORD_INPUT_BUFFER { FileReferenceNumber = (long)frn };
            Marshal.StructureToPtr(input, inBuf, false);
            bool ok = UsnNative.DeviceIoControl(Handle, UsnNative.FSCTL_GET_NTFS_FILE_RECORD,
                inBuf, (uint)inSize, outBuf, (uint)outSize, out uint ret, IntPtr.Zero);
            if (!ok || ret < 12)
            {
                Console.WriteLine($"[ntfs] GET_NTFS_FILE_RECORD frn={frn} 失败 ok={ok} ret={ret} win32={(ok ? 0 : Marshal.GetLastWin32Error())}");
                return null;
            }
            int recLen = Marshal.ReadInt32(outBuf, 8);
            if (recLen <= 0 || recLen > BytesPerFileRecord)
            {
                Console.WriteLine($"[ntfs] 记录长度异常 recLen={recLen} expect<={BytesPerFileRecord}");
                return null;
            }
            byte[] buf = new byte[recLen];
            Marshal.Copy(outBuf + 12, buf, 0, recLen);
            return buf;
        }
        finally
        {
            Marshal.FreeHGlobal(inBuf);
            Marshal.FreeHGlobal(outBuf);
        }
    }

    /// <summary>从卷绝对偏移读取大块数据（顺序读 $MFT 用）。</summary>
    public unsafe bool ReadAbsolute(long offset, byte[] buffer, int count)
    {
        if (!UsnNative.SetFilePointerEx(Handle, offset, out _, 0)) return false;
        fixed (byte* p = buffer)
        {
            int total = 0;
            while (total < count)
            {
                bool ok = UsnNative.ReadFile(Handle, (IntPtr)(p + total), (uint)(count - total), out uint read, IntPtr.Zero);
                if (!ok || read == 0) return false;
                total += (int)read;
            }
        }
        return true;
    }

    public UsnNative.USN_JOURNAL_DATA? QueryJournal()
    {
        int size = Marshal.SizeOf<UsnNative.USN_JOURNAL_DATA>();
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            bool ok = UsnNative.DeviceIoControl(Handle, UsnNative.FSCTL_QUERY_USN_JOURNAL,
                IntPtr.Zero, 0, buf, (uint)size, out _, IntPtr.Zero);
            if (!ok) Console.WriteLine($"[ntfs] QUERY_USN_JOURNAL 失败 win32={Marshal.GetLastWin32Error()}");
            return ok ? Marshal.PtrToStructure<UsnNative.USN_JOURNAL_DATA>(buf) : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public bool CreateJournal(ulong maxSize = 32u * 1024 * 1024, ulong delta = 4u * 1024 * 1024)
    {
        var data = new UsnNative.CREATE_USN_JOURNAL_DATA { MaximumSize = maxSize, AllocationDelta = delta };
        int size = Marshal.SizeOf(data);
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(data, buf, false);
            return UsnNative.DeviceIoControl(Handle, UsnNative.FSCTL_CREATE_USN_JOURNAL,
                buf, (uint)size, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Dispose() => Handle.Dispose();
}
