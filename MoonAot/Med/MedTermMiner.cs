using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MoonAot.Med;

public sealed class MedMineReportDto
{
    public bool Ok { get; set; } = true;
    public string Kind { get; set; } = "";
    public int Added { get; set; }
    public int Skipped { get; set; }
    public int Scanned { get; set; }
    public int ReindexHits { get; set; }
    public List<string> Samples { get; set; } = new();
    public long ElapsedMs { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// 「所有概念入库」的两条来源：
/// 1) ImportDictionary —— 导入外部权威词表（ICD-10 / MeSH 中文等 JSON 数组或每行一词的 txt）；
/// 2) Mine —— 从已导入图书的 med_chunks 正文里做 n-gram 新词发现，写入候选词条并对齐命中。
/// 两者都写入 med_concepts，只是 source 不同，便于以后单独重建/清理。
/// </summary>
public static class MedTermMiner
{
    private static readonly Regex CnRun = new(@"[\u4e00-\u9fa5]{2,}", RegexOptions.Compiled);
    private static readonly HashSet<char> FunctionChars = new("的了和与及在是有可为不中等或之其者上下对从以并把被让使这那也很更最再即且而于所但如若则及");
    private static readonly string[] GenericWords =
    {
        "患者", "治疗", "临床", "可以", "进行", "出现", "主要", "以及", "对于", "根据",
        "相关", "由于", "通过", "使用", "需要", "应该", "可能", "通常", "一般", "同时",
        "其中", "这种", "这些", "那些", "目前", "方法", "情况", "时间", "不同", "发生",
        "作用", "影响", "结果", "包括", "提供", "具有", "没有", "显示", "建议", "检查",
        "第一", "第二", "一种", "部分", "内容", "本书", "章节", "如下", "所示", "参见",
        "细胞", "感染", "疾病", "功能", "表现", "药物", "症状", "诊断", "水平", "指数",
        "系统", "组织", "结构", "过程", "变化", "增加", "减少", "异常", "正常", "程度",
        "类型", "状态", "机制", "途径", "反应", "综合征", "患者", "治疗", "检查", "评估",
        "处理", "操作", "记录", "报告", "资料", "文献", "研究", "观察", "分析", "数据",
        "大多数", "一部分", "可能性", "情况下", "有助于", "取决于", "不典型", "进一步",
    };

    private static string IdFor(string prefix, string name)
        => prefix + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(name)))[..16].ToLowerInvariant();

