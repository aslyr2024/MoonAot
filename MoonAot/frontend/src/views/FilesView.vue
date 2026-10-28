<script setup lang="ts">
defineOptions({ name: 'FilesView' })

import { computed, onMounted, reactive, ref, watch } from 'vue'
import {
  ArrowUp, BookOpen, ChevronRight, Copy, File, FileArchive, Film, Folder, FolderOpen,
  HardDrive, Image as ImageIcon, ListMusic, Loader2, Music, Plus, RefreshCw, Search, Star, Trash2,
} from 'lucide-vue-next'
import { NButton, NInput } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import ContextMenu from '../components/ContextMenu.vue'
import type { MenuItem } from '../components/ContextMenu.vue'
import { api, urls } from '../api'
import { state } from '../store'
import { host } from '../host'
import { confirmDialog, toast, toastError } from '../ui'
import type {
  FavoriteFolder, FsEntry, FsList, FsRoots, MediaProgressItem, PlaylistSummary,
} from '../types'
import {
  basename, dirname, fileKindLabel, formatBytes, formatDate,
} from '../utils'

const roots = ref<FsRoots | null>(null)
const path = ref('')
const list = ref<FsList | null>(null)
const loading = ref(false)
const kind = ref<'all' | 'image' | 'audio' | 'video' | 'books' | 'zip'>('all')
const q = ref('')
const visibleCount = ref(300)
const selected = ref<FsEntry | null>(null)
const audioMeta = ref<any>(null)
const progressMap = ref<Record<string, MediaProgressItem>>({})
const playlists = ref<PlaylistSummary[]>([])
const favorites = ref<FavoriteFolder[]>([])
const menu = reactive({ show: false, x: 0, y: 0, item: null as FsEntry | null })
const pathInput = ref('')

const kindTabs = [
  { key: 'all', label: '全部' },
  { key: 'image', label: '图片' },
  { key: 'audio', label: '音乐' },
  { key: 'video', label: '视频' },
  { key: 'books', label: '图书' },
  { key: 'zip', label: '压缩包' },
]

