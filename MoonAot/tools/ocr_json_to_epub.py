#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""PaddleOCR-VL-1.6 导出的 OCR JSON 图书 → EPUB 3（零第三方依赖）。

源格式：顶层为页面数组，每页含
  prunedResult.parsing_res_list  —— 块（doc_title / paragraph_title / text / image ...）
  markdown.text                  —— 正文（普通文本 + HTML 片段，图片写 <img src="imgs/xxx.jpg">）
  markdown.images                —— { "imgs/xxx.jpg": "https://...签名URL" }

转换结果：标准 EPUB 3（同时带 toc.ncx 兼容 EPUB2 阅读器），每页一个 XHTML，
图片下载/复制进 OEBPS/images/ 并改写 src，nav 目录按 doc_title 页聚合。

用法示例：
  python ocr_json_to_epub.py book_by_PaddleOCR-VL-1.6.json -o epub图书
  python ocr_json_to_epub.py D:/books -o epub图书 --workers 12
  python ocr_json_to_epub.py book.json -o out --local-images D:/path/to/imgs   # 复用已下载图片，不联网
  python ocr_json_to_epub.py book.json -o out --cache-dir D:/imgcache          # 缓存下载图片供以后复用
  python ocr_json_to_epub.py book.json -o out --max-pages 5                    # 只转前 5 页做检查
