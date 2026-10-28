<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  ArrowUp, BookOpen, ChevronRight, Copy, File, FileArchive, Film, Folder, FolderOpen,
  Image as ImageIcon, Loader2, Music, RefreshCw, Search, X,
} from 'lucide-vue-next'
import { NButton, NInput } from 'naive-ui'
import { api, urls } from '../../api'
import { host } from '../../host'
import { toast, toastError } from '../../ui'
import type { ZipItem, ZipList } from '../../types'
import { basename, formatBytes } from '../../utils'

const route = useRoute()
const zip = ref(String(route.query.path || ''))
const prefix = ref('')
const list = ref<ZipList | null>(null)
const loading = ref(true)
const error = ref('')
const q = ref('')
const selected = ref<ZipItem | null>(null)
const extracting = ref(false)
const scroller = ref<HTMLElement | null>(null)
const scrollTop = ref(0)
const viewport = ref(0)

const ROW = 42
const OVERSCAN = 6

const entries = computed(() => list.value?.items ?? [])
const filtered = computed(() => {
  const needle = q.value.trim().toLowerCase()
  if (!needle) return entries.value
  return entries.value.filter(e => e.name.toLowerCase().includes(needle))
})
const startRow = computed(() => Math.max(0, Math.floor(scrollTop.value / ROW) - OVERSCAN))
const endRow = computed(() => Math.min(filtered.value.length - 1, Math.ceil((scrollTop.value + viewport.value) / ROW) + OVERSCAN))
const visibleRows = computed(() => {
  const arr: number[] = []
  for (let i = startRow.value; i <= endRow.value && i - startRow.value < 200; i++) arr.push(i)
  return arr
})
const crumbs = computed(() => {
  const parts = (prefix.value || '').split('/').filter(Boolean)
  const out: { name: string; prefix: string }[] = []
  let acc = ''
  for (const p of parts) { acc += p + '/'; out.push({ name: p, prefix: acc }) }
  return out
})
const parentPrefix = computed(() => {
  const parts = (prefix.value || '').split('/').filter(Boolean)
  parts.pop()
  return parts.length ? parts.join('/') + '/' : ''
})
const totalSize = computed(() => entries.value.reduce((n, e) => n + (e.isDir ? 0 : e.size), 0))

async function load(p = prefix.value) {
  loading.value = true
  error.value = ''
  try {
    const res = await api.zipList(zip.value, p)
    list.value = res
    prefix.value = res.prefix || ''
    selected.value = null
    q.value = ''
    if (scroller.value) scroller.value.scrollTop = 0
    scrollTop.value = 0
    void api.saveZipLast(zip.value, prefix.value || null)
    await nextTick()
    measure()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
    toastError(e)
  } finally { loading.value = false }
}

function measure() {
  if (!scroller.value) return
  viewport.value = scroller.value.clientHeight
}

function onScroll() { scrollTop.value = scroller.value?.scrollTop ?? 0 }

function enterDir(e: ZipItem) {
  load(e.path.endsWith('/') ? e.path : e.path + '/')
}

function goUp() { if (parentPrefix.value !== prefix.value) void load(parentPrefix.value) }

function selectEntry(e: ZipItem) {
  selected.value = e
  if (e.isDir) return
}

async function openEntry(e: ZipItem) {
  if (e.isDir) { enterDir(e); return }
  if (kindOf(e) === 'image') { selected.value = e; return }
  await extractAndOpen(e, false)
}

async function extractAndOpen(e: ZipItem, external: boolean) {
  if (extracting.value) return
  extracting.value = true
  try {
    const res = await api.zipExtract(zip.value, e.path)
    if (!res.ok || !res.path) throw new Error(res.error || '解压失败')
    if (external) await api.fsOpen(res.path)
    else host.openTarget(res.path)
    toast(external ? '已用系统软件打开' : '已提取并打开')
  } catch (err) { toastError(err) } finally { extracting.value = false }
}

function entryIcon(e: ZipItem) {
  if (e.isDir) return Folder
  const ext = (e.name.split('.').pop() || '').toLowerCase()
  if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'avif', 'svg'].includes(ext)) return ImageIcon
  if (['mp3', 'flac', 'wav', 'm4a', 'ogg', 'opus', 'aac', 'wma'].includes(ext)) return Music
  if (['mp4', 'mkv', 'webm', 'avi', 'mov', 'wmv', 'm4v', 'ts', 'flv'].includes(ext)) return Film
  if (['epub', 'pdf', 'mobi', 'azw3', 'txt', 'md'].includes(ext)) return BookOpen
  if (['zip', 'cbz'].includes(ext)) return FileArchive
  return File
}

const kindLabels: Record<string, string> = {
  image: '图片', audio: '音乐', video: '视频', book: '图书', zip: '压缩包', other: '',
}

function kindOf(e: ZipItem): 'image' | 'audio' | 'video' | 'book' | 'zip' | 'other' {
  if (e.isDir) return 'other'
  const ext = (e.name.split('.').pop() || '').toLowerCase()
  if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'avif', 'svg'].includes(ext)) return 'image'
  if (['mp3', 'flac', 'wav', 'm4a', 'ogg', 'opus', 'aac', 'wma'].includes(ext)) return 'audio'
  if (['mp4', 'mkv', 'webm', 'avi', 'mov', 'wmv', 'm4v', 'ts', 'flv'].includes(ext)) return 'video'
  if (['epub', 'pdf', 'mobi', 'azw3', 'txt', 'md', 'markdown'].includes(ext)) return 'book'
  if (['zip', 'cbz'].includes(ext)) return 'zip'
  return 'other'
}

