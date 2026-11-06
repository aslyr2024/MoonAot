using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MoonAot.Services;
using static MoonAot.Med.MedDatabase;

namespace MoonAot.Med;

/// <summary>Clinical JSON 导入、术语词典（Aho-Corasick）、概念命中与自动双链。</summary>
public static class MedConcepts
{
    private static readonly object CacheGate = new();
    private static List<ConceptLite> _cache = new();
    private static long _cacheAt;

    public sealed class ConceptLite
    {
        public string Id = "";
        public string Type = "";
        public string Name = "";
        public string NameNorm = "";
        public string Pinyin = "";
        public string Initials = "";
        public string Category = "";
        public string Summary = "";
        public string DataJson = "{}";
        public string Source = "";
        public int Edited;
    }

    private static readonly Dictionary<string, string> BuiltinSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WBC"] = "白细胞计数", ["RBC"] = "红细胞计数", ["HGB"] = "血红蛋白", ["PLT"] = "血小板计数",
        ["CRP"] = "C反应蛋白", ["PCT"] = "降钙素原", ["AMI"] = "急性心肌梗死", ["CPR"] = "心肺复苏(CPR)",
        ["COPD"] = "慢性阻塞性肺疾病", ["DKA"] = "糖尿病酮症酸中毒", ["ACS"] = "急性冠脉综合征",
        ["心梗"] = "急性心肌梗死", ["中风"] = "脑梗死", ["脑梗"] = "脑梗死", ["高血压病"] = "高血压",
    };

    public static void Invalidate() { lock (CacheGate) { _cacheAt = 0; } }

    public static List<ConceptLite> All()
    {
        lock (CacheGate)
        {
            if (_cacheAt > 0 && Environment.TickCount64 - _cacheAt < 120000) return _cache;
            var list = new List<ConceptLite>();
            using var cn = MedDatabase.Instance.Open();
            // data_json 可能很大（药品说明书约 100MB），热层只缓存检索字段，详情按需读取
            using var cmd = Cmd(cn, "SELECT id,type,name,name_norm,pinyin,initials,category,summary,source,edited FROM med_concepts WHERE COALESCE(deleted,0)=0;");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ConceptLite
                {
                    Id = r.GetString(0), Type = r.GetString(1), Name = r.GetString(2), NameNorm = r.GetString(3),
                    Pinyin = r.GetString(4), Initials = r.GetString(5), Category = r.GetString(6),
                    Summary = r.GetString(7), DataJson = "{}", Source = r.GetString(8), Edited = r.GetInt32(9),
                });
            _cache = list;
            _cacheAt = Environment.TickCount64;
            return list;
        }
    }

    /// <summary>精读页用：全部概念名 + 缩写别名，供前端做全量最长匹配高亮与词条跳转。</summary>
    public static List<MedTermDto> Terms()
    {
        var list = new List<MedTermDto>();
        foreach (var c in All())
            list.Add(new MedTermDto { Id = c.Id, Name = c.Name, Type = c.Type, Label = c.Name });
        using (var cn = MedDatabase.Instance.Open())
        using (var cmd = Cmd(cn, "SELECT term,canonical_id FROM med_synonyms;"))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var term = r.GetString(0);
                if (term.Length < 2) continue;
                list.Add(new MedTermDto { Id = r.GetString(1), Name = term, Type = "alias", Label = term });
            }
        return list;
    }

    /// <summary>导入 clinical JSON（默认快照复制到 data/medical/sources/clinical/&lt;version&gt;）。</summary>
    public static (int Count, string Version, List<string> Messages) ImportClinical(string? sourceDir, bool snapshot, Action<string>? progress = null)
    {
        var msgs = new List<string>();
        var db = MedDatabase.Instance;
        var src = string.IsNullOrWhiteSpace(sourceDir) ? DefaultClinicalDir : sourceDir!;
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException("clinical 源目录不存在: " + src);
        var manifestPath = Path.Combine(src, "manifest.json");
        string version = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (File.Exists(manifestPath))
        {
            using var md = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (md.RootElement.TryGetProperty("version", out var v)) version = v.GetString() ?? version;
        }
        var work = src;
        if (snapshot)
        {
            work = Path.Combine(db.ClinicalDir, version);
            Directory.CreateDirectory(work);
            foreach (var f in Directory.GetFiles(src, "*.json"))
            {
                var dst = Path.Combine(work, Path.GetFileName(f));
                if (!File.Exists(dst) || new FileInfo(f).Length != new FileInfo(dst).Length) File.Copy(f, dst, true);
            }
            msgs.Add("已快照到 " + work);
        }

        int count = 0, templates = 0, tags = 0;
        var aliasPairs = new List<(string Term, string Canonical)>();
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var tx = cn.BeginTransaction();
            var protectedIds = new HashSet<string>(StringComparer.Ordinal);
            using (var cmd = Cmd(cn, "SELECT id FROM med_concepts WHERE COALESCE(edited,0)=1 OR COALESCE(deleted,0)=1;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) protectedIds.Add(r.GetString(0));
            using (var del = Cmd(cn, "DELETE FROM med_concepts WHERE source='clinical' AND COALESCE(edited,0)=0 AND COALESCE(deleted,0)=0;")) { del.Transaction = tx; del.ExecuteNonQuery(); }
            using var ins = Cmd(cn, """
INSERT OR REPLACE INTO med_concepts
 (id,type,name,name_norm,pinyin,initials,category,keywords,data_json,summary,source,source_version)
 VALUES($id,$type,$name,$norm,$py,$ini,$cat,$kw,$data,$sum,'clinical',$ver);
""");
            ins.Transaction = tx;
            using var tIns = Cmd(cn, """
INSERT OR REPLACE INTO med_templates(id,kind,name,schema_json,content_json,pinned,builtin,created_at,updated_at)
 VALUES($id,'doc',$name,'{}',$content,0,1,$now,$now);
""");
            tIns.Transaction = tx;
            using var tagIns = Cmd(cn, "INSERT OR IGNORE INTO med_tags(id,name,parent_id,color,ord,kind) VALUES($id,$name,'','#5B8FF9',$ord,'category');");
            tagIns.Transaction = tx;
            using var tagLink = Cmd(cn, "INSERT OR IGNORE INTO med_tagged(tag_id,target_type,target_id,added_at,note) VALUES($t,'concept',$id,$now,'');");
            tagLink.Transaction = tx;
            var cats = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in Directory.GetFiles(work, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (Path.GetFileName(file).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(File.ReadAllText(file)); }
                catch (Exception ex) { msgs.Add("跳过 " + Path.GetFileName(file) + ": " + ex.Message); continue; }
                using (doc)
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                    foreach (var it in doc.RootElement.EnumerateArray())
                    {
                        var id = Str(it, "id");
                        var name = Str(it, "name");
                        if (id.Length == 0 || name.Length == 0) continue;
                        if (protectedIds.Contains("cli-" + id)) continue;
                        var type = Str(it, "type");
                        var data = it.TryGetProperty("data", out var d) ? d.GetRawText() : "{}";
                        var kw = Str(it, "keywords");
                        var summary = FirstText(data, kw);
                        var cat = Str(it, "category");
                        ins.Parameters.Clear();
                        P(ins, "$id", "cli-" + id); P(ins, "$type", type); P(ins, "$name", name);
                        P(ins, "$norm", MedText.NormalizeTitle(name)); P(ins, "$py", Str(it, "pinyin"));
                        P(ins, "$ini", Str(it, "initials")); P(ins, "$cat", cat); P(ins, "$kw", kw);
                        P(ins, "$data", data); P(ins, "$sum", summary); P(ins, "$ver", version);
                        ins.ExecuteNonQuery();
                        count++;
                        if (type == "lab")
                        {
                            using var dd = JsonDocument.Parse(data);
                            var abbr = Str(dd.RootElement, "abbr");
                            if (abbr.Length > 0) aliasPairs.Add((abbr, name));
                        }
                        if (type is "template" or "document")
                        {
                            using var dd = JsonDocument.Parse(data);
                            var content = Str(dd.RootElement, "content");
                            tIns.Parameters.Clear();
                            P(tIns, "$id", "tpl-cli-" + id); P(tIns, "$name", name);
                            P(tIns, "$content", MedJson.S(new MedDocTemplateContent
                            {
                                Content = content, Category = cat, Disease = Str(dd.RootElement, "disease"),
                                Department = Str(dd.RootElement, "department"),
                            }));
                            P(tIns, "$now", now);
                            tIns.ExecuteNonQuery();
                            templates++;
                        }
                        if (cat.Length > 0)
                        {
                            var tagId = "tag-" + MedText.NormalizeTitle(cat);
                            if (cats.Add(cat))
                            {
                                tagIns.Parameters.Clear();
                                P(tagIns, "$id", tagId); P(tagIns, "$name", cat); P(tagIns, "$ord", cats.Count);
                                tagIns.ExecuteNonQuery();
                                tags++;
                            }
                            // 概念挂到分类标签，分类计数与筛选才有意义
                            tagLink.Parameters.Clear();
                            P(tagLink, "$t", tagId); P(tagLink, "$id", "cli-" + id); P(tagLink, "$now", now);
                            tagLink.ExecuteNonQuery();
                        }
                    }
                }
                progress?.Invoke("导入 " + Path.GetFileName(file));
            }
            tx.Commit();
        }

        // 别名/缩写表（clinical 自带 abbr + 内置常见缩写）
        Invalidate();
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var tx = cn.BeginTransaction();
            using var cmd = Cmd(cn, "INSERT OR REPLACE INTO med_synonyms(term,canonical_id,kind) VALUES($t,$id,'abbr');");
            cmd.Transaction = tx;
            var byName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in All()) byName.TryAdd(c.NameNorm, c.Id);
            foreach (var (term, canonical) in aliasPairs.Concat(BuiltinSynonyms.Select(kv => (kv.Key, kv.Value))))
            {
                if (!byName.TryGetValue(MedText.NormalizeTitle(canonical), out var cid)) continue;
                cmd.Parameters.Clear(); P(cmd, "$t", MedText.NormalizeTitle(term)); P(cmd, "$id", cid);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        db.SetMeta("clinical_version", version);
        db.SetMeta("clinical_imported_at", now.ToString());
        Invalidate();
        msgs.Add($"概念 {count} 条 / 内置模板 {templates} 份 / 分类 {tags} 个 / 版本 {version}");
        return (count, version, msgs);
    }

    public static string DefaultClinicalDir =>
        @"D:\code\用药参考本地版\医生私人ai大脑\医生私人ai大脑\Windows\MedRecordAot\bin\Release\net10.0\win-x64\Assets\clinical";

    private static string Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string FirstText(string dataJson, string kw)
    {
        try
        {
            using var d = JsonDocument.Parse(dataJson);
            foreach (var key in new[] { "summary", "indication", "scope", "abbr", "common_causes", "content" })
            {
                if (d.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString() ?? "";
                    if (s.Length > 0) return s.Length > 160 ? s[..160] : s;
                }
            }
        }
        catch { }
        return kw.Length > 160 ? kw[..160] : kw;
    }

    // ---------------- Aho-Corasick ----------------

    private sealed class AcNode
    {
        public Dictionary<char, int> Next = new();
        public int Fail;
        public List<int> Out = new();
    }

    private sealed class Ac
    {
        public List<AcNode> Nodes = new() { new AcNode() };
        public List<string> Ids = new();
        public List<int> Lengths = new();

        public void Add(string term, string id)
        {
            if (term.Length == 0) return;
            int cur = 0;
            foreach (var ch in term)
            {
                if (!Nodes[cur].Next.TryGetValue(ch, out var nxt)) { nxt = Nodes.Count; Nodes[cur].Next[ch] = nxt; Nodes.Add(new AcNode()); }
                cur = nxt;
            }
            Nodes[cur].Out.Add(Ids.Count);
            Ids.Add(id);
            Lengths.Add(term.Length);
        }

        public void Build()
        {
            var q = new Queue<int>();
            foreach (var kv in Nodes[0].Next) { Nodes[kv.Value].Fail = 0; q.Enqueue(kv.Value); }
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var kv in Nodes[cur].Next)
                {
                    int fail = Nodes[cur].Fail;
                    while (fail != 0 && !Nodes[fail].Next.ContainsKey(kv.Key)) fail = Nodes[fail].Fail;
                    if (Nodes[fail].Next.TryGetValue(kv.Key, out var f) && f != kv.Value) Nodes[kv.Value].Fail = f;
                    q.Enqueue(kv.Value);
                }
                if (Nodes[Nodes[cur].Fail].Out.Count > 0) Nodes[cur].Out.AddRange(Nodes[Nodes[cur].Fail].Out);
            }
        }

        public List<(string Id, int Pos, int Len)> Scan(string text)
        {
            var res = new List<(string, int, int)>();
            int cur = 0;
            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                while (cur != 0 && !Nodes[cur].Next.ContainsKey(ch)) cur = Nodes[cur].Fail;
                if (Nodes[cur].Next.TryGetValue(ch, out var nxt)) cur = nxt; else continue;
                foreach (var oi in Nodes[cur].Out)
                    res.Add((Ids[oi], i - Lengths[oi] + 1, Lengths[oi]));
            }
            return res;
        }
    }

    private static Ac BuildAutomaton()
    {
        var ac = new Ac();
        foreach (var c in All())
        {
            var key = MedText.NormalizeTitle(c.Name);
            if (key.Length >= 2) ac.Add(key, c.Id);
        }
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT term,canonical_id FROM med_synonyms;");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var t = r.GetString(0);
            if (t.Length >= 2) ac.Add(t, r.GetString(1));
        }
        ac.Build();
        return ac;
    }

    /// <summary>概念对齐：扫描全书 chunk，写 med_hits；返回命中数与涉及 chunk 数。</summary>
    public static (int Hits, int Chunks) BuildHits(string bookId, Action<int, int, string>? progress = null)
    {
        var db = MedDatabase.Instance;
        var ac = BuildAutomaton();
        var rows = new List<(string ChunkId, string ChapterId, string Text)>();
        using (var cn = db.Open())
        {
            using var cmd = Cmd(cn, "SELECT id,chapter_id,text_norm FROM med_chunks WHERE book_id=$b;");
            P(cmd, "$b", bookId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        }
        int hits = 0;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var tx = cn.BeginTransaction();
            using (var del = Cmd(cn, "DELETE FROM med_hits WHERE book_id=$b;")) { del.Transaction = tx; P(del, "$b", bookId); del.ExecuteNonQuery(); }
            using var ins = Cmd(cn, "INSERT OR REPLACE INTO med_hits(concept_id,book_id,chapter_id,chunk_id,tf,positions) VALUES($c,$b,$ch,$k,$tf,$pos);");
            ins.Transaction = tx;
            for (int i = 0; i < rows.Count; i++)
            {
                var (chunkId, chapterId, text) = rows[i];
                if (string.IsNullOrEmpty(text)) continue;
                var found = ac.Scan(text);
                found.Sort((a, b) => a.Pos != b.Pos ? a.Pos.CompareTo(b.Pos) : b.Len.CompareTo(a.Len));
                var perConcept = new Dictionary<string, (int Tf, List<int> Pos)>(StringComparer.Ordinal);
                int lastEnd = -1;
                foreach (var f in found)
                {
                    if (f.Pos < lastEnd) continue; // 长词优先，跳过被包含的短词
                    lastEnd = f.Pos + f.Len;
                    if (!perConcept.TryGetValue(f.Id, out var acc)) { acc = (0, new List<int>()); perConcept[f.Id] = acc; }
                    acc.Tf++;
                    if (acc.Pos.Count < 12) acc.Pos.Add(f.Pos);
                    perConcept[f.Id] = acc;
                    if (perConcept.Count >= 24) break;
                }
                foreach (var kv in perConcept)
                {
                    ins.Parameters.Clear();
                    P(ins, "$c", kv.Key); P(ins, "$b", bookId); P(ins, "$ch", chapterId); P(ins, "$k", chunkId);
                    P(ins, "$tf", kv.Value.Tf); P(ins, "$pos", string.Join(",", kv.Value.Pos));
                    ins.ExecuteNonQuery();
                    hits++;
                }
                if ((i & 511) == 0) progress?.Invoke(i, rows.Count, "概念对齐 " + i + "/" + rows.Count);
            }
            tx.Commit();
        }
        return (hits, rows.Count);
    }

    /// <summary>按 chunk 共现重建自动双链（全量，成本可控）。</summary>
    public static int RebuildAutoLinks(int maxEdges = 40000)
    {
        var db = MedDatabase.Instance;
        var byChunk = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var cn = db.Open())
        {
            using var cmd = Cmd(cn, "SELECT chunk_id,concept_id FROM med_hits;");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var k = r.GetString(0);
                if (!byChunk.TryGetValue(k, out var l)) { l = new List<string>(); byChunk[k] = l; }
                if (l.Count < 24) l.Add(r.GetString(1));
            }
        }
        var pairs = new Dictionary<(string, string), int>();
        foreach (var l in byChunk.Values)
        {
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    var a = string.CompareOrdinal(l[i], l[j]) < 0 ? l[i] : l[j];
                    var b = string.CompareOrdinal(l[i], l[j]) < 0 ? l[j] : l[i];
                    var key = (a, b);
                    pairs.TryGetValue(key, out var n);
                    pairs[key] = n + 1;
                }
        }
        var top = pairs.OrderByDescending(kv => kv.Value).Take(maxEdges).ToList();
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var tx = cn.BeginTransaction();
            using (var del = Cmd(cn, "DELETE FROM med_links WHERE kind='auto_cooccur';")) { del.Transaction = tx; del.ExecuteNonQuery(); }
            using var ins = Cmd(cn, "INSERT INTO med_links(id,src_type,src_id,dst_type,dst_id,kind,weight,note,created_at) VALUES($id,'concept',$a,'concept',$b,'auto_cooccur',$w,'',$now);");
            ins.Transaction = tx;
            foreach (var kv in top)
            {
                ins.Parameters.Clear();
                P(ins, "$id", MedEpub.NewId("l")); P(ins, "$a", kv.Key.Item1); P(ins, "$b", kv.Key.Item2);
                P(ins, "$w", kv.Value); P(ins, "$now", now);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }
        return top.Count;
    }

    /// <summary>新增或编辑临床条目；编辑 clinical 条目会标记 edited，重新导入 clinical 时保留用户修改。</summary>
    public static string SaveConcept(MedConceptDto c, string bookId = "", string chapterId = "", string chunkId = "", int pStart = 0)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var tx = cn.BeginTransaction();
            if (string.IsNullOrEmpty(c.Id)) c.Id = "user-" + MedEpub.NewId("");
            var name = (c.Name ?? "").Trim();
            if (name.Length == 0) throw new InvalidOperationException("名称不能为空");
            var norm = MedText.NormalizeTitle(name);
            var pinyin = (c.Pinyin ?? "").Trim();
            var initials = DeriveInitials(pinyin);
            var data = string.IsNullOrWhiteSpace(c.DataJson) ? "{}" : c.DataJson;
            try { using var _ = JsonDocument.Parse(data); } catch { data = "{}"; }
            var summary = (c.Summary ?? "").Trim();
            if (summary.Length == 0) summary = FirstText(data, c.Keywords ?? "");
            if (summary.Length > 160) summary = summary[..160];
            var type = string.IsNullOrWhiteSpace(c.Type) ? "other" : c.Type.Trim();
            var category = (c.Category ?? "").Trim();
            using (var exists = Cmd(cn, "SELECT COUNT(*) FROM med_concepts WHERE id=$id;")) { P(exists, "$id", c.Id); }
            var found = false;
            using (var q = Cmd(cn, "SELECT COUNT(*) FROM med_concepts WHERE id=$id;")) { P(q, "$id", c.Id); found = Convert.ToInt64(q.ExecuteScalar() ?? 0) > 0; }
            using (var cmd = found
                ? Cmd(cn, "UPDATE med_concepts SET type=$t,name=$n,name_norm=$nn,pinyin=$p,initials=$i,category=$c,keywords=$k,data_json=$d,summary=$s,edited=1,deleted=0 WHERE id=$id;")
                : Cmd(cn, "INSERT INTO med_concepts(id,type,name,name_norm,pinyin,initials,category,keywords,data_json,summary,source,source_version,edited,deleted) VALUES($id,$t,$n,$nn,$p,$i,$c,$k,$d,$s,'user','user',1,0);"))
            {
                cmd.Transaction = tx;
                P(cmd, "$id", c.Id); P(cmd, "$t", type); P(cmd, "$n", name); P(cmd, "$nn", norm);
                P(cmd, "$p", pinyin); P(cmd, "$i", initials); P(cmd, "$c", category);
                P(cmd, "$k", c.Keywords ?? ""); P(cmd, "$d", data); P(cmd, "$s", summary);
                cmd.ExecuteNonQuery();
            }
            if (category.Length > 0)
            {
                var tagId = "tag-" + MedText.NormalizeTitle(category);
                using (var tag = Cmd(cn, "INSERT OR IGNORE INTO med_tags(id,name,parent_id,color,ord,kind) VALUES($id,$n,'','#5B8FF9',900,'category');"))
                { tag.Transaction = tx; P(tag, "$id", tagId); P(tag, "$n", category); tag.ExecuteNonQuery(); }
                using (var clr = Cmd(cn, "DELETE FROM med_tagged WHERE target_type='concept' AND target_id=$id AND tag_id IN (SELECT id FROM med_tags WHERE kind='category');"))
                { clr.Transaction = tx; P(clr, "$id", c.Id); clr.ExecuteNonQuery(); }
                using (var link = Cmd(cn, "INSERT OR IGNORE INTO med_tagged(tag_id,target_type,target_id,added_at,note) VALUES($t,'concept',$id,$now,'');"))
                { link.Transaction = tx; P(link, "$t", tagId); P(link, "$id", c.Id); P(link, "$now", Now); link.ExecuteNonQuery(); }
            }
            if (chunkId.Length == 0 && chapterId.Length > 0)
            {
                using (var find = Cmd(cn, "SELECT id FROM med_chunks WHERE chapter_id=$ch AND p_start<=$p AND p_end>=$p ORDER BY ord LIMIT 1;"))
                {
                    find.Transaction = tx;
                    P(find, "$ch", chapterId); P(find, "$p", pStart);
                    chunkId = find.ExecuteScalar() as string ?? "";
                }
                if (chunkId.Length == 0)
                {
                    using var near = Cmd(cn, "SELECT id FROM med_chunks WHERE chapter_id=$ch ORDER BY ABS(p_start-$p) LIMIT 1;");
                    near.Transaction = tx;
                    P(near, "$ch", chapterId); P(near, "$p", pStart);
                    chunkId = near.ExecuteScalar() as string ?? "";
                }
            }
            if (chunkId.Length > 0)
            {
                using var hit = Cmd(cn, "INSERT OR REPLACE INTO med_hits(concept_id,book_id,chapter_id,chunk_id,tf,positions) VALUES($c,$b,$ch,$k,1,'');");
                hit.Transaction = tx;
                P(hit, "$c", c.Id); P(hit, "$b", bookId); P(hit, "$ch", chapterId); P(hit, "$k", chunkId);
                hit.ExecuteNonQuery();
            }
            tx.Commit();
        }
        Invalidate();
        return c.Id;
    }

    /// <summary>软删除：保留行并标记 deleted，重新导入 clinical 时不会复活；搜索结果与分类计数立即消失。</summary>
    public static void DeleteConcept(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var tx = cn.BeginTransaction();
            using (var cmd = Cmd(cn, "UPDATE med_concepts SET deleted=1, edited=1 WHERE id=$id;")) { cmd.Transaction = tx; P(cmd, "$id", id); cmd.ExecuteNonQuery(); }
            using (var cmd = Cmd(cn, "DELETE FROM med_hits WHERE concept_id=$id;")) { cmd.Transaction = tx; P(cmd, "$id", id); cmd.ExecuteNonQuery(); }
            using (var cmd = Cmd(cn, "DELETE FROM med_tagged WHERE target_type='concept' AND target_id=$id;")) { cmd.Transaction = tx; P(cmd, "$id", id); cmd.ExecuteNonQuery(); }
            tx.Commit();
        }
        Invalidate();
    }

    private static long Now => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    private static string DeriveInitials(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin)) return "";
        var caps = new string(pinyin.Where(char.IsUpper).ToArray());
        return caps.Length >= 2 ? caps.ToLowerInvariant() : pinyin.ToLowerInvariant();
    }

    public static List<MedConceptDto> SearchLite(string q, int limit = 12) => SearchPage(q, limit, 0).Items;

    /// <summary>概念卡服务端分页：返回当前页与全量匹配总数（不再把结果截断成固定 16/24 条）。</summary>
    public static (List<MedConceptDto> Items, int Total) SearchPage(
        string q, int limit, int offset, List<string>? types = null, HashSet<string>? conceptIds = null)
    {
        var norm = MedText.NormalizeTitle(q);
        var py = (q ?? "").Trim().ToLowerInvariant();
        var scored = new List<(int Score, ConceptLite C)>();
        foreach (var c in All())
        {
            if (types != null && types.Count > 0 && !types.Contains(c.Type, StringComparer.OrdinalIgnoreCase)) continue;
            if (conceptIds != null && !conceptIds.Contains(c.Id)) continue;
            int score = 0;
            if (c.NameNorm == norm) score = 100;
            else if (norm.Length >= 2 && c.NameNorm.StartsWith(norm, StringComparison.Ordinal)) score = 80;
            else if (norm.Length >= 2 && c.NameNorm.Contains(norm, StringComparison.Ordinal)) score = 60;
            else if (py.Length >= 2 && c.Initials.StartsWith(py, StringComparison.OrdinalIgnoreCase)) score = 55;
            else if (py.Length >= 3 && c.Pinyin.StartsWith(py, StringComparison.OrdinalIgnoreCase)) score = 50;
            else if (py.Length >= 2 && c.Pinyin.Contains(py, StringComparison.OrdinalIgnoreCase)) score = 35;
            if (score > 0) scored.Add((score, c));
        }
        var ordered = scored.OrderByDescending(x => x.Score).ThenBy(x => x.C.Name.Length).ThenBy(x => x.C.Id).ToList();
        int total = ordered.Count;
        var items = ordered.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 300))
            .Select(x => ToDto(x.C)).ToList();
        return (items, total);
    }

    public static MedConceptDto ToDto(ConceptLite c) => new()
    {
        Id = c.Id, Type = c.Type, Name = c.Name, NameNorm = c.NameNorm, Pinyin = c.Pinyin,
        Initials = c.Initials, Category = c.Category, Summary = c.Summary, DataJson = c.DataJson, Source = c.Source,
        Edited = c.Edited,
    };

    public static MedConceptDto? Get(string id, bool withOccurrences = true)
    {
        var c = All().FirstOrDefault(x => x.Id == id);
        if (c == null) return null;
        var dto = ToDto(c);
        using var cn = MedDatabase.Instance.Open();
        using (var cmd = Cmd(cn, "SELECT data_json FROM med_concepts WHERE id=$id;"))
        { P(cmd, "$id", id); dto.DataJson = cmd.ExecuteScalar() as string ?? "{}"; }
        using (var cmd = Cmd(cn, "SELECT COUNT(*) FROM med_hits WHERE concept_id=$c;"))
        { P(cmd, "$c", id); dto.Hits = Convert.ToInt32(cmd.ExecuteScalar() ?? 0); }
        if (withOccurrences)
        {
            using var cmd = Cmd(cn, """
SELECT h.chunk_id,h.chapter_id,b.id,b.title,b.volume,c.title,c.href,k.text,k.p_start,k.p_end
FROM med_hits h JOIN med_books b ON b.id=h.book_id
JOIN med_chapters c ON c.id=h.chapter_id JOIN med_chunks k ON k.id=h.chunk_id
WHERE h.concept_id=$c ORDER BY b.added_at,c.ord LIMIT 60;
""");
            P(cmd, "$c", id);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                dto.Occurrences.Add(new MedHitDto
                {
                    ChunkId = r.GetString(0), ChapterId = r.GetString(1), BookId = r.GetString(2),
                    BookTitle = r.GetString(3), Volume = r.GetString(4), ChapterTitle = r.GetString(5),
                    ChapterPath = r.GetString(5), Href = r.GetString(6),
                    Snippet = MedText.Snippet(r.GetString(7), c.Name, 100),
                    PStart = r.GetInt32(8), PEnd = r.GetInt32(9),
                });
            }
        }
        using (var cmd = Cmd(cn, """
SELECT CASE WHEN l.src_id=$c THEN l.dst_id ELSE l.src_id END, SUM(l.weight)
FROM med_links l WHERE (l.src_id=$c OR l.dst_id=$c) AND l.dst_type='concept'
GROUP BY 1 ORDER BY 2 DESC LIMIT 12;
"""))
        {
            P(cmd, "$c", id);
            var ids = new List<(string, int)>();
            using (var r = cmd.ExecuteReader()) while (r.Read()) ids.Add((r.GetString(0), r.GetInt32(1)));
            foreach (var (rid, w) in ids)
            {
                var rel = All().FirstOrDefault(x => x.Id == rid);
                if (rel != null) { var d = ToDto(rel); d.Hits = w; dto.Related.Add(d); }
            }
        }
        dto.Backlinks = MedStore.Backlinks("concept", id);
        return dto;
    }
}