const entries = computed(() => list.value?.entries ?? [])
const filtered = computed(() => {
  const needle = q.value.trim().toLowerCase()
  if (!needle) return entries.value
  return entries.value.filter(e => e.name.toLowerCase().includes(needle))
})
const visible = computed(() => filtered.value.slice(0, visibleCount.value))
const hasMore = computed(() => filtered.value.length > visibleCount.value)
const crumbs = computed(() => {
  const p = (path.value || '').replace(/\//g, '\\')
  if (!p) return [] as { name: string; path: string }[]
  const parts = p.split('\\').filter(Boolean)
  const out: { name: string; path: string }[] = []
  let acc = ''
  for (let i = 0; i < parts.length; i++) {
    if (i === 0) { acc = parts[0] + '\\' }
    else { acc = acc.endsWith('\\') ? acc + parts[i] : acc + '\\' + parts[i] }
    out.push({ name: parts[i], path: acc })
  }
  return out
})
const parentPath = computed(() => {
  if (list.value?.parent) return list.value.parent
  const p = path.value
  if (!p) return ''
  const d = dirname(p)
  return d === p ? '' : d
})
const subtitle = computed(() => `${entries.value.length} 项 · ${path.value || '此电脑'}`)
const currentIsFavorite = computed(() => isFavoritePath(path.value))

function normalizePath(p: string) {
  return (p || '').replace(/[\\/]+$/, '').toLowerCase()
}

function isFavoritePath(p: string) {
  const key = normalizePath(p)
  return !!key && favorites.value.some(f => normalizePath(f.path) === key)
}

watch(kind, () => { if (path.value) void navigate(path.value, false) })
watch(q, () => { visibleCount.value = 300 })
watch(path, () => { pathInput.value = path.value })

async function loadRoots() {
  try { roots.value = await api.fsRoots() } catch { }
}

async function loadPlaylists() {
  try { playlists.value = (await api.playlists()).items } catch { }
}

async function loadFavorites() {
  try { favorites.value = await api.favorites() } catch { }
}

async function toggleFavoritePath(target: string, name?: string) {
  if (!target) return
  try {
    if (isFavoritePath(target)) {
      await api.removeFavorite(target)
      toast('已取消收藏')
    } else {
      await api.addFavorite(target, name || basename(target))
      toast('已收藏文件夹')
    }
    await loadFavorites()
  } catch (e) { toastError(e) }
}

function toggleFavoriteCurrent() {
  if (!path.value) return
  void toggleFavoritePath(path.value, basename(path.value))
}

function toggleFolderFavorite(e: FsEntry) {
  if (!e.isDir) { toast('目前收藏夹只保存文件夹', 'info'); return }
  void toggleFavoritePath(e.path, e.name)
}

function removeFavorite(f: FavoriteFolder) {
  void toggleFavoritePath(f.path)
}

function openImageBrowser() {
  host.openViewer('image', '')
}

function openMusicPlayer() {
  host.openViewer('music', '')
}

function openVideoPlayer() {
  host.openViewer('video', '')
}

async function loadProgress() {
  try {
    const arr = await api.mediaProgressAll()
    const map: Record<string, MediaProgressItem> = {}
    for (const p of arr) map[p.path.toLowerCase()] = p
    progressMap.value = map
  } catch { }
}

async function navigate(p: string, remember = true) {
  if (!p) return
  loading.value = true
  try {
    const res = await api.fsList(p, kind.value === 'all' ? 'all' : kind.value, 8000)
    list.value = res
    path.value = res.path
    selected.value = null
    audioMeta.value = null
    visibleCount.value = 300
    if (remember) void api.fsRemember(res.path)
  } catch (e) { toastError(e) } finally { loading.value = false }
}

function goUp() {
  if (parentPath.value) void navigate(parentPath.value)
}

function goHome() {
  const p = roots.value?.drives[0]?.path || roots.value?.roots[0]?.path || ''
  if (p) void navigate(p)
}

function onPathEnter() {
  const p = pathInput.value.trim()
  if (p) void navigate(p)
}

function selectEntry(e: FsEntry) {
  selected.value = e
  audioMeta.value = null
  if (e.kind === 'audio') {
    api.audioMeta(e.path).then(m => { audioMeta.value = m }).catch(() => { })
  }
}

function openEntry(e: FsEntry, force = false) {
  if (e.isDir) { void navigate(e.path); return }
  if (e.kind === 'image') host.openViewer('image', e.path)
  else if (e.kind === 'audio') host.openViewer('music', e.path)
  else if (e.kind === 'video') host.openViewer('video', e.path)
  else if (e.kind === 'zip') host.openViewer('zip', e.path)
  else if (e.kind !== 'other') host.openTarget(e.path)
  else void api.fsOpen(e.path)
}

function openMenu(e: FsEntry, ev: MouseEvent) {
  menu.item = e
  menu.x = ev?.clientX ?? 0
  menu.y = ev?.clientY ?? 0
  menu.show = true
}

const menuItems = computed<MenuItem[]>(() => {
  const e = menu.item
  if (!e) return []
  const items: MenuItem[] = [
    { key: 'open', label: e.isDir ? '打开文件夹' : '打开', icon: FolderOpen },
    { key: 'system', label: '用系统软件打开', icon: File },
    { key: 'reveal', label: '在文件夹中显示', icon: Folder },
    { key: 'copy', label: '复制路径', icon: Copy },
  ]
  if (e.kind === 'book') items.push({ key: 'import', label: '导入并阅读', icon: BookOpen })
  if (e.kind === 'image' || e.kind === 'audio' || e.kind === 'video' || e.kind === 'zip')
    items.push({ key: 'viewer', label: '用内置播放器打开', icon: Film })
  if (e.kind === 'code' || e.kind === 'doc' || e.kind === 'sheet' || e.kind === 'slide' ||
      e.kind === 'model' || e.kind === 'cad' || e.kind === 'zim')
    items.push({ key: 'viewer', label: '用内置查看器打开', icon: Film })
  if (e.isDir)
    items.push(
      {
        key: 'fav',
        label: isFavoritePath(e.path) ? '取消收藏文件夹' : '收藏文件夹',
        icon: Star,
      },
      { key: 'viewImage', label: '用图片浏览器打开', icon: ImageIcon },
      { key: 'viewMusic', label: '用音乐播放器打开', icon: Music },
      { key: 'viewVideo', label: '用视频播放器打开', icon: Film },
    )
  return items
})

async function onMenuSelect(key: string) {
  const e = menu.item
  if (!e) return
  try {
    if (key === 'open') openEntry(e)
    else if (key === 'system') await api.fsOpen(e.path)
    else if (key === 'reveal') await api.fsReveal(e.path)
    else if (key === 'copy') { await navigator.clipboard.writeText(e.path); toast('路径已复制') }
    else if (key === 'import') host.openTarget(e.path)
    else if (key === 'viewer') openEntry(e)
    else if (key === 'fav') toggleFolderFavorite(e)
    else if (key === 'viewImage') host.openViewer('image', '', { folder: e.path })
    else if (key === 'viewMusic') host.openViewer('music', '', { folder: e.path })
    else if (key === 'viewVideo') host.openViewer('video', '', { folder: e.path })
  } catch (err) { toastError(err) }
}

async function chooseFolder() {
  const folder = await host.pickFolder('选择要浏览的文件夹')
  if (folder) void navigate(folder)
}

async function createPlaylist() {
  const audio = entries.value.filter(e => !e.isDir && e.kind === 'audio')
  if (!audio.length) { toast('当前文件夹没有音乐文件', 'warning'); return }
  try {
    const rec = await api.createPlaylist({
      name: basename(path.value) || '新建播放列表',
      kind: 'audio',
      items: audio.map(a => a.path),
      current: 0,
    })
    await loadPlaylists()
    host.openViewer('music', audio[0].path, { playlist: rec.id })
    toast(`已创建播放列表（${audio.length} 首）`)
  } catch (e) { toastError(e) }
}

async function openPlaylist(p: PlaylistSummary) {
  try {
    const rec = await api.playlist(p.id)
    if (!rec.items.length) { toast('播放列表为空', 'warning'); return }
    const first = rec.items[Math.min(rec.current, rec.items.length - 1)] || rec.items[0]
    host.openViewer('music', first, { playlist: rec.id })
  } catch (e) { toastError(e) }
}

async function removePlaylist(p: PlaylistSummary) {
  const ok = await confirmDialog({ title: '删除播放列表', content: `确定删除「${p.name}」吗？`, positiveText: '删除', danger: true })
  if (!ok) return
  try { await api.deletePlaylist(p.id); await loadPlaylists(); toast('已删除') } catch (e) { toastError(e) }
}

function entryIcon(e: FsEntry) {
  if (e.isDir) return Folder
  switch (e.kind) {
    case 'image': return ImageIcon
    case 'audio': return Music
    case 'video': return Film
    case 'book': return BookOpen
    case 'zip': return FileArchive
    default: return File
  }
}

function progressOf(e: FsEntry): MediaProgressItem | undefined {
  return progressMap.value[e.path.toLowerCase()]
}

function progressPct(e: FsEntry) {
  const p = progressOf(e)
  if (!p || !p.duration) return 0
  return Math.min(100, Math.round((p.position / p.duration) * 100))
}

function fmtName(e: FsEntry) {
  return e.name
}

function fileSub(e: FsEntry) {
  if (e.isDir) return '文件夹'
  return `${fileKindLabel(e.kind)} · ${formatBytes(e.size)}`
}

onMounted(async () => {
  await Promise.all([loadRoots(), loadPlaylists(), loadProgress(), loadFavorites()])
  const start = state.settings.lastFolder || roots.value?.roots.find(r => r.name === '图片')?.path ||
    roots.value?.roots[0]?.path || roots.value?.drives[0]?.path || ''
  if (start) void navigate(start, false)
})
</script>

<template>
  <div class="page files-page">
    <PageHeader title="媒体库" :subtitle="subtitle">
      <div class="head-search">
        <n-input v-model:value="pathInput" placeholder="输入文件夹路径后回车" @keyup.enter="onPathEnter">
          <template #prefix><search :size="15" /></template>
        </n-input>
      </div>
      <n-button @click="chooseFolder"><template #icon><folder-open :size="15" /></template>打开文件夹</n-button>
      <n-button :type="currentIsFavorite ? 'primary' : 'default'" :disabled="!path" @click="toggleFavoriteCurrent">
        <template #icon><star :size="15" /></template>{{ currentIsFavorite ? '已收藏' : '收藏' }}
      </n-button>
      <n-button :disabled="!parentPath" @click="goUp"><template #icon><arrow-up :size="15" /></template>上级</n-button>
      <n-button :loading="loading" @click="navigate(path)"><template #icon><refresh-cw :size="15" /></template></n-button>
    </PageHeader>

    <div class="files-layout">
      <aside class="files-side panel">
        <div class="files-side-title">快速访问</div>
        <button v-for="r in roots?.roots ?? []" :key="r.path" class="side-link" :class="{ active: path === r.path }" @click="navigate(r.path)" :title="r.path">
          <component :is="r.kind === 'image' ? ImageIcon : r.kind === 'audio' ? Music : r.kind === 'video' ? Film : FolderOpen" :size="15" />
          <span>{{ r.name }}</span>
        </button>
        <div class="files-side-title">驱动器</div>
        <button v-for="d in roots?.drives ?? []" :key="d.path" class="side-link" @click="navigate(d.path)" :title="d.path">
          <hard-drive :size="15" /><span>{{ d.name }}</span>
        </button>
        <div v-if="roots?.recent?.length" class="files-side-title">最近</div>
        <button v-for="r in roots?.recent ?? []" :key="r" class="side-link small" @click="navigate(r)" :title="r">
          <folder :size="14" /><span>{{ basename(r) }}</span>
        </button>

        <div class="files-side-title">收藏的文件夹</div>
        <button v-for="f in favorites" :key="f.path" class="side-link favorite" :class="{ active: path === f.path }" @click="navigate(f.path)" :title="f.path">
          <star :size="14" class="side-star" /><span>{{ f.name || basename(f.path) }}</span>
          <span class="side-del" title="取消收藏" @click.stop="removeFavorite(f)"><trash2 :size="12" /></span>
        </button>
        <div v-if="!favorites.length" class="side-empty">在文件夹上点星标即可收藏</div>

        <div class="files-side-title with-action">
          <span>播放列表</span>
          <button class="mini-btn" title="用当前文件夹音乐创建播放列表" @click="createPlaylist"><plus :size="13" /></button>
        </div>
        <button v-for="p in playlists" :key="p.id" class="side-link playlist" @click="openPlaylist(p)" :title="p.name">
          <list-music :size="15" /><span>{{ p.name }}</span><small>{{ p.count }}</small>
          <span class="side-del" title="删除播放列表" @click.stop="removePlaylist(p)"><trash2 :size="12" /></span>
        </button>
        <div v-if="!playlists.length" class="side-empty">暂无播放列表</div>
      </aside>

      <section class="files-main">
        <div class="media-tools">
          <button class="tool-btn" title="在独立窗口打开图片浏览器" @click="openImageBrowser">
            <image-icon :size="17" /><span>图片浏览器</span>
          </button>
          <button class="tool-btn" title="在独立窗口打开音乐播放器" @click="openMusicPlayer">
            <music :size="17" /><span>音乐播放器</span>
          </button>
          <button class="tool-btn" title="在独立窗口打开视频播放器" @click="openVideoPlayer">
            <film :size="17" /><span>视频播放器</span>
          </button>
          <span class="media-tools-hint">独立窗口 · 支持字幕/歌词/幻灯片</span>
        </div>
        <div class="files-toolbar">
          <div class="chips">
            <button v-for="t in kindTabs" :key="t.key" class="chip" :class="{ active: kind === t.key }" @click="kind = t.key as any">{{ t.label }}</button>
          </div>
          <div class="files-search">
            <n-input v-model:value="q" size="small" placeholder="筛选当前文件夹…" clearable>
              <template #prefix><search :size="14" /></template>
            </n-input>
          </div>
        </div>
        <div class="crumbs">
          <button class="crumb" @click="goHome()"><hard-drive :size="13" />此电脑</button>
          <template v-for="c in crumbs" :key="c.path">
            <chevron-right :size="12" class="crumb-sep" />
            <button class="crumb" :title="c.path" @click="navigate(c.path)">{{ c.name }}</button>
          </template>
        </div>

        <div class="files-scroll">
          <div v-if="loading" class="recent-loading"><loader2 :size="18" class="spin" /> 正在读取目录…</div>
          <template v-else>
            <div v-if="visible.length" class="file-grid">
              <button
                v-for="e in visible"
                :key="e.path"
                class="file-card"
                :class="{ selected: selected?.path === e.path, dir: e.isDir }"
                @click="selectEntry(e)"
                @dblclick="openEntry(e)"
                @contextmenu.prevent="openMenu(e, $event)"
              >
                <div class="file-cover" :class="'k-' + e.kind">
                  <template v-if="e.isDir">
                    <folder :size="34" :stroke-width="1.2" />
                  </template>
                  <template v-else-if="e.kind === 'image'">
                    <img :src="urls.thumbPath(e.path, 320, 240)" loading="lazy" decoding="async" alt="" />
                  </template>
                  <template v-else-if="e.kind === 'audio'">
                    <img :src="urls.audioCover(e.path)" loading="lazy" decoding="async" alt="" @error="($event.target as HTMLImageElement).style.display = 'none'" />
                    <music :size="30" class="file-fallback" />
                  </template>
                  <template v-else>
                    <component :is="entryIcon(e)" :size="30" :stroke-width="1.2" />
                  </template>
                  <span v-if="e.kind === 'video' || e.kind === 'audio'" class="file-kind-badge">{{ e.kind === 'video' ? '视频' : '音乐' }}</span>
                  <span v-if="e.isDir && isFavoritePath(e.path)" class="file-fav" title="已收藏"><star :size="11" /></span>
                </div>
                <div class="file-info">
                  <span class="file-name" :title="e.name">{{ e.name }}</span>
                  <span class="file-sub">{{ fileSub(e) }}</span>
                  <div v-if="progressPct(e)" class="file-progress"><i :style="{ width: progressPct(e) + '%' }" /></div>
                </div>
              </button>
            </div>
            <div v-else class="empty-state inline">
              <div class="empty-icon"><folder-open :size="42" :stroke-width="1.2" /></div>
              <h3>{{ q ? '没有匹配的文件' : '这个文件夹是空的' }}</h3>
              <p>{{ q ? '换个关键词试试。' : '可以继续进入子文件夹，或切换到其他类型。' }}</p>
            </div>
            <div v-if="hasMore" class="load-more">
              <n-button @click="visibleCount += 300">显示更多（还有 {{ filtered.length - visibleCount }} 项）</n-button>
            </div>
          </template>
        </div>
      </section>

      <aside class="file-preview panel">
        <div v-if="!selected" class="preview-empty">
          <div class="preview-empty-icon"><folder-open :size="42" :stroke-width="1.15" /></div>
          <h3>文件预览</h3>
          <p>单击文件或文件夹查看信息，双击直接打开。</p>
          <div v-if="path" class="preview-meta preview-folder">
            <div><span>当前文件夹</span><b class="mono" :title="path">{{ path }}</b></div>
            <div><span>项目数量</span><b>{{ entries.length }}</b></div>
          </div>
          <div class="preview-tip">信息栏始终保留，切换文件或文件夹时主体布局不会跳动。</div>
        </div>
        <template v-else>
          <div class="preview-head">
            <strong :title="selected.name">{{ selected.name }}</strong>
            <button class="icon-btn subtle" @click="selected = null">×</button>
          </div>
          <div class="preview-body">
            <div v-if="selected.kind === 'image'" class="preview-image">
              <img :src="urls.image(selected.path)" alt="" />
            </div>
            <div v-else-if="selected.kind === 'audio'" class="preview-audio">
              <div class="audio-art">
                <img :src="urls.audioCover(selected.path)" alt="" @error="($event.target as HTMLImageElement).style.display = 'none'" />
                <music :size="46" />
              </div>
              <strong>{{ audioMeta?.title || basename(selected.path) }}</strong>
              <span>{{ audioMeta?.artist || '未知艺术家' }}</span>
              <span v-if="audioMeta?.album" class="muted">{{ audioMeta.album }}</span>
            </div>
            <div v-else class="preview-icon">
              <component :is="entryIcon(selected)" :size="64" :stroke-width="1" />
              <span>{{ fileKindLabel(selected.kind) }}</span>
            </div>
            <div class="preview-meta">
              <div><span>大小</span><b>{{ selected.isDir ? '-' : formatBytes(selected.size) }}</b></div>
              <div><span>修改时间</span><b>{{ formatDate(selected.modified) }}</b></div>
              <div><span>类型</span><b>{{ selected.isDir ? '文件夹' : fileKindLabel(selected.kind) }}</b></div>
              <div class="path-row"><span>路径</span><b class="mono" :title="selected.path">{{ selected.path }}</b></div>
            </div>
            <div class="preview-actions">
              <n-button type="primary" size="small" block @click="openEntry(selected)">
                {{ selected.isDir ? '打开文件夹' : '打开' }}
              </n-button>
              <n-button size="small" block @click="api.fsOpen(selected.path)">用系统软件打开</n-button>
              <n-button size="small" block @click="api.fsReveal(selected.path)">在文件夹中显示</n-button>
            </div>
          </div>
        </template>
      </aside>
    </div>

    <ContextMenu :show="menu.show" :x="menu.x" :y="menu.y" :items="menuItems" @select="onMenuSelect" @close="menu.show = false" />
  </div>
</template>
