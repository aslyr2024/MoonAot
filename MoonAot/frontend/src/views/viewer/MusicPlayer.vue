<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ExternalLink, FileMusic, FolderOpen, ImagePlus, ListMusic, Maximize, Music, Pause as PauseIcon, Play as PlayIcon, Plus,
  Repeat, Repeat1, Shuffle, SkipBack, SkipForward, Volume2, VolumeX, X,
} from 'lucide-vue-next'
import { NButton, NInput, NModal, NSelect, NSlider, NSwitch } from 'naive-ui'
import { api, urls } from '../../api'
import { state } from '../../store'
import { host } from '../../host'
import { toast, toastError } from '../../ui'
import type { AudioMeta, ImageItem, Lyrics, PlaylistRecord, PlaylistSummary } from '../../types'
import { basename, clamp, dirname, formatBytes, formatClock } from '../../utils'

const route = useRoute()
const router = useRouter()

const path = ref(String(route.query.path || ''))
const playlistId = ref(String(route.query.playlist || ''))
const tracks = ref<ImageItem[]>([])
const index = ref(0)
const audio = ref<HTMLAudioElement | null>(null)
const playing = ref(false)
const currentTime = ref(0)
const duration = ref(0)
const volume = ref(clamp(Number(state.settings.media.musicVolume ?? 0.9), 0, 1))
const muted = ref(false)
const mode = ref<'order' | 'repeat' | 'shuffle'>(
  state.settings.media.musicMode === 'repeat' || state.settings.media.musicMode === 'shuffle'
    ? state.settings.media.musicMode : 'order')
const meta = ref<AudioMeta | null>(null)
const lyrics = ref<Lyrics | null>(null)
const activeLine = ref(-1)
const tab = ref<'list' | 'lyrics'>(state.settings.media.musicTab === 'lyrics' ? 'lyrics' : 'list')
const playlistName = ref('')
const savedPlaylists = ref<PlaylistSummary[]>([])
const saveModal = ref(false)
const saveName = ref('')
const saving = ref(false)
const coverFailed = ref(false)
const codecError = ref(false)
const bgFolder = ref(state.settings.media.musicBackgroundFolder || '')
const bgUrls = ref<string[]>([])
const bgIndex = ref(state.settings.media.musicBackgroundIndex || 0)
const bgUrl = ref('')
const bgNext = ref('')
const bgFading = ref(false)
const bgRandom = ref(!!state.settings.media.musicBackgroundRandom)
const bgFit = ref<'cover' | 'contain' | 'stretch' | 'center'>(
  state.settings.media.musicBackgroundFit === 'contain' || state.settings.media.musicBackgroundFit === 'stretch' || state.settings.media.musicBackgroundFit === 'center'
    ? state.settings.media.musicBackgroundFit : 'cover')
const bgBlur = ref(clamp(Number(state.settings.media.musicBackgroundBlur ?? 5), 0, 10))
const showBgSettings = ref(false)

let progressSaveAt = 0
let bgTimer: ReturnType<typeof setInterval> | null = null
let bgFadeTimer: ReturnType<typeof setTimeout> | null = null
let bgSaveTimer: ReturnType<typeof setTimeout> | null = null
let loadingSeq = 0

const mediaSettings = computed(() => state.settings.media)
const current = computed<ImageItem | undefined>(() => tracks.value[index.value])
const empty = computed(() => !current.value && !path.value)
const coverUrl = computed(() => current.value ? urls.audioCover(current.value.path) : '')
const bgBlurPx = computed(() => Math.round(bgBlur.value * 3))
function bgLayerStyle(url: string) {
  if (!url) return {}
  const size = bgFit.value === 'contain' ? 'contain' : bgFit.value === 'stretch' ? '100% 100%' : bgFit.value === 'center' ? 'auto' : 'cover'
  return {
    backgroundImage: `url(${url})`,
    backgroundSize: size,
    backgroundRepeat: 'no-repeat',
    backgroundPosition: 'center',
    filter: bgBlurPx.value > 0 ? `blur(${bgBlurPx.value}px) saturate(1.08)` : 'none',
    transform: bgBlurPx.value > 0 ? 'scale(1.02)' : 'none',
  }
}
const title = computed(() => meta.value?.title || basename(current.value?.path) || '音乐')
const artist = computed(() => meta.value?.artist || splitName(basename(current.value?.path)).artist || '未知艺术家')
const modeIcon = computed(() => mode.value === 'shuffle' ? Shuffle : mode.value === 'repeat' ? Repeat1 : Repeat)
const modeLabel = computed(() => mode.value === 'shuffle' ? '随机播放' : mode.value === 'repeat' ? '单曲循环' : '顺序播放')

