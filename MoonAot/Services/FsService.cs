using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MoonAot.Store;
using MoonAot.TinySearch;

namespace MoonAot.Services;

/// <summary>文件分类与 MIME 表。</summary>
public static class FileKinds
{
    public static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma", ".opus", ".ape", ".aiff", ".alac" };

    public static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".flv", ".m3u8", ".mpg", ".mpeg", ".3gp" };

    public static readonly HashSet<string> Image = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff", ".svg", ".avif" };

    public static readonly HashSet<string> Archive = new(StringComparer.OrdinalIgnoreCase)
    { ".zip", ".cbz" };

    public static readonly HashSet<string> Book = new(StringComparer.OrdinalIgnoreCase)
    { ".txt", ".epub", ".pdf", ".mobi", ".azw3", ".md", ".markdown" };

    /// <summary>可用代码编辑器打开并保存的文本格式。</summary>
    public static readonly HashSet<string> Code = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".jsonc", ".cs", ".py", ".pyw", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".vue",
        ".css", ".scss", ".less", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".sql",
        ".sh", ".bash", ".bat", ".cmd", ".ps1", ".java", ".kt", ".kts", ".c", ".h", ".cpp", ".hpp",
        ".cc", ".go", ".rs", ".php", ".rb", ".lua", ".r", ".dart", ".swift", ".asm", ".log", ".env",
        ".props", ".csproj", ".sln", ".gradle", ".editorconfig", ".gitignore",
    };

    public static readonly HashSet<string> Doc = new(StringComparer.OrdinalIgnoreCase) { ".docx" };
    public static readonly HashSet<string> Sheet = new(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xls", ".csv" };
    public static readonly HashSet<string> Slide = new(StringComparer.OrdinalIgnoreCase) { ".pptx" };
    public static readonly HashSet<string> Model = new(StringComparer.OrdinalIgnoreCase)
    { ".fbx", ".obj", ".glb", ".gltf", ".stl", ".3ds", ".dae", ".ply", ".3mf" };
    public static readonly HashSet<string> Cad = new(StringComparer.OrdinalIgnoreCase) { ".dxf" };
    public static readonly HashSet<string> Zim = new(StringComparer.OrdinalIgnoreCase) { ".zim" };

    public static string KindOf(string path)
    {
        var ext = Path.GetExtension(path);
        if (Book.Contains(ext)) return "book";
        if (Audio.Contains(ext)) return "audio";
        if (Video.Contains(ext)) return "video";
        if (Image.Contains(ext)) return "image";
        if (Archive.Contains(ext)) return "zip";
        if (Zim.Contains(ext)) return "zim";
        if (Code.Contains(ext)) return "code";
        if (Doc.Contains(ext)) return "doc";
        if (Sheet.Contains(ext)) return "sheet";
        if (Slide.Contains(ext)) return "slide";
        if (Model.Contains(ext)) return "model";
        if (Cad.Contains(ext)) return "cad";
        return "other";
    }

    public static bool IsMedia(string path) => KindOf(path) is "audio" or "video" or "image";

    public static string MimeOf(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".jsonl" => "application/json",
            ".map" => "application/json",
            ".txt" or ".md" => "text/plain; charset=utf-8",
            ".xml" => "text/xml; charset=utf-8",
            ".csv" => "text/csv; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".tif" or ".tiff" => "image/tiff",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".opus" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".wma" => "audio/x-ms-wma",
            ".ape" => "audio/x-ape",
            ".aiff" => "audio/aiff",
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".ts" => "video/mp2t",
            ".flv" => "video/x-flv",
            ".mpg" or ".mpeg" => "video/mpeg",
            ".3gp" => "video/3gpp",
            ".m3u8" => "application/vnd.apple.mpegurl",
            ".epub" => "application/epub+zip",
            ".pdf" => "application/pdf",
            ".zip" or ".cbz" => "application/zip",
            ".wasm" => "application/wasm",
            ".webmanifest" or ".manifest" => "application/manifest+json",
            ".bcmap" => "application/octet-stream",
            ".pfb" => "application/octet-stream",
            _ => "application/octet-stream",
        };
    }
}

