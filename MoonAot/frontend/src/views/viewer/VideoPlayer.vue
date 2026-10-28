<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ChevronLeft, ChevronRight, ExternalLink, FileVideo, FolderOpen, ListVideo, Maximize, RotateCcw, X,
} from 'lucide-vue-next'
import { NButton } from 'naive-ui'
import { api, urls } from '../../api'
import { state } from '../../store'
import { host } from '../../host'
import { toast, toastError } from '../../ui'
import type { ImageItem, PlaylistRecord } from '../../types'
import { basename, clamp, formatBytes, formatClock } from '../../utils'

const route = useRoute()
const router = useRouter()

const path = ref(String(route.query.path || ''))
const tracks = ref<ImageItem[]>([])
const index = ref(0)
const container = ref<HTMLElement | null>(null)
const showList = ref(state.settings.media.videoShowList !== false)
const error = ref('')
const loading = ref(true)
const currentTime = ref(0)
const duration = ref(0)
const playing = ref(false)
const playlistId = ref(String(route.query.playlist || ''))
const playlistName = ref('')
const volume = ref(clamp(Number(state.settings.media.videoVolume ?? 1), 0, 1))

let player: any = null
let pendingResume = 0
let saveAt = 0
let seq = 0

const current = computed<ImageItem | undefined>(() => tracks.value[index.value])
const empty = computed(() => !current.value && !path.value)
const ext = computed(() => (current.value?.path || '').split('.').pop()?.toLowerCase() || '')

function splitName(name: string) {
  return (name || '').replace(/\.[^.]+$/, '')
}

async function loadTracks(target: string) {
  if (!target) {
    tracks.value = []
    index.value = 0
    path.value = ''
    loading.value = false
    return
  }
  try {
    const list = await api.siblings(target, 'video')
    tracks.value = list.length ? list : [{ name: basename(target), path: target, size: 0 }]
    const found = tracks.value.findIndex(t => t.path === target)
    index.value = found >= 0 ? found : 0
  } catch (e) { toastError(e); tracks.value = [{ name: basename(target), path: target, size: 0 }]; index.value = 0 }
  await setup(target)
}

async function saveMedia(patch: Record<string, any>) {
  Object.assign(state.settings.media, patch)
  try { await api.saveAppSettings({ media: { ...state.settings.media, ...patch } }) } catch { }
}

let volumeTimer: ReturnType<typeof setTimeout> | null = null
function persistVolume(v: number) {
  if (volumeTimer) clearTimeout(volumeTimer)
  volumeTimer = setTimeout(() => { void saveMedia({ videoVolume: v }) }, 350)
}

async function ensurePlaylist(name: string, items: string[], current: number): Promise<PlaylistRecord | null> {
  try {
    const all = await api.playlists()
    const existing = all.items.find(p => p.kind === 'video' && p.name === name)
    if (existing) return await api.updatePlaylist(existing.id, { name, kind: 'video', items, current })
    return await api.createPlaylist({ name, kind: 'video', items, current })
  } catch { return null }
}

async function loadQueue(items: ImageItem[], start: number, pl: PlaylistRecord | null, autoplay = true) {
  if (!items.length) return
  tracks.value = items
  index.value = clamp(start, 0, items.length - 1)
  playlistId.value = pl?.id ?? ''
  playlistName.value = pl?.name ?? ''
  path.value = items[index.value].path
  await setup(path.value, autoplay)
}

async function restoreLastPlaylist(): Promise<boolean> {
  try {
    const all = await api.playlists()
    const last = all.items
      .filter(p => p.kind === 'video')
      .sort((a, b) => b.updatedAt - a.updatedAt)[0]
    if (!last) return false
    const rec = await api.playlist(last.id)
    if (!rec.items.length) return false
    const items = rec.items.map(p => ({ name: basename(p), path: p, size: 0 }))
    await loadQueue(items, rec.current, rec, false)
    return true
  } catch { return false }
}

async function openVideoFile() {
  const file = await host.pickFile('video', '选择要播放的视频')
  if (!file) return
  playlistId.value = ''
  playlistName.value = ''
  path.value = file
  await loadTracks(file)
}

