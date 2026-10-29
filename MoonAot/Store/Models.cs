using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoonAot.Store;

/// <summary>书籍格式常量与 readest 格式映射。</summary>
public static class BookFormatEx
{
    public const string Txt = "TXT";
    public const string Epub = "EPUB";
    public const string Pdf = "PDF";
    public const string Mobi = "MOBI";
    public const string Azw3 = "AZW3";
    public const string Md = "MD";

    public static string NnFormat(string format) => format switch
    {
        Epub => "EPUB",
        Pdf => "PDF",
        Mobi => "MOBI",
        Azw3 => "MOBI",       // AZW3 与 MOBI 共用引擎
        Md => "TXT",          // Markdown 按 TXT 渲染
        _ => "TXT"
    };

    public static bool IsBookExt(string ext) => ext switch
    {
        ".txt" or ".epub" or ".pdf" or ".mobi" or ".azw3" or ".md" or ".markdown" => true,
        _ => false
    };
}

/// <summary>书籍记录（分片保存在 data/books/xx.json）。字段名保持与旧版 books.json 兼容。</summary>
public class BookRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    /// <summary>实际读取的书籍文件路径。</summary>
    public string FilePath { get; set; } = "";
    /// <summary>原始路径：为空表示 FilePath 是应用托管的副本（可从书架彻底删除）。</summary>
    public string? SourcePath { get; set; }
    public string Format { get; set; } = BookFormatEx.Txt;
    public long FileSize { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? LastReadAt { get; set; }
    /// <summary>阅读进度 0~1。</summary>
    public double Progress { get; set; }
    /// <summary>阅读定位（EPUB CFI / PDF 页码等）。</summary>
    public string? Location { get; set; }
    /// <summary>手动指定封面文件路径（为空时使用提取封面/缺省封面）。</summary>
    public string? CoverFile { get; set; }
    public string? CoverExt { get; set; }
    /// <summary>封面版本号，用于缩略图 URL 破缓存。</summary>
    public int CoverVersion { get; set; }

    // 以下为元数据（旧版没有，反序列化时取默认值）
    public string? Publisher { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public string? Category { get; set; }
    public bool Favorite { get; set; }
    /// <summary>累计阅读秒数（粗略统计，用于阅读统计页）。</summary>
    public long ReadingSeconds { get; set; }
    /// <summary>完成度标记时间（达到 99.5% 时写入）。</summary>
    public DateTime? FinishedAt { get; set; }

    public BookRecord Clone() => (BookRecord)MemberwiseClone();
}

/// <summary>每本书的阅读配置（分文件保存在 data/configs/{bookId}.json）。</summary>
public class ConfigRecord
{
    public string BookId { get; set; } = "";
    /// <summary>readest BookConfig 完整 JSON（progress/location/viewSettings/booknotes/searchConfig）。</summary>
    public string? ConfigJson { get; set; }
    public string? Progress { get; set; }
    public string? Location { get; set; }
    public string? ViewSettings { get; set; }
    public string? PdfLayout { get; set; }
    public string? PdfZoom { get; set; }
    public bool? PdfAutoPage { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ConfigRecord Clone() => (ConfigRecord)MemberwiseClone();
}

/// <summary>媒体库偏好。</summary>
public class MediaSettings
{
    /// <summary>幻灯片间隔（秒），0.1–60，可含一位小数。</summary>
    public double ImageSlideSeconds { get; set; } = 5;
    public bool ComicMode { get; set; }
    /// <summary>full / 2/3 / 1/2 / default</summary>
    public string ComicWidth { get; set; } = "default";
    public bool ComicAutoPage { get; set; }
    public bool RecursiveImages { get; set; }
    /// <summary>音乐播放器背景轮播间隔（秒），0.1–60。</summary>
    public double MusicBackgroundSeconds { get; set; } = 8;
    public bool MusicBackgroundRandom { get; set; }
    /// <summary>背景图显示方式：cover / contain / stretch / center。</summary>
    public string MusicBackgroundFit { get; set; } = "cover";
    /// <summary>磨玻璃强度 0–10（0 为不模糊）。</summary>
    public int MusicBackgroundBlur { get; set; } = 5;
    public bool MusicShowLyrics { get; set; } = true;
    public bool VideoAutoResume { get; set; } = true;
    /// <summary>幻灯片切换效果：none / fade / slide / zoom。</summary>
    public string ImageTransition { get; set; } = "fade";
    /// <summary>图片显示方式：contain / width / height / center / actual。</summary>
    public string ImageFitMode { get; set; } = "contain";
    /// <summary>幻灯片背景音乐文件（空 = 不播放）。</summary>
    public string? ImageBackgroundMusic { get; set; }
    /// <summary>幻灯片背景音乐文件夹（优先于单文件；空 = 不使用文件夹）。</summary>
    public string? ImageBackgroundMusicFolder { get; set; }
    /// <summary>幻灯片背景音乐使用的已保存音乐列表 ID（优先于文件夹/单文件；空 = 不使用列表）。</summary>
    public string? ImageBackgroundMusicPlaylist { get; set; }
    /// <summary>背景音乐文件夹上次播放到的索引。</summary>
    public int ImageBackgroundMusicIndex { get; set; }
    /// <summary>幻灯片到末尾后是否循环。</summary>
    public bool ImageSlideLoop { get; set; } = true;
    /// <summary>音乐播放器上次音量（0~1）。</summary>
    public double MusicVolume { get; set; } = 0.9;
    /// <summary>音乐播放器上次播放模式：order / repeat / shuffle。</summary>
    public string MusicMode { get; set; } = "order";
    /// <summary>音乐播放器上次标签页：list / lyrics。</summary>
    public string MusicTab { get; set; } = "list";
    /// <summary>视频播放器上次音量（0~1）。</summary>
    public double VideoVolume { get; set; } = 1.0;
    /// <summary>视频播放器是否显示右侧播放列表。</summary>
    public bool VideoShowList { get; set; } = true;
    /// <summary>音乐播放器背景轮播图片文件夹（空 = 使用当前歌曲所在目录的图片）。</summary>
    public string? MusicBackgroundFolder { get; set; }
    /// <summary>上次轮播到的背景图索引。</summary>
    public int MusicBackgroundIndex { get; set; }
    /// <summary>图片浏览器上次打开的文件夹（空 = 未记录）。</summary>
    public string? ImageLastFolder { get; set; }
}

/// <summary>窗口尺寸记忆。</summary>
public class WindowState
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool HasPosition { get; set; }
    public bool Maximized { get; set; }

    public WindowState() { }
    public WindowState(int w, int h) { Width = w; Height = h; }
}

/// <summary>应用自身设置（data/appsettings.json）。</summary>
public class AppSettings
{
    /// <summary>dark / light / forest / violet / warm</summary>
    public string AppTheme { get; set; } = "dark";
    /// <summary>added / recent / title / author / size / format</summary>
    public string SortMode { get; set; } = "recent";
    public bool SortDesc { get; set; } = true;
    public bool ShowCovers { get; set; } = true;
    /// <summary>grid / list</summary>
    public string LibraryView { get; set; } = "grid";
    /// <summary>封面显示宽度（px），书架据此计算列数。</summary>
    public int GridSize { get; set; } = 168;
    /// <summary>readest SystemSettings 完整 JSON（原样透传阅读器）。</summary>
    public string ReaderSettingsJson { get; set; } = "{}";
    /// <summary>缺省封面（covers 目录下的 _default.*）。</summary>
    public string? DefaultCoverPath { get; set; }
    public bool ImportCovers { get; set; } = true;
    /// <summary>无提取封面的书（EPUB/PDF/MOBI/AZW3）是否显示缺省封面。</summary>
    public bool UseDefaultCover { get; set; } = true;
    public List<string> RecentFolders { get; set; } = new();
    public string? LastFolder { get; set; }
    public MediaSettings Media { get; set; } = new();
    public WindowState ShelfWindow { get; set; } = new(1360, 900);
    public WindowState ReaderWindow { get; set; } = new(1120, 860);
    public WindowState ViewWindow { get; set; } = new(1280, 820);
    public WindowState BrowserWindow { get; set; } = new(1280, 820);
    public long ReadingSeconds { get; set; }
    /// <summary>按日期（yyyy-MM-dd）累计的阅读秒数，用于统计图。</summary>
    public Dictionary<string, long> DailyReading { get; set; } = new();
    public bool AutoRefreshLibrary { get; set; } = true;
    /// <summary>书架筛选/搜索状态 JSON（q/filter/format/author/folder），重启后恢复。</summary>
    public string? ShelfQueryJson { get; set; }
    public string? LastVersion { get; set; }

    public AppSettings Clone()
    {
        var c = (AppSettings)MemberwiseClone();
        c.RecentFolders = new List<string>(RecentFolders);
        c.Media = new MediaSettings
        {
            ImageSlideSeconds = Media.ImageSlideSeconds,
            ComicMode = Media.ComicMode,
            ComicWidth = Media.ComicWidth,
            ComicAutoPage = Media.ComicAutoPage,
            RecursiveImages = Media.RecursiveImages,
            MusicBackgroundSeconds = Media.MusicBackgroundSeconds,
            MusicShowLyrics = Media.MusicShowLyrics,
            VideoAutoResume = Media.VideoAutoResume,
        };
        c.ShelfWindow = CopyWindow(ShelfWindow);
        c.ReaderWindow = CopyWindow(ReaderWindow);
        c.ViewWindow = CopyWindow(ViewWindow);
        c.DailyReading = new Dictionary<string, long>(DailyReading);
        return c;
    }

    private static WindowState CopyWindow(WindowState w) => new()
    {
        Width = w.Width, Height = w.Height, X = w.X, Y = w.Y,
        HasPosition = w.HasPosition, Maximized = w.Maximized,
    };
}

/// <summary>聚合笔记视图（从各书 configJson.booknotes 提取）。</summary>
public class NoteView
{
    public string Id { get; set; } = "";
    public string BookId { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public string Type { get; set; } = "annotation";
    public string Cfi { get; set; } = "";
    public string? Text { get; set; }
    public string Note { get; set; } = "";
    public string? Style { get; set; }
    public string? Color { get; set; }
    public int? Page { get; set; }
    public double Progress { get; set; }
    public string? Label { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public string? Chapter { get; set; }
}

// ==================== API DTO ====================

/// <summary>书架列表项（轻量，分页返回）。</summary>
public class LibraryItem
{
    public string Hash { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Format { get; set; } = "";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public double Progress { get; set; }
    public int CoverVersion { get; set; }
    public bool HasCover { get; set; }
    public bool Favorite { get; set; }
    public long FileSize { get; set; }
    public string? Category { get; set; }
    public string? Folder { get; set; }
    public bool Missing { get; set; }
    public bool Finished { get; set; }
}

public class LibraryPage
{
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public long Version { get; set; }
    public List<LibraryItem> Items { get; set; } = new();
}

public class BookDetail
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Format { get; set; } = "";
    public long FileSize { get; set; }
    public long AddedAt { get; set; }
    public long? LastReadAt { get; set; }
    public double Progress { get; set; }
    public string? Location { get; set; }
    public string FilePath { get; set; } = "";
    public string? SourcePath { get; set; }
    public bool HasCover { get; set; }
    public int CoverVersion { get; set; }
    public int Notes { get; set; }
    public bool Missing { get; set; }
    public bool Favorite { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? Language { get; set; }
    public long ReadingSeconds { get; set; }
    public bool Finished { get; set; }
}

public class CountItem
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public class Facets
{
    public int Total { get; set; }
    public int Favorites { get; set; }
    public int Finished { get; set; }
    public int Reading { get; set; }
    public int Unread { get; set; }
    public List<CountItem> Formats { get; set; } = new();
    public List<CountItem> Authors { get; set; } = new();
    public List<CountItem> Folders { get; set; } = new();
}

public class StatsDto
{
    public int TotalBooks { get; set; }
    public int FinishedBooks { get; set; }
    public int ReadingBooks { get; set; }
    public int UnreadBooks { get; set; }
    public int Notes { get; set; }
    public int Favorites { get; set; }
    public long ReadingSeconds { get; set; }
    public long StorageBytes { get; set; }
    public long CoversBytes { get; set; }
    public int Covers { get; set; }
    public int Missing { get; set; }
    public List<CountItem> Formats { get; set; } = new();
    public List<CountItem> RecentDays { get; set; } = new();
}

public class OkDto
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public int Count { get; set; }
    public int Applied { get; set; }
    public string? Id { get; set; }
    public long Received { get; set; }
    public string? UploadId { get; set; }
    public string? Path { get; set; }
}

public class ImportResultDto
{
    public bool Ok { get; set; }
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public string? Error { get; set; }
}

public class FsEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsDir { get; set; }
    public long Size { get; set; }
    public long Modified { get; set; }
    /// <summary>book / audio / video / image / zip / other</summary>
    public string Kind { get; set; } = "other";
    public string Ext { get; set; } = "";
}

public class FsList
{
    public string Path { get; set; } = "";
    public string? Parent { get; set; }
    public int Total { get; set; }
    public bool Truncated { get; set; }
    public List<FsEntry> Entries { get; set; } = new();
}

public class FsRoot
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "folder";
}

public class FsRoots
{
    public List<FsRoot> Roots { get; set; } = new();
    public List<FsRoot> Drives { get; set; } = new();
    public List<string> Recent { get; set; } = new();
}

public class ImageItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
}

