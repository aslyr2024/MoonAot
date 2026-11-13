"""医保编码 XLSX → 医学助手 med_concepts（零第三方依赖）。

三类新词条：
  yibao_drug 医保药品   —— 按「注册名称」去重，合并剂型/规格/企业/甲乙类
  yibao_herb 医保中草药 —— 按「中药饮片名称」去重，保留 12 列详细信息
  yibao_icd  医保ICD编码 —— 按「医保版2.0名称」去重，保留两套编码

医保药品详情页会依据 data_json._leafletId 直通 med_concepts 里的药品说明书
（source='drugdata'，按药名前缀匹配，带商品名括号前缀的也会归一化）。

用法：
  python yibao_xlsx_to_medical.py [xlsx目录] [medical.db路径]
默认目录：工作区/医保编码，默认库：MoonAot/publish/data/medical.db
重复运行会先删除本脚本上次写入的条目（source 匹配且未被手工编辑）。
"""
import bisect
import glob
import hashlib
import json
import os
import re
import sqlite3
import sys
import time
import unicodedata
import zipfile
from collections import defaultdict
from xml.etree.ElementTree import iterparse

NS = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
SHEET_RE = re.compile(r'xl/worksheets/sheet\d+\.xml$')
BRACKET_PREFIX = re.compile(r'^[（(][^）)]*[）)]')


def normalize(s):
    """与 C# MedText.NormalizeTitle 对齐：NFKC + 只保留字母数字汉字 + 小写。"""
    s = unicodedata.normalize('NFKC', s or '')
    return ''.join(ch.lower() for ch in s if ch.isalnum())


def col_index(ref):
    m = re.match(r'([A-Z]+)', ref or '')
    if not m:
        return 0
    n = 0
    for ch in m.group(1):
        n = n * 26 + (ord(ch) - 64)
    return n - 1


def load_shared(z):
    if 'xl/sharedStrings.xml' not in z.namelist():
        return []
    out = []
    with z.open('xl/sharedStrings.xml') as f:
        buf = []
        for ev, el in iterparse(f, events=('start', 'end')):
            if ev == 'start' and el.tag == NS + 'si':
                buf = []
            elif ev == 'end' and el.tag == NS + 't':
                buf.append(el.text or '')
            elif ev == 'end' and el.tag == NS + 'si':
                out.append(''.join(buf))
                el.clear()
    return out


def iter_rows(z, sheet, shared):
    """流式返回每行的 {列索引: 文本}，避免把 18 万行一次性读进内存。"""
    with z.open(sheet) as f:
        cur = None
        for ev, el in iterparse(f, events=('start', 'end')):
            if ev == 'start' and el.tag == NS + 'row':
                cur = {}
            elif ev == 'end' and el.tag == NS + 'c':
                i = col_index(el.get('r'))
                t = el.get('t')
                v = el.find(NS + 'v')
                txt = ''
                if t == 'inlineStr':
                    isel = el.find(NS + 'is')
                    if isel is not None:
                        txt = ''.join(x.text or '' for x in isel.iter(NS + 't'))
                elif v is not None and v.text is not None:
                    try:
                        txt = shared[int(v.text)] if t == 's' else v.text
                    except (ValueError, IndexError):
                        txt = v.text
                if cur is not None:
                    cur[i] = txt
                el.clear()
            elif ev == 'end' and el.tag == NS + 'row':
                yield cur or {}
                el.clear()


def sheet1(z):
    sheets = sorted(n for n in z.namelist() if SHEET_RE.match(n))
    return sheets[0] if sheets else None


def clip(v, n=180):
    v = str(v or '').strip()
    return v if len(v) <= n else v[:n] + '…'


def uniq(values, limit=8):
    seen, out = set(), []
    for v in values:
        v = str(v or '').strip()
        if not v or v in ('无', '-', '—') or v in seen:
            continue
        seen.add(v)
        out.append(clip(v, 80))
        if len(out) >= limit:
            break
    return out


def find_xlsx(folder):
    files = sorted(glob.glob(os.path.join(folder, '*.xlsx')))
    by_size = {os.path.basename(p): os.path.getsize(p) for p in files}
    drug = max(files, key=os.path.getsize)  # 25MB 的药品总表
    others = [p for p in files if p != drug]
    herb = next((p for p in others if '中药' in open_workbook_name(p)), None)
    icd = next((p for p in others if p != herb), None)
    print('files', by_size, flush=True)
    return drug, herb, icd


