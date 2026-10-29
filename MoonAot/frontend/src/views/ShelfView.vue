<script setup lang="ts">
defineOptions({ name: 'ShelfView' })

import { computed, onActivated, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ArrowDown, ArrowUp, ArrowUpDown, BookOpen, Copy, Dices, FolderOpen, Heart, ImagePlus,
  Info, LibraryBig, Pencil, Plus, RefreshCw, Search, SlidersHorizontal, Trash2, UploadCloud, X,
} from 'lucide-vue-next'
import { NButton, NInput, NPopover, NSelect, NTooltip } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import VirtualGrid from '../components/VirtualGrid.vue'
import BookDetail from '../components/BookDetail.vue'
import ContextMenu from '../components/ContextMenu.vue'
import type { MenuItem } from '../components/ContextMenu.vue'
import { api, uploadBook } from '../api'
import {
  finishImport, libraryTick, loadFacets, loadSettings, saveSettings, scheduleLibraryRefresh, state,
} from '../store'
import { host } from '../host'
import { confirmDialog, toast, toastError } from '../ui'
import {
  basename, debounce, isCoverCapable,
} from '../utils'
import type { LibraryItem } from '../types'

const PAGE = 120
const router = useRouter()
const route = useRoute()

const q = ref('')
const sort = ref(state.settings.sortMode || 'recent')
const desc = ref(state.settings.sortDesc ?? true)
const filter = ref('all')
const format = ref('all')
const author = ref('')
const folder = ref('')

const total = ref(0)
const items = ref<(LibraryItem | undefined)[]>([])
const loading = ref(true)
const gridRef = ref<InstanceType<typeof VirtualGrid> | null>(null)
const searchRef = ref<any>(null)

const loadedPages = new Set<number>()
const inflight = new Map<number, Promise<void>>()
let generation = 0
let activeSig = ''
let loadedOnce = false

const importPop = ref(false)
const filterPop = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const folderInput = ref<HTMLInputElement | null>(null)
const coverInput = ref<HTMLInputElement | null>(null)
const coverTarget = ref<LibraryItem | null>(null)
const dragOver = ref(false)

const menu = reactive({ show: false, x: 0, y: 0, item: null as LibraryItem | null })
const detailShow = ref(false)
const detailItem = ref<LibraryItem | null>(null)

const facets = computed(() => state.facets)
const itemWidth = computed(() => state.settings.gridSize || 168)

const sortOptions = [
  { label: '最近阅读', value: 'recent' },
  { label: '导入时间', value: 'added' },
  { label: '书名', value: 'title' },
  { label: '作者', value: 'author' },
  { label: '文件大小', value: 'size' },
  { label: '格式', value: 'format' },
]
const formatOptions = [
  { label: '全部格式', value: 'all' },
  { label: 'TXT', value: 'TXT' },
  { label: 'EPUB', value: 'EPUB' },
  { label: 'PDF', value: 'PDF' },
  { label: 'MOBI', value: 'MOBI' },
  { label: 'AZW3', value: 'AZW3' },
  { label: 'Markdown', value: 'MD' },
]
const statusChips = computed(() => [
  { key: 'all', label: '全部', count: facets.value?.total ?? 0 },
  { key: 'reading', label: '在读', count: facets.value?.reading ?? 0 },
  { key: 'unread', label: '未读', count: facets.value?.unread ?? 0 },
  { key: 'finished', label: '已读', count: facets.value?.finished ?? 0 },
  { key: 'favorite', label: '收藏', count: facets.value?.favorites ?? 0 },
])
const authorOptions = computed(() => (facets.value?.authors ?? []).map(a => ({
  label: `${a.name}（${a.count}）`, value: a.name,
})))
const folderOptions = computed(() => (facets.value?.folders ?? []).map(f => ({
  label: basename(f.name) + `（${f.count}）`, value: f.name,
})))
const hasFilters = computed(() => !!q.value || filter.value !== 'all' || format.value !== 'all' || !!author.value || !!folder.value)
const subtitle = computed(() => {
  const f = facets.value
  if (!f) return `${total.value} 本`
  return `${total.value} 本 · ${f.reading} 本在读 · ${f.favorites} 本收藏`
})

