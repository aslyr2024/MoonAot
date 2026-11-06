using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MoonAot.Services;
using static MoonAot.Med.MedDatabase;

namespace MoonAot.Med;

/// <summary>书籍/文档导入与索引构建（后台 job + 可同步等待）。</summary>
public static class MedImport
{
    private static readonly ConcurrentDictionary<string, MedJobDto> Jobs = new(StringComparer.Ordinal);

    public static MedImportReportDto StartBook(string path, string volume, string type = "epub")
    {
        var report = new MedImportReportDto { Kind = type };
        if (!File.Exists(path)) { report.Ok = false; report.Error = "文件不存在: " + path; return report; }
        var job = new MedJobDto
        {
            Id = MedEpub.NewId("j"), Kind = type, Running = true, Total = 100,
            StartedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(), Message = "排队中",
        };
        Jobs[job.Id] = job;
        report.JobId = job.Id;
        _ = Task.Run(() =>
        {
            try
            {
                var r = RunBook(path, volume, type, job);
                r.JobId = job.Id;
                job.Report = r;
                job.Done = job.Total;
                job.Message = r.Ok ? "完成" : (r.Error ?? "失败");
            }
            catch (Exception ex)
            {
                job.Report = new MedImportReportDto { Ok = false, JobId = job.Id, Kind = type, Error = ex.ToString() };
                job.Message = "失败: " + ex.Message;
                Log.Error("[med] 导入失败 " + path, ex);
            }
            finally
            {
                job.Running = false;
                job.FinishedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }
        });
        return report;
    }

    /// <summary>目录批量导入同格式 OCR JSON：一个总 job，逐本完成图片本地化 + 建索引。</summary>
    public static MedImportReportDto StartJsonBookBatch(string path, string volume = "")
    {
        var paths = new List<string>();
        if (Directory.Exists(path))
        {
            paths = Directory.GetFiles(path, "*PaddleOCR*.json", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 0)
                paths = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
        else if (File.Exists(path) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            paths.Add(path);

        var report = new MedImportReportDto { Kind = "jsonbook-batch" };
        if (paths.Count == 0) { report.Ok = false; report.Error = "目录中没有找到 JSON 图书: " + path; return report; }
        var job = new MedJobDto
        {
            Id = MedEpub.NewId("j"), Kind = "jsonbook-batch", Running = true, Total = paths.Count,
            StartedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(), Message = "准备导入 " + paths.Count + " 本",
        };
        Jobs[job.Id] = job;
        report.Ok = true;
        report.JobId = job.Id;
        report.Messages.Add("待导入 " + paths.Count + " 本：" + string.Join(" / ", paths.Take(3).Select(Path.GetFileName)) + (paths.Count > 3 ? " …" : ""));

        _ = Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var agg = new MedImportReportDto { Ok = true, Kind = "jsonbook-batch", JobId = job.Id };
            int i = 0;
            foreach (var p in paths)
            {
                i++;
                job.Message = $"({i}/{paths.Count}) {Path.GetFileName(p)}";
                try
                {
                    var r = RunBook(p, volume.Length > 0 ? volume : GuessVolumeFromName(Path.GetFileName(p)), "jsonbook", job);
                    agg.Books += r.Books; agg.Chapters += r.Chapters; agg.Chunks += r.Chunks;
                    agg.Chars += r.Chars; agg.Images += r.Images; agg.Hits += r.Hits;
                    agg.Links = r.Links; agg.AnchorTotal += r.AnchorTotal; agg.AnchorResolved += r.AnchorResolved;
                    agg.Messages.Add($"{Path.GetFileName(p)}：{r.Chapters} 章 / {r.Chunks} 段 / 图片 {r.Images} 张 / {r.ElapsedMs}ms");
                    agg.Messages.AddRange(r.Messages.Where(m => m.Contains("失败")));
                    if (!r.Ok) { agg.Ok = false; agg.Messages.Add("失败：" + r.Error); }
                }
                catch (Exception ex)
                {
                    agg.Ok = false;
                    agg.Messages.Add(Path.GetFileName(p) + " 导入异常：" + ex.Message);
                    Log.Error("[med] JSON 图书导入失败 " + p, ex);
                }
                job.Done = i;
                job.Total = paths.Count;
            }
            agg.ElapsedMs = sw.ElapsedMilliseconds;
            agg.DbBytes = MedDatabase.Instance.DbBytes;
            agg.Messages.Add($"批量完成：{agg.Books} 本 / {agg.Chapters} 章 / {agg.Chunks} 段 / 图片 {agg.Images} 张 / {agg.ElapsedMs}ms");
            job.Report = agg;
            job.Message = "完成";
            job.Running = false;
            job.FinishedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        });
        return report;
    }