async function openVideoFolder() {
  const dir = await host.pickFolder('选择视频文件夹（递归加入播放列表）')
  if (dir) await loadVideoFolder(dir)
}

async function loadVideoFolder(dir: string) {
  if (!dir) return
  try {
    const res = await api.fsScan(dir, 'video', 10_000_000)
    const files = res.entries.filter(e => !e.isDir)
    if (!files.length) { toast('这个文件夹里没有视频', 'warning'); return }
    const name = basename(dir) || dir
    let current = 0
    try {
      const all = await api.playlists()
      const ex = all.items.find(p => p.kind === 'video' && p.name === name)
      if (ex) current = (await api.playlist(ex.id)).current
    } catch { }
    const pl = await ensurePlaylist(name, files.map(f => f.path), current)
    const items: ImageItem[] = files.map(f => ({ name: f.name, path: f.path, size: f.size }))
    if (res.truncated) toast('文件过多，仅加入前 1000 万个', 'warning')
    await loadQueue(items, current, pl, true)
    toast(`已加入播放列表：${name}（${items.length} 个）`)
  } catch (e) { toastError(e) }
}

function destroyPlayer() {
  try { player?.destroy?.() } catch { }
  player = null
  if (container.value) container.value.innerHTML = ''
}

function loadScript(src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const existing = document.querySelector(`script[data-xg="${src}"]`)
    if (existing) { resolve(); return }
    const s = document.createElement('script')
    s.src = src
    s.dataset.xg = src
    s.onload = () => resolve()
    s.onerror = () => reject(new Error('加载失败 ' + src))
    document.head.appendChild(s)
  })
}
function loadCss(href: string) {
  if (document.querySelector(`link[data-xg="${href}"]`)) return
  const l = document.createElement('link')
  l.rel = 'stylesheet'
  l.href = href
  l.dataset.xg = href
  document.head.appendChild(l)
}

async function setup(target: string, autoplay = true) {
  const my = ++seq
  loading.value = true
  error.value = ''
  playing.value = false
  currentTime.value = 0
  duration.value = 0
  destroyPlayer()
  host.setTitle(splitName(basename(target)))
  const q: Record<string, string> = { path: target }
  if (playlistId.value) q.playlist = playlistId.value
  void router.replace({ query: q })

  pendingResume = 0
  try {
    const prog = await api.mediaProgress(target)
    if (prog && prog.position > 5 && state.settings.media.videoAutoResume) pendingResume = prog.position
  } catch { }
  if (my !== seq) return

  await nextTick()
  const el = container.value
  if (!el) return
  const e = (target.split('.').pop() || '').toLowerCase()

  // mp4 / webm / mov 等交给浏览器原生解码：支持 Range 流式播放，控制条稳定
  if (e !== 'm3u8' && e !== 'flv') {
    const video = document.createElement('video')
    video.className = 'native-video'
    video.controls = true
    video.autoplay = autoplay
    video.playsInline = true
    video.preload = 'metadata'
    video.volume = volume.value
    video.src = urls.media(target)
    el.appendChild(video)
    video.addEventListener('volumechange', () => {
      volume.value = video.volume
      persistVolume(video.volume)
    })
    player = {
      destroy() {
        try { video.pause(); video.removeAttribute('src'); video.load(); video.remove() } catch { }
      },
    }
    video.addEventListener('loadedmetadata', () => {
      duration.value = video.duration || 0
      if (pendingResume > 0 && pendingResume < duration.value - 5) {
        video.currentTime = pendingResume
      }
      pendingResume = 0
      loading.value = false
    })
    video.addEventListener('timeupdate', () => {
      currentTime.value = video.currentTime || 0
      duration.value = video.duration || 0
      const now = Date.now()
      if (now - saveAt > 5000) { saveAt = now; saveProgress(false) }
    })
    video.addEventListener('play', () => { playing.value = true })
    video.addEventListener('pause', () => { playing.value = false })
    video.addEventListener('ended', () => {
      if (index.value < tracks.value.length - 1) void goTo(index.value + 1)
      else playing.value = false
    })
    video.addEventListener('error', () => {
      error.value = '当前视频编码无法在 WebView 中播放（MKV/AVI/WMV 等容器受限），请用系统播放器打开。'
      loading.value = false
    })
    setTimeout(() => { loading.value = false }, 1200)
    return
  }

  // HLS / FLV 使用随程序附带的 xgplayer 插件
  try {
    loadCss('/assets/vendor/xgplayer/xgplayer.min.css')
    await loadScript('/assets/vendor/xgplayer/xgplayer.min.js')
    if (e === 'm3u8') await loadScript('/assets/vendor/xgplayer/xgplayer-hls.min.js')
    if (e === 'flv') await loadScript('/assets/vendor/xgplayer/xgplayer-flv.min.js')
    if (my !== seq) return
    const w = window as any
    const Ctor = e === 'm3u8' ? w.HlsPlayer : w.FlvPlayer
    if (!Ctor) throw new Error('播放器内核不可用')
    el.innerHTML = ''
    const holder = document.createElement('div')
    holder.id = 'xg-player-' + my
    holder.style.width = '100%'
    holder.style.height = '100%'
    el.appendChild(holder)
    player = new Ctor({
      id: holder.id, url: urls.media(target), autoplay, playsinline: true,
      fluid: false, width: '100%', height: '100%', lang: 'zh-CN',
      videoAttributes: { preload: 'metadata' },
    })
    try { player.volume = volume.value } catch { }
    player.on('volumechange', () => {
      try { volume.value = player.volume; persistVolume(player.volume) } catch { }
    })
    player.on('timeupdate', () => {
      try {
        currentTime.value = player.currentTime || 0
        duration.value = player.duration || 0
        const now = Date.now()
        if (now - saveAt > 5000) { saveAt = now; saveProgress(false) }
      } catch { }
    })
    player.on('play', () => { playing.value = true })
    player.on('pause', () => { playing.value = false })
    player.on('canplay', () => {
      if (pendingResume > 0) {
        try { player.currentTime = pendingResume } catch { }
        pendingResume = 0
      }
    })
    player.on('ended', () => {
      if (index.value < tracks.value.length - 1) void goTo(index.value + 1)
      else playing.value = false
    })
    player.on('error', () => {
      error.value = '当前流媒体格式无法在 WebView 中播放'
      loading.value = false
    })
    setTimeout(() => { loading.value = false }, 900)
  } catch (err) {
    loading.value = false
    error.value = err instanceof Error ? err.message : String(err)
  }
}