    private static string Str(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static HashSet<string> ExistingNorms()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var cn = MedDatabase.Instance.Open();
        using var cmd = MedDatabase.Cmd(cn, "SELECT name_norm FROM med_concepts WHERE COALESCE(deleted,0)=0;");
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.IsDBNull(0) ? "" : r.GetString(0));
        return set;
    }

    /// <summary>方案 1：导入外部词表。body 支持 {items:[...]}、直接数组、{path:"词表.json|.txt"}。</summary>
    public static MedMineReportDto ImportDictionary(JsonElement body)
    {
        var sw = Stopwatch.StartNew();
        var rep = new MedMineReportDto { Kind = "dictionary" };
        try
        {
            var items = new List<(string Name, string Type, string Category, string Keywords, string Summary)>();
            void AddItem(JsonElement e)
            {
                var name = Str(e, "name");
                if (name.Length == 0 && e.ValueKind == JsonValueKind.String) name = e.GetString() ?? "";
                name = name.Trim();
                if (name.Length < 2 || name.Length > 60) return;
                items.Add((name, Str(e, "type") is { Length: > 0 } t ? t : "term",
                    Str(e, "category") is { Length: > 0 } c ? c : "导入词表",
                    Str(e, "keywords"), Str(e, "summary")));
            }
            if (body.ValueKind == JsonValueKind.Array)
                foreach (var e in body.EnumerateArray()) AddItem(e);
            else if (body.ValueKind == JsonValueKind.Object)
            {
                if (body.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var e in arr.EnumerateArray()) AddItem(e);
                var path = Str(body, "path");
                if (path.Length > 0 && File.Exists(path))
                {
                    if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var line in File.ReadAllLines(path))
                        {
                            var n = line.Trim();
                            if (n.Length >= 2 && n.Length <= 60) items.Add((n, "term", "导入词表", "", ""));
                        }
                    }
                    else
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(path));
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            foreach (var e in doc.RootElement.EnumerateArray()) AddItem(e);
                    }
                }
            }
            rep.Scanned = items.Count;
            var norms = ExistingNorms();
            using var cn = MedDatabase.Instance.Open();
            using var tx = cn.BeginTransaction();
            using var ins = MedDatabase.Cmd(cn, """
INSERT OR IGNORE INTO med_concepts(id,type,name,name_norm,pinyin,initials,category,keywords,data_json,summary,source,source_version)
 VALUES($id,$t,$n,$nn,'','',$c,$k,'{}',$s,'dictionary','user');
""");
            ins.Transaction = tx;
            foreach (var it in items)
            {
                var norm = MedText.NormalizeTitle(it.Name);
                if (norm.Length < 2 || !norms.Add(norm)) { rep.Skipped++; continue; }
                ins.Parameters.Clear();
                MedDatabase.P(ins, "$id", IdFor("dict-", it.Name));
                MedDatabase.P(ins, "$t", it.Type);
                MedDatabase.P(ins, "$n", it.Name);
                MedDatabase.P(ins, "$nn", norm);
                MedDatabase.P(ins, "$c", it.Category);
                MedDatabase.P(ins, "$k", it.Keywords);
                MedDatabase.P(ins, "$s", it.Summary.Length > 0 ? it.Summary : it.Name);
                rep.Added += ins.ExecuteNonQuery();
                if (rep.Samples.Count < 10) rep.Samples.Add(it.Name);
            }
            tx.Commit();
            MedConcepts.Invalidate();
            MedSearch.Invalidate();
        }
        catch (Exception ex) { rep.Ok = false; rep.Error = ex.Message; }
        rep.ElapsedMs = sw.ElapsedMilliseconds;
        return rep;
    }

    private sealed class Stat { public int Tf; public int Df; }

    private static bool BadCandidate(string w)
    {
        if (w.Length < 2 || FunctionChars.Contains(w[0]) || FunctionChars.Contains(w[^1])) return true;
        foreach (var g in GenericWords) if (w.Contains(g, StringComparison.Ordinal)) return true;
        bool allSame = true;
        for (int i = 1; i < w.Length; i++) if (w[i] != w[0]) { allSame = false; break; }
        return allSame;
    }

    /// <summary>方案 2：从 20,585 段正文里发现高频、成词的中文术语，写入候选词条并重对齐。</summary>
    public static MedMineReportDto Mine(int top = 800, int minDf = 3)
    {
        var sw = Stopwatch.StartNew();
        var rep = new MedMineReportDto { Kind = "mined" };
        try
        {
            top = Math.Clamp(top, 1, 3000);
            minDf = Math.Clamp(minDf, 2, 50);
            var stat = new Dictionary<string, Stat>(StringComparer.Ordinal);
            var chunks = new List<string>();
            using (var cn = MedDatabase.Instance.Open())
            using (var cmd = MedDatabase.Cmd(cn, "SELECT text FROM med_chunks;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) chunks.Add(r.IsDBNull(0) ? "" : r.GetString(0));
            rep.Scanned = chunks.Count;

            foreach (var text in chunks)
            {
                if (string.IsNullOrEmpty(text)) continue;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match m in CnRun.Matches(text))
                {
                    var s = m.Value;
                    for (int n = 2; n <= 5; n++)
                    {
                        for (int i = 0; i + n <= s.Length; i++)
                        {
                            var w = s.Substring(i, n);
                            if (BadCandidate(w)) continue;
                            if (!stat.TryGetValue(w, out var st)) { st = new Stat(); stat[w] = st; }
                            st.Tf++;
                            if (seen.Add(w)) st.Df++;
                        }
                    }
                }
            }

            var norms = ExistingNorms();
            int dfCeiling = Math.Max(20, (int)(chunks.Count * 0.18));   // 太高频的是通用词，不是术语
            var candidates = stat
                .Where(kv => kv.Key.Length >= 3 && kv.Value.Df >= minDf && kv.Value.Df <= dfCeiling &&
                             kv.Value.Tf >= Math.Max(5, minDf * 2))
                .Where(kv => !norms.Contains(MedText.NormalizeTitle(kv.Key)))
                .OrderByDescending(kv => kv.Value.Tf * Math.Log(1 + kv.Value.Df) + kv.Key.Length * kv.Key.Length * 4)
                .Select(kv => (Word: kv.Key, kv.Value.Tf, kv.Value.Df))
                .ToList();

            // 子串抑制：更长的词频接近时丢掉短碎片（如「肌梗」保留「心肌梗死」）
            var chosen = new List<(string Word, int Tf, int Df)>();
            foreach (var c in candidates)
            {
                bool shadowed = chosen.Any(x => x.Word.Length > c.Word.Length &&
                    x.Word.Contains(c.Word, StringComparison.Ordinal) && x.Tf >= c.Tf * 0.6);
                if (shadowed) continue;
                chosen.Add(c);
                if (chosen.Count >= top) break;
            }

            using var cn2 = MedDatabase.Instance.Open();
            using var tx = cn2.BeginTransaction();
            using var ins = MedDatabase.Cmd(cn2, """
INSERT OR IGNORE INTO med_concepts(id,type,name,name_norm,pinyin,initials,category,keywords,data_json,summary,source,source_version)
 VALUES($id,'term',$n,$nn,'','','书中术语','','{}',$s,'mined','auto');
""");
            ins.Transaction = tx;
            foreach (var c in chosen)
            {
                ins.Parameters.Clear();
                MedDatabase.P(ins, "$id", IdFor("mine-", c.Word));
                MedDatabase.P(ins, "$n", c.Word);
                MedDatabase.P(ins, "$nn", MedText.NormalizeTitle(c.Word));
                MedDatabase.P(ins, "$s", $"书中出现 {c.Df} 段 / {c.Tf} 次");
                rep.Added += ins.ExecuteNonQuery();
                if (rep.Samples.Count < 12) rep.Samples.Add($"{c.Word} ({c.Df}段/{c.Tf}次)");
            }
            tx.Commit();
            MedConcepts.Invalidate();
            MedSearch.Invalidate();
            rep.ReindexHits = MedImport.Reindex().Hits; // 让新词立刻在全书产生命中与双链
        }
        catch (Exception ex) { rep.Ok = false; rep.Error = ex.Message; }
        rep.ElapsedMs = sw.ElapsedMilliseconds;
        return rep;
    }
}