public class ImagePage
{
    public string Path { get; set; } = "";
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int Index { get; set; } = -1;
    public bool Scanning { get; set; }
    public string? Message { get; set; }
    public List<ImageItem> Items { get; set; } = new();
}

public class AudioMetaDto
{
    public bool Ok { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public bool HasCover { get; set; }
    public string? Error { get; set; }
}

public class ScanProgress
{
    public string Phase { get; set; } = "idle";
    public int Files { get; set; }
    public int Scanned { get; set; }
    public string? CurrentDir { get; set; }
    public long ElapsedMs { get; set; }
}

public class MediaProgressItem
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "audio";
    public double Position { get; set; }
    public double Duration { get; set; }
    public long UpdatedAt { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    /// <summary>图片查看器缩放比例（其他媒体为 1）。</summary>
    public double Scale { get; set; } = 1;
    /// <summary>图片查看器旋转角度（0/90/180/270，其他媒体为 0）。</summary>
    public double Rotation { get; set; }
}

public class MediaProgressStoreData
{
    public Dictionary<string, MediaProgressItem> Items { get; set; } = new();
}

public class PlaylistRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "播放列表";
    public string Kind { get; set; } = "audio";
    public List<string> Items { get; set; } = new();
    public int Current { get; set; }
    public long UpdatedAt { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();
}

public class PlaylistStoreData
{
    public string? LastId { get; set; }
    public List<PlaylistRecord> Playlists { get; set; } = new();
}

public class PlaylistSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "audio";
    public int Count { get; set; }
    public long UpdatedAt { get; set; }
}

