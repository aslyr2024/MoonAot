using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using MoonAot.Services;

namespace MoonAot.Store;

/// <summary>书库查询条件。</summary>
public sealed class LibraryQuery
{
    public string? Q { get; set; }
    public string Sort { get; set; } = "recent";
    public bool Desc { get; set; } = true;
    public string? Format { get; set; }
    public string? Author { get; set; }
    public string? Category { get; set; }
    public string? Folder { get; set; }
    /// <summary>all / unread / reading / finished / favorite</summary>
    public string Filter { get; set; } = "all";

    public string Signature =>
        (Q ?? "") + "|" + Sort + "|" + (Desc ? 1 : 0) + "|" + (Format ?? "") + "|" +
        (Author ?? "") + "|" + (Category ?? "") + "|" + (Folder ?? "") + "|" + Filter;
}

/// <summary>
/// 应用数据存储（SQLite，原生 SQL，NativeAOT 已实测）。
/// 数据库位于 exe\data\moonaot.db；旧版 JSON 分片会在首次启动时自动导入并保留原文件作为备份。
/// 书库分页/搜索/排序全部交给 SQL，10 万本书不再需要一次性加载 95MB JSON。
/// </summary>
public sealed class DbStore
{
    public static DbStore Instance { get; } = new();

    public const string DefaultReaderSettings =
        "{\"version\":1,\"language\":\"zh-CN\",\"themeMode\":\"light\",\"themeColor\":\"#3B6EF6\"," +
        "\"libraryViewMode\":\"grid\",\"librarySortBy\":\"updated\",\"librarySortAscending\":false," +
        "\"globalViewSettings\":{\"defaultFontSize\":18,\"minimumFontSize\":8,\"fontWeight\":400," +
        "\"lineHeight\":\"1.8\",\"defaultFont\":\"Sans-Serif Font\",\"defaultCJKFont\":\"FZKeBenFangSong\"," +
        "\"serifFont\":\"Bitter\",\"sansSerifFont\":\"Roboto\",\"monospaceFont\":\"Consolas\"," +
        "\"fullJustification\":true,\"textIndent\":0,\"paragraphMargin\":0.8,\"letterSpacing\":0," +
        "\"theme\":\"light\",\"scrolled\":false,\"continuousScroll\":false,\"maxColumnCount\":2," +
        "\"marginTopPx\":40,\"marginBottomPx\":40,\"marginLeftPx\":20,\"marginRightPx\":20," +
        "\"animated\":false,\"showHeader\":true,\"showFooter\":true,\"showPageNumber\":true}}";

    private const string BookCols =
        "id,title,author,file_path,source_path,format,file_size,added_at,last_read_at,progress,location," +
        "cover_file,cover_ext,cover_version,publisher,description,language,category,folder,favorite,reading_seconds,finished_at";

    private const string ItemCols =
        "id,title,author,format,added_at,last_read_at,progress,cover_version,cover_file,file_size,category,folder,finished_at,favorite,file_path";

    private readonly Dictionary<string, DateTime> _lastConfigSeen = new(StringComparer.Ordinal);
    private readonly object _seenLock = new();

    public string DataDir => Database.Instance.DataDir;
    public string CoversDir => Database.Instance.CoversDir;
    public string ThumbsDir => Database.Instance.ThumbsDir;
    public string MediaDir => Database.Instance.MediaDir;
    public string LyricsDir => Database.Instance.LyricsDir;
    public string LibraryFilesDir => Database.Instance.LibraryDir;

    private DbStore()
    {
        Database.Instance.Initialize();
        Log.Info("数据目录: " + DataDir);
        MigrateLegacy();
        EnsureDefaults();
        ScanCovers();
        CleanupLegacyCoverMarkers();
        Log.Info($"SQLite 就绪: {Count} 本，版本 {Version}");
    }

    // ==================== 基础工具 ====================

