using System;

namespace MoonAot.TinySearch.Storage;

/// <summary>搜索结果 DTO：仅在查询/展示期按需物化。</summary>
public class FileEntry
{
    public string DriveLetter { get; set; } = "";
    public long Frn { get; set; }
    public long ParentFrn { get; set; }
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long Size { get; set; }
    public string Extension { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long ModifiedUtc { get; set; }
    public long Attributes { get; set; }
}