function saveProgress(keepalive: boolean) {
  const t = current.value
  if (!t) return
  const item = {
    path: t.path, kind: 'video', position: currentTime.value,
    duration: duration.value, title: splitName(t.name),
  }
  if (keepalive) void api.saveMediaProgressKeepalive(item)
  else void api.saveMediaProgress(item).catch(() => { })
}

async function goTo(i: number) {
  if (i < 0 || i >= tracks.value.length) return
  saveProgress(false)
  index.value = i
  const t = tracks.value[i]
  path.value = t.path
  await setup(t.path)
  if (playlistId.value) {
    void api.updatePlaylist(playlistId.value, {
      name: playlistName.value || '视频播放列表',
      kind: 'video',
      items: tracks.value.map(x => x.path),
      current: i,
    }).catch(() => { })
  }
}

function next() { if (index.value < tracks.value.length - 1) void goTo(index.value + 1) }
function prev() { if (index.value > 0) void goTo(index.value - 1) }

function retry() { void setup(path.value) }

function onKey(ev: KeyboardEvent) {
  const tag = (document.activeElement?.tagName || '').toLowerCase()
  if (tag === 'input' || tag === 'textarea') return
  if (ev.key.toLowerCase() === 'n') next()
  else if (ev.key.toLowerCase() === 'p') prev()
}

watch(showList, (v) => { void saveMedia({ videoShowList: v }) })
watch(() => route.query.folder, (v) => {
  const dir = String(v || '')
  if (dir) void loadVideoFolder(dir)
})

