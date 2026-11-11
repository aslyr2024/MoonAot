using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using static MoonAot.Med.MedDatabase;

namespace MoonAot.Med;

/// <summary>注释、分类/收藏、模板、保存检索式、章节视图、导出。</summary>
public static class MedStore
{
    private static long Now => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    public const string FavoriteTagId = "med-favorite";
    /// <summary>医学助手版本，随应用版本一起发布；显示在页面标题与使用说明中。</summary>
    public const string MedVersion = "1.3.4";

    /// <summary>内置数据源默认位置（机器相关）；状态接口会返回是否存在，前端据此决定“一键导入”是否可用。</summary>
    public static readonly (string Path, string Volume)[] DefaultBookSources =
    {
        (@"D:\office\Movies\图书\西氏内科学\西氏内科学上.epub", "上卷"),
        (@"D:\office\Movies\图书\西氏内科学\西氏内科学中.epub", "中卷"),
        (@"D:\office\Movies\图书\西氏内科学\西氏内科学下.epub", "下卷"),
        (@"D:\office\Movies\图书\西氏内科学\胸腹部影像图解_胡春洪.epub", "图谱"),
    };

    // ---------------- notes ----------------

    public static List<MedNoteDto> Notes(string q = "", int limit = 200)
    {
        var list = new List<MedNoteDto>();
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT * FROM med_notes " +
            (q.Length > 0 ? "WHERE title LIKE $q OR body LIKE $q OR quote LIKE $q " : "") +
            "ORDER BY updated_at DESC LIMIT $l;");
        if (q.Length > 0) P(cmd, "$q", "%" + q + "%");
        P(cmd, "$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadNote(r));
        return list;
    }