def open_workbook_name(path):
    """仅读 workbook.xml 的 sheet 名，用于区分中草药/ICD 文件。"""
    try:
        z = zipfile.ZipFile(path)
        wb = z.read('xl/workbook.xml').decode('utf-8', 'ignore')
        z.close()
        return ''.join(re.findall(r'<sheet[^>]*name="([^"]+)"', wb))
    except Exception:
        return ''


def read_table(path):
    z = zipfile.ZipFile(path)
    shared = load_shared(z)
    sh = sheet1(z)
    rows = iter_rows(z, sh, shared)
    header = next(rows)
    cols = {i: header.get(i, '') for i in range(max(header) + 1)}
    print(' ', os.path.basename(path), 'sheet', sh, 'cols', [cols[i] for i in range(min(6, len(cols)))], flush=True)
    return z, rows, cols


# ---------------- 说明书索引 ----------------

def build_leaflet_index(cn):
    entries = []
    for cid, name in cn.execute("SELECT id,name FROM med_concepts WHERE source='drugdata';"):
        norm = normalize(name)
        if len(norm) >= 2:
            entries.append((norm, cid, name))
        stripped = BRACKET_PREFIX.sub('', name or '')
        n2 = normalize(stripped)
        if len(n2) >= 2 and n2 != norm:
            entries.append((n2, cid, name))
    entries.sort(key=lambda x: x[0])
    return [e[0] for e in entries], entries


def match_leaflet(norm, keys, entries, limit=5):
    i = bisect.bisect_left(keys, norm)
    out = []
    while i < len(keys) and keys[i].startswith(norm) and len(out) < limit:
        out.append((entries[i][1], entries[i][2]))
        i += 1
    return out


# ---------------- 三类解析 ----------------

def parse_drugs(path, keys, entries):
    z, rows, cols = read_table(path)
    merged = {}
    n = 0
    for row in rows:
        n += 1
        name = (row.get(2) or '').strip()  # 注册名称
        if not name or name in ('注册名称',):
            continue
        d = merged.get(name)
        if d is None:
            d = {'name': name, 'count': 0, 'forms': [], 'specs': [], 'firms': [],
                 'approvals': [], 'sources': [], 'trade': [], 'ybname': '',
                 'class': '', 'ybform': ''}
            merged[name] = d
        d['count'] += 1
        for key, val in (('forms', row.get(4)), ('forms', row.get(5)),
                         ('specs', row.get(6)), ('specs', row.get(7)),
                         ('firms', row.get(12)), ('approvals', row.get(14)),
                         ('sources', row.get(1)), ('trade', row.get(3))):
            if val and len(d[key]) < 12:
                d[key].append(val)
        if not d['ybname'] and row.get(18):
            d['ybname'] = row.get(18)
        if not d['class'] and row.get(19):
            d['class'] = row.get(19)
        if not d['ybform'] and row.get(20):
            d['ybform'] = row.get(20)
    z.close()
    print('  drug rows', n, 'unique names', len(merged), flush=True)
    out = []
    linked = 0
    for name, d in merged.items():
        norm = normalize(name)
        forms, specs = uniq(d['forms']), uniq(d['specs'])
        firms, approvals = uniq(d['firms'], 5), uniq(d['approvals'], 5)
        data = {
            '医保药品名称': d['ybname'], '甲乙类': d['class'], '医保剂型': d['ybform'],
            '剂型': forms, '规格': specs, '药品企业': firms,
            '批准文号': approvals, '数据来源': uniq(d['sources']),
            '商品名': uniq(d['trade'], 4), '目录记录数': d['count'],
        }
        hits = match_leaflet(norm, keys, entries) if len(norm) >= 2 else []
        if hits:
            linked += 1
            data['_leafletId'] = hits[0][0]
            data['_leafletName'] = hits[0][1]
            data['_leafletCount'] = len(hits)
            data['_leafletIds'] = [h[0] for h in hits]
        kw = uniq(forms + specs + [d['ybname'], d['class'], d['ybform']], 12)
        summary = f"医保药品 · {d['class'] or '未分类'} · 剂型 {('/'.join(forms[:3]) or '—')} · 共 {d['count']} 条目录记录"
        out.append((name, norm, 'yibao_drug', '医保药品', 'yibao_drug',
                    '；'.join(kw), clip(summary), json.dumps(data, ensure_ascii=False)))
    print('  drug leaflet links', linked, '/', len(out), flush=True)
    return out


