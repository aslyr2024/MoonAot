using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace MoonAot.Store;

/// <summary>媒体库收藏夹：只存文件夹路径，名称可在添加时自定义。</summary>
public sealed class FavoriteStore
{
    public static FavoriteStore Instance { get; } = new();

    private FavoriteStore()
    {
        Database.Instance.Initialize();
    }

    private static SqliteCommand Cmd(SqliteConnection cn, string sql)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private static void P(SqliteCommand c, string name, object? value)
        => c.Parameters.AddWithValue(name, value ?? DBNull.Value);

    public List<FavoriteFolder> List()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT path,name,added_at FROM favorite_folders ORDER BY added_at DESC;");
            using var r = cmd.ExecuteReader();
            var list = new List<FavoriteFolder>();
            while (r.Read())
            {
                list.Add(new FavoriteFolder
                {
                    Path = r.GetString(0),
                    Name = r.IsDBNull(1) ? "" : r.GetString(1),
                    AddedAt = r.GetInt64(2),
                });
            }
            return list;
        }
    }

    public FavoriteFolder Add(string path, string? name)
    {
        var full = Normalize(path);
        if (full.Length == 0) throw new ArgumentException("收藏路径为空");
        var display = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)) : name.Trim();
        if (string.IsNullOrEmpty(display)) display = full;
        var rec = new FavoriteFolder
        {
            Path = full,
            Name = display,
            AddedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
        };
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, """
INSERT INTO favorite_folders(path,name,added_at) VALUES($p,$n,$a)
ON CONFLICT(path) DO UPDATE SET name=excluded.name;
""");
            P(cmd, "$p", rec.Path); P(cmd, "$n", rec.Name); P(cmd, "$a", rec.AddedAt);
            cmd.ExecuteNonQuery();
        }
        return rec;
    }

    public bool Remove(string path)
    {
        var full = Normalize(path);
        if (full.Length == 0) return false;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "DELETE FROM favorite_folders WHERE path=$p COLLATE NOCASE;");
            P(cmd, "$p", full);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public bool Exists(string path)
    {
        var full = Normalize(path);
        if (full.Length == 0) return false;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT 1 FROM favorite_folders WHERE path=$p COLLATE NOCASE;");
            P(cmd, "$p", full);
            return cmd.ExecuteScalar() != null;
        }
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path.Trim()); }
        catch { return path.Trim(); }
    }
}