    private static string GuessVolumeFromName(string name)
    {
        if (name.Contains("（上）") || name.Contains("上卷") || name.Contains("上册")) return "上卷";
        if (name.Contains("（中）") || name.Contains("中卷") || name.Contains("中册")) return "中卷";
        if (name.Contains("（下）") || name.Contains("下卷") || name.Contains("下册")) return "下卷";
        return "";
    }

    public static MedJobDto? Job(string id) => Jobs.TryGetValue(id, out var j) ? j : null;

    public static MedJobDto? Wait(string id, int timeoutMs = 300000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var j = Job(id);
            if (j == null || !j.Running) return j;
            Thread.Sleep(120);
        }
        return Job(id);
    }

    private static MedImportReportDto RunBook(string path, string volume, string type, MedJobDto job)
    {
        var sw = Stopwatch.StartNew();
        var db = MedDatabase.Instance;
        db.Initialize();
        var report = new MedImportReportDto { Kind = type, JobId = job.Id };
        var sha = Sha1(path);
        string bookId;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using (var q = Cmd(cn, "SELECT id,status FROM med_books WHERE sha1=$s LIMIT 1;"))
            {
                P(q, "$s", sha);
                using var r = q.ExecuteReader();
                if (r.Read() && r.GetString(1) == "ready")
                {
                    report.Ok = true; report.Books = 0; report.ElapsedMs = sw.ElapsedMilliseconds;
                    report.Messages.Add("已索引，跳过（sha1 相同）");
                    return report;
                }
            }
            bookId = MedEpub.NewId("b");
            // 与主书库联动：若该书尚未在静读书架登记，则登记一次，
            // 让「在静读阅读器中打开原书」可以直接复用现有 ReaderWindow。
            string mainBookId = "";
            try { mainBookId = LibraryService.Instance.ImportFile(path, out _)?.Id ?? ""; }
            catch (Exception ex) { Log.Warn("[med] 主书库登记失败: " + ex.Message); }
            using var ins = Cmd(cn, "INSERT OR REPLACE INTO med_books(id,book_id,title,volume,type,path,sha1,status,added_at) VALUES($i,$mb,$t,$v,$ty,$p,$s,'pending',$now);");
            P(ins, "$i", bookId); P(ins, "$mb", mainBookId); P(ins, "$t", Path.GetFileNameWithoutExtension(path)); P(ins, "$v", volume);
            P(ins, "$ty", type); P(ins, "$p", path); P(ins, "$s", sha);
            P(ins, "$now", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            ins.ExecuteNonQuery();
        }

        MedBookDraft draft;
        if (type == "markdown") draft = ParseMarkdown(path, volume);
        else if (type == "jsonbook") draft = MedJsonBook.Parse(path, bookId, (d, t, m) =>
        {
            job.Done = Math.Min(70, (int)(d * 70.0 / Math.Max(1, t)));
            job.Total = 100;
            job.Message = m;
        });
        else draft = MedEpub.Parse(path, volume, (d, t, m) =>
        {
            job.Done = (int)(d * 70.0 / Math.Max(1, t));
            job.Total = 100;
            job.Message = m;
        });
        report.Messages.AddRange(draft.Messages);
        job.Message = "写入索引（" + draft.Chunks.Count + " 段）";
        job.Done = 72;

        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var tx = cn.BeginTransaction();
            using (var del = Cmd(cn, "DELETE FROM med_fts WHERE chunk_id IN (SELECT id FROM med_chunks WHERE book_id=$b);"))
            { del.Transaction = tx; P(del, "$b", bookId); del.ExecuteNonQuery(); }
            foreach (var sql in new[] { "DELETE FROM med_hits WHERE book_id=$b;", "DELETE FROM med_chunks WHERE book_id=$b;", "DELETE FROM med_chapters WHERE book_id=$b;" })
            {
                using var c = Cmd(cn, sql); c.Transaction = tx; P(c, "$b", bookId); c.ExecuteNonQuery();
            }
            using var ci = Cmd(cn, """
INSERT INTO med_chapters(id,book_id,parent_id,level,spine_index,href,anchor,title,title_norm,page_hint,ord,char_start,char_end,img_count,block_start,block_end)
 VALUES($i,$b,$p,$l,$s,$h,$a,$t,$n,$ph,$o,$cs,$ce,$ic,$bs,$be);
""");
            ci.Transaction = tx;
            foreach (var c in draft.Chapters)
            {
                ci.Parameters.Clear();
                P(ci, "$i", c.Id); P(ci, "$b", bookId); P(ci, "$p", c.ParentId); P(ci, "$l", c.Level);
                P(ci, "$s", c.SpineIndex); P(ci, "$h", c.Href); P(ci, "$a", c.Anchor); P(ci, "$t", c.Title);
                P(ci, "$n", c.TitleNorm); P(ci, "$ph", c.PageHint); P(ci, "$o", c.Ord);
                P(ci, "$cs", c.CharStart); P(ci, "$ce", c.CharEnd); P(ci, "$ic", c.ImgCount);
                P(ci, "$bs", c.BlockStart); P(ci, "$be", c.BlockEnd);
                ci.ExecuteNonQuery();
            }
            using var ki = Cmd(cn, """
INSERT INTO med_chunks(id,book_id,chapter_id,ord,text,text_norm,p_start,p_end,char_start,char_end,img_refs)
 VALUES($i,$b,$c,$o,$t,$n,$ps,$pe,$cs,$ce,$img);
""");
            ki.Transaction = tx;
            using var fi = Cmd(cn, "INSERT INTO med_fts(text,book_id,chapter_id,chunk_id) VALUES($t,$b,$c,$k);");
            fi.Transaction = tx;
            for (int i = 0; i < draft.Chunks.Count; i++)
            {
                var k = draft.Chunks[i];
                ki.Parameters.Clear();
                P(ki, "$i", k.Id); P(ki, "$b", bookId); P(ki, "$c", k.ChapterId); P(ki, "$o", k.Ord);
                P(ki, "$t", k.Text); P(ki, "$n", k.Norm); P(ki, "$ps", k.PStart); P(ki, "$pe", k.PEnd);
                P(ki, "$cs", k.CharStart); P(ki, "$ce", k.CharEnd); P(ki, "$img", k.ImgRefs);
                ki.ExecuteNonQuery();
                fi.Parameters.Clear();
                P(fi, "$t", k.Norm); P(fi, "$b", bookId); P(fi, "$c", k.ChapterId); P(fi, "$k", k.Id);
                fi.ExecuteNonQuery();
                if ((i & 1023) == 0) { job.Done = 72 + (int)(i * 12.0 / Math.Max(1, draft.Chunks.Count)); job.Message = "写入索引 " + i + "/" + draft.Chunks.Count; }
            }
            using var up = Cmd(cn, """
UPDATE med_books SET title=$t,status='ready',indexed_at=$now,doc_count=$dc,chapter_count=$cc,chunk_count=$kc,
 char_count=$ch,anchor_total=$at,anchor_resolved=$ar,image_count=$ic,elapsed_ms=$ms,error=NULL WHERE id=$i;
""");
            up.Transaction = tx;
            P(up, "$t", draft.Title); P(up, "$now", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            P(up, "$dc", draft.DocCount); P(up, "$cc", draft.Chapters.Count); P(up, "$kc", draft.Chunks.Count);
            P(up, "$ch", draft.CharCount); P(up, "$at", draft.AnchorTotal); P(up, "$ar", draft.AnchorResolved);
            P(up, "$ic", draft.ImageCount); P(up, "$ms", sw.ElapsedMilliseconds); P(up, "$i", bookId);
            up.ExecuteNonQuery();
            tx.Commit();
        }
        report.Chapters = draft.Chapters.Count;
        report.Chunks = draft.Chunks.Count;
        report.Chars = draft.CharCount;
        report.Images = draft.ImageCount;
        report.AnchorTotal = draft.AnchorTotal;
        report.AnchorResolved = draft.AnchorResolved;
        report.Books = 1;

        job.Message = "概念对齐与双链";
        job.Done = 86;
        var (hits, _) = MedConcepts.BuildHits(bookId, (d, t, m) => { job.Message = m; });
        report.Hits = hits;
        report.Links = MedConcepts.RebuildAutoLinks();
        job.Done = 98;
        MedConcepts.Invalidate();
        MedSearch.Invalidate();
        report.Ok = true;
        report.ElapsedMs = sw.ElapsedMilliseconds;
        report.DbBytes = db.DbBytes;
        report.Messages.Add($"锚点重建 {report.AnchorResolved}/{report.AnchorTotal} · 章节 {report.Chapters} · 段 {report.Chunks} · 概念命中 {hits}");
        Log.Info($"[med] 导入完成 {Path.GetFileName(path)}: {report.Chapters} 章 / {report.Chunks} 段 / {report.ElapsedMs}ms");
        return report;
    }

    /// <summary>重新对齐概念与双链（导入 clinical 后或新增词典后调用），不重跑 EPUB 解析。</summary>
    public static MedImportReportDto Reindex()
    {
        var sw = Stopwatch.StartNew();
        var report = new MedImportReportDto { Kind = "reindex", Ok = true };
        var ids = new List<string>();
        using (var cn = MedDatabase.Instance.Open())
        {
            using var cmd = Cmd(cn, "SELECT id FROM med_books WHERE status='ready';");
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
        }
        foreach (var id in ids) report.Hits += MedConcepts.BuildHits(id).Hits;
        report.Links = MedConcepts.RebuildAutoLinks();
        MedConcepts.Invalidate();
        MedSearch.Invalidate();
        report.Books = ids.Count;
        report.ElapsedMs = sw.ElapsedMilliseconds;
        report.DbBytes = MedDatabase.Instance.DbBytes;
        report.Messages.Add($"重对齐 {ids.Count} 本 / 命中 {report.Hits} / 自动边 {report.Links}");
        return report;
    }

    private static MedBookDraft ParseMarkdown(string path, string volume)
    {
        var draft = new MedBookDraft { Title = Path.GetFileNameWithoutExtension(path), Volume = volume, DocCount = 1 };
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var heading = new Regex(@"^(#{1,4})\s+(.+)$");
        MedChapterDraft? current = null;
        var buf = new StringBuilder();
        int ord = 0, p = 0;
        void Flush()
        {
            var text = buf.ToString().Trim();
            buf.Clear();
            if (text.Length == 0 || current == null) return;
            draft.Chunks.Add(new MedChunkRow
            {
                Id = MedEpub.NewId("k"), ChapterId = current.Id, Ord = draft.Chunks.Count,
                Text = text, Norm = MedText.NormalizeText(text), PStart = p, PEnd = p,
            });
            draft.CharCount += text.Length;
            p++;
        }
        current = new MedChapterDraft { Id = MedEpub.NewId("c"), Title = "开篇", Level = 1, Ord = ord++, Href = "" };
        draft.Chapters.Add(current);
        foreach (var line in lines)
        {
            var m = heading.Match(line);
            if (m.Success)
            {
                Flush();
                current = new MedChapterDraft
                {
                    Id = MedEpub.NewId("c"), Title = m.Groups[2].Value.Trim(), Level = m.Groups[1].Value.Length,
                    Ord = ord++, Href = "", TitleNorm = MedText.NormalizeTitle(m.Groups[2].Value),
                };
                draft.Chapters.Add(current);
                continue;
            }
            buf.AppendLine(line);
            if (buf.Length >= 800) Flush();
        }
        Flush();
        draft.AnchorTotal = draft.AnchorResolved = draft.Chapters.Count;
        return draft;
    }

    /// <summary>药品说明书速查：手动录入/批量 JSON 字段，注册为 leaflet 概念。</summary>
    public static int ImportLeaflets(JsonElement body)
    {
        var items = new List<JsonElement>();
        if (body.ValueKind == JsonValueKind.Array) foreach (var x in body.EnumerateArray()) items.Add(x);
        else if (body.ValueKind == JsonValueKind.Object)
        {
            if (body.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var x in arr.EnumerateArray()) items.Add(x);
            else items.Add(body);
        }
        if (items.Count == 0) return 0;
        var db = MedDatabase.Instance;
        int n = 0;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var tx = cn.BeginTransaction();
            using var ins = Cmd(cn, """
INSERT OR REPLACE INTO med_concepts(id,type,name,name_norm,pinyin,initials,category,keywords,data_json,summary,source,source_version)
 VALUES($id,'leaflet',$name,$norm,'','','药品说明书','',$data,$sum,'leaflet','user');
""");
            ins.Transaction = tx;
            const string leafletTagName = "药品说明书";
            const string leafletTagId = "tag-" + "药品说明书";
            using (var tag = Cmd(cn, "INSERT OR IGNORE INTO med_tags(id,name,parent_id,color,ord,kind) VALUES($id,$name,'','#5B8FF9',999,'category');"))
            { tag.Transaction = tx; P(tag, "$id", leafletTagId); P(tag, "$name", leafletTagName); tag.ExecuteNonQuery(); }
            using var tagLink = Cmd(cn, "INSERT OR IGNORE INTO med_tagged(tag_id,target_type,target_id,added_at,note) VALUES($t,'concept',$id,$now,'');");
            tagLink.Transaction = tx;
            foreach (var it in items)
            {
                var name = Get(it, "name");
                if (name.Length == 0) continue;
                var data = MedJson.S(new Dictionary<string, string>
                {
                    ["通用名"] = name, ["商品名"] = Get(it, "brand"), ["规格"] = Get(it, "spec"),
                    ["适应症"] = Get(it, "indication"), ["用法用量"] = Get(it, "dosage"),
                    ["禁忌"] = Get(it, "contraindication"), ["不良反应"] = Get(it, "adverse"),
                    ["相互作用"] = Get(it, "interaction"), ["贮藏"] = Get(it, "storage"),
                    ["备注"] = Get(it, "note"),
                });
                var id = "leaf-" + MedEpub.NewId("");
                ins.Parameters.Clear();
                P(ins, "$id", id); P(ins, "$name", name);
                P(ins, "$norm", MedText.NormalizeTitle(name)); P(ins, "$data", data);
                var ind = Get(it, "indication");
                var sum = ind.Length > 0 ? ind : name;
                P(ins, "$sum", sum.Length > 160 ? sum[..160] : sum);
                ins.ExecuteNonQuery();
                tagLink.Parameters.Clear();
                P(tagLink, "$t", leafletTagId); P(tagLink, "$id", id);
                P(tagLink, "$now", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                tagLink.ExecuteNonQuery();
                n++;
            }
            tx.Commit();
        }
        MedConcepts.Invalidate();
        return n;
    }

    private static string Get(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Sha1(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
    }
}