async function copyEntry() {
  if (!selected.value) return
  try { await navigator.clipboard.writeText(selected.value.path); toast('条目路径已复制') } catch { }
}

function onKey(ev: KeyboardEvent) {
  if (ev.key === 'Escape') host.closeWindow()
  else if (ev.key === 'Backspace') { ev.preventDefault(); goUp() }
}

onMounted(async () => {
  window.addEventListener('keydown', onKey)
  try {
    const last = await api.zipLast(zip.value)
    if (last.entry) prefix.value = last.entry
  } catch { }
  await load(prefix.value)
  window.addEventListener('resize', measure)
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKey)
  window.removeEventListener('resize', measure)
})
</script>

<template>
  <div class="viewer zip-viewer">
    <header class="viewer-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="17" /></button>
        <div class="viewer-name">
          <strong :title="zip">{{ basename(zip) }}</strong>
          <span>{{ entries.length }} 项<template v-if="totalSize"> · {{ formatBytes(totalSize) }}</template></span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="上一级" :disabled="!parentPrefix" @click="goUp"><arrow-up :size="16" /></button>
        <button class="viewer-btn" title="刷新" @click="load()"><refresh-cw :size="16" /></button>
        <button class="viewer-btn" title="在文件夹中显示" @click="host.reveal(zip)"><folder-open :size="16" /></button>
        <button class="viewer-btn" title="打开 zip" @click="api.fsOpen(zip)">用系统打开</button>
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="16" /></button>
      </div>
    </header>

    <div class="zip-toolbar">
      <div class="zip-crumbs">
        <button class="crumb" @click="load('')">{{ basename(zip) }}</button>
        <template v-for="c in crumbs" :key="c.prefix">
          <chevron-right :size="12" class="crumb-sep" />
          <button class="crumb" @click="load(c.prefix)">{{ c.name }}</button>
        </template>
      </div>
      <div class="zip-search">
        <n-input v-model:value="q" size="small" placeholder="筛选当前目录…" clearable>
          <template #prefix><search :size="14" /></template>
        </n-input>
      </div>
    </div>

    <div class="zip-layout">
      <div ref="scroller" class="zip-scroll" @scroll.passive="onScroll">
        <div v-if="loading" class="recent-loading"><loader2 :size="18" class="spin" /> 正在读取压缩包目录…</div>
        <div v-else-if="error" class="viewer-error">{{ error }}</div>
        <div v-else-if="!filtered.length" class="empty-state inline">
          <div class="empty-icon"><file-archive :size="42" :stroke-width="1.2" /></div>
          <h3>{{ q ? '没有匹配的条目' : '这个目录是空的' }}</h3>
        </div>
        <div v-else class="zip-canvas" :style="{ height: filtered.length * ROW + 'px' }">
          <button
            v-for="i in visibleRows"
            :key="filtered[i].path + i"
            class="zip-row"
            :class="{ active: selected?.path === filtered[i].path, dir: filtered[i].isDir }"
            :style="{ transform: `translateY(${i * ROW}px)` }"
            @click="selectEntry(filtered[i])"
            @dblclick="openEntry(filtered[i])"
          >
            <component :is="entryIcon(filtered[i])" :size="16" />
            <span class="zip-name">{{ filtered[i].name }}</span>
            <span class="zip-kind">{{ filtered[i].isDir ? '文件夹' : kindLabels[kindOf(filtered[i])] }}</span>
            <span class="zip-size">{{ filtered[i].isDir ? '' : formatBytes(filtered[i].size) }}</span>
            <span v-if="filtered[i].isDir" class="zip-arrow"><chevron-right :size="13" /></span>
          </button>
        </div>
      </div>

      <aside class="zip-preview" v-if="selected">
        <div class="preview-head">
          <strong :title="selected.name">{{ selected.name }}</strong>
          <button class="icon-btn subtle" @click="selected = null"><x :size="14" /></button>
        </div>
        <div class="zip-preview-body">
          <div v-if="kindOf(selected) === 'image'" class="zip-image">
            <img :src="urls.zipEntry(zip, selected.path)" alt="" />
          </div>
          <div v-else class="preview-icon">
            <component :is="entryIcon(selected)" :size="64" :stroke-width="1" />
            <span>{{ selected.isDir ? '文件夹' : kindLabels[kindOf(selected)] }}</span>
          </div>
          <div class="preview-meta">
            <div><span>条目</span><b class="mono">{{ selected.path }}</b></div>
            <div><span>大小</span><b>{{ selected.isDir ? '-' : formatBytes(selected.size) }}</b></div>
            <div><span>压缩后</span><b>{{ selected.isDir ? '-' : formatBytes(selected.compressedSize) }}</b></div>
          </div>
          <div class="preview-actions">
            <n-button v-if="selected.isDir" type="primary" size="small" block @click="enterDir(selected)">打开文件夹</n-button>
            <template v-else>
              <n-button type="primary" size="small" block :loading="extracting" @click="extractAndOpen(selected, false)">提取并用本程序打开</n-button>
              <n-button size="small" block :loading="extracting" @click="extractAndOpen(selected, true)">提取并交给系统</n-button>
              <n-button size="small" block @click="copyEntry">复制条目路径</n-button>
            </template>
          </div>
        </div>
      </aside>
    </div>
  </div>
</template>
