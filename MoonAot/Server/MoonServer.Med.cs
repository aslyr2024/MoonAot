using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using MoonAot.Med;
using MoonAot.Services;

namespace MoonAot.Server;

/// <summary>医学助手 API：/medapi/*。与 /zimapi/ 同构，独立命名空间，不侵入现有 /api/*。</summary>
public sealed partial class MoonServer
{
    private void HandleMedApi(HttpListenerContext ctx, string tail, string query)
    {
        var seg = tail.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var action = seg.Length > 0 ? seg[0] : "";
        var id = seg.Length > 1 ? seg[1] : "";
        var req = ctx.Request;
        try
        {
            switch (action)
            {
                case "status":
                    SendJsonString(ctx, 200, MedJson.S(MedStore.Status())); return;
                case "manifest":
                    SendJsonString(ctx, 200, MedJson.S(new MedManifestDto
                    {
                        Version = MedStore.MedVersion,
                        NavItems = new List<string> { "全局搜索", "章节精读", "知识图谱", "收藏分类", "模板", "数据源" },
                        CardTypes = new List<string> { "disease", "symptom", "drug", "lab", "procedure", "guideline", "document", "template", "leaflet" },
                        SourceKinds = new List<string> { "epub", "markdown", "clinical-json", "drug-leaflet" },
                        QuickActions = new List<string> { "添加书籍", "导入临床数据", "药品说明书", "保存检索模板", "新建注释", "重建索引" },
                    })); return;
                case "books":
                    if (req.HttpMethod == "DELETE" && id.Length > 0) { MedStore.DeleteBook(id); Ok(ctx); return; }
                    SendJsonString(ctx, 200, MedJson.S(MedStore.Books())); return;
                case "search":
                    {
                        var dsl = new MedSearchDsl();
                        if (req.HttpMethod == "POST")
                        {
                            using var body = ReadJson(req);
                            if (body != null)
                            {
                                var r = body.RootElement;
                                dsl.Q = GetString(r, "q") ?? GetString(r, "Q") ?? "";
                                dsl.Mode = GetString(r, "mode") ?? "phrase";
                                dsl.Sort = GetString(r, "sort") ?? "relevance";
                                dsl.Limit = (int)Math.Clamp(GetLong(r, "limit", 50), 1, 300);
                                dsl.Offset = (int)Math.Max(0, GetLong(r, "offset", 0));
                                dsl.Scope = StrArray(r, "scope");
                                dsl.Types = StrArray(r, "types");
                                dsl.Books = StrArray(r, "books");
                                dsl.Tags = StrArray(r, "tags");
                            }
                        }
                        else
                        {
                            dsl.Q = QueryValue(query, "q");
                            dsl.Mode = FirstNonEmpty(QueryValue(query, "mode"), "phrase");
                            dsl.Sort = FirstNonEmpty(QueryValue(query, "sort"), "relevance");
                            dsl.Limit = Math.Clamp(QueryInt(query, "limit", 50), 1, 300);
                            dsl.Offset = Math.Max(0, QueryInt(query, "offset", 0));
                            var b = QueryValue(query, "book");
                            if (b.Length > 0) dsl.Books.Add(b);
                            var t = QueryValue(query, "tag");
                            if (t.Length > 0) dsl.Tags.Add(t);
                        }
                        SendJsonString(ctx, 200, MedJson.S(MedSearch.Search(dsl))); return;
                    }
                case "suggest":
                    SendJsonString(ctx, 200, MedJson.S(MedSearch.Suggest(QueryValue(query, "q"), Math.Clamp(QueryInt(query, "limit", 12), 1, 40)))); return;
                case "chapter":
                    {
                        var cid = FirstNonEmpty(QueryValue(query, "id"), id);
                        var view = MedStore.ChapterView(cid);
                        if (view == null) { SendJsonString(ctx, 404, MedJson.S(new MedOk { Ok = false, Error = "章节不存在" })); return; }
                        SendJsonString(ctx, 200, MedJson.S(view)); return;
                    }
                case "asset":
                    {
                        var book = MedStore.Book(QueryValue(query, "book"));
                        var path = QueryValue(query, "path");
                        if (book == null || path.Length == 0 || path.Contains(".."))
                        { SendText(ctx, 404, "asset not found"); return; }
                        byte[]? bytes;
                        if (book.Type == "jsonbook")
                        {
                            var local = MedJsonBook.ResolveImage(book.Id, Uri.UnescapeDataString(path));
                            bytes = local != null && File.Exists(local) ? File.ReadAllBytes(local) : null;
                        }
                        else bytes = File.Exists(book.Path) ? MedEpub.ReadAsset(book.Path, Uri.UnescapeDataString(path)) : null;
                        if (bytes == null) { SendText(ctx, 404, "asset not found"); return; }
                        var ext = Path.GetExtension(path).ToLowerInvariant();
                        var mime = ext switch
                        {
                            ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp",
                            ".svg" => "image/svg+xml", _ => "image/jpeg",
                        };
                        WriteBody(ctx, 200, bytes, mime, false, null, "public, max-age=86400"); return;
                    }
                case "concept":
                    {
                        var cid = FirstNonEmpty(QueryValue(query, "id"), id);
                        var dto = MedConcepts.Get(cid);
                        if (dto == null) { SendJsonString(ctx, 404, MedJson.S(new MedOk { Ok = false, Error = "概念不存在" })); return; }
                        SendJsonString(ctx, 200, MedJson.S(dto)); return;
                    }
                case "terms":
                    SendJsonString(ctx, 200, MedJson.S(MedConcepts.Terms())); return;
                case "concepts":
                    {
                        if (req.HttpMethod == "POST")
                        {
                            using var body = ReadJson(req);
                            if (body == null) { SendText(ctx, 400, "body required"); return; }
                            var r = body.RootElement;
                            var saved = MedConcepts.SaveConcept(new MedConceptDto
                            {
                                Id = GetString(r, "id") ?? "",
                                Type = GetString(r, "type") ?? "other",
                                Name = GetString(r, "name") ?? "",
                                Pinyin = GetString(r, "pinyin") ?? "",
                                Category = GetString(r, "category") ?? "",
                                Keywords = GetString(r, "keywords") ?? "",
                                Summary = GetString(r, "summary") ?? "",
                                DataJson = GetString(r, "dataJson") ?? "{}",
                            }, GetString(r, "bookId") ?? "", GetString(r, "chapterId") ?? "",
                               GetString(r, "chunkId") ?? "", (int)GetLong(r, "pStart", 0));
                            SendJsonString(ctx, 200, MedJson.S(new MedOk { Ok = true, Id = saved })); return;
                        }
                        if (req.HttpMethod == "DELETE" && id.Length > 0)
                        {
                            MedConcepts.DeleteConcept(id);
                            Ok(ctx); return;
                        }
                        SendJsonString(ctx, 200, MedJson.S(MedConcepts.SearchLite(QueryValue(query, "q"), Math.Clamp(QueryInt(query, "limit", 30), 1, 100)))); return;
                    }
                case "anchors":
                    {
                        if (req.HttpMethod == "POST")
                        {
                            using var body = ReadJson(req);
                            if (body == null) { SendText(ctx, 400, "body required"); return; }
                            var r = body.RootElement;
                            var aid = MedStore.SaveAnchor(new MedAnchorDto
                            {
                                Id = GetString(r, "id") ?? "",
                                BookId = GetString(r, "bookId") ?? "",
                                ChapterId = GetString(r, "chapterId") ?? "",
                                ChunkId = GetString(r, "chunkId") ?? "",
                                PStart = (int)GetLong(r, "pStart", 0),
                                Label = GetString(r, "label") ?? "锚点",
                                Note = GetString(r, "note") ?? "",
                            });
                            SendJsonString(ctx, 200, MedJson.S(new MedOk { Ok = true, Id = aid })); return;
                        }
                        if (req.HttpMethod == "DELETE" && id.Length > 0) { MedStore.DeleteAnchor(id); Ok(ctx); return; }
                        SendJsonString(ctx, 200, MedJson.S(MedStore.Anchors(QueryValue(query, "chapter"), QueryValue(query, "book")))); return;
                    }
                case "graph":
                    SendJsonString(ctx, 200, MedJson.S(MedGraph.Build(
                        QueryValue(query, "center"), Math.Clamp(QueryInt(query, "depth", 1), 1, 2),
                        Math.Clamp(QueryInt(query, "limit", 180), 10, 400)))); return;
                case "notes":
                    if (req.HttpMethod == "POST")
                    {
                        using var body = ReadJson(req);
                        if (body == null) { SendText(ctx, 400, "body required"); return; }
                        var r = body.RootElement;
                        var nid = MedStore.SaveNote(new MedNoteDto
                        {
                            Id = GetString(r, "id") ?? "", Title = GetString(r, "title") ?? "",
                            Body = GetString(r, "body") ?? "", TargetType = GetString(r, "targetType") ?? "",
                            TargetId = GetString(r, "targetId") ?? "", BookId = GetString(r, "bookId") ?? "",
                            ChapterId = GetString(r, "chapterId") ?? "", ChunkId = GetString(r, "chunkId") ?? "",
                            Quote = GetString(r, "quote") ?? "", Color = GetString(r, "color") ?? "",
                            CreatedAt = GetLong(r, "createdAt", 0),
                        });
                        SendJsonString(ctx, 200, MedJson.S(new MedOk { Ok = true, Id = nid })); return;
                    }
                    if (req.HttpMethod == "DELETE" && id.Length > 0) { MedStore.DeleteNote(id); Ok(ctx); return; }
                    SendJsonString(ctx, 200, MedJson.S(MedStore.Notes(QueryValue(query, "q"), Math.Clamp(QueryInt(query, "limit", 200), 1, 1000)))); return;
                case "tags":
                    if (req.HttpMethod == "POST")
                    {
                        using var body = ReadJson(req);
                        if (body == null) { SendText(ctx, 400, "body required"); return; }
                        var r = body.RootElement;
                        var tid = MedStore.SaveTag(new MedTagDto
                        {
                            Id = GetString(r, "id") ?? "", Name = GetString(r, "name") ?? "新分类",
                            ParentId = GetString(r, "parentId") ?? "", Color = GetString(r, "color") ?? "#5B8FF9",
                            Ord = (int)GetLong(r, "ord", 0), Kind = GetString(r, "kind") ?? "category",
                        });
                        SendJsonString(ctx, 200, MedJson.S(new MedOk { Ok = true, Id = tid })); return;
                    }
                    if (req.HttpMethod == "DELETE" && id.Length > 0) { MedStore.DeleteTag(id); Ok(ctx); return; }
                    MedStore.EnsureConceptTags();
                    SendJsonString(ctx, 200, MedJson.S(MedStore.Tags(QueryBool(query, "tree")))); return;
                case "tagged":
                    {
                        var tag = FirstNonEmpty(QueryValue(query, "tag"), MedStore.FavoriteTagId);
                        if (req.HttpMethod == "POST")
                        {
                            using var body = ReadJson(req);
                            if (body == null) { SendText(ctx, 400, "body required"); return; }
                            var r = body.RootElement;
                            MedStore.Tag(GetString(r, "tagId") ?? tag, GetString(r, "targetType") ?? "chunk", GetString(r, "targetId") ?? "");
                            Ok(ctx); return;
                        }
                        if (req.HttpMethod == "DELETE")
                        {
                            MedStore.Untag(tag, QueryValue(query, "targetType"), QueryValue(query, "targetId"));
                            Ok(ctx); return;
                        }
                        var tagLimit = Math.Clamp(QueryInt(query, "limit", 200), 1, 5000);
                        if (QueryBool(query, "detail"))
                            SendJsonString(ctx, 200, MedJson.S(MedStore.TaggedItems(tag, tagLimit)));
                        else
                            SendJsonString(ctx, 200, MedJson.S(MedStore.Tagged(tag, tagLimit)));
                        return;
                    }
                case "templates":
                    if (req.HttpMethod == "POST")
                    {
                        using var body = ReadJson(req);
                        if (body == null) { SendText(ctx, 400, "body required"); return; }
                        var r = body.RootElement;
                        var tid = MedStore.SaveTemplate(new MedTemplateDto
                        {
                            Id = GetString(r, "id") ?? "", Kind = GetString(r, "kind") ?? "note",
                            Name = GetString(r, "name") ?? "新模板", SchemaJson = GetString(r, "schemaJson") ?? "{}",
                            ContentJson = GetString(r, "contentJson") ?? "{}", Pinned = GetLong(r, "pinned", 0) != 0 ? 1 : 0,
                            Builtin = GetLong(r, "builtin", 0) != 0 ? 1 : 0, CreatedAt = GetLong(r, "createdAt", 0),
                        });
                        SendJsonString(ctx, 200, MedJson.S(new MedOk { Ok = true, Id = tid })); return;
                    }
                    if (req.HttpMethod == "DELETE" && id.Length > 0) { MedStore.DeleteTemplate(id); Ok(ctx); return; }
                    SendJsonString(ctx, 200, MedJson.S(MedStore.Templates(
                        QueryValue(query, "kind"), Math.Clamp(QueryInt(query, "limit", 800), 1, 5000),
                        QueryInt(query, "builtin", -1), QueryValue(query, "q"))));
                    return;
                case "queries":
                    if (req.HttpMethod == "POST")
                    {
                        using var body = ReadJson(req);
                        if (body == null) { SendText(ctx, 400, "body required"); return; }
                        var r = body.RootElement;
                        var qid = MedStore.SaveQuery(new MedQueryDto
                        {
                            Id = GetString(r, "id") ?? "", Name = GetString(r, "name") ?? "检索式",
                            DslJson = GetString(r, "dslJson") ?? "{}", Pinned = GetLong(r, "pinned", 0) != 0 ? 1 : 0,
                            LastUsedAt = GetLong(r, "lastUsedAt", 0), CreatedAt = GetLong(r, "createdAt", 0),
                        });
                        SendJsonString(ctx, 200, MedJson.S(new MedOk { Ok = true, Id = qid })); return;
                    }
                    if (req.HttpMethod == "DELETE" && id.Length > 0) { MedStore.DeleteQuery(id); Ok(ctx); return; }
                    SendJsonString(ctx, 200, MedJson.S(MedStore.Queries())); return;
                case "export":
                    SendJsonString(ctx, 200, MedJson.S(MedStore.Export())); return;
                case "reindex":
                    SendJsonString(ctx, 200, MedJson.S(MedImport.Reindex())); return;
                case "jobs":
                    {
                        var jid = FirstNonEmpty(QueryValue(query, "id"), id);
                        var job = QueryBool(query, "wait") ? MedImport.Wait(jid) : MedImport.Job(jid);
                        if (job == null) { SendJsonString(ctx, 404, MedJson.S(new MedOk { Ok = false, Error = "任务不存在" })); return; }
                        SendJsonString(ctx, 200, MedJson.S(job)); return;
                    }
                case "import":
                    HandleMedImport(ctx, id, query); return;
                default:
                    SendJsonString(ctx, 404, MedJson.S(new MedOk { Ok = false, Error = "unknown med api" })); return;
            }
        }
        catch (Exception ex)
        {
            Log.Error("[med] API 失败 " + action, ex);
            SendJsonString(ctx, 500, MedJson.S(new MedOk { Ok = false, Error = ex.ToString() }));
        }
    }