/// <summary>本地文件系统浏览、系统打开/定位。</summary>
public static class FsService
{
    public static FsList List(string path, string kind = "all", int limit = 4000)
    {
        var result = new FsList();
        if (string.IsNullOrWhiteSpace(path))
        {
            result.Path = "";
            return result;
        }
        string dir;
        try
        {
            dir = Path.GetFullPath(path.Trim().Trim('"'));
            if (File.Exists(dir)) dir = Path.GetDirectoryName(dir) ?? dir;
        }
        catch
        {
            result.Path = path;
            return result;
        }
        result.Path = dir;
        try
        {
            var parent = Directory.GetParent(dir);
            result.Parent = parent?.FullName;
        }
        catch { result.Parent = null; }

        if (!Directory.Exists(dir)) return result;

        var entries = new List<FsEntry>();
        try
        {
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = 0,
            };
            foreach (var fsi in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", opts))
            {
                try
                {
                    bool isDir = (fsi.Attributes & FileAttributes.Directory) != 0;
                    if (isDir)
                    {
                        var name = fsi.Name;
                        if (name == "$RECYCLE.BIN" || name == "System Volume Information") continue;
                        if ((fsi.Attributes & FileAttributes.System) != 0) continue;
                        entries.Add(new FsEntry
                        {
                            Name = name, Path = fsi.FullName, IsDir = true,
                            Modified = SafeMs(fsi), Kind = "dir",
                        });
                    }
                    else
                    {
                        var k = FileKinds.KindOf(fsi.FullName);
                        if (!Match(kind, k)) continue;
                        entries.Add(new FsEntry
                        {
                            Name = fsi.Name,
                            Path = fsi.FullName,
                            IsDir = false,
                            Size = SafeLen(fsi),
                            Modified = SafeMs(fsi),
                            Kind = k,
                            Ext = fsi.Extension.ToLowerInvariant(),
                        });
                    }
                    if (entries.Count >= limit)
                    {
                        result.Truncated = true;
                        break;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("浏览目录失败 " + dir + ": " + ex.Message);
        }

        entries.Sort((a, b) =>
        {
            if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
            var c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
        result.Entries = entries;
        result.Total = entries.Count;
        return result;
    }

    private static bool Match(string kind, string fileKind) => kind switch
    {
        "" or "all" or "any" => fileKind != "other",
        "media" => fileKind is "audio" or "video" or "image",
        "audio" => fileKind == "audio",
        "video" => fileKind == "video",
        "image" => fileKind == "image",
        "books" => fileKind == "book",
        "zip" => fileKind == "zip",
        _ => fileKind != "other",
    };

    /// <summary>递归扫描文件夹中的指定类型文件（播放器“打开文件夹”用）。</summary>
    public static FsList Scan(string path, string kind = "media", int limit = 10_000_000)
    {
        var result = new FsList { Path = path };
        string full;
        try { full = Path.GetFullPath(path.Trim().Trim('"')); }
        catch { return result; }
        result.Path = full;
        try { result.Parent = Directory.GetParent(full)?.FullName; } catch { }
        if (!Directory.Exists(full)) return result;
        if (limit <= 0) limit = 10_000_000;
        limit = Math.Min(limit, 10_000_000);

        var entries = new List<FsEntry>();
        if (TinySearchService.TryEnumerateFiles(full, ExtsForKind(kind), limit, out var indexed))
        {
            foreach (var f in indexed)
            {
                entries.Add(new FsEntry
                {
                    Name = f.Name,
                    Path = f.FullPath,
                    IsDir = false,
                    Size = f.Size,
                    Modified = TicksToMs(f.ModifiedUtc),
                    Kind = FileKinds.KindOf(f.FullPath),
                    Ext = "." + f.Extension,
                });
            }
            result.Truncated = indexed.Count >= limit;
        }
        else
        {
            try
            {
                var opts = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden,
                    MaxRecursionDepth = 32,
                };
                foreach (var file in Directory.EnumerateFiles(full, "*", opts))
                {
                    var k = FileKinds.KindOf(file);
                    if (!Match(kind, k)) continue;
                    if (entries.Count >= limit) { result.Truncated = true; break; }
                    var fi = new FileInfo(file);
                    entries.Add(new FsEntry
                    {
                        Name = fi.Name,
                        Path = fi.FullName,
                        IsDir = false,
                        Size = SafeLen(fi),
                        Modified = SafeMs(fi),
                        Kind = k,
                        Ext = fi.Extension.ToLowerInvariant(),
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("递归扫描失败 " + full + ": " + ex.Message);
            }
        }
        entries.Sort((a, b) =>
        {
            var c = string.Compare(Path.GetDirectoryName(a.Path), Path.GetDirectoryName(b.Path),
                StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        result.Entries = entries;
        result.Total = entries.Count;
        return result;
    }

    private static readonly HashSet<string> MediaExts = new(
        FileKinds.Audio.Concat(FileKinds.Video).Concat(FileKinds.Image), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> KnownExts = new(
        FileKinds.Audio.Concat(FileKinds.Video).Concat(FileKinds.Image)
            .Concat(FileKinds.Book).Concat(FileKinds.Archive).Concat(FileKinds.Code)
            .Concat(FileKinds.Doc).Concat(FileKinds.Sheet).Concat(FileKinds.Slide)
            .Concat(FileKinds.Model).Concat(FileKinds.Cad).Concat(FileKinds.Zim),
        StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> ExtsForKind(string kind) => kind switch
    {
        "audio" => FileKinds.Audio,
        "video" => FileKinds.Video,
        "image" => FileKinds.Image,
        "books" => FileKinds.Book,
        "zip" => FileKinds.Archive,
        "media" => MediaExts,
        _ => KnownExts,
    };

    private static long TicksToMs(long ticks)
    {
        if (ticks <= 0) return 0;
        try { return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc)).ToUnixTimeMilliseconds(); }
        catch { return 0; }
    }

    private static long SafeLen(FileSystemInfo fsi)
    {
        try { return fsi is FileInfo fi ? fi.Length : 0; } catch { return 0; }
    }

    private static long SafeMs(FileSystemInfo fsi)
    {
        try { return new DateTimeOffset(fsi.LastWriteTime).ToUnixTimeMilliseconds(); } catch { return 0; }
    }

    public static FsRoots Roots()
    {
        var r = new FsRoots();
        void Add(string name, Environment.SpecialFolder folder, string kind = "folder")
        {
            try
            {
                var p = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                    r.Roots.Add(new FsRoot { Name = name, Path = p, Kind = kind });
            }
            catch { }
        }
        Add("桌面", Environment.SpecialFolder.Desktop);
        Add("文档", Environment.SpecialFolder.MyDocuments);
        Add("下载", Environment.SpecialFolder.UserProfile);
        Add("图片", Environment.SpecialFolder.MyPictures, "image");
        Add("音乐", Environment.SpecialFolder.MyMusic, "audio");
        Add("视频", Environment.SpecialFolder.MyVideos, "video");
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) r.Roots.Add(new FsRoot { Name = "下载", Path = downloads });
        // 去重（"下载" 上面用 UserProfile 占位，改为真正 Downloads）
        r.Roots = r.Roots.Where(x => x.Path != Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            || x.Name != "下载").ToList();

        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    r.Drives.Add(new FsRoot
                    {
                        Name = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name : d.VolumeLabel + " (" + d.Name.TrimEnd('\\') + ")",
                        Path = d.RootDirectory.FullName,
                        Kind = "drive",
                    });
                }
                catch { }
            }
        }
        catch { }
        r.Recent = DbStore.Instance.AppSettings.RecentFolders
            .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        return r;
    }

    public static void RememberFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        DbStore.Instance.UpdateAppSettingsDeferred(s =>
        {
            s.LastFolder = path;
            s.RecentFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            s.RecentFolders.Insert(0, path);
            if (s.RecentFolders.Count > 16) s.RecentFolders.RemoveRange(16, s.RecentFolders.Count - 16);
        });
    }

    public static bool OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("打开失败 " + path + ": " + ex.Message);
            return false;
        }
    }

    public static bool RevealPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            var args = Directory.Exists(path) ? "\"" + path + "\"" : "/select,\"" + path + "\"";
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = args, UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("定位失败 " + path + ": " + ex.Message);
            return false;
        }
    }

    /// <summary>同目录下指定类型的文件（播放器列表用）。</summary>
    public static List<ImageItem> Siblings(string filePath, string kind)
    {
        var list = new List<ImageItem>();
        try
        {
            var full = Path.GetFullPath(filePath);
            var dir = Path.GetDirectoryName(full);
            if (dir == null || !Directory.Exists(dir)) return list;
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var k = FileKinds.KindOf(f);
                if (kind == "media" ? !FileKinds.IsMedia(f) : k != kind) continue;
                long size = 0;
                try { size = new FileInfo(f).Length; } catch { }
                list.Add(new ImageItem { Name = Path.GetFileName(f), Path = f, Size = size });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }
        catch { }
        return list;
    }
}
