using System;
using System.IO;
using Microsoft.Data.Sqlite;
using MoonAot.Services;
using MoonAot.Store;

namespace MoonAot.Med;

/// <summary>
/// 医学助手独立数据库 data/medical.db。与 moonaot.db 物理隔离：
/// 主库已 115MB，医学 FTS5 索引还会增长 100MB+，混库会拖慢书架并放大锁竞争。
/// </summary>
public sealed class MedDatabase
{
    public static MedDatabase Instance { get; } = new();

    public string DataDir { get; private set; } = "";
    public string DbPath { get; private set; } = "";
    public string SourcesDir { get; private set; } = "";
    public string ClinicalDir { get; private set; } = "";
    public string ConnectionString { get; private set; } = "";
    public bool Initialized { get; private set; }
    /// <summary>医学库专用写锁，不与主库互相阻塞。</summary>
    public readonly object SyncRoot = new();

    private MedDatabase() { }

    public void Initialize()
    {
        if (Initialized) return;
        lock (SyncRoot)
        {
            if (Initialized) return;
            Database.Instance.Initialize();
            DataDir = Database.Instance.DataDir;
            DbPath = Path.Combine(DataDir, "medical.db");
            SourcesDir = Path.Combine(DataDir, "medical", "sources");
            ClinicalDir = Path.Combine(SourcesDir, "clinical");
            Directory.CreateDirectory(SourcesDir);
            Directory.CreateDirectory(ClinicalDir);

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                DefaultTimeout = 15,
                Cache = SqliteCacheMode.Shared,
            };
            ConnectionString = builder.ToString();
            // 这里不能走 Open()：Open() 在 Initialized 为 false 时会回调 Initialize()，形成递归。
            using var cn = new SqliteConnection(ConnectionString);
            cn.Open();
            Exec(cn, "PRAGMA journal_mode=WAL;");
            CreateSchema(cn);
            Initialized = true;
        }
    }

    public SqliteConnection Open()
    {
        if (!Initialized) Initialize();
        var cn = new SqliteConnection(ConnectionString);
        cn.Open();
        Exec(cn, "PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-32000; PRAGMA mmap_size=268435456;");
        return cn;
    }

    private static void Exec(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        try { cmd.ExecuteNonQuery(); } catch (Exception ex) { Log.Warn("[med] pragma: " + ex.Message); }
    }

    public long DbBytes
    {
        get
        {
            try { return new FileInfo(DbPath).Length; } catch { return 0; }
        }
    }

    public static SqliteCommand Cmd(SqliteConnection cn, string sql)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    public static void P(SqliteCommand cmd, string name, object? value)
        => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static void CreateSchema(SqliteConnection cn)
    {
        const string sql = """
CREATE TABLE IF NOT EXISTS med_books (
    id TEXT PRIMARY KEY, book_id TEXT NOT NULL DEFAULT '', title TEXT NOT NULL DEFAULT '',
    volume TEXT NOT NULL DEFAULT '', type TEXT NOT NULL DEFAULT 'epub', path TEXT NOT NULL DEFAULT '',
    sha1 TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'pending',
    added_at INTEGER NOT NULL DEFAULT 0, indexed_at INTEGER NOT NULL DEFAULT 0,
    doc_count INTEGER NOT NULL DEFAULT 0, chapter_count INTEGER NOT NULL DEFAULT 0,
    chunk_count INTEGER NOT NULL DEFAULT 0, char_count INTEGER NOT NULL DEFAULT 0,
    anchor_total INTEGER NOT NULL DEFAULT 0, anchor_resolved INTEGER NOT NULL DEFAULT 0,
    image_count INTEGER NOT NULL DEFAULT 0, elapsed_ms INTEGER NOT NULL DEFAULT 0, error TEXT
);
CREATE TABLE IF NOT EXISTS med_chapters (
    id TEXT PRIMARY KEY, book_id TEXT NOT NULL, parent_id TEXT NOT NULL DEFAULT '',
    level INTEGER NOT NULL DEFAULT 1, spine_index INTEGER NOT NULL DEFAULT 0,
    href TEXT NOT NULL DEFAULT '', anchor TEXT NOT NULL DEFAULT '', title TEXT NOT NULL DEFAULT '',
    title_norm TEXT NOT NULL DEFAULT '', page_hint TEXT NOT NULL DEFAULT '', ord INTEGER NOT NULL DEFAULT 0,
    char_start INTEGER NOT NULL DEFAULT 0, char_end INTEGER NOT NULL DEFAULT 0,
    img_count INTEGER NOT NULL DEFAULT 0, block_start INTEGER NOT NULL DEFAULT 0,
    block_end INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_med_chapters_book ON med_chapters(book_id, ord);
CREATE TABLE IF NOT EXISTS med_chunks (
    id TEXT PRIMARY KEY, book_id TEXT NOT NULL, chapter_id TEXT NOT NULL, ord INTEGER NOT NULL DEFAULT 0,
    text TEXT NOT NULL DEFAULT '', text_norm TEXT NOT NULL DEFAULT '',
    p_start INTEGER NOT NULL DEFAULT 0, p_end INTEGER NOT NULL DEFAULT 0,
    char_start INTEGER NOT NULL DEFAULT 0, char_end INTEGER NOT NULL DEFAULT 0,
    img_refs TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_med_chunks_book ON med_chunks(book_id, chapter_id, ord);
CREATE INDEX IF NOT EXISTS ix_med_chunks_chapter ON med_chunks(chapter_id);
CREATE VIRTUAL TABLE IF NOT EXISTS med_fts USING fts5(
    text, book_id UNINDEXED, chapter_id UNINDEXED, chunk_id UNINDEXED,
    tokenize='trigram');
CREATE TABLE IF NOT EXISTS med_concepts (
    id TEXT PRIMARY KEY, type TEXT NOT NULL DEFAULT '', name TEXT NOT NULL DEFAULT '',
    name_norm TEXT NOT NULL DEFAULT '', pinyin TEXT NOT NULL DEFAULT '', initials TEXT NOT NULL DEFAULT '',
    category TEXT NOT NULL DEFAULT '', keywords TEXT NOT NULL DEFAULT '', data_json TEXT NOT NULL DEFAULT '{}',
    summary TEXT NOT NULL DEFAULT '', source TEXT NOT NULL DEFAULT '', source_version TEXT NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_med_concepts_norm ON med_concepts(name_norm);
CREATE INDEX IF NOT EXISTS ix_med_concepts_init ON med_concepts(initials);
CREATE INDEX IF NOT EXISTS ix_med_concepts_type ON med_concepts(type);
CREATE TABLE IF NOT EXISTS med_synonyms (
    term TEXT PRIMARY KEY, canonical_id TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'alias'
);
CREATE TABLE IF NOT EXISTS med_hits (
    concept_id TEXT NOT NULL, book_id TEXT NOT NULL, chapter_id TEXT NOT NULL,
    chunk_id TEXT NOT NULL, tf INTEGER NOT NULL DEFAULT 1, positions TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (concept_id, chunk_id)
);
CREATE INDEX IF NOT EXISTS ix_med_hits_concept ON med_hits(concept_id, book_id);
CREATE INDEX IF NOT EXISTS ix_med_hits_chunk ON med_hits(chunk_id);
CREATE TABLE IF NOT EXISTS med_links (
    id TEXT PRIMARY KEY, src_type TEXT NOT NULL, src_id TEXT NOT NULL,
    dst_type TEXT NOT NULL, dst_id TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'manual',
    weight INTEGER NOT NULL DEFAULT 1, note TEXT NOT NULL DEFAULT '', created_at INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_med_links_dst ON med_links(dst_type, dst_id);
CREATE INDEX IF NOT EXISTS ix_med_links_src ON med_links(src_type, src_id);
CREATE TABLE IF NOT EXISTS med_notes (
    id TEXT PRIMARY KEY, title TEXT NOT NULL DEFAULT '', body TEXT NOT NULL DEFAULT '',
    target_type TEXT NOT NULL DEFAULT '', target_id TEXT NOT NULL DEFAULT '',
    book_id TEXT NOT NULL DEFAULT '', chapter_id TEXT NOT NULL DEFAULT '',
    chunk_id TEXT NOT NULL DEFAULT '', quote TEXT NOT NULL DEFAULT '', color TEXT NOT NULL DEFAULT '',
    created_at INTEGER NOT NULL DEFAULT 0, updated_at INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS med_tags (
    id TEXT PRIMARY KEY, name TEXT NOT NULL DEFAULT '', parent_id TEXT NOT NULL DEFAULT '',
    color TEXT NOT NULL DEFAULT '', ord INTEGER NOT NULL DEFAULT 0, kind TEXT NOT NULL DEFAULT 'category'
);
CREATE TABLE IF NOT EXISTS med_tagged (
    tag_id TEXT NOT NULL, target_type TEXT NOT NULL, target_id TEXT NOT NULL,
    added_at INTEGER NOT NULL DEFAULT 0, note TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (tag_id, target_type, target_id)
);
CREATE INDEX IF NOT EXISTS ix_med_tagged_target ON med_tagged(target_type, target_id);
CREATE TABLE IF NOT EXISTS med_templates (
    id TEXT PRIMARY KEY, kind TEXT NOT NULL DEFAULT 'note', name TEXT NOT NULL DEFAULT '',
    schema_json TEXT NOT NULL DEFAULT '{}', content_json TEXT NOT NULL DEFAULT '{}',
    pinned INTEGER NOT NULL DEFAULT 0, builtin INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL DEFAULT 0, updated_at INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS med_queries (
    id TEXT PRIMARY KEY, name TEXT NOT NULL DEFAULT '', dsl_json TEXT NOT NULL DEFAULT '{}',
    pinned INTEGER NOT NULL DEFAULT 0, last_used_at INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS med_meta (key TEXT PRIMARY KEY, value TEXT);
PRAGMA user_version=1;
""";
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public string GetMeta(string key)
    {
        using var cn = Open();
        using var cmd = Cmd(cn, "SELECT value FROM med_meta WHERE key=$k;");
        P(cmd, "$k", key);
        return cmd.ExecuteScalar() as string ?? "";
    }

    public void SetMeta(string key, string value)
    {
        lock (SyncRoot)
        {
            using var cn = Open();
            using var cmd = Cmd(cn, "INSERT INTO med_meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value;");
            P(cmd, "$k", key);
            P(cmd, "$v", value);
            cmd.ExecuteNonQuery();
        }
    }
}