function splitName(name: string): { artist: string; title: string } {
  const s = (name || '').replace(/\.[^.]+$/, '')
  for (const sep of [' - ', ' – ', ' — ']) {
    const i = s.indexOf(sep)
    if (i > 0) return { artist: s.slice(0, i).trim(), title: s.slice(i + sep.length).trim() }
  }
  return { artist: '', title: s }
}

function displayTitle(t: ImageItem) {
  if (t.path === current.value?.path && meta.value?.title) return meta.value.title
  return splitName(t.name).title || t.name
}
function displayArtist(t: ImageItem) {
  if (t.path === current.value?.path && meta.value?.artist) return meta.value.artist
  return splitName(t.name).artist || '未知艺术家'
}

async function loadTracks(target: string, usePlaylist = true) {
  if (!target) {
    tracks.value = []
    index.value = 0
    path.value = ''
    playlistId.value = ''
    playlistName.value = ''
    return
  }
  playlistId.value = usePlaylist ? String(route.query.playlist || '') : ''
  if (playlistId.value) {
    try {
      const rec = await api.playlist(playlistId.value)
      playlistName.value = rec.name
      tracks.value = rec.items.map(p => ({ name: basename(p), path: p, size: 0 }))
      index.value = Math.max(0, Math.min(rec.current, tracks.value.length - 1))
      const found = tracks.value.findIndex(t => t.path === target)
      if (found >= 0) index.value = found
      if (tracks.value.length) { await loadTrack(true); return }
    } catch { /* fall back */ }
  }
  playlistName.value = ''
  const list = await api.siblings(target, 'audio')
  tracks.value = list.length ? list : [{ name: basename(target), path: target, size: 0 }]
  const found = tracks.value.findIndex(t => t.path === target)
  index.value = found >= 0 ? found : 0
  await loadTrack(true)
}

async function loadTrack(autoplay: boolean) {
  const t = current.value
  if (!t) return
  const seq = ++loadingSeq
  // 立即丢弃上一首元数据，否则标题会先显示上一首的名字，
  // 等新歌 audioMeta 返回后才跳回正确标题。
  meta.value = null
  path.value = t.path
  codecError.value = false
  coverFailed.value = false
  audio.value?.pause()
  playing.value = false
  currentTime.value = 0
  duration.value = 0
  lyrycsReset()
  host.setTitle(title.value)
  if (audio.value) {
    audio.value.src = urls.media(t.path)
    audio.value.load()
  }
  const q: Record<string, string> = { path: t.path }
  if (playlistId.value) q.playlist = playlistId.value
  void router.replace({ query: q })

  try {
    const [m, l] = await Promise.all([
      api.audioMeta(t.path).catch(() => null),
      api.lyrics(t.path).catch(() => null),
    ])
    if (seq !== loadingSeq) return
    meta.value = m
    lyrics.value = l
    host.setTitle(m?.title || splitName(t.name).title || t.name)
  } catch { }
  try {
    const prog = await api.mediaProgress(t.path)
    if (seq !== loadingSeq) return
    if (prog && prog.position > 5 && state.settings.media.videoAutoResume) {
      pendingResume = prog.position
    }
  } catch { }
  await nextTick()
  if (autoplay) void play()
  void loadBackground(t.path)
}

function lyrycsReset() {
  lyrics.value = null
  activeLine.value = -1
}

let pendingResume = 0

async function play() {
  if (!audio.value) return
  try {
    await audio.value.play()
    playing.value = true
    if (pendingResume > 0 && audio.value.duration > 0) {
      audio.value.currentTime = pendingResume
      pendingResume = 0
    }
  } catch {
    playing.value = false
  }
}
function pause() { audio.value?.pause(); playing.value = false }
function togglePlay() { if (audio.value?.paused) void play(); else pause() }

