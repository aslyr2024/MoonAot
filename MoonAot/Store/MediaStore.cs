using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using MoonAot.Services;

namespace MoonAot.Store;

/// <summary>
/// 媒体数据（SQLite）：
///   media_progress / playlists / playlist_items / lyrics_cache / zip_last
/// 与书库共用 exe\data\moonaot.db。
/// </summary>
public sealed class MediaStore
{
    public static MediaStore Instance { get; } = new();

    private MediaStore()
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

    // ==================== 播放进度 ====================

    public MediaProgressItem? GetProgress(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT path,kind,position,duration,updated_at,title,artist,scale,rotation FROM media_progress WHERE path=$p COLLATE NOCASE;");
            P(cmd, "$p", path);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new MediaProgressItem
            {
                Path = r.GetString(0), Kind = r.GetString(1), Position = r.GetDouble(2),
                Duration = r.GetDouble(3), UpdatedAt = r.GetInt64(4),
                Title = r.IsDBNull(5) ? null : r.GetString(5),
                Artist = r.IsDBNull(6) ? null : r.GetString(6),
                Scale = r.IsDBNull(7) ? 1 : r.GetDouble(7),
                Rotation = r.IsDBNull(8) ? 0 : r.GetDouble(8),
            };
        }
    }

    public List<MediaProgressItem> AllProgress()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT path,kind,position,duration,updated_at,title,artist,scale,rotation FROM media_progress ORDER BY updated_at DESC LIMIT 500;");
            using var r = cmd.ExecuteReader();
            var list = new List<MediaProgressItem>();
            while (r.Read())
            {
                list.Add(new MediaProgressItem
                {
                    Path = r.GetString(0), Kind = r.GetString(1), Position = r.GetDouble(2),
                    Duration = r.GetDouble(3), UpdatedAt = r.GetInt64(4),
                    Title = r.IsDBNull(5) ? null : r.GetString(5),
                    Artist = r.IsDBNull(6) ? null : r.GetString(6),
                    Scale = r.IsDBNull(7) ? 1 : r.GetDouble(7),
                    Rotation = r.IsDBNull(8) ? 0 : r.GetDouble(8),
                });
            }
            return list;
        }
    }

    public void UpsertProgress(string path, string kind, double position, double duration, string? title, string? artist, double scale = 1, double rotation = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            using (var cmd = Cmd(cn, """
INSERT INTO media_progress(path,kind,position,duration,updated_at,title,artist,scale,rotation)
VALUES($p,$k,$pos,$dur,$u,$t,$a,$sc,$ro)
ON CONFLICT(path) DO UPDATE SET kind=excluded.kind,position=excluded.position,duration=excluded.duration,
updated_at=excluded.updated_at,title=excluded.title,artist=excluded.artist,scale=excluded.scale,rotation=excluded.rotation;
"""))
            {
                cmd.Transaction = tx;
                P(cmd, "$p", path); P(cmd, "$k", string.IsNullOrEmpty(kind) ? "audio" : kind);
                P(cmd, "$pos", position < 0 ? 0 : position); P(cmd, "$dur", duration < 0 ? 0 : duration);
                P(cmd, "$u", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                P(cmd, "$t", title); P(cmd, "$a", artist);
                P(cmd, "$sc", scale <= 0 ? 1 : scale); P(cmd, "$ro", rotation);
                cmd.ExecuteNonQuery();
            }
            using (var count = Cmd(cn, "SELECT COUNT(*) FROM media_progress;"))
            {
                count.Transaction = tx;
                var n = Convert.ToInt32(count.ExecuteScalar() ?? 0);
                if (n > 3000)
                {
                    using var del = Cmd(cn, "DELETE FROM media_progress WHERE path IN (SELECT path FROM media_progress ORDER BY updated_at ASC LIMIT $n);");
                    del.Transaction = tx;
                    P(del, "$n", n - 2500);
                    del.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
    }

    public void RemoveProgress(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "DELETE FROM media_progress WHERE path=$p COLLATE NOCASE;");
            P(cmd, "$p", path);
            cmd.ExecuteNonQuery();
        }
    }

    // ==================== 播放列表 ====================

    public PlaylistList GetAllPlaylists()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            var result = new PlaylistList { Items = new List<PlaylistSummary>() };
            using (var last = Cmd(cn, "SELECT last_playlist_id FROM app_settings WHERE id=1;"))
                result.LastId = last.ExecuteScalar() as string;
            using var cmd = Cmd(cn, """
SELECT p.id,p.name,p.kind,p.updated_at,(SELECT COUNT(*) FROM playlist_items i WHERE i.playlist_id=p.id)
FROM playlists p ORDER BY p.updated_at DESC;
""");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Items.Add(new PlaylistSummary
                {
                    Id = r.GetString(0), Name = r.GetString(1), Kind = r.GetString(2),
                    UpdatedAt = r.GetInt64(3), Count = (int)r.GetInt64(4),
                });
            }
            return result;
        }
    }

    public PlaylistRecord? GetPlaylist(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            PlaylistRecord? rec = null;
            using (var cmd = Cmd(cn, "SELECT id,name,kind,current,updated_at FROM playlists WHERE id=$id;"))
            {
                P(cmd, "$id", id);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                rec = new PlaylistRecord
                {
                    Id = r.GetString(0), Name = r.GetString(1), Kind = r.GetString(2),
                    Current = (int)r.GetInt64(3), UpdatedAt = r.GetInt64(4),
                };
            }
            using (var items = Cmd(cn, "SELECT path FROM playlist_items WHERE playlist_id=$id ORDER BY ord;"))
            {
                P(items, "$id", id);
                using var r = items.ExecuteReader();
                while (r.Read()) rec.Items.Add(r.GetString(0));
            }
            return rec;
        }
    }

    public PlaylistRecord SavePlaylist(PlaylistRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Id)) record.Id = Guid.NewGuid().ToString("N");
        record.UpdatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            using (var cmd = Cmd(cn, """
INSERT INTO playlists(id,name,kind,current,updated_at) VALUES($id,$n,$k,$c,$u)
ON CONFLICT(id) DO UPDATE SET name=excluded.name,kind=excluded.kind,current=excluded.current,updated_at=excluded.updated_at;
"""))
            {
                cmd.Transaction = tx;
                P(cmd, "$id", record.Id); P(cmd, "$n", record.Name); P(cmd, "$k", record.Kind);
                P(cmd, "$c", record.Current); P(cmd, "$u", record.UpdatedAt);
                cmd.ExecuteNonQuery();
            }
            using (var del = Cmd(cn, "DELETE FROM playlist_items WHERE playlist_id=$id;"))
            {
                del.Transaction = tx;
                P(del, "$id", record.Id);
                del.ExecuteNonQuery();
            }
            using (var ins = Cmd(cn, "INSERT INTO playlist_items(playlist_id,ord,path) VALUES($id,$o,$p);"))
            {
                ins.Transaction = tx;
                var pi = ins.Parameters.Add("$id", SqliteType.Text);
                var po = ins.Parameters.Add("$o", SqliteType.Integer);
                var pp = ins.Parameters.Add("$p", SqliteType.Text);
                pi.Value = record.Id;
                for (int i = 0; i < record.Items.Count; i++)
                {
                    po.Value = i; pp.Value = record.Items[i];
                    try { ins.ExecuteNonQuery(); } catch { }
                }
            }
            using (var last = Cmd(cn, "UPDATE app_settings SET last_playlist_id=$id WHERE id=1;"))
            {
                last.Transaction = tx;
                P(last, "$id", record.Id);
                last.ExecuteNonQuery();
            }
            tx.Commit();
            return record;
        }
    }

    public bool DeletePlaylist(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            int n;
            using (var cmd = Cmd(cn, "DELETE FROM playlist_items WHERE playlist_id=$id; DELETE FROM playlists WHERE id=$id;"))
            {
                cmd.Transaction = tx;
                P(cmd, "$id", id);
                n = cmd.ExecuteNonQuery();
            }
            using (var clear = Cmd(cn, "UPDATE app_settings SET last_playlist_id=NULL WHERE id=1 AND last_playlist_id=$id;"))
            {
                clear.Transaction = tx;
                P(clear, "$id", id);
                clear.ExecuteNonQuery();
            }
            tx.Commit();
            return n > 0;
        }
    }

    public void SetLastPlaylist(string? id)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "UPDATE app_settings SET last_playlist_id=$id WHERE id=1;");
            P(cmd, "$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // ==================== 压缩包位置 ====================

    public string? GetZipLast(string zipPath)
    {
        if (string.IsNullOrEmpty(zipPath)) return null;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT entry FROM zip_last WHERE path=$p COLLATE NOCASE;");
            P(cmd, "$p", zipPath);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetZipLast(string zipPath, string? entry)
    {
        if (string.IsNullOrWhiteSpace(zipPath)) return;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            if (string.IsNullOrEmpty(entry))
            {
                using var del = Cmd(cn, "DELETE FROM zip_last WHERE path=$p COLLATE NOCASE;");
                P(del, "$p", zipPath);
                del.ExecuteNonQuery();
                return;
            }
            using var cmd = Cmd(cn, "INSERT INTO zip_last(path,entry) VALUES($p,$e) ON CONFLICT(path) DO UPDATE SET entry=excluded.entry;");
            P(cmd, "$p", zipPath); P(cmd, "$e", entry);
            cmd.ExecuteNonQuery();
        }
    }

    // ==================== 歌词缓存 ====================

    public LyricsCacheData? GetLyrics(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var hash = LyricsKey(key);
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT key,fetched_at,source,synced,plain,lrc FROM lyrics_cache WHERE key=$k;");
            P(cmd, "$k", hash);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var fetched = r.GetInt64(1);
            if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - fetched > 30L * 24 * 3600 * 1000) return null;
            return new LyricsCacheData
            {
                Key = r.GetString(0), FetchedAt = fetched, Source = r.GetString(2),
                Synced = r.GetInt64(3) != 0, Plain = r.GetString(4),
                Lrc = r.IsDBNull(5) ? null : r.GetString(5),
            };
        }
    }

    public void PutLyrics(string key, LyricsCacheData data)
    {
        if (string.IsNullOrEmpty(key)) return;
        var hash = LyricsKey(key);
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, """
INSERT INTO lyrics_cache(key,fetched_at,source,synced,plain,lrc) VALUES($k,$f,$s,$y,$p,$l)
ON CONFLICT(key) DO UPDATE SET fetched_at=excluded.fetched_at,source=excluded.source,synced=excluded.synced,
plain=excluded.plain,lrc=excluded.lrc;
""");
            P(cmd, "$k", hash);
            P(cmd, "$f", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            P(cmd, "$s", data.Source ?? "");
            P(cmd, "$y", data.Synced ? 1 : 0);
            P(cmd, "$p", data.Plain ?? "");
            P(cmd, "$l", data.Lrc);
            cmd.ExecuteNonQuery();
        }
    }

    public static string LyricsKey(string text)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void FlushAll()
    {
        Database.Instance.Checkpoint();
    }
}
