using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MoonAot.TinySearch.Storage;

namespace MoonAot.TinySearch.Search;

public record SearchResult(long Total, long ElapsedMs, List<FileEntry> Items);

/// <summary>
/// Everything 风格查询语言（纯内存、紧凑索引版）：
/// 匹配直接在 EntryStore 的 struct 记录上进行（不做全量快照复制）；
/// 命中以 (盘序号, FRN) 的轻量 Hit 数组表示，排序后按（查询+排序+索引版本）缓存，
/// 翻页只物化当前页的 FileEntry DTO（完整路径按需构建）。
/// 空格=AND，a|b=OR，!x=NOT，"短语"，* ? 通配符，
/// ext: size: dm: path: parent: folder: file: regex: 过滤器。
/// 排序：name/path/size/type(ext)/date(dm)，默认 size 降序。
/// </summary>
public sealed class SearchService
{
    private readonly MemoryIndex _mem;

    public SearchService(MemoryIndex mem) => _mem = mem;

    private static readonly Dictionary<string, string> SortMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "Name", ["path"] = "FullPath", ["size"] = "Size",
        ["type"] = "Extension", ["ext"] = "Extension",
        ["date"] = "ModifiedUtc", ["modified"] = "ModifiedUtc", ["dm"] = "ModifiedUtc",
    };

    /// <summary>命中的轻量定位（盘序号 + FRN），16 字节，不带任何字符串。</summary>
    private readonly struct Hit
    {
        public readonly int Store;
        public readonly long Frn;
        public Hit(int store, long frn) { Store = store; Frn = frn; }
    }

    /// <summary>某一（查询+排序+方向）的全量有序命中，供翻页反复使用。</summary>
    private sealed class OrderedResult
    {
        public string Query = "";
        public string Sort = "";
        public bool Desc = true;
        public long Version = -1;
        public EntryStore[] Stores = Array.Empty<EntryStore>();
        public Hit[] Hits = Array.Empty<Hit>();
        public bool Recomputing;
    }

    private OrderedResult? _cache;
    private readonly object _cacheGate = new();
    private readonly object _computeGate = new();

    // ---------- 对外 ----------

    public SearchResult Search(string? query, string? sort, bool desc, int offset, int limit)
    {
        var sw = Stopwatch.StartNew();
        var res = GetOrdered(query ?? "", sort ?? "size", desc);
        var hits = res.Hits;
        long total = hits.Length;
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 1000);
        var page = new List<FileEntry>(Math.Min(limit, 64));
        for (int i = offset; i < total && page.Count < limit; i++)
        {
            var h = hits[i];
            var store = res.Stores[h.Store];
            if (!store.TryGet(h.Frn, out var rec) || !rec.Visible) continue;
            page.Add(ToDto(store, h.Frn, rec));
        }
        sw.Stop();
        return new SearchResult(total, sw.ElapsedMilliseconds, page);
    }

    /// <summary>按排序流式枚举全部命中（「打开所有」等场景；逐条物化，不常驻内存）。</summary>
    public IEnumerable<FileEntry> Enumerate(string? query, string? sort, bool desc)
    {
        var res = GetOrdered(query ?? "", sort ?? "size", desc);
        foreach (var h in res.Hits)
        {
            var store = res.Stores[h.Store];
            if (!store.TryGet(h.Frn, out var rec) || !rec.Visible) continue;
            yield return ToDto(store, h.Frn, rec);
        }
    }

    private static FileEntry ToDto(EntryStore store, long frn, in EntryRecord rec) => new()
    {
        DriveLetter = store.Drive,
        Frn = frn,
        ParentFrn = rec.ParentFrn,
        Name = rec.Name,
        FullPath = store.GetPath(frn) ?? rec.Name,
        Size = rec.Size,
        Extension = rec.GetExtension(),
        IsDirectory = rec.IsDirectory,
        ModifiedUtc = rec.ModifiedUtc,
        Attributes = rec.Attributes,
    };

    // ---------- 有序结果缓存 ----------

    /// <summary>
    /// 取全量有序命中（翻页共用）。
    /// 命中缓存（查询+排序+方向一致）直接返回；索引版本已推进时返回旧结果并后台重算，
    /// 翻页路径上永远不做同步全量重排序。
    /// </summary>
    private OrderedResult GetOrdered(string query, string sort, bool desc)
    {
        lock (_cacheGate)
        {
            if (_cache != null && _cache.Query == query && _cache.Sort == sort && _cache.Desc == desc)
            {
                if (_cache.Version != _mem.Version && !_cache.Recomputing)
                    StartRecompute(query, sort, desc);
                return _cache;
            }
        }
        lock (_computeGate)
        {
            lock (_cacheGate)
            {
                if (_cache != null && _cache.Query == query && _cache.Sort == sort && _cache.Desc == desc)
                {
                    if (_cache.Version != _mem.Version && !_cache.Recomputing)
                        StartRecompute(query, sort, desc);
                    return _cache;
                }
            }
            var res = Compute(query, sort, desc);
            lock (_cacheGate) { _cache = res; }
            return res;
        }
    }

    private void StartRecompute(string query, string sort, bool desc)
    {
        if (_cache == null) return;
        _cache.Recomputing = true;
        var mem = _mem;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            OrderedResult? done = null;
            try { done = Compute(query, sort, desc); }
            catch { }
            finally
            {
                lock (_cacheGate)
                {
                    if (_cache != null && _cache.Query == query && _cache.Sort == sort && _cache.Desc == desc)
                    {
                        if (done != null) _cache = done;
                        else _cache.Recomputing = false;
                    }
                }
            }
        });
    }

    private OrderedResult Compute(string query, string sort, bool desc)
    {
        var plan = BuildPlan(query);
        string sortProp = SortMap.TryGetValue(sort, out var p) ? p : "Size";
        bool strSort = sortProp is "Name" or "Extension" or "FullPath";
        var stores = _mem.StoresSnapshot();

        var hits = new List<Hit>(256);
        var longKeys = strSort ? null : new List<long>(256);
        var strKeys = strSort ? new List<string>(256) : null;

        for (int si = 0; si < stores.Length; si++)
        {
            int idx = si;
            var store = stores[si];
            store.ForEach((frn, rec) =>
            {
                if (!rec.Visible || !plan.Match(frn, rec, store)) return;
                hits.Add(new Hit(idx, frn));
                if (strKeys != null)
                    strKeys.Add(sortProp switch
                    {
                        "Name" => rec.Name,
                        "Extension" => rec.GetExtension(),
                        _ => store.GetPath(frn) ?? rec.Name, // FullPath 排序才构建路径
                    });
                else
                    longKeys!.Add(sortProp == "ModifiedUtc" ? rec.ModifiedUtc : rec.Size);
            });
        }

        var hitArr = hits.ToArray();
        if (hitArr.Length > 1)
        {
            if (strKeys != null)
            {
                var keys = strKeys.ToArray();
                var cmp = StringComparer.OrdinalIgnoreCase;
                Array.Sort(keys, hitArr, Comparer<string>.Create((a, b) => desc ? cmp.Compare(b, a) : cmp.Compare(a, b)));
            }
            else
            {
                var keys = longKeys!.ToArray();
                Array.Sort(keys, hitArr, desc ? Comparer<long>.Create((x, y) => y.CompareTo(x)) : Comparer<long>.Default);
            }
        }

        return new OrderedResult
        {
            Query = query, Sort = sort, Desc = desc,
            Version = _mem.Version,
            Stores = stores, Hits = hitArr,
        };
    }

    // ---------- 查询计划 ----------

    private sealed class QueryPlan
    {
        public List<Func<long, EntryRecord, EntryStore, bool>> Ands { get; } = new();
        public List<(Regex Re, bool Neg)> Regexes { get; } = new();

        public bool Match(long frn, in EntryRecord rec, EntryStore store)
        {
            foreach (var pred in Ands)
                if (!pred(frn, rec, store)) return false;
            foreach (var (re, neg) in Regexes)
            {
                if (neg == re.IsMatch(store.GetPath(frn) ?? rec.Name)) return false;
            }
            return true;
        }
    }

    private static QueryPlan BuildPlan(string query)
    {
        var plan = new QueryPlan();
        foreach (var token in Tokenize(query))
        {
            bool neg = token.Text.StartsWith('!') && !token.Quoted;
            string t = neg ? token.Text[1..] : token.Text;
            if (t.Length == 0) continue;

            var alts = token.Quoted ? new List<string> { t } : SplitOr(t);
            var preds = new List<Func<long, EntryRecord, EntryStore, bool>>();
            foreach (var alt in alts)
            {
                var p = BuildTerm(alt, token.Quoted, plan.Regexes);
                if (p != null) preds.Add(p);
            }
            if (preds.Count == 0) continue;

            Func<long, EntryRecord, EntryStore, bool> tokenPred =
                preds.Count == 1 ? preds[0]
                : ((frn, rec, store) =>
                {
                    foreach (var p in preds) if (p(frn, rec, store)) return true;
                    return false;
                });
            if (neg)
            {
                var inner = tokenPred;
                tokenPred = (frn, rec, store) => !inner(frn, rec, store);
            }
            plan.Ands.Add(tokenPred);
        }
        return plan;
    }

    private static Func<long, EntryRecord, EntryStore, bool>? BuildTerm(string term, bool quoted,
        List<(Regex, bool)> regexes)
    {
        if (!quoted)
        {
            var m = Regex.Match(term, @"^(\w+):(.*)$");
            if (m.Success)
            {
                string field = m.Groups[1].Value.ToLowerInvariant();
                string value = m.Groups[2].Value;
                switch (field)
                {
                    case "ext":
                    {
                        var exts = value.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                        .Select(e => e.TrimStart('.').ToLowerInvariant())
                                        .Where(e => e.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (exts.Count == 0) return null;
                        return (_, rec, _) => exts.Contains(rec.GetExtension());
                    }
                    case "size": return BuildSize(value);
                    case "dm": case "date": return BuildDate(value);
                    case "path": return MakePathMatch(value);
                    case "parent": return MakeParentMatch(value);
                    case "folder":
                        return string.IsNullOrEmpty(value)
                            ? ((long _, EntryRecord rec, EntryStore _) => rec.IsDirectory)
                            : And(e => e.IsDirectory, MakeNameMatch(value));
                    case "file":
                        return string.IsNullOrEmpty(value)
                            ? ((long _, EntryRecord rec, EntryStore _) => !rec.IsDirectory)
                            : And(e => !e.IsDirectory, MakeNameMatch(value));
                    case "regex":
                        try { regexes.Add((new Regex(value, RegexOptions.IgnoreCase | RegexOptions.Compiled), false)); }
                        catch { }
                        return null;
                }
            }
        }
        return MakeNameMatch(term);
    }

    private static Func<long, EntryRecord, EntryStore, bool> And(
        Func<EntryRecord, bool> a, Func<long, EntryRecord, EntryStore, bool> b)
        => (frn, rec, store) => a(rec) && b(frn, rec, store);

    // ---------- 匹配器 ----------

    /// <summary>名称匹配：无通配符=子串；含 * ? = 完整通配匹配。</summary>
    private static Func<long, EntryRecord, EntryStore, bool> MakeNameMatch(string value)
    {
        var m = MakeWildcard(value);
        return (_, rec, _) => m(rec.Name);
    }

    /// <summary>路径匹配：无通配符=子串；含通配符=完整通配匹配。路径按需构建。</summary>
    private static Func<long, EntryRecord, EntryStore, bool> MakePathMatch(string value)
    {
        var m = MakeWildcard(value);
        return (frn, rec, store) => m(store.GetPath(frn) ?? rec.Name);
    }

    /// <summary>父目录名匹配：路径中出现 "\value\" 或以 "\value" 结尾（忽略大小写）。</summary>
    private static Func<long, EntryRecord, EntryStore, bool> MakeParentMatch(string value)
    {
        value = value.Trim('\\');
        if (value.Length == 0) return (_, _, _) => true;
        var needle = "\\" + value;
        return (frn, rec, store) =>
        {
            var p = store.GetPath(frn);
            if (p == null) return false;
            return p.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                   || p.EndsWith(needle, StringComparison.OrdinalIgnoreCase);
        };
    }

    private static Func<string, bool> MakeWildcard(string value)
    {
        bool hasWild = value.Contains('*') || value.Contains('?');
        if (!hasWild)
            return s => s.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        var sb = new StringBuilder("^");
        foreach (var c in value)
        {
            switch (c)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if ("+()[]{}|^$.# ".Contains(c)) sb.Append('\\').Append(c);
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('$');
        var re = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return s => re.IsMatch(s);
    }

    private static Func<long, EntryRecord, EntryStore, bool>? BuildSize(string value)
    {
        value = value.Trim();
        var rangeIdx = value.IndexOf("..", StringComparison.Ordinal);
        if (rangeIdx > 0)
        {
            var lo = ParseSize(value[..rangeIdx]); var hi = ParseSize(value[(rangeIdx + 2)..]);
            if (lo == null || hi == null) return null;
            long L = lo.Value, H = hi.Value;
            return (_, rec, _) => rec.Size >= L && rec.Size <= H;
        }
        var m = Regex.Match(value, @"^(>=|<=|>|<)?\s*(.+)$");
        if (!m.Success) return null;
        var size = ParseSize(m.Groups[2].Value);
        if (size == null) return null;
        long S = size.Value;
        return m.Groups[1].Value switch
        {
            ">" => (_, rec, _) => rec.Size > S,
            "<" => (_, rec, _) => rec.Size < S,
            ">=" => (_, rec, _) => rec.Size >= S,
            "<=" => (_, rec, _) => rec.Size <= S,
            _ => (_, rec, _) => rec.Size == S,
        };
    }

    private static long? ParseSize(string s)
    {
        s = s.Trim().ToLowerInvariant();
        var m = Regex.Match(s, @"^([\d.]+)\s*(b|kb|mb|gb|tb|k|m|g|t)?$");
        if (!m.Success || !double.TryParse(m.Groups[1].Value, out double num)) return null;
        double mult = m.Groups[2].Value switch
        {
            "kb" or "k" => 1024.0,
            "mb" or "m" => 1024.0 * 1024,
            "gb" or "g" => 1024.0 * 1024 * 1024,
            "tb" or "t" => 1024.0 * 1024 * 1024 * 1024,
            _ => 1.0,
        };
        return (long)(num * mult);
    }

    private static Func<long, EntryRecord, EntryStore, bool>? BuildDate(string value)
    {
        value = value.Trim().ToLowerInvariant();
        var rangeIdx = value.IndexOf("..", StringComparison.Ordinal);
        if (rangeIdx > 0)
        {
            var lo = ParseDate(value[..rangeIdx], false); var hi = ParseDate(value[(rangeIdx + 2)..], true);
            if (lo == null || hi == null) return null;
            long L = lo.Value, H = hi.Value;
            return (_, rec, _) => rec.ModifiedUtc >= L && rec.ModifiedUtc <= H;
        }
        var m = Regex.Match(value, @"^(>=|<=|>|<)?\s*(.+)$");
        if (!m.Success) return null;
        string op = m.Groups[1].Value;
        bool isRange = op.Length == 0;
        var ticks = ParseDate(m.Groups[2].Value, isRange ? false : op is "<" or "<=");
        if (ticks == null) return null;
        if (isRange)
        {
            var end = ParseDate(m.Groups[2].Value, true);
            if (end == null) return null;
            long L = ticks.Value, H = end.Value;
            return (_, rec, _) => rec.ModifiedUtc >= L && rec.ModifiedUtc <= H;
        }
        long T = ticks.Value;
        return op switch
        {
            ">" => (_, rec, _) => rec.ModifiedUtc > T,
            "<" => (_, rec, _) => rec.ModifiedUtc < T,
            ">=" => (_, rec, _) => rec.ModifiedUtc >= T,
            "<=" => (_, rec, _) => rec.ModifiedUtc <= T,
            _ => (_, rec, _) => rec.ModifiedUtc >= T && rec.ModifiedUtc < ParseDate(m.Groups[2].Value, true)!.Value,
        };
    }

    private static long? ParseDate(string s, bool endOfPeriod)
    {
        try
        {
            var now = DateTime.Now;
            DateTime start, end;
            switch (s)
            {
                case "today": start = now.Date; end = start.AddDays(1); break;
                case "yesterday": start = now.Date.AddDays(-1); end = start.AddDays(1); break;
                case "thisweek": start = now.Date.AddDays(-(int)now.DayOfWeek); end = start.AddDays(7); break;
                case "thismonth": start = new DateTime(now.Year, now.Month, 1); end = start.AddMonths(1); break;
                case "thisyear": start = new DateTime(now.Year, 1, 1); end = start.AddYears(1); break;
                default:
                {
                    DateTime dt;
                    if (Regex.IsMatch(s, @"^\d{4}$"))
                    { start = new DateTime(int.Parse(s), 1, 1); end = start.AddYears(1); }
                    else if (Regex.IsMatch(s, @"^\d{4}[-/]\d{1,2}$"))
                    {
                        var parts = s.Split('-', '/');
                        start = new DateTime(int.Parse(parts[0]), int.Parse(parts[1]), 1); end = start.AddMonths(1);
                    }
                    else if (DateTime.TryParse(s, out dt))
                    { start = dt.Date; end = start.AddDays(1); }
                    else return null;
                    break;
                }
            }
            var chosen = endOfPeriod ? end : start;
            return chosen.ToUniversalTime().Ticks;
        }
        catch { return null; }
    }

    // ---------- 分词 ----------

    private readonly struct Token
    {
        public readonly string Text; public readonly bool Quoted;
        public Token(string t, bool q) { Text = t; Quoted = q; }
    }

    private static List<Token> Tokenize(string query)
    {
        var tokens = new List<Token>();
        var sb = new StringBuilder();
        bool inQuote = false, quoted = false;
        foreach (var c in query)
        {
            if (c == '"')
            {
                if (inQuote) { tokens.Add(new Token(sb.ToString(), true)); sb.Clear(); inQuote = false; quoted = false; }
                else { if (sb.Length > 0) { tokens.Add(new Token(sb.ToString(), quoted)); sb.Clear(); } inQuote = true; }
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (sb.Length > 0) { tokens.Add(new Token(sb.ToString(), quoted)); sb.Clear(); quoted = false; }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(new Token(sb.ToString(), inQuote || quoted));
        return tokens;
    }

    private static List<string> SplitOr(string token)
    {
        var parts = token.Split('|', StringSplitOptions.None);
        return parts.Where(p => p.Length > 0).ToList();
    }
}