    private static MedNoteDto ReadNote(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), Title = r.GetString(1), Body = r.GetString(2), TargetType = r.GetString(3),
        TargetId = r.GetString(4), BookId = r.GetString(5), ChapterId = r.GetString(6), ChunkId = r.GetString(7),
        Quote = r.GetString(8), Color = r.GetString(9), CreatedAt = r.GetInt64(10), UpdatedAt = r.GetInt64(11),
    };

    public static MedNoteDto? Note(string id)
    {
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT * FROM med_notes WHERE id=$i;");
        P(cmd, "$i", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadNote(r) : null;
    }

    public static string SaveNote(MedNoteDto n)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            if (string.IsNullOrEmpty(n.Id)) n.Id = MedEpub.NewId("n");
            if (n.CreatedAt == 0) n.CreatedAt = Now;
            n.UpdatedAt = Now;
            using (var cmd = Cmd(cn, """
INSERT OR REPLACE INTO med_notes(id,title,body,target_type,target_id,book_id,chapter_id,chunk_id,quote,color,created_at,updated_at)
 VALUES($id,$title,$body,$tt,$ti,$b,$ch,$k,$q,$color,$ca,$ua);
"""))
            {
                P(cmd, "$id", n.Id); P(cmd, "$title", n.Title); P(cmd, "$body", n.Body);
                P(cmd, "$tt", n.TargetType); P(cmd, "$ti", n.TargetId); P(cmd, "$b", n.BookId);
                P(cmd, "$ch", n.ChapterId); P(cmd, "$k", n.ChunkId); P(cmd, "$q", n.Quote);
                P(cmd, "$color", n.Color); P(cmd, "$ca", n.CreatedAt); P(cmd, "$ua", n.UpdatedAt);
                cmd.ExecuteNonQuery();
            }
            // [[双链]] → 概念边
            using (var del = Cmd(cn, "DELETE FROM med_links WHERE kind='wiki' AND src_type='note' AND src_id=$i;"))
            { P(del, "$i", n.Id); del.ExecuteNonQuery(); }
            var all = MedConcepts.All();
            foreach (Match m in Regex.Matches(n.Body ?? "", @"\[\[([^\]]{1,60})\]\]"))
            {
                var key = MedText.NormalizeTitle(m.Groups[1].Value);
                var c = all.FirstOrDefault(x => x.NameNorm == key) ?? all.FirstOrDefault(x => x.NameNorm.Contains(key));
                if (c == null) continue;
                using var ins = Cmd(cn, "INSERT INTO med_links(id,src_type,src_id,dst_type,dst_id,kind,weight,note,created_at) VALUES($id,'note',$s,'concept',$d,'wiki',1,'',$now);");
                P(ins, "$id", MedEpub.NewId("l")); P(ins, "$s", n.Id); P(ins, "$d", c.Id); P(ins, "$now", Now);
                ins.ExecuteNonQuery();
            }
            // 引用型笔记 → 目标边（先清旧的，避免每次保存叠加）
            using (var del = Cmd(cn, "DELETE FROM med_links WHERE kind='citation' AND src_type='note' AND src_id=$i;"))
            { P(del, "$i", n.Id); del.ExecuteNonQuery(); }
            if (n.TargetId.Length > 0)
            {
                using var ins = Cmd(cn, "INSERT INTO med_links(id,src_type,src_id,dst_type,dst_id,kind,weight,note,created_at) VALUES($id,'note',$s,$tt,$d,'citation',1,'',$now);");
                P(ins, "$id", MedEpub.NewId("l")); P(ins, "$s", n.Id); P(ins, "$tt", n.TargetType); P(ins, "$d", n.TargetId); P(ins, "$now", Now);
                ins.ExecuteNonQuery();
            }
            return n.Id;
        }
    }

    public static void DeleteNote(string id)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using (var c1 = Cmd(cn, "DELETE FROM med_notes WHERE id=$i;")) { P(c1, "$i", id); c1.ExecuteNonQuery(); }
            using (var c2 = Cmd(cn, "DELETE FROM med_links WHERE src_type='note' AND src_id=$i;")) { P(c2, "$i", id); c2.ExecuteNonQuery(); }
        }
    }

    public static List<MedNoteDto> Backlinks(string targetType, string targetId, int limit = 40)
    {
        var list = new List<MedNoteDto>();
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, """
SELECT n.* FROM med_notes n WHERE n.id IN (
  SELECT src_id FROM med_links WHERE dst_type=$tt AND dst_id=$ti AND src_type='note'
  UNION SELECT id FROM med_notes WHERE target_type=$tt AND target_id=$ti
) ORDER BY n.updated_at DESC LIMIT $l;
""");
        P(cmd, "$tt", targetType); P(cmd, "$ti", targetId); P(cmd, "$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadNote(r));
        return list;
    }

    // ---------------- anchors（用户自建段落锚点） ----------------

    public static List<MedAnchorDto> Anchors(string chapterId = "", string bookId = "", int limit = 300)
    {
        var list = new List<MedAnchorDto>();
        using var cn = MedDatabase.Instance.Open();
        var where = new List<string>();
        if (!string.IsNullOrEmpty(chapterId)) where.Add("chapter_id=$ch");
        if (!string.IsNullOrEmpty(bookId)) where.Add("book_id=$b");
        using var cmd = Cmd(cn, "SELECT id,book_id,chapter_id,chunk_id,p_start,label,note,created_at FROM med_anchors " +
            (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) + " " : "") +
            "ORDER BY created_at DESC LIMIT $l;");
        if (!string.IsNullOrEmpty(chapterId)) P(cmd, "$ch", chapterId);
        if (!string.IsNullOrEmpty(bookId)) P(cmd, "$b", bookId);
        P(cmd, "$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MedAnchorDto
        {
            Id = r.GetString(0), BookId = r.GetString(1), ChapterId = r.GetString(2), ChunkId = r.GetString(3),
            PStart = r.GetInt32(4), Label = r.GetString(5), Note = r.GetString(6), CreatedAt = r.GetInt64(7),
        });
        return list;
    }

    public static string SaveAnchor(MedAnchorDto a)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            if (string.IsNullOrEmpty(a.Id)) a.Id = MedEpub.NewId("a");
            if (a.CreatedAt == 0) a.CreatedAt = Now;
            using var cmd = Cmd(cn, "INSERT OR REPLACE INTO med_anchors(id,book_id,chapter_id,chunk_id,p_start,label,note,created_at) VALUES($id,$b,$ch,$k,$p,$l,$n,$t);");
            P(cmd, "$id", a.Id); P(cmd, "$b", a.BookId); P(cmd, "$ch", a.ChapterId); P(cmd, "$k", a.ChunkId);
            P(cmd, "$p", a.PStart); P(cmd, "$l", a.Label); P(cmd, "$n", a.Note); P(cmd, "$t", a.CreatedAt);
            cmd.ExecuteNonQuery();
            return a.Id;
        }
    }

    public static void DeleteAnchor(string id)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var cmd = Cmd(cn, "DELETE FROM med_anchors WHERE id=$id;");
            P(cmd, "$id", id); cmd.ExecuteNonQuery();
        }
    }

    // ---------------- tags / favorites ----------------

    public static List<MedTagDto> Tags(bool tree = false)
    {
        EnsureFavoriteTag();
        var flat = new List<MedTagDto>();
        using var cn = MedDatabase.Instance.Open();
        using (var cmd = Cmd(cn, """
SELECT t.id,t.name,t.parent_id,t.color,t.ord,t.kind,
 (SELECT COUNT(*) FROM med_tagged g WHERE g.tag_id=t.id)
FROM med_tags t ORDER BY t.ord,t.name;
"""))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) flat.Add(new MedTagDto
            {
                Id = r.GetString(0), Name = r.GetString(1), ParentId = r.GetString(2), Color = r.GetString(3),
                Ord = r.GetInt32(4), Kind = r.GetString(5), Count = r.GetInt32(6),
            });
        if (!tree) return flat;
        var byId = flat.ToDictionary(x => x.Id, x => x, StringComparer.Ordinal);
        var roots = new List<MedTagDto>();
        foreach (var t in flat)
        {
            if (t.ParentId.Length > 0 && byId.TryGetValue(t.ParentId, out var p)) p.Children.Add(t);
            else roots.Add(t);
        }
        return roots;
    }

    private static void EnsureFavoriteTag()
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var cmd = Cmd(cn, "INSERT OR IGNORE INTO med_tags(id,name,parent_id,color,ord,kind) VALUES($i,'★ 收藏','','#E8A33D',0,'favorite');");
            P(cmd, "$i", FavoriteTagId);
            cmd.ExecuteNonQuery();
        }
    }

    public static string SaveTag(MedTagDto t)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            if (string.IsNullOrEmpty(t.Id)) t.Id = "tag-" + MedEpub.NewId("");
            using var cmd = Cmd(cn, "INSERT OR REPLACE INTO med_tags(id,name,parent_id,color,ord,kind) VALUES($i,$n,$p,$c,$o,$k);");
            P(cmd, "$i", t.Id); P(cmd, "$n", t.Name); P(cmd, "$p", t.ParentId); P(cmd, "$c", t.Color);
            P(cmd, "$o", t.Ord); P(cmd, "$k", t.Kind);
            cmd.ExecuteNonQuery();
        }
        return t.Id;
    }

    public static void DeleteTag(string id)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using (var c1 = Cmd(cn, "DELETE FROM med_tags WHERE id=$i;")) { P(c1, "$i", id); c1.ExecuteNonQuery(); }
            using (var c2 = Cmd(cn, "DELETE FROM med_tagged WHERE tag_id=$i;")) { P(c2, "$i", id); c2.ExecuteNonQuery(); }
        }
    }

    public static void Tag(string tagId, string targetType, string targetId, string note = "")
    {
        EnsureFavoriteTag();
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var cmd = Cmd(cn, "INSERT OR REPLACE INTO med_tagged(tag_id,target_type,target_id,added_at,note) VALUES($t,$tt,$ti,$now,$n);");
            P(cmd, "$t", tagId); P(cmd, "$tt", targetType); P(cmd, "$ti", targetId); P(cmd, "$now", Now); P(cmd, "$n", note);
            cmd.ExecuteNonQuery();
        }
    }

    public static void Untag(string tagId, string targetType, string targetId)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var cmd = Cmd(cn, "DELETE FROM med_tagged WHERE tag_id=$t AND target_type=$tt AND target_id=$ti;");
            P(cmd, "$t", tagId); P(cmd, "$tt", targetType); P(cmd, "$ti", targetId);
            cmd.ExecuteNonQuery();
        }
    }

    public static List<string> Tagged(string tagId, int limit = 200)
    {
        var list = new List<string>();
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT target_type||':'||target_id FROM med_tagged WHERE tag_id=$t ORDER BY added_at DESC LIMIT $l;");
        P(cmd, "$t", tagId); P(cmd, "$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>收藏/分类的展示项：把 target_type+id 解析成可读标题，供“我的收藏”使用。</summary>
    public static List<MedTaggedItemDto> TaggedItems(string tagId, int limit = 300)
    {
        var list = new List<MedTaggedItemDto>();
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, """
SELECT g.target_type,g.target_id,g.added_at,
 CASE g.target_type
   WHEN 'concept' THEN COALESCE((SELECT name FROM med_concepts WHERE id=g.target_id),'')
   WHEN 'chapter' THEN COALESCE((SELECT title FROM med_chapters WHERE id=g.target_id),'')
   WHEN 'chunk'   THEN COALESCE((SELECT substr(text,1,80) FROM med_chunks WHERE id=g.target_id),'')
   WHEN 'book'    THEN COALESCE((SELECT title FROM med_books WHERE id=g.target_id),'')
   ELSE g.target_id END label,
 CASE g.target_type
   WHEN 'concept' THEN COALESCE((SELECT category FROM med_concepts WHERE id=g.target_id),'')
   WHEN 'chapter' THEN COALESCE((SELECT b.title FROM med_chapters c JOIN med_books b ON b.id=c.book_id WHERE c.id=g.target_id),'')
   WHEN 'chunk'   THEN COALESCE((SELECT c.title FROM med_chunks k JOIN med_chapters c ON c.id=k.chapter_id WHERE k.id=g.target_id),'')
   ELSE '' END sub,
 CASE WHEN g.target_type='concept' THEN COALESCE((SELECT type FROM med_concepts WHERE id=g.target_id),'') ELSE '' END type,
 CASE WHEN g.target_type='chunk' THEN COALESCE((SELECT chapter_id FROM med_chunks WHERE id=g.target_id),'') ELSE '' END chapter_id
FROM med_tagged g WHERE g.tag_id=$t ORDER BY g.added_at DESC LIMIT $l;
""");
        P(cmd, "$t", tagId); P(cmd, "$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MedTaggedItemDto
        {
            TargetType = r.GetString(0), TargetId = r.GetString(1), AddedAt = r.GetInt64(2),
            Label = r.GetString(3), Sub = r.GetString(4), Type = r.GetString(5), ChapterId = r.GetString(6),
        });
        return list;
    }

    public static List<string> TagsOf(string targetType, string targetId)
    {
        var list = new List<string>();
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT tag_id FROM med_tagged WHERE target_type=$tt AND target_id=$ti;");
        P(cmd, "$tt", targetType); P(cmd, "$ti", targetId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    // ---------------- templates / saved queries ----------------

    public static List<MedTemplateDto> Templates(string kind = "", int limit = 800, int builtin = -1, string q = "")
    {
        var list = new List<MedTemplateDto>();
        var where = new List<string>();
        if (kind.Length > 0) where.Add("kind=$k");
        if (builtin >= 0) where.Add("builtin=$b");
        if (q.Length > 0) where.Add("name LIKE $q");
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT * FROM med_templates " +
            (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) + " " : "") +
            "ORDER BY pinned DESC,updated_at DESC LIMIT $l;");
        if (kind.Length > 0) P(cmd, "$k", kind);
        if (builtin >= 0) P(cmd, "$b", builtin);
        if (q.Length > 0) P(cmd, "$q", "%" + q + "%");
        P(cmd, "$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MedTemplateDto
        {
            Id = r.GetString(0), Kind = r.GetString(1), Name = r.GetString(2), SchemaJson = r.GetString(3),
            ContentJson = r.GetString(4), Pinned = r.GetInt32(5), Builtin = r.GetInt32(6),
            CreatedAt = r.GetInt64(7), UpdatedAt = r.GetInt64(8),
        });
        return list;
    }

    public static string SaveTemplate(MedTemplateDto t)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            if (string.IsNullOrEmpty(t.Id)) t.Id = MedEpub.NewId("t");
            if (t.CreatedAt == 0) t.CreatedAt = Now;
            t.UpdatedAt = Now;
            using var cmd = Cmd(cn, """
INSERT OR REPLACE INTO med_templates(id,kind,name,schema_json,content_json,pinned,builtin,created_at,updated_at)
 VALUES($i,$k,$n,$s,$c,$p,$b,$ca,$ua);
""");
            P(cmd, "$i", t.Id); P(cmd, "$k", t.Kind); P(cmd, "$n", t.Name); P(cmd, "$s", t.SchemaJson);
            P(cmd, "$c", t.ContentJson); P(cmd, "$p", t.Pinned); P(cmd, "$b", t.Builtin);
            P(cmd, "$ca", t.CreatedAt); P(cmd, "$ua", t.UpdatedAt);
            cmd.ExecuteNonQuery();
        }
        return t.Id;
    }

    public static void DeleteTemplate(string id)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var cmd = Cmd(cn, "DELETE FROM med_templates WHERE id=$i;");
            P(cmd, "$i", id); cmd.ExecuteNonQuery();
        }
    }

    public static List<MedQueryDto> Queries()
    {
        var list = new List<MedQueryDto>();
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT * FROM med_queries ORDER BY pinned DESC,last_used_at DESC;");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MedQueryDto
        {
            Id = r.GetString(0), Name = r.GetString(1), DslJson = r.GetString(2),
            Pinned = r.GetInt32(3), LastUsedAt = r.GetInt64(4), CreatedAt = r.GetInt64(5),
        });
        return list;
    }

    public static string SaveQuery(MedQueryDto q)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            if (string.IsNullOrEmpty(q.Id)) q.Id = MedEpub.NewId("q");
            if (q.CreatedAt == 0) q.CreatedAt = Now;
            using var cmd = Cmd(cn, "INSERT OR REPLACE INTO med_queries(id,name,dsl_json,pinned,last_used_at,created_at) VALUES($i,$n,$d,$p,$l,$c);");
            P(cmd, "$i", q.Id); P(cmd, "$n", q.Name); P(cmd, "$d", q.DslJson);
            P(cmd, "$p", q.Pinned); P(cmd, "$l", q.LastUsedAt); P(cmd, "$c", q.CreatedAt);
            cmd.ExecuteNonQuery();
        }
        return q.Id;
    }

    public static void DeleteQuery(string id)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var cmd = Cmd(cn, "DELETE FROM med_queries WHERE id=$i;");
            P(cmd, "$i", id); cmd.ExecuteNonQuery();
        }
    }

    // ---------------- books / chapter view ----------------

    public static List<MedBookDto> Books()
    {
        var list = new List<MedBookDto>();
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT * FROM med_books ORDER BY added_at DESC;");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadBook(r));
        return list;
    }

    private static MedBookDto ReadBook(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), BookId = r.GetString(1), Title = r.GetString(2), Volume = r.GetString(3),
        Type = r.GetString(4), Path = r.GetString(5), Status = r.GetString(7),
        AddedAt = r.GetInt64(8), IndexedAt = r.GetInt64(9), DocCount = r.GetInt32(10),
        ChapterCount = r.GetInt32(11), ChunkCount = r.GetInt32(12), CharCount = r.GetInt64(13),
        AnchorTotal = r.GetInt32(14), AnchorResolved = r.GetInt32(15), ImageCount = r.GetInt32(16),
        ElapsedMs = r.GetInt64(17), Error = r.IsDBNull(18) ? null : r.GetString(18),
    };

    public static MedBookDto? Book(string id)
    {
        using var cn = MedDatabase.Instance.Open();
        using var cmd = Cmd(cn, "SELECT * FROM med_books WHERE id=$i;");
        P(cmd, "$i", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadBook(r) : null;
    }

    public static void DeleteBook(string id)
    {
        var db = MedDatabase.Instance;
        lock (db.SyncRoot)
        {
            using var cn = db.Open();
            using var tx = cn.BeginTransaction();
            using (var c0 = Cmd(cn, "DELETE FROM med_fts WHERE chunk_id IN (SELECT id FROM med_chunks WHERE book_id=$b);"))
            { c0.Transaction = tx; P(c0, "$b", id); c0.ExecuteNonQuery(); }
            foreach (var sql in new[]
            {
                "DELETE FROM med_hits WHERE book_id=$b;", "DELETE FROM med_anchors WHERE book_id=$b;",
                "DELETE FROM med_chunks WHERE book_id=$b;",
                "DELETE FROM med_chapters WHERE book_id=$b;", "DELETE FROM med_books WHERE id=$b;",
            })
            {
                using var c = Cmd(cn, sql);
                c.Transaction = tx; P(c, "$b", id); c.ExecuteNonQuery();
            }
            tx.Commit();
        }
        MedConcepts.RebuildAutoLinks();
        MedSearch.Invalidate();
    }

    public static MedChapterViewDto? ChapterView(string chapterId)
    {
        var db = MedDatabase.Instance;
        MedChapterDto? ch = null;
        string bookId = "", bookTitle = "";
        using (var cn = db.Open())
        {
            using var cmd = Cmd(cn, "SELECT c.id,c.book_id,c.parent_id,c.level,c.spine_index,c.href,c.anchor,c.title,c.page_hint,c.ord,c.img_count,b.id,b.title " +
                                   "FROM med_chapters c JOIN med_books b ON b.id=c.book_id WHERE c.id=$i;");
            P(cmd, "$i", chapterId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            ch = new MedChapterDto
            {
                Id = r.GetString(0), BookId = r.GetString(1), ParentId = r.GetString(2), Level = r.GetInt32(3),
                SpineIndex = r.GetInt32(4), Href = r.GetString(5), Anchor = r.GetString(6), Title = r.GetString(7),
                PageHint = r.GetString(8), Ord = r.GetInt32(9), ImgCount = r.GetInt32(10),
            };
            bookId = r.GetString(11); bookTitle = r.GetString(12);
        }
        var book = Book(bookId);
        var view = new MedChapterViewDto
        {
            BookId = bookId, BookTitle = bookTitle, ChapterId = chapterId, Title = ch.Title,
            ChapterPath = ChapterPath(ch), ImageCount = ch.ImgCount,
        };
        if (book != null && File.Exists(book.Path) && book.Type == "epub")
        {
            try
            {
                using var zip = ZipFile.OpenRead(book.Path);
                var html = MedEpub.ReadChapter(zip, ch.Href) ?? "";
                view.Html = MedText.BuildChapterHtml(html, ch.Href, bookId);
            }
            catch (Exception ex) { view.Html = "<p>章节读取失败：" + MedText.EscapeHtml(ex.Message) + "</p>"; }
        }
        else if (book != null && book.Type == "jsonbook")
        {
            var file = Path.Combine(MedJsonBook.BookDir(bookId), "chapters", Path.GetFileName(ch.Href));
            view.Html = File.Exists(file)
                ? File.ReadAllText(file)
                : "<p>章节 HTML 缺失，请在「添加数据源」里重新导入这本 JSON 图书。</p>";
        }
        else if (book != null && book.Type == "markdown")
        {
            using var cn = db.Open();
            using var cmd = Cmd(cn, "SELECT text,p_start FROM med_chunks WHERE chapter_id=$c ORDER BY ord;");
            P(cmd, "$c", chapterId);
            var sb = new StringBuilder();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                sb.Append("<p data-p=\"").Append(r.GetInt32(1)).Append("\">")
                  .Append(MedText.EscapeHtml(r.GetString(0))).Append("</p>");
            view.Html = sb.ToString().Length > 0 ? sb.ToString() : "<p>（本章没有可显示的正文）</p>";
        }

        using (var cn = db.Open())
        {
            using (var cmd = Cmd(cn, "SELECT id,title FROM med_chapters WHERE book_id=$b AND ord<$o ORDER BY ord DESC LIMIT 1;"))
            { P(cmd, "$b", bookId); P(cmd, "$o", ch.Ord); var v = cmd.ExecuteScalar(); view.Prev = v as string ?? ""; }
            using (var cmd = Cmd(cn, "SELECT id,title FROM med_chapters WHERE book_id=$b AND ord>$o ORDER BY ord LIMIT 1;"))
            { P(cmd, "$b", bookId); P(cmd, "$o", ch.Ord); var v = cmd.ExecuteScalar(); view.Next = v as string ?? ""; }
            using (var cmd = Cmd(cn, "SELECT id,book_id,parent_id,level,spine_index,href,anchor,title,page_hint,ord,img_count FROM med_chapters WHERE book_id=$b AND ord BETWEEN $a AND $z ORDER BY ord LIMIT 40;"))
            {
                P(cmd, "$b", bookId); P(cmd, "$a", Math.Max(0, ch.Ord - 12)); P(cmd, "$z", ch.Ord + 27);
                using var r = cmd.ExecuteReader();
                while (r.Read()) view.Siblings.Add(new MedChapterDto
                {
                    Id = r.GetString(0), BookId = r.GetString(1), ParentId = r.GetString(2), Level = r.GetInt32(3),
                    SpineIndex = r.GetInt32(4), Href = r.GetString(5), Anchor = r.GetString(6), Title = r.GetString(7),
                    PageHint = r.GetString(8), Ord = r.GetInt32(9), ImgCount = r.GetInt32(10),
                });
            }
            using (var cmd = Cmd(cn, """
SELECT DISTINCT h.concept_id FROM med_hits h WHERE h.chapter_id=$c ORDER BY h.tf DESC LIMIT 400;
"""))
            {
                P(cmd, "$c", chapterId);
                var ids = new List<string>();
                using (var r = cmd.ExecuteReader()) while (r.Read()) ids.Add(r.GetString(0));
                var all = MedConcepts.All();
                foreach (var cid in ids)
                {
                    var c = all.FirstOrDefault(x => x.Id == cid);
                    if (c != null) view.Concepts.Add(MedConcepts.ToDto(c));
                }
            }
        }
        view.Backlinks = Backlinks("chapter", chapterId);
        view.Anchors = Anchors(chapterId);
        return view;
    }

    public static string ChapterPath(MedChapterDto ch)
    {
        var parts = new List<string> { ch.Title };
        var cur = ch;
        using var cn = MedDatabase.Instance.Open();
        for (int i = 0; i < 5 && cur.ParentId.Length > 0; i++)
        {
            using var cmd = Cmd(cn, "SELECT id,book_id,parent_id,level,spine_index,href,anchor,title,page_hint,ord,img_count FROM med_chapters WHERE id=$i;");
            P(cmd, "$i", cur.ParentId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) break;
            cur = new MedChapterDto
            {
                Id = r.GetString(0), BookId = r.GetString(1), ParentId = r.GetString(2), Level = r.GetInt32(3),
                SpineIndex = r.GetInt32(4), Href = r.GetString(5), Anchor = r.GetString(6), Title = r.GetString(7),
                PageHint = r.GetString(8), Ord = r.GetInt32(9), ImgCount = r.GetInt32(10),
            };
            parts.Insert(0, cur.Title);
        }
        return string.Join(" › ", parts);
    }

    public static MedStatusDto Status()
    {
        var db = MedDatabase.Instance;
        db.Initialize();
        EnsureConceptTags();
        var s = new MedStatusDto
        {
            Ready = true, Version = MedVersion, DbPath = db.DbPath, DbBytes = db.DbBytes,
            SourcesDir = db.SourcesDir, ClinicalDefault = MedConcepts.DefaultClinicalDir,
            ClinicalDefaultExists = Directory.Exists(MedConcepts.DefaultClinicalDir),
        };
        foreach (var (path, volume) in DefaultBookSources)
            s.DefaultBooks.Add(new MedDefaultSourceDto
            {
                Path = path, Volume = volume, Title = Path.GetFileNameWithoutExtension(path), Exists = File.Exists(path),
            });
        using var cn = db.Open();
        foreach (var (table, set) in new (string, Action<long>)[]
        {
            ("med_books", v => s.Books = (int)v), ("med_chapters", v => s.Chapters = (int)v),
            ("med_chunks", v => s.Chunks = (int)v), ("med_concepts", v => s.Concepts = (int)v),
            ("med_notes", v => s.Notes = (int)v), ("med_tags", v => s.Tags = (int)v),
            ("med_templates", v => s.Templates = (int)v), ("med_links", v => s.Links = (int)v),
        })
        {
            var sql = table == "med_concepts"
                ? "SELECT COUNT(*) FROM med_concepts WHERE COALESCE(deleted,0)=0;"
                : "SELECT COUNT(*) FROM " + table + ";";
            using var cmd = Cmd(cn, sql);
            set(Convert.ToInt64(cmd.ExecuteScalar() ?? 0));
        }
        using (var cmd = Cmd(cn, "SELECT COALESCE(SUM(char_count),0) FROM med_books;"))
            s.Chars = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        using (var cmd = Cmd(cn, "SELECT COUNT(*) FROM med_tagged WHERE tag_id=$t;"))
        { P(cmd, "$t", FavoriteTagId); s.Favorites = Convert.ToInt32(cmd.ExecuteScalar() ?? 0); }
        s.ClinicalVersion = db.GetMeta("clinical_version");
        return s;
    }

    private static bool _conceptTagsChecked;

    /// <summary>
    /// 旧版 clinical 导入只建了分类标签、没有把概念挂到标签上，导致分类计数全为 0。
    /// 首次打开医学助手时补一次 concept → tag 关系；新导入在 ImportClinical 中直接建立。
    /// </summary>
    public static int EnsureConceptTags()
    {
        if (_conceptTagsChecked) return 0;
        _conceptTagsChecked = true;
        var inserted = 0;
        try
        {
            var db = MedDatabase.Instance;
            lock (db.SyncRoot)
            {
                using var cn = db.Open();
                long untagged = 0, taggedConcepts = 0;
                using (var c1 = Cmd(cn, "SELECT COUNT(*) FROM med_concepts WHERE category<>'' AND COALESCE(deleted,0)=0;"))
                    untagged = Convert.ToInt64(c1.ExecuteScalar() ?? 0);
                using (var c2 = Cmd(cn, "SELECT COUNT(*) FROM med_tagged WHERE target_type='concept';"))
                    taggedConcepts = Convert.ToInt64(c2.ExecuteScalar() ?? 0);
                if (taggedConcepts >= untagged) return 0;
                var rows = new List<(string Id, string Category)>();
                var tagIds = new Dictionary<string, string>(StringComparer.Ordinal);
                using (var cmd = Cmd(cn, "SELECT id,category FROM med_concepts WHERE category<>'' AND COALESCE(deleted,0)=0;"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) rows.Add((r.GetString(0), r.GetString(1)));
                using (var cmd = Cmd(cn, "SELECT id,name FROM med_tags;"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) tagIds[r.GetString(1)] = r.GetString(0);
                using var tx = cn.BeginTransaction();
                using var tagIns = Cmd(cn, "INSERT OR IGNORE INTO med_tags(id,name,parent_id,color,ord,kind) VALUES($id,$name,'','#5B8FF9',$ord,'category');");
                tagIns.Transaction = tx;
                using var link = Cmd(cn, "INSERT OR IGNORE INTO med_tagged(tag_id,target_type,target_id,added_at,note) VALUES($t,'concept',$id,$now,'');");
                link.Transaction = tx;
                var ord = tagIds.Count;
                foreach (var (id, cat) in rows)
                {
                    if (!tagIds.TryGetValue(cat, out var tagId))
                    {
                        tagId = "tag-" + MedText.NormalizeTitle(cat);
                        tagIds[cat] = tagId;
                        tagIns.Parameters.Clear();
                        P(tagIns, "$id", tagId); P(tagIns, "$name", cat); P(tagIns, "$ord", ++ord);
                        tagIns.ExecuteNonQuery();
                    }
                    link.Parameters.Clear();
                    P(link, "$t", tagId); P(link, "$id", id); P(link, "$now", Now);
                    inserted += link.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }
        catch { }
        return inserted;
    }

    public static MedExportDto Export()
    {
        return new MedExportDto
        {
            Version = MedVersion, ExportedAt = Now, Notes = Notes("", 10000), Tags = Tags(),
            Templates = Templates("", 10000, 0), Queries = Queries(),
        };
    }
}
