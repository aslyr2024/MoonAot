using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MoonAot.Store;

namespace MoonAot.Services;

/// <summary>歌词：优先同目录 .lrc，其次 lrclib.net，结果缓存 30 天。</summary>
public static class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, Task<LyricsDto>> Inflight = new(StringComparer.Ordinal);

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MoonAot/1.0 (local reader)");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return h;
    }

    public static LyricsDto Get(string audioPath, string? title = null, string? artist = null)
    {
        var dto = new LyricsDto();
        try
        {
            // 1) 本地 .lrc
            var lrcPath = Path.ChangeExtension(audioPath, ".lrc");
            if (File.Exists(lrcPath))
            {
                var text = TextCodec.Decode(File.ReadAllBytes(lrcPath));
                var parsed = ParseLrc(text);
                if (parsed.Lines.Count > 0 || !string.IsNullOrWhiteSpace(parsed.Plain))
                {
                    parsed.Ok = true;
                    parsed.Source = "local";
                    return parsed;
                }
            }

            // 2) 标签/文件名
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            {
                var meta = TagService.ReadMeta(audioPath);
                title ??= meta.Title;
                artist ??= meta.Artist;
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                var (a, t) = TagService.SplitName(Path.GetFileNameWithoutExtension(audioPath));
                title = t;
                if (string.IsNullOrWhiteSpace(artist)) artist = a;
            }

            var key = (title ?? "") + "|" + (artist ?? "");
            var cached = MediaStore.Instance.GetLyrics(key);
            if (cached != null)
            {
                var parsedCache = !string.IsNullOrEmpty(cached.Lrc) ? ParseLrc(cached.Lrc!) : new LyricsDto();
                return new LyricsDto
                {
                    Ok = true,
                    Source = cached.Source,
                    Synced = cached.Synced,
                    Plain = string.IsNullOrEmpty(parsedCache.Plain) ? cached.Plain : parsedCache.Plain,
                    Lines = parsedCache.Lines,
                    Title = title,
                    Artist = artist,
                };
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                dto.Ok = true;
                return dto;
            }

            var task = Inflight.GetOrAdd(key, k => Task.Run(() => FetchNetwork(k, title!, artist ?? "")));
            var result = task.GetAwaiter().GetResult();
            Inflight.TryRemove(key, out _);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warn("获取歌词失败: " + ex.Message);
            dto.Ok = true;
            dto.Source = "none";
            return dto;
        }
    }

    private static LyricsDto FetchNetwork(string key, string title, string artist)
    {
        var dto = new LyricsDto { Ok = true, Title = title, Artist = artist };
        try
        {
            string? synced = null, plain = null, foundTitle = null, foundArtist = null;
            var getUrl = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title) +
                         "&artist_name=" + Uri.EscapeDataString(artist);
            using (var resp = Http.GetAsync(getUrl).GetAwaiter().GetResult())
            {
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    var root = doc.RootElement;
                    synced = GetString(root, "syncedLyrics");
                    plain = GetString(root, "plainLyrics");
                    foundTitle = GetString(root, "trackName");
                    foundArtist = GetString(root, "artistName");
                }
            }

            if (string.IsNullOrWhiteSpace(synced) && string.IsNullOrWhiteSpace(plain))
            {
                // 与 Avalonia 版一致：精确匹配失败后依次用歌名、歌手+歌名、歌名+歌手模糊搜索
                foreach (var query in new[]
                {
                    title,
                    string.IsNullOrWhiteSpace(artist) ? null : artist + " " + title,
                    string.IsNullOrWhiteSpace(artist) ? null : title + " " + artist,
                })
                {
                    if (string.IsNullOrWhiteSpace(query)) continue;
                    var hit = SearchLyrics(query!);
                    if (hit == null) continue;
                    synced = GetString(hit.Value, "syncedLyrics");
                    plain = GetString(hit.Value, "plainLyrics");
                    foundTitle = GetString(hit.Value, "trackName") ?? title;
                    foundArtist = GetString(hit.Value, "artistName") ?? artist;
                    if (!string.IsNullOrWhiteSpace(synced) || !string.IsNullOrWhiteSpace(plain)) break;
                }
            }

            dto.Title = foundTitle ?? title;
            dto.Artist = foundArtist ?? artist;
            var parsed = !string.IsNullOrWhiteSpace(synced) ? ParseLrc(synced!) : new LyricsDto();
            if (!string.IsNullOrWhiteSpace(synced) && parsed.Lines.Count > 0)
            {
                dto.Synced = true;
                dto.Lines = parsed.Lines;
                dto.Plain = parsed.Plain.Length > 0 ? parsed.Plain : (plain ?? "");
                dto.Source = "lrclib";
            }
            else if (!string.IsNullOrWhiteSpace(plain))
            {
                dto.Synced = false;
                dto.Plain = plain!;
                dto.Source = "lrclib";
            }
            else
            {
                dto.Source = "none";
            }

            MediaStore.Instance.PutLyrics(key, new LyricsCacheData
            {
                Source = dto.Source,
                Synced = dto.Synced,
                Plain = dto.Plain,
                Lrc = synced,
            });
        }
        catch (Exception ex)
        {
            Log.Warn("在线歌词获取失败: " + ex.Message);
            dto.Source = "none";
        }
        return dto;
    }


    private static System.Text.Json.JsonElement? SearchLyrics(string query)
    {
        try
        {
            var url = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(query);
            using var resp = Http.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var s = GetString(item, "syncedLyrics");
                var p = GetString(item, "plainLyrics");
                if (!string.IsNullOrWhiteSpace(s) || !string.IsNullOrWhiteSpace(p)) return item.Clone();
            }
        }
        catch { }
        return null;
    }

    private static string? GetString(System.Text.Json.JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

    private static readonly Regex TimeTag = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]");
    private static readonly Regex OffsetTag = new(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase);
    private static readonly Regex MetaTag = new(@"\[(ar|ti|al|by|re|ve|length|offset|tool|encoding):[^\]]*\]", RegexOptions.IgnoreCase);
    private static readonly Regex WordTag = new(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>");

    /// <summary>解析 LRC / 纯文本歌词。行时间单位秒。</summary>
    public static LyricsDto ParseLrc(string text)
    {
        var dto = new LyricsDto();
        if (string.IsNullOrWhiteSpace(text)) return dto;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        int offsetMs = 0;
        var om = OffsetTag.Match(text);
        if (om.Success && int.TryParse(om.Groups[1].Value, out var off)) offsetMs = off;

        var lines = new List<LyricLine>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var matches = TimeTag.Matches(line);
            if (matches.Count == 0)
            {
                if (MetaTag.IsMatch(line) && TimeTag.Matches(line).Count == 0 && line.StartsWith("[")) continue;
                continue;
            }
            var body = TimeTag.Replace(line, "").Trim();
            body = WordTag.Replace(body, "").Trim();
            foreach (Match m in matches)
            {
                int min = int.Parse(m.Groups[1].Value);
                int sec = int.Parse(m.Groups[2].Value);
                double frac = 0;
                if (m.Groups[3].Success)
                {
                    var f = m.Groups[3].Value;
                    frac = f.Length switch { 1 => int.Parse(f) / 10.0, 2 => int.Parse(f) / 100.0, _ => int.Parse(f) / 1000.0 };
                }
                double t = min * 60 + sec + frac + offsetMs / 1000.0;
                if (t < 0) t = 0;
                if (body.Length > 0) lines.Add(new LyricLine { T = Math.Round(t, 2), Text = body });
            }
        }

        if (lines.Count > 0)
        {
            lines.Sort((a, b) => a.T.CompareTo(b.T));
            dto.Synced = true;
            dto.Lines = lines;
            dto.Plain = string.Join("\n", lines.Select(l => l.Text));
            return dto;
        }

        // 纯文本歌词
        var plainLines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !(l.StartsWith("[") && l.EndsWith("]")))
            .ToList();
        dto.Synced = false;
        dto.Plain = string.Join("\n", plainLines);
        return dto;
    }
}