"""
from __future__ import annotations

import argparse
import concurrent.futures
import datetime
import hashlib
import html as html_mod
import json
import os
import re
import shutil
import sys
import tempfile
import time
import uuid
import zipfile
from html.parser import HTMLParser
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from xml.sax.saxutils import escape as xe

try:
    sys.stdout.reconfigure(encoding='utf-8')
except Exception:
    pass

SEG_SPLIT = re.compile(r'\n{2,}')
HEADING_RE = re.compile(r'^(#{1,6})\s*(.*)$')
IMG_SRC_SCAN = re.compile(r'<img\b[^>]*?\bsrc\s*=\s*["\']([^"\']+)["\']', re.I | re.S)
IMG_EXT_MIME = {'.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.png': 'image/png',
                '.gif': 'image/gif', '.webp': 'image/webp', '.svg': 'image/svg+xml'}
KEEP_ATTRS = ('style', 'colspan', 'rowspan', 'class', 'align', 'valign')
VOID_TAGS = ('br', 'hr', 'img', 'input', 'meta', 'link')

CONTAINER_XML = '''<?xml version="1.0" encoding="UTF-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
</container>
'''

STYLE_CSS = '''body { font-family: "Noto Serif CJK SC", "SimSun", serif; line-height: 1.75; margin: 1em 1.1em; }
h1,h2,h3,h4 { line-height: 1.4; margin: 1.1em 0 .5em; }
p { margin: 0 0 .7em; }
img { max-width: 100%; height: auto; display: block; margin: .6em auto; }
.ocr-block { margin: .5em 0; }
table { border-collapse: collapse; width: 100%; margin: .6em 0; }
td,th { border: 1px solid #999; padding: .2em .35em; }
'''

XHTML_HEAD = '''<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="zh-CN" lang="zh-CN">
<head><title>%s</title><link rel="stylesheet" type="text/css" href="../style.css"/></head>
<body><section epub:type="chapter">
'''

NAV_HEAD = '''<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="zh-CN" lang="zh-CN">
<head><title>目录</title><link rel="stylesheet" type="text/css" href="style.css"/></head>
<body><nav epub:type="toc" id="toc"><h1>目录</h1><ol>
'''


class Cleaner(HTMLParser):
    """把 OCR 的 HTML 片段规范化成 XHTML，并改写图片 src。"""

    def __init__(self, img_map: dict[str, str]):
        super().__init__(convert_charrefs=True)
        self.out: list[str] = []
        self.img_map = img_map
        self.skip = 0

    def handle_starttag(self, tag, attrs):
        if tag in ('script', 'style', 'meta', 'link'):
            self.skip += 1
            return
        a = dict(attrs)
        if tag == 'img':
            src = a.get('src', '')
            mapped = self.img_map.get(src) or self.img_map.get(src.lstrip('./')) or src
            alt = xe(a.get('alt', ''), {'"': '&quot;'})
            self.out.append(f'<img src="{xe(mapped, {chr(34): "&quot;"})}" alt="{alt}" />')
            return
        if tag in VOID_TAGS:
            self.out.append(f'<{tag} />')
            return
        at = ''.join(f' {k}="{xe(v, {chr(34): "&quot;"})}"' for k, v in attrs if k in KEEP_ATTRS)
        self.out.append(f'<{tag}{at}>')

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in VOID_TAGS and tag not in ('script', 'style', 'meta', 'link'):
            self.out.append(f'</{tag}>')

    def handle_endtag(self, tag):
        if tag in ('script', 'style', 'meta', 'link'):
            self.skip = max(0, self.skip - 1)
            return
        if tag in VOID_TAGS:
            return
        self.out.append(f'</{tag}>')

    def handle_data(self, data):
        if not self.skip:
            self.out.append(xe(data))

    def result(self) -> str:
        return ''.join(self.out)


def page_title(page: dict, idx: int) -> str:
    blocks = (page.get('prunedResult') or {}).get('parsing_res_list') or []
    for b in blocks:
        if b.get('block_label') in ('doc_title', 'paragraph_title'):
            t = str(b.get('block_content', '')).lstrip('#').strip()
            if t:
                return t[:120]
    return f'第 {idx + 1} 页'


def has_doc_title(page: dict) -> bool:
    blocks = (page.get('prunedResult') or {}).get('parsing_res_list') or []
    return any(b.get('block_label') == 'doc_title' and str(b.get('block_content', '')).strip()
               for b in blocks)


def build_xhtml(title: str, md_text: str, img_map: dict[str, str]) -> str:
    body: list[str] = []
    for raw in SEG_SPLIT.split(md_text or ''):
        seg = raw.strip()
        if not seg:
            continue
        m = HEADING_RE.match(seg)
        if m:
            level = min(len(m.group(1)), 6)
            body.append(f'<h{level}>{xe(m.group(2).strip())}</h{level}>')
        elif '<' in seg and '>' in seg:
            c = Cleaner(img_map)
            try:
                c.feed(seg)
                c.close()
                body.append(f'<div class="ocr-block">{c.result()}</div>')
            except Exception:
                body.append(f'<p>{xe(seg)}</p>')
        else:
            body.append(f'<p>{xe(seg)}</p>')
    return (XHTML_HEAD % xe(title)) + '\n'.join(body) + '\n</section></body></html>\n'


def safe_rel(rel: str) -> str:
    rel = rel.replace('\\', '/').lstrip('/')
    parts = [p for p in rel.split('/') if p not in ('', '.', '..')]
    return '/'.join(parts)


def guess_meta(path: str) -> tuple[str, str]:
    stem = os.path.splitext(os.path.basename(path))[0]
    stem = re.sub(r'_by_PaddleOCR-VL-1\.6.*$', '', stem)
    stem = re.sub(r'\.pdf$', '', stem, flags=re.I).strip()
    author = ''
    m = re.search(r'\]\.([^.\]]+)\.', stem)
    if m:
        author = m.group(1).strip()
    else:
        m = re.search(r'_([^_\.]+?)\d*\.?$', stem)
        if m:
            author = m.group(1).strip()
    return stem, author or 'PaddleOCR-VL'


def safe_filename(title: str) -> str:
    return re.sub(r'[\\/:*?"<>|\r\n\t]', '_', title).strip()[:120] or 'book'


def fetch_image(task: tuple[str, str, str, str | None, str | None]) -> tuple[str, bool, str]:
    """(rel, url, dest, cache_dir, local_root) -> (rel, ok, message)"""
    rel, url, dest, cache_dir, local_root = task
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    if os.path.isfile(dest) and os.path.getsize(dest) > 0:
        return rel, True, 'exists'
    if local_root:
        fname = os.path.basename(rel)
        for cand in (os.path.join(local_root, rel),
                     os.path.join(local_root, 'imgs', rel),
                     os.path.join(local_root, 'imgs', 'imgs', fname),
                     os.path.join(local_root, 'images', rel),
                     os.path.join(local_root, fname)):
            if os.path.isfile(cand) and os.path.getsize(cand) > 0:
                shutil.copy2(cand, dest)
                return rel, True, 'local'
    cache_file = None
    if cache_dir and url.startswith('http'):
        ext = os.path.splitext(rel)[1] or '.jpg'
        cache_file = os.path.join(cache_dir, hashlib.sha1(url.encode('utf-8')).hexdigest() + ext)
        if os.path.isfile(cache_file) and os.path.getsize(cache_file) > 0:
            shutil.copy2(cache_file, dest)
            return rel, True, 'cache'
    if not url.startswith('http'):
        return rel, False, 'no url'
    last = ''
    for attempt in range(3):
        try:
            req = Request(url, headers={'User-Agent': 'Mozilla/5.0 (ocr-json-to-epub)'})
            with urlopen(req, timeout=30) as r:
                data = r.read()
            if not data:
                raise ValueError('empty')
            with open(dest, 'wb') as f:
                f.write(data)
            if cache_file:
                os.makedirs(cache_dir, exist_ok=True)
                shutil.copy2(dest, cache_file)
            return rel, True, 'downloaded'
        except (HTTPError, OSError, ValueError, TimeoutError) as e:
            last = f'{type(e).__name__}: {e}'[:120]
            time.sleep(0.15 * (attempt + 1))
    return rel, False, last


def convert_one(path: str, args) -> dict:
    t0 = time.time()
    with open(path, encoding='utf-8') as f:
        pages = json.load(f)
    if not isinstance(pages, list):
        raise ValueError('顶层不是页面数组')
    if args.max_pages > 0:
        pages = pages[:args.max_pages]
    title, author = guess_meta(path)
    out_name = args.title or safe_filename(title)
    epub_path = os.path.join(args.out, out_name + '.epub')
    os.makedirs(args.out, exist_ok=True)

    stage = tempfile.mkdtemp(prefix='ocr2epub_')
    oebps = os.path.join(stage, 'OEBPS')
    text_dir = os.path.join(oebps, 'text')
    img_dir = os.path.join(oebps, 'images')
    os.makedirs(text_dir)
    os.makedirs(img_dir)

    # 1) 收集图片引用（同相对路径只下一次）
    urls: dict[str, str] = {}
    for p in pages:
        imgs = (p.get('markdown') or {}).get('images') or {}
        if isinstance(imgs, dict):
            for k, v in imgs.items():
                rel = safe_rel(k)
                if rel and isinstance(v, str) and v.startswith('http'):
                    urls.setdefault(rel, v)

    # 只保留正文真正引用的图片：markdown.images 常含页眉/页脚图，正文没引用，
    # 全打包会让 EPUB 白白大上一倍（且下载更多）。
    used = set()
    for p in pages:
        txt = (p.get('markdown') or {}).get('text', '') or ''
        for m in IMG_SRC_SCAN.finditer(txt):
            rel = safe_rel(html_mod.unescape(m.group(1)))
            if rel:
                used.add(rel)
    declared = len(urls)
    urls = {rel: url for rel, url in urls.items() if rel in used}

    # 2) 并发取得图片到 staging
    tasks = [(rel, url, os.path.join(img_dir, rel.replace('/', os.sep)), args.cache_dir, args.local_images)
             for rel, url in urls.items()]
    ok = fail = 0
    failures: list[str] = []
    if tasks:
        with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as ex:
            for rel, good, msg in ex.map(fetch_image, tasks):
                if good:
                    ok += 1
                else:
                    fail += 1
                    if len(failures) < 5:
                        failures.append(f'{rel}: {msg}')

    # 3) 逐页生成 XHTML；图片 src 指向包内 images/
    img_map = {rel: '../images/' + rel for rel in urls}
    files: list[tuple[str, str]] = []   # (arcname, fullpath)
    page_names: list[str] = []
    for i, p in enumerate(pages):
        name = f'ch_{i:05d}.xhtml'
        full = os.path.join(text_dir, name)
        with open(full, 'w', encoding='utf-8') as f:
            f.write(build_xhtml(page_title(p, i), (p.get('markdown') or {}).get('text', ''), img_map))
        files.append((f'OEBPS/text/{name}', full))
        page_names.append(name)

    # 4) 目录：优先 doc_title 页，太少则按每 20 页分组
    toc = [(i, page_title(p, i)) for i, p in enumerate(pages) if has_doc_title(p)]
    if len(toc) < 2:
        toc = [(i, f'第 {i + 1}–{min(i + 20, len(pages))} 页') for i in range(0, len(pages), 20)]
    toc = toc[:args.max_toc]

    nav_full = os.path.join(oebps, 'nav.xhtml')
    with open(nav_full, 'w', encoding='utf-8') as f:
        f.write(NAV_HEAD)
        for i, t in toc:
            f.write(f'<li><a href="text/{page_names[i]}">{xe(t)}</a></li>\n')
        f.write('</ol></nav></body></html>\n')
    files.append(('OEBPS/nav.xhtml', nav_full))

    ncx_full = os.path.join(oebps, 'toc.ncx')
    with open(ncx_full, 'w', encoding='utf-8') as f:
        f.write('<?xml version="1.0" encoding="UTF-8"?>\n'
                '<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">\n'
                f'<head><meta name="dtb:uid" content="urn:uuid:{uuid.uuid4()}"/></head>\n'
                f'<docTitle><text>{xe(title)}</text></docTitle>\n<navMap>\n')
        for n, (i, t) in enumerate(toc, 1):
            f.write(f'<navPoint id="np{n}" playOrder="{n}"><navLabel><text>{xe(t)}</text></navLabel>'
                    f'<content src="text/{page_names[i]}"/></navPoint>\n')
        f.write('</navMap></ncx>\n')
    files.append(('OEBPS/toc.ncx', ncx_full))

    css_full = os.path.join(oebps, 'style.css')
    with open(css_full, 'w', encoding='utf-8') as f:
        f.write(STYLE_CSS)
    files.append(('OEBPS/style.css', css_full))

    # 只打包实际落地的图片
    img_items: list[tuple[str, str]] = []
    for rel in urls:
        full = os.path.join(img_dir, rel.replace('/', os.sep))
        if os.path.isfile(full) and os.path.getsize(full) > 0:
            img_items.append((f'OEBPS/images/{rel}', full))
    files.extend(img_items)

    # 5) content.opf
    now = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
    opf_full = os.path.join(oebps, 'content.opf')
    with open(opf_full, 'w', encoding='utf-8') as f:
        f.write('<?xml version="1.0" encoding="UTF-8"?>\n'
                '<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">\n'
                '<metadata xmlns:dc="http://purl.org/dc/elements/1.1/">\n'
                f'<dc:identifier id="bookid">urn:uuid:{uuid.uuid4()}</dc:identifier>\n'
                f'<dc:title>{xe(title)}</dc:title>\n<dc:creator>{xe(author)}</dc:creator>\n'
                '<dc:language>zh-CN</dc:language>\n'
                f'<meta property="dcterms:modified">{now}</meta>\n</metadata>\n<manifest>\n'
                '<item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>\n'
                '<item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>\n'
                '<item id="css" href="style.css" media-type="text/css"/>\n')
        for idx, n in enumerate(page_names):
            f.write(f'<item id="p{idx:05d}" href="text/{n}" media-type="application/xhtml+xml"/>\n')
        for j, (arc, _) in enumerate(img_items):
            ext = os.path.splitext(arc)[1].lower()
            f.write(f'<item id="im{j:05d}" href="{arc[len("OEBPS/"):]}" '
                    f'media-type="{IMG_EXT_MIME.get(ext, "image/jpeg")}"/>\n')
        f.write('</manifest>\n<spine toc="ncx">\n')
        for idx, _ in enumerate(page_names):
            f.write(f'<itemref idref="p{idx:05d}"/>\n')
        f.write('</spine>\n</package>\n')
    files.append(('OEBPS/content.opf', opf_full))

    container_full = os.path.join(stage, 'META-INF', 'container.xml')
    os.makedirs(os.path.dirname(container_full), exist_ok=True)
    with open(container_full, 'w', encoding='utf-8') as f:
        f.write(CONTAINER_XML)
    files.append(('META-INF/container.xml', container_full))

    # 6) 打包：mimetype 必须第一个且 STORED
    with zipfile.ZipFile(epub_path, 'w') as z:
        zi = zipfile.ZipInfo('mimetype')
        zi.compress_type = zipfile.ZIP_STORED
        z.writestr(zi, 'application/epub+zip')
        for arc, full in files:
            ct = (zipfile.ZIP_STORED if full.lower().endswith(('.jpg', '.jpeg', '.png', '.gif', '.webp'))
                  else zipfile.ZIP_DEFLATED)
            z.write(full, arc, compress_type=ct)

    size = os.path.getsize(epub_path)
    shutil.rmtree(stage, ignore_errors=True)
    return dict(title=title, pages=len(pages), images=ok, failed=fail, failures=failures,
                declared=declared, toc=len(toc), epub=epub_path, bytes=size,
                seconds=round(time.time() - t0, 1))


def main() -> int:
    ap = argparse.ArgumentParser(description='PaddleOCR-VL-1.6 OCR JSON → EPUB 3（零第三方依赖）',
                                 formatter_class=argparse.RawDescriptionHelpFormatter,
                                 epilog=__doc__.split('用法示例：')[-1])
    ap.add_argument('inputs', nargs='+', help='一个或多个 JSON 文件，或包含 JSON 的目录')
    ap.add_argument('-o', '--out', default='epub图书', help='输出目录（默认 epub图书）')
    ap.add_argument('--workers', type=int, default=10, help='图片并发下载数（默认 10）')
    ap.add_argument('--local-images', help='已本地化的图片目录，优先复用（不再联网）')
    ap.add_argument('--cache-dir', help='下载图片缓存目录，供以后重复转换复用')
    ap.add_argument('--max-pages', type=int, default=0, help='只转换前 N 页（0=全部，用于试跑）')
    ap.add_argument('--max-toc', type=int, default=300, help='目录最多条目数（默认 300）')
    ap.add_argument('--title', help='只转单本时覆盖输出文件名（不含 .epub）')
    args = ap.parse_args()

    inputs: list[str] = []
    for p in args.inputs:
        if os.path.isdir(p):
            found = sorted(f for f in os.listdir(p) if f.lower().endswith('.json'))
            inputs.extend(os.path.join(p, f) for f in found)
        elif os.path.isfile(p):
            inputs.append(p)
        else:
            print(f'[跳过] 路径不存在: {p}')
    if not inputs:
        print('没有找到可转换的 JSON')
        return 1
    os.makedirs(args.out, exist_ok=True)
    print(f'共 {len(inputs)} 本，输出目录 {os.path.abspath(args.out)}，并发 {args.workers}')

    ok = bad = 0
    total_bytes = 0
    for i, p in enumerate(inputs, 1):
        try:
            r = convert_one(p, args)
            ok += 1
            total_bytes += r['bytes']
            print(f'[{i}/{len(inputs)}] {r["title"][:44]} | {r["pages"]} 页 | '
                  f'图片 {r["images"]}/{r["declared"]} 张(失败 {r["failed"]}) | 目录 {r["toc"]} 条 | '
                  f'{r["bytes"] / 1048576:.1f} MB | {r["seconds"]}s')
            if r['failures']:
                print('    失败示例:', '; '.join(r['failures'][:3]))
            print('    ->', r['epub'])
        except Exception as e:
            bad += 1
            print(f'[{i}/{len(inputs)}] 失败 {os.path.basename(p)}: {type(e).__name__}: {e}')
    print(f'完成：成功 {ok} 本，失败 {bad} 本，EPUB 合计 {total_bytes / 1048576:.1f} MB')
    return 0 if bad == 0 else 2


if __name__ == '__main__':
    raise SystemExit(main())
