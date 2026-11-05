using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MoonAot.Med;

public sealed class MedChapterDraft
{
    public string Id = "";
    public string ParentId = "";
    public int Level;
    public int SpineIndex;
    public string Href = "";
    public string Anchor = "";
    public string Title = "";
    public string TitleNorm = "";
    public string PageHint = "";
    public int Ord;
    public int ImgCount;
    public int DocIndex;
    public int BlockStart;
    public int BlockEnd;
    public int CharStart;
    public int CharEnd;
}

public sealed class MedChunkRow
{
    public string Id = "";
    public string ChapterId = "";
    public int Ord;
    public string Text = "";
    public string Norm = "";
    public int PStart;
    public int PEnd;
    public int CharStart;
    public int CharEnd;
    public string ImgRefs = "";
}

public sealed class MedBookDraft
{
    public string Title = "";
    public string Volume = "";
    public int DocCount;
    public int AnchorTotal;
    public int AnchorResolved;
    public int ImageCount;
    public long CharCount;
    public List<MedChapterDraft> Chapters = new();
    public List<MedChunkRow> Chunks = new();
    public List<string> Messages = new();
}

internal sealed class HeadingRef
{
    public int DocIndex;
    public int BlockIndex;
    public int Level;
    public string RawId = "";
    public string Title = "";
}

