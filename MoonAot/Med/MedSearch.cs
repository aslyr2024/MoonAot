using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using static MoonAot.Med.MedDatabase;

namespace MoonAot.Med;

/// <summary>
/// 三层检索：L0 概念热层（拼音/首字母）→ L1 FTS5 trigram（≥3 字）→ L2 内存 chunk 快照（1-2 字短词）。
/// 实测 trigram 查不到「心梗」这类 2 字词，L2 是必需层，不是优化项。
/// </summary>
public static class MedSearch
{
    private sealed class Snap
    {
        public string ChunkId = "";
        public string BookId = "";
        public string ChapterId = "";
        public string Text = "";
        public string Norm = "";
        public int PStart;
        public int PEnd;
        public int Ord;
        public string BookTitle = "";
        public string Volume = "";
        public string ChapterTitle = "";
        public string Href = "";
    }

    private static readonly object Gate = new();
    private static List<Snap> _snap = new();
    private static bool _loaded;

    private sealed class TagFilterResult
    {
        public bool Active;
        public bool FilterHits;
        public readonly HashSet<string> ConceptIds = new(StringComparer.Ordinal);
        public readonly HashSet<string> TargetIds = new(StringComparer.Ordinal);
    }

    /// <summary>分类标签只约束概念卡；收藏/自定义标签同时约束原文命中。</summary>
    private static TagFilterResult BuildTagFilter(List<string> tags)
    {
        var f = new TagFilterResult();
        if (tags.Count == 0) return f;
        f.Active = true;
        try
        {
            using var cn = MedDatabase.Instance.Open();
            var pars = string.Join(",", tags.Select((_, i) => "$t" + i));
            using var cmd = Cmd(cn, "SELECT t.kind,g.target_type,g.target_id FROM med_tagged g JOIN med_tags t ON t.id=g.tag_id WHERE g.tag_id IN (" + pars + ");");
            for (int i = 0; i < tags.Count; i++) P(cmd, "$t" + i, tags[i]);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var kind = r.GetString(0);
                var type = r.GetString(1);
                var id = r.GetString(2);
                f.TargetIds.Add(id);
                if (type == "concept") f.ConceptIds.Add(id);
                if (kind != "category") f.FilterHits = true;
            }
        }
        catch { }
        return f;
    }

    public static void Invalidate() { lock (Gate) { _loaded = false; } }

    private static List<Snap> Snapshot()
    {
        lock (Gate)
        {
            if (_loaded) return _snap;
            var list = new List<Snap>();
            using var cn = MedDatabase.Instance.Open();
            using var cmd = Cmd(cn, """
SELECT k.id,k.book_id,k.chapter_id,k.text,k.text_norm,k.p_start,k.p_end,k.ord,b.title,b.volume,c.title,c.href
FROM med_chunks k JOIN med_books b ON b.id=k.book_id JOIN med_chapters c ON c.id=k.chapter_id
ORDER BY b.added_at,c.ord,k.ord;
""");
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new Snap
            {
                ChunkId = r.GetString(0), BookId = r.GetString(1), ChapterId = r.GetString(2),
                Text = r.GetString(3), Norm = r.GetString(4), PStart = r.GetInt32(5), PEnd = r.GetInt32(6),
                Ord = r.GetInt32(7), BookTitle = r.GetString(8), Volume = r.GetString(9),
                ChapterTitle = r.GetString(10), Href = r.GetString(11),
            });
            _snap = list;
            _loaded = true;
            return list;
        }
    }

    public static MedSearchResultDto Search(MedSearchDsl dsl)
    {
        var sw = Stopwatch.StartNew();
        var q = (dsl.Q ?? "").Trim();
        var result = new MedSearchResultDto { Q = q, Mode = dsl.Mode };
        var tagFilter = BuildTagFilter(dsl.Tags);
        var scope = dsl.Scope.Count > 0 ? new HashSet<string>(dsl.Scope, StringComparer.OrdinalIgnoreCase) : null;
        var books = dsl.Books.Count > 0 ? new HashSet<string>(dsl.Books, StringComparer.Ordinal) : null;
        int limit = Math.Clamp(dsl.Limit <= 0 ? 50 : dsl.Limit, 1, 300);

        // 空关键词：直接 SQL 分页浏览全部条目，避免把 3 万多条概念全部加载进内存再排序
        if (q.Length == 0)
        {
            using var cn = MedDatabase.Instance.Open();
            var where = new List<string> { "COALESCE(deleted,0)=0" };
            if (dsl.Types.Count > 0)
                where.Add("type IN (" + string.Join(",", dsl.Types.Select((_, i) => "$ty" + i)) + ")");
            if (dsl.Tags.Count > 0)
            {
                where.Add("id IN (SELECT target_id FROM med_tagged WHERE target_type='concept' AND tag_id IN (" +
                          string.Join(",", dsl.Tags.Select((_, i) => "$tg" + i)) + "))");
            }
            var whereSql = " WHERE " + string.Join(" AND ", where);
            using (var cnt = Cmd(cn, "SELECT COUNT(*) FROM med_concepts" + whereSql + ";"))
            {
                for (int i = 0; i < dsl.Types.Count; i++) P(cnt, "$ty" + i, dsl.Types[i]);
                for (int i = 0; i < dsl.Tags.Count; i++) P(cnt, "$tg" + i, dsl.Tags[i]);
                result.ConceptTotal = Convert.ToInt32(cnt.ExecuteScalar() ?? 0);
            }
            using (var cmd = Cmd(cn, "SELECT id,type,name,name_norm,pinyin,initials,category,summary,source,edited FROM med_concepts" +
                whereSql + " ORDER BY category,name LIMIT $l OFFSET $o;"))
            {
                for (int i = 0; i < dsl.Types.Count; i++) P(cmd, "$ty" + i, dsl.Types[i]);
                for (int i = 0; i < dsl.Tags.Count; i++) P(cmd, "$tg" + i, dsl.Tags[i]);
                P(cmd, "$l", limit); P(cmd, "$o", Math.Max(0, dsl.Offset));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    result.Concepts.Add(MedConcepts.ToDto(new MedConcepts.ConceptLite
                    {
                        Id = r.GetString(0), Type = r.GetString(1), Name = r.GetString(2), NameNorm = r.GetString(3),
                        Pinyin = r.GetString(4), Initials = r.GetString(5), Category = r.GetString(6),
                        Summary = r.GetString(7), Source = r.GetString(8), Edited = r.GetInt32(9), DataJson = "{}",
                    }));
            }
            result.Total = result.ConceptTotal;
            result.Mode = "browse";
            result.TookMs = sw.ElapsedMilliseconds;
            return result;
        }
        var terms = MedText.SplitTerms(q);
        if (terms.Count == 0) return result;

        if (scope == null || scope.Contains("concept"))
        {
            var (items, total) = MedConcepts.SearchPage(q, limit, Math.Max(0, dsl.Offset),
                dsl.Types.Count > 0 ? dsl.Types : null,
                tagFilter.Active ? tagFilter.ConceptIds : null);
            result.Concepts = items;
            result.ConceptTotal = total;
        }

        // 同义词/缩写扩展：「心梗」→「急性心肌梗死」，简称也能召回正式术语的原文段落
        var groups = terms.Select(ExpandTerm).ToList();
        var allTerms = groups.SelectMany(g => g).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        var qForSnippet = string.Join(" ", allTerms);
        var found = new Dictionary<string, MedHitDto>(StringComparer.Ordinal);
        string mode = "l2";
        var ftsGroups = groups
            .Select(g => g.Where(t => t.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => "\"" + t.Replace("\"", "\"\"") + "\"" + (dsl.Mode == "prefix" ? " *" : "")).ToList())
            .Where(g => g.Count > 0).ToList();
        if (ftsGroups.Count > 0 && (scope == null || scope.Contains("chunk")))
        {
            var expr = string.Join(" AND ", ftsGroups.Select(g => "(" + string.Join(" OR ", g) + ")"));
            try
            {
                using var cn = MedDatabase.Instance.Open();
                var bookIds = books?.ToArray() ?? Array.Empty<string>();
                var bookClause = bookIds.Length > 0
                    ? " AND book_id IN (" + string.Join(",", bookIds.Select((_, i) => "$b" + i)) + ")"
                    : "";
                using var cmd = Cmd(cn,
                    "SELECT chunk_id, bm25(med_fts), snippet(med_fts,0,'<mark>','</mark>','…',22) " +
                    "FROM med_fts WHERE med_fts MATCH $m" + bookClause +
                    " ORDER BY bm25(med_fts), chunk_id LIMIT 600;");
                P(cmd, "$m", expr);
                for (int i = 0; i < bookIds.Length; i++) P(cmd, "$b" + i, bookIds[i]);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var id = r.GetString(0);
                    if (found.ContainsKey(id)) continue;
                    found[id] = new MedHitDto
                    {
                        ChunkId = id, Score = r.IsDBNull(1) ? 0 : -r.GetDouble(1),
                        Snippet = r.IsDBNull(2) ? "" : r.GetString(2), Source = "fts",
                    };
                }
                mode = "fts";
            }
            catch (Exception ex) { result.Mode = "fts-error:" + ex.Message; }
        }
        if (scope == null || scope.Contains("chunk"))
        {
            var snap = Snapshot();
            var lowerGroups = groups.Select(g => g.Select(t => t.ToLowerInvariant()).Distinct().ToList()).ToList();
            foreach (var s in snap)
            {
                // 不按 limit*6 提前截断：否则「全部书籍」的 hitTotal 会小于「指定书籍」，总数不可比。
                if (books != null && !books.Contains(s.BookId)) continue;
                bool all = lowerGroups.All(g => g.Any(t => s.Norm.Contains(t, StringComparison.Ordinal)));
                if (!all) continue;
                if (found.TryGetValue(s.ChunkId, out var existing))
                {
                    FillMeta(existing, s);
                    if (terms.All(t => s.Norm.Contains(t.ToLowerInvariant(), StringComparison.Ordinal))) existing.Score += 2;
                    continue;
                }
                var hit = new MedHitDto
                {
                    ChunkId = s.ChunkId, Score = terms.All(t => s.Norm.Contains(t.ToLowerInvariant(), StringComparison.Ordinal)) ? 3 : 1,
                    Source = "short",
                    Snippet = Highlight(MedText.Snippet(s.Text, qForSnippet, 180), qForSnippet),
                };
                FillMeta(hit, s);
                found[s.ChunkId] = hit;
                // 同上：全量收集命中，由分页决定返回多少条
            }
            mode = mode == "fts" ? "fts+short" : "short";
        }

        // 补齐 FTS 命中的展示元数据
        if (found.Count > 0)
        {
            var byId = Snapshot().ToDictionary(x => x.ChunkId, x => x, StringComparer.Ordinal);
            foreach (var kv in found)
                if (kv.Value.BookId.Length == 0 && byId.TryGetValue(kv.Key, out var s)) FillMeta(kv.Value, s);
        }

        var hits = found.Values.Where(h => h.BookId.Length > 0)
            .Where(h => books == null || books.Contains(h.BookId)).ToList();
        if (tagFilter.Active && tagFilter.FilterHits)
            hits = hits.Where(h => tagFilter.TargetIds.Contains(h.ChunkId) ||
                                   tagFilter.TargetIds.Contains(h.ChapterId) ||
                                   tagFilter.TargetIds.Contains(h.BookId)).ToList();
        hits = dsl.Sort == "book_order"
            ? hits.OrderBy(h => h.BookTitle).ThenBy(h => h.Ord).ThenBy(h => h.ChunkId).ToList()
            : hits.OrderByDescending(h => h.Score).ThenBy(h => h.BookTitle).ThenBy(h => h.ChunkId).ToList();
        result.HitTotal = hits.Count;
        result.Hits = hits.Skip(dsl.Offset).Take(limit).ToList();
        FillConcepts(result.Hits);

        if (scope == null || scope.Contains("note")) result.Notes = MedStore.Notes(q, 20);
        if (scope == null || scope.Contains("template"))
            result.Templates = MedStore.Templates("", 800).Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();

        result.Total = result.ConceptTotal + result.HitTotal + result.Notes.Count + result.Templates.Count;
        result.Mode = result.Mode.StartsWith("fts-error", StringComparison.Ordinal) ? result.Mode + " / " + mode : mode;
        result.TookMs = sw.ElapsedMilliseconds;
        return result;
    }

    private static void FillMeta(MedHitDto h, Snap s)
    {
        h.BookId = s.BookId; h.ChapterId = s.ChapterId; h.BookTitle = s.BookTitle;
        h.Volume = s.Volume; h.ChapterTitle = s.ChapterTitle; h.ChapterPath = s.BookTitle + " › " + s.ChapterTitle;
        h.PStart = s.PStart; h.PEnd = s.PEnd; h.Ord = s.Ord; h.Href = s.Href;
        if (h.Snippet.Length == 0) h.Snippet = MedText.Snippet(s.Text, "", 160);
    }

    private static void FillConcepts(List<MedHitDto> hits)
    {
        if (hits.Count == 0) return;
        using var cn = MedDatabase.Instance.Open();
        var names = MedConcepts.All().ToDictionary(x => x.Id, x => x.Name, StringComparer.Ordinal);
        foreach (var h in hits)
        {
            using var cmd = Cmd(cn, "SELECT concept_id FROM med_hits WHERE chunk_id=$k ORDER BY tf DESC LIMIT 6;");
            P(cmd, "$k", h.ChunkId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (names.TryGetValue(r.GetString(0), out var n)) h.Concepts.Add(n);
        }
    }

    /// <summary>把一个查询词扩展成「原词 + 同义词/缩写对应的规范概念名」。</summary>
    private static List<string> ExpandTerm(string term)
    {
        var list = new List<string> { term.ToLowerInvariant() };
        var norm = MedText.NormalizeTitle(term);
        if (norm.Length < 2) return list;
        try
        {
            using var cn = MedDatabase.Instance.Open();
            using var cmd = Cmd(cn, """
SELECT c.name FROM med_synonyms s JOIN med_concepts c ON c.id=s.canonical_id
WHERE s.term=$t OR s.term LIKE $p LIMIT 4;
""");
            P(cmd, "$t", norm);
            P(cmd, "$p", norm + "%");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var n = r.GetString(0).ToLowerInvariant();
                if (!list.Contains(n, StringComparer.OrdinalIgnoreCase)) list.Add(n);
            }
        }
        catch { }
        return list;
    }

    private static string Highlight(string text, string q)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = MedText.EscapeHtml(text);
        foreach (var t in MedText.SplitTerms(q).OrderByDescending(t => t.Length).Take(4))
        {
            if (t.Length == 0) continue;
            s = Regex.Replace(s, Regex.Escape(MedText.EscapeHtml(t)), m => "<mark>" + m.Value + "</mark>", RegexOptions.IgnoreCase);
        }
        return s;
    }

    public static MedSuggestDto Suggest(string q, int limit = 12)
    {
        var dto = new MedSuggestDto { Q = q };
        if (string.IsNullOrWhiteSpace(q)) return dto;
        var cs = MedConcepts.SearchLite(q, limit);
        dto.Concepts = cs;
        dto.Items = cs.Select(c => c.Name).ToList();
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT term FROM med_synonyms WHERE term LIKE $q LIMIT $l;");
        P(cmd, "$q", MedText.NormalizeTitle(q) + "%"); P(cmd, "$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) dto.Items.Add(r.GetString(0));
        return dto;
    }
}