function signature() {
  return [q.value.trim(), sort.value, desc.value ? 1 : 0, filter.value, format.value, author.value, folder.value].join('|')
}

function params(offset: number, limit: number, needCount = true) {
  return {
    offset, limit,
    count: needCount ? 1 : 0,
    q: q.value.trim() || undefined,
    sort: sort.value,
    desc: desc.value ? 1 : 0,
    filter: filter.value,
    format: format.value === 'all' ? undefined : format.value,
    author: author.value || undefined,
    folder: folder.value || undefined,
  }
}

function resizeItems(n: number) {
  const old = items.value
  const arr = new Array(n)
  const len = Math.min(n, old.length)
  for (let i = 0; i < len; i++) arr[i] = old[i]
  items.value = arr
}

function loadPage(page: number, force = false): Promise<void> {
  const gen = generation
  if (!force && loadedPages.has(page)) return Promise.resolve()
  const running = inflight.get(page)
  if (running) return running

  const promise = api.libraryPage(params(page * PAGE, PAGE, page === 0))
    .then((res) => {
      if (gen !== generation) return
      if (res.total >= 0) {
        if (res.total !== total.value) resizeItems(res.total)
        total.value = res.total
      }
      res.items.forEach((it, i) => { items.value[res.offset + i] = it })
      loadedPages.add(page)
      if (page === 0) loading.value = false
    })
    .catch((e) => {
      if (gen === generation) {
        loading.value = false
        toastError(e)
      }
    })
    .finally(() => {
      if (gen === generation && inflight.get(page) === promise) inflight.delete(page)
    })
  inflight.set(page, promise)
  return promise
}

function ensure(start: number, end: number) {
  if (!total.value && !loading.value) return
  const p0 = Math.floor(start / PAGE)
  const p1 = Math.floor(Math.max(start, end - 1) / PAGE)
  for (let p = p0; p <= p1; p++) void loadPage(p)
  if (p1 + 1 < Math.ceil(total.value / PAGE)) void loadPage(p1 + 1)
}

function reset() {
  generation++
  activeSig = signature()
  total.value = 0
  items.value = []
  loading.value = true
  loadedPages.clear()
  inflight.clear()
  gridRef.value?.scrollToTop()
  void loadPage(0, true)
  void loadFacets()
}

async function refreshVisible() {
  const pages = [...loadedPages].sort((a, b) => a - b)
  if (!pages.length) { reset(); return }
  for (const p of pages) await loadPage(p, true)
  void loadFacets()
}

const resetSoon = debounce(() => reset(), 260)

watch(q, () => resetSoon())
watch([sort, desc], () => {
  void saveSettings({ sortMode: sort.value, sortDesc: desc.value })
  reset()
})
watch([filter, format, author, folder], () => reset())
watch(() => libraryTick.value, () => { if (route.name === 'shelf') void refreshVisible() })

function openItem(item: LibraryItem) {
  if (item.missing) {
    toast('这本书的文件已不存在，可在详情中查看原路径', 'warning')
    showDetail(item)
    return
  }
  host.openBook(item.hash, {
    q: q.value.trim() || undefined,
    sort: sort.value,
    desc: desc.value,
    filter: filter.value,
    format: format.value === 'all' ? undefined : format.value,
    author: author.value || undefined,
    folder: folder.value || undefined,
  })
}

function showDetail(item: LibraryItem) {
  detailItem.value = item
  detailShow.value = true
}

function openMenu(item: LibraryItem, ev: MouseEvent) {
  let x = ev?.clientX ?? 0
  let y = ev?.clientY ?? 0
  if (!x && !y) {
    const el = ev?.target as HTMLElement | null
    const r = el?.getBoundingClientRect()
    if (r) { x = r.right; y = r.bottom }
  }
  menu.item = item
  menu.x = x
  menu.y = y
  menu.show = true
}