public class PlaylistList
{
    public string? LastId { get; set; }
    public List<PlaylistSummary> Items { get; set; } = new();
}

public class LyricLine
{
    public double T { get; set; }
    public string Text { get; set; } = "";
}

public class LyricsDto
{
    public bool Ok { get; set; }
    public string Source { get; set; } = "none";
    public bool Synced { get; set; }
    public string Plain { get; set; } = "";
    public List<LyricLine> Lines { get; set; } = new();
    public string? Title { get; set; }
    public string? Artist { get; set; }
}

public class LyricsCacheData
{
    public string Key { get; set; } = "";
    public long FetchedAt { get; set; }
    public string Source { get; set; } = "";
    public bool Synced { get; set; }
    public string Plain { get; set; } = "";
    /// <summary>原始 LRC 文本（SQLite 中直接存储，读取时解析）。</summary>
    public string? Lrc { get; set; }
    /// <summary>旧版 JSON 缓存字段，仅迁移时使用。</summary>
    public List<LyricLine> Lines { get; set; } = new();
}

public class ZipItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsDir { get; set; }
    public long Size { get; set; }
    public long CompressedSize { get; set; }
}

public class ZipList
{
    public string Path { get; set; } = "";
    public string Prefix { get; set; } = "";
    public int Total { get; set; }
    public List<ZipItem> Items { get; set; } = new();
}

