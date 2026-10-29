using System;
using System.IO;
using Microsoft.Data.Sqlite;
using MoonAot.Services;

namespace MoonAot.Store;

/// <summary>
/// SQLite 数据库位置与连接（嵌入式、单文件、NativeAOT 已实测）。
/// 数据目录固定在 exe 同级 data\；若该目录不可写则回退到 %LOCALAPPDATA%\MoonAot\PortableData。
///   data\moonaot.db   书库/笔记/设置/播放进度/播放列表/歌词缓存
///   data\covers       提取/自定义封面
///   data\thumbs       书架缩略图缓存
///   data\library      网页导入的书籍副本
///   data\logs         运行日志
/// </summary>
public sealed class Database
{
    public static Database Instance { get; } = new();

    public string DataDir { get; private set; } = "";
    public string DbPath { get; private set; } = "";
    public string CoversDir { get; private set; } = "";
    public string ThumbsDir { get; private set; } = "";
    public string LibraryDir { get; private set; } = "";
    public string UploadsDir { get; private set; } = "";
    public string LogsDir { get; private set; } = "";
    public string MediaDir { get; private set; } = "";
    public string LyricsDir { get; private set; } = "";
    public string ConnectionString { get; private set; } = "";
    public bool Initialized { get; private set; }
    /// <summary>跨线程写串行化（本应用为单用户本地库，简单锁即可避免 SQLITE_BUSY）。</summary>
    public readonly object SyncRoot = new();

    private Database() { }

