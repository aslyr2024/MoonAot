<script setup lang="ts">
import { computed, nextTick, onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { host } from '../host'
import { toast } from '../ui'
import GraphCanvas from '../med/GraphCanvas.vue'
import MedCard from '../med/MedCard.vue'
import LeafletView from '../med/LeafletView.vue'
import MedPager from '../med/MedPager.vue'
import { ensureTerms, linkifyDom, termIdFromEvent, termWordFromEvent, termCount } from '../med/terms'
import {
  med, fmtBytes,
  type MedStatus, type MedBook, type MedSearchResult, type MedHit, type MedConcept,
  type MedChapterView, type MedNote, type MedTag, type MedTemplate, type MedQuery,
  type MedGraph, type MedReport, type MedTaggedItem, type MedAnchor,
} from '../med/api'

const route = useRoute()
const router = useRouter()
const q = ref('')
const searchInput = ref<HTMLInputElement | null>(null)
const result = ref<MedSearchResult | null>(null)
const loading = ref(false)
const tab = ref<'search' | 'read' | 'graph' | 'mine'>('search')
const status = ref<MedStatus | null>(null)
const books = ref<MedBook[]>([])
const tags = ref<MedTag[]>([])
const templates = ref<MedTemplate[]>([])
const queries = ref<MedQuery[]>([])
const notes = ref<MedNote[]>([])
const chapter = ref<MedChapterView | null>(null)
const chapterBox = ref<HTMLDivElement | null>(null)
const concept = ref<MedConcept | null>(null)
const graph = ref<MedGraph>({ nodes: [], edges: [], truncated: 0 })
const graphCenter = ref('')
const graphCenterLabel = ref('')
const graphQuery = ref('')
const graphDepth = ref<1 | 2>(1)
const activeTypes = ref<string[]>([])
const activeBooks = ref<string[]>([])
const activeTag = ref('')
const suggest = ref<MedConcept[]>([])
const report = ref<MedReport | null>(null)
const busy = ref('')
const showNote = ref(false)
const showTemplate = ref(false)
const importPath = ref('')
const importVolume = ref('')
const importKind = ref<'epub' | 'markdown'>('epub')
const jsonBookPath = ref('')
const clinicalPath = ref('')
const leaflet = ref<Record<string, string>>({ name: '', brand: '', spec: '', indication: '', dosage: '', contraindication: '', adverse: '', interaction: '', storage: '' })
const noteDraft = ref<Partial<MedNote>>({ title: '', body: '', quote: '', targetType: 'chapter', targetId: '' })
const templateDraft = ref({ name: '', kind: 'note', contentJson: '{}' })
const currentTarget = ref<{ type: string; id: string; label: string }>({ type: '', id: '', label: '' })
const favorites = ref<string[]>([])
const resultTab = ref<'cards' | 'hits' | 'notes'>('cards')
const sortMode = ref<'relevance' | 'book_order'>('relevance')
const keyword = ref('')
const conceptLoading = ref(false)
const showHelp = ref(false)
const showData = ref(false)
const showPreview = ref(false)
const previewTemplate = ref<MedTemplate | null>(null)
const showBuiltin = ref(false)
const builtinQuery = ref('')
const builtinTemplates = ref<MedTemplate[]>([])
const buildingBuiltin = ref(false)
const mineSection = ref<'notes' | 'favorites' | 'templates' | 'queries'>('notes')
const favoritesDetailed = ref<MedTaggedItem[]>([])
const dataTab = ref<'sources' | 'import' | 'maintain'>('sources')
const editingNoteId = ref('')
const showConcept = ref(false)
const conceptSaving = ref(false)
const conceptDraft = ref({ id: '', name: '', type: 'other', category: '', keywords: '', summary: '', dataJson: '{}' })
const conceptCtx = ref({ bookId: '', chapterId: '', chunkId: '', pStart: 0 })
const showAnchor = ref(false)
const anchorDraft = ref<Partial<MedAnchor>>({ label: '', note: '', pStart: 0, chapterId: '', bookId: '', chunkId: '' })
const leafletConcept = ref<MedConcept | null>(null)
const editingTemplateId = ref('')
let skipRouteChapter = ''

const TYPE_CHIPS = [
  { key: 'disease', label: '疾病' }, { key: 'symptom', label: '症状' }, { key: 'drug', label: '药品' },
  { key: 'lab', label: '检验' }, { key: 'procedure', label: '操作' }, { key: 'guideline', label: '指南' },
  { key: 'leaflet', label: '说明书' }, { key: 'template', label: '模板' },
]
const TYPE_LABEL: Record<string, string> = Object.fromEntries(TYPE_CHIPS.map(t => [t.key, t.label]))
const anchorRate = computed(() => {
  const b = books.value.filter(x => x.status === 'ready')
  const total = b.reduce((s, x) => s + x.anchorTotal, 0)
  const ok = b.reduce((s, x) => s + x.anchorResolved, 0)
  return total ? `${ok}/${total} (${((ok / total) * 100).toFixed(1)}%)` : '—'
})
const myTemplates = computed(() => templates.value.filter(t => !t.builtin))
const builtinCount = computed(() => Math.max(0, (status.value?.templates || 0) - myTemplates.value.length))
const medVersion = computed(() => status.value?.version || '1.1.0')
const categoryTags = computed(() => tags.value.filter(t => t.kind === 'category'))
const favoriteCount = computed(() => favorites.value.length)
const resultCards = computed(() => result.value?.concepts || [])
const canBrowse = computed(() => true)

// 结果统一走分页：卡片默认 4 条（2 列 × 2 行），原文段落默认 10 条
const cardsPage = ref(1)
const hitsPage = ref(1)
const cardsPageSize = ref(4)
const hitsPageSize = ref(10)
const minePage = ref(1)
const minePageSize = ref(4)
const builtinPage = ref(1)
const builtinPageSize = ref(4)
const showConceptWide = ref(false)
const termLinkCount = ref(0)

// 栏宽可拖动：列表页中间是卡片列（窄）、右栏是详情（宽）；精读/图谱页反过来
const leftWidth = ref(180)
const midWidth = ref(520)
const rightWidth = ref(320)
const resizing = ref<'left' | 'right' | null>(null)
let resizeStartX = 0
let resizeStartVal = 0
const bodyStyle = computed(() => ({
  '--med-left': leftWidth.value + 'px',
  '--med-mid': midWidth.value + 'px',
  '--med-right': rightWidth.value + 'px',
}))
function loadWidths() {
  try {
    const l = Number(localStorage.getItem('med.leftW'))
    const m = Number(localStorage.getItem('med.midW'))
    const r = Number(localStorage.getItem('med.rightW'))
    if (l >= 120 && l <= 320) leftWidth.value = l
    if (m >= 280 && m <= 900) midWidth.value = m
    if (r >= 220 && r <= 800) rightWidth.value = r
  } catch { }
}
function isWideCenter() { return tab.value === 'read' || tab.value === 'graph' }
function startResize(which: 'left' | 'right', e: MouseEvent) {
  resizing.value = which
  resizeStartX = e.clientX
  resizeStartVal = which === 'left' ? leftWidth.value : (isWideCenter() ? rightWidth.value : midWidth.value)
  window.addEventListener('mousemove', onResizeMove)
  window.addEventListener('mouseup', endResize)
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
}
function onResizeMove(e: MouseEvent) {
  if (!resizing.value) return
  const dx = e.clientX - resizeStartX
  if (resizing.value === 'left') {
    leftWidth.value = Math.min(320, Math.max(120, resizeStartVal + dx))
  } else if (isWideCenter()) {
    rightWidth.value = Math.min(760, Math.max(220, resizeStartVal - dx))
  } else {
    const maxMid = Math.max(280, window.innerWidth - leftWidth.value - 340)
    midWidth.value = Math.min(maxMid, Math.max(280, resizeStartVal + dx))
  }
}
function endResize() {
  if (!resizing.value) return
  resizing.value = null
  window.removeEventListener('mousemove', onResizeMove)
  window.removeEventListener('mouseup', endResize)
  document.body.style.cursor = ''
  document.body.style.userSelect = ''
  try {
    localStorage.setItem('med.leftW', String(leftWidth.value))
    localStorage.setItem('med.midW', String(midWidth.value))
    localStorage.setItem('med.rightW', String(rightWidth.value))
  } catch { }
}
const mineTotal = computed(() => mineSection.value === 'notes' ? notes.value.length
  : mineSection.value === 'favorites' ? favoritesDetailed.value.length
  : mineSection.value === 'templates' ? myTemplates.value.length : queries.value.length)
const mineStart = computed(() => (minePage.value - 1) * minePageSize.value)
const mineEnd = computed(() => mineStart.value + minePageSize.value)
const builtinStart = computed(() => (builtinPage.value - 1) * builtinPageSize.value)
const builtinEnd = computed(() => builtinStart.value + builtinPageSize.value)

function sleep(ms: number) { return new Promise(r => setTimeout(r, ms)) }

async function loadAll() {
  try {
    // 先取状态：旧库会在 status 里补分类关系，再取 tag 计数才是正确的
    const s = await med.status()
    status.value = s
    if (!clinicalPath.value) clinicalPath.value = s.clinicalDefault || ''
    const [, b, t, tp, qr, nt, fav] = await Promise.all([
      ensureTerms().catch(() => { }),
      med.books(), med.tags(false),
      med.templates('', { builtin: 0, limit: 500 }), med.queries(), med.notes(), med.tagged('med-favorite'),
    ])
    books.value = b; tags.value = t; templates.value = tp
    queries.value = qr; notes.value = nt; favorites.value = fav
  } catch (e: any) { toast(e?.message || '加载医学助手数据失败') }
}

async function loadFavoritesDetailed() {
  try { favoritesDetailed.value = await med.taggedDetail('med-favorite', 500) } catch { favoritesDetailed.value = [] }
}

async function doSearch(text = q.value, keepPage = false) {
  q.value = text
  if (!text.trim() && activeBooks.value.length && !activeTypes.value.length && !activeTag.value) {
    toast('按数据源筛选时请同时输入关键词', 'warning')
    return
  }
  tab.value = 'search'
  if (!keepPage) { cardsPage.value = 1; hitsPage.value = 1 }
  loading.value = true
  try {
    const isHits = resultTab.value === 'hits'
    const page = isHits ? hitsPage.value : cardsPage.value
    const size = isHits ? hitsPageSize.value : cardsPageSize.value
    const r = await med.search({
      q: text.trim(), offset: (page - 1) * size, limit: size,
      sort: sortMode.value, mode: 'phrase',
      types: activeTypes.value, books: activeBooks.value, tags: activeTag.value ? [activeTag.value] : [],
    })
    result.value = r
    keyword.value = text.trim()
    if (!keepPage) {
      const want = r.concepts.length ? 'cards' : (r.hits.length ? 'hits' : 'notes')
      if (want !== resultTab.value) {
        resultTab.value = want
        void doSearch(keyword.value, true)
      }
    }
  } catch (e: any) { toast(e?.message || '检索失败') }
  finally { loading.value = false; suggest.value = [] }
}

/** 分页组件的页码/每页数量变化：保持当前关键词与筛选，只换页。 */
function refreshPage() { void doSearch(keyword.value || q.value, true) }

function switchResultTab(next: 'cards' | 'hits' | 'notes') {
  if (resultTab.value === next) return
  resultTab.value = next
  if (result.value) void doSearch(keyword.value || q.value, true)
}

let suggestTimer: ReturnType<typeof setTimeout> | null = null
let suggestSeq = 0
function onInput() {
  if (suggestTimer) clearTimeout(suggestTimer)
  const text = q.value.trim()
  if (text.length < 2) { suggest.value = []; return }
  const seq = ++suggestSeq
  suggestTimer = setTimeout(async () => {
    try {
      const r = await med.suggest(text)
      if (seq === suggestSeq) suggest.value = r.concepts.slice(0, 8)
    } catch { if (seq === suggestSeq) suggest.value = [] }
  }, 180)
}

function openHit(h: MedHit) {
  router.push({ name: 'med', query: { view: 'read', chapter: h.chapterId, chunk: h.chunkId, p: String(h.pStart), q: q.value } })
}

async function loadChapter(id: string, chunkId = '', terms = '', pStart = 0) {
  if (!id) return
  busy.value = '加载章节'
  try {
    chapter.value = await med.chapter(id)
    currentTarget.value = { type: 'chapter', id, label: chapter.value?.title || '' }
    await nextTick()
    if (!termCount()) await ensureTerms().catch(() => { })
    highlight(terms)
    if (chunkId) {
      const el = chapterBox.value?.querySelector(`[data-p="${pStart}"]`) as HTMLElement | null
      if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' })
      else chapterBox.value?.scrollTo({ top: 0 })
    } else {
      chapterBox.value?.scrollTo({ top: 0 })
    }
  } catch (e: any) { toast(e?.message || '章节加载失败') }
  finally { busy.value = '' }
}

/** 精读正文：命中查询词 → mark；数据库中所有概念（含缩写别名）→ 可点击下划链接。 */
function highlight(terms: string) {
  const root = chapterBox.value
  if (!root) return
  termLinkCount.value = linkifyDom(root, terms, concept.value?.id)
}

/** 页面级点击委托：精读正文、模板预览等处的词条链接统一跳到概念详情。 */
function onTermClick(e: MouseEvent) {
  const id = termIdFromEvent(e)
  if (id) {
    e.preventDefault(); e.stopPropagation()
    void openConcept(id)
    document.querySelector('.med-right')?.scrollTo({ top: 0, behavior: 'smooth' })
    return
  }
  // 数据库未收录的命中词：点击跳到该词的全文检索结果
  const word = termWordFromEvent(e)
  if (word) {
    e.preventDefault(); e.stopPropagation()
    resultTab.value = 'hits'
    void doSearch(word)
  }
}

async function openConcept(id: string) {
  conceptLoading.value = true
  try { concept.value = await med.concept(id) }
  catch (e: any) { toast(e?.message || '概念加载失败') }
  finally { conceptLoading.value = false }
}
async function showGraph(c: MedConcept) {
  graphCenter.value = c.id
  graphCenterLabel.value = c.name
  tab.value = 'graph'
  graph.value = await med.graph(c.id, 1)
}

function openCard(c: MedConcept) {
  if (c.type === 'leaflet') void openLeaflet(c)
  else void openConcept(c.id)
}

async function openLeaflet(c: MedConcept) {
  try { leafletConcept.value = await med.concept(c.id) }
  catch { leafletConcept.value = c }
}

function prettyJson(text: string) {
  try { return JSON.stringify(JSON.parse(text || '{}'), null, 2) } catch { return text || '{}' }
}

function editConcept(c: MedConcept | null, fromSelection = false) {
  conceptDraft.value = c
    ? { id: c.id, name: c.name, type: c.type, category: c.category || '', keywords: c.keywords || '', summary: c.summary || '', dataJson: prettyJson(c.dataJson) }
    : { id: '', name: '', type: 'other', category: '', keywords: '', summary: '', dataJson: '{}' }
  conceptCtx.value = {
    bookId: chapter.value?.bookId || '', chapterId: chapter.value?.chapterId || '',
    chunkId: '', pStart: 0,
  }
  if (fromSelection) {
    const info = selectionInfo()
    conceptDraft.value.name = info.text.slice(0, 80)
    conceptDraft.value.summary = info.text.slice(0, 160)
    conceptCtx.value.pStart = info.pStart
  }
  showConcept.value = true
}

function selectionInfo() {
  const sel = window.getSelection()
  const text = sel?.toString().trim() || ''
  let pStart = 0
  const node = sel?.anchorNode
  const el = node?.nodeType === Node.ELEMENT_NODE ? node as HTMLElement : node?.parentElement
  const pEl = el?.closest?.('[data-p]') as HTMLElement | null
  if (pEl) pStart = Number(pEl.getAttribute('data-p') || 0)
  return { text, pStart }
}

async function saveConceptDraft() {
  conceptSaving.value = true
  try {
    const saved = await med.saveConcept({
      ...conceptDraft.value, ...conceptCtx.value,
    })
    showConcept.value = false
    toast(conceptDraft.value.id ? '条目已更新' : '条目已添加')
    await loadAll()
    if (concept.value?.id === saved.id) concept.value = await med.concept(saved.id)
    if (chapter.value && tab.value === 'read') await loadChapter(chapter.value.chapterId, '', keyword.value)
    else if (result.value) await doSearch(keyword.value)
  } catch (e: any) { toast(e?.message || '保存失败') }
  finally { conceptSaving.value = false }
}

async function deleteConcept(c: MedConcept) {
  if (!confirm(`删除条目“${c.name}”？搜索结果中会立即移除。`)) return
  try {
    await med.deleteConcept(c.id)
    if (concept.value?.id === c.id) concept.value = null
    toast('条目已删除')
    if (result.value) await doSearch(keyword.value)
    await loadAll()
  } catch (e: any) { toast(e?.message || '删除失败') }
}

function newConceptFromSelection() { editConcept(null, true) }

function newAnchorFromSelection() {
  if (!chapter.value) { toast('请先打开章节'); return }
  const info = selectionInfo()
  anchorDraft.value = {
    label: (info.text || chapter.value.title).slice(0, 60),
    note: '', pStart: info.pStart, bookId: chapter.value.bookId,
    chapterId: chapter.value.chapterId, chunkId: '',
  }
  showAnchor.value = true
}

async function saveAnchorDraft() {
  if (!anchorDraft.value.label?.trim()) { toast('请输入锚点名称'); return }
  try {
    await med.saveAnchor(anchorDraft.value)
    showAnchor.value = false
    toast('锚点已添加')
    if (chapter.value) await loadChapter(chapter.value.chapterId, '', keyword.value)
  } catch (e: any) { toast(e?.message || '锚点保存失败') }
}

async function deleteAnchor(a: MedAnchor) {
  if (!confirm(`删除锚点“${a.label}”？`)) return
  await med.deleteAnchor(a.id)
  if (chapter.value) await loadChapter(chapter.value.chapterId, '', keyword.value)
}

function scrollToP(p: number) {
  const el = chapterBox.value?.querySelector(`[data-p="${p}"]`) as HTMLElement | null
  if (el) el.scrollIntoView({ block: 'center', behavior: 'smooth' })
}

async function goChapter(id: string) {
  if (!id) return
  skipRouteChapter = id
  await loadChapter(id, '', keyword.value, 0)
  const query: Record<string, string> = { view: 'read', chapter: id }
  if (keyword.value) query.q = keyword.value
  void router.replace({ name: 'med', query })
}

function openReader() {
  const b = books.value.find(x => x.id === chapter.value?.bookId)
  if (b?.bookId) host.openBook(b.bookId)
  else toast('该书尚未登记到静读书架，请先导入主书库')
}

function isFav(type: string, id: string) { return favorites.value.includes(`${type}:${id}`) }

async function favorite(type: string, id: string, label: string) {
  try {
    const key = `${type}:${id}`
    if (favorites.value.includes(key)) { await med.untag('med-favorite', type, id); favorites.value = favorites.value.filter(x => x !== key); toast('已取消收藏') }
    else { await med.tag('med-favorite', type, id); favorites.value.push(key); toast('已收藏：' + label) }
    if (mineSection.value === 'favorites') void loadFavoritesDetailed()
  } catch (e: any) { toast(e?.message || '收藏失败') }
}

function newNote() {
  const sel = window.getSelection()?.toString().trim() || ''
  editingNoteId.value = ''
  const useConcept = concept.value && (tab.value === 'search' || tab.value === 'graph')
  const target = useConcept
    ? { type: 'concept', id: concept.value!.id, label: concept.value!.name }
    : { type: currentTarget.value.type || 'chapter', id: currentTarget.value.id || chapter.value?.chapterId || '', label: chapter.value?.title || '' }
  noteDraft.value = {
    title: chapter.value?.title || concept.value?.name || '新注释', body: '', quote: sel,
    targetType: target.type, targetId: target.id,
    bookId: chapter.value?.bookId || '', chapterId: chapter.value?.chapterId || '',
  }
  showNote.value = true
}

function editNote(n: MedNote) {
  editingNoteId.value = n.id
  noteDraft.value = { ...n }
  showNote.value = true
}

async function saveNote() {
  if (!noteDraft.value.body?.trim()) { toast('写点内容再保存'); return }
  await med.saveNote(noteDraft.value)
  notes.value = await med.notes()
  showNote.value = false
  toast(editingNoteId.value ? '注释已更新' : '注释已保存（支持 [[双链]]）')
  editingNoteId.value = ''
  if (chapter.value && tab.value === 'read') await loadChapter(chapter.value.chapterId, '', keyword.value)
}

async function removeNote(n: MedNote) {
  if (!confirm(`删除注释“${n.title || '未命名'}”？`)) return
  await med.removeNote(n.id)
  notes.value = await med.notes()
  if (showNote.value && editingNoteId.value === n.id) showNote.value = false
}

async function removeEditingNote() {
  const id = noteDraft.value.id || editingNoteId.value
  if (!id || !confirm('删除这条注释？')) return
  await med.removeNote(id)
  notes.value = await med.notes()
  showNote.value = false
  toast('注释已删除')
}

function askReindex() {
  if (!confirm('重建概念命中与自动双链？数据量大时可能需要数十秒。')) return
  void reindex()
}

async function saveCurrentQuery() {
  const name = prompt('检索模板名称', q.value.slice(0, 24) || '我的检索式')
  if (!name) return
  await med.saveQuery({ name, dslJson: JSON.stringify({ q: q.value, types: activeTypes.value, books: activeBooks.value, tags: activeTag.value ? [activeTag.value] : [] }) })
  queries.value = await med.queries(); toast('检索模板已保存')
}
function applyQuery(item: MedQuery) {
  try {
    const d = JSON.parse(item.dslJson || '{}')
    activeTypes.value = d.types || []; activeBooks.value = d.books || []; activeTag.value = (d.tags || [])[0] || ''
    doSearch(d.q || item.name)
  } catch { toast('模板内容无效') }
}
function openTemplate(t: MedTemplate) { previewTemplate.value = t; showPreview.value = true }

async function saveAsMine(t: MedTemplate) {
  await med.saveTemplate({
    name: t.name.replace(/\s*·\s*我的副本$/, '') + ' · 我的副本',
    kind: t.kind, contentJson: t.contentJson, schemaJson: t.schemaJson, builtin: 0,
  })
  templates.value = await med.templates('', { builtin: 0, limit: 500 })
  toast('已另存为我的模板')
}

async function deleteTemplate(t: MedTemplate) {
  if (!confirm(`删除模板“${t.name}”？`)) return
  await med.removeTemplate(t.id)
  templates.value = await med.templates('', { builtin: 0, limit: 500 })
  if (previewTemplate.value?.id === t.id) showPreview.value = false
  toast('模板已删除')
}

async function loadBuiltin(text = '') {
  buildingBuiltin.value = true
  try { builtinTemplates.value = await med.templates('', { builtin: 1, q: text.trim(), limit: 150 }) }
  finally { buildingBuiltin.value = false }
}

function openBuiltin() {
  showBuiltin.value = true
  builtinQuery.value = ''
  void loadBuiltin('')
}

function openNewTemplate() {
  editingTemplateId.value = ''
  templateDraft.value = { name: '', kind: 'note', contentJson: '{}' }
  showTemplate.value = true
}

function editTemplate(t: MedTemplate) {
  editingTemplateId.value = t.id
  templateDraft.value = { name: t.name, kind: t.kind, contentJson: prettyJson(t.contentJson) }
  showPreview.value = false
  showTemplate.value = true
}

async function saveTemplateDraft() {
  if (!templateDraft.value.name.trim()) { toast('请输入模板名称'); return }
  await med.saveTemplate({
    id: editingTemplateId.value || undefined,
    name: templateDraft.value.name.trim(), kind: templateDraft.value.kind,
    contentJson: templateDraft.value.contentJson, schemaJson: '{}', builtin: 0,
  })
  templates.value = await med.templates('', { builtin: 0, limit: 500 })
  showTemplate.value = false
  toast(editingTemplateId.value ? '模板已更新' : '模板已保存')
  editingTemplateId.value = ''
}

function templateBody(t: MedTemplate | null): string {
  if (!t) return ''
  try {
    const o = JSON.parse(t.contentJson || '{}')
    if (typeof o.content === 'string') return o.content
    if (typeof o === 'string') return o
    return JSON.stringify(o, null, 2)
  } catch { return t.contentJson || '' }
}

async function pollJob(id: string): Promise<MedJobLike | null> {
  for (let i = 0; i < 600; i++) {
    const j = await med.job(id)
    busy.value = j.message || '索引中'
    if (!j.running) return j
    await sleep(400)
  }
  return null
}
interface MedJobLike { report?: MedReport; message: string; running: boolean }

async function importOne(path: string, volume: string, kind: 'epub' | 'markdown' = 'epub') {
  try {
    busy.value = '启动导入 ' + path.split(/[\\/]/).pop()
    const r = kind === 'markdown' ? await med.importMarkdown(path, false) : await med.importBook(path, volume, false)
    if (r.error) { toast(r.error); return }
    const j = await pollJob(r.jobId)
    report.value = j?.report || r
    await loadAll()
    toast(`完成：${report.value?.chapters || 0} 章 / ${report.value?.chunks || 0} 段 / ${report.value?.elapsedMs || 0}ms`)
  } catch (e: any) { toast(e?.message || '导入失败') }
  finally { busy.value = '' }
}
async function importDefaults() {
  const list = (status.value?.defaultBooks || []).filter(b => b.exists)
  if (!list.length) { toast('没有检测到内置书籍，请使用“添加新书 / 文档”选择文件', 'warning'); return }
  for (const b of list) await importOne(b.path, b.volume)
  if (status.value?.clinicalDefaultExists || clinicalPath.value) {
    try {
      const r = await med.importClinical(clinicalPath.value)
      report.value = r
      await loadAll()
      toast('临床数据已导入：' + r.concepts + ' 条')
    } catch (e: any) { toast(e?.message || '临床数据导入失败') }
  }
}
async function importClinical() {
  try {
    busy.value = '导入 clinical JSON'
    report.value = await med.importClinical(clinicalPath.value)
    await loadAll(); toast('临床数据已导入：' + report.value?.concepts + ' 条')
  } catch (e: any) { toast(e?.message || '导入失败') }
  finally { busy.value = '' }
}
async function pickFile() {
  const p = await host.pickFile('any', '选择要加入医学助手的 EPUB / Markdown')
  if (p) importPath.value = p
}

async function pickClinicalFolder() {
  const p = await host.pickFolder('选择 clinical JSON 文件夹')
  if (p) clinicalPath.value = p
}

async function pickJsonBook() {
  const p = await host.pickFile('any', '选择 PaddleOCR-VL 导出的 JSON 图书')
  if (p) jsonBookPath.value = p
}
async function pickJsonBookFolder() {
  const p = await host.pickFolder('选择存放 OCR JSON 图书的目录（批量导入）')
  if (p) jsonBookPath.value = p
}
async function importJsonBooks() {
  const p = jsonBookPath.value.trim()
  if (!p) { toast('请选择 JSON 文件或包含 JSON 图书的目录', 'warning'); return }
  busy.value = 'OCR JSON 图书导入中（图片下载 + 建索引）'
  try {
    const r = await med.importJsonBook(p, false)
    if (r.error) { toast(r.error); return }
    const j = r.jobId ? await pollJob(r.jobId) : null
    report.value = j?.report || r
    await loadAll()
    toast(`已导入 ${report.value?.books || 0} 本 / ${report.value?.chapters || 0} 章 / 图片 ${report.value?.images || 0} 张`)
  } catch (e: any) { toast(e?.message || 'JSON 图书导入失败') }
  finally { busy.value = '' }
}

function openData(tabName: 'sources' | 'import' | 'maintain' = 'sources') {
  dataTab.value = tabName
  showData.value = true
  med.status().then(s => { status.value = s; if (!clinicalPath.value) clinicalPath.value = s.clinicalDefault || '' }).catch(() => { })
}

async function graphSearch() {
  const text = graphQuery.value.trim()
  if (!text) return
  try {
    const cs = await med.concepts(text, 8)
    if (!cs.length) { toast('没有找到匹配的概念'); return }
    const c = cs.find(x => x.name === text) || cs[0]
    graphCenter.value = c.id
    graphCenterLabel.value = c.name
    graphQuery.value = ''
    graph.value = await med.graph(c.id, graphDepth.value)
    tab.value = 'graph'
  } catch (e: any) { toast(e?.message || '图谱加载失败') }
}

async function expandGraph(depth: 1 | 2) {
  if (!graphCenter.value) return
  graphDepth.value = depth
  graph.value = await med.graph(graphCenter.value, depth)
}

function openMine(section: 'notes' | 'favorites' | 'templates' | 'queries' = 'notes') {
  tab.value = 'mine'
  mineSection.value = section
  minePage.value = 1
  if (section === 'notes') med.notes().then(n => notes.value = n).catch(() => { })
  if (section === 'favorites') void loadFavoritesDetailed()
  if (section === 'templates') med.templates('', { builtin: 0, limit: 500 }).then(t => templates.value = t).catch(() => { })
  if (section === 'queries') med.queries().then(qs => queries.value = qs).catch(() => { })
}

function openFavoriteItem(f: MedTaggedItem) {
  if (f.targetType === 'concept') { tab.value = 'search'; void openConcept(f.targetId); return }
  const chapterId = f.targetType === 'chapter' ? f.targetId : f.chapterId
  if (chapterId) router.push({ name: 'med', query: { view: 'read', chapter: chapterId, q: keyword.value } })
}

function onGraphSelect(id: string) {
  const cid = id.replace('concept:', '')
  const node = graph.value.nodes.find(x => x.id === id)
  if (node) graphCenterLabel.value = node.label
  graphCenter.value = cid
  void openConcept(cid)
}

async function deleteQuery(item: MedQuery) {
  if (!confirm(`删除检索式“${item.name}”？`)) return
  await med.removeQuery(item.id)
  queries.value = queries.value.filter(x => x.id !== item.id)
  toast('检索式已删除')
}
async function importCustom() {
  if (!importPath.value.trim()) { toast('请输入或选择文件路径'); return }
  await importOne(importPath.value.trim(), importVolume.value, importKind.value)
}
async function importLeaflet() {
  if (!leaflet.value.name.trim()) { toast('请填写药品名称'); return }
  try {
    const r = await med.importLeaflet(leaflet.value)
    toast('说明书已加入速查：' + r.concepts + ' 条')
    leaflet.value = { name: '', brand: '', spec: '', indication: '', dosage: '', contraindication: '', adverse: '', interaction: '', storage: '' }
    await loadAll()
  } catch (e: any) { toast(e?.message || '添加失败') }
}
async function reindex() {
  try { busy.value = '重建概念索引'; report.value = await med.reindex(); await loadAll(); toast('索引已重建') }
  catch (e: any) { toast(e?.message || '重建失败') } finally { busy.value = '' }
}
async function exportData() {
  const data = await med.export()
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
  const a = document.createElement('a'); a.href = URL.createObjectURL(blob)
  a.download = 'moonaot-medical-' + new Date().toISOString().slice(0, 10) + '.json'; a.click()
  URL.revokeObjectURL(a.href)
}
async function removeBook(id: string) {
  if (!confirm('从医学助手移除这本书？原文件不会删除。')) return
  await med.removeBook(id); await loadAll(); toast('已移除')
}
function toggleType(k: string) {
  activeTypes.value = activeTypes.value.includes(k) ? activeTypes.value.filter(x => x !== k) : [...activeTypes.value, k]
  if (canBrowse.value) void doSearch()
}
function toggleBook(id: string) {
  activeBooks.value = activeBooks.value.includes(id) ? activeBooks.value.filter(x => x !== id) : [...activeBooks.value, id]
  if (q.value.trim()) void doSearch()
}
function onKey(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); searchInput.value?.focus() }
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'd') {
    e.preventDefault()
    if (concept.value) favorite('concept', concept.value.id, concept.value.name)
    else if (currentTarget.value.id) favorite(currentTarget.value.type, currentTarget.value.id, currentTarget.value.label)
  }
  if (e.key === 'Escape') {
    suggest.value = []
    showHelp.value = false; showData.value = false; showPreview.value = false
    showBuiltin.value = false; showTemplate.value = false; showNote.value = false
  }
}