const menuItems = computed<MenuItem[]>(() => {
  const it = menu.item
  if (!it) return []
  return [
    { key: 'open', label: '打开阅读', icon: BookOpen, disabled: it.missing },
    { key: 'detail', label: '书籍详情', icon: Info },
    { key: 'fav', label: it.favorite ? '取消收藏' : '加入收藏', icon: Heart },
    { key: 'rename', label: '重命名', icon: Pencil },
    { key: 'cover', label: '更换封面', icon: ImagePlus },
    { key: 'reveal', label: '在文件夹中显示', icon: FolderOpen },
    { key: 'copy', label: '复制文件路径', icon: Copy },
    { key: 'd1', label: '', divider: true },
    { key: 'delete', label: '从书架移除', icon: Trash2, danger: true },
  ]
})

async function onMenuSelect(key: string) {
  const it = menu.item
  if (!it) return
  try {
    switch (key) {
      case 'open': openItem(it); break
      case 'detail': showDetail(it); break
      case 'fav': {
        await api.favorite(it.hash, !it.favorite)
        it.favorite = !it.favorite
        scheduleLibraryRefresh(200)
        toast(it.favorite ? '已加入收藏' : '已取消收藏')
        break
      }
      case 'rename': showDetail(it); break
      case 'cover': {
        coverTarget.value = it
        coverInput.value?.click()
        break
      }
      case 'reveal': {
        const d = await api.detail(it.hash)
        host.reveal(d.filePath)
        break
      }
      case 'copy': {
        const d = await api.detail(it.hash)
        await navigator.clipboard.writeText(d.filePath)
        toast('路径已复制')
        break
      }
      case 'delete': {
        const ok = await confirmDialog({
          title: '从书架移除',
          content: `确定移除《${it.title}》吗？只移除书架记录与可再生的封面缓存，不会删除原文件。`,
          positiveText: '移除',
          danger: true,
        })
        if (!ok) return
        await api.deleteBook(it.hash)
        toast('已移除')
        scheduleLibraryRefresh(0)
        break
      }
    }
  } catch (e) { toastError(e) }
}

function onItemFavorite(item: LibraryItem) {
  item.favorite = !item.favorite
  scheduleLibraryRefresh(260)
}

function onDetailChanged() {
  scheduleLibraryRefresh(120)
}

function onDeleted() {
  detailShow.value = false
  scheduleLibraryRefresh(0)
}

async function onCoverFile(ev: Event) {
  const input = ev.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file || !coverTarget.value) return
  try {
    const dataUrl = await readDataUrl(file)
    await api.updateBook(coverTarget.value.hash, { coverDataUrl: dataUrl })
    toast('封面已更换')
    scheduleLibraryRefresh(120)
  } catch (e) { toastError(e) }
  coverTarget.value = null
}

function readDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result || ''))
    reader.onerror = () => reject(new Error('读取图片失败'))
    reader.readAsDataURL(file)
  })
}

const bookExts = ['.txt', '.epub', '.pdf', '.mobi', '.azw3', '.md', '.markdown']
function isBookFile(name: string) {
  const lower = name.toLowerCase()
  return bookExts.some(e => lower.endsWith(e))
}

function chooseImportFiles() {
  importPop.value = false
  if (host.available) host.importFiles()
  else fileInput.value?.click()
}

function chooseImportFolder() {
  importPop.value = false
  if (host.available) host.importFolder()
  else folderInput.value?.click()
}

function chooseUpload() {
  importPop.value = false
  fileInput.value?.click()
}

function onFilesChosen(ev: Event) {
  const input = ev.target as HTMLInputElement
  const files = Array.from(input.files || [])
  input.value = ''
  void uploadFiles(files)
}

function onFolderChosen(ev: Event) {
  const input = ev.target as HTMLInputElement
  const files = Array.from(input.files || []).filter(f => isBookFile(f.name))
  input.value = ''
  void uploadFiles(files)
}

