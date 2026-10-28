using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>阅读器 SystemSettings JSON 的局部修改工具（保持未知字段原样）。</summary>
public static class SettingsJson
{
    /// <summary>把 JSON 中 section.key 设为 bool，section 不存在则追加。</summary>
    public static string SetBool(string json, string section, string key, bool value)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            json = DbStore.DefaultReaderSettings;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return json;
            using var ms = new MemoryStream();
            using (var w = new System.Text.Json.Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                bool sectionFound = false;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.NameEquals(section))
                    {
                        sectionFound = true;
                        w.WritePropertyName(section);
                        w.WriteStartObject();
                        bool keyFound = false;
                        if (p.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            foreach (var q in p.Value.EnumerateObject())
                            {
                                if (q.NameEquals(key)) { w.WriteBoolean(key, value); keyFound = true; }
                                else q.WriteTo(w);
                            }
                        }
                        if (!keyFound) w.WriteBoolean(key, value);
                        w.WriteEndObject();
                    }
                    else
                    {
                        p.WriteTo(w);
                    }
                }
                if (!sectionFound)
                {
                    w.WritePropertyName(section);
                    w.WriteStartObject();
                    w.WriteBoolean(key, value);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch { return json; }
    }
}

/// <summary>原生多选文件对话框（GetOpenFileNameW，AOT 友好）。</summary>
internal static class FilePicker
{
    private const int OFN_READONLY = 0x00000001;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_HIDEREADONLY = 0x00000004;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_ALLOWMULTISELECT = 0x00000200;
    private const int OFN_EXPLORER = 0x00080000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAMEW lpofn);

    /// <summary>选择单个文件（音频/图片/视频/任意）；取消返回 null。</summary>
    public static string? PickFile(IntPtr owner, string kind, string? title = null)
    {
        var (filter, caption) = kind.ToLowerInvariant() switch
        {
            "audio" => ("音频文件 (*.mp3;*.flac;*.wav;*.ogg;*.m4a;*.aac;*.wma;*.opus)\0*.mp3;*.flac;*.wav;*.ogg;*.m4a;*.aac;*.wma;*.opus\0所有文件\0*.*\0", "选择音频文件"),
            "image" => ("图片文件 (*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp;*.avif;*.svg)\0*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp;*.avif;*.svg\0所有文件\0*.*\0", "选择图片"),
            "video" => ("视频文件 (*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.flv;*.m3u8)\0*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.flv;*.m3u8\0所有文件\0*.*\0", "选择视频"),
            "media" => ("媒体文件\0*.mp3;*.flac;*.wav;*.ogg;*.m4a;*.aac;*.wma;*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.flv;*.m3u8\0所有文件\0*.*\0", "选择媒体文件"),
            _ => ("所有文件\0*.*\0", "选择文件"),
        };
        return PickSingle(owner, filter, title ?? caption);
    }

    private static string? PickSingle(IntPtr owner, string filter, string title)
    {
        const int chars = 4096;
        var buffer = Marshal.AllocHGlobal(chars * 2);
        try
        {
            for (int i = 0; i < 8; i++) Marshal.WriteInt16(buffer, i * 2, 0);
            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = owner,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = chars,
                lpstrTitle = title,
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST |
                        OFN_NOCHANGEDIR | OFN_HIDEREADONLY,
            };
            if (!GetOpenFileNameW(ref ofn)) return null;
            var raw = Marshal.PtrToStringUni(buffer) ?? "";
            return raw.Length > 0 && File.Exists(raw) ? raw : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>选择一本文或多本书；取消返回空数组。</summary>
    public static string[] PickBooks(IntPtr owner)
    {
        const int chars = 32768;
        var buffer = Marshal.AllocHGlobal(chars * 2);
        try
        {
            for (int i = 0; i < 64; i++) Marshal.WriteInt16(buffer, i * 2, 0);

            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = owner,
                lpstrFilter = "书籍 (TXT/EPUB/PDF/MOBI/AZW3/MD)\0*.txt;*.epub;*.pdf;*.mobi;*.azw3;*.md;*.markdown\0所有文件\0*.*\0",
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = chars,
                lpstrTitle = "选择要导入书架的书（可多选）",
                Flags = OFN_EXPLORER | OFN_ALLOWMULTISELECT | OFN_FILEMUSTEXIST |
                        OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY,
            };
            if (!GetOpenFileNameW(ref ofn)) return Array.Empty<string>();

            var raw = Marshal.PtrToStringUni(buffer) ?? "";
            var parts = new List<string>();
            foreach (var p in raw.Split('\0'))
            {
                if (p.Length > 0) parts.Add(p);
            }
            if (parts.Count == 0) return Array.Empty<string>();
            if (parts.Count == 1) return File.Exists(parts[0]) ? new[] { parts[0] } : Array.Empty<string>();

            var dir = parts[0];
            var files = new List<string>();
            for (int i = 1; i < parts.Count; i++)
            {
                try
                {
                    var full = Path.Combine(dir, parts[i]);
                    if (File.Exists(full)) files.Add(full);
                }
                catch { }
            }
            return files.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
