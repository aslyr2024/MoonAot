using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MoonAot.Services;

/// <summary>网页翻译（MyMemory 免费接口，无需密钥）。按文本片段翻译并缓存。</summary>
public static class TranslateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static async Task<List<string>> TranslateAsync(List<string> texts, string langpair)
    {
        var result = new List<string>(texts.Count);
        using var gate = new System.Threading.SemaphoreSlim(4, 4);
        var tasks = new Task[texts.Count];
        var slot = new string[texts.Count];
        for (int i = 0; i < texts.Count; i++)
        {
            int idx = i;
            tasks[i] = Task.Run(async () =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try { slot[idx] = await TranslateOne(texts[idx], langpair).ConfigureAwait(false); }
                catch { slot[idx] = ""; }
                finally { gate.Release(); }
            });
        }
        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
        result.AddRange(slot);
        return result;
    }

    private static async Task<string> TranslateOne(string text, string langpair)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0) return "";
        var key = langpair + "|" + value;
        if (Cache.TryGetValue(key, out var hit)) return hit;
        if (value.Length > 500) value = value[..500];
        var url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(value) +
                  "&langpair=" + Uri.EscapeDataString(langpair);
        using var resp = await Http.GetAsync(url).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return "";
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("responseData", out var data) ||
            !data.TryGetProperty("translatedText", out var t) ||
            t.ValueKind != JsonValueKind.String) return "";
        var translated = t.GetString() ?? "";
        if (translated.Contains("INVALID", StringComparison.OrdinalIgnoreCase) ||
            translated.Contains("QUERY LENGTH LIMIT", StringComparison.OrdinalIgnoreCase))
            return "";
        if (Cache.Count < 5000) Cache[key] = translated;
        return translated;
    }
}