async function uploadFiles(files: File[]) {
  const list = files.filter(f => isBookFile(f.name))
  if (!list.length) { toast('没有可导入的书籍文件', 'warning'); return }
  state.importState.active = true
  state.importState.done = 0
  state.importState.total = list.length
  state.importState.current = ''
  state.importState.result = null
  let imported = 0
  let failed = 0
  for (let i = 0; i < list.length; i++) {
    const f = list[i]
    state.importState.current = f.name
    try {
      await uploadBook(f, (done, total) => {
        state.importState.done = i + (total ? done / total : 0)
      })
      imported++
    } catch (e) {
      failed++
      console.warn('导入失败', f.name, e)
    }
    state.importState.done = i + 1
  }
  finishImport({ ok: true, imported, skipped: 0, failed })
}

function onDrop(ev: DragEvent) {
  dragOver.value = false
  const files = Array.from(ev.dataTransfer?.files || [])
  void uploadFiles(files)
}

function onDragLeave(ev: DragEvent) {
  const el = ev.currentTarget as HTMLElement | null
  if (el && ev.relatedTarget && el.contains(ev.relatedTarget as Node)) return
  dragOver.value = false
}

async function openRandom() {
  if (!total.value) { toast('书架还是空的', 'warning'); return }
  try {
    const idx = Math.floor(Math.random() * total.value)
    const page = Math.floor(idx / PAGE)
    const res = await api.libraryPage(params(page * PAGE, PAGE))
    const item = res.items[idx % PAGE] ?? res.items[0]
    if (item) openItem(item)
  } catch (e) { toastError(e) }
}

function clearFilters() {
  q.value = ''
  filter.value = 'all'
  format.value = 'all'
  author.value = ''
  folder.value = ''
}

const onGlobalImport = () => { importPop.value = true }

function onKeyDown(ev: KeyboardEvent) {
  if (route.name !== 'shelf') return
  const tag = (document.activeElement?.tagName || '').toLowerCase()
  const typing = tag === 'input' || tag === 'textarea' || (document.activeElement as HTMLElement)?.isContentEditable
  if (ev.key === '/' && !typing) {
    ev.preventDefault()
    searchRef.value?.focus?.()
  }
}

onMounted(() => {
  if (!loadedOnce) { loadedOnce = true; reset() }
  window.addEventListener('moonaot-import', onGlobalImport)
  window.addEventListener('keydown', onKeyDown)
})

onActivated(() => {
  if (!loadedOnce) { loadedOnce = true; reset() }
})

onBeforeUnmount(() => {
  window.removeEventListener('moonaot-import', onGlobalImport)
  window.removeEventListener('keydown', onKeyDown)
})

</script>

