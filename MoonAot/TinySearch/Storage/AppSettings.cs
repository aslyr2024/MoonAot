using System.Collections.Generic;
using System.IO;

namespace MoonAot.TinySearch.Storage;

/// <summary>全盘索引设置（由应用设置页/网页设置面板修改）。</summary>
public class AppSettings
{
    public List<string> ExcludedFolders { get; set; } = new()
    {
        @"C:\$Recycle.Bin",
        @"C:\System Volume Information",
    };

    public List<string> IncludedFolders { get; set; } = new();
    public List<string> EnabledDrives { get; set; } = new();
    public int RescanIntervalHours { get; set; } = 0;
}
