import { apiGet, apiPost, apiDelete } from '../api'

export interface MedDefaultSource { path: string; title: string; volume: string; exists: boolean }
export interface MedTaggedItem {
  targetType: string; targetId: string; label: string; sub: string; type: string
  chapterId: string; addedAt: number
}
export interface MedStatus {
  ready: boolean; version: string; dbPath: string; dbBytes: number; books: number; chapters: number; chunks: number
  concepts: number; notes: number; tags: number; templates: number; links: number; favorites: number
  chars: number; clinicalVersion: string; clinicalDefault: string; clinicalDefaultExists: boolean
  defaultBooks: MedDefaultSource[]; sourcesDir: string
}
export interface MedBook {
  id: string; bookId: string; title: string; volume: string; type: string; path: string
  status: string; addedAt: number; indexedAt: number; docCount: number; chapterCount: number
  chunkCount: number; charCount: number; anchorTotal: number; anchorResolved: number
  imageCount: number; elapsedMs: number; error?: string
}
export interface MedHit {
  chunkId: string; bookId: string; bookTitle: string; volume: string; chapterId: string
  chapterPath: string; chapterTitle: string; snippet: string; href: string
  pStart: number; pEnd: number; ord: number; score: number; source: string; concepts: string[]
}
export interface MedTerm { id: string; name: string; type: string; label: string }
export interface MedConcept {
  id: string; type: string; name: string; nameNorm: string; pinyin: string; initials: string
  category: string; keywords: string; dataJson: string; summary: string; source: string
  edited?: number; hits: number; occurrences: MedHit[]; related: MedConcept[]; backlinks: MedNote[]
}
export interface MedAnchor {
  id: string; bookId: string; chapterId: string; chunkId: string; pStart: number
  label: string; note: string; createdAt: number
}
export interface MedNote {
  id: string; title: string; body: string; targetType: string; targetId: string
  bookId: string; chapterId: string; chunkId: string; quote: string; color: string
  createdAt: number; updatedAt: number
}
export interface MedTag {
  id: string; name: string; parentId: string; color: string; ord: number; kind: string
  count: number; children: MedTag[]
}
export interface MedTemplate {
  id: string; kind: string; name: string; schemaJson: string; contentJson: string
  pinned: number; builtin: number; createdAt: number; updatedAt: number
}
export interface MedQuery { id: string; name: string; dslJson: string; pinned: number; lastUsedAt: number; createdAt: number }
export interface MedSearchResult {
  q: string; tookMs: number; total: number; conceptTotal: number; hitTotal: number; mode: string
  concepts: MedConcept[]; hits: MedHit[]; notes: MedNote[]; templates: MedTemplate[]
}
export interface MedChapter {
  id: string; bookId: string; parentId: string; level: number; spineIndex: number
  href: string; anchor: string; title: string; pageHint: string; ord: number; imgCount: number
}
export interface MedChapterView {
  html: string; bookId: string; bookTitle: string; chapterId: string; chapterPath: string
  title: string; prev: string; next: string; imageCount: number
  concepts: MedConcept[]; backlinks: MedNote[]; siblings: MedChapter[]; anchors: MedAnchor[]
}
export interface MedGraphNode { id: string; type: string; label: string; weight: number }
export interface MedGraphEdge { source: string; target: string; kind: string; weight: number }
export interface MedGraph { nodes: MedGraphNode[]; edges: MedGraphEdge[]; truncated: number }
export interface MedReport {
  ok: boolean; kind: string; jobId: string; error?: string; books: number; chapters: number
  chunks: number; concepts: number; hits: number; links: number; anchorTotal: number
  anchorResolved: number; images: number; chars: number; elapsedMs: number; dbBytes: number; messages: string[]
}
export interface MedJob { id: string; kind: string; running: boolean; done: number; total: number; message: string; report?: MedReport }

