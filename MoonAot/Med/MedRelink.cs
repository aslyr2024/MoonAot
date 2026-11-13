using System;
using System.Collections.Generic;
using System.IO;
using MoonAot.Services;
using MoonAot.Store;

namespace MoonAot.Med;

/// <summary>
/// 换电脑后把医学助手里失效的 EPUB 路径自动重连到本机主书库中的同名文件。
/// 不复制、不内嵌图书字节：只更新 med_books.path / book_id，
/// 精读与 /medapi/asset 仍按路径读本机已导入的文件。
/// </summary>
public static class MedRelink
{
    private static readonly object Gate = new();
    private static long _lastRun;

    public sealed class Result
    {
        public int Missing { get; set; }
        public int Relinked { get; set; }
        public List<string> Messages { get; set; } = new();
        public string? Error { get; set; }
    }

    private static IEnumerable<string> Keys(string path, string title)
    {
        yield return MedText.NormalizeTitle(Path.GetFileNameWithoutExtension(path ?? ""));
        yield return MedText.NormalizeTitle(title ?? "");
    }

    public static Result Run()
    {
        lock (Gate)
        {
            var res = new Result();
            try
            {
                Database.Instance.Initialize();
                MedDatabase.Instance.Initialize();

                // 本机主书库里所有已登记且确实存在的 EPUB，按规范化文件名/标题建索引
                var candidates = new Dictionary<string, List<(string Id, string Path)>>(StringComparer.Ordinal);
                using (var cn = Database.Instance.Open())
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id,file_path,title FROM books WHERE lower(format)='epub' AND file_path IS NOT NULL AND file_path<>'';";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var id = r.GetString(0);
                        var path = r.GetString(1);
                        var title = r.IsDBNull(2) ? "" : r.GetString(2);
                        if (!File.Exists(path)) continue;
                        foreach (var key in Keys(path, title))
                        {
                            if (key.Length < 2) continue;
                            if (!candidates.TryGetValue(key, out var l)) { l = new List<(string, string)>(); candidates[key] = l; }
                            l.Add((id, path));
                        }
                    }
                }

                var missing = new List<(string Id, string Title, string Path)>();
                using (var cn = MedDatabase.Instance.Open())
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id,title,path FROM med_books WHERE type='epub' AND path<>'';";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var p = r.GetString(2);
                        if (File.Exists(p)) continue;
                        missing.Add((r.GetString(0), r.GetString(1), p));
                    }
                }
                res.Missing = missing.Count;
                if (missing.Count == 0 || candidates.Count == 0) return res;

                using var cn2 = MedDatabase.Instance.Open();
                using var tx = cn2.BeginTransaction();
                using var up = cn2.CreateCommand();
                up.Transaction = tx;
                up.CommandText = "UPDATE med_books SET path=$p, book_id=$mb WHERE id=$i;";
                foreach (var row in missing)
                {
                    (string Id, string Path)? hit = null;
                    foreach (var key in Keys(row.Path, row.Title))
                    {
                        if (key.Length < 2 || !candidates.TryGetValue(key, out var l)) continue;
                        foreach (var c in l)
                            if (File.Exists(c.Path)) { hit = c; break; }
                        if (hit != null) break;
                    }
                    if (hit == null) continue;
                    up.Parameters.Clear();
                    up.Parameters.AddWithValue("$p", hit.Value.Path);
                    up.Parameters.AddWithValue("$mb", hit.Value.Id);
                    up.Parameters.AddWithValue("$i", row.Id);
                    up.ExecuteNonQuery();
                    res.Relinked++;
                    if (res.Messages.Count < 20) res.Messages.Add($"{row.Title} → {hit.Value.Path}");
                }
                tx.Commit();
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                Log.Warn("[med] 路径重连失败: " + ex.Message);
            }
            return res;
        }
    }

    /// <summary>状态/书目接口调用；10 秒节流，导入新书后很快自动重连。</summary>
    public static void RunThrottled()
    {
        var now = Environment.TickCount64;
        if (now - _lastRun < 10000) return;
        _lastRun = now;
        var r = Run();
        if (r.Relinked > 0) Log.Info($"[med] 自动重连图书路径 {r.Relinked}/{r.Missing} 本");
    }
}
