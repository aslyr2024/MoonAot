import { med, type MedTerm } from './api'

/**
 * 精读/详情页的全量词条链接。
 * 后端 /medapi/terms 一次返回全部概念名 + 缩写别名；前端按首字建索引，
 * 对文本做「最长匹配」扫描：查询命中包 <mark>，数据库概念包可点的 <a class="med-term">。
 * 使用首字索引而不是 2400 词的巨型正则，避免长章节卡顿与正则注入。
 */

export interface TermMatch {
  name: string
  lower: string
  id: string
  type: string
  kind: 'term' | 'hit'
}
interface Segment { text: string; item?: TermMatch }

let loaded = false
let inflight: Promise<void> | null = null
let baseIndex = new Map<string, TermMatch[]>()
let combined = new Map<string, TermMatch[]>()
let combinedQuery = ''
let termsCount = 0

export async function ensureTerms(): Promise<void> {
  if (loaded) return
  if (!inflight) {
    inflight = med.terms().then(list => {
      baseIndex = buildIndex(list)
      termsCount = list.length
      loaded = true
      combinedQuery = '\u0000'
    }).catch(e => { inflight = null; throw e })
  }
  await inflight
}

export function termCount(): number { return termsCount }

function buildIndex(list: MedTerm[]): Map<string, TermMatch[]> {
  const m = new Map<string, TermMatch[]>()
  for (const t of list) {
    const name = (t.name || '').trim()
    if (name.length < 2) continue
    const lower = name.toLowerCase()
    const arr = m.get(lower[0]) || []
    if (!arr.some(x => x.lower === lower && x.id === t.id)) {
      arr.push({ name, lower, id: t.id, type: t.type, kind: 'term' })
    }
    m.set(lower[0], arr)
  }
  for (const arr of m.values()) arr.sort((a, b) => b.lower.length - a.lower.length)
  return m
}

function queryMatches(q: string): TermMatch[] {
  return (q || '')
    .split(/[\s,，+;；]+/)
    .map(x => x.trim().toLowerCase())
    .filter(x => x.length >= 2)
    .slice(0, 6)
    .map(x => ({ name: x, lower: x, id: '', type: 'hit', kind: 'hit' as const }))
}

function ensureCombined(q: string) {
  const key = q || ''
  if (combinedQuery === key && loaded) return
  combined = new Map<string, TermMatch[]>()
  for (const [k, v] of baseIndex) combined.set(k, v.slice())
  for (const h of queryMatches(key)) {
    const arr = combined.get(h.lower[0]) || []
    arr.push(h)
    combined.set(h.lower[0], arr)
  }
  for (const arr of combined.values()) {
    arr.sort((a, b) => b.lower.length - a.lower.length || (a.kind === 'hit' ? -1 : 1))
  }
  combinedQuery = key
}

function scan(text: string, q: string, excludeId?: string): Segment[] {
  ensureCombined(q)
  const lower = text.toLowerCase()
  const out: Segment[] = []
  let buf = ''
  const flush = () => { if (buf) { out.push({ text: buf }); buf = '' } }
  let i = 0
  while (i < text.length) {
    const candidates = combined.get(lower[i])
    let match: TermMatch | undefined
    if (candidates) {
      for (const c of candidates) {
        if (c.kind === 'term' && excludeId && c.id === excludeId) continue
        if (lower.startsWith(c.lower, i)) { match = c; break }
      }
    }
    if (match) {
      flush()
      out.push({ text: text.slice(i, i + match.lower.length), item: match })
      i += match.lower.length
    } else {
      buf += text[i]
      i++
    }
  }
  flush()
  return out
}

function esc(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function segmentHtml(s: Segment): string {
  if (!s.item) return esc(s.text)
  if (s.item.kind === 'hit') return `<mark class="med-hit" data-term-word="${esc(s.item.name)}">${esc(s.text)}</mark>`
  return `<a class="med-term" role="link" tabindex="0" data-term-id="${esc(s.item.id)}" data-term-type="${esc(s.item.type)}" title="打开词条：${esc(s.item.name)}">${esc(s.text)}</a>`
}

/** 详情页字段/模板正文：返回可直接 v-html 的安全 HTML（原文已转义）。 */
export function linkifyHtml(text: string, q = '', excludeId?: string): string {
  if (!loaded || !text) return esc(text || '')
  return scan(String(text), q, excludeId).map(segmentHtml).join('')
}

/** 精读页：对已渲染 DOM 的文本节点做替换，不重建整段 HTML，避免图片/段落定位丢失。 */
export function linkifyDom(root: HTMLElement | null, q: string, excludeId?: string): number {
  if (!root || !loaded) return 0
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
  const nodes: Text[] = []
  while (walker.nextNode()) {
    const n = walker.currentNode as Text
    const parent = n.parentElement
    if (!parent) continue
    const tag = parent.tagName.toLowerCase()
    if (tag === 'script' || tag === 'style' || tag === 'textarea') continue
    if (parent.closest('.med-term, .med-hit, mark, a')) continue
    if (!n.nodeValue || n.nodeValue.length < 2) continue
    nodes.push(n)
  }
  let count = 0
  for (const n of nodes) {
    const segments = scan(n.nodeValue || '', q, excludeId)
    if (!segments.some(s => s.item)) continue
    const frag = document.createDocumentFragment()
    for (const s of segments) {
      if (!s.item) { frag.appendChild(document.createTextNode(s.text)); continue }
      let el: HTMLElement
      if (s.item.kind === 'hit') {
        el = document.createElement('mark')
        el.setAttribute('data-term-word', s.item.name)
      }
      else {
        el = document.createElement('a')
        el.setAttribute('data-term-id', s.item.id)
        el.setAttribute('data-term-type', s.item.type)
        el.setAttribute('role', 'link')
        el.setAttribute('tabindex', '0')
        el.setAttribute('title', `打开词条：${s.item.name}`)
      }
      el.className = s.item.kind === 'hit' ? 'med-hit' : 'med-term'
      el.textContent = s.text
      frag.appendChild(el)
      count++
    }
    n.parentNode?.replaceChild(frag, n)
  }
  return count
}

/** 从点击事件里取出词条 id（供卡片与页面统一委托）。 */
export function termIdFromEvent(e: Event): string {
  const target = e.target as HTMLElement | null
  const el = target?.closest?.('.med-term') as HTMLElement | null
  return el?.dataset?.termId || ''
}

/** 查询命中词（数据库未收录时）点击跳到全文检索结果。 */
export function termWordFromEvent(e: Event): string {
  const target = e.target as HTMLElement | null
  const el = target?.closest?.('.med-hit') as HTMLElement | null
  return el?.dataset?.termWord || ''
}