function onLoaded() {
  if (!audio.value) return
  duration.value = audio.value.duration || 0
  audio.value.volume = volume.value
  audio.value.muted = volume.value === 0
  if (pendingResume > 0 && pendingResume < duration.value - 5) {
    audio.value.currentTime = pendingResume
    pendingResume = 0
  }
}

function onTime() {
  if (!audio.value) return
  currentTime.value = audio.value.currentTime
  updateActiveLine()
  const now = Date.now()
  if (now - progressSaveAt > 5000) {
    progressSaveAt = now
    saveProgress(false)
  }
}

function onEnded() {
  if (mode.value === 'repeat') {
    if (audio.value) { audio.value.currentTime = 0; void play() }
    return
  }
  if (mode.value === 'shuffle') {
    if (tracks.value.length > 1) {
      let nextIdx = index.value
      while (nextIdx === index.value) nextIdx = Math.floor(Math.random() * tracks.value.length)
      void goTo(nextIdx)
    }
    return
  }
  if (index.value < tracks.value.length - 1) void goTo(index.value + 1)
}

function onAudioError() {
  codecError.value = true
  playing.value = false
}

function seek(v: number) {
  if (!audio.value) return
  audio.value.currentTime = Math.max(0, Math.min(v, duration.value || 0))
  currentTime.value = audio.value.currentTime
}

function setVolume(v: number) {
  volume.value = v
  if (audio.value) { audio.value.volume = v; audio.value.muted = v === 0 }
  muted.value = v === 0
  persistVolume(v)
}

function toggleMute() {
  if (!audio.value) return
  audio.value.muted = !audio.value.muted
  muted.value = audio.value.muted
}

async function goTo(i: number, autoplay = true) {
  if (i < 0 || i >= tracks.value.length) return
  saveProgress(false)
  index.value = i
  await loadTrack(autoplay)
  if (playlistId.value) {
    const rec = await api.playlist(playlistId.value).catch(() => null)
    if (rec) {
      rec.current = i
      await api.updatePlaylist(rec.id, { current: i, items: rec.items }).catch(() => { })
    }
  }
}

function next() {
  if (!tracks.value.length) return
  if (index.value < tracks.value.length - 1) void goTo(index.value + 1)
  else if (mode.value === 'repeat') void goTo(index.value)
}
function prev() {
  if (audio.value && audio.value.currentTime > 4) { seek(0); return }
  if (index.value > 0) void goTo(index.value - 1)
}

function cycleMode() {
  mode.value = mode.value === 'order' ? 'repeat' : mode.value === 'repeat' ? 'shuffle' : 'order'
  void saveMedia({ musicMode: mode.value })
}

function setTab(next: 'list' | 'lyrics') {
  tab.value = next
  void saveMedia({ musicTab: next })
}

function updateActiveLine() {
  const lines = lyrics.value?.lines
  if (!lines?.length) { activeLine.value = -1; return }
  let lo = 0, hi = lines.length - 1, ans = -1
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    if (lines[mid].t <= currentTime.value + 0.05) { ans = mid; lo = mid + 1 } else hi = mid - 1
  }
  if (ans !== activeLine.value) activeLine.value = ans
}

const lyricView = computed(() => {
  const lines = lyrics.value?.lines
  if (!lines?.length) return null
  const i = activeLine.value >= 0 ? Math.min(activeLine.value, lines.length - 1) : 0
  return {
    prev: i > 0 ? lines[i - 1] : null,
    current: lines[i],
    next: i + 1 < lines.length ? lines[i + 1] : null,
  }
})

function saveProgress(keepalive: boolean) {
  const t = current.value
  if (!t) return
  const item = {
    path: t.path, kind: 'audio', position: currentTime.value, duration: duration.value,
    title: meta.value?.title ?? undefined, artist: meta.value?.artist ?? undefined,
  }
  if (keepalive) void api.saveMediaProgressKeepalive(item)
  else void api.saveMediaProgress(item).catch(() => { })
}

