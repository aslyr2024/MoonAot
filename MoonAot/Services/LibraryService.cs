using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>
/// 书库服务：导入（本地路径/网页上传/文件夹递归）、元数据、封面/缩略图、删除、统计、后台维护任务。
/// 本地书籍默认「只登记路径、不复制文件」；网页上传与拖拽导入才写入 LibraryFiles。
/// </summary>
public sealed class LibraryService
{
    public static LibraryService Instance { get; } = new();

    public static readonly string[] BookExt =
        { ".txt", ".epub", ".pdf", ".mobi", ".azw3", ".md", ".markdown" };

    public string LibraryFilesDir => DbStore.Instance.LibraryFilesDir;

    public event Action? Changed;

    private LibraryService() { }

    public void NotifyChanged() => Changed?.Invoke();

    // ==================== 导入：本地路径 ====================

    public BookRecord? ImportFile(string path, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { error = "文件不存在"; return null; }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!BookFormatEx.IsBookExt(ext)) { error = "不支持的格式：" + ext; return null; }

        var existing = DbStore.Instance.FindByPath(path);
        if (existing != null) return existing;

        var book = BuildRecord(path, fast: false);
        if (!DbStore.Instance.AddBook(book)) return DbStore.Instance.FindByPath(path);
        try
        {
            EnsureCover(book, writeThumb: true);
        }
        catch (Exception ex)
        {
            Log.Warn("导入后提取封面失败: " + ex.Message);
        }
        Changed?.Invoke();
        return DbStore.Instance.GetBook(book.Id) ?? book;
    }

    private BookRecord BuildRecord(string path, bool fast)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }
        var book = new BookRecord
        {
            Title = MetadataService.CleanFileNameTitle(Path.GetFileNameWithoutExtension(path)),
            Author = "",
            FilePath = path,
            SourcePath = path,
            Format = ExtToFormat(ext),
            FileSize = size,
            AddedAt = DateTime.Now,
        };
        MetadataService.Enrich(book, fast);
        if (string.IsNullOrWhiteSpace(book.Title))
            book.Title = MetadataService.CleanFileNameTitle(Path.GetFileNameWithoutExtension(path));
        return book;
    }

    /// <summary>
    /// 批量导入磁盘路径（文件夹导入走这里）。
    /// 只登记路径，不复制文件；封面按需在书架滚动到时提取，避免 10 万本导入时卡住。
    /// </summary>
    public ImportResultDto ImportPaths(
        IEnumerable<string> paths,
        Action<int, int, string>? progress = null,
        CancellationToken token = default)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if (!BookFormatEx.IsBookExt(ext)) continue;
            if (!seen.Add(p)) continue;
            files.Add(p);
        }

        int total = files.Count;
        if (total == 0) return new ImportResultDto { Ok = true };
        bool fast = total > 500;
        bool importCovers = DbStore.Instance.AppSettings.ImportCovers;
        int done = 0, imported = 0, skipped = 0, failed = 0;
        var batch = new List<BookRecord>(512);
        var coverBooks = new ConcurrentBag<BookRecord>();
        var gate = new object();

        void FlushBatch()
        {
            List<BookRecord>? copy = null;
            lock (gate)
            {
                if (batch.Count == 0) return;
                copy = new List<BookRecord>(batch);
                batch.Clear();
            }
            var n = DbStore.Instance.AddBooks(copy);
            Interlocked.Add(ref imported, n);
            Interlocked.Add(ref skipped, copy.Count - n);
            if (importCovers)
            {
                foreach (var b in copy)
                    if (IsCoverableFormat(b.Format)) coverBooks.Add(b);
            }
        }

        var existing = DbStore.Instance.AllFilePaths();
        progress?.Invoke(0, total, "");
        try
        {
            Parallel.ForEach(files,
                new ParallelOptions { MaxDegreeOfParallelism = fast ? 4 : 2, CancellationToken = token },
                file =>
                {
                    try
                    {
                        if (existing.Contains(file))
                        {
                            Interlocked.Increment(ref skipped);
                        }
                        else
                        {
                            var book = BuildRecord(file, fast);
                            lock (gate) batch.Add(book);
                            if (batch.Count >= 512) FlushBatch();
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        Log.Warn($"导入失败 {file}: {ex.Message}");
                    }
                    finally
                    {
                        var d = Interlocked.Increment(ref done);
                        if (d % 25 == 0 || d == total) progress?.Invoke(d, total, file);
                    }
                });
        }
        catch (OperationCanceledException) { }

        FlushBatch();
        progress?.Invoke(total, total, "正在写入书库…");
        DbStore.Instance.FlushDirty();
        Changed?.Invoke();
        if (importCovers && !coverBooks.IsEmpty)
            StartCoverBackfill(coverBooks.ToList(), "导入补齐封面");
        return new ImportResultDto
        {
            Ok = true,
            Imported = imported,
            Skipped = skipped,
            Failed = failed,
        };
    }

    public ImportResultDto ImportFolder(string folder, Action<int, int, string>? progress = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return new ImportResultDto { Ok = false, Error = "文件夹不存在" };

        progress?.Invoke(0, 0, "正在枚举文件…");
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            MaxRecursionDepth = 32,
        };
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", opts))
            {
                if (token.IsCancellationRequested) break;
                if (BookFormatEx.IsBookExt(Path.GetExtension(file))) files.Add(file);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("枚举文件夹失败: " + ex.Message);
        }
        progress?.Invoke(0, files.Count, "找到 " + files.Count + " 本，开始导入…");
        return ImportPaths(files, progress, token);
    }

    // ==================== 导入：网页上传 ====================

    public BookRecord? ImportUploaded(string fileName, long size, string uploadedTempFile, out string error)
    {
        error = "";
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".txt";
        if (!BookFormatEx.IsBookExt(ext)) { error = "不支持的格式：" + ext; return null; }

        var book = new BookRecord
        {
            Title = MetadataService.CleanFileNameTitle(Path.GetFileNameWithoutExtension(fileName ?? "book")),
            Author = "",
            Format = ExtToFormat(ext),
            FileSize = size,
            AddedAt = DateTime.Now,
            SourcePath = null,
        };
        var target = Path.Combine(LibraryFilesDir, book.Id + ext);
        try
        {
            Directory.CreateDirectory(LibraryFilesDir);
            File.Move(uploadedTempFile, target, true);
        }
        catch (Exception ex)
        {
            error = "保存上传文件失败：" + ex.Message;
            return null;
        }
        book.FilePath = target;
        book.FileSize = new FileInfo(target).Length;
        MetadataService.Enrich(book, fast: false);
        if (!DbStore.Instance.AddBook(book))
        {
            DbStore.Instance.FlushDirty();
            return DbStore.Instance.FindByPath(target);
        }
        try { EnsureCover(book, writeThumb: true); } catch { }
        Changed?.Invoke();
        return DbStore.Instance.GetBook(book.Id) ?? book;
    }

    public static string ExtToFormat(string ext) => ext switch
    {
        ".epub" => BookFormatEx.Epub,
        ".pdf" => BookFormatEx.Pdf,
        ".mobi" => BookFormatEx.Mobi,
        ".azw3" => BookFormatEx.Azw3,
        ".md" or ".markdown" => BookFormatEx.Md,
        _ => BookFormatEx.Txt,
    };

    // ==================== 封面 ====================

    /// <summary>确保封面已提取（EPUB/PDF/MOBI/AZW3）；返回封面文件路径。</summary>
    public string? EnsureCover(BookRecord book, bool writeThumb = false)
    {
        var existing = DbStore.Instance.GetCoverFile(book.Id);
        if (existing == null && !string.IsNullOrEmpty(book.CoverFile) && File.Exists(book.CoverFile))
            existing = book.CoverFile;
        if (existing == null &&
            File.Exists(book.FilePath) &&
            book.Format is BookFormatEx.Epub or BookFormatEx.Pdf or BookFormatEx.Mobi or BookFormatEx.Azw3)
        {
            try
            {
                var data = CoverService.Extract(book.FilePath, book.Format, out var ext);
                if (data != null && data.Length > 256)
                {
                    var path = ThumbnailService.SaveExtractedCover(book.Id, data, ext);
                    if (path == null) return null;
                    DbStore.Instance.UpdateBook(book.Id, b =>
                    {
                        b.CoverFile = path;
                        b.CoverExt = Path.GetExtension(path).ToLowerInvariant();
                        b.CoverVersion++;
                    });
                    existing = path;
                }
                else ThumbnailService.MarkNoCover(book.Id);
            }
            catch (Exception ex)
            {
                Log.Warn($"提取封面失败 {book.Id}: {ex.Message}");
            }
        }
        if (writeThumb && existing != null)
        {
            try { ThumbnailService.GetBookThumb(DbStore.Instance.GetBook(book.Id) ?? book); } catch { }
        }
        return existing;
    }

    public string? GetCoverPath(BookRecord book) => DbStore.Instance.GetCoverFile(book.Id)
        ?? (!string.IsNullOrEmpty(book.CoverFile) && File.Exists(book.CoverFile) ? book.CoverFile : null);

    public bool ReplaceCoverFromDataUrl(string bookId, string dataUrl, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(dataUrl) || !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            error = "仅支持图片 dataUrl";
            return false;
        }
        var m = Regex.Match(dataUrl, @"^data:image/(png|jpe?g|webp|gif|bmp);base64,(?<d>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) { error = "dataUrl 格式错误"; return false; }
        byte[] data;
        try { data = Convert.FromBase64String(m.Groups["d"].Value); }
        catch { error = "base64 解码失败"; return false; }
        if (data.Length < 256) { error = "图片内容过小"; return false; }
        var kind = m.Groups[1].Value.ToLowerInvariant();
        var ext = kind switch { "png" => ".png", "webp" => ".webp", "gif" => ".gif", "bmp" => ".bmp", _ => ".jpg" };
        string? path;
        try { path = ThumbnailService.SaveExtractedCover(bookId, data, ext); }
        catch (Exception ex) { error = "写入封面失败：" + ex.Message; return false; }
        if (path == null) { error = "写入封面失败"; return false; }
        ext = Path.GetExtension(path).ToLowerInvariant();

        try { File.Delete(DbStore.Instance.ThumbPath(bookId)); } catch { }
        try { File.Delete(DbStore.Instance.NoCoverMarker(bookId)); } catch { }
        DbStore.Instance.UpdateBook(bookId, b =>
        {
            b.CoverFile = path;
            b.CoverExt = ext;
            b.CoverVersion++;
        });
        return true;
    }

    public int SetDefaultCoverFromDataUrl(string dataUrl)
    {
        var m = Regex.Match(dataUrl, @"^data:image/(png|jpe?g|webp|gif|bmp);base64,(?<d>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) throw new ArgumentException("dataUrl 格式错误");
        var data = Convert.FromBase64String(m.Groups["d"].Value);
        var kind = m.Groups[1].Value.ToLowerInvariant();
        var ext = kind switch { "png" => ".png", "webp" => ".webp", "gif" => ".gif", "bmp" => ".bmp", _ => ".jpg" };
        var dir = DbStore.Instance.CoversDir;
        foreach (var old in Directory.GetFiles(dir, "_default.*"))
        {
            try { File.Delete(old); } catch { }
        }
        var raw = Path.Combine(dir, "_default.src" + ext);
        WriteAtomic(raw, data);
        var jpg = Path.Combine(dir, "_default.jpg");
        var path = ThumbnailService.CompressFileToJpeg(raw, jpg) ? jpg : Path.Combine(dir, "_default" + ext);
        if (!string.Equals(path, jpg, StringComparison.OrdinalIgnoreCase)) WriteAtomic(path, data);
        try { File.Delete(raw); } catch { }
        DbStore.Instance.UpdateAppSettings(s => s.DefaultCoverPath = path);
        return CountWithoutCover();
    }

    public int CountWithoutCover() => DbStore.Instance.CountWithoutCover();

    public string? DefaultCoverPath()
    {
        var p = DbStore.Instance.AppSettings.DefaultCoverPath;
        return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
    }

    // ==================== 删除 / 清理 ====================

    /// <summary>从书架移除。只删除应用托管的副本，绝不动用户原文件。</summary>
    public bool RemoveBook(string id, out string error)
    {
        error = "";
        var book = DbStore.Instance.GetBook(id);
        if (book == null) { error = "书籍不存在"; return false; }
        DbStore.Instance.RemoveCover(id);
        if (!DbStore.Instance.RemoveBook(id, out _)) { error = "移除失败"; return false; }

        var imported = string.IsNullOrEmpty(book.SourcePath);
        if (imported && !string.IsNullOrEmpty(book.FilePath) && File.Exists(book.FilePath))
        {
            try
            {
                var full = Path.GetFullPath(book.FilePath);
                var root = Path.GetFullPath(LibraryFilesDir);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) File.Delete(full);
            }
            catch (Exception ex) { Log.Warn("删除导入副本失败: " + ex.Message); }
        }
        Changed?.Invoke();
        return true;
    }

    public int RemoveBooks(IEnumerable<string> ids)
    {
        int n = 0;
        foreach (var id in ids.ToList())
        {
            if (RemoveBook(id, out _)) n++;
        }
        if (n > 0) Changed?.Invoke();
        return n;
    }

    /// <summary>清理失效记录（只删登记，不删任何文件）。</summary>
    public JobStatus StartPurgeMissing()
    {
        var all = DbStore.Instance.AllPaths();
        var job = Jobs.Start("purge", all.Count);
        _ = Task.Run(() =>
        {
            var token = Jobs.TokenOf(job.Id);
            var missing = new List<string>();
            int done = 0;
            foreach (var (id, path) in all)
            {
                if (token.IsCancellationRequested) break;
                if (!File.Exists(path)) missing.Add(id);
                if (++done % 1000 == 0) Jobs.Update(job.Id, done, all.Count, "已检查 " + done);
            }
            var removed = 0;
            if (missing.Count > 0)
            {
                foreach (var id in missing) DbStore.Instance.RemoveCover(id);
                removed = DbStore.Instance.RemoveBooks(missing);
                DbStore.Instance.FlushDirty();
                Changed?.Invoke();
            }
            Jobs.Update(job.Id, all.Count, all.Count);
            Jobs.Finish(job.Id, $"已清理 {removed} 条失效记录");
        });
        return job;
    }

    private static bool IsCoverableFormat(string format) =>
        format is BookFormatEx.Epub or BookFormatEx.Pdf or BookFormatEx.Mobi or BookFormatEx.Azw3;

    private readonly SemaphoreSlim _coverBackfillGate = new(1, 1);

    /// <summary>启动时补齐缺失封面（只处理有封面的格式，后台串行，不阻塞书架）。</summary>
    public void StartMissingCoverBackfillIfNeeded()
    {
        try
        {
            if (!DbStore.Instance.AppSettings.ImportCovers) return;
            var missing = DbStore.Instance.ListMissingCoverable();
            if (missing.Count > 0) StartCoverBackfill(missing, "启动补齐封面");
        }
        catch (Exception ex) { Log.Warn("启动封面补齐失败: " + ex.Message); }
    }

    /// <summary>后台提取封面并生成缩略图；任务之间串行，避免并发磁盘抖动。</summary>
    private void StartCoverBackfill(List<BookRecord> books, string label)
    {
        if (books.Count == 0) return;
        var job = Jobs.Start("covers", books.Count);
        _ = Task.Run(async () =>
        {
            await _coverBackfillGate.WaitAsync();
            var token = Jobs.TokenOf(job.Id);
            int done = 0, generated = 0;
            try
            {
                foreach (var b in books)
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        if (DbStore.Instance.GetCoverFile(b.Id) == null && !ThumbnailService.HasNoCoverMarker(b.Id))
                        {
                            if (EnsureCover(b, writeThumb: true) != null) generated++;
                        }
                    }
                    catch { }
                    done++;
                    if (done % 25 == 0)
                        Jobs.Update(job.Id, done, books.Count, $"{label} {done}/{books.Count}，成功 {generated}");
                    if (done % 250 == 0) DbStore.Instance.FlushDirty();
                    if (done % 1000 == 0) Changed?.Invoke();
                }
                DbStore.Instance.FlushDirty();
                Jobs.Update(job.Id, books.Count, books.Count);
                Jobs.Finish(job.Id, $"{label}完成：{generated} 本");
                Changed?.Invoke();
                Log.Info($"{label}：处理 {done} 本，生成 {generated} 本封面");
            }
            catch (Exception ex)
            {
                Log.Warn(label + "失败: " + ex.Message);
                Jobs.Finish(job.Id, label + "失败：" + ex.Message);
            }
            finally
            {
                _coverBackfillGate.Release();
            }
        });
    }

    /// <summary>
    /// 启动维护：把存量超大封面压到 ≤50KB、超大书籍缩略图压到 ≤40KB。
    /// 只做图片重编码，不解析书籍，跑完一次后再启动几乎无开销。
    /// </summary>
    public void StartCoverHousekeeping(Action? onCompleted = null)
    {
        _ = Task.Run(() =>
        {
            int compressed = 0, thumbs = 0;
            try
            {
                var coversDir = DbStore.Instance.CoversDir;
                foreach (var f in Directory.EnumerateFiles(coversDir))
                {
                    try
                    {
                        var name = Path.GetFileName(f);
                        if (name.Contains(".src", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        var len = new FileInfo(f).Length;
                        if (len <= ThumbnailService.MaxCoverBytes) continue;

                        var stem = Path.GetFileNameWithoutExtension(f);
                        if (stem.StartsWith("_default", StringComparison.OrdinalIgnoreCase))
                        {
                            var jpg = Path.Combine(coversDir, "_default.jpg");
                            if (ThumbnailService.CompressFileToJpeg(f, jpg))
                            {
                                if (!string.Equals(f, jpg, StringComparison.OrdinalIgnoreCase))
                                    try { File.Delete(f); } catch { }
                                DbStore.Instance.UpdateAppSettings(s => s.DefaultCoverPath = jpg);
                                compressed++;
                            }
                            continue;
                        }

                        var outPath = Path.Combine(coversDir, stem + ".jpg");
                        if (ThumbnailService.CompressFileToJpeg(f, outPath))
                        {
                            if (!string.Equals(f, outPath, StringComparison.OrdinalIgnoreCase))
                                try { File.Delete(f); } catch { }
                            DbStore.Instance.UpdateCoverMeta(stem, outPath, ".jpg");
                            compressed++;
                        }
                    }
                    catch { }
                }

                // 书架直接使用封面主文件，旧版书籍缩略图全部清理（保留 .none 标记与 thumbs/path 媒体缩略图）
                long freed = 0;
                foreach (var f in Directory.EnumerateFiles(DbStore.Instance.ThumbsDir, "*.jpg", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var len = new FileInfo(f).Length;
                        File.Delete(f);
                        freed += len;
                        thumbs++;
                    }
                    catch { }
                }
                if (compressed > 0 || thumbs > 0)
                    Log.Info($"封面体积维护完成：封面 {compressed} 张；清理旧书籍缩略图 {thumbs} 张，释放 {freed / 1024 / 1024}MB");
            }
            catch (Exception ex) { Log.Warn("封面体积维护失败: " + ex.Message); }
            finally
            {
                try { onCompleted?.Invoke(); } catch { }
            }
        });
    }

    /// <summary>重建缩略图（保留用户自定义封面，只重新生成缩略图；无封面的书尝试重新提取）。</summary>
    public JobStatus StartRebuildCovers()
    {
        var all = DbStore.Instance.ListForCovers();
        var job = Jobs.Start("covers", all.Count);
        _ = Task.Run(() =>
        {
            var token = Jobs.TokenOf(job.Id);
            int done = 0, generated = 0;
            foreach (var b in all)
            {
                if (token.IsCancellationRequested) break;
                try
                {
                    var hasCover = !string.IsNullOrEmpty(b.CoverFile) && File.Exists(b.CoverFile);
                    var coverable = IsCoverableFormat(b.Format);
                    if (!hasCover && !coverable)
                    {
                        done++;
                        continue;
                    }
                    File.Delete(DbStore.Instance.ThumbPath(b.Id));
                    File.Delete(DbStore.Instance.NoCoverMarker(b.Id));
                    if (coverable) EnsureCover(b, writeThumb: true);
                    else ThumbnailService.GetBookThumb(DbStore.Instance.GetBook(b.Id) ?? b);
                    if (DbStore.Instance.GetCoverFile(b.Id) != null) generated++;
                }
                catch { }
                if (++done % 50 == 0)
                {
                    Jobs.Update(job.Id, done, all.Count, "已处理 " + done + "，生成 " + generated);
                    DbStore.Instance.FlushDirty();
                }
            }
            DbStore.Instance.FlushDirty();
            Jobs.Update(job.Id, all.Count, all.Count);
            Jobs.Finish(job.Id, $"完成：{generated} 本有封面");
            Changed?.Invoke();
        });
        return job;
    }

    /// <summary>批量补齐旧书的作者/出版社/简介等元数据（只填空字段）。</summary>
    public JobStatus StartRepairMetadata()
    {
        var list = DbStore.Instance.ListForMetadataRepair();
        var job = Jobs.Start("metadata", list.Count);
        _ = Task.Run(() =>
        {
            var token = Jobs.TokenOf(job.Id);
            var results = new System.Collections.Concurrent.ConcurrentBag<BookRecord>();
            int done = 0;
            try
            {
                Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = 4 }, rec =>
                {
                    if (token.IsCancellationRequested) return;
                    try
                    {
                        if (!File.Exists(rec.FilePath)) return;
                        var b = new BookRecord
                        {
                            Id = rec.Id, Title = rec.Title, Author = rec.Author,
                            FilePath = rec.FilePath, Format = rec.Format, AddedAt = rec.AddedAt,
                        };
                        MetadataService.Enrich(b, fast: false);
                        if (!string.IsNullOrWhiteSpace(b.Author) ||
                            !string.IsNullOrWhiteSpace(b.Publisher) ||
                            !string.IsNullOrWhiteSpace(b.Description) ||
                            !string.IsNullOrWhiteSpace(b.Language) ||
                            !string.IsNullOrWhiteSpace(b.Category))
                        {
                            results.Add(b);
                        }
                    }
                    catch { }
                    var d = Interlocked.Increment(ref done);
                    if (d % 50 == 0) Jobs.Update(job.Id, d, list.Count, $"正在解析 {d}/{list.Count}");
                });
            }
            catch (OperationCanceledException) { }

            var all = results.ToList();
            Jobs.Update(job.Id, done, list.Count, "正在写入数据库…");
            int updated = 0;
            for (int i = 0; i < all.Count; i += 200)
            {
                if (token.IsCancellationRequested) break;
                updated += DbStore.Instance.UpdateMetadataBatch(all.Skip(i).Take(200).ToList());
            }
            Jobs.Update(job.Id, list.Count, list.Count);
            Jobs.Finish(job.Id, $"已补齐 {updated} 本（解析 {done} 本）");
            Changed?.Invoke();
        });
        return job;
    }

    /// <summary>删除 %LOCALAPPDATA%\MoonAot\data 下已迁移的旧版 JSON；不涉及原书文件/封面/当前数据库。</summary>
    public bool CleanupLegacyJson(out int files, out long bytes, out string error)
    {
        files = 0; bytes = 0; error = "";
        var oldData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonAot", "data");
        if (!Directory.Exists(oldData)) { error = "没有旧版 JSON 数据"; return false; }
        try
        {
            var old = Path.GetFullPath(oldData);
            var cur = Path.GetFullPath(DbStore.Instance.DataDir);
            if (old.StartsWith(cur, StringComparison.OrdinalIgnoreCase) ||
                cur.StartsWith(old, StringComparison.OrdinalIgnoreCase))
            {
                error = "旧目录与当前数据目录重叠，已拒绝删除";
                return false;
            }
            if (DbStore.Instance.Count == 0) { error = "当前 SQLite 书库为空，已拒绝删除"; return false; }
            foreach (var f in Directory.EnumerateFiles(oldData, "*", SearchOption.AllDirectories))
            {
                try { files++; bytes += new FileInfo(f).Length; } catch { }
            }
            Directory.Delete(oldData, true);
            Log.Info($"已清理旧版 JSON: {files} 个文件，{bytes / 1024 / 1024} MB");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ==================== 统计 ====================

    public StatsDto BuildStats()
    {
        var dto = DbStore.Instance.BuildStats();
        long coversBytes = 0;
        int covers = 0;
        try
        {
            foreach (var f in Directory.GetFiles(DbStore.Instance.CoversDir))
            {
                if (!DbStore.IsImageExt(Path.GetExtension(f))) continue;
                covers++;
                coversBytes += new FileInfo(f).Length;
            }
        }
        catch { }
        dto.Covers = covers;
        dto.CoversBytes = coversBytes;
        long dbBytes = 0;
        try { dbBytes = new FileInfo(Store.Database.Instance.DbPath).Length; } catch { }
        dto.StorageBytes = dbBytes + coversBytes;
        return dto;
    }

    public static string ReadTxt(string path) => TextCodec.ReadTxt(path);

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, true);
    }
}