    private static SqliteCommand Cmd(SqliteConnection cn, string sql, SqliteTransaction? tx = null)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        if (tx != null) cmd.Transaction = tx;
        return cmd;
    }

    private static void P(SqliteCommand c, string name, object? value)
        => c.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static long NowMs() => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    private static long Ms(DateTime dt) => new DateTimeOffset(dt).ToUnixTimeMilliseconds();

    private static long? NMs(DateTime? dt) => dt == null ? null : new DateTimeOffset(dt.Value).ToUnixTimeMilliseconds();

    private static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    private static DateTime? FromMs(long? ms) => ms == null ? null : FromMs(ms.Value);

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static long Lng(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);

    private static long? NLng(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    private static double Dbl(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetDouble(i);

    private static string SafeDir(string? path)
    {
        try { return Path.GetDirectoryName(path ?? "") ?? ""; }
        catch { return ""; }
    }

    private static string FormatFromExt(string ext) => ext.ToLowerInvariant() switch
    {
        ".epub" => BookFormatEx.Epub,
        ".pdf" => BookFormatEx.Pdf,
        ".mobi" => BookFormatEx.Mobi,
        ".azw3" => BookFormatEx.Azw3,
        ".md" or ".markdown" => BookFormatEx.Md,
        _ => BookFormatEx.Txt,
    };

    private static void BumpVersion(SqliteConnection cn, SqliteTransaction? tx)
    {
        using var cmd = Cmd(cn, "UPDATE app_settings SET library_version = library_version + 1 WHERE id = 1;", tx);
        cmd.ExecuteNonQuery();
    }

    // ==================== 书籍读取 ====================

    private static BookRecord ReadBook(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Title = r.GetString(1),
        Author = r.GetString(2),
        FilePath = r.GetString(3),
        SourcePath = Str(r, 4),
        Format = r.GetString(5),
        FileSize = Lng(r, 6),
        AddedAt = FromMs(Lng(r, 7)),
        LastReadAt = FromMs(NLng(r, 8)),
        Progress = Dbl(r, 9),
        Location = Str(r, 10),
        CoverFile = Str(r, 11),
        CoverExt = Str(r, 12),
        CoverVersion = (int)Lng(r, 13),
        Publisher = Str(r, 14),
        Description = Str(r, 15),
        Language = Str(r, 16),
        Category = Str(r, 17),
        Favorite = Lng(r, 19) != 0,
        ReadingSeconds = Lng(r, 20),
        FinishedAt = FromMs(NLng(r, 21)),
    };

    private static void EnsureBookParams(SqliteCommand c)
    {
        void A(string n, SqliteType t) => c.Parameters.Add(new SqliteParameter(n, t));
        A("$id", SqliteType.Text);
        A("$title", SqliteType.Text);
        A("$author", SqliteType.Text);
        A("$file_path", SqliteType.Text);
        A("$source_path", SqliteType.Text);
        A("$format", SqliteType.Text);
        A("$file_size", SqliteType.Integer);
        A("$added_at", SqliteType.Integer);
        A("$last_read_at", SqliteType.Integer);
        A("$progress", SqliteType.Real);
        A("$location", SqliteType.Text);
        A("$cover_file", SqliteType.Text);
        A("$cover_ext", SqliteType.Text);
        A("$cover_version", SqliteType.Integer);
        A("$publisher", SqliteType.Text);
        A("$description", SqliteType.Text);
        A("$language", SqliteType.Text);
        A("$category", SqliteType.Text);
        A("$folder", SqliteType.Text);
        A("$favorite", SqliteType.Integer);
        A("$reading_seconds", SqliteType.Integer);
        A("$finished_at", SqliteType.Integer);
    }

    private static void Set(SqliteCommand c, string name, object? value)
    {
        var p = c.Parameters[name];
        if (p == null) c.Parameters.AddWithValue(name, value ?? DBNull.Value);
        else p.Value = value ?? DBNull.Value;
    }

    private static void BindBook(SqliteCommand c, BookRecord b)
    {
        Set(c, "$id", b.Id);
        Set(c, "$title", b.Title);
        Set(c, "$author", b.Author ?? "");
        Set(c, "$file_path", b.FilePath);
        Set(c, "$source_path", b.SourcePath);
        Set(c, "$format", b.Format);
        Set(c, "$file_size", b.FileSize);
        Set(c, "$added_at", Ms(b.AddedAt));
        Set(c, "$last_read_at", NMs(b.LastReadAt));
        Set(c, "$progress", b.Progress);
        Set(c, "$location", b.Location);
        Set(c, "$cover_file", b.CoverFile);
        Set(c, "$cover_ext", b.CoverExt);
        Set(c, "$cover_version", b.CoverVersion);
        Set(c, "$publisher", b.Publisher);
        Set(c, "$description", b.Description);
        Set(c, "$language", b.Language);
        Set(c, "$category", b.Category);
        Set(c, "$folder", SafeDir(b.FilePath));
        Set(c, "$favorite", b.Favorite ? 1 : 0);
        Set(c, "$reading_seconds", b.ReadingSeconds);
        Set(c, "$finished_at", NMs(b.FinishedAt));
    }

    private static void Normalize(BookRecord b)
    {
        if (string.IsNullOrWhiteSpace(b.Id)) b.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(b.FilePath)) b.FilePath = "";
        b.Title = (b.Title ?? "").Trim();
        if (b.Title.Length == 0)
            b.Title = MetadataService.CleanFileNameTitle(Path.GetFileNameWithoutExtension(b.FilePath));
        if (b.Title.Length == 0) b.Title = "未命名";
        b.Author = (b.Author ?? "").Trim();
        if (string.IsNullOrWhiteSpace(b.Format))
            b.Format = FormatFromExt(Path.GetExtension(b.FilePath));
        if (b.AddedAt.Year < 1900) b.AddedAt = DateTime.Now;
        if (b.LastReadAt is { Year: < 1900 }) b.LastReadAt = null;
        if (b.FinishedAt is { Year: < 1900 }) b.FinishedAt = null;
        if (b.Progress < 0) b.Progress = 0;
        if (b.Progress > 1) b.Progress = 1;
        if (!string.IsNullOrEmpty(b.CoverFile) && !File.Exists(b.CoverFile)) b.CoverFile = null;
        if (string.IsNullOrEmpty(b.CoverFile)) b.CoverExt = null;
        if (string.IsNullOrEmpty(b.CoverExt) && !string.IsNullOrEmpty(b.CoverFile))
            b.CoverExt = Path.GetExtension(b.CoverFile).ToLowerInvariant();
    }

    // ==================== 书籍 API ====================

    public int Count
    {
        get
        {
            lock (Database.Instance.SyncRoot)
            {
                using var cn = Database.Instance.Open();
                using var cmd = Cmd(cn, "SELECT COUNT(*) FROM books;");
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }
        }
    }

    public long Version
    {
        get
        {
            lock (Database.Instance.SyncRoot)
            {
                using var cn = Database.Instance.Open();
                using var cmd = Cmd(cn, "SELECT library_version FROM app_settings WHERE id = 1;");
                var v = cmd.ExecuteScalar();
                return v == null ? 1 : Convert.ToInt64(v);
            }
        }
    }

    public BookRecord? GetBook(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, $"SELECT {BookCols} FROM books WHERE id = $id;");
            P(cmd, "$id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadBook(r) : null;
        }
    }

    public BookRecord? FindByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, $"SELECT {BookCols} FROM books WHERE file_path = $p COLLATE NOCASE;");
            P(cmd, "$p", path);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadBook(r) : null;
        }
    }

    public List<BookRecord> GetBooks(IEnumerable<string> ids)
    {
        var list = new List<BookRecord>();
        var arr = ids.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).Take(500).ToArray();
        if (arr.Length == 0) return list;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = cn.CreateCommand();
            var names = new string[arr.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                names[i] = "$p" + i;
                P(cmd, names[i], arr[i]);
            }
            cmd.CommandText = $"SELECT {BookCols} FROM books WHERE id IN ({string.Join(',', names)});";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadBook(r));
        }
        return list;
    }

    public bool AddBook(BookRecord book) => AddBooks(new[] { book }) > 0;

    public int AddBooks(IReadOnlyList<BookRecord> books)
    {
        if (books.Count == 0) return 0;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            var added = InsertBooks(cn, tx, books);
            if (added > 0) BumpVersion(cn, tx);
            tx.Commit();
            return added;
        }
    }

    private static int InsertBooks(SqliteConnection cn, SqliteTransaction tx, IReadOnlyList<BookRecord> books)
    {
        using var cmd = Cmd(cn, """
INSERT OR IGNORE INTO books(id,title,author,file_path,source_path,format,file_size,added_at,last_read_at,progress,location,cover_file,cover_ext,cover_version,publisher,description,language,category,folder,favorite,reading_seconds,finished_at)
VALUES($id,$title,$author,$file_path,$source_path,$format,$file_size,$added_at,$last_read_at,$progress,$location,$cover_file,$cover_ext,$cover_version,$publisher,$description,$language,$category,$folder,$favorite,$reading_seconds,$finished_at);
""", tx);
        EnsureBookParams(cmd);
        int added = 0;
        int failed = 0;
        string? firstError = null;
        foreach (var book in books)
        {
            try
            {
                Normalize(book);
                BindBook(cmd, book);
                added += cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                if (failed++ == 0) firstError = ex.Message;
            }
        }
        if (failed > 0) Log.Warn($"插入书籍失败 {failed} 本，首个错误: {firstError}");
        return added;
    }

    /// <summary>修改单本书（标题/作者/收藏/封面等）。进度请用 UpdateProgress。</summary>
    public void UpdateBook(string id, Action<BookRecord> mutate, bool metadataChanged = true)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            BookRecord? book;
            using (var get = Cmd(cn, $"SELECT {BookCols} FROM books WHERE id = $id;", tx))
            {
                P(get, "$id", id);
                using var r = get.ExecuteReader();
                if (!r.Read()) return;
                book = ReadBook(r);
            }
            mutate(book);
            Normalize(book);
            try
            {
                using var up = Cmd(cn, """
UPDATE books SET title=$title,author=$author,file_path=$file_path,source_path=$source_path,format=$format,
file_size=$file_size,added_at=$added_at,last_read_at=$last_read_at,progress=$progress,location=$location,
cover_file=$cover_file,cover_ext=$cover_ext,cover_version=$cover_version,publisher=$publisher,description=$description,
language=$language,category=$category,folder=$folder,favorite=$favorite,reading_seconds=$reading_seconds,finished_at=$finished_at
WHERE id=$id;
""", tx);
                EnsureBookParams(up);
                BindBook(up, book);
                up.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                Log.Warn("更新书籍失败（可能路径重复）" + id + ": " + ex.Message);
                tx.Rollback();
                return;
            }
            if (metadataChanged) BumpVersion(cn, tx);
            tx.Commit();
        }
    }

    /// <summary>仅更新进度/最近阅读（阅读器每次保存都会调用，避免整行读写）。</summary>
    public void UpdateProgress(string id, double progress, string? location)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            var now = NowMs();
            using (var cmd = Cmd(cn, """
UPDATE books SET last_read_at=$now,
  progress = CASE WHEN $p >= 0 THEN $p ELSE progress END,
  location = COALESCE($loc, location),
  finished_at = CASE WHEN $p >= 0.995 AND finished_at IS NULL THEN $now ELSE finished_at END
WHERE id=$id;
""", tx))
            {
                P(cmd, "$now", now);
                P(cmd, "$p", progress);
                P(cmd, "$loc", location);
                P(cmd, "$id", id);
                cmd.ExecuteNonQuery();
            }
            BumpVersion(cn, tx);
            tx.Commit();
        }
    }

    public void AddReadingSeconds(string id, long seconds, string? day = null)
    {
        if (seconds <= 0) return;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            using (var a = Cmd(cn, "UPDATE books SET reading_seconds = reading_seconds + $s WHERE id = $id;", tx))
            {
                P(a, "$s", seconds); P(a, "$id", id); a.ExecuteNonQuery();
            }
            using (var b = Cmd(cn, "UPDATE app_settings SET reading_seconds = reading_seconds + $s WHERE id = 1;", tx))
            {
                P(b, "$s", seconds); b.ExecuteNonQuery();
            }
            var key = day ?? DateTime.Now.ToString("yyyy-MM-dd");
            using (var c = Cmd(cn, """
INSERT INTO daily_reading(day, seconds) VALUES($d, $s)
ON CONFLICT(day) DO UPDATE SET seconds = seconds + excluded.seconds;
""", tx))
            {
                P(c, "$d", key); P(c, "$s", seconds); c.ExecuteNonQuery();
            }
            using (var d = Cmd(cn, "DELETE FROM daily_reading WHERE day NOT IN (SELECT day FROM daily_reading ORDER BY day DESC LIMIT 120);", tx))
                d.ExecuteNonQuery();
            tx.Commit();
        }
    }

    public bool RemoveBook(string id, out BookRecord? removed)
    {
        removed = GetBook(id);
        if (removed == null) return false;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            using (var c = Cmd(cn, "DELETE FROM configs WHERE book_id=$id; DELETE FROM notes WHERE book_id=$id; DELETE FROM books WHERE id=$id;", tx))
            {
                P(c, "$id", id);
                c.ExecuteNonQuery();
            }
            BumpVersion(cn, tx);
            tx.Commit();
        }
        return true;
    }

    public int RemoveBooks(IEnumerable<string> ids)
    {
        var arr = ids.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).ToArray();
        if (arr.Length == 0) return 0;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            int removed = 0;
            for (int off = 0; off < arr.Length; off += 200)
            {
                var chunk = arr.Skip(off).Take(200).ToArray();
                var names = new string[chunk.Length];
                using var cmd = cn.CreateCommand();
                cmd.Transaction = tx;
                for (int i = 0; i < chunk.Length; i++)
                {
                    names[i] = "$p" + i;
                    P(cmd, names[i], chunk[i]);
                }
                var inList = string.Join(',', names);
                cmd.CommandText = $"DELETE FROM configs WHERE book_id IN ({inList}); DELETE FROM notes WHERE book_id IN ({inList}); DELETE FROM books WHERE id IN ({inList});";
                removed += cmd.ExecuteNonQuery();
            }
            if (removed > 0) BumpVersion(cn, tx);
            tx.Commit();
            return Math.Min(removed, arr.Length);
        }
    }

    public LibraryPage Query(LibraryQuery q, int offset, int limit, bool needTotal = true)
    {
        if (offset < 0) offset = 0;
        if (limit <= 0) limit = 120;
        if (limit > 500) limit = 500;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            var (where, args) = BuildWhere(q);
            var order = BuildOrder(q);
            var page = new LibraryPage { Offset = offset, Limit = limit, Total = -1 };
            if (needTotal)
            {
                using var count = Cmd(cn, "SELECT COUNT(*) FROM books " + where + ";");
                foreach (var (n, v) in args) P(count, n, v);
                page.Total = Convert.ToInt32(count.ExecuteScalar() ?? 0);
            }
            using (var ver = Cmd(cn, "SELECT library_version FROM app_settings WHERE id = 1;"))
                page.Version = Convert.ToInt64(ver.ExecuteScalar() ?? 1L);

            using (var cmd = Cmd(cn, $"SELECT {ItemCols} FROM books {where} ORDER BY {order} LIMIT $limit OFFSET $offset;"))
            {
                foreach (var (n, v) in args) P(cmd, n, v);
                P(cmd, "$limit", limit);
                P(cmd, "$offset", offset);
                using var r = cmd.ExecuteReader();
                while (r.Read()) page.Items.Add(ReadItem(r));
            }
            return page;
        }
    }

    private static (string where, List<(string, object?)> args) BuildWhere(LibraryQuery q)
    {
        var sb = new StringBuilder("WHERE 1=1");
        var args = new List<(string, object?)>();
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var needle = q.Q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            sb.Append(" AND (title LIKE $like ESCAPE '\\' OR author LIKE $like ESCAPE '\\' OR folder LIKE $like ESCAPE '\\')");
            args.Add(("$like", "%" + needle + "%"));
        }
        if (!string.IsNullOrEmpty(q.Format) && q.Format != "all")
        {
            sb.Append(" AND format = $format");
            args.Add(("$format", q.Format));
        }
        if (!string.IsNullOrEmpty(q.Author))
        {
            sb.Append(" AND author = $author");
            args.Add(("$author", q.Author));
        }
        if (!string.IsNullOrEmpty(q.Category))
        {
            sb.Append(" AND category = $category");
            args.Add(("$category", q.Category));
        }
        if (!string.IsNullOrEmpty(q.Folder))
        {
            sb.Append(" AND folder = $folder COLLATE NOCASE");
            args.Add(("$folder", q.Folder));
        }
        switch (q.Filter)
        {
            case "unread":
                sb.Append(" AND progress <= 0 AND finished_at IS NULL");
                break;
            case "reading":
                sb.Append(" AND progress > 0 AND progress < 0.995 AND finished_at IS NULL");
                break;
            case "finished":
                sb.Append(" AND (finished_at IS NOT NULL OR progress >= 0.995)");
                break;
            case "favorite":
                sb.Append(" AND favorite = 1");
                break;
        }
        return (sb.ToString(), args);
    }

    private static string BuildOrder(LibraryQuery q)
    {
        var dir = q.Desc ? "DESC" : "ASC";
        return q.Sort switch
        {
            "added" => "added_at " + dir,
            "title" => "title COLLATE NOCASE " + dir,
            "author" => "author COLLATE NOCASE " + dir + ", title COLLATE NOCASE " + dir,
            "size" => "file_size " + dir,
            "format" => "format " + dir + ", title COLLATE NOCASE " + dir,
            _ => "COALESCE(last_read_at, added_at) " + dir,
        };
    }

    private LibraryItem ReadItem(SqliteDataReader r)
    {
        var progress = Dbl(r, 6);
        var coverFile = Str(r, 8);
        var filePath = r.GetString(14);
        var finishedAt = NLng(r, 12);
        var added = Lng(r, 4);
        var lastRead = NLng(r, 5);
        return new LibraryItem
        {
            Hash = r.GetString(0),
            Title = r.GetString(1),
            Author = r.GetString(2),
            Format = r.GetString(3),
            CreatedAt = added,
            UpdatedAt = lastRead ?? added,
            Progress = progress,
            CoverVersion = (int)Lng(r, 7),
            HasCover = !string.IsNullOrEmpty(coverFile) && File.Exists(coverFile),
            FileSize = Lng(r, 9),
            Category = Str(r, 10),
            Folder = Str(r, 11),
            Finished = finishedAt != null || progress >= 0.995,
            Favorite = Lng(r, 13) != 0,
            Missing = !File.Exists(filePath),
        };
    }

    public List<LibraryItem> Recent(int limit, bool inProgressOnly = false)
    {
        if (limit <= 0) limit = 12;
        if (limit > 200) limit = 200;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            var where = "WHERE last_read_at IS NOT NULL";
            if (inProgressOnly) where += " AND progress > 0 AND progress < 0.995";
            using var cmd = Cmd(cn, $"SELECT {ItemCols} FROM books {where} ORDER BY last_read_at DESC LIMIT $limit;");
            P(cmd, "$limit", limit);
            var list = new List<LibraryItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadItem(r));
            return list;
        }
    }

    public Facets GetFacets()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            var f = new Facets();
            using (var cmd = Cmd(cn, """
SELECT COUNT(*),
  COALESCE(SUM(favorite),0),
  COALESCE(SUM(CASE WHEN finished_at IS NOT NULL OR progress >= 0.995 THEN 1 ELSE 0 END),0),
  COALESCE(SUM(CASE WHEN finished_at IS NULL AND progress < 0.995 AND progress > 0 THEN 1 ELSE 0 END),0),
  COALESCE(SUM(CASE WHEN finished_at IS NULL AND progress <= 0 THEN 1 ELSE 0 END),0)
FROM books;
"""))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    f.Total = (int)r.GetInt64(0);
                    f.Favorites = (int)r.GetInt64(1);
                    f.Finished = (int)r.GetInt64(2);
                    f.Reading = (int)r.GetInt64(3);
                    f.Unread = (int)r.GetInt64(4);
                }
            }
            f.Formats = GroupCounts(cn, "SELECT format, COUNT(*) c FROM books GROUP BY format ORDER BY c DESC LIMIT 20;");
            f.Authors = GroupCounts(cn, "SELECT author, COUNT(*) c FROM books WHERE author <> '' GROUP BY author ORDER BY c DESC LIMIT 80;");
            f.Folders = GroupCounts(cn, "SELECT folder, COUNT(*) c FROM books WHERE folder IS NOT NULL AND folder <> '' GROUP BY folder ORDER BY c DESC LIMIT 80;");
            return f;
        }
    }

    private static List<CountItem> GroupCounts(SqliteConnection cn, string sql)
    {
        var list = new List<CountItem>();
        using var cmd = Cmd(cn, sql);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CountItem { Name = r.IsDBNull(0) ? "" : r.GetString(0), Count = (int)r.GetInt64(1) });
        return list;
    }

    public BookDetail? BuildDetail(string id, Func<string, int> countNotes)
    {
        var b = GetBook(id);
        if (b == null) return null;
        return new BookDetail
        {
            Id = b.Id, Title = b.Title, Author = b.Author, Format = b.Format,
            FileSize = b.FileSize, AddedAt = Ms(b.AddedAt), LastReadAt = NMs(b.LastReadAt),
            Progress = b.Progress, Location = b.Location, FilePath = b.FilePath, SourcePath = b.SourcePath,
            HasCover = GetCoverFile(b.Id) != null, CoverVersion = b.CoverVersion,
            Notes = countNotes(b.Id), Missing = !File.Exists(b.FilePath),
            Favorite = b.Favorite, Category = b.Category, Description = b.Description,
            Publisher = b.Publisher, Language = b.Language, ReadingSeconds = b.ReadingSeconds,
            Finished = b.FinishedAt != null || b.Progress >= 0.995,
        };
    }

    public string[] TitleOrder()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT id FROM books ORDER BY title COLLATE NOCASE, id;");
            using var r = cmd.ExecuteReader();
            var list = new List<string>();
            while (r.Read()) list.Add(r.GetString(0));
            return list.ToArray();
        }
    }

    public BookRecord[] Snapshot()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, $"SELECT {BookCols} FROM books;");
            using var r = cmd.ExecuteReader();
            var list = new List<BookRecord>();
            while (r.Read()) list.Add(ReadBook(r));
            return list.ToArray();
        }
    }

    public HashSet<string> AllFilePaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT file_path FROM books;");
            using var r = cmd.ExecuteReader();
            while (r.Read()) set.Add(r.GetString(0));
        }
        return set;
    }

    public List<(string Id, string FilePath)> AllPaths()
    {
        var list = new List<(string, string)>();
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT id, file_path FROM books;");
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        }
        return list;
    }

    public List<BookRecord> ListForCovers()
    {
        var list = new List<BookRecord>();
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT id,title,file_path,format,cover_file,cover_ext,cover_version FROM books;");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BookRecord
                {
                    Id = r.GetString(0), Title = r.GetString(1), FilePath = r.GetString(2),
                    Format = r.GetString(3), CoverFile = Str(r, 4), CoverExt = Str(r, 5),
                    CoverVersion = (int)Lng(r, 6),
                });
            }
        }
        return list;
    }

    public void StreamNnBooks(Utf8JsonWriter writer, string baseUrl)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT id,title,author,format,added_at,last_read_at,progress FROM books ORDER BY COALESCE(last_read_at, added_at) DESC;");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dto = new NnBookDto
                {
                    Hash = r.GetString(0),
                    Title = r.GetString(1),
                    Author = r.GetString(2),
                    Format = BookFormatEx.NnFormat(r.GetString(3)),
                    CreatedAt = Lng(r, 4),
                    UpdatedAt = NLng(r, 5) ?? Lng(r, 4),
                    Progress = Dbl(r, 6) > 0 ? new[] { Math.Round(Dbl(r, 6) * 100, 2), 100.0 } : null,
                    CoverImageUrl = baseUrl + "/cover/" + r.GetString(0),
                };
                JsonSerializer.Serialize(writer, dto, Info<NnBookDto>.I);
            }
        }
    }

    public StatsDto BuildStats()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            var dto = new StatsDto();
            using (var cmd = Cmd(cn, """
SELECT COUNT(*),
  COALESCE(SUM(CASE WHEN finished_at IS NOT NULL OR progress >= 0.995 THEN 1 ELSE 0 END),0),
  COALESCE(SUM(CASE WHEN finished_at IS NULL AND progress < 0.995 AND progress > 0 THEN 1 ELSE 0 END),0),
  COALESCE(SUM(CASE WHEN finished_at IS NULL AND progress <= 0 THEN 1 ELSE 0 END),0),
  COALESCE(SUM(favorite),0)
FROM books;
"""))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    dto.TotalBooks = (int)r.GetInt64(0);
                    dto.FinishedBooks = (int)r.GetInt64(1);
                    dto.ReadingBooks = (int)r.GetInt64(2);
                    dto.UnreadBooks = (int)r.GetInt64(3);
                    dto.Favorites = (int)r.GetInt64(4);
                }
            }
            dto.Formats = GroupCounts(cn, "SELECT format, COUNT(*) c FROM books GROUP BY format ORDER BY c DESC LIMIT 20;");
            using (var n = Cmd(cn, "SELECT COUNT(*) FROM notes WHERE deleted_at IS NULL;"))
                dto.Notes = Convert.ToInt32(n.ExecuteScalar() ?? 0);
            var s = ReadSettings(cn);
            dto.ReadingSeconds = s.ReadingSeconds;
            dto.RecentDays = s.DailyReading.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .TakeLast(14)
                .Select(kv => new CountItem { Name = kv.Key, Count = (int)Math.Min(int.MaxValue, kv.Value / 60) })
                .ToList();
            return dto;
        }
    }

    public int CountMissing()
    {
        var paths = AllPaths();
        int n = 0;
        foreach (var (_, path) in paths)
            if (!File.Exists(path)) n++;
        return n;
    }

    public int CountWithoutCover()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT COUNT(*) FROM books WHERE cover_file IS NULL OR cover_file = '';");
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
    }

    public List<BookRecord> ListForMetadataRepair()
    {
        var list = new List<BookRecord>();
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, """
SELECT id,title,author,file_path,format,added_at FROM books
WHERE (author IS NULL OR author = '') AND format IN ('EPUB','MOBI','AZW3','PDF')
ORDER BY added_at DESC;
""");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BookRecord
                {
                    Id = r.GetString(0), Title = r.GetString(1), Author = r.GetString(2),
                    FilePath = r.GetString(3), Format = r.GetString(4),
                    AddedAt = FromMs(Lng(r, 5)),
                });
            }
        }
        return list;
    }

    /// <summary>批量补齐元数据：只填空字段，不覆盖用户已编辑/已有内容。</summary>
    public int UpdateMetadataBatch(IReadOnlyList<BookRecord> books)
    {
        if (books.Count == 0) return 0;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            using var cmd = Cmd(cn, """
UPDATE books SET
  title = CASE WHEN (title IS NULL OR title='') AND $title <> '' THEN $title ELSE title END,
  author = CASE WHEN (author IS NULL OR author='') AND $author <> '' THEN $author ELSE author END,
  publisher = CASE WHEN (publisher IS NULL OR publisher='') THEN NULLIF($publisher,'') ELSE publisher END,
  description = CASE WHEN (description IS NULL OR description='') THEN NULLIF($description,'') ELSE description END,
  language = CASE WHEN (language IS NULL OR language='') THEN NULLIF($language,'') ELSE language END,
  category = CASE WHEN (category IS NULL OR category='') THEN NULLIF($category,'') ELSE category END
WHERE id = $id;
""", tx);
            var pid = cmd.Parameters.Add("$id", SqliteType.Text);
            var ptitle = cmd.Parameters.Add("$title", SqliteType.Text);
            var pauthor = cmd.Parameters.Add("$author", SqliteType.Text);
            var ppub = cmd.Parameters.Add("$publisher", SqliteType.Text);
            var pdesc = cmd.Parameters.Add("$description", SqliteType.Text);
            var plang = cmd.Parameters.Add("$language", SqliteType.Text);
            var pcat = cmd.Parameters.Add("$category", SqliteType.Text);
            int n = 0;
            foreach (var b in books)
            {
                pid.Value = b.Id;
                ptitle.Value = b.Title ?? "";
                pauthor.Value = b.Author ?? "";
                ppub.Value = (object?)b.Publisher ?? DBNull.Value;
                pdesc.Value = (object?)b.Description ?? DBNull.Value;
                plang.Value = (object?)b.Language ?? DBNull.Value;
                pcat.Value = (object?)b.Category ?? DBNull.Value;
                try { n += cmd.ExecuteNonQuery(); } catch { }
            }
            if (n > 0) BumpVersion(cn, tx);
            tx.Commit();
            return n;
        }
    }

    // ==================== 配置 / 笔记 ====================

    public ConfigRecord? GetConfig(string bookId)
    {
        if (string.IsNullOrEmpty(bookId)) return null;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, """
SELECT book_id,config_json,progress,location,view_settings,pdf_layout,pdf_zoom,pdf_auto_page,updated_at
FROM configs WHERE book_id=$id;
""");
            P(cmd, "$id", bookId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new ConfigRecord
            {
                BookId = r.GetString(0),
                ConfigJson = Str(r, 1),
                Progress = Str(r, 2),
                Location = Str(r, 3),
                ViewSettings = Str(r, 4),
                PdfLayout = Str(r, 5),
                PdfZoom = Str(r, 6),
                PdfAutoPage = r.IsDBNull(7) ? null : r.GetInt64(7) != 0,
                UpdatedAt = FromMs(Lng(r, 8)),
            };
        }
    }

    public void PutConfig(ConfigRecord cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.BookId)) return;
        cfg.UpdatedAt = DateTime.Now;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            UpsertConfig(cn, tx, cfg);
            tx.Commit();
        }
    }

    private static void UpsertConfig(SqliteConnection cn, SqliteTransaction tx, ConfigRecord cfg)
    {
        var hash = string.IsNullOrEmpty(cfg.ConfigJson)
            ? null
            : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(cfg.ConfigJson)))
                .ToLowerInvariant();
        string? oldHash = null;
        using (var get = Cmd(cn, "SELECT config_hash FROM configs WHERE book_id=$id;", tx))
        {
            P(get, "$id", cfg.BookId);
            oldHash = get.ExecuteScalar() as string;
        }
        using (var cmd = Cmd(cn, """
INSERT INTO configs(book_id,config_json,config_hash,progress,location,view_settings,pdf_layout,pdf_zoom,pdf_auto_page,updated_at)
VALUES($id,$json,$hash,$progress,$location,$view,$layout,$zoom,$auto,$updated)
ON CONFLICT(book_id) DO UPDATE SET
  config_json = COALESCE(excluded.config_json, configs.config_json),
  config_hash = COALESCE(excluded.config_hash, configs.config_hash),
  progress = COALESCE(excluded.progress, configs.progress),
  location = COALESCE(excluded.location, configs.location),
  view_settings = COALESCE(excluded.view_settings, configs.view_settings),
  pdf_layout = COALESCE(excluded.pdf_layout, configs.pdf_layout),
  pdf_zoom = COALESCE(excluded.pdf_zoom, configs.pdf_zoom),
  pdf_auto_page = COALESCE(excluded.pdf_auto_page, configs.pdf_auto_page),
  updated_at = excluded.updated_at;
""", tx))
        {
            P(cmd, "$id", cfg.BookId);
            P(cmd, "$json", cfg.ConfigJson);
            P(cmd, "$hash", hash);
            P(cmd, "$progress", cfg.Progress);
            P(cmd, "$location", cfg.Location);
            P(cmd, "$view", cfg.ViewSettings);
            P(cmd, "$layout", cfg.PdfLayout);
            P(cmd, "$zoom", cfg.PdfZoom);
            P(cmd, "$auto", cfg.PdfAutoPage == null ? null : (cfg.PdfAutoPage.Value ? 1 : 0));
            P(cmd, "$updated", Ms(cfg.UpdatedAt));
            cmd.ExecuteNonQuery();
        }
        if (hash != null && hash != oldHash && cfg.ConfigJson != null)
            SyncNotes(cn, tx, cfg.BookId, cfg.ConfigJson);
    }

    private static void SyncNotes(SqliteConnection cn, SqliteTransaction tx, string bookId, string configJson)
    {
        using (var del = Cmd(cn, "DELETE FROM notes WHERE book_id=$id;", tx))
        {
            P(del, "$id", bookId);
            del.ExecuteNonQuery();
        }
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("booknotes", out var notes) || notes.ValueKind != JsonValueKind.Array)
                return;
            using var ins = Cmd(cn, """
INSERT OR REPLACE INTO notes(id,book_id,type,cfi,text,note,style,color,page,progress,label,chapter,created_at,updated_at,deleted_at)
VALUES($id,$book,$type,$cfi,$text,$note,$style,$color,$page,$progress,$label,$chapter,$created,$updated,$deleted);
""", tx);
            var pid = ins.Parameters.Add("$id", SqliteType.Text);
            var pbook = ins.Parameters.Add("$book", SqliteType.Text);
            var ptype = ins.Parameters.Add("$type", SqliteType.Text);
            var pcfi = ins.Parameters.Add("$cfi", SqliteType.Text);
            var ptext = ins.Parameters.Add("$text", SqliteType.Text);
            var pnote = ins.Parameters.Add("$note", SqliteType.Text);
            var pstyle = ins.Parameters.Add("$style", SqliteType.Text);
            var pcolor = ins.Parameters.Add("$color", SqliteType.Text);
            var ppage = ins.Parameters.Add("$page", SqliteType.Integer);
            var pprog = ins.Parameters.Add("$progress", SqliteType.Real);
            var plabel = ins.Parameters.Add("$label", SqliteType.Text);
            var pchapter = ins.Parameters.Add("$chapter", SqliteType.Text);
            var pcreated = ins.Parameters.Add("$created", SqliteType.Integer);
            var pupdated = ins.Parameters.Add("$updated", SqliteType.Integer);
            var pdeleted = ins.Parameters.Add("$deleted", SqliteType.Integer);
            foreach (var it in notes.EnumerateArray())
            {
                var id = GetStr(it, "id");
                if (string.IsNullOrEmpty(id)) continue;
                pid.Value = id;
                pbook.Value = bookId;
                ptype.Value = GetStr(it, "type") ?? "annotation";
                pcfi.Value = GetStr(it, "cfi") ?? "";
                ptext.Value = (object?)GetStr(it, "text") ?? (object?)GetStr(it, "annotatedText") ?? DBNull.Value;
                pnote.Value = GetStr(it, "note") ?? "";
                pstyle.Value = (object?)GetStr(it, "style") ?? DBNull.Value;
                pcolor.Value = (object?)GetStr(it, "color") ?? DBNull.Value;
                ppage.Value = it.TryGetProperty("page", out var pg) && pg.TryGetInt64(out var pv) ? pv : DBNull.Value;
                pprog.Value = it.TryGetProperty("progress", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetDouble() : 0;
                plabel.Value = (object?)(GetStr(it, "label") ?? GetStr(it, "chapter")) ?? DBNull.Value;
                pchapter.Value = (object?)GetStr(it, "chapter") ?? DBNull.Value;
                pcreated.Value = GetLong(it, "createdAt");
                pupdated.Value = GetLong(it, "updatedAt");
                pdeleted.Value = GetDeletedAt(it) ?? (object)DBNull.Value;
                try { ins.ExecuteNonQuery(); } catch { }
            }
        }
        catch { }
    }

    private static string? GetStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long GetLong(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return 0;
    }

    private static long? GetDeletedAt(JsonElement e)
    {
        if (!e.TryGetProperty("deletedAt", out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => null,
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : NowMs(),
            JsonValueKind.True => NowMs(),
            JsonValueKind.String => long.TryParse(v.GetString(), out var s) ? s : NowMs(),
            _ => NowMs(),
        };
    }

    public bool UpdateConfig(string bookId, Action<ConfigRecord> mutate)
    {
        var existing = GetConfig(bookId) ?? new ConfigRecord { BookId = bookId };
        mutate(existing);
        PutConfig(existing);
        return true;
    }

    public void RemoveConfig(string bookId)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            using (var cmd = Cmd(cn, "DELETE FROM configs WHERE book_id=$id; DELETE FROM notes WHERE book_id=$id;", tx))
            {
                P(cmd, "$id", bookId);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public List<ConfigRecord> AllConfigs()
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, """
SELECT book_id,config_json,progress,location,view_settings,pdf_layout,pdf_zoom,pdf_auto_page,updated_at FROM configs;
""");
            using var r = cmd.ExecuteReader();
            var list = new List<ConfigRecord>();
            while (r.Read())
            {
                list.Add(new ConfigRecord
                {
                    BookId = r.GetString(0), ConfigJson = Str(r, 1), Progress = Str(r, 2),
                    Location = Str(r, 3), ViewSettings = Str(r, 4), PdfLayout = Str(r, 5),
                    PdfZoom = Str(r, 6), PdfAutoPage = r.IsDBNull(7) ? null : r.GetInt64(7) != 0,
                    UpdatedAt = FromMs(Lng(r, 8)),
                });
            }
            return list;
        }
    }

    public int CountNotes(string bookId)
    {
        if (string.IsNullOrEmpty(bookId)) return 0;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT COUNT(*) FROM notes WHERE book_id=$id AND deleted_at IS NULL;");
            P(cmd, "$id", bookId);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
    }

    /// <summary>直接更新笔记行（配置 JSON 不存在时的兜底）。</summary>
    public bool UpdateNoteRow(string noteId, string? note, bool remove)
    {
        if (string.IsNullOrEmpty(noteId)) return false;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = remove
                ? Cmd(cn, "UPDATE notes SET deleted_at=$u, updated_at=$u WHERE id=$id;")
                : Cmd(cn, "UPDATE notes SET note=$n, updated_at=$u WHERE id=$id;");
            var now = NowMs();
            if (!remove) P(cmd, "$n", note ?? "");
            P(cmd, "$u", now);
            P(cmd, "$id", noteId);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public string? FindNoteBook(string noteId)
    {
        if (string.IsNullOrEmpty(noteId)) return null;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT book_id FROM notes WHERE id=$id;");
            P(cmd, "$id", noteId);
            return cmd.ExecuteScalar() as string;
        }
    }

    public List<NoteView> BuildNotes(string? bookFilter)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            var sql = """
SELECT n.id,n.book_id,COALESCE(b.title,''),n.type,n.cfi,n.text,n.note,n.style,n.color,n.page,n.progress,
       n.label,n.chapter,n.created_at,n.updated_at
FROM notes n LEFT JOIN books b ON b.id = n.book_id
WHERE n.deleted_at IS NULL
""";
            if (!string.IsNullOrEmpty(bookFilter)) sql += " AND n.book_id = $book";
            sql += " ORDER BY CASE WHEN n.updated_at > 0 THEN n.updated_at ELSE n.created_at END DESC LIMIT 5000;";
            using var cmd = Cmd(cn, sql);
            if (!string.IsNullOrEmpty(bookFilter)) P(cmd, "$book", bookFilter);
            using var r = cmd.ExecuteReader();
            var list = new List<NoteView>();
            while (r.Read())
            {
                list.Add(new NoteView
                {
                    Id = r.GetString(0), BookId = r.GetString(1), BookTitle = r.GetString(2),
                    Type = r.GetString(3), Cfi = r.GetString(4), Text = Str(r, 5),
                    Note = r.GetString(6), Style = Str(r, 7), Color = Str(r, 8),
                    Page = r.IsDBNull(9) ? null : (int)r.GetInt64(9), Progress = Dbl(r, 10),
                    Label = Str(r, 11), Chapter = Str(r, 12),
                    CreatedAt = Lng(r, 13), UpdatedAt = Lng(r, 14),
                });
            }
            return list;
        }
    }

    public static int CountNotesInJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("booknotes", out var bn) || bn.ValueKind != JsonValueKind.Array)
                return 0;
            int n = 0;
            foreach (var it in bn.EnumerateArray())
            {
                if (it.TryGetProperty("deletedAt", out var da) &&
                    da.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                    continue;
                n++;
            }
            return n;
        }
        catch { return 0; }
    }

    // ==================== 封面 / 缩略图 ====================

    public string? GetCoverFile(string bookId)
    {
        if (string.IsNullOrEmpty(bookId)) return null;
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            string? path = null;
            using (var cmd = Cmd(cn, "SELECT cover_file FROM books WHERE id=$id;"))
            {
                P(cmd, "$id", bookId);
                path = cmd.ExecuteScalar() as string;
            }
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" })
            {
                var p = Path.Combine(CoversDir, bookId + ext);
                if (!File.Exists(p)) continue;
                using var up = Cmd(cn, "UPDATE books SET cover_file=$p, cover_ext=$e WHERE id=$id;");
                P(up, "$p", p); P(up, "$e", ext); P(up, "$id", bookId);
                up.ExecuteNonQuery();
                return p;
            }
            return null;
        }
    }

    public string CoverPathFor(string bookId, string ext)
    {
        ext = ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            string? old = null;
            using (var cmd = Cmd(cn, "SELECT cover_ext FROM books WHERE id=$id;"))
            {
                P(cmd, "$id", bookId);
                old = cmd.ExecuteScalar() as string;
            }
            if (!string.IsNullOrEmpty(old) && !string.Equals(old, ext, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(Path.Combine(CoversDir, bookId + old)); } catch { }
            }
            return Path.Combine(CoversDir, bookId + ext);
        }
    }

    public void RemoveCover(string bookId)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            string? path = null;
            using (var cmd = Cmd(cn, "SELECT cover_file FROM books WHERE id=$id;"))
            {
                P(cmd, "$id", bookId);
                path = cmd.ExecuteScalar() as string;
            }
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var full = Path.GetFullPath(path);
                    var root = Path.GetFullPath(CoversDir);
                    if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        File.Delete(full);
                }
                catch { }
            }
            using (var up = Cmd(cn, "UPDATE books SET cover_file=NULL, cover_ext=NULL WHERE id=$id;"))
            {
                P(up, "$id", bookId);
                up.ExecuteNonQuery();
            }
            try { File.Delete(ThumbPath(bookId)); } catch { }
            try { File.Delete(NoCoverMarker(bookId)); } catch { }
        }
    }

    public void BumpCover(string bookId)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            using (var cmd = Cmd(cn, "UPDATE books SET cover_version = cover_version + 1 WHERE id=$id;", tx))
            {
                P(cmd, "$id", bookId);
                cmd.ExecuteNonQuery();
            }
            BumpVersion(cn, tx);
            tx.Commit();
        }
    }

    /// <summary>维护用：封面被压缩/转换格式后更新记录（不递增全库版本，避免书架整页刷新）。</summary>
    public void UpdateCoverMeta(string bookId, string? path, string? ext)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "UPDATE books SET cover_file=$p, cover_ext=$e, cover_version = cover_version + 1 WHERE id=$id;");
            P(cmd, "$p", path); P(cmd, "$e", ext); P(cmd, "$id", bookId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>尚未提取封面的 EPUB/PDF/MOBI/AZW3（后台补齐封面用）。</summary>
    public List<BookRecord> ListMissingCoverable(int limit = 100000)
    {
        var list = new List<BookRecord>();
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var cmd = Cmd(cn, "SELECT id,title,file_path,format,cover_file,cover_ext,cover_version FROM books WHERE (cover_file IS NULL OR cover_file='') AND format IN ('EPUB','PDF','MOBI','AZW3') LIMIT $limit;");
            P(cmd, "$limit", Math.Max(1, limit));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BookRecord
                {
                    Id = r.GetString(0), Title = r.GetString(1), FilePath = r.GetString(2),
                    Format = r.GetString(3), CoverFile = Str(r, 4), CoverExt = Str(r, 5),
                    CoverVersion = (int)Lng(r, 6),
                });
            }
        }
        return list;
    }

    public string ThumbPath(string bookId) => Path.Combine(ThumbsDir, bookId + ".jpg");
    public string NoCoverMarker(string bookId) => Path.Combine(ThumbsDir, bookId + ".none");

    private void ScanCovers()
    {
        try
        {
            var files = Directory.GetFiles(CoversDir);
            lock (Database.Instance.SyncRoot)
            {
                using var cn = Database.Instance.Open();
                using var tx = cn.BeginTransaction();
                int n = 0, cleared = 0;

                // 外部清理过 covers 目录时，把指向已不存在文件的记录清空，
                // 让书架显示占位/缺省封面，并允许后台重新提取。
                var stale = new List<string>();
                using (var q = Cmd(cn, "SELECT id, cover_file FROM books WHERE cover_file IS NOT NULL AND cover_file <> '';", tx))
                using (var r = q.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var p = r.IsDBNull(1) ? null : r.GetString(1);
                        if (string.IsNullOrEmpty(p) || !File.Exists(p)) stale.Add(r.GetString(0));
                    }
                }
                if (stale.Count > 0)
                {
                    using var clear = Cmd(cn, "UPDATE books SET cover_file=NULL, cover_ext=NULL WHERE id=$id;", tx);
                    var ci = clear.Parameters.Add("$id", SqliteType.Text);
                    foreach (var id in stale)
                    {
                        ci.Value = id;
                        cleared += clear.ExecuteNonQuery();
                    }
                }

                using var up = Cmd(cn, "UPDATE books SET cover_file=$p, cover_ext=$e WHERE id=$id AND (cover_file IS NULL OR cover_file='');", tx);
                var pp = up.Parameters.Add("$p", SqliteType.Text);
                var pe = up.Parameters.Add("$e", SqliteType.Text);
                var pi = up.Parameters.Add("$id", SqliteType.Text);
                foreach (var f in files)
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (!IsImageExt(ext)) continue;
                    if (name == "_default")
                    {
                        using var def = Cmd(cn, "UPDATE app_settings SET default_cover_path=$p WHERE id=1 AND (default_cover_path IS NULL OR default_cover_path='');", tx);
                        P(def, "$p", f); def.ExecuteNonQuery();
                        continue;
                    }
                    pp.Value = f; pe.Value = ext; pi.Value = name;
                    n += up.ExecuteNonQuery();
                }
                tx.Commit();
                if (n > 0) Log.Info($"封面索引: {n} 本");
                if (cleared > 0) Log.Info($"清理失效封面引用: {cleared} 本");
            }
        }
        catch (Exception ex) { Log.Warn("扫描封面目录失败: " + ex.Message); }
    }

    private void CleanupLegacyCoverMarkers()
    {
        try
        {
            int n = 0;
            foreach (var f in Directory.GetFiles(ThumbsDir, "*.none"))
            {
                try
                {
                    if (!string.Equals(File.ReadAllText(f).Trim(), ThumbnailService.MarkerVersion, StringComparison.Ordinal))
                    {
                        File.Delete(f);
                        n++;
                    }
                }
                catch { }
            }
            if (n > 0) Log.Info($"清理旧版无封面标记 {n} 个");
        }
        catch { }
    }

    public static bool IsImageExt(string ext) =>
        ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".avif";

    // ==================== 应用设置 ====================

    public AppSettings AppSettings
    {
        get
        {
            lock (Database.Instance.SyncRoot)
            {
                using var cn = Database.Instance.Open();
                return ReadSettings(cn);
            }
        }
    }

    private static AppSettings ReadSettings(SqliteConnection cn)
    {
        var s = new AppSettings();
        using (var cmd = Cmd(cn, "SELECT * FROM app_settings WHERE id=1;"))
        using (var r = cmd.ExecuteReader())
        {
            if (r.Read())
            {
                int O(string n) => r.GetOrdinal(n);
                string S(string n) => r.IsDBNull(O(n)) ? "" : r.GetString(O(n));
                long L(string n) => r.IsDBNull(O(n)) ? 0 : r.GetInt64(O(n));
                bool B(string n) => L(n) != 0;
                double D(string n) => r.IsDBNull(O(n)) ? 0 : r.GetDouble(O(n));
                s.AppTheme = Empty(S("app_theme"), "dark");
                s.SortMode = Empty(S("sort_mode"), "recent");
                s.SortDesc = B("sort_desc");
                s.ShowCovers = B("show_covers");
                s.LibraryView = Empty(S("library_view"), "grid");
                s.GridSize = (int)Math.Clamp(L("grid_size"), 96, 320);
                s.ReaderSettingsJson = Empty(S("reader_settings_json"), "{}");
                s.DefaultCoverPath = S("default_cover_path");
                s.ImportCovers = B("import_covers");
                s.UseDefaultCover = B("use_default_cover");
                s.AutoRefreshLibrary = B("auto_refresh_library");
                s.LastFolder = S("last_folder");
                s.ReadingSeconds = L("reading_seconds");
                s.Media = new MediaSettings
                {
                    ImageSlideSeconds = SlideSeconds(D("media_image_slide_seconds")),
                    ComicMode = B("media_comic_mode"),
                    ComicWidth = Empty(S("media_comic_width"), "default"),
                    ComicAutoPage = B("media_comic_auto_page"),
                    RecursiveImages = B("media_recursive_images"),
                    MusicBackgroundSeconds = (int)Math.Clamp(L("media_music_background_seconds"), 1, 60),
                    MusicShowLyrics = B("media_music_show_lyrics"),
                    VideoAutoResume = B("media_video_auto_resume"),
                    ImageTransition = Empty(S("media_image_transition"), "fade"),
                    ImageFitMode = Empty(S("media_image_fit"), "contain"),
                    ImageBackgroundMusicFolder = S("media_image_bgm_folder").Length == 0 ? null : S("media_image_bgm_folder"),
                    ImageBackgroundMusicIndex = Math.Max(0, (int)L("media_image_bgm_index")),
                    ImageBackgroundMusic = S("media_image_bgm").Length == 0 ? null : S("media_image_bgm"),
                    ImageSlideLoop = L("media_image_slide_loop") != 0,
                    MusicVolume = Math.Clamp(D("media_music_volume"), 0, 1),
                    MusicMode = Empty(S("media_music_mode"), "order"),
                    MusicTab = Empty(S("media_music_tab"), "list"),
                    VideoVolume = Math.Clamp(D("media_video_volume"), 0, 1),
                    VideoShowList = L("media_video_show_list") != 0,
                    MusicBackgroundFolder = S("media_music_bg_folder").Length == 0 ? null : S("media_music_bg_folder"),
                    MusicBackgroundIndex = (int)Math.Max(0, L("media_music_bg_index")),
                    ImageLastFolder = S("media_image_last_folder").Length == 0 ? null : S("media_image_last_folder"),
                };
                s.ShelfWindow = new WindowState
                {
                    Width = (int)Math.Max(400, L("shelf_w")), Height = (int)Math.Max(300, L("shelf_h")),
                    X = (int)L("shelf_x"), Y = (int)L("shelf_y"), HasPosition = B("shelf_pos"), Maximized = B("shelf_max"),
                };
                s.ReaderWindow = new WindowState
                {
                    Width = (int)Math.Max(400, L("reader_w")), Height = (int)Math.Max(300, L("reader_h")),
                    X = (int)L("reader_x"), Y = (int)L("reader_y"), HasPosition = B("reader_pos"), Maximized = B("reader_max"),
                };
                s.ViewWindow = new WindowState
                {
                    Width = (int)Math.Max(400, L("view_w")), Height = (int)Math.Max(300, L("view_h")),
                    X = (int)L("view_x"), Y = (int)L("view_y"), HasPosition = B("view_pos"), Maximized = B("view_max"),
                };
                s.BrowserWindow = new WindowState
                {
                    Width = (int)Math.Max(400, L("browser_w")), Height = (int)Math.Max(300, L("browser_h")),
                    X = (int)L("browser_x"), Y = (int)L("browser_y"), HasPosition = B("browser_pos"), Maximized = B("browser_max"),
                };
            }
        }
        using (var cmd = Cmd(cn, "SELECT path FROM recent_folders ORDER BY updated_at DESC LIMIT 16;"))
        using (var r = cmd.ExecuteReader())
        {
            s.RecentFolders = new List<string>();
            while (r.Read()) s.RecentFolders.Add(r.GetString(0));
        }
        using (var cmd = Cmd(cn, "SELECT day, seconds FROM daily_reading ORDER BY day DESC LIMIT 120;"))
        using (var r = cmd.ExecuteReader())
        {
            s.DailyReading = new Dictionary<string, long>();
            while (r.Read()) s.DailyReading[r.GetString(0)] = r.GetInt64(1);
        }
        return s;
    }

    private static string Empty(string v, string fallback) => string.IsNullOrWhiteSpace(v) ? fallback : v;

    private static double SlideSeconds(double v) => v <= 0 ? 5 : Math.Clamp(v, 0.1, 60);

    public void UpdateAppSettings(Action<AppSettings> mutate) => UpdateAppSettingsCore(mutate);

    public void UpdateAppSettingsDeferred(Action<AppSettings> mutate) => UpdateAppSettingsCore(mutate);

    private void UpdateAppSettingsCore(Action<AppSettings> mutate)
    {
        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            using var tx = cn.BeginTransaction();
            var s = ReadSettings(cn);
            mutate(s);
            WriteSettings(cn, tx, s);
            tx.Commit();
        }
    }

    private static void WriteSettings(SqliteConnection cn, SqliteTransaction tx, AppSettings s)
    {
        s.Media ??= new MediaSettings();
        s.ShelfWindow ??= new WindowState(1360, 900);
        s.ReaderWindow ??= new WindowState(1120, 860);
        s.ViewWindow ??= new WindowState(1280, 820);
        s.BrowserWindow ??= new WindowState(1280, 820);
        using (var cmd = Cmd(cn, """
UPDATE app_settings SET
 app_theme=$theme, sort_mode=$sort, sort_desc=$desc, show_covers=$covers, library_view=$view,
 grid_size=$grid, reader_settings_json=$reader, default_cover_path=$defaultCover, import_covers=$importCovers,
 use_default_cover=$useDefault, auto_refresh_library=$autoRefresh, last_folder=$lastFolder,
 reading_seconds=$reading, shelf_w=$sw, shelf_h=$sh, shelf_x=$sx, shelf_y=$sy, shelf_pos=$sp, shelf_max=$sm,
 reader_w=$rw, reader_h=$rh, reader_x=$rx, reader_y=$ry, reader_pos=$rp, reader_max=$rm,
 view_w=$vw, view_h=$vh, view_x=$vx, view_y=$vy, view_pos=$vp, view_max=$vm,
 browser_w=$bw, browser_h=$bh, browser_x=$bx, browser_y=$by, browser_pos=$bp, browser_max=$bm,
 media_image_slide_seconds=$m1, media_comic_mode=$m2, media_comic_width=$m3, media_comic_auto_page=$m4,
 media_recursive_images=$m5, media_music_background_seconds=$m6, media_music_show_lyrics=$m7, media_video_auto_resume=$m8,
 media_image_transition=$m9, media_image_bgm=$m10, media_image_slide_loop=$m11,
 media_music_volume=$m12, media_music_mode=$m13, media_music_tab=$m14,
 media_video_volume=$m15, media_video_show_list=$m16,
 media_music_bg_folder=$m17, media_music_bg_index=$m18, media_image_last_folder=$m19,
 media_image_fit=$m20, media_image_bgm_folder=$m21, media_image_bgm_index=$m22
WHERE id=1;
""", tx))
        {
            P(cmd, "$theme", s.AppTheme);
            P(cmd, "$sort", s.SortMode);
            P(cmd, "$desc", s.SortDesc ? 1 : 0);
            P(cmd, "$covers", s.ShowCovers ? 1 : 0);
            P(cmd, "$view", s.LibraryView);
            P(cmd, "$grid", s.GridSize);
            P(cmd, "$reader", string.IsNullOrEmpty(s.ReaderSettingsJson) ? "{}" : s.ReaderSettingsJson);
            P(cmd, "$defaultCover", s.DefaultCoverPath);
            P(cmd, "$importCovers", s.ImportCovers ? 1 : 0);
            P(cmd, "$useDefault", s.UseDefaultCover ? 1 : 0);
            P(cmd, "$autoRefresh", s.AutoRefreshLibrary ? 1 : 0);
            P(cmd, "$lastFolder", s.LastFolder);
            P(cmd, "$reading", s.ReadingSeconds);
            var sw = s.ShelfWindow; var rw = s.ReaderWindow; var vw = s.ViewWindow; var bw = s.BrowserWindow;
            P(cmd, "$sw", sw.Width); P(cmd, "$sh", sw.Height); P(cmd, "$sx", sw.X); P(cmd, "$sy", sw.Y);
            P(cmd, "$sp", sw.HasPosition ? 1 : 0); P(cmd, "$sm", sw.Maximized ? 1 : 0);
            P(cmd, "$rw", rw.Width); P(cmd, "$rh", rw.Height); P(cmd, "$rx", rw.X); P(cmd, "$ry", rw.Y);
            P(cmd, "$rp", rw.HasPosition ? 1 : 0); P(cmd, "$rm", rw.Maximized ? 1 : 0);
            P(cmd, "$vw", vw.Width); P(cmd, "$vh", vw.Height); P(cmd, "$vx", vw.X); P(cmd, "$vy", vw.Y);
            P(cmd, "$vp", vw.HasPosition ? 1 : 0); P(cmd, "$vm", vw.Maximized ? 1 : 0);
            P(cmd, "$bw", bw.Width); P(cmd, "$bh", bw.Height); P(cmd, "$bx", bw.X); P(cmd, "$by", bw.Y);
            P(cmd, "$bp", bw.HasPosition ? 1 : 0); P(cmd, "$bm", bw.Maximized ? 1 : 0);
            var m = s.Media;
            P(cmd, "$m1", m.ImageSlideSeconds); P(cmd, "$m2", m.ComicMode ? 1 : 0);
            P(cmd, "$m3", m.ComicWidth); P(cmd, "$m4", m.ComicAutoPage ? 1 : 0);
            P(cmd, "$m5", m.RecursiveImages ? 1 : 0); P(cmd, "$m6", m.MusicBackgroundSeconds);
            P(cmd, "$m7", m.MusicShowLyrics ? 1 : 0); P(cmd, "$m8", m.VideoAutoResume ? 1 : 0);
            P(cmd, "$m9", string.IsNullOrWhiteSpace(m.ImageTransition) ? "fade" : m.ImageTransition);
            P(cmd, "$m10", m.ImageBackgroundMusic);
            P(cmd, "$m11", m.ImageSlideLoop ? 1 : 0);
            P(cmd, "$m12", Math.Clamp(m.MusicVolume, 0, 1));
            P(cmd, "$m13", m.MusicMode is "repeat" or "shuffle" ? m.MusicMode : "order");
            P(cmd, "$m14", m.MusicTab == "lyrics" ? "lyrics" : "list");
            P(cmd, "$m15", Math.Clamp(m.VideoVolume, 0, 1));
            P(cmd, "$m16", m.VideoShowList ? 1 : 0);
            P(cmd, "$m17", m.MusicBackgroundFolder);
            P(cmd, "$m18", m.MusicBackgroundIndex);
            P(cmd, "$m19", m.ImageLastFolder);
            P(cmd, "$m20", m.ImageFitMode is "width" or "height" or "center" or "actual" ? m.ImageFitMode : "contain");
            P(cmd, "$m21", m.ImageBackgroundMusicFolder);
            P(cmd, "$m22", Math.Max(0, m.ImageBackgroundMusicIndex));
            cmd.ExecuteNonQuery();
        }
        using (var del = Cmd(cn, "DELETE FROM recent_folders;", tx)) del.ExecuteNonQuery();
        if (s.RecentFolders != null)
        {
            using var ins = Cmd(cn, "INSERT OR REPLACE INTO recent_folders(path, updated_at) VALUES($p, $u);", tx);
            var pp = ins.Parameters.Add("$p", SqliteType.Text);
            var pu = ins.Parameters.Add("$u", SqliteType.Integer);
            var now = NowMs();
            foreach (var p in s.RecentFolders.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(16))
            {
                pp.Value = p; pu.Value = now--;
                try { ins.ExecuteNonQuery(); } catch { }
            }
        }
    }

    private void EnsureDefaults()
    {
        var s = AppSettings;
        if (string.IsNullOrWhiteSpace(s.ReaderSettingsJson) || s.ReaderSettingsJson.Trim() == "{}")
        {
            UpdateAppSettings(x => x.ReaderSettingsJson = DefaultReaderSettings);
        }
    }

    public long NoteConfigSeen(string bookId)
    {
        var now = DateTime.UtcNow;
        lock (_seenLock)
        {
            if (_lastConfigSeen.TryGetValue(bookId, out var last))
            {
                var delta = (long)(now - last).TotalSeconds;
                _lastConfigSeen[bookId] = now;
                if (delta < 1 || delta > 120) return 0;
                return delta;
            }
            _lastConfigSeen[bookId] = now;
            return 0;
        }
    }

    public void FlushDirty() { }

    public void FlushAll()
    {
        Database.Instance.Checkpoint();
        Log.Info("数据已落盘（SQLite checkpoint）");
    }

    // ==================== 旧版 JSON 迁移 ====================

    private void MigrateLegacy()
    {
        var oldRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonAot");
        var oldData = Path.Combine(oldRoot, "data");
        var hasLegacy =
            File.Exists(Path.Combine(oldData, "books.json")) ||
            File.Exists(Path.Combine(oldData, "books.json.bak")) ||
            Directory.Exists(Path.Combine(oldData, "books")) ||
            Directory.Exists(Path.Combine(oldData, "configs")) ||
            File.Exists(Path.Combine(oldData, "configs.json")) ||
            File.Exists(Path.Combine(oldData, "appsettings.json"));
        if (!hasLegacy) return;

        lock (Database.Instance.SyncRoot)
        {
            using var cn = Database.Instance.Open();
            if (GetMeta(cn, "legacy_json") == "1") return;
            Log.Info("检测到旧版 JSON 数据，开始迁移到 SQLite…");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                CopyDir(Path.Combine(oldRoot, "covers"), CoversDir);
                CopyDir(Path.Combine(oldRoot, "thumbs"), ThumbsDir);
                CopyDir(Path.Combine(oldRoot, "LibraryFiles"), LibraryFilesDir);

                using var tx = cn.BeginTransaction();
                int bookCount = MigrateBooks(cn, tx, oldRoot, oldData);
                int cfgCount = MigrateConfigs(cn, tx, oldData);
                MigrateSettings(cn, tx, oldData);
                MigrateMedia(cn, tx, oldData);
                SetMeta(cn, tx, "legacy_json", "1");
                SetMeta(cn, tx, "legacy_migrated_at", DateTime.Now.ToString("s"));
                tx.Commit();
                Log.Info($"JSON→SQLite 迁移完成: {bookCount} 本，{cfgCount} 个配置，耗时 {sw.ElapsedMilliseconds}ms（旧文件保留在 {oldRoot}）");
            }
            catch (Exception ex)
            {
                Log.Error("旧版 JSON 迁移失败（旧文件未删除）", ex);
            }
        }
    }

    private static string? GetMeta(SqliteConnection cn, string key)
    {
        using var cmd = Cmd(cn, "SELECT value FROM meta WHERE key=$k;");
        P(cmd, "$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void SetMeta(SqliteConnection cn, SqliteTransaction tx, string key, string value)
    {
        using var cmd = Cmd(cn, "INSERT OR REPLACE INTO meta(key,value) VALUES($k,$v);", tx);
        P(cmd, "$k", key); P(cmd, "$v", value);
        cmd.ExecuteNonQuery();
    }

    private int MigrateBooks(SqliteConnection cn, SqliteTransaction tx, string oldRoot, string oldData)
    {
        using var check = Cmd(cn, "SELECT COUNT(*) FROM books;", tx);
        if (Convert.ToInt32(check.ExecuteScalar() ?? 0) > 0) return 0;

        var records = new List<BookRecord>();
        var shardDir = Path.Combine(oldData, "books");
        if (Directory.Exists(shardDir))
        {
            foreach (var f in Directory.GetFiles(shardDir, "*.json"))
            {
                try
                {
                    using var fs = File.OpenRead(f);
                    var list = JsonSerializer.Deserialize(fs, Info<List<BookRecord>>.I);
                    if (list != null) records.AddRange(list);
                }
                catch (Exception ex) { Log.Warn("读取旧书库分片失败 " + f + ": " + ex.Message); }
            }
        }
        if (records.Count == 0)
        {
            var legacyFile = Path.Combine(oldData, "books.json");
            if (!File.Exists(legacyFile)) legacyFile = Path.Combine(oldData, "books.json.bak");
            if (File.Exists(legacyFile))
            {
                try
                {
                    using var fs = File.OpenRead(legacyFile);
                    var list = JsonSerializer.Deserialize(fs, Info<List<BookRecord>>.I);
                    if (list != null) records.AddRange(list);
                }
                catch (Exception ex) { Log.Warn("读取旧 books.json 失败: " + ex.Message); }
            }
        }
        if (records.Count == 0) return 0;

        var oldLibrary = Path.Combine(oldRoot, "LibraryFiles");
        var oldCovers = Path.Combine(oldRoot, "covers");
        foreach (var b in records)
        {
            if (b.AddedAt.Year < 1900) b.AddedAt = DateTime.Now;
            if (string.IsNullOrEmpty(b.SourcePath) && !string.IsNullOrEmpty(b.FilePath) &&
                b.FilePath.StartsWith(oldLibrary, StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(b.FilePath);
                var newPath = Path.Combine(LibraryFilesDir, name);
                try
                {
                    if (!File.Exists(newPath) && File.Exists(b.FilePath))
                    {
                        Directory.CreateDirectory(LibraryFilesDir);
                        File.Copy(b.FilePath, newPath, true);
                    }
                    b.FilePath = newPath;
                }
                catch (Exception ex) { Log.Warn("迁移导入副本失败 " + b.FilePath + ": " + ex.Message); }
            }
            if (!string.IsNullOrEmpty(b.CoverFile) && b.CoverFile.StartsWith(oldCovers, StringComparison.OrdinalIgnoreCase))
            {
                var newCover = Path.Combine(CoversDir, Path.GetFileName(b.CoverFile));
                if (File.Exists(newCover)) b.CoverFile = newCover;
            }
        }
        Log.Info($"迁移书籍记录 {records.Count} 本…");
        var added = InsertBooks(cn, tx, records);
        if (added == 0 && records.Count > 0)
            throw new InvalidOperationException("所有书籍记录插入失败，已回滚以便下次重试");
        if (added < records.Count)
            Log.Warn($"部分书籍未插入（重复路径或错误）: 成功 {added} / 共 {records.Count}");
        return added;
    }

    private int MigrateConfigs(SqliteConnection cn, SqliteTransaction tx, string oldData)
    {
        using var check = Cmd(cn, "SELECT COUNT(*) FROM configs;", tx);
        if (Convert.ToInt32(check.ExecuteScalar() ?? 0) > 0) return 0;
        int n = 0;
        var dir = Path.Combine(oldData, "configs");
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    using var fs = File.OpenRead(f);
                    var cfg = JsonSerializer.Deserialize(fs, Info<ConfigRecord>.I);
                    if (cfg == null) continue;
                    if (string.IsNullOrEmpty(cfg.BookId)) cfg.BookId = Path.GetFileNameWithoutExtension(f);
                    UpsertConfig(cn, tx, cfg);
                    n++;
                }
                catch (Exception ex) { Log.Warn("迁移配置失败 " + f + ": " + ex.Message); }
            }
        }
        var legacy = Path.Combine(oldData, "configs.json");
        if (n == 0 && File.Exists(legacy))
        {
            try
            {
                using var fs = File.OpenRead(legacy);
                var dict = JsonSerializer.Deserialize(fs, Info<Dictionary<string, ConfigRecord>>.I);
                if (dict != null)
                {
                    foreach (var (id, cfg) in dict)
                    {
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        cfg.BookId = id;
                        UpsertConfig(cn, tx, cfg);
                        n++;
                    }
                }
            }
            catch (Exception ex) { Log.Warn("迁移 configs.json 失败: " + ex.Message); }
        }
        return n;
    }

    private void MigrateSettings(SqliteConnection cn, SqliteTransaction tx, string oldData)
    {
        if (GetMeta(cn, "legacy_settings") == "1") return;
        var file = Path.Combine(oldData, "appsettings.json");
        if (!File.Exists(file))
        {
            SetMeta(cn, tx, "legacy_settings", "1");
            return;
        }
        try
        {
            using var fs = File.OpenRead(file);
            var s = JsonSerializer.Deserialize(fs, Info<AppSettings>.I);
            if (s != null)
            {
                s.Media ??= new MediaSettings();
                s.RecentFolders ??= new List<string>();
                s.ShelfWindow ??= new WindowState(1360, 900);
                s.ReaderWindow ??= new WindowState(1120, 860);
                s.ViewWindow ??= new WindowState(1280, 820);
                s.BrowserWindow ??= new WindowState(1280, 820);
                s.DailyReading ??= new Dictionary<string, long>();
                var oldCovers = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonAot", "covers");
                if (!string.IsNullOrEmpty(s.DefaultCoverPath) &&
                    s.DefaultCoverPath.StartsWith(oldCovers, StringComparison.OrdinalIgnoreCase))
                {
                    var newCover = Path.Combine(CoversDir, Path.GetFileName(s.DefaultCoverPath));
                    if (File.Exists(newCover)) s.DefaultCoverPath = newCover; else s.DefaultCoverPath = null;
                }
                WriteSettings(cn, tx, s);
                using (var ins = Cmd(cn, "INSERT OR REPLACE INTO daily_reading(day, seconds) VALUES($d,$s);", tx))
                {
                    var pd = ins.Parameters.Add("$d", SqliteType.Text);
                    var ps = ins.Parameters.Add("$s", SqliteType.Integer);
                    foreach (var (day, sec) in s.DailyReading)
                    {
                        pd.Value = day; ps.Value = sec;
                        try { ins.ExecuteNonQuery(); } catch { }
                    }
                }
            }
            SetMeta(cn, tx, "legacy_settings", "1");
        }
        catch (Exception ex) { Log.Warn("迁移 appsettings.json 失败: " + ex.Message); }
    }

    private static void MigrateMedia(SqliteConnection cn, SqliteTransaction tx, string oldData)
    {
        var mediaDir = Path.Combine(oldData, "media");
        var progressFile = Path.Combine(mediaDir, "progress.json");
        if (File.Exists(progressFile))
        {
            try
            {
                using var fs = File.OpenRead(progressFile);
                var d = JsonSerializer.Deserialize(fs, Info<MediaProgressStoreData>.I);
                if (d?.Items != null)
                {
                    using var ins = Cmd(cn, """
INSERT OR REPLACE INTO media_progress(path,kind,position,duration,updated_at,title,artist)
VALUES($p,$k,$pos,$dur,$u,$t,$a);
""", tx);
                    var pp = ins.Parameters.Add("$p", SqliteType.Text);
                    var pk = ins.Parameters.Add("$k", SqliteType.Text);
                    var ppos = ins.Parameters.Add("$pos", SqliteType.Real);
                    var pdur = ins.Parameters.Add("$dur", SqliteType.Real);
                    var pu = ins.Parameters.Add("$u", SqliteType.Integer);
                    var pt = ins.Parameters.Add("$t", SqliteType.Text);
                    var pa = ins.Parameters.Add("$a", SqliteType.Text);
                    foreach (var item in d.Items.Values)
                    {
                        pp.Value = item.Path; pk.Value = item.Kind; ppos.Value = item.Position;
                        pdur.Value = item.Duration; pu.Value = item.UpdatedAt;
                        pt.Value = (object?)item.Title ?? DBNull.Value;
                        pa.Value = (object?)item.Artist ?? DBNull.Value;
                        try { ins.ExecuteNonQuery(); } catch { }
                    }
                }
            }
            catch { }
        }
        var playlistFile = Path.Combine(mediaDir, "playlists.json");
        if (File.Exists(playlistFile))
        {
            try
            {
                using var fs = File.OpenRead(playlistFile);
                var d = JsonSerializer.Deserialize(fs, Info<PlaylistStoreData>.I);
                if (d?.Playlists != null)
                {
                    foreach (var p in d.Playlists)
                    {
                        using var ins = Cmd(cn, "INSERT OR REPLACE INTO playlists(id,name,kind,current,updated_at) VALUES($id,$n,$k,$c,$u);", tx);
                        P(ins, "$id", p.Id); P(ins, "$n", p.Name); P(ins, "$k", p.Kind);
                        P(ins, "$c", p.Current); P(ins, "$u", p.UpdatedAt);
                        try { ins.ExecuteNonQuery(); } catch { }
                        for (int i = 0; i < p.Items.Count; i++)
                        {
                            using var it = Cmd(cn, "INSERT OR REPLACE INTO playlist_items(playlist_id,ord,path) VALUES($p,$o,$path);", tx);
                            P(it, "$p", p.Id); P(it, "$o", i); P(it, "$path", p.Items[i]);
                            try { it.ExecuteNonQuery(); } catch { }
                        }
                    }
                    if (!string.IsNullOrEmpty(d.LastId))
                    {
                        using var last = Cmd(cn, "UPDATE app_settings SET last_playlist_id=$id WHERE id=1;", tx);
                        P(last, "$id", d.LastId); last.ExecuteNonQuery();
                    }
                }
            }
            catch { }
        }
        var zipFile = Path.Combine(mediaDir, "zip.json");
        if (File.Exists(zipFile))
        {
            try
            {
                using var fs = File.OpenRead(zipFile);
                var d = JsonSerializer.Deserialize(fs, Info<Dictionary<string, string>>.I);
                if (d != null)
                {
                    using var ins = Cmd(cn, "INSERT OR REPLACE INTO zip_last(path,entry) VALUES($p,$e);", tx);
                    var pp = ins.Parameters.Add("$p", SqliteType.Text);
                    var pe = ins.Parameters.Add("$e", SqliteType.Text);
                    foreach (var (path, entry) in d)
                    {
                        pp.Value = path; pe.Value = entry;
                        try { ins.ExecuteNonQuery(); } catch { }
                    }
                }
            }
            catch { }
        }
        var lyricsDir = Path.Combine(oldData, "lyrics");
        if (Directory.Exists(lyricsDir))
        {
            foreach (var f in Directory.GetFiles(lyricsDir, "*.json"))
            {
                try
                {
                    using var fs = File.OpenRead(f);
                    var d = JsonSerializer.Deserialize(fs, Info<LyricsCacheData>.I);
                    if (d == null) continue;
                    var lrc = d.Lrc;
                    if (string.IsNullOrEmpty(lrc) && d.Lines != null && d.Lines.Count > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var line in d.Lines)
                            sb.Append('[').Append(TimeSpan.FromSeconds(line.T).ToString(@"mm\:ss\.ff")).Append(']').Append(line.Text).Append('\n');
                        lrc = sb.ToString();
                    }
                    using var ins = Cmd(cn, """
INSERT OR REPLACE INTO lyrics_cache(key,fetched_at,source,synced,plain,lrc) VALUES($k,$f,$s,$y,$p,$l);
""", tx);
                    P(ins, "$k", d.Key); P(ins, "$f", d.FetchedAt); P(ins, "$s", d.Source);
                    P(ins, "$y", d.Synced ? 1 : 0); P(ins, "$p", d.Plain); P(ins, "$l", lrc);
                    try { ins.ExecuteNonQuery(); } catch { }
                }
                catch { }
            }
        }
    }

    private static void CopyDir(string src, string dst)
    {
        try
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            int n = 0;
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, file);
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? dst);
                if (!File.Exists(target))
                {
                    try { File.Copy(file, target, false); n++; } catch { }
                }
            }
            if (n > 0) Log.Info($"复制旧缓存 {n} 个文件: {src} → {dst}");
        }
        catch (Exception ex) { Log.Warn("复制旧目录失败 " + src + ": " + ex.Message); }
    }
}