    public void Initialize()
    {
        if (Initialized) return;
        lock (SyncRoot)
        {
            if (Initialized) return;

            var exeData = Path.Combine(AppContext.BaseDirectory, "data");
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MoonAot", "PortableData", "data");

            DataDir = TryPrepare(exeData) ? exeData : (TryPrepare(fallback) ? fallback : exeData);
            Directory.CreateDirectory(DataDir);

            DbPath = Path.Combine(DataDir, "moonaot.db");
            CoversDir = Path.Combine(DataDir, "covers");
            ThumbsDir = Path.Combine(DataDir, "thumbs");
            LibraryDir = Path.Combine(DataDir, "library");
            UploadsDir = Path.Combine(DataDir, "uploads");
            LogsDir = Path.Combine(DataDir, "logs");
            MediaDir = Path.Combine(DataDir, "media");
            LyricsDir = Path.Combine(DataDir, "lyrics");
            foreach (var d in new[] { CoversDir, ThumbsDir, LibraryDir, UploadsDir, LogsDir, MediaDir, LyricsDir })
                Directory.CreateDirectory(d);

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                DefaultTimeout = 8,
                ForeignKeys = false,
                Cache = SqliteCacheMode.Shared,
            };
            ConnectionString = builder.ToString();
            Initialized = true;

            using var cn = Open();
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                try { cmd.ExecuteScalar(); } catch { }
            }
            CreateSchema(cn);
            // 旧库升级：CREATE TABLE IF NOT EXISTS 不会补列
            EnsureColumn(cn, "app_settings", "media_image_transition", "TEXT NOT NULL DEFAULT 'fade'");
            EnsureColumn(cn, "app_settings", "media_image_bgm", "TEXT");
            EnsureColumn(cn, "app_settings", "media_image_slide_loop", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(cn, "app_settings", "media_music_volume", "REAL NOT NULL DEFAULT 0.9");
            EnsureColumn(cn, "app_settings", "media_music_mode", "TEXT NOT NULL DEFAULT 'order'");
            EnsureColumn(cn, "app_settings", "media_music_tab", "TEXT NOT NULL DEFAULT 'list'");
            EnsureColumn(cn, "app_settings", "media_video_volume", "REAL NOT NULL DEFAULT 1.0");
            EnsureColumn(cn, "app_settings", "media_video_show_list", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(cn, "app_settings", "media_music_bg_folder", "TEXT");
            EnsureColumn(cn, "app_settings", "media_music_bg_index", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(cn, "app_settings", "media_music_bg_random", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(cn, "app_settings", "media_music_bg_fit", "TEXT NOT NULL DEFAULT 'cover'");
            EnsureColumn(cn, "app_settings", "media_music_bg_blur", "INTEGER NOT NULL DEFAULT 5");
            EnsureColumn(cn, "app_settings", "media_image_last_folder", "TEXT");
            EnsureColumn(cn, "app_settings", "media_image_fit", "TEXT NOT NULL DEFAULT 'contain'");
            EnsureColumn(cn, "app_settings", "media_image_bgm_folder", "TEXT");
            EnsureColumn(cn, "app_settings", "media_image_bgm_playlist", "TEXT");
            EnsureColumn(cn, "app_settings", "media_image_bgm_index", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(cn, "app_settings", "shelf_query_json", "TEXT");
            EnsureColumn(cn, "app_settings", "browser_w", "INTEGER NOT NULL DEFAULT 1280");
            EnsureColumn(cn, "app_settings", "browser_h", "INTEGER NOT NULL DEFAULT 820");
            EnsureColumn(cn, "app_settings", "browser_x", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(cn, "app_settings", "browser_y", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(cn, "app_settings", "browser_pos", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(cn, "app_settings", "browser_max", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(cn, "media_progress", "scale", "REAL NOT NULL DEFAULT 1");
            EnsureColumn(cn, "media_progress", "rotation", "REAL NOT NULL DEFAULT 0");
        }
    }

    private static void EnsureColumn(SqliteConnection cn, string table, string column, string ddl)
    {
        try
        {
            using (var check = cn.CreateCommand())
            {
                check.CommandText = "PRAGMA table_info(" + table + ");";
                using var r = check.ExecuteReader();
                while (r.Read())
                {
                    if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
                }
            }
            using var alter = cn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {ddl};";
            alter.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log.Warn($"补列失败 {table}.{column}: {ex.Message}");
        }
    }

    private static bool TryPrepare(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public SqliteConnection Open()
    {
        if (!Initialized) Initialize();
        var cn = new SqliteConnection(ConnectionString);
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-32000; PRAGMA mmap_size=268435456;";
        try { cmd.ExecuteNonQuery(); } catch { }
        return cn;
    }

    public void Checkpoint()
    {
        try
        {
            using var cn = Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    private static void CreateSchema(SqliteConnection cn)
    {
        const string sql = """
CREATE TABLE IF NOT EXISTS books (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL DEFAULT '',
    author TEXT NOT NULL DEFAULT '',
    file_path TEXT NOT NULL,
    source_path TEXT,
    format TEXT NOT NULL DEFAULT 'TXT',
    file_size INTEGER NOT NULL DEFAULT 0,
    added_at INTEGER NOT NULL,
    last_read_at INTEGER,
    progress REAL NOT NULL DEFAULT 0,
    location TEXT,
    cover_file TEXT,
    cover_ext TEXT,
    cover_version INTEGER NOT NULL DEFAULT 0,
    publisher TEXT,
    description TEXT,
    language TEXT,
    category TEXT,
    folder TEXT,
    favorite INTEGER NOT NULL DEFAULT 0,
    reading_seconds INTEGER NOT NULL DEFAULT 0,
    finished_at INTEGER
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_books_path ON books(file_path COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_books_added ON books(added_at DESC);
CREATE INDEX IF NOT EXISTS ix_books_read ON books(last_read_at DESC);
CREATE INDEX IF NOT EXISTS ix_books_recent ON books(COALESCE(last_read_at, added_at) DESC);
CREATE INDEX IF NOT EXISTS ix_books_size ON books(file_size DESC);
CREATE INDEX IF NOT EXISTS ix_books_title ON books(title COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_books_format ON books(format);
CREATE INDEX IF NOT EXISTS ix_books_format_title ON books(format, title COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_books_folder ON books(folder COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_books_author ON books(author COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_books_author_title ON books(author COLLATE NOCASE, title COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_books_fav ON books(favorite);

CREATE TABLE IF NOT EXISTS configs (
    book_id TEXT PRIMARY KEY,
    config_json TEXT,
    config_hash TEXT,
    progress TEXT,
    location TEXT,
    view_settings TEXT,
    pdf_layout TEXT,
    pdf_zoom TEXT,
    pdf_auto_page INTEGER,
    updated_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS notes (
    id TEXT PRIMARY KEY,
    book_id TEXT NOT NULL,
    type TEXT NOT NULL DEFAULT 'annotation',
    cfi TEXT NOT NULL DEFAULT '',
    text TEXT,
    note TEXT NOT NULL DEFAULT '',
    style TEXT,
    color TEXT,
    page INTEGER,
    progress REAL NOT NULL DEFAULT 0,
    label TEXT,
    chapter TEXT,
    created_at INTEGER NOT NULL DEFAULT 0,
    updated_at INTEGER NOT NULL DEFAULT 0,
    deleted_at INTEGER
);
CREATE INDEX IF NOT EXISTS ix_notes_book ON notes(book_id);
CREATE INDEX IF NOT EXISTS ix_notes_updated ON notes(updated_at DESC);

CREATE TABLE IF NOT EXISTS app_settings (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    app_theme TEXT NOT NULL DEFAULT 'dark',
    sort_mode TEXT NOT NULL DEFAULT 'recent',
    sort_desc INTEGER NOT NULL DEFAULT 1,
    show_covers INTEGER NOT NULL DEFAULT 1,
    library_view TEXT NOT NULL DEFAULT 'grid',
    grid_size INTEGER NOT NULL DEFAULT 168,
    reader_settings_json TEXT NOT NULL DEFAULT '{}',
    default_cover_path TEXT,
    import_covers INTEGER NOT NULL DEFAULT 1,
    use_default_cover INTEGER NOT NULL DEFAULT 1,
    auto_refresh_library INTEGER NOT NULL DEFAULT 1,
    last_folder TEXT,
    reading_seconds INTEGER NOT NULL DEFAULT 0,
    library_version INTEGER NOT NULL DEFAULT 1,
    last_playlist_id TEXT,
    shelf_w INTEGER NOT NULL DEFAULT 1360,
    shelf_h INTEGER NOT NULL DEFAULT 900,
    shelf_x INTEGER NOT NULL DEFAULT 0,
    shelf_y INTEGER NOT NULL DEFAULT 0,
    shelf_pos INTEGER NOT NULL DEFAULT 0,
    shelf_max INTEGER NOT NULL DEFAULT 0,
    reader_w INTEGER NOT NULL DEFAULT 1120,
    reader_h INTEGER NOT NULL DEFAULT 860,
    reader_x INTEGER NOT NULL DEFAULT 0,
    reader_y INTEGER NOT NULL DEFAULT 0,
    reader_pos INTEGER NOT NULL DEFAULT 0,
    reader_max INTEGER NOT NULL DEFAULT 0,
    view_w INTEGER NOT NULL DEFAULT 1280,
    view_h INTEGER NOT NULL DEFAULT 820,
    view_x INTEGER NOT NULL DEFAULT 0,
    view_y INTEGER NOT NULL DEFAULT 0,
    view_pos INTEGER NOT NULL DEFAULT 0,
    view_max INTEGER NOT NULL DEFAULT 0,
    browser_w INTEGER NOT NULL DEFAULT 1280,
    browser_h INTEGER NOT NULL DEFAULT 820,
    browser_x INTEGER NOT NULL DEFAULT 0,
    browser_y INTEGER NOT NULL DEFAULT 0,
    browser_pos INTEGER NOT NULL DEFAULT 0,
    browser_max INTEGER NOT NULL DEFAULT 0,
    media_image_slide_seconds INTEGER NOT NULL DEFAULT 5,
    media_comic_mode INTEGER NOT NULL DEFAULT 0,
    media_comic_width TEXT NOT NULL DEFAULT 'default',
    media_comic_auto_page INTEGER NOT NULL DEFAULT 0,
    media_recursive_images INTEGER NOT NULL DEFAULT 0,
    media_music_background_seconds INTEGER NOT NULL DEFAULT 8,
    media_music_show_lyrics INTEGER NOT NULL DEFAULT 1,
    media_video_auto_resume INTEGER NOT NULL DEFAULT 1,
    media_image_transition TEXT NOT NULL DEFAULT 'fade',
    media_image_bgm TEXT,
    media_image_slide_loop INTEGER NOT NULL DEFAULT 1,
    media_music_volume REAL NOT NULL DEFAULT 0.9,
    media_music_mode TEXT NOT NULL DEFAULT 'order',
    media_music_tab TEXT NOT NULL DEFAULT 'list',
    media_video_volume REAL NOT NULL DEFAULT 1.0,
    media_video_show_list INTEGER NOT NULL DEFAULT 1,
    media_music_bg_folder TEXT,
    media_music_bg_index INTEGER NOT NULL DEFAULT 0,
    media_music_bg_random INTEGER NOT NULL DEFAULT 0,
    media_music_bg_fit TEXT NOT NULL DEFAULT 'cover',
    media_music_bg_blur INTEGER NOT NULL DEFAULT 5,
    media_image_last_folder TEXT,
    media_image_fit TEXT NOT NULL DEFAULT 'contain',
    media_image_bgm_folder TEXT,
    media_image_bgm_playlist TEXT,
    media_image_bgm_index INTEGER NOT NULL DEFAULT 0,
    shelf_query_json TEXT,
    last_version TEXT
);
INSERT OR IGNORE INTO app_settings(id) VALUES(1);

CREATE TABLE IF NOT EXISTS recent_folders (
    path TEXT PRIMARY KEY COLLATE NOCASE,
    updated_at INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS daily_reading (
    day TEXT PRIMARY KEY,
    seconds INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS media_progress (
    path TEXT PRIMARY KEY COLLATE NOCASE,
    kind TEXT NOT NULL DEFAULT 'audio',
    position REAL NOT NULL DEFAULT 0,
    duration REAL NOT NULL DEFAULT 0,
    updated_at INTEGER NOT NULL DEFAULT 0,
    title TEXT,
    artist TEXT,
    scale REAL NOT NULL DEFAULT 1,
    rotation REAL NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS playlists (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL DEFAULT '',
    kind TEXT NOT NULL DEFAULT 'audio',
    current INTEGER NOT NULL DEFAULT 0,
    updated_at INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS playlist_items (
    playlist_id TEXT NOT NULL,
    ord INTEGER NOT NULL,
    path TEXT NOT NULL,
    PRIMARY KEY (playlist_id, ord)
);

CREATE TABLE IF NOT EXISTS lyrics_cache (
    key TEXT PRIMARY KEY,
    fetched_at INTEGER NOT NULL DEFAULT 0,
    source TEXT NOT NULL DEFAULT '',
    synced INTEGER NOT NULL DEFAULT 0,
    plain TEXT NOT NULL DEFAULT '',
    lrc TEXT
);

CREATE TABLE IF NOT EXISTS zip_last (
    path TEXT PRIMARY KEY COLLATE NOCASE,
    entry TEXT
);

CREATE TABLE IF NOT EXISTS macros (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL DEFAULT '新宏',
    enabled INTEGER NOT NULL DEFAULT 1,
    trigger_mode TEXT NOT NULL DEFAULT 'hotkey',
    trigger TEXT NOT NULL DEFAULT 'F1',
    repeat INTEGER NOT NULL DEFAULT 1,
    repeat_delay_ms INTEGER NOT NULL DEFAULT 500,
    lead_in_ms INTEGER NOT NULL DEFAULT 1500,
    send_mode TEXT NOT NULL DEFAULT 'scan',
    created_at INTEGER NOT NULL DEFAULT 0,
    updated_at INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS macro_steps (
    macro_id TEXT NOT NULL,
    ord INTEGER NOT NULL,
    kind TEXT NOT NULL DEFAULT 'key',
    keys TEXT NOT NULL DEFAULT '',
    hold_ms INTEGER NOT NULL DEFAULT 30,
    delay_ms INTEGER NOT NULL DEFAULT 0,
    x INTEGER NOT NULL DEFAULT 0,
    y INTEGER NOT NULL DEFAULT 0,
    button TEXT,
    wheel INTEGER NOT NULL DEFAULT 120,
    text TEXT,
    PRIMARY KEY (macro_id, ord)
);

CREATE TABLE IF NOT EXISTS favorite_folders (
    path TEXT PRIMARY KEY COLLATE NOCASE,
    name TEXT NOT NULL DEFAULT '',
    added_at INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS meta (
    key TEXT PRIMARY KEY,
    value TEXT
);
PRAGMA user_version=1;
""";
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