async function loadBackground(trackPath: string) {
  // 手动选择的背景文件夹与歌曲无关，切歌时不重置轮播
  if (bgFolder.value && bgUrls.value.length) return
  try {
    const dir = bgFolder.value || dirname(trackPath)
    let list: string[] = []
    if (dir) {
      const res = await api.fsScan(dir, 'image', 3000)
      list = res.entries.filter(e => !e.isDir).map(e => urls.image(e.path))
    }
    if (!list.length && trackPath) {
      const imgs = await api.siblings(trackPath, 'image')
      list = imgs.filter(x => x.path !== trackPath).map(x => urls.image(x.path))
    }
    if (!list.length) {
      bgUrls.value = []
      stopBgTimer()
      setBgImmediate(current.value ? urls.audioCover(current.value.path) : '')
      return
    }
    bgUrls.value = list
    bgIndex.value = clamp(state.settings.media.musicBackgroundIndex || 0, 0, list.length - 1)
    setBgImmediate(list[bgIndex.value])
    startBgTimer()
  } catch { }
}

function setBgImmediate(url: string) {
  if (bgFadeTimer) { clearTimeout(bgFadeTimer); bgFadeTimer = null }
  bgFading.value = false
  bgNext.value = ''
  bgUrl.value = url || ''
}

function startBgTimer() {
  if (bgTimer) { clearInterval(bgTimer); bgTimer = null }
  if (bgUrls.value.length <= 1) return
  bgTimer = setInterval(rotateBg, Math.max(0.1, Number(state.settings.media.musicBackgroundSeconds) || 8) * 1000)
}

function stopBgTimer() {
  if (bgTimer) { clearInterval(bgTimer); bgTimer = null }
}

function showBgIndex(next: number) {
  if (bgUrls.value.length <= 1) return
  next = ((next % bgUrls.value.length) + bgUrls.value.length) % bgUrls.value.length
  if (next === bgIndex.value) return
  const url = bgUrls.value[next]
  bgNext.value = url
  bgFading.value = true
  if (bgFadeTimer) clearTimeout(bgFadeTimer)
  bgFadeTimer = setTimeout(() => {
    bgUrl.value = url
    bgNext.value = ''
    bgFading.value = false
    bgFadeTimer = null
    bgIndex.value = next
    if (bgSaveTimer) clearTimeout(bgSaveTimer)
    bgSaveTimer = setTimeout(() => {
      void saveMedia({ musicBackgroundIndex: next, musicBackgroundFolder: bgFolder.value || null })
    }, 300)
  }, 900)
}

function rotateBg() {
  if (bgUrls.value.length <= 1) return
  let next: number
  if (bgRandom.value) {
    do { next = Math.floor(Math.random() * bgUrls.value.length) } while (next === bgIndex.value)
  } else {
    next = (bgIndex.value + 1) % bgUrls.value.length
  }
  showBgIndex(next)
}

function stepBg(dir: number) {
  showBgIndex(bgIndex.value + dir)
}

function setBgSeconds(v: number | null) {
  if (v === null || !Number.isFinite(v)) return
  const seconds = Math.round(clamp(v, 0.1, 60) * 10) / 10
  void saveMedia({ musicBackgroundSeconds: seconds })
  if (bgUrls.value.length > 1) startBgTimer()
}

function setBgFit(v: 'cover' | 'contain' | 'stretch' | 'center') {
  bgFit.value = v
  void saveMedia({ musicBackgroundFit: v })
}

function setBgBlur(v: number | null) {
  if (v === null || !Number.isFinite(v)) return
  bgBlur.value = clamp(Math.round(v), 0, 10)
  void saveMedia({ musicBackgroundBlur: bgBlur.value })
}

function setBgRandom(v: boolean) {
  bgRandom.value = v
  void saveMedia({ musicBackgroundRandom: v })
}

async function pickBackgroundFolder() {
  const dir = await host.pickFolder('选择背景图片文件夹（递归轮播）')
  if (!dir) return
  bgFolder.value = dir
  bgIndex.value = 0
  bgUrls.value = []
  await saveMedia({ musicBackgroundFolder: dir, musicBackgroundIndex: 0 })
  await loadBackground(current.value?.path || '')
  toast(bgUrls.value.length ? `背景轮播已启用：${bgUrls.value.length} 张图片` : '该文件夹内没有图片', bgUrls.value.length ? 'success' : 'warning')
}

function clearBackgroundFolder() {
  bgFolder.value = ''
  bgUrls.value = []
  stopBgTimer()
  void saveMedia({ musicBackgroundFolder: null, musicBackgroundIndex: 0 })
  void loadBackground(current.value?.path || '')
}