export const med = {
  status: () => apiGet<MedStatus>('/medapi/status'),
  manifest: () => apiGet<{ navItems: string[]; cardTypes: string[]; sourceKinds: string[]; quickActions: string[] }>('/medapi/manifest'),
  books: () => apiGet<MedBook[]>('/medapi/books'),
  removeBook: (id: string) => apiDelete(`/medapi/books/${encodeURIComponent(id)}`),
  search: (dsl: Record<string, any>) => apiPost<MedSearchResult>('/medapi/search', dsl),
  suggest: (q: string) => apiGet<{ q: string; items: string[]; concepts: MedConcept[] }>('/medapi/suggest', { q, limit: 10 }),
  terms: () => apiGet<MedTerm[]>('/medapi/terms'),
  concept: (id: string) => apiGet<MedConcept>(`/medapi/concept?id=${encodeURIComponent(id)}`),
  chapter: (id: string) => apiGet<MedChapterView>(`/medapi/chapter?id=${encodeURIComponent(id)}`),
  graph: (center: string, depth = 1) => apiGet<MedGraph>('/medapi/graph', { center, depth, limit: 160 }),
  notes: (q = '') => apiGet<MedNote[]>('/medapi/notes', q ? { q } : {}),
  saveNote: (n: Partial<MedNote>) => apiPost<{ ok: boolean; id: string }>('/medapi/notes', n),
  removeNote: (id: string) => apiDelete(`/medapi/notes/${encodeURIComponent(id)}`),
  tags: (tree = true) => apiGet<MedTag[]>('/medapi/tags', tree ? { tree: 1 } : {}),
  saveTag: (t: Partial<MedTag>) => apiPost<{ ok: boolean; id: string }>('/medapi/tags', t),
  removeTag: (id: string) => apiDelete(`/medapi/tags/${encodeURIComponent(id)}`),
  tagged: (tag: string) => apiGet<string[]>(`/medapi/tagged?tag=${encodeURIComponent(tag)}`),
  taggedDetail: (tag: string, limit = 300) =>
    apiGet<MedTaggedItem[]>('/medapi/tagged', { tag, detail: 1, limit }),
  tag: (tagId: string, targetType: string, targetId: string) => apiPost('/medapi/tagged', { tagId, targetType, targetId }),
  untag: (tag: string, targetType: string, targetId: string) =>
    apiDelete(`/medapi/tagged?tag=${encodeURIComponent(tag)}&targetType=${encodeURIComponent(targetType)}&targetId=${encodeURIComponent(targetId)}`),
  concepts: (q: string, limit = 10) => apiGet<MedConcept[]>('/medapi/concepts', { q, limit }),
  saveConcept: (c: Partial<MedConcept> & { bookId?: string; chapterId?: string; chunkId?: string; pStart?: number }) =>
    apiPost<{ ok: boolean; id: string }>('/medapi/concepts', c),
  deleteConcept: (id: string) => apiDelete(`/medapi/concepts/${encodeURIComponent(id)}`),
  anchors: (chapterId: string) => apiGet<MedAnchor[]>('/medapi/anchors', { chapter: chapterId }),
  saveAnchor: (a: Partial<MedAnchor>) => apiPost<{ ok: boolean; id: string }>('/medapi/anchors', a),
  deleteAnchor: (id: string) => apiDelete(`/medapi/anchors/${encodeURIComponent(id)}`),
  templates: (kind = '', opts: { limit?: number; builtin?: number; q?: string } = {}) => {
    const p: Record<string, any> = {}
    if (kind) p.kind = kind
    if (opts.limit) p.limit = opts.limit
    if (opts.builtin !== undefined) p.builtin = opts.builtin
    if (opts.q) p.q = opts.q
    return apiGet<MedTemplate[]>('/medapi/templates', p)
  },
  saveTemplate: (t: Partial<MedTemplate>) => apiPost<{ ok: boolean; id: string }>('/medapi/templates', t),
  removeTemplate: (id: string) => apiDelete(`/medapi/templates/${encodeURIComponent(id)}`),
  queries: () => apiGet<MedQuery[]>('/medapi/queries'),
  saveQuery: (q: Partial<MedQuery>) => apiPost<{ ok: boolean; id: string }>('/medapi/queries', q),
  removeQuery: (id: string) => apiDelete(`/medapi/queries/${encodeURIComponent(id)}`),
  export: () => apiGet<any>('/medapi/export'),
  reindex: () => apiGet<MedReport>('/medapi/reindex'),
  importBook: (path: string, volume = '', wait = false) =>
    apiPost<MedReport>(`/medapi/import/book${wait ? '?wait=1' : ''}`, { path, volume, wait: wait ? '1' : '0' }),
  importMarkdown: (path: string, wait = false) =>
    apiPost<MedReport>(`/medapi/import/markdown${wait ? '?wait=1' : ''}`, { path, wait: wait ? '1' : '0' }),
  importClinical: (path = '') => apiPost<MedReport>('/medapi/import/clinical', { path, snapshot: '1' }),
  importJsonBook: (path: string, wait = false) =>
    apiPost<MedReport>(`/medapi/import/jsonbook${wait ? '?wait=1' : ''}`, { path, wait: wait ? '1' : '0' }),
  importLeaflet: (item: Record<string, string>) => apiPost<MedReport>('/medapi/import/leaflet', item),
  job: (id: string, wait = false) => apiGet<MedJob>(`/medapi/jobs?id=${encodeURIComponent(id)}${wait ? '&wait=1' : ''}`),
}

export const CLINICAL_DEFAULT = 'D:\\code\\用药参考本地版\\医生私人ai大脑\\医生私人ai大脑\\Windows\\MedRecordAot\\bin\\Release\\net10.0\\win-x64\\Assets\\clinical'
export const BOOKS_DEFAULT = [
  { path: 'D:\\office\\Movies\\图书\\西氏内科学\\西氏内科学上.epub', volume: '上卷' },
  { path: 'D:\\office\\Movies\\图书\\西氏内科学\\西氏内科学中.epub', volume: '中卷' },
  { path: 'D:\\office\\Movies\\图书\\西氏内科学\\西氏内科学下.epub', volume: '下卷' },
  { path: 'D:\\office\\Movies\\图书\\西氏内科学\\胸腹部影像图解_胡春洪.epub', volume: '图谱' },
]

export function fmtBytes(n: number) {
  if (!n) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  const i = Math.min(units.length - 1, Math.floor(Math.log(n) / Math.log(1024)))
  return (n / Math.pow(1024, i)).toFixed(i > 1 ? 1 : 0) + ' ' + units[i]
}