public class TempFileDto
{
    public bool Ok { get; set; }
    public string? Path { get; set; }
    public string? Error { get; set; }
}

public class JobStatus
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Running { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }
    public string? Message { get; set; }
    public long StartedAt { get; set; }
    public long FinishedAt { get; set; }
}

public class FontsDto
{
    public List<string> Fonts { get; set; } = new();
    public List<string> Embedded { get; set; } = new();
}

public class AppInfoDto
{
    public string Version { get; set; } = "1.0.0";
    public string DataDir { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public int BookCount { get; set; }
    public bool IsAot { get; set; }
    public string Os { get; set; } = "";
    public string? WebViewVersion { get; set; }
    public long StartedAt { get; set; }
}

/// <summary>readest 阅读器需要的书籍项（/api/library）。</summary>
public class NnBookDto
{
    public string Hash { get; set; } = "";
    public string Format { get; set; } = "TXT";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public double[]? Progress { get; set; }
    public string? CoverImageUrl { get; set; }
}

/// <summary>readest BookConfig 的强类型视图（保留未知字段，供笔记增删改使用；AOT 安全，替代 JsonNode）。</summary>
public class BookConfigDto
{
    public string? BookHash { get; set; }
    public JsonElement? Progress { get; set; }
    public string? Location { get; set; }
    public JsonElement? ViewSettings { get; set; }
    public JsonElement? SearchConfig { get; set; }
    public List<BookNoteRecord>? Booknotes { get; set; }
    public long? UpdatedAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public class BookNoteRecord
{
    public string? BookHash { get; set; }
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Cfi { get; set; }
    public string? Text { get; set; }
    public string? Style { get; set; }
    public string? Color { get; set; }
    public string? Note { get; set; }
    public long? CreatedAt { get; set; }
    public long? UpdatedAt { get; set; }
    public JsonElement? DeletedAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

// ==================== 按键精灵 ====================

/// <summary>宏步骤。delayMs 表示本步执行完后的等待时间。</summary>
public class MacroStep
{
    /// <summary>key / delay / mouse_move / mouse_click / mouse_wheel / text</summary>
    public string Kind { get; set; } = "key";
    /// <summary>组合键，如 ["S"]、["S","D"]、["CTRL","K"]。</summary>
    public List<string> Keys { get; set; } = new();
    /// <summary>按键按下保持时间（毫秒）。</summary>
    public int HoldMs { get; set; } = 30;
    /// <summary>本步结束后的等待时间（毫秒）。</summary>
    public int DelayMs { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>left / right / middle / x1 / x2</summary>
    public string? Button { get; set; } = "left";
    /// <summary>滚轮增量（120 = 一格）。</summary>
    public int Wheel { get; set; } = 120;
    public string? Text { get; set; }
}

/// <summary>一条宏：触发方式 + 自定义步骤序列。</summary>
public class MacroRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新宏";
    public bool Enabled { get; set; } = true;
    /// <summary>hotkey = 全局热键；remap = 拦截触发键并替换为宏。</summary>
    public string TriggerMode { get; set; } = "hotkey";
    /// <summary>如 F1、CTRL+SHIFT+1、S（remap 可单键）、XButton1。</summary>
    public string Trigger { get; set; } = "F1";
    public int Repeat { get; set; } = 1;
    public int RepeatDelayMs { get; set; } = 500;
    /// <summary>从界面点“测试运行”时的准备时间，方便切到游戏窗口。</summary>
    public int LeadInMs { get; set; } = 1500;
    /// <summary>scan = 扫描码（游戏兼容更好）；virtual = 虚拟键。</summary>
    public string SendMode { get; set; } = "scan";
    public List<MacroStep> Steps { get; set; } = new();
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    /// <summary>注册热键失败原因（仅 API 返回，不落库）。</summary>
    public string? HotkeyError { get; set; }

    public MacroRecord Clone()
    {
        return new MacroRecord
        {
            Id = Id, Name = Name, Enabled = Enabled, TriggerMode = TriggerMode, Trigger = Trigger,
            Repeat = Repeat, RepeatDelayMs = RepeatDelayMs, LeadInMs = LeadInMs, SendMode = SendMode,
            CreatedAt = CreatedAt, UpdatedAt = UpdatedAt, HotkeyError = HotkeyError,
            Steps = Steps.Select(s => new MacroStep
            {
                Kind = s.Kind, Keys = new List<string>(s.Keys), HoldMs = s.HoldMs, DelayMs = s.DelayMs,
                X = s.X, Y = s.Y, Button = s.Button, Wheel = s.Wheel, Text = s.Text,
            }).ToList(),
        };
    }
}

/// <summary>媒体库收藏夹（文件夹）。</summary>
public class FavoriteFolder
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long AddedAt { get; set; }
}

/// <summary>按键精灵列表快照（含运行/录制状态与全局开关）。</summary>
public class MacroListDto
{
    public List<MacroRecord> Items { get; set; } = new();
    public bool GlobalEnabled { get; set; } = true;
    public bool Recording { get; set; }
    public bool Running { get; set; }
}

// ==================== 工具箱 / 网站工具 ====================

public class DiskInfoDto
{
    public string Name { get; set; } = "";
    public long Total { get; set; }
    public long Free { get; set; }
}

public class SystemInfoDto
{
    public string Os { get; set; } = "";
    public string Machine { get; set; } = "";
    public string User { get; set; } = "";
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public int CpuCount { get; set; }
    public string CpuName { get; set; } = "";
    public long MemoryTotal { get; set; }
    public long MemoryAvailable { get; set; }
    public long WorkingSet { get; set; }
    public long UptimeSeconds { get; set; }
    public string AppVersion { get; set; } = "";
    public string DataDir { get; set; } = "";
    public List<DiskInfoDto> Disks { get; set; } = new();
}

public class ProcessItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public long Memory { get; set; }
    public long StartedAt { get; set; }
}

public class PortItemDto
{
    public string Proto { get; set; } = "";
    public string Local { get; set; } = "";
    public string Remote { get; set; } = "";
    public string State { get; set; } = "";
    public int Pid { get; set; }
    public int Port { get; set; }
}

public class HashResultDto
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Hash { get; set; }
    public string Algo { get; set; } = "";
    public long Size { get; set; }
    public string? Path { get; set; }
}

public class SiteHostInfo
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";
    public long StartedAt { get; set; }
}

public class SiteHostList
{
    public List<SiteHostInfo> Items { get; set; } = new();
}

public class SiteFetchRequest
{
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string? Body { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

public class SiteFetchResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int Status { get; set; }
    public string? ContentType { get; set; }
    public long Length { get; set; }
    public string? Body { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
}

public class SiteDownloadRequest
{
    public string Url { get; set; } = "";
    public string Folder { get; set; } = "";
    public int MaxPages { get; set; } = 1;
    public int MaxDepth { get; set; } = 2;
}

public class SiteDownloadResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int Files { get; set; }
    public int Pages { get; set; }
    public long Bytes { get; set; }
    public string? Entry { get; set; }
    public List<string> Written { get; set; } = new();
}