async function saveMedia(patch: Record<string, any>) {
  Object.assign(state.settings.media, patch)
  try { await api.saveAppSettings({ media: { ...state.settings.media, ...patch } }) } catch { }
}

let volumeTimer: ReturnType<typeof setTimeout> | null = null
function persistVolume(v: number) {
  if (volumeTimer) clearTimeout(volumeTimer)
  volumeTimer = setTimeout(() => { void saveMedia({ musicVolume: v }) }, 350)
}

async function ensurePlaylist(name: string, items: string[], current: number): Promise<PlaylistRecord | null> {
  try {
    const all = await api.playlists()
    const existing = all.items.find(p => p.kind === 'audio' && p.name === name)
    if (existing) return await api.updatePlaylist(existing.id, { name, kind: 'audio', items, current })
    return await api.createPlaylist({ name, kind: 'audio', items, current })
  } catch { return null }
}

async function loadQueue(items: ImageItem[], start: number, pl: PlaylistRecord | null, autoplay = true) {
  if (!items.length) return
  tracks.value = items
  index.value = clamp(start, 0, items.length - 1)
  playlistId.value = pl?.id ?? ''
  playlistName.value = pl?.name ?? ''
  path.value = items[index.value].path
  await loadTrack(autoplay)
}

async function restoreLastPlaylist(): Promise<boolean> {
  try {
    const all = await api.playlists()
    const last = all.items
      .filter(p => p.kind === 'audio')
      .sort((a, b) => b.updatedAt - a.updatedAt)[0]
    if (!last) return false
    const rec = await api.playlist(last.id)
    if (!rec.items.length) return false
    const items = rec.items.map(p => ({ name: basename(p), path: p, size: 0 }))
    await loadQueue(items, rec.current, rec, false)
    return true
  } catch { return false }
}

async function openMusicFile() {
  const file = await host.pickFile('audio', '选择要播放的音乐')
  if (!file) return
  playlistId.value = ''
  playlistName.value = ''
  path.value = file
  await loadTracks(file, false)
}

async function openMusicFolder() {
  const dir = await host.pickFolder('选择音乐文件夹（递归加入播放列表）')
  if (dir) await loadMusicFolder(dir)
}

async function loadMusicFolder(dir: string) {
  if (!dir) return
  try {
    const res = await api.fsScan(dir, 'audio', 10_000_000)
    const files = res.entries.filter(e => !e.isDir)
    if (!files.length) { toast('这个文件夹里没有音乐', 'warning'); return }
    const name = basename(dir) || dir
    let current = 0
    try {
      const all = await api.playlists()
      const ex = all.items.find(p => p.kind === 'audio' && p.name === name)
      if (ex) current = (await api.playlist(ex.id)).current
    } catch { }
    const pl = await ensurePlaylist(name, files.map(f => f.path), current)
    const items: ImageItem[] = files.map(f => ({ name: f.name, path: f.path, size: f.size }))
    if (res.truncated) toast('文件过多，仅加入前 1000 万首', 'warning')
    await loadQueue(items, current, pl, true)
    toast(`已加入播放列表：${name}（${items.length} 首）`)
  } catch (e) { toastError(e) }
}

async function loadSavedPlaylists() {
  try { savedPlaylists.value = (await api.playlists()).items } catch { }
}

function openSave() {
  saveName.value = playlistName.value || `播放列表 ${new Date().toLocaleDateString()}`
  saveModal.value = true
}

async function doSave() {
  if (!saveName.value.trim()) { toast('请输入名称', 'warning'); return }
  saving.value = true
  try {
    const rec = await api.createPlaylist({
      name: saveName.value.trim(), kind: 'audio',
      items: tracks.value.map(t => t.path), current: index.value,
    })
    playlistId.value = rec.id
    playlistName.value = rec.name
    toast('播放列表已保存')
    saveModal.value = false
    await loadSavedPlaylists()
    void router.replace({ query: { path: path.value, playlist: rec.id } })
  } catch (e) { toastError(e) } finally { saving.value = false }
}

async function loadPlaylist(p: PlaylistSummary) {
  try {
    const rec = await api.playlist(p.id)
    if (!rec.items.length) { toast('播放列表为空', 'warning'); return }
    const items: ImageItem[] = rec.items.map(x => ({ name: basename(x), path: x, size: 0 }))
    // 直接使用刚载入的列表，避免 loadTracks 又从旧路由 query 覆盖回上一个列表
    await loadQueue(items, rec.current, rec, true)
    toast(`已载入播放列表：${rec.name}`)
  } catch (e) { toastError(e) }
}