watch(() => JSON.stringify(route.query), async () => {
  const v = route.query
  if (v.view === 'read' && typeof v.chapter === 'string') {
    if (v.chapter === skipRouteChapter) { skipRouteChapter = ''; return }
    tab.value = 'read'
    q.value = typeof v.q === 'string' ? v.q : q.value
    keyword.value = q.value
    await loadChapter(v.chapter, typeof v.chunk === 'string' ? v.chunk : '', q.value, Number(v.p || 0))
  }
})
onMounted(async () => {
  loadWidths()
  await loadAll()
  window.addEventListener('keydown', onKey)
  const v = route.query
  if (v.view === 'read' && typeof v.chapter === 'string') {
    q.value = typeof v.q === 'string' ? v.q : ''
    keyword.value = q.value
    await loadChapter(v.chapter, typeof v.chunk === 'string' ? v.chunk : '', q.value, Number(v.p || 0))
  } else if ((status.value?.concepts || 0) > 0) {
    await doSearch('')
  }
})
onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKey)
  window.removeEventListener('mousemove', onResizeMove)
  window.removeEventListener('mouseup', endResize)
})
</script>

<template>
  <div class="med-page" @click="onTermClick">
    <header class="med-top">
      <div class="med-brand">
        <div class="med-logo">医</div>
        <div class="med-brand-text">
          <h1>医学助手 <span class="med-ver">v{{ medVersion }}</span></h1>
          <p>本地临床速查与原文定位 · 全程离线 · 仅供学习参考，不作诊疗依据</p>
        </div>
      </div>
      <div class="med-top-actions">
        <button class="med-btn" @click="openData('import')">＋ 添加数据源</button>
        <button class="med-btn" @click="showHelp = true">使用说明</button>
        <button class="med-btn" @click="openData('sources')">数据与设置</button>
      </div>
    </header>

    <div class="med-searchbar">
      <div class="med-search-wrap">
        <input ref="searchInput" v-model="q" class="med-search" placeholder="搜索疾病、症状、药品、检验、指南，或输入拼音首字母（如 xlsj）"
               @keydown.enter="doSearch()" @input="onInput" />
        <button class="med-go" @click="doSearch()">检索</button>
        <button class="med-save-query" title="把当前检索条件保存为模板" @click="saveCurrentQuery">保存检索式</button>
        <div v-if="suggest.length && q" class="med-suggest">
          <button v-for="c in suggest" :key="c.id" @mousedown.prevent="doSearch(c.name)">
            <span>{{ c.name }}</span><em>{{ TYPE_LABEL[c.type] || c.type }} · {{ c.category }}</em>
          </button>
        </div>
      </div>
    </div>

    <div class="med-body" :class="{ 'is-reading': isWideCenter(), 'is-resizing': resizing }" :style="bodyStyle">
      <div class="med-resizer med-resizer-left" title="拖动调整左栏宽度" @mousedown.prevent="startResize('left', $event)"></div>
      <div class="med-resizer med-resizer-right" title="拖动调整中间 / 右侧详情宽度" @mousedown.prevent="startResize('right', $event)"></div>
      <aside class="med-left">
        <section>
          <div class="med-sec-head"><span>类型筛选</span><button v-if="activeTypes.length" @click="activeTypes = []; doSearch()">清除</button></div>
          <div class="med-chips">
            <button v-for="t in TYPE_CHIPS" :key="t.key" :class="{ on: activeTypes.includes(t.key) }" @click="toggleType(t.key)">{{ t.label }}</button>
          </div>
        </section>
        <section>
          <div class="med-sec-head"><span>临床分类</span><button v-if="activeTag" @click="activeTag = ''; doSearch()">全部</button></div>
          <div class="med-tags">
            <button :class="{ on: activeTag === 'med-favorite' }" @click="activeTag = activeTag === 'med-favorite' ? '' : 'med-favorite'; doSearch()">★ 我的收藏 ({{ favoriteCount }})</button>
            <button v-for="t in categoryTags" :key="t.id" :class="{ on: activeTag === t.id }"
                    @click="activeTag = activeTag === t.id ? '' : t.id; doSearch()">
              {{ t.name }} <em>{{ t.count }}</em>
            </button>
          </div>
        </section>
        <section>
          <div class="med-sec-head"><span>我的知识</span></div>
          <button class="med-side-link" @click="openMine('notes')">我的注释 <em>{{ notes.length }}</em></button>
          <button class="med-side-link" @click="openMine('favorites')">我的收藏 <em>{{ favoriteCount }}</em></button>
          <button class="med-side-link" @click="openMine('templates')">我的模板 <em>{{ myTemplates.length }}</em></button>
          <button class="med-side-link" @click="openMine('queries')">检索式 <em>{{ queries.length }}</em></button>
        </section>
      </aside>

      <main class="med-main">
        <div class="med-tabs">
          <button :class="{ on: tab === 'search' }" @click="tab = 'search'">检索</button>
          <button :class="{ on: tab === 'read' }" @click="tab = 'read'" :disabled="!chapter">精读</button>
          <button :class="{ on: tab === 'graph' }" @click="tab = 'graph'">图谱</button>
          <button :class="{ on: tab === 'mine' }" @click="openMine(mineSection)">我的</button>
          <span class="med-stat" v-if="result && tab === 'search'">{{ result.conceptTotal }} 张卡片 · {{ result.hitTotal }} 段原文 · {{ result.tookMs }}ms</span>
          <span class="med-stat" v-else-if="tab === 'search' && !result">输入关键词或选择分类开始检索</span>
        </div>

        <div v-show="tab === 'search'" class="med-scroll">
          <div v-if="loading" class="med-loading">检索中…</div>
          <div v-else-if="!result" class="med-welcome">
            <template v-if="!status || (status.books === 0 && status.concepts === 0)">
              <div class="med-welcome-icon">医</div>
              <h2>先导入一份资料</h2>
              <p>医学助手不会联网，也不会自动上传任何内容。导入 EPUB 医学书或 clinical JSON 后，才能检索与定位原文。</p>
              <div class="med-examples">
                <button class="med-primary" @click="openData('import')">去导入资料</button>
                <button @click="showHelp = true">查看使用说明</button>
              </div>
            </template>
            <template v-else>
              <h2>开始检索</h2>
              <p>输入疾病、症状、药品、检验或指南名称；也支持拼音首字母，例如 <code>xlsj</code> → 心力衰竭。左侧分类可直接浏览临床条目。</p>
              <div class="med-examples">
                <button v-for="w in ['心肌梗死', '高血压', '阿莫西林', '发热', '胸痛', 'WBC']" :key="w" @click="doSearch(w)">{{ w }}</button>
              </div>
              <div class="med-welcome-stats">
                <div><b>{{ status.chapters }}</b><span>书中章节</span></div>
                <div><b>{{ status.concepts }}</b><span>临床条目</span></div>
                <div><b>{{ status.notes }}</b><span>我的注释</span></div>
                <div><b>{{ favoriteCount }}</b><span>收藏</span></div>
              </div>
            </template>
          </div>
          <template v-else>
            <div class="med-result-tabs">
              <button :class="{ on: resultTab === 'cards' }" @click="switchResultTab('cards')">临床速查 <em>{{ result.conceptTotal }}</em></button>
              <button :class="{ on: resultTab === 'hits' }" @click="switchResultTab('hits')">原文段落 <em>{{ result.hitTotal }}</em></button>
              <button :class="{ on: resultTab === 'notes' }" @click="switchResultTab('notes')">相关注释 <em>{{ result.notes.length }}</em></button>
              <select v-model="sortMode" class="med-mini-select" @change="doSearch()">
                <option value="relevance">按相关度</option>
                <option value="book_order">按书内顺序</option>
              </select>
            </div>

            <div v-if="resultTab === 'cards'">
              <div v-if="!result.concepts.length" class="med-hint">没有匹配的临床条目。可换个词，或选择左侧分类浏览。</div>
              <div class="med-concept-grid">
                <MedCard v-for="c in resultCards" :key="c.id" :concept="c" compact :q="q"
                         :favorited="isFav('concept', c.id)"
                         @open="openCard(c)" @search="doSearch" @open-term="openConcept"
                         @favorite="favorite('concept', c.id, c.name)" @graph="showGraph" @open-hit="openHit" />
              </div>
              <MedPager :total="result.conceptTotal" :page="cardsPage" :page-size="cardsPageSize"
                        :sizes="[4, 8, 12, 20, 50]" label="张"
                        @update:page="cardsPage = $event" @update:page-size="cardsPageSize = $event" @change="refreshPage" />
            </div>

            <div v-else-if="resultTab === 'hits'">
              <div v-if="!result.hits.length" class="med-hint">没有匹配的原文段落。两字词与拼音首字母也能检索；若仍为空，请确认数据源已索引完成。</div>
              <div v-for="h in result.hits" :key="h.chunkId" class="med-hit" @click="openHit(h)">
                <div class="med-hit-path">{{ h.volume }} · {{ h.bookTitle }} › {{ h.chapterTitle }}</div>
                <div class="med-hit-snippet" v-html="h.snippet"></div>
                <div class="med-hit-foot">
                  <span v-for="c in h.concepts.slice(0, 4)" :key="c" class="med-tag-pill">{{ c }}</span>
                  <span class="med-hit-src">{{ h.source === 'fts' ? '全文索引' : '短词层' }}</span>
                  <button :class="{ on: isFav('chunk', h.chunkId) }" @click.stop="favorite('chunk', h.chunkId, h.chapterTitle)">{{ isFav('chunk', h.chunkId) ? '★' : '☆' }}</button>
                  <button class="med-jump">跳转 →</button>
                </div>
              </div>
              <MedPager :total="result.hitTotal" :page="hitsPage" :page-size="hitsPageSize"
                        :sizes="[10, 20, 50, 100]" label="段"
                        @update:page="hitsPage = $event" @update:page-size="hitsPageSize = $event" @change="refreshPage" />
            </div>

            <div v-else>
              <div v-if="!result.notes.length" class="med-hint">当前检索没有关联注释。你在精读页选中文字即可添加。</div>
              <button v-for="n in result.notes" :key="n.id" class="med-note-card" @click="editNote(n)">
                <strong>{{ n.title || '未命名注释' }}</strong>
                <p>{{ n.quote || n.body }}</p>
              </button>
            </div>
          </template>
        </div>

        <div v-show="tab === 'read'" class="med-read">
          <div class="med-read-head" v-if="chapter">
            <div class="med-crumb">{{ chapter.bookTitle }} › {{ chapter.chapterPath }}</div>
            <div class="med-read-actions">
              <button :disabled="!chapter.prev" @click="goChapter(chapter!.prev)">← 上一节</button>
              <button :disabled="!chapter.next" @click="goChapter(chapter!.next)">下一节 →</button>
              <button @mousedown.prevent @click="newNote">＋ 注释</button>
              <button @mousedown.prevent @click="newConceptFromSelection">＋ 概念</button>
              <button @mousedown.prevent @click="newAnchorFromSelection">＋ 锚点</button>
              <button :class="{ on: isFav('chapter', chapter.chapterId) }" @click="favorite('chapter', chapter.chapterId, chapter.title)">{{ isFav('chapter', chapter.chapterId) ? '★ 已收藏' : '☆ 收藏' }}</button>
              <button @click="openReader">在静读中打开</button>
            </div>
          </div>
          <div class="med-read-body">
            <div ref="chapterBox" class="med-content" v-html="chapter?.html || ''"></div>
            <aside class="med-read-side">
              <div class="med-side-sec">
                <div class="med-sec-head"><span>本章概念</span><em>{{ chapter?.concepts.length || 0 }}</em></div>
                <div class="med-side-chips">
                  <button v-for="c in chapter?.concepts || []" :key="c.id" @click="c.type === 'leaflet' ? openLeaflet(c) : openConcept(c.id)">{{ c.name }}</button>
                </div>
              </div>
              <div class="med-side-sec">
                <div class="med-sec-head"><span>反链与注释</span><button @mousedown.prevent @click="newNote">＋</button></div>
                <button v-for="n in chapter?.backlinks || []" :key="n.id" class="med-side-note" @click="editNote(n)">
                  {{ n.title }}<br /><em>{{ (n.body || '').slice(0, 60) }}</em>
                </button>
                <div v-if="!chapter?.backlinks?.length" class="med-hint">还没有引用本章的注释</div>
              </div>
              <div class="med-side-sec">
                <div class="med-sec-head"><span>我的锚点</span><button @mousedown.prevent @click="newAnchorFromSelection">＋</button></div>
                <div v-for="a in chapter?.anchors || []" :key="a.id" class="med-anchor-row">
                  <button class="med-anchor-jump" @click="scrollToP(a.pStart)">{{ a.label }}</button>
                  <button class="med-anchor-del" @click="deleteAnchor(a)">×</button>
                </div>
                <div v-if="!chapter?.anchors?.length" class="med-hint">选中文字后点“＋ 锚点”，可快速跳回该段</div>
              </div>
              <div class="med-side-sec" v-if="concept">
                <div class="med-sec-head"><span>概念详情</span><button @click="concept = null">×</button></div>
                <MedCard :concept="concept" compact :q="q" :favorited="isFav('concept', concept!.id)" @search="doSearch"
                         @favorite="favorite('concept', concept!.id, concept!.name)" @graph="showGraph" @open-hit="openHit"
                         @open-term="openConcept" @open="openConcept(concept!.id)" />
              </div>
              <div class="med-side-sec">
                <div class="med-sec-head"><span>本章目录</span><em>{{ chapter?.siblings.length || 0 }} 节</em></div>
                <div class="med-siblings">
                  <button v-for="s in chapter?.siblings || []" :key="s.id" :class="{ on: s.id === chapter?.chapterId }"
                          :data-no="s.ord + 1" :title="s.title" @click="goChapter(s.id)">{{ s.title }}</button>
                </div>
              </div>
            </aside>
          </div>
        </div>

        <div v-show="tab === 'graph'" class="med-graph-view">
          <div class="med-graph-bar">
            <input v-model="graphQuery" class="med-mini-input" placeholder="输入概念名后回车，如 急性心肌梗死" @keydown.enter="graphSearch" />
            <button @click="graphSearch">定位概念</button>
            <span class="med-graph-center">{{ graphCenterLabel ? '中心：' + graphCenterLabel : '未选择中心概念' }}</span>
            <button :class="{ on: graphDepth === 1 }" @click="expandGraph(1)">1 跳</button>
            <button :class="{ on: graphDepth === 2 }" @click="expandGraph(2)">2 跳</button>
          </div>
          <GraphCanvas :nodes="graph.nodes" :edges="graph.edges" @select="onGraphSelect" />
        </div>

        <div v-show="tab === 'mine'" class="med-mine">
          <nav class="med-mine-nav">
            <button :class="{ on: mineSection === 'notes' }" @click="openMine('notes')">我的注释 <em>{{ notes.length }}</em></button>
            <button :class="{ on: mineSection === 'favorites' }" @click="openMine('favorites')">我的收藏 <em>{{ favoriteCount }}</em></button>
            <button :class="{ on: mineSection === 'templates' }" @click="openMine('templates')">我的模板 <em>{{ myTemplates.length }}</em></button>
            <button :class="{ on: mineSection === 'queries' }" @click="openMine('queries')">检索式 <em>{{ queries.length }}</em></button>
          </nav>
          <div class="med-mine-body">
            <template v-if="mineSection === 'notes'">
              <div class="med-mine-head"><strong>我的注释</strong><button class="med-btn" @click="newNote">＋ 新建注释</button></div>
              <div v-if="!notes.length" class="med-hint">还没有注释。在精读页选中文字，点「＋ 注释」即可保存并支持 [[双链]]。</div>
              <div class="med-mine-grid">
                <div v-for="n in notes.slice(mineStart, mineEnd)" :key="n.id" class="med-mine-row" @click="editNote(n)">
                  <div><strong>{{ n.title || '未命名注释' }}</strong><em>{{ n.targetType }} · {{ n.quote || n.body }}</em></div>
                  <span class="med-row-actions"><button @click.stop="removeNote(n)">删除</button></span>
                </div>
              </div>
            </template>
            <template v-else-if="mineSection === 'favorites'">
              <div class="med-mine-head"><strong>我的收藏</strong><span class="med-hint">在概念、章节或原文段落上点 ☆ 收藏</span></div>
              <div v-if="!favoritesDetailed.length" class="med-hint">还没有收藏内容。</div>
              <div class="med-mine-grid">
                <div v-for="f in favoritesDetailed.slice(mineStart, mineEnd)" :key="f.targetType + f.targetId" class="med-mine-row" @click="openFavoriteItem(f)">
                  <div><strong>{{ f.label || f.targetId }}</strong><em>{{ f.type ? (TYPE_LABEL[f.type] || f.type) : f.targetType }}{{ f.sub ? ' · ' + f.sub : '' }}</em></div>
                  <span class="med-row-actions">打开</span>
                </div>
              </div>
            </template>
            <template v-else-if="mineSection === 'templates'">
              <div class="med-mine-head">
                <strong>我的模板</strong>
                <button class="med-btn" @click="openNewTemplate">＋ 新建模板</button>
                <button class="med-btn" @click="openBuiltin">内置模板库（{{ builtinCount }}）</button>
              </div>
              <div v-if="!myTemplates.length" class="med-hint">还没有自己的模板。可从内置模板库另存，或新建笔记/文书模板。</div>
              <div class="med-mine-grid">
                <div v-for="t in myTemplates.slice(mineStart, mineEnd)" :key="t.id" class="med-mine-row" @click="openTemplate(t)">
                  <div><strong>{{ t.name }}</strong><em>{{ t.kind }} · {{ templateBody(t).slice(0, 70) }}</em></div>
                  <span class="med-row-actions"><button @click.stop="deleteTemplate(t)">删除</button></span>
                </div>
              </div>
            </template>
            <template v-else>
              <div class="med-mine-head"><strong>检索式</strong><span class="med-hint">保存常用组合：关键词 + 类型 + 分类</span></div>
              <div v-if="!queries.length" class="med-hint">还没有保存的检索式。检索后点搜索框右侧「保存检索式」。</div>
              <div class="med-mine-grid">
                <div v-for="qitem in queries.slice(mineStart, mineEnd)" :key="qitem.id" class="med-mine-row" @click="applyQuery(qitem)">
                  <div><strong>{{ qitem.name }}</strong><em>{{ qitem.dslJson }}</em></div>
                  <span class="med-row-actions"><button @click.stop="deleteQuery(qitem)">删除</button></span>
                </div>
              </div>
            </template>
            <MedPager :total="mineTotal" :page="minePage" :page-size="minePageSize"
                      :sizes="[4, 8, 12, 20, 50]" label="条"
                      @update:page="minePage = $event" @update:page-size="minePageSize = $event" />
          </div>
        </div>
      </main>

      <aside class="med-right">
        <section>
          <div class="med-sec-head"><span>概念详情</span><button v-if="concept" @click="showConceptWide = true" title="放大详情">放大</button></div>
          <div v-if="conceptLoading" class="med-hint">正在加载…</div>
          <MedCard v-else-if="concept" :concept="concept" :q="q" :favorited="isFav('concept', concept.id)"
                   @search="doSearch" @favorite="favorite('concept', concept.id, concept.name)"
                   @graph="showGraph" @open-hit="openHit" @open-term="openConcept" @open="openConcept(concept.id)" />
          <div v-else class="med-hint">点击检索结果中的概念卡查看结构、原文出处与相关条目。卡片本身可点击。</div>
          <button v-if="concept?.type === 'leaflet'" class="med-btn primary" style="margin-top:8px;width:100%" @click="openLeaflet(concept)">打开说明书</button>
          <div v-if="concept" class="med-detail-actions">
            <button @click="editConcept(concept)">编辑</button>
            <button class="danger" @click="deleteConcept(concept)">删除</button>
            <button @click="editConcept(null)">＋ 新增</button>
          </div>
          <button v-else class="med-btn" @click="editConcept(null)">＋ 新增临床条目</button>
        </section>
        <section>
          <div class="med-sec-head"><span>最近注释</span><em>{{ notes.length }}</em></div>
          <div v-for="n in notes.slice(0, 8)" :key="n.id" class="med-note-line">
            <span @click="editNote(n)">{{ n.title || '未命名注释' }}</span><button @click.stop="removeNote(n)">×</button>
          </div>
          <div v-if="!notes.length" class="med-hint">在精读页选中文字后点「＋ 注释」</div>
        </section>
        <section>
          <div class="med-sec-head"><span>我的知识</span></div>
          <button class="med-side-link" @click="openMine('favorites')">我的收藏 <em>{{ favoriteCount }}</em></button>
          <button class="med-side-link" @click="openMine('templates')">我的模板 <em>{{ myTemplates.length }}</em></button>
          <button class="med-side-link" @click="openBuiltin">内置模板库 <em>{{ builtinCount }}</em></button>
        </section>
      </aside>
    </div>

    <footer class="med-foot">
      <span>医学助手 v{{ medVersion }}</span>
      <span v-if="status">medical.db {{ fmtBytes(status.dbBytes) }} · {{ status.books }} 本 / {{ status.chapters }} 章 / {{ status.chunks }} 段 · {{ status.concepts }} 概念 · {{ status.links }} 条链</span>
      <span>锚点重建 {{ anchorRate }}</span>
      <span v-if="report" class="med-report">最近：{{ report.kind }} {{ report.chapters }} 章 / {{ report.anchorResolved }}/{{ report.anchorTotal }} 锚点 / {{ report.elapsedMs }}ms</span>
      <span v-if="busy" class="med-busy">{{ busy }}</span>
    </footer>

    <div v-if="showData" class="med-modal-mask" @click.self="showData = false">
      <div class="med-modal">
        <div class="med-modal-head">
          <strong>数据与设置</strong>
          <div class="med-modal-tabs">
            <button :class="{ on: dataTab === 'sources' }" @click="dataTab = 'sources'">数据源</button>
            <button :class="{ on: dataTab === 'import' }" @click="dataTab = 'import'">导入</button>
            <button :class="{ on: dataTab === 'maintain' }" @click="dataTab = 'maintain'">维护</button>
          </div>
          <button @click="showData = false">×</button>
        </div>
        <div class="med-modal-body">
          <template v-if="dataTab === 'sources'">
            <div class="med-stat-grid">
              <div><b>{{ status?.books || 0 }}</b><span>书籍</span></div>
              <div><b>{{ status?.chapters || 0 }}</b><span>章节</span></div>
              <div><b>{{ status?.chunks || 0 }}</b><span>检索段</span></div>
              <div><b>{{ status?.concepts || 0 }}</b><span>临床条目</span></div>
              <div><b>{{ fmtBytes(status?.dbBytes || 0) }}</b><span>数据库</span></div>
            </div>
            <h4>已索引书籍</h4>
            <div v-if="!books.length" class="med-hint">还没有书籍。切换到「导入」添加 EPUB 或 Markdown。</div>
            <div v-for="b in books" :key="b.id" class="med-source-row">
              <div class="med-source-main">
                <strong>{{ b.volume ? '[' + b.volume + '] ' : '' }}{{ b.title }}</strong>
                <em :title="b.path">{{ b.path }}</em>
              </div>
              <span>{{ b.status === 'ready' ? `${b.chapterCount} 章 · 锚点 ${b.anchorResolved}/${b.anchorTotal}` : b.status }}</span>
              <button @click="removeBook(b.id)">移除</button>
            </div>
            <h4>临床数据</h4>
            <p class="med-hint">版本 {{ status?.clinicalVersion || '尚未导入' }} · 快照目录 {{ status?.sourcesDir }}</p>
          </template>

          <template v-else-if="dataTab === 'import'">
            <h4>内置资料</h4>
            <p class="med-hint">只导入本机实际存在的文件；未找到的条目会跳过。clinical 数据用于疾病/药品/检验/指南速查。</p>
            <div v-for="s in status?.defaultBooks || []" :key="s.path" class="med-source-row">
              <div class="med-source-main"><strong>{{ s.title }}</strong><em :title="s.path">{{ s.path }}</em></div>
              <span :class="{ 'med-ok': s.exists, 'med-missing': !s.exists }">{{ s.exists ? '已找到' : '未找到' }}</span>
            </div>
            <p class="med-hint">clinical：{{ status?.clinicalDefault || '未设置' }}（{{ status?.clinicalDefaultExists ? '已找到' : '未找到' }}）</p>
            <div class="med-row">
              <button class="med-primary" @click="importDefaults">导入检测到的内置资料</button>
              <button @click="importClinical">仅导入 clinical</button>
            </div>

            <h4>clinical JSON 目录</h4>
            <div class="med-row">
              <input v-model="clinicalPath" class="med-mini-input" placeholder="包含 manifest.json 与 8 个 JSON 的目录" />
              <button @click="pickClinicalFolder">浏览…</button>
              <button class="med-primary" @click="importClinical">导入</button>
            </div>

            <h4>添加书籍 / Markdown</h4>
            <input v-model="importPath" class="med-mini-input wide" placeholder="EPUB 或 Markdown 绝对路径" />
            <div class="med-row">
              <button @click="pickFile">浏览…</button>
              <select v-model="importKind"><option value="epub">EPUB</option><option value="markdown">Markdown</option></select>
              <input v-model="importVolume" class="med-mini-input" placeholder="卷次（可选）" />
              <button class="med-primary" @click="importCustom">导入</button>
            </div>

            <h4>OCR JSON 图书（PaddleOCR-VL 格式）</h4>
            <p class="med-hint">自动把 JSON 里引用的远程图片下载到 data/medical/jsonbooks/，按页生成章节与段落索引；原始 JSON 不修改。可选单个 .json，也可选整个目录批量导入（同格式即可，以后新增图书直接放目录里再导入）。</p>
            <div class="med-row">
              <input v-model="jsonBookPath" class="med-mini-input" placeholder="JSON 文件路径，或包含 JSON 图书的目录" />
              <button @click="pickJsonBook">选择文件</button>
              <button @click="pickJsonBookFolder">选择目录</button>
              <button class="med-primary" @click="importJsonBooks">导入</button>
            </div>

            <h4>药品说明书速查</h4>
            <div class="med-leaflet">
              <input v-for="k in ['name','brand','spec','indication','dosage','contraindication','adverse','interaction','storage']" :key="k"
                     v-model="leaflet[k]" class="med-mini-input" :placeholder="({ name:'药品名称*', brand:'商品名', spec:'规格', indication:'适应症', dosage:'用法用量', contraindication:'禁忌', adverse:'不良反应', interaction:'相互作用', storage:'贮藏' } as any)[k]" />
            </div>
            <button class="med-primary" @click="importLeaflet">加入速查</button>
            <div v-if="report?.messages?.length" class="med-report-lines">
              <div v-for="(m, i) in report.messages.slice(-6)" :key="i">{{ m }}</div>
            </div>
          </template>

          <template v-else>
            <h4>维护</h4>
            <p class="med-hint">新增书籍后会自动重建概念命中；只有数据异常时才需要手动重建。重建可能耗时数十秒。</p>
            <div class="med-row">
              <button @click="askReindex">重建概念索引</button>
              <button @click="exportData">导出笔记 / 模板 / 检索式</button>
            </div>
            <h4>关于</h4>
            <p class="med-hint">医学助手 v{{ medVersion }} · 数据库 {{ fmtBytes(status?.dbBytes || 0) }} · 全部本地离线，不上传任何内容。</p>
            <button @click="showHelp = true">打开使用说明</button>
          </template>
        </div>
      </div>
    </div>

    <div v-if="showHelp" class="med-modal-mask" @click.self="showHelp = false">
      <div class="med-modal help">
        <div class="med-modal-head"><strong>医学助手使用说明 <span class="med-ver">v{{ medVersion }}</span></strong><button @click="showHelp = false">×</button></div>
        <div class="med-modal-body">
          <h4>它是什么</h4>
          <p>本地医学速查工具：把医学书原文与临床条目放在一起检索，并支持跳回书中段落、写注释、收藏与模板。全程离线，<b>仅供学习参考，不作诊疗依据</b>。</p>
          <h4>第一次使用</h4>
          <ol>
            <li>点右上角「数据与设置 → 导入」。</li>
            <li>有内置资料时点「导入检测到的内置资料」；没有则用「添加书籍 / Markdown」选择自己的 EPUB。</li>
            <li>导入完成后等待索引结束（底部会显示进度），再回到检索页。</li>
          </ol>
          <h4>检索技巧</h4>
          <ul>
            <li>直接搜疾病、症状、药品、检验、指南名称；也支持两字词，例如「心梗」。</li>
            <li>纯字母可试拼音首字母：<code>xlsj</code> → 心力衰竭，<code>amxljn</code> → 阿莫西林胶囊。</li>
            <li>搜索框留空直接检索可浏览全部条目；再叠加类型/分类/收藏筛选。</li>
            <li>结果分「临床速查」和「原文段落」：前者是结构化卡片，后者可跳回书中原文。</li>
            <li>左侧「临床分类」可直接浏览；「类型筛选」可只看药品/检验等；输入关键词后再选数据源可只看某本书。</li>
          </ul>
          <h4>精读与注释</h4>
          <ul>
            <li>点原文结果进入「精读」，会自动滚动并高亮命中段落。</li>
            <li>选中文字后点「＋ 注释」，可写 Markdown，支持 <code>[[急性心肌梗死]]</code> 双链。</li>
            <li>选中文字后还可「＋ 概念」加入自建条目（可新建分类/分组），或「＋ 锚点」保存当前段落跳转。</li>
            <li>疾病/症状/药品/指南等条目在右侧详情可编辑、删除或新增；不会修改原书文件。</li>
            <li>「药品说明书」类型有专用阅读界面，左侧目录可跳到适应症、用法用量、药理作用、禁忌等章节。</li>
            <li>注释、收藏与模板都在「我的」页签中编辑或删除；模板可预览后直接编辑。</li>
          </ul>
          <h4>快捷键</h4>
          <p><code>Ctrl+K</code> 聚焦搜索 · <code>Ctrl+D</code> 收藏当前概念/章节 · <code>Esc</code> 关闭弹窗。</p>
          <h4>常见问题</h4>
          <ul>
            <li>搜不到：先到「数据与设置 → 数据源」确认书籍状态为 ready、章节数不为 0。</li>
            <li>章节没有正文：Markdown 导入需要重新导入以生成段落；EPUB 原文件移动后路径会失效。</li>
            <li>结果太多时用底部分页条：可设置每页数量、点页码或输入页码跳转；默认卡片每页 4 张（两行）、原文每页 10 段。</li>
          </ul>
          <p class="med-hint">详细文档：docs/医学助手使用说明.md · 数据保存在 data/medical.db。</p>
        </div>
      </div>
    </div>

    <div v-if="showConcept" class="med-modal-mask" @click.self="showConcept = false">
      <div class="med-modal">
        <div class="med-modal-head"><strong>{{ conceptDraft.id ? '编辑临床条目' : '新增临床条目' }}</strong><button @click="showConcept = false">×</button></div>
        <div class="med-modal-body">
          <div class="med-row">
            <input v-model="conceptDraft.name" class="med-mini-input" placeholder="名称（疾病/症状/药品/指南…）" />
            <select v-model="conceptDraft.type" class="med-mini-input" style="width:130px">
              <option v-for="t in TYPE_CHIPS" :key="t.key" :value="t.key">{{ t.label }}</option>
              <option value="document">文书</option>
              <option value="other">其他</option>
            </select>
          </div>
          <div class="med-row">
            <input v-model="conceptDraft.category" class="med-mini-input" placeholder="分类 / 分组（可输入新名称，自动创建分类）" />
            <input v-model="conceptDraft.keywords" class="med-mini-input" placeholder="关键词 / 别名，空格分隔" />
          </div>
          <input v-model="conceptDraft.summary" class="med-mini-input wide" style="margin-top:8px" placeholder="摘要（列表卡片显示）" />
          <textarea v-model="conceptDraft.dataJson" class="med-textarea med-json-edit" placeholder='结构化字段 JSON，例如 {"适应症":"…","用法用量":"…"}'></textarea>
          <p class="med-hint">字段使用 JSON；键名会作为说明书目录与卡片字段。当前{{ conceptCtx.chapterId ? '会关联到正在阅读的段落' : '未关联原文段落' }}。</p>
          <div class="med-modal-actions">
            <button @click="showConcept = false">取消</button>
            <button class="med-primary" :disabled="conceptSaving" @click="saveConceptDraft">{{ conceptSaving ? '保存中…' : '保存' }}</button>
          </div>
        </div>
      </div>
    </div>

    <div v-if="showAnchor" class="med-modal-mask" @click.self="showAnchor = false">
      <div class="med-modal small">
        <div class="med-modal-head"><strong>添加锚点</strong><button @click="showAnchor = false">×</button></div>
        <div class="med-modal-body">
          <input v-model="anchorDraft.label" class="med-mini-input wide" placeholder="锚点名称" />
          <input v-model="anchorDraft.note" class="med-mini-input wide" placeholder="备注（可选）" />
          <p class="med-hint">将记录当前选中段落（第 {{ anchorDraft.pStart || 0 }} 段），之后可在本章右侧一键跳回。</p>
          <div class="med-modal-actions">
            <button @click="showAnchor = false">取消</button>
            <button class="med-primary" @click="saveAnchorDraft">保存锚点</button>
          </div>
        </div>
      </div>
    </div>

    <div v-if="showNote" class="med-modal-mask" @click.self="showNote = false">
      <div class="med-modal small">
        <div class="med-modal-head"><strong>{{ editingNoteId ? '编辑注释' : '添加注释' }}</strong><button @click="showNote = false">×</button></div>
        <div class="med-modal-body">
          <input v-model="noteDraft.title" class="med-mini-input wide" placeholder="标题" />
          <div v-if="noteDraft.quote" class="med-quote">{{ noteDraft.quote }}</div>
          <textarea v-model="noteDraft.body" class="med-textarea" placeholder="支持 [[概念]] 双链，例如 [[急性心肌梗死]]"></textarea>
          <div class="med-modal-actions">
            <button v-if="editingNoteId" class="danger" @click="removeEditingNote">删除</button>
            <button class="med-primary" @click="saveNote">保存注释</button>
          </div>
        </div>
      </div>
    </div>

    <div v-if="showTemplate" class="med-modal-mask" @click.self="showTemplate = false">
      <div class="med-modal small">
        <div class="med-modal-head"><strong>{{ editingTemplateId ? '编辑模板' : '新建模板' }}</strong><button @click="showTemplate = false">×</button></div>
        <div class="med-modal-body">
          <input v-model="templateDraft.name" class="med-mini-input wide" placeholder="模板名称" />
          <select v-model="templateDraft.kind" class="med-mini-input wide"><option value="note">笔记模板</option><option value="card">卡片模板</option><option value="doc">文书模板</option></select>
          <textarea v-model="templateDraft.contentJson" class="med-textarea" placeholder='内容 JSON，或 {"content":"..."}'></textarea>
          <button class="med-primary" @click="saveTemplateDraft">保存模板</button>
        </div>
      </div>
    </div>

    <div v-if="showPreview" class="med-modal-mask" @click.self="showPreview = false">
      <div class="med-modal">
        <div class="med-modal-head"><strong>{{ previewTemplate?.name }}</strong><button @click="showPreview = false">×</button></div>
        <div class="med-modal-body">
          <pre class="med-pre">{{ templateBody(previewTemplate) }}</pre>
        </div>
        <div class="med-modal-actions">
          <button v-if="previewTemplate" class="med-primary" @click="saveAsMine(previewTemplate)">另存为我的模板</button>
          <button v-if="previewTemplate && previewTemplate.builtin === 0" @click="editTemplate(previewTemplate)">编辑</button>
          <button v-if="previewTemplate && previewTemplate.builtin === 0" class="danger" @click="deleteTemplate(previewTemplate)">删除</button>
          <button @click="showPreview = false">关闭</button>
        </div>
      </div>
    </div>

    <div v-if="showBuiltin" class="med-modal-mask" @click.self="showBuiltin = false">
      <div class="med-modal">
        <div class="med-modal-head"><strong>内置模板库（{{ builtinCount }}）</strong><button @click="showBuiltin = false">×</button></div>
        <div class="med-modal-body">
          <div class="med-row">
            <input v-model="builtinQuery" class="med-mini-input" placeholder="搜索模板名称，如 入院记录" @keydown.enter="loadBuiltin(builtinQuery)" />
            <button @click="loadBuiltin(builtinQuery)">搜索</button>
          </div>
          <div v-if="buildingBuiltin" class="med-hint">正在读取…</div>
          <div class="med-mine-grid">
            <button v-for="t in builtinTemplates.slice(builtinStart, builtinEnd)" :key="t.id" class="med-mine-row" @click="openTemplate(t)">
              <div><strong>{{ t.name }}</strong><em>{{ templateBody(t).slice(0, 90) }}</em></div>
              <span class="med-row-actions">预览</span>
            </button>
          </div>
          <MedPager :total="builtinTemplates.length" :page="builtinPage" :page-size="builtinPageSize"
                    :sizes="[4, 8, 12, 20, 50]" label="份"
                    @update:page="builtinPage = $event" @update:page-size="builtinPageSize = $event" />
          <div v-if="!buildingBuiltin && !builtinTemplates.length" class="med-hint">没有匹配的内置模板。</div>
        </div>
      </div>
    </div>

    <div v-if="showConceptWide && concept" class="med-modal-mask" @click.self="showConceptWide = false">
      <div class="med-modal wide">
        <div class="med-modal-head"><strong>词条详情 · {{ concept.name }}</strong><button @click="showConceptWide = false">×</button></div>
        <div class="med-modal-body">
          <MedCard :concept="concept" :q="q" :favorited="isFav('concept', concept.id)"
                   @search="doSearch" @favorite="favorite('concept', concept.id, concept.name)"
                   @graph="showGraph" @open-hit="openHit" @open-term="openConcept" />
        </div>
      </div>
    </div>

    <LeafletView v-if="leafletConcept" :concept="leafletConcept" @close="leafletConcept = null" />
  </div>