    private void HandleMedImport(HttpListenerContext ctx, string kind, string query)
    {
        using var body = ReadJson(ctx.Request);
        var root = body == null ? default : body.RootElement;
        string S(string k) => root.ValueKind == JsonValueKind.Object ? GetString(root, k) ?? "" : "";
        var path = S("path");
        if (path.Length == 0) path = QueryValue(query, "path");
        switch (kind)
        {
            case "book":
            case "markdown":
                {
                    if (path.Length == 0 || !File.Exists(path))
                    { SendJsonString(ctx, 400, MedJson.S(new MedImportReportDto { Ok = false, Error = "文件不存在: " + path })); return; }
                    var volume = S("volume");
                    if (volume.Length == 0) volume = GuessVolume(Path.GetFileNameWithoutExtension(path));
                    var report = MedImport.StartBook(path, volume, kind == "markdown" ? "markdown" : "epub");
                    if (QueryBool(query, "wait") || S("wait") is "1" or "true")
                    {
                        var job = MedImport.Wait(report.JobId);
                        if (job?.Report != null) report = job.Report;
                    }
                    SendJsonString(ctx, 200, MedJson.S(report)); return;
                }
            case "jsonbook":
                {
                    if (path.Length == 0 || (!File.Exists(path) && !Directory.Exists(path)))
                    { SendJsonString(ctx, 400, MedJson.S(new MedImportReportDto { Ok = false, Error = "JSON 文件或目录不存在: " + path })); return; }
                    var report = MedImport.StartJsonBookBatch(path, S("volume"));
                    if ((QueryBool(query, "wait") || S("wait") is "1" or "true") && report.JobId.Length > 0)
                    {
                        var job = MedImport.Wait(report.JobId);
                        if (job?.Report != null) report = job.Report;
                    }
                    SendJsonString(ctx, 200, MedJson.S(report)); return;
                }
            case "clinical":
                {
                    var src = path.Length > 0 ? path : MedConcepts.DefaultClinicalDir;
                    var (count, version, msgs) = MedConcepts.ImportClinical(src, S("snapshot") != "0");
                    var report = new MedImportReportDto
                    {
                        Ok = true, Kind = "clinical", Concepts = count, Books = 0,
                        Messages = msgs, ElapsedMs = 0, DbBytes = MedDatabase.Instance.DbBytes,
                    };
                    report.Messages.Add("版本 " + version);
                    SendJsonString(ctx, 200, MedJson.S(report)); return;
                }
            case "leaflet":
                {
                    var n = body == null ? 0 : MedImport.ImportLeaflets(root);
                    SendJsonString(ctx, 200, MedJson.S(new MedImportReportDto { Ok = true, Kind = "leaflet", Concepts = n })); return;
                }
            default:
                SendJsonString(ctx, 404, MedJson.S(new MedOk { Ok = false, Error = "unknown import kind" })); return;
        }
    }

    private static string GuessVolume(string name)
    {
        if (name.Contains('上') || name.EndsWith("上卷", StringComparison.Ordinal)) return "上卷";
        if (name.Contains('中') || name.EndsWith("中卷", StringComparison.Ordinal)) return "中卷";
        if (name.Contains('下') || name.EndsWith("下卷", StringComparison.Ordinal)) return "下卷";
        return "";
    }

    private static long GetLong(JsonElement e, string prop, long fallback)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean() ? 1 : 0;
        return fallback;
    }

    private static List<string> StrArray(JsonElement e, string prop)
    {
        var list = new List<string>();
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray()) if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } s) list.Add(s);
        return list;
    }

    private static void Ok(HttpListenerContext ctx) => SendJsonString(ctx, 200, MedJson.S(new MedOk { Ok = true }));
}
