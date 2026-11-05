using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using static MoonAot.Med.MedDatabase;

namespace MoonAot.Med;

public static class MedGraph
{
    /// <summary>以任一节点为中心做 1-2 跳展开；节点上限 200，避免前端卡顿。</summary>
    public static MedGraphDto Build(string center, int depth = 1, int limit = 180)
    {
        var dto = new MedGraphDto();
        depth = Math.Clamp(depth, 1, 2);
        if (string.IsNullOrWhiteSpace(center)) return dto;
        var concepts = MedConcepts.All().ToDictionary(x => x.Id, x => x, StringComparer.Ordinal);
        var weights = new Dictionary<string, int>(StringComparer.Ordinal) { [center] = 100 };
        var edges = new List<MedGraphEdge>();

        using var cn = MedDatabase.Instance.Open();
        var frontier = new HashSet<string>(StringComparer.Ordinal) { center };
        for (int d = 0; d < depth; d++)
        {
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in frontier)
            {
                using var cmd = Cmd(cn, """
SELECT src_type,src_id,dst_type,dst_id,kind,weight FROM med_links
WHERE (src_id=$i OR dst_id=$i) AND src_type='concept' AND dst_type='concept'
ORDER BY weight DESC LIMIT 60;
""");
                P(cmd, "$i", id);
                var rows = new List<MedGraphEdge>();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) rows.Add(new MedGraphEdge
                    {
                        Source = r.GetString(0) + ":" + r.GetString(1), Target = r.GetString(2) + ":" + r.GetString(3),
                        Kind = r.GetString(4), Weight = r.GetInt32(5),
                    });
                foreach (var e in rows)
                {
                    if (edges.Count >= limit * 2) break;
                    edges.Add(e);
                    var other = e.Source == "concept:" + id ? e.Target : e.Source;
                    var oid = other.StartsWith("concept:") ? other[8..] : "";
                    if (oid.Length > 0 && oid != id) { next.Add(oid); weights[oid] = Math.Max(weights.GetValueOrDefault(oid), e.Weight); }
                }
            }
            frontier = next;
            if (weights.Count >= limit) break;
        }
        var keep = weights.OrderByDescending(kv => kv.Value).Take(limit).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var id in keep)
        {
            if (concepts.TryGetValue(id, out var c))
                dto.Nodes.Add(new MedGraphNode { Id = "concept:" + id, Type = c.Type, Label = c.Name, Weight = weights[id] });
        }
        foreach (var e in edges.Where(e => keep.Contains(e.Source[8..]) && keep.Contains(e.Target[8..])).Take(limit * 2))
            dto.Edges.Add(e);
        dto.Truncated = weights.Count > limit ? 1 : 0;
        return dto;
    }
}
