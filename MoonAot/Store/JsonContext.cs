using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MoonAot.Store;

/// <summary>
/// NativeAOT 源生成 JSON 上下文（禁止反射序列化，保证 AOT 可用）。
/// 统一 camelCase、压缩输出；空值不落盘以缩小 10 万级书库文件。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BookRecord))]
[JsonSerializable(typeof(List<BookRecord>))]
[JsonSerializable(typeof(ConfigRecord))]
[JsonSerializable(typeof(Dictionary<string, ConfigRecord>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(NoteView))]
[JsonSerializable(typeof(List<NoteView>))]
[JsonSerializable(typeof(LibraryItem))]
[JsonSerializable(typeof(List<LibraryItem>))]
[JsonSerializable(typeof(LibraryPage))]
[JsonSerializable(typeof(BookDetail))]
[JsonSerializable(typeof(CountItem))]
[JsonSerializable(typeof(List<CountItem>))]
[JsonSerializable(typeof(Facets))]
[JsonSerializable(typeof(StatsDto))]
[JsonSerializable(typeof(AudioMetaDto))]
[JsonSerializable(typeof(OkDto))]
[JsonSerializable(typeof(ImportResultDto))]
[JsonSerializable(typeof(FsEntry))]
[JsonSerializable(typeof(List<FsEntry>))]
[JsonSerializable(typeof(FsList))]
[JsonSerializable(typeof(FsRoot))]
[JsonSerializable(typeof(FsRoots))]
[JsonSerializable(typeof(ImageItem))]
[JsonSerializable(typeof(List<ImageItem>))]
[JsonSerializable(typeof(ImagePage))]
[JsonSerializable(typeof(ScanProgress))]
[JsonSerializable(typeof(MediaProgressItem))]
[JsonSerializable(typeof(List<MediaProgressItem>))]
[JsonSerializable(typeof(MediaProgressStoreData))]
[JsonSerializable(typeof(PlaylistRecord))]
[JsonSerializable(typeof(List<PlaylistRecord>))]
[JsonSerializable(typeof(PlaylistStoreData))]
[JsonSerializable(typeof(PlaylistSummary))]
[JsonSerializable(typeof(List<PlaylistSummary>))]
[JsonSerializable(typeof(PlaylistList))]
[JsonSerializable(typeof(LyricLine))]
[JsonSerializable(typeof(List<LyricLine>))]
[JsonSerializable(typeof(LyricsDto))]
[JsonSerializable(typeof(LyricsCacheData))]
[JsonSerializable(typeof(ZipItem))]
[JsonSerializable(typeof(List<ZipItem>))]
[JsonSerializable(typeof(ZipList))]
[JsonSerializable(typeof(TempFileDto))]
[JsonSerializable(typeof(JobStatus))]
[JsonSerializable(typeof(FontsDto))]
[JsonSerializable(typeof(AppInfoDto))]
[JsonSerializable(typeof(NnBookDto))]
[JsonSerializable(typeof(List<NnBookDto>))]
[JsonSerializable(typeof(BookConfigDto))]
[JsonSerializable(typeof(BookNoteRecord))]
[JsonSerializable(typeof(List<BookNoteRecord>))]
[JsonSerializable(typeof(MacroStep))]
[JsonSerializable(typeof(List<MacroStep>))]
[JsonSerializable(typeof(MacroRecord))]
[JsonSerializable(typeof(List<MacroRecord>))]
[JsonSerializable(typeof(MacroListDto))]
[JsonSerializable(typeof(MoonAot.TinySearch.Storage.AppSettings), TypeInfoPropertyName = "TinyAppSettings")]
[JsonSerializable(typeof(SystemInfoDto))]
[JsonSerializable(typeof(List<DiskInfoDto>))]
[JsonSerializable(typeof(HashResultDto))]
[JsonSerializable(typeof(ProcessItemDto))]
[JsonSerializable(typeof(List<ProcessItemDto>))]
[JsonSerializable(typeof(PortItemDto))]
[JsonSerializable(typeof(List<PortItemDto>))]
[JsonSerializable(typeof(SiteHostInfo))]
[JsonSerializable(typeof(List<SiteHostInfo>))]
[JsonSerializable(typeof(SiteHostList))]
[JsonSerializable(typeof(SiteFetchRequest))]
[JsonSerializable(typeof(SiteFetchResult))]
[JsonSerializable(typeof(SiteDownloadRequest))]
[JsonSerializable(typeof(SiteDownloadResult))]
[JsonSerializable(typeof(FavoriteFolder))]
[JsonSerializable(typeof(List<FavoriteFolder>))]
[JsonSerializable(typeof(MoonAot.Server.MoonServer.FileTextDto))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, long>))]
internal sealed partial class JsonContext : JsonSerializerContext
{
}

/// <summary>按类型取对应的 JsonTypeInfo（AOT 安全的泛型包装）。</summary>
internal static class Info<T>
{
    public static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> I =
        (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)JsonContext.Default.GetTypeInfo(typeof(T))!;
}