function removeTrack(i: number) {
  if (i === index.value && playing.value) pause()
  tracks.value.splice(i, 1)
  if (i < index.value) index.value--
  else if (i === index.value) {
    index.value = Math.min(index.value, tracks.value.length - 1)
    if (tracks.value.length) void loadTrack(playing.value)
  }
  persistQueue()
}

function persistQueue() {
  if (!playlistId.value) return
  void api.updatePlaylist(playlistId.value, {
    name: playlistName.value || '音乐播放列表',
    kind: 'audio',
    items: tracks.value.map(t => t.path),
    current: Math.max(0, index.value),
  }).catch(() => { })
}

function onKey(ev: KeyboardEvent) {
  const tag = (document.activeElement?.tagName || '').toLowerCase()
  if (tag === 'input' || tag === 'textarea') return
  if (ev.code === 'Space') { ev.preventDefault(); togglePlay() }
  else if (ev.key === 'ArrowRight') { ev.preventDefault(); seek(currentTime.value + 5) }
  else if (ev.key === 'ArrowLeft') { ev.preventDefault(); seek(currentTime.value - 5) }
  else if (ev.key === 'ArrowUp') { ev.preventDefault(); setVolume(Math.min(1, volume.value + 0.05)) }
  else if (ev.key === 'ArrowDown') { ev.preventDefault(); setVolume(Math.max(0, volume.value - 0.05)) }
  else if (ev.key.toLowerCase() === 'n') next()
  else if (ev.key.toLowerCase() === 'p') prev()
  else if (ev.key.toLowerCase() === 'm') toggleMute()
}

watch(() => route.query.folder, (v) => {
  const dir = String(v || '')
  if (dir) void loadMusicFolder(dir)
})

watch(() => state.settings.media.musicBackgroundSeconds, () => {
  if (bgUrls.value.length > 1) startBgTimer()
})

onMounted(async () => {
  await loadSavedPlaylists()
  if (audio.value) {
    audio.value.volume = volume.value
    audio.value.muted = volume.value === 0
  }
  const folderParam = String(route.query.folder || '')
  if (path.value) await loadTracks(path.value)
  else if (folderParam) await loadMusicFolder(folderParam)
  else await restoreLastPlaylist()
  window.addEventListener('keydown', onKey)
  window.addEventListener('beforeunload', () => saveProgress(true))
  window.addEventListener('pagehide', () => saveProgress(true))
})

onBeforeUnmount(() => {
  saveProgress(true)
  audio.value?.pause()
  if (bgTimer) clearInterval(bgTimer)
  window.removeEventListener('keydown', onKey)
})
</script>

