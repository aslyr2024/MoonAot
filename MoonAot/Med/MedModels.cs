using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoonAot.Med;

/// <summary>医学助手全部 API DTO。字段保持简单类型，供 NativeAOT 源生成序列化。</summary>
/// <summary>精读页全量词条索引项（轻量，一次加载到前端做最长匹配高亮）。</summary>
public class MedTermDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
}

public class MedOk
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public string? Id { get; set; }
    public int Count { get; set; }
}

public class MedStatusDto
{
    public bool Ready { get; set; }
    public string Version { get; set; } = "";
    public string DbPath { get; set; } = "";
    public long DbBytes { get; set; }
    public int Books { get; set; }
    public int Chapters { get; set; }
    public int Chunks { get; set; }
    public int Concepts { get; set; }
    public int Notes { get; set; }
    public int Tags { get; set; }
    public int Templates { get; set; }
    public int Links { get; set; }
    public int Favorites { get; set; }
    public long Chars { get; set; }
    public string ClinicalVersion { get; set; } = "";
    public string ClinicalDefault { get; set; } = "";
    public bool ClinicalDefaultExists { get; set; }
    public List<MedDefaultSourceDto> DefaultBooks { get; set; } = new();
    public string SourcesDir { get; set; } = "";
}

public class MedDefaultSourceDto
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Volume { get; set; } = "";
    public bool Exists { get; set; }
}

public class MedTaggedItemDto
{
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Sub { get; set; } = "";
    public string Type { get; set; } = "";
    public string ChapterId { get; set; } = "";
    public long AddedAt { get; set; }
}

public class MedManifestDto
{
    public List<string> NavItems { get; set; } = new();
    public List<string> CardTypes { get; set; } = new();
    public List<string> SourceKinds { get; set; } = new();
    public List<string> QuickActions { get; set; } = new();
    public string Version { get; set; } = "0.1";
}

public class MedBookDto
{
    public string Id { get; set; } = "";
    public string BookId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Volume { get; set; } = "";
    public string Type { get; set; } = "epub";
    public string Path { get; set; } = "";
    public string Status { get; set; } = "pending";
    public long AddedAt { get; set; }
    public long IndexedAt { get; set; }
    public int DocCount { get; set; }
    public int ChapterCount { get; set; }
    public int ChunkCount { get; set; }
    public long CharCount { get; set; }
    public int AnchorTotal { get; set; }
    public int AnchorResolved { get; set; }
    public int ImageCount { get; set; }
    public long ElapsedMs { get; set; }
    public string? Error { get; set; }
}

public class MedChapterDto
{
    public string Id { get; set; } = "";
    public string BookId { get; set; } = "";
    public string ParentId { get; set; } = "";
    public int Level { get; set; }
    public int SpineIndex { get; set; }
    public string Href { get; set; } = "";
    public string Anchor { get; set; } = "";
    public string Title { get; set; } = "";
    public string PageHint { get; set; } = "";
    public int Ord { get; set; }
    public int ImgCount { get; set; }
}

public class MedChapterViewDto
{
    public string Html { get; set; } = "";
    public string BookId { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public string ChapterId { get; set; } = "";
    public string ChapterPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Prev { get; set; } = "";
    public string Next { get; set; } = "";
    public int ImageCount { get; set; }
    public List<MedConceptDto> Concepts { get; set; } = new();
    public List<MedNoteDto> Backlinks { get; set; } = new();
    public List<MedChapterDto> Siblings { get; set; } = new();
    public List<MedAnchorDto> Anchors { get; set; } = new();
}

public class MedAnchorDto
{
    public string Id { get; set; } = "";
    public string BookId { get; set; } = "";
    public string ChapterId { get; set; } = "";
    public string ChunkId { get; set; } = "";
    public int PStart { get; set; }
    public string Label { get; set; } = "";
    public string Note { get; set; } = "";
    public long CreatedAt { get; set; }
}

public class MedConceptDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameNorm { get; set; } = "";
    public string Pinyin { get; set; } = "";
    public string Initials { get; set; } = "";
    public string Category { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public string Summary { get; set; } = "";
    public string Source { get; set; } = "";
    public int Edited { get; set; }
    public int Hits { get; set; }
    public List<MedHitDto> Occurrences { get; set; } = new();
    public List<MedConceptDto> Related { get; set; } = new();
    public List<MedNoteDto> Backlinks { get; set; } = new();
}

public class MedHitDto
{
    public string ChunkId { get; set; } = "";
    public string BookId { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public string Volume { get; set; } = "";
    public string ChapterId { get; set; } = "";
    public string ChapterPath { get; set; } = "";
    public string ChapterTitle { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string Href { get; set; } = "";
    public int PStart { get; set; }
    public int PEnd { get; set; }
    public int Ord { get; set; }
    public double Score { get; set; }
    public string Source { get; set; } = "fts";
    public List<string> Concepts { get; set; } = new();
}

public class MedSearchResultDto
{
    public string Q { get; set; } = "";
    public long TookMs { get; set; }
    public int Total { get; set; }
    public int ConceptTotal { get; set; }
    public int HitTotal { get; set; }
    public List<MedConceptDto> Concepts { get; set; } = new();
    public List<MedHitDto> Hits { get; set; } = new();
    public List<MedNoteDto> Notes { get; set; } = new();
    public List<MedTemplateDto> Templates { get; set; } = new();
    public string Mode { get; set; } = "";
}

public class MedNoteDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string BookId { get; set; } = "";
    public string ChapterId { get; set; } = "";
    public string ChunkId { get; set; } = "";
    public string Quote { get; set; } = "";
    public string Color { get; set; } = "";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}

public class MedTagDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParentId { get; set; } = "";
    public string Color { get; set; } = "";
    public int Ord { get; set; }
    public string Kind { get; set; } = "category";
    public int Count { get; set; }
    public List<MedTagDto> Children { get; set; } = new();
}

public class MedTemplateDto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "note";
    public string Name { get; set; } = "";
    public string SchemaJson { get; set; } = "{}";
    public string ContentJson { get; set; } = "{}";
    public int Pinned { get; set; }
    public int Builtin { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}

public class MedQueryDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DslJson { get; set; } = "{}";
    public int Pinned { get; set; }
    public long LastUsedAt { get; set; }
    public long CreatedAt { get; set; }
}

