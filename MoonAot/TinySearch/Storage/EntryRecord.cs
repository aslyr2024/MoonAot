namespace MoonAot.TinySearch.Storage;

/// <summary>
/// 内存索引的单条文件/目录记录（紧凑 struct，按值存入字典，无对象头/指针开销）。
/// 只存文件名不存完整路径——路径由 EntryStore.GetPath 沿 ParentFrn 链按需构建（Everything 同款内存模型），
/// 扩展名也从 Name 即时推导，不再单独存字符串。
/// </summary>
public struct EntryRecord
{
    public const byte FlagDirectory = 1;
    public const byte FlagVisible = 2;

    /// <summary>父目录 FRN（NTFS）或父路径 FNV 哈希（非 NTFS）；根直属子项为 EntryStore.RootFrn</summary>
    public long ParentFrn;

    /// <summary>文件大小（目录为 0）</summary>
    public long Size;

    /// <summary>DateTime.UtcTicks</summary>
    public long ModifiedUtc;

    /// <summary>NTFS FILE_ATTRIBUTE_* / FileAttributes</summary>
    public uint Attributes;

    /// <summary>bit0=IsDirectory，bit1=Visible（未被排除规则过滤，可参与搜索）</summary>
    public byte Flags;

    /// <summary>文件名/目录名（不含路径），唯一的长字段</summary>
    public string Name = "";

    public EntryRecord() { }

    public bool IsDirectory
    {
        readonly get => (Flags & FlagDirectory) != 0;
        set { if (value) Flags |= FlagDirectory; else Flags &= unchecked((byte)~FlagDirectory); }
    }

    /// <summary>未被排除规则过滤（被排除的目录树仍保留在索引中，仅标记不可见，保证路径链完整可回溯）</summary>
    public bool Visible
    {
        readonly get => (Flags & FlagVisible) != 0;
        set { if (value) Flags |= FlagVisible; else Flags &= unchecked((byte)~FlagVisible); }
    }

    /// <summary>从文件名推导小写无点扩展名（目录返回空串）。</summary>
    public readonly string GetExtension()
    {
        if (IsDirectory) return "";
        int dot = Name.LastIndexOf('.');
        if (dot <= 0 || dot == Name.Length - 1) return "";
        return Name[(dot + 1)..].ToLowerInvariant();
    }
}

/// <summary>USN 实时变更回调单元：FRN + 该 FRN 的当前状态。</summary>
public readonly struct EntryChange
{
    public readonly ulong Frn;
    public readonly EntryRecord Record;
    public EntryChange(ulong frn, EntryRecord record) { Frn = frn; Record = record; }
}