def parse_herbs(path):
    z, rows, cols = read_table(path)
    labels = {i: cols.get(i, '') for i in range(len(cols))}
    merged, n = {}, 0
    for row in rows:
        n += 1
        name = (row.get(1) or '').strip()  # 中药饮片名称
        if not name or name == '中药饮片名称':
            continue
        if name not in merged:
            merged[name] = {'row': row, 'count': 0}
        merged[name]['count'] += 1
    z.close()
    print('  herb rows', n, 'unique names', len(merged), flush=True)
    out = []
    for name, item in merged.items():
        row = item['row']
        data = {}
        for i, label in labels.items():
            if not label or i in (1,):
                continue
            v = (row.get(i) or '').strip()
            if v:
                data[label] = clip(v, 500)
        data['目录记录数'] = item['count']
        kw = uniq([row.get(2), row.get(4), row.get(5), row.get(7), row.get(9)], 8)
        summary = clip(row.get(9) or (row.get(8) or name), 160)
        out.append((name, normalize(name), 'yibao_herb', '医保中草药', 'yibao_herb',
                    '；'.join(kw), summary, json.dumps(data, ensure_ascii=False)))
    return out


def parse_icd(path):
    z, rows, cols = read_table(path)
    merged, n = {}, 0
    for row in rows:
        n += 1
        name = (row.get(3) or row.get(1) or '').strip()  # 医保版2.0名称优先
        if not name or name in ('医保版2.0名称', '国临版名称'):
            continue
        d = merged.get(name)
        if d is None:
            d = {'name': name, 'count': 0, 'codes': [], 'national': [], 'alias': []}
            merged[name] = d
        d['count'] += 1
        for key, val in (('codes', row.get(2)), ('national', row.get(0)), ('alias', row.get(1))):
            if val and len(d[key]) < 20:
                d[key].append(val)
    z.close()
    print('  icd rows', n, 'unique names', len(merged), flush=True)
    out = []
    for name, d in merged.items():
        codes, national = uniq(d['codes'], 6), uniq(d['national'], 6)
        alias = uniq([a for a in d['alias'] if a != name], 6)
        data = {'医保版2.0编码': codes, '国临版编码': national,
                '国临版名称': alias, '目录记录数': d['count']}
        summary = f"医保 ICD · {('/'.join(codes[:2]) or '—')} · 国临版 {('/'.join(national[:2]) or '—')}"
        out.append((name, normalize(name), 'yibao_icd', '医保ICD编码', 'yibao_icd',
                    '；'.join(uniq(codes + national + alias, 12)), clip(summary),
                    json.dumps(data, ensure_ascii=False)))
    return out


def main():
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, '医保编码')
    dbpath = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, 'MoonAot', 'publish', 'data', 'medical.db')
    t0 = time.time()
    drug, herb, icd = find_xlsx(folder)
    cn = sqlite3.connect(dbpath, timeout=120)
    keys, entries = build_leaflet_index(cn)
    print('leaflet index', len(entries), 'concepts before', cn.execute('SELECT COUNT(*) FROM med_concepts').fetchone()[0], flush=True)
    records = []
    records += parse_drugs(drug, keys, entries)
    records += parse_herbs(herb)
    records += parse_icd(icd)
    print('total records to write', len(records), flush=True)

    sources = ('yibao_drug', 'yibao_herb', 'yibao_icd')
    placeholders = ','.join('?' for _ in sources)
    cn.execute('PRAGMA journal_mode=WAL;')
    cn.execute('BEGIN;')
    cn.execute(f'DELETE FROM med_concepts WHERE source IN ({placeholders}) AND COALESCE(edited,0)=0;', sources)
    cn.executemany("""
INSERT OR REPLACE INTO med_concepts
 (id,type,name,name_norm,pinyin,initials,category,keywords,data_json,summary,source,source_version,edited,deleted)
 VALUES(?,?,?,?,'','',?,?,?,?,?,'file',0,0);
""", [(
        'yb-' + hashlib.sha1((typ + '|' + name).encode('utf-8')).hexdigest()[:16],
        typ, name, norm, category, kw, data, summary, source,
    ) for (name, norm, typ, category, source, kw, summary, data) in records])
    cn.commit()
    print('concepts after', cn.execute('SELECT COUNT(*) FROM med_concepts').fetchone()[0], flush=True)
    for s in sources:
        print(' ', s, cn.execute('SELECT COUNT(*) FROM med_concepts WHERE source=?', (s,)).fetchone()[0], flush=True)
    linked = cn.execute("SELECT COUNT(*) FROM med_concepts WHERE source='yibao_drug' AND data_json LIKE '%_leafletId%'").fetchone()[0]
    print('drug with leaflet link', linked, flush=True)
    cn.close()
    print('db bytes', os.path.getsize(dbpath), 'seconds', round(time.time() - t0, 1), flush=True)


if __name__ == '__main__':
    main()