onMounted(async () => {
  window.addEventListener('keydown', onKey)
  window.addEventListener('beforeunload', () => saveProgress(true))
  window.addEventListener('pagehide', () => saveProgress(true))
  const folderParam = String(route.query.folder || '')
  if (path.value) {
    if (route.query.playlist) {
      try {
        const rec = await api.playlist(String(route.query.playlist))
        if (rec.items.length) {
          const items = rec.items.map(p => ({ name: basename(p), path: p, size: 0 }))
          await loadQueue(items, rec.current, rec, false)
        } else await loadTracks(path.value)
      } catch { await loadTracks(path.value) }
    } else await loadTracks(path.value)
  } else if (folderParam) {
    await loadVideoFolder(folderParam)
  } else if (!(await restoreLastPlaylist())) {
    loading.value = false
  }
})

onBeforeUnmount(() => {
  saveProgress(true)
  seq++
  destroyPlayer()
  window.removeEventListener('keydown', onKey)
})
</script>

<template>
  <div class="viewer video-viewer">
    <header class="viewer-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="17" /></button>
        <div class="viewer-name">
          <strong :title="current?.path">{{ empty ? '视频播放器' : (splitName(basename(current?.path)) || '视频') }}</strong>
          <span>{{ empty ? '未选择视频' : `${index + 1} / ${tracks.length}` }}<template v-if="!empty && current"> · {{ formatBytes(current.size) }}</template></span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="打开视频文件" @click="openVideoFile"><file-video :size="16" /></button>
        <button class="viewer-btn" title="打开视频文件夹" @click="openVideoFolder"><folder-open :size="16" /></button>
        <template v-if="!empty">
          <span class="bar-sep" />
          <button class="viewer-btn" title="上一个" :disabled="index <= 0" @click="prev"><chevron-left :size="18" /></button>
          <button class="viewer-btn" title="下一个" :disabled="index >= tracks.length - 1" @click="next"><chevron-right :size="18" /></button>
          <button class="viewer-btn" title="重试" @click="retry"><rotate-ccw :size="16" /></button>
          <button class="viewer-btn" title="在文件夹中显示" @click="host.reveal(path)"><external-link :size="16" /></button>
          <button class="viewer-btn" :class="{ active: showList }" title="播放列表" @click="showList = !showList"><list-video :size="16" /></button>
        </template>
        <span class="bar-sep" />
        <button class="viewer-btn" title="全屏" @click="host.toggleFullscreen()"><maximize :size="16" /></button>
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="16" /></button>
      </div>
    </header>

    <div class="video-layout" :class="{ 'no-list': !showList }">
      <div class="video-stage">
        <div v-if="empty" class="viewer-empty video-empty">
          <div class="ve-icon"><file-video :size="58" :stroke-width="1.05" /></div>
          <h2>视频播放器</h2>
          <p>打开一个视频文件，或选择文件夹开始连续播放。支持 MP4/WebM/MOV，以及 HLS/FLV 流。</p>
          <div class="ve-actions">
            <n-button type="primary" @click="openVideoFile">
              <template #icon><file-video :size="15" /></template>打开视频文件
            </n-button>
            <n-button @click="openVideoFolder">
              <template #icon><folder-open :size="15" /></template>打开文件夹
            </n-button>
          </div>
        </div>
        <template v-else>
          <div ref="container" class="video-container" />
          <div v-if="loading" class="viewer-loading">正在加载视频…</div>
          <div v-if="error" class="video-error">
            <p>{{ error }}</p>
            <div class="error-actions">
              <n-button size="small" @click="api.fsOpen(path)">用系统播放器打开</n-button>
              <n-button size="small" @click="retry">重试</n-button>
            </div>
          </div>
        </template>
      </div>
      <aside v-if="showList && !empty" class="video-list">
        <div class="video-list-head">播放列表 · {{ tracks.length }}</div>
        <div class="video-list-scroll">
          <button
            v-for="(t, i) in tracks"
            :key="t.path + i"
            class="video-item"
            :class="{ active: i === index }"
            @click="goTo(i)"
          >
            <span class="v-no">{{ i + 1 }}</span>
            <span class="v-title" :title="t.name">{{ splitName(t.name) }}</span>
            <span class="v-size">{{ t.size ? formatBytes(t.size) : '' }}</span>
          </button>
        </div>
      </aside>
    </div>

    <footer v-if="!empty" class="video-foot">
      <span>{{ formatClock(currentTime) }} / {{ formatClock(duration) }}</span>
      <span>{{ playing ? '正在播放' : '已暂停' }}</span>
    </footer>
  </div>
</template>