public static class MedEpub
{
    private static readonly Regex OpfRefRe = new(@"<itemref\b[^>]*\bidref\s*=\s*(['""])(?<v>.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OpfItemRe = new(@"<item\b(?<attrs>[^>]*?)\bid\s*=\s*(?<q>['""])(?<id>.*?)\k<q>[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HrefRe = new(@"\bhref\s*=\s*(['""])(?<v>.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // .NET 中命名组的编号排在所有未命名组之后，因此用 \k<q> 命名反向引用，
    // 不要写 \1 / \2 —— 那会指错组（曾导致 2750 个目录项只匹配到 233 个）。
    private static readonly Regex NavTextRe = new(@"<text>(?<v>[\s\S]*?)</text>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NavContentRe = new(@"<content\b[^>]*\bsrc\s*=\s*(?<q>['""])(?<v>.*?)\k<q>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MedBookDraft Parse(string path, string volume, Action<int, int, string>? progress = null)
    {
        var draft = new MedBookDraft { Volume = volume };
        using var zip = ZipFile.OpenRead(path);
        var opfPath = ResolveOpfPath(zip);
        if (opfPath == null) throw new InvalidOperationException("找不到 OPF（META-INF/container.xml）");
        var opfDir = Path.GetDirectoryName(opfPath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        var opf = Read(zip, opfPath) ?? "";
        var title = Regex.Match(opf, @"<dc:title[^>]*>(?<v>[\s\S]*?)</dc:title>", RegexOptions.IgnoreCase);
        draft.Title = title.Success ? MedText.HtmlToText(title.Groups["v"].Value) : Path.GetFileNameWithoutExtension(path);
        if (draft.Title.Length > 120) draft.Title = draft.Title[..120];

        var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in OpfItemRe.Matches(opf))
        {
            var hm = HrefRe.Match(m.Groups["attrs"].Value + m.Value);
            if (hm.Success) items[m.Groups["id"].Value] = MedText.ResolveAssetPath(opfDir, MedText.HtmlDecode(hm.Groups["v"].Value));
        }
        var spine = new List<string>();
        foreach (Match m in OpfRefRe.Matches(opf))
        {
            if (items.TryGetValue(m.Groups["v"].Value, out var href) &&
                (href.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || href.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
                spine.Add(href);
        }
        if (spine.Count == 0)
            spine = zip.Entries.Where(e => e.FullName.StartsWith("EPUB/", StringComparison.OrdinalIgnoreCase) &&
                                           e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase))
                              .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Select(e => e.FullName).ToList();
        if (spine.Count == 0) throw new InvalidOperationException("spine 中没有可解析的 XHTML");
        draft.DocCount = spine.Count;

        // 1) 逐文档解析块、登记所有带 id 的标题
        var docs = new List<List<MedBlock>>();
        var headingsByNorm = new Dictionary<string, Queue<HeadingRef>>(StringComparer.Ordinal);
        var allHeadings = new List<HeadingRef>();
        for (int di = 0; di < spine.Count; di++)
        {
            var html = Read(zip, spine[di]) ?? "";
            var blocks = MedText.ParseBlocks(html);
            docs.Add(blocks);
            for (int bi = 0; bi < blocks.Count; bi++)
            {
                var b = blocks[bi];
                if (b.Kind == MedBlockKind.Image) draft.ImageCount++;
                if (b.Kind != MedBlockKind.Heading) continue;
                var hr = new HeadingRef { DocIndex = di, BlockIndex = bi, Level = b.Level, RawId = b.Id, Title = b.Text };
                allHeadings.Add(hr);
                foreach (var key in new[] { MedText.NormalizeTitle(b.Id), MedText.NormalizeTitle(b.Text) })
                {
                    if (key.Length == 0) continue;
                    if (!headingsByNorm.TryGetValue(key, out var q)) { q = new Queue<HeadingRef>(); headingsByNorm[key] = q; }
                    q.Enqueue(hr);
                }
            }
            progress?.Invoke(di + 1, spine.Count + 2, "解析 " + Path.GetFileName(spine[di]));
        }

        // 2) NCX 目录项 → 归一化后映射到真实正文 id（原 src 命中率仅 8%，归一后 100%）
        var ncxPath = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase))?.FullName;
        var navs = new List<(string Label, string Src)>();
        if (ncxPath != null)
        {
            var ncx = Read(zip, ncxPath) ?? "";
            // NCX 中每个 navPoint 的 <text> 都在自己的 <content> 之前；
            // 按位置配对可拿到全部目录项（docTitle 的 <text> 会被自然跳过），不依赖嵌套结构。
            var texts = NavTextRe.Matches(ncx);
            var contents = NavContentRe.Matches(ncx);
            int ti = -1;
            foreach (Match cm in contents)
            {
                while (ti + 1 < texts.Count && texts[ti + 1].Index < cm.Index) ti++;
                var label = ti >= 0 ? MedText.HtmlToText(texts[ti].Groups["v"].Value) : "";
                navs.Add((label, MedText.HtmlDecode(cm.Groups["v"].Value)));
            }
        }

        var picked = new List<(HeadingRef H, string Label, string Anchor, bool Resolved)>();
        var consumed = new HashSet<HeadingRef>();
        draft.AnchorTotal = navs.Count;
        foreach (var nav in navs)
        {
            var anchor = nav.Src.Contains('#') ? nav.Src[(nav.Src.IndexOf('#') + 1)..] : "";
            var keys = new[] { MedText.NormalizeTitle(anchor), MedText.NormalizeTitle(nav.Label) };
            HeadingRef? hit = null;
            foreach (var key in keys)
            {
                if (key.Length == 0 || !headingsByNorm.TryGetValue(key, out var q)) continue;
                while (q.Count > 0 && consumed.Contains(q.Peek())) q.Dequeue();
                if (q.Count > 0) { hit = q.Dequeue(); break; }
            }
            if (hit != null) { consumed.Add(hit); picked.Add((hit, nav.Label, anchor, true)); draft.AnchorResolved++; }
        }
        // 影像图解这类书 NCX 只有 4 项而正文有 163 个标题：
        // 目录项少于正文标题的一半时，必须按 h1-h4 重建，否则全书会被切成 6 个巨型章节。
        if (navs.Count < allHeadings.Count / 2 || picked.Count == 0)
        {
            picked.Clear();
            foreach (var h in allHeadings) picked.Add((h, h.Title, h.RawId, true));
            draft.AnchorTotal = picked.Count;
            draft.AnchorResolved = picked.Count;
            draft.Messages.Add($"NCX 目录不足（{navs.Count} 项 / 正文 {allHeadings.Count} 个标题），已按 h1-h4 重建章节树");
        }

        // 3) 按 (文档, 块位置) 排序生成章节树；未命中的标题补进树，保证正文不丢
        picked.Sort((a, b) => a.H.DocIndex != b.H.DocIndex ? a.H.DocIndex.CompareTo(b.H.DocIndex) : a.H.BlockIndex.CompareTo(b.H.BlockIndex));
        int ord = 0;
        var lastByDoc = new Dictionary<int, int>();
        var parentStack = new MedChapterDraft?[6];
        for (int i = 0; i < picked.Count; i++)
        {
            var (h, label, anchor, resolved) = picked[i];
            var blocks = docs[h.DocIndex];
            int nextBlock = blocks.Count;
            if (i + 1 < picked.Count && picked[i + 1].H.DocIndex == h.DocIndex)
                nextBlock = picked[i + 1].H.BlockIndex;
            var chunkDrafts = MedText.BuildChunks(blocks, h.BlockIndex + 1, nextBlock);
            var cid = NewId("c");
            var chTitle = string.IsNullOrWhiteSpace(label) ? h.Title : label;
            var ch = new MedChapterDraft
            {
                Id = cid, Level = Math.Clamp(h.Level, 1, 5), SpineIndex = h.DocIndex,
                Href = spine[h.DocIndex], Anchor = h.RawId, Title = chTitle.Length > 160 ? chTitle[..160] : chTitle,
                TitleNorm = MedText.NormalizeTitle(chTitle), Ord = ord++, DocIndex = h.DocIndex,
                BlockStart = h.BlockIndex + 1, BlockEnd = nextBlock,
                ImgCount = blocks.Skip(h.BlockIndex + 1).Take(Math.Max(0, nextBlock - h.BlockIndex - 1)).Count(b => b.Kind == MedBlockKind.Image),
            };
            for (int l = 1; l < ch.Level; l++) if (parentStack[l] != null) ch.ParentId = parentStack[l]!.Id;
            parentStack[ch.Level] = ch;
            for (int l = ch.Level + 1; l < parentStack.Length; l++) parentStack[l] = null;

            int k = 0;
            foreach (var cd in chunkDrafts)
            {
                var imgs = string.Join("\n", cd.ImgRefs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => MedText.ResolveAssetPath(Path.GetDirectoryName(ch.Href)?.Replace('\\', '/') ?? "", x))
                    .Where(x => x.Length > 0));
                draft.Chunks.Add(new MedChunkRow
                {
                    Id = NewId("k"), ChapterId = cid, Ord = k++, Text = cd.Text, Norm = cd.Norm,
                    PStart = cd.PStart, PEnd = cd.PEnd, CharStart = cd.CharStart, CharEnd = cd.CharEnd, ImgRefs = imgs,
                });
                draft.CharCount += cd.Text.Length;
            }
            ch.CharStart = chunkDrafts.Count > 0 ? chunkDrafts[0].CharStart : 0;
            ch.CharEnd = chunkDrafts.Count > 0 ? chunkDrafts[^1].CharEnd : 0;
            draft.Chapters.Add(ch);
            lastByDoc[h.DocIndex] = nextBlock;
        }

        // 4) 文档开头（首个标题之前）的内容不丢：归到「卷首」章节
        for (int di = 0; di < docs.Count; di++)
        {
            int first = picked.Where(p => p.H.DocIndex == di).Select(p => p.H.BlockIndex).DefaultIfEmpty(docs[di].Count).Min();
            if (first <= 0) continue;
            var cds = MedText.BuildChunks(docs[di], 0, first);
            if (cds.Count == 0) continue;
            var cid = NewId("c");
            var ch = new MedChapterDraft
            {
                Id = cid, Level = 1, SpineIndex = di, Href = spine[di], Title = "卷首 · " + Path.GetFileNameWithoutExtension(spine[di]),
                TitleNorm = "卷首", Ord = ord++, DocIndex = di, BlockStart = 0, BlockEnd = first,
                ImgCount = docs[di].Take(first).Count(b => b.Kind == MedBlockKind.Image),
            };
            int k = 0;
            foreach (var cd in cds)
                draft.Chunks.Add(new MedChunkRow
                {
                    Id = NewId("k"), ChapterId = cid, Ord = k++, Text = cd.Text, Norm = cd.Norm,
                    PStart = cd.PStart, PEnd = cd.PEnd, CharStart = cd.CharStart, CharEnd = cd.CharEnd,
                });
            draft.CharCount += cds.Sum(x => x.Text.Length);
            draft.Chapters.Add(ch);
        }
        draft.Chapters.Sort((a, b) => a.SpineIndex != b.SpineIndex ? a.SpineIndex.CompareTo(b.SpineIndex) : a.BlockStart.CompareTo(b.BlockStart));
        for (int i = 0; i < draft.Chapters.Count; i++) draft.Chapters[i].Ord = i;
        if (draft.AnchorTotal == 0) draft.AnchorTotal = draft.AnchorResolved = draft.Chapters.Count;
        return draft;
    }

    public static string? ReadChapter(ZipArchive zip, string href) => Read(zip, href);

    public static byte[]? ReadAsset(string path, string assetPath)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry(assetPath) ?? zip.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), assetPath, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms, 81920);
        return ms.ToArray();
    }

    private static string? ResolveOpfPath(ZipArchive zip)
    {
        var c = zip.GetEntry("META-INF/container.xml");
        if (c == null) return null;
        var xml = Read(zip, "META-INF/container.xml") ?? "";
        var m = Regex.Match(xml, @"full-path\s*=\s*(['""])(?<v>.*?)\1", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var p = MedText.ResolveAssetPath("", m.Groups["v"].Value);
        return zip.GetEntry(p) != null ? p : zip.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName, p, StringComparison.OrdinalIgnoreCase))?.FullName;
    }

    private static string? Read(ZipArchive zip, string name)
    {
        var e = zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(x =>
            string.Equals(x.FullName.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase));
        if (e == null) return null;
        try
        {
            using var s = e.Open();
            using var sr = new StreamReader(s, Encoding.UTF8, true, 81920);
            return sr.ReadToEnd();
        }
        catch { return null; }
    }

    public static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N")[..16];
}