<template>
  <div
    class="page shelf-page"
    @dragover.prevent="dragOver = true"
    @dragleave="onDragLeave"
    @drop.prevent="onDrop"
  >
    <PageHeader title="书架" :subtitle="subtitle">
      <div class="head-search">
        <n-input
          ref="searchRef"
          v-model:value="q"
          placeholder="搜索书名、作者…  ( / )"
          clearable
          size="medium"
        >
          <template #prefix><search :size="15" /></template>
        </n-input>
      </div>
      <div class="seg-control">
        <n-select v-model:value="sort" :options="sortOptions" size="medium" class="sort-select" />
        <n-tooltip trigger="hover">
          <template #trigger>
            <button class="icon-btn" @click="desc = !desc">
              <arrow-down v-if="desc" :size="16" />
              <arrow-up v-else :size="16" />
            </button>
          </template>
          {{ desc ? '降序' : '升序' }}
        </n-tooltip>
      </div>
      <n-popover v-model:show="importPop" trigger="click" placement="bottom-end" :show-arrow="false">
        <template #trigger>
          <n-button type="primary" size="medium">
            <template #icon><plus :size="16" /></template>导入书籍
          </n-button>
        </template>
        <div class="import-menu">
          <button @click="chooseImportFiles"><upload-cloud :size="16" /><span><b>导入文件</b><small>直接登记原文件位置，不复制</small></span></button>
          <button @click="chooseImportFolder"><folder-open :size="16" /><span><b>导入文件夹</b><small>递归扫描全部支持的格式</small></span></button>
          <button @click="chooseUpload"><refresh-cw :size="16" /><span><b>上传副本</b><small>把文件复制到程序数据目录</small></span></button>
        </div>
      </n-popover>
      <n-tooltip trigger="hover">
        <template #trigger>
          <button class="icon-btn" @click="openRandom"><dices :size="17" /></button>
        </template>
        随机打开一本
      </n-tooltip>
    </PageHeader>

    <div class="filter-row">
      <div class="chips">
        <button
          v-for="c in statusChips"
          :key="c.key"
          class="chip"
          :class="{ active: filter === c.key }"
          @click="filter = c.key"
        >
          {{ c.label }}<span v-if="c.count" class="chip-count">{{ c.count > 9999 ? '9999+' : c.count }}</span>
        </button>
      </div>
      <div class="filter-right">
        <n-select v-model:value="format" :options="formatOptions" size="small" class="format-select" />
        <n-popover v-model:show="filterPop" trigger="click" placement="bottom-end" :show-arrow="false">
          <template #trigger>
            <button class="chip ghost" :class="{ active: author || folder }">
              <sliders-horizontal :size="14" />筛选
            </button>
          </template>
          <div class="filter-pop">
            <label>作者</label>
            <n-select v-model:value="author" :options="authorOptions" filterable clearable placeholder="全部作者" size="small" />
            <label>文件夹</label>
            <n-select v-model:value="folder" :options="folderOptions" filterable clearable placeholder="全部文件夹" size="small" />
            <button v-if="author || folder" class="link-btn" @click="author = ''; folder = ''">清除筛选</button>
          </div>
        </n-popover>
        <button v-if="hasFilters" class="chip ghost" @click="clearFilters"><x :size="13" />清除</button>
        <n-tooltip trigger="hover">
          <template #trigger>
            <button class="icon-btn" title="刷新" @click="refreshVisible"><refresh-cw :size="15" /></button>
          </template>
          刷新进度
        </n-tooltip>
      </div>
    </div>


    <div class="grid-wrap">
      <VirtualGrid
        ref="gridRef"
        :items="items"
        :total="total"
        :loading="loading"
        :item-width="itemWidth"
        :ensure="ensure"
        @open="openItem"
        @menu="openMenu"
        @favorite="onItemFavorite"
      />
      <div v-if="!loading && total === 0" class="empty-state">
        <div class="empty-icon"><library-big :size="46" :stroke-width="1.2" /></div>
        <h3>{{ hasFilters ? '没有匹配的书籍' : '书架还是空的' }}</h3>
        <p>{{ hasFilters ? '换个关键词或清除筛选条件试试' : '支持 TXT / EPUB / PDF / MOBI / AZW3 / Markdown，可直接导入文件夹' }}</p>
        <div class="empty-actions">
          <n-button v-if="hasFilters" @click="clearFilters">清除筛选</n-button>
          <template v-else>
            <n-button type="primary" @click="chooseImportFiles"><template #icon><plus :size="15" /></template>导入文件</n-button>
            <n-button @click="chooseImportFolder"><template #icon><folder-open :size="15" /></template>导入文件夹</n-button>
          </template>
        </div>
      </div>
    </div>

    <transition name="fade">
      <div v-if="dragOver" class="drop-overlay">
        <upload-cloud :size="42" :stroke-width="1.3" />
        <strong>松开以导入书籍</strong>
        <span>Dragged files will be copied into the library</span>
      </div>
    </transition>

    <ContextMenu
      :show="menu.show"
      :x="menu.x"
      :y="menu.y"
      :items="menuItems"
      @select="onMenuSelect"
      @close="menu.show = false"
    />

    <BookDetail
      v-model:show="detailShow"
      :item="detailItem"
      @changed="onDetailChanged"
      @deleted="onDeleted"
    />

    <input
      ref="fileInput"
      type="file"
      multiple
      accept=".txt,.epub,.pdf,.mobi,.azw3,.md,.markdown"
      hidden
      @change="onFilesChosen"
    />
    <input ref="folderInput" type="file" webkitdirectory multiple hidden @change="onFolderChosen" />
    <input ref="coverInput" type="file" accept="image/png,image/jpeg,image/webp" hidden @change="onCoverFile" />
  </div>
</template>