</template>

<style scoped>
.med-page { display: flex; flex-direction: column; height: 100%; min-height: 0; color: var(--text); }
.med-top { display: flex; align-items: center; gap: 14px; padding: 14px 18px 12px; border-bottom: 1px solid var(--border); }
.med-search-wrap { position: relative; display: flex; flex: 1; max-width: 720px; }
.med-search { flex: 1; height: 38px; border: 1px solid var(--border-strong); border-radius: 10px 0 0 10px; background: var(--panel-2); color: var(--text); padding: 0 14px; font-size: 13px; outline: none; }
.med-search:focus { border-color: var(--accent); }
.med-go { height: 38px; padding: 0 20px; border: none; border-radius: 0 10px 10px 0; background: var(--accent); color: #0a0c10; font-weight: 600; cursor: pointer; }
.med-top-actions { display: flex; gap: 8px; margin-left: auto; }
.med-top-actions button, .med-read-actions button, .med-modal button, .med-graph-bar button { border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 8px; padding: 7px 12px; font-size: 11.5px; cursor: pointer; }
.med-top-actions button:hover, .med-read-actions button:hover, .med-modal button:hover { border-color: var(--accent); color: var(--accent); }
.med-primary { background: var(--accent) !important; color: #0a0c10 !important; font-weight: 600; border: none !important; }
.med-suggest { position: absolute; z-index: 30; top: 40px; left: 0; right: 60px; background: var(--panel); border: 1px solid var(--border-strong); border-radius: 10px; overflow: hidden; box-shadow: var(--shadow); }
.med-suggest button { display: flex; justify-content: space-between; width: 100%; border: none; background: none; color: var(--text); padding: 8px 12px; font-size: 12px; cursor: pointer; text-align: left; }
.med-suggest button:hover { background: var(--panel-2); }
.med-suggest em { color: var(--text-3); font-style: normal; font-size: 11px; }
.med-body { position: relative; display: grid; grid-template-columns: var(--med-left, 180px) var(--med-mid, 520px) minmax(0, 1fr); flex: 1; min-height: 0; }
.med-left, .med-right { overflow-y: auto; padding: 14px 12px 40px; border-right: 1px solid var(--border); }
.med-right { border-right: none; border-left: 1px solid var(--border); }
.med-left section, .med-right section { margin-bottom: 20px; }
.med-sec-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 9px; font-size: 11px; letter-spacing: .06em; color: var(--text-3); }
.med-sec-head em { font-style: normal; }
.med-sec-head button { border: none; background: none; color: var(--accent); cursor: pointer; font-size: 15px; }
.med-book { position: relative; padding: 8px 10px; border: 1px solid transparent; border-radius: 9px; cursor: pointer; margin-bottom: 5px; }
.med-book:hover { background: var(--panel-2); }
.med-book.active { border-color: var(--accent); background: var(--accent-soft); }
.med-book-title { font-size: 12px; color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding-right: 16px; }
.med-book-meta { font-size: 10.5px; color: var(--text-3); margin-top: 3px; }
.med-err { color: var(--danger); }
.med-x { position: absolute; right: 4px; top: 6px; border: none; background: none; color: var(--text-3); cursor: pointer; }
.med-add-book { width: 100%; border: 1px dashed var(--border-strong); background: none; color: var(--text-3); border-radius: 8px; padding: 7px; cursor: pointer; font-size: 11px; }
.med-add-book:hover { color: var(--accent); border-color: var(--accent); }
.med-chips, .med-side-chips { display: flex; flex-wrap: wrap; gap: 5px; }
.med-chips button, .med-side-chips button, .med-tags button { border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 999px; padding: 3px 9px; font-size: 11px; cursor: pointer; }
.med-chips button.on, .med-tags button.on { background: var(--accent-soft); border-color: var(--accent); color: var(--accent); }
.med-tags { display: flex; flex-direction: column; gap: 5px; align-items: flex-start; }
.med-tags button { width: 100%; text-align: left; border-radius: 7px; }
.med-tags em { float: right; font-style: normal; color: var(--text-3); }
.med-mini-input { width: 100%; height: 30px; border: 1px solid var(--border); background: var(--panel-2); color: var(--text); border-radius: 7px; padding: 0 9px; font-size: 11.5px; outline: none; }
.med-mini-input.wide { margin-bottom: 8px; }
.med-query, .med-tpl, .med-note-line { display: flex; justify-content: space-between; align-items: center; gap: 6px; padding: 6px 8px; border-radius: 7px; font-size: 11.5px; color: var(--text-2); cursor: pointer; }
.med-query:hover, .med-tpl:hover, .med-note-line:hover { background: var(--panel-2); color: var(--text); }
.med-query button, .med-note-line button { border: none; background: none; color: var(--text-3); cursor: pointer; }
.med-tpl.builtin { color: var(--text-3); }
.med-tpl-tabs { display: flex; gap: 10px; font-size: 10.5px; color: var(--text-3); margin-bottom: 6px; }
.med-hint { font-size: 11px; color: var(--text-3); padding: 6px 2px; }
.med-main { display: flex; flex-direction: column; min-width: 0; min-height: 0; }
.med-tabs { display: flex; align-items: center; gap: 4px; padding: 9px 16px; border-bottom: 1px solid var(--border); }
.med-tabs button { border: none; background: none; color: var(--text-3); padding: 6px 12px; border-radius: 8px; cursor: pointer; font-size: 12px; }
.med-tabs button.on { background: var(--accent-soft); color: var(--accent); }
.med-stat { margin-left: auto; font-size: 10.5px; color: var(--text-3); }
.med-scroll { flex: 1; min-height: 0; overflow-y: auto; padding: 14px 18px 40px; }
.med-group-title { display: flex; align-items: center; gap: 8px; margin: 12px 0 9px; font-size: 12px; color: var(--text-2); }
.med-group-title em { font-style: normal; font-size: 10.5px; color: var(--text-3); }
.med-concept-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 10px; }
.med-hit { border: 1px solid var(--border); border-radius: 11px; background: var(--panel); padding: 11px 14px; margin-bottom: 9px; cursor: pointer; transition: border-color .18s, transform .18s; }
.med-hit:hover { border-color: var(--accent); transform: translateY(-1px); }
.med-hit-path { font-size: 10.5px; color: var(--accent); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.med-hit-snippet { font-size: 12.5px; line-height: 1.75; color: var(--text-2); margin: 7px 0; }
.med-hit-snippet :deep(mark), .med-content :deep(mark) { background: rgba(255, 200, 87, .24); color: #ffd479; border-radius: 3px; padding: 0 2px; }
.med-hit-foot { display: flex; align-items: center; gap: 7px; }
.med-tag-pill { font-size: 10px; color: var(--text-3); background: var(--panel-2); border-radius: 999px; padding: 2px 8px; }
.med-hit-src { font-size: 10px; color: var(--text-3); margin-left: auto; }
.med-hit-foot button { border: none; background: none; color: var(--text-3); cursor: pointer; font-size: 11px; }
.med-hit-foot .med-jump { color: var(--accent); }
.med-loading { padding: 40px; text-align: center; color: var(--text-3); font-size: 12px; }
.med-read { display: flex; flex-direction: column; flex: 1; min-height: 0; }
.med-read-head { display: flex; align-items: center; gap: 10px; padding: 9px 16px; border-bottom: 1px solid var(--border); }
.med-crumb { flex: 1; font-size: 11px; color: var(--text-3); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.med-read-actions { display: flex; gap: 6px; }
.med-read-actions button:disabled { opacity: .4; cursor: default; }
.med-read-body { display: grid; grid-template-columns: minmax(0, 1fr) 230px; flex: 1; min-height: 0; }
.med-content { overflow-y: auto; padding: 20px 28px 60px; font-size: 14px; line-height: 1.9; color: var(--text); }
.med-content :deep(img) { max-width: 100%; height: auto; border-radius: 8px; margin: 10px 0; }
.med-content :deep(h1), .med-content :deep(h2), .med-content :deep(h3) { color: var(--accent-2); line-height: 1.5; margin: 22px 0 10px; }
.med-content :deep(p) { margin: 0 0 9px; }
.med-read-side { border-left: 1px solid var(--border); overflow-y: auto; padding: 12px; }
.med-side-sec { margin-bottom: 18px; }
.med-side-note { font-size: 11.5px; color: var(--text-2); padding: 6px; border-radius: 7px; background: var(--panel-2); margin-bottom: 5px; }
.med-side-note em { font-style: normal; color: var(--text-3); font-size: 10.5px; }
/* 本章目录：每行给足高度、标题允许换行完整显示，避免单行 ellipsis 看不清 */
.med-siblings { display: flex; flex-direction: column; gap: 5px; max-height: 52vh; overflow-y: auto; padding-right: 4px; }
.med-siblings button {
  display: block; width: 100%; min-height: 38px;
  border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2);
  text-align: left; font-size: 12px; line-height: 1.55; padding: 8px 10px;
  border-radius: 8px; cursor: pointer; white-space: normal; word-break: break-word; overflow-wrap: anywhere;
}
.med-siblings button::before {
  content: attr(data-no); display: inline-block; min-width: 24px; margin-right: 6px;
  color: var(--text-3); font-size: 10.5px; font-variant-numeric: tabular-nums;
}
.med-siblings button:hover { border-color: var(--border-strong); color: var(--text); background: var(--panel-3); }
.med-siblings button.on {
  border-color: var(--accent); background: var(--accent-soft); color: var(--accent);
  font-weight: 600; box-shadow: inset 3px 0 0 var(--accent);
}
.med-siblings button.on::before { color: var(--accent); }
.med-graph-view { display: flex; flex-direction: column; flex: 1; min-height: 0; }
.med-graph-bar { display: flex; align-items: center; gap: 10px; padding: 10px 16px; border-bottom: 1px solid var(--border); font-size: 11px; color: var(--text-3); }
.med-graph-bar .med-mini-input { width: 220px; }
.med-foot { display: flex; gap: 18px; align-items: center; padding: 7px 16px; border-top: 1px solid var(--border); font-size: 10.5px; color: var(--text-3); }
.med-report { color: var(--success); }
.med-busy { margin-left: auto; color: var(--warning); }
.med-modal-mask { position: absolute; inset: 0; background: rgba(0,0,0,.6); display: grid; place-items: center; z-index: 60; }
.med-modal { width: min(720px, 92vw); max-height: 86vh; overflow-y: auto; background: var(--panel); border: 1px solid var(--border-strong); border-radius: 16px; box-shadow: var(--shadow); }
.med-modal.small { width: min(460px, 92vw); }
.med-modal-head { display: flex; justify-content: space-between; align-items: center; padding: 14px 18px; border-bottom: 1px solid var(--border); font-size: 14px; }
.med-modal-head button { border: none; background: none; color: var(--text-3); font-size: 20px; cursor: pointer; }
.med-modal-body { padding: 16px 18px 22px; }
.med-modal-sec { margin-bottom: 20px; }
.med-modal-sec h4 { margin: 0 0 6px; font-size: 12.5px; color: var(--text); }
.med-modal-sec p { margin: 0 0 10px; font-size: 11.5px; color: var(--text-3); }
.med-row { display: flex; gap: 8px; align-items: center; margin-top: 8px; }
.med-row select { height: 30px; border: 1px solid var(--border); background: var(--panel-2); color: var(--text); border-radius: 7px; font-size: 11.5px; }
.med-leaflet { display: grid; grid-template-columns: repeat(3, 1fr); gap: 7px; margin-bottom: 10px; }
.med-textarea { width: 100%; height: 130px; border: 1px solid var(--border); background: var(--panel-2); color: var(--text); border-radius: 8px; padding: 10px; font-size: 12px; margin: 8px 0; resize: vertical; }
.med-quote { border-left: 2px solid var(--accent); background: var(--panel-2); padding: 8px 10px; font-size: 11.5px; color: var(--text-2); margin: 8px 0; }
.med-report-lines { border-top: 1px solid var(--border); padding-top: 10px; font-size: 11px; color: var(--success); }

/* v1.1.0 界面优化 */
.med-top { justify-content: space-between; padding: 16px 22px 12px; border-bottom: none; }
.med-brand { display: flex; align-items: center; gap: 12px; min-width: 0; }
.med-logo { width: 40px; height: 40px; flex: none; display: grid; place-items: center; border-radius: 12px; background: linear-gradient(140deg, var(--accent), color-mix(in srgb, var(--accent) 45%, #7b5cff)); color: #0a0c10; font-weight: 800; font-size: 18px; }
.med-brand-text { min-width: 0; }
.med-brand-text h1 { margin: 0; font-size: 18px; font-weight: 700; letter-spacing: -.01em; display: flex; align-items: center; gap: 8px; }
.med-brand-text p { margin: 3px 0 0; font-size: 11px; color: var(--text-3); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.med-ver { font-size: 10px; font-weight: 600; color: var(--accent); background: var(--accent-soft); border-radius: 999px; padding: 2px 7px; vertical-align: middle; }
.med-btn { border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 8px; padding: 7px 12px; font-size: 11.5px; cursor: pointer; }
.med-btn:hover { border-color: var(--accent); color: var(--accent); }
.med-btn.primary { background: var(--accent); color: #0a0c10; border-color: transparent; font-weight: 600; }
.med-searchbar { flex: none; padding: 0 22px 10px; }
.med-search-wrap { max-width: none; width: 100%; }
.med-save-query { height: 38px; padding: 0 14px; border: 1px solid var(--border); border-left: none; border-radius: 0 10px 10px 0; background: var(--panel-2); color: var(--text-2); font-size: 12px; cursor: pointer; white-space: nowrap; }
.med-save-query:hover { color: var(--accent); border-color: var(--accent); }
.med-go { border-radius: 0; }
.med-result-tabs { display: flex; align-items: center; gap: 6px; margin-bottom: 12px; flex-wrap: wrap; }
.med-result-tabs button { border: 1px solid transparent; background: transparent; color: var(--text-3); border-radius: 8px; padding: 6px 12px; font-size: 12px; cursor: pointer; }
.med-result-tabs button em { font-style: normal; font-size: 10.5px; opacity: .8; margin-left: 4px; }
.med-result-tabs button.on { background: var(--accent-soft); color: var(--accent); }
.med-mini-select { margin-left: auto; height: 30px; border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 7px; font-size: 11.5px; padding: 0 8px; }
.med-welcome { max-width: 780px; margin: 8vh auto 0; text-align: center; padding: 0 20px; }
.med-welcome-icon { width: 64px; height: 64px; margin: 0 auto 16px; border-radius: 20px; display: grid; place-items: center; font-size: 26px; font-weight: 800; color: #0a0c10; background: linear-gradient(140deg, var(--accent), color-mix(in srgb, var(--accent) 45%, #7b5cff)); }
.med-welcome h2 { margin: 0 0 10px; font-size: 22px; }
.med-welcome p { margin: 0 auto 18px; max-width: 620px; font-size: 12.5px; line-height: 1.8; color: var(--text-2); }
.med-welcome code, .med-modal code { background: var(--panel-2); border-radius: 4px; padding: 1px 5px; font-size: .92em; color: var(--accent-2); }
.med-examples { display: flex; justify-content: center; flex-wrap: wrap; gap: 8px; margin-bottom: 22px; }
.med-examples button { border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 999px; padding: 7px 14px; font-size: 12px; cursor: pointer; }
.med-examples button:hover { border-color: var(--accent); color: var(--accent); }
.med-welcome-stats { display: flex; justify-content: center; gap: 34px; padding-top: 18px; border-top: 1px solid var(--border); }
.med-welcome-stats div { display: flex; flex-direction: column; gap: 4px; }
.med-welcome-stats b { font-size: 18px; }
.med-welcome-stats span { font-size: 10.5px; color: var(--text-3); }
.med-note-card { display: flex; flex-direction: column; gap: 5px; width: 100%; text-align: left; border: 1px solid var(--border); background: var(--panel); border-radius: 10px; padding: 10px 12px; margin-bottom: 8px; cursor: pointer; color: var(--text); }
.med-note-card:hover { border-color: var(--accent); }
.med-note-card p { margin: 0; font-size: 11.5px; color: var(--text-3); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.med-side-link { display: flex; justify-content: space-between; width: 100%; border: none; background: none; color: var(--text-2); border-radius: 7px; padding: 7px 6px; font-size: 11.5px; cursor: pointer; text-align: left; }
.med-side-link:hover { background: var(--panel-2); color: var(--text); }
.med-side-link em { font-style: normal; color: var(--text-3); }
.med-side-note { display: block; width: 100%; text-align: left; border: none; cursor: pointer; }
.med-mine { display: flex; flex: 1; min-height: 0; }
.med-mine-nav { width: 150px; flex: none; padding: 14px 8px; border-right: 1px solid var(--border); display: flex; flex-direction: column; gap: 4px; }
.med-mine-nav button { display: flex; justify-content: space-between; border: none; background: none; color: var(--text-2); border-radius: 8px; padding: 8px 10px; font-size: 12px; cursor: pointer; text-align: left; }
.med-mine-nav button.on { background: var(--accent-soft); color: var(--accent); }
.med-mine-nav em { font-style: normal; color: var(--text-3); }
.med-mine-body { flex: 1; min-width: 0; overflow-y: auto; padding: 16px 20px 40px; }
.med-mine-head { display: flex; align-items: center; gap: 10px; margin-bottom: 12px; }
.med-mine-head strong { font-size: 14px; }
.med-mine-head .med-hint { margin-left: auto; }
.med-mine-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; width: 100%; text-align: left; border: 1px solid var(--border); background: var(--panel); color: var(--text); border-radius: 10px; padding: 10px 12px; margin-bottom: 7px; cursor: pointer; }
.med-mine-row:hover { border-color: var(--accent); }
.med-mine-row > div { min-width: 0; display: flex; flex-direction: column; gap: 3px; }
.med-mine-row strong { font-size: 12.5px; }
.med-mine-row em { font-style: normal; font-size: 10.5px; color: var(--text-3); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 100%; }
.med-row-actions { flex: none; font-size: 11px; color: var(--text-3); }
.med-row-actions button { border: none; background: none; color: var(--danger); cursor: pointer; font-size: 11px; }
.med-stat-grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: 8px; margin-bottom: 18px; }
.med-stat-grid div { border: 1px solid var(--border); background: var(--panel-2); border-radius: 10px; padding: 10px; display: flex; flex-direction: column; gap: 4px; }
.med-stat-grid b { font-size: 16px; }
.med-stat-grid span { font-size: 10.5px; color: var(--text-3); }
.med-source-row { display: flex; align-items: center; gap: 12px; padding: 8px 0; border-bottom: 1px solid var(--border); font-size: 11px; color: var(--text-3); }
.med-source-main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 3px; }
.med-source-main strong { font-size: 12px; color: var(--text); font-weight: 600; }
.med-source-main em { font-style: normal; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.med-source-row > button { border: 1px solid var(--border); background: none; color: var(--danger); border-radius: 6px; padding: 3px 8px; font-size: 10.5px; cursor: pointer; }
.med-ok { color: var(--success); }
.med-missing { color: var(--warning); }
.med-modal-tabs { display: flex; gap: 4px; margin-left: auto; margin-right: 10px; }
.med-modal-tabs button { border: none; background: none; color: var(--text-3); border-radius: 7px; padding: 5px 10px; font-size: 11.5px; cursor: pointer; }
.med-modal-tabs button.on { background: var(--accent-soft); color: var(--accent); }
.med-modal-actions { display: flex; justify-content: flex-end; gap: 8px; padding: 12px 18px; border-top: 1px solid var(--border); }
.med-modal-actions button.danger, .med-read-actions button.on { color: var(--warning); border-color: var(--warning); }
.med-pre { margin: 0; white-space: pre-wrap; word-break: break-word; font-family: inherit; font-size: 12px; line-height: 1.8; color: var(--text-2); }
.med-modal.help .med-modal-body { max-height: 64vh; overflow-y: auto; }
.med-modal.help h4 { margin: 16px 0 6px; font-size: 12.5px; color: var(--text); }
.med-modal.help h4:first-child { margin-top: 0; }
.med-modal.help p, .med-modal.help li { font-size: 12px; line-height: 1.8; color: var(--text-2); }
.med-modal.help ul, .med-modal.help ol { margin: 4px 0 8px; padding-left: 20px; }
.med-graph-center { font-size: 11px; color: var(--text-3); }
.med-graph-bar button.on { border-color: var(--accent); color: var(--accent); }
.med-modal-mask { position: fixed; }
.med-json-edit { height: 260px; font-family: "Cascadia Code", Consolas, monospace; font-size: 11.5px; line-height: 1.6; }
.med-detail-actions { display: flex; gap: 6px; margin-top: 10px; flex-wrap: wrap; }
.med-detail-actions button { border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 7px; padding: 5px 10px; font-size: 11px; cursor: pointer; }
.med-detail-actions button:hover { border-color: var(--accent); color: var(--accent); }
.med-detail-actions button.danger { color: var(--danger); border-color: color-mix(in srgb, var(--danger) 35%, transparent); }
.med-anchor-row { display: flex; align-items: center; gap: 4px; margin-bottom: 4px; }
.med-anchor-jump { flex: 1; min-width: 0; border: none; background: none; color: var(--text-2); text-align: left; font-size: 11.5px; padding: 5px 6px; border-radius: 6px; cursor: pointer; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.med-anchor-jump:hover { background: var(--panel-2); color: var(--accent); }
.med-anchor-del { border: none; background: none; color: var(--text-3); cursor: pointer; }

/* v1.4 列表页：窄卡片列 + 宽详情；精读/图谱页：宽正文 + 窄右侧详情，两者都可拖动 */
.med-body.is-reading { grid-template-columns: var(--med-left, 180px) minmax(0, 1fr) var(--med-right, 320px); }
.med-read-body { grid-template-columns: minmax(0, 1fr) 300px; }
.med-concept-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
.med-mine-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; margin-bottom: 4px; }
.med-mine-grid .med-mine-row { width: 100%; margin-bottom: 0; }
.med-modal.wide { width: min(900px, 92vw); }

/* 精读正文与详情页里的词条下划链接 */
.med-page :deep(.med-term) { color: var(--accent-2); background: rgba(110, 168, 254, .08); text-decoration: underline dotted; text-underline-offset: 3px; cursor: pointer; border-radius: 3px; padding: 0 1px; }
.med-page :deep(.med-term:hover) { color: var(--accent); background: var(--accent-soft); text-decoration: underline solid; }
.med-page :deep(.med-hit), .med-content :deep(mark) { background: rgba(255, 200, 87, .24); color: #ffd479; border-radius: 3px; padding: 0 2px; text-decoration: underline dotted; text-underline-offset: 3px; cursor: pointer; }
@media (max-width: 1180px) {
  .med-body { grid-template-columns: var(--med-left, 180px) minmax(260px, var(--med-mid, 520px)) minmax(0, 1fr); }
  .med-body.is-reading { grid-template-columns: var(--med-left, 180px) minmax(0, 1fr) var(--med-right, 320px); }
  .med-concept-grid, .med-mine-grid { grid-template-columns: minmax(0, 1fr); }
}

/* 两条可拖动分隔条：左栏 / 中间，中间 / 右侧详情 */
.med-resizer { position: absolute; top: 0; bottom: 0; width: 8px; z-index: 25; cursor: col-resize; }
.med-resizer::after { content: ''; position: absolute; left: 3px; top: 0; bottom: 0; width: 2px; background: transparent; transition: background .15s; }
.med-resizer:hover::after, .med-body.is-resizing .med-resizer::after { background: var(--accent); }
.med-resizer-left { left: calc(var(--med-left, 180px) - 4px); }
.med-resizer-right { left: calc(var(--med-left, 180px) + var(--med-mid, 520px) - 4px); }
.med-body.is-reading .med-resizer-right { left: auto; right: calc(var(--med-right, 320px) - 4px); }
.med-body.is-resizing { user-select: none; }
</style>