<template>
  <div class="viewer music-viewer">
    <header class="viewer-bar music-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="17" /></button>
        <div class="viewer-name">
          <strong>{{ empty ? '音乐播放器' : (playlistName || '正在播放') }}</strong>
          <span>{{ empty ? '未选择音乐' : `${index + 1} / ${tracks.length}` }}</span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="打开音乐文件" @click="openMusicFile"><file-music :size="16" /></button>
        <button class="viewer-btn" title="打开音乐文件夹" @click="openMusicFolder"><folder-open :size="16" /></button>
        <button class="viewer-btn" :class="{ active: showBgSettings }" title="背景轮播设置" @click="showBgSettings = !showBgSettings"><image-plus :size="16" /></button>
        <template v-if="!empty">
          <span class="bar-sep" />
          <button class="viewer-btn" title="在文件夹中显示" @click="host.reveal(path)"><external-link :size="16" /></button>
          <button class="viewer-btn" title="保存播放列表" @click="openSave"><plus :size="16" /></button>
        </template>
        <span class="bar-sep" />
        <button class="viewer-btn" title="全屏" @click="host.toggleFullscreen()"><maximize :size="16" /></button>
      </div>
    </header>

    <div v-if="showBgSettings" class="music-bg-settings">
      <div class="mbg-head">
        <strong>背景轮播</strong>
        <button class="icon-btn subtle" @click="showBgSettings = false"><x :size="14" /></button>
      </div>
      <div class="mbg-row">
        <span>文件夹</span>
        <div class="mbg-path" :title="bgFolder">{{ bgFolder ? basename(bgFolder) : '未选择（使用歌曲目录图片）' }}</div>
      </div>
      <div class="mbg-row">
        <n-button size="tiny" @click="pickBackgroundFolder"><template #icon><folder-open :size="13" /></template>选择文件夹</n-button>
        <n-button size="tiny" :disabled="!bgFolder" @click="clearBackgroundFolder">恢复歌曲目录</n-button>
      </div>
      <div class="mbg-row">
        <span>上一张/下一张</span>
        <button class="viewer-btn" title="上一张背景" @click="stepBg(-1)"><skip-back :size="15" /></button>
        <button class="viewer-btn" title="下一张背景" @click="stepBg(1)"><skip-forward :size="15" /></button>
        <span class="mbg-hint">{{ bgUrls.length ? `${bgIndex + 1} / ${bgUrls.length}` : '无图片' }}</span>
      </div>
      <div class="mbg-row">
        <span>间隔</span>
        <n-slider :value="mediaSettings.musicBackgroundSeconds || 8" :min="0.1" :max="60" :step="0.1" style="flex:1" @update:value="setBgSeconds" />
        <b>{{ Number(mediaSettings.musicBackgroundSeconds || 8).toFixed(1) }}s</b>
      </div>
      <div class="mbg-row">
        <span>显示</span>
        <n-select
          :value="bgFit"
          size="tiny"
          style="flex:1"
          :options="[
            { label: '铺满裁切（cover）', value: 'cover' },
            { label: '完整包含（contain）', value: 'contain' },
            { label: '拉伸填满', value: 'stretch' },
            { label: '原始居中', value: 'center' },
          ]"
          @update:value="setBgFit"
        />
      </div>
      <div class="mbg-row">
        <span>磨玻璃</span>
        <n-slider :value="bgBlur" :min="0" :max="10" :step="1" style="flex:1" @update:value="setBgBlur" />
        <b>{{ bgBlur }}</b>
      </div>
      <div class="mbg-row">
        <span>随机</span>
        <n-switch :value="bgRandom" size="small" @update:value="setBgRandom" />
        <span class="mbg-hint">自动轮播时随机选择</span>
      </div>
    </div>

    <div v-if="empty" class="viewer-empty music-empty">
      <div class="ve-icon"><music :size="58" :stroke-width="1.05" /></div>
      <h2>音乐播放器</h2>
      <p>打开一首音乐，或选择整个文件夹加入播放队列。支持歌词、封面、后台轮播图与播放列表。</p>
      <div class="ve-actions">
        <n-button type="primary" @click="openMusicFile">
          <template #icon><file-music :size="15" /></template>打开音乐文件
        </n-button>
        <n-button @click="openMusicFolder">
          <template #icon><folder-open :size="15" /></template>打开文件夹
        </n-button>
      </div>
    </div>

    <div v-else class="music-layout">
      <section class="music-left">
        <div class="music-art">
          <img v-if="!coverFailed" :src="coverUrl" alt="" @error="coverFailed = true" />
          <div v-else class="art-fallback"><music :size="64" :stroke-width="1" /><span>{{ title }}</span></div>
          <div class="art-glow" />
        </div>
        <div class="music-info">
          <h1 :title="title">{{ title }}</h1>
          <p>{{ artist }}</p>
          <span class="music-album">{{ meta?.album || '' }}</span>
        </div>

        <div class="music-progress">
          <n-slider
            :value="currentTime"
            :max="Math.max(1, duration)"
            :step="0.1"
            :tooltip="false"
            @update:value="seek"
          />
          <div class="music-times"><span>{{ formatClock(currentTime) }}</span><span>{{ formatClock(duration) }}</span></div>
        </div>

        <div class="music-controls">
          <button class="ctrl-btn" :title="modeLabel" @click="cycleMode"><component :is="modeIcon" :size="18" /></button>
          <button class="ctrl-btn" title="上一首" @click="prev"><skip-back :size="22" /></button>
          <button class="ctrl-play" :title="playing ? '暂停' : '播放'" @click="togglePlay">
            <pause-icon v-if="playing" :size="26" /><play-icon v-else :size="26" />
          </button>
          <button class="ctrl-btn" title="下一首" @click="next"><skip-forward :size="22" /></button>
          <button class="ctrl-btn" title="静音" @click="toggleMute">
            <volume-x v-if="muted || volume === 0" :size="18" /><volume2 v-else :size="18" />
          </button>
          <div class="volume-slider">
            <n-slider :value="volume" :min="0" :max="1" :step="0.01" :tooltip="false" @update:value="setVolume" />
          </div>
        </div>

        <div v-if="codecError" class="codec-error">
          当前 WebView 可能不支持该音频编码。
          <n-button size="small" @click="api.fsOpen(path)">用系统播放器打开</n-button>
        </div>

        <div class="playlist-panel music-left-playlist">
          <div class="playlist-head">
            <span>{{ playlistName || '当前文件夹' }} · {{ tracks.length }} 首</span>
            <n-select
              size="tiny"
              style="width: 150px"
              placeholder="载入已保存列表"
              :options="savedPlaylists.map(p => ({ label: `${p.name}（${p.count}）`, value: p.id }))"
              @update:value="(id: string) => { const p = savedPlaylists.find(x => x.id === id); if (p) loadPlaylist(p) }"
            />
          </div>
          <div class="playlist-list">
            <button
              v-for="(t, i) in tracks"
              :key="t.path + i"
              class="track"
              :class="{ active: i === index }"
              @click="goTo(i)"
            >
              <span class="track-no">{{ i === index && playing ? '♪' : i + 1 }}</span>
              <span class="track-title">{{ displayTitle(t) }}</span>
              <span class="track-artist">{{ displayArtist(t) }}</span>
              <span class="track-size">{{ t.size ? formatBytes(t.size) : '' }}</span>
              <span class="track-del" title="从队列移除" @click.stop="removeTrack(i)"><x :size="12" /></span>
            </button>
          </div>
        </div>
      </section>

      <section class="music-right">
        <div class="music-bg" :class="{ hide: bgFading }" :style="bgLayerStyle(bgUrl)" />
        <div class="music-bg next" :class="{ show: bgFading }" :style="bgLayerStyle(bgNext)" />
        <div class="music-veil" />
        <div class="music-right-head">
          <span class="music-right-label">歌词</span>
          <span class="music-right-count">{{ bgUrls.length ? `${bgIndex + 1} / ${bgUrls.length}` : '无背景图' }}</span>
        </div>
        <div class="lyric-stage">
          <template v-if="lyricView">
            <Transition name="lyric" mode="out-in">
              <div :key="activeLine" class="lyric-focus">
                <p class="lyric-context prev">{{ lyricView.prev?.text || '' }}</p>
                <p class="lyric-current" @click="lyricView.current && seek(lyricView.current.t)">{{ lyricView.current?.text || '♪' }}</p>
                <p class="lyric-context next">{{ lyricView.next?.text || '' }}</p>
              </div>
            </Transition>
          </template>
          <pre v-else-if="lyrics?.plain" class="lyrics-plain lyric-plain-center">{{ lyrics.plain }}</pre>
          <p v-else class="lyrics-empty lyric-empty-center">暂无歌词。可将同名 .lrc 文件放在音乐目录，或联网自动获取。</p>
        </div>
        <div class="music-bg-bar">
          <button class="viewer-btn" title="上一张背景" @click="stepBg(-1)"><skip-back :size="15" /></button>
          <button class="viewer-btn" title="下一张背景" @click="stepBg(1)"><skip-forward :size="15" /></button>
          <span>{{ bgUrls.length ? `${bgIndex + 1} / ${bgUrls.length}` : '无背景图' }}</span>
        </div>
      </section>
    </div>

    <audio
      ref="audio"
      preload="metadata"
      @loadedmetadata="onLoaded"
      @timeupdate="onTime"
      @ended="onEnded"
      @play="playing = true"
      @pause="playing = false"
      @error="onAudioError"
    />

    <n-modal v-model:show="saveModal" preset="card" title="保存播放列表" style="width: 420px">
      <n-input v-model:value="saveName" placeholder="播放列表名称" />
      <template #footer>
        <div class="modal-actions">
          <n-button @click="saveModal = false">取消</n-button>
          <n-button type="primary" :loading="saving" @click="doSave">保存</n-button>
        </div>
      </template>
    </n-modal>
  </div>
</template>