public class MedGraphNode
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public int Weight { get; set; }
}

public class MedGraphEdge
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Weight { get; set; }
}

public class MedGraphDto
{
    public List<MedGraphNode> Nodes { get; set; } = new();
    public List<MedGraphEdge> Edges { get; set; } = new();
    public int Truncated { get; set; }
}

public class MedImportReportDto
{
    public bool Ok { get; set; }
    public string Kind { get; set; } = "";
    public string JobId { get; set; } = "";
    public string? Error { get; set; }
    public int Books { get; set; }
    public int Chapters { get; set; }
    public int Chunks { get; set; }
    public int Concepts { get; set; }
    public int Hits { get; set; }
    public int Links { get; set; }
    public int AnchorTotal { get; set; }
    public int AnchorResolved { get; set; }
    public int Images { get; set; }
    public long Chars { get; set; }
    public long ElapsedMs { get; set; }
    public long DbBytes { get; set; }
    public List<string> Messages { get; set; } = new();
}

public class MedJobDto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Running { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }
    public string Message { get; set; } = "";
    public long StartedAt { get; set; }
    public long FinishedAt { get; set; }
    public MedImportReportDto? Report { get; set; }
}

public class MedSuggestDto
{
    public string Q { get; set; } = "";
    public List<string> Items { get; set; } = new();
    public List<MedConceptDto> Concepts { get; set; } = new();
}

public class MedExportDto
{
    public string Version { get; set; } = "0.1";
    public long ExportedAt { get; set; }
    public List<MedNoteDto> Notes { get; set; } = new();
    public List<MedTagDto> Tags { get; set; } = new();
    public List<MedTemplateDto> Templates { get; set; } = new();
    public List<MedQueryDto> Queries { get; set; } = new();
}

public class MedSearchDsl
{
    public string Q { get; set; } = "";
    public string Mode { get; set; } = "phrase";
    public string Sort { get; set; } = "relevance";
    public List<string> Scope { get; set; } = new();
    public List<string> Types { get; set; } = new();
    public List<string> Books { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public bool FullText { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public class MedDocTemplateContent
{
    public string Content { get; set; } = "";
    public string Category { get; set; } = "";
    public string Disease { get; set; } = "";
    public string Department { get; set; } = "";
}

/// <summary>AOT 源生成上下文：医学助手所有响应类型集中注册，避免反射序列化。</summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(MedOk))]
[JsonSerializable(typeof(MedStatusDto))]
[JsonSerializable(typeof(MedDefaultSourceDto))]
[JsonSerializable(typeof(List<MedDefaultSourceDto>))]
[JsonSerializable(typeof(MedTaggedItemDto))]
[JsonSerializable(typeof(List<MedTaggedItemDto>))]
[JsonSerializable(typeof(MedManifestDto))]
[JsonSerializable(typeof(MedBookDto))]
[JsonSerializable(typeof(List<MedBookDto>))]
[JsonSerializable(typeof(MedChapterDto))]
[JsonSerializable(typeof(List<MedChapterDto>))]
[JsonSerializable(typeof(MedChapterViewDto))]
[JsonSerializable(typeof(MedAnchorDto))]
[JsonSerializable(typeof(List<MedAnchorDto>))]
[JsonSerializable(typeof(MedConceptDto))]
[JsonSerializable(typeof(List<MedConceptDto>))]
[JsonSerializable(typeof(MedHitDto))]
[JsonSerializable(typeof(List<MedHitDto>))]
[JsonSerializable(typeof(MedSearchResultDto))]
[JsonSerializable(typeof(MedNoteDto))]
[JsonSerializable(typeof(List<MedNoteDto>))]
[JsonSerializable(typeof(MedTagDto))]
[JsonSerializable(typeof(List<MedTagDto>))]
[JsonSerializable(typeof(MedTemplateDto))]
[JsonSerializable(typeof(List<MedTemplateDto>))]
[JsonSerializable(typeof(MedQueryDto))]
[JsonSerializable(typeof(List<MedQueryDto>))]
[JsonSerializable(typeof(MedGraphDto))]
[JsonSerializable(typeof(MedImportReportDto))]
[JsonSerializable(typeof(MedRelink.Result))]
[JsonSerializable(typeof(MedMineReportDto))]
[JsonSerializable(typeof(MedJobDto))]
[JsonSerializable(typeof(MedSuggestDto))]
[JsonSerializable(typeof(MedExportDto))]
[JsonSerializable(typeof(MedDocTemplateContent))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<MedTermDto>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class MedJsonContext : JsonSerializerContext
{
}

internal static class MedJson
{
    public static string S<T>(T value)
        => JsonSerializer.Serialize(value!, (JsonTypeInfo<T>)MedJsonContext.Default.GetTypeInfo(typeof(T))!);
}
