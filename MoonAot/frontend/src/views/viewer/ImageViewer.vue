<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ChevronLeft, ChevronRight, Copy, ExternalLink, FileImage, FolderOpen, Info, Maximize, Minus, Music, Pause, Play, Plus,
  RotateCw, Rows3, Scan, SlidersHorizontal, Trash2, Wand2, X,
} from 'lucide-vue-next'
import { NButton, NSelect, NSlider, NSwitch, NSpin, NTooltip } from 'naive-ui'
import { api, urls } from '../../api'
import { state } from '../../store'
import { host } from '../../host'
import { toast, toastError } from '../../ui'
import type { ImageItem, ImagePage, PlaylistSummary } from '../../types'
import { basename, clamp, dirname, formatBytes } from '../../utils'
import ImageEditor from './ImageEditor.vue'

const route = useRoute()
const router = useRouter()

const current = ref(String(route.query.path || ''))
const rootFolder = ref(String(route.query.root || ''))
const recursive = ref(route.query.recursive === '1' || state.settings.media.recursiveImages)
const page = ref<ImagePage | null>(null)
const items = reactive(new Map<number, ImageItem>())
const index = ref(-1)
const total = ref(0)
const scanning = ref(false)
const loading = ref(true)
const error = ref('')
const zoom = ref(1)
const rotate = ref(0)
const offsetX = ref(0)
const offsetY = ref(0)
type FitMode = 'contain' | 'width' | 'height' | 'center' | 'actual'
const fitMode = ref<FitMode>((['width', 'height', 'center', 'actual'].includes(state.settings.media.imageFitMode)
  ? state.settings.media.imageFitMode : 'contain') as FitMode)
const slideshow = ref(false)
const comic = ref(state.settings.media.comicMode)
const comicWidth = ref(state.settings.media.comicWidth)
const infoOpen = ref(false)
const natural = reactive({ w: 0, h: 0 })
const dragging = ref(false)
const container = ref<HTMLElement | null>(null)
const imageEl = ref<HTMLImageElement | null>(null)
const showSlideSettings = ref(false)
const immersive = ref(false)
const bgmEl = ref<HTMLAudioElement | null>(null)
const bgmTracks = ref<string[]>([])
const bgmIndex = ref(0)
const bgmPlaylists = ref<PlaylistSummary[]>([])
const animClass = ref('')
const imgBust = ref(0)
const editorOpen = ref(false)
const ctx = reactive({ show: false, x: 0, y: 0 })

let pollTimer: ReturnType<typeof setTimeout> | null = null
let slideTimer: ReturnType<typeof setInterval> | null = null
let saveTimer: ReturnType<typeof setTimeout> | null = null
let comicTimer: ReturnType<typeof setTimeout> | null = null
let animTimer: ReturnType<typeof setTimeout> | null = null
let sliderTimer: ReturnType<typeof setTimeout> | null = null

const mediaSettings = computed(() => state.settings.media)
const slideTransition = computed(() => mediaSettings.value.imageTransition || 'fade')
const bgmFolder = computed(() => mediaSettings.value.imageBackgroundMusicFolder || '')
const bgmPlaylist = computed(() => bgmPlaylists.value.find(p => p.id === (mediaSettings.value.imageBackgroundMusicPlaylist || '')) || null)
const bgmTrack = computed(() => bgmTracks.value.length ? bgmTracks.value[Math.min(bgmIndex.value, bgmTracks.value.length - 1)] : '')
const bgmSrc = computed(() => bgmTrack.value
  ? urls.media(bgmTrack.value)
  : (mediaSettings.value.imageBackgroundMusic ? urls.media(mediaSettings.value.imageBackgroundMusic) : ''))

const folder = computed(() => rootFolder.value || dirname(current.value))
const currentItem = computed<ImageItem | undefined>(() => index.value >= 0 ? items.get(index.value) : undefined)
const imgSrc = computed(() => currentItem.value ? urls.image(currentItem.value.path) + '&v=' + imgBust.value : '')
const comicWidthPct = computed(() => comicWidth.value === 'full' ? 100 : comicWidth.value === '2/3' ? 66.6 : comicWidth.value === '1/2' ? 50 : 100)
const imageStyle = computed(() => {
  const t: string[] = []
  if (zoom.value !== 1 || offsetX.value || offsetY.value || rotate.value)
    t.push(`translate(${offsetX.value}px, ${offsetY.value}px) scale(${zoom.value}) rotate(${rotate.value}deg)`)
  return {
    transform: t.join(' ') || 'none',
    width: comic.value ? comicWidthPct.value + '%' : undefined,
  }
})
const containerClass = computed(() => ({
  comic: comic.value,
  contain: !comic.value && fitMode.value === 'contain',
  width: !comic.value && fitMode.value === 'width',
  height: !comic.value && fitMode.value === 'height',
  center: !comic.value && fitMode.value === 'center',
  actual: !comic.value && fitMode.value === 'actual',
}))
const canPan = computed(() => zoom.value > 1 && !comic.value)
const empty = computed(() => !current.value)
const thumbStrip = computed(() => {
  const list: { i: number; item: ImageItem }[] = []
  for (const [i, item] of items) list.push({ i, item })
  list.sort((a, b) => a.i - b.i)
  return list
})

function clearTimers() {
  if (pollTimer) { clearTimeout(pollTimer); pollTimer = null }
  if (slideTimer) { clearInterval(slideTimer); slideTimer = null }
  if (comicTimer) { clearTimeout(comicTimer); comicTimer = null }
  if (animTimer) { clearTimeout(animTimer); animTimer = null }
}

function scheduleSave() {
  if (!folder.value) return
  if (saveTimer) clearTimeout(saveTimer)
  saveTimer = setTimeout(() => {
    api.saveMediaProgress({ path: folder.value, kind: 'image', position: index.value, duration: total.value, scale: zoom.value, rotation: rotate.value }).catch(() => { })
  }, 500)
}

let loadSeq = 0

async function load(targetPath: string, refresh = false, silent = false) {
  if (!targetPath) {
    loading.value = false
    error.value = ''
    page.value = null
    items.clear()
    total.value = 0
    index.value = -1
    return
  }
  if (!silent) { loading.value = true; error.value = '' }
  const seq = ++loadSeq
  try {
    const dir = rootFolder.value || dirname(targetPath)
    const res = await api.images({
      path: dir,
      recursive: recursive.value ? 1 : 0,
      current: targetPath,
      limit: 240,
      refresh: refresh ? 1 : 0,
    })
    if (seq !== loadSeq) return
    if (silent && targetPath !== current.value) return
    page.value = res
    total.value = res.total
    scanning.value = res.scanning
    if (silent) {
      // 扫描进行中：只增量合并，绝不清空当前已加载图片，避免闪屏/丢图
      res.items.forEach((it, i) => items.set(res.offset + i, it))
      if (index.value < 0 && res.index >= 0) index.value = res.index
      if (res.scanning) schedulePoll()
      return
    }
    items.clear()
    res.items.forEach((it, i) => items.set(res.offset + i, it))
    if (res.index >= 0) index.value = res.index
    else {
      let found = -1
      for (const [i, it] of items) if (it.path === targetPath) { found = i; break }
      index.value = found
    }
    if (res.scanning) schedulePoll()
    if (!res.scanning && index.value < 0 && !res.items.some(x => x.path === targetPath))
      error.value = '文件不在当前图片列表中'
    loading.value = false
    savePosition()
  } catch (e) {
    if (seq !== loadSeq) return
    if (!silent) {
      loading.value = false
      error.value = e instanceof Error ? e.message : String(e)
    } else if (scanning.value) schedulePoll()
  }
}

function schedulePoll() {
  if (pollTimer) clearTimeout(pollTimer)
  pollTimer = setTimeout(() => {
    pollTimer = null
    if (scanning.value && current.value) void load(current.value, false, true)
  }, 600)
}

async function loadAround(i: number) {
  if (i < 0) return
  try {
    const res = await api.images({
      path: folder.value,
      recursive: recursive.value ? 1 : 0,
      offset: Math.max(0, i - 120),
      limit: 260,
      current: current.value,
    })
    total.value = res.total
    scanning.value = res.scanning
    res.items.forEach((it, k) => items.set(res.offset + k, it))
  } catch { /* ignore */ }
}

async function restoreViewState() {
  if (!folder.value) return
  try {
    const prog = await api.mediaProgress(folder.value)
    if (prog) {
      if (prog.scale && prog.scale > 0) zoom.value = clamp(prog.scale, 0.2, 8)
      rotate.value = prog.rotation || 0
      offsetX.value = 0
      offsetY.value = 0
    }
  } catch { }
}

async function openImagePath(target: string, root?: string, restoreView = true) {
  if (!target) return
  rootFolder.value = root || dirname(target)
  rememberFolder(rootFolder.value)
  if (restoreView) await restoreViewState()
  current.value = target
  host.setTitle(basename(target))
  const q: Record<string, any> = { ...route.query, path: target, recursive: recursive.value ? '1' : '0' }
  delete q.folder
  if (rootFolder.value && rootFolder.value.toLowerCase() !== dirname(target).toLowerCase())
    q.root = rootFolder.value
  else delete q.root
  await router.replace({ query: q })
  await load(target, true)
}

async function openImageFile() {
  const file = await host.pickFile('image', '选择要浏览的图片')
  if (file) await openImagePath(file)
}

async function openImageFolder() {
  const dir = await host.pickFolder('选择图片文件夹（递归浏览）')
  if (dir) await loadImageFolder(dir)
}

async function loadImageFolder(dir: string, silent = false) {
  if (!dir) return
  rootFolder.value = dir
  rememberFolder(dir)
  zoom.value = 1
  rotate.value = 0
  recursive.value = true
  if (!state.settings.media.recursiveImages) {
    state.settings.media.recursiveImages = true
    void api.saveAppSettings({ media: { ...state.settings.media, recursiveImages: true } }).catch(() => { })
  }
  try {
    let first: ImageItem | undefined
    for (let attempt = 0; attempt < 40; attempt++) {
      const res = await api.images({
        path: dir,
        recursive: 1,
        limit: 1,
        refresh: attempt === 0 ? 1 : 0,
      })
      first = res.items?.[0]
      if (first) break
      if (!res.scanning) break
      await new Promise(r => setTimeout(r, 300))
    }
    if (!first) {
      rootFolder.value = ''
      if (!silent) toast('这个文件夹里没有图片', 'warning')
      return
    }

    // 有上次浏览位置时从上次那张继续（索引超出时回退到第一张）
    let target = first
    try {
      const prog = await api.mediaProgress(dir)
      if (prog) {
        if (prog.scale && prog.scale > 0) zoom.value = clamp(prog.scale, 0.2, 8)
        rotate.value = prog.rotation || 0
        if (prog.position > 0) {
          const page = await api.images({ path: dir, recursive: 1, offset: prog.position, limit: 1 })
          if (page.items?.[0]) target = page.items[0]
        }
      }
    } catch { }
    await openImagePath(target.path, dir, false)
  } catch (e) { if (!silent) toastError(e) }
}

/** 记录上次打开的图片文件夹，下次空启动时自动恢复。 */
function rememberFolder(dir: string) {
  if (!dir) return
  if (mediaSettings.value.imageLastFolder === dir) return
  void saveMedia({ imageLastFolder: dir })
}

function onSliderInput(ev: Event) {
  const v = Number((ev.target as HTMLInputElement).value)
  if (!Number.isFinite(v)) return
  index.value = clamp(Math.round(v), 0, Math.max(0, total.value - 1))
  if (sliderTimer) clearTimeout(sliderTimer)
  sliderTimer = setTimeout(() => {
    sliderTimer = null
    void goTo(index.value)
  }, 60)
}

function onSliderChange() {
  if (sliderTimer) { clearTimeout(sliderTimer); sliderTimer = null }
  void goTo(index.value)
}

async function goTo(i: number) {  if (i < 0 || i >= total.value) return
  index.value = i
  if (!items.has(i)) await loadAround(i)
  const it = items.get(i)
  if (!it) return
  index.value = i
  current.value = it.path
  offsetX.value = 0
  offsetY.value = 0
  if (slideshow.value) playTransition()
  if (comic.value && container.value) container.value.scrollTop = 0
  const q = { ...route.query, path: it.path }
  void router.replace({ query: q })
  host.setTitle(basename(it.path))
  savePosition()
  preload(i + 1)
  preload(i - 1)
  void nextTick(scrollCurrentThumb)
}

function scrollCurrentThumb() {
  const el = document.querySelector('.image-strip .thumb.cur') as HTMLElement | null
  el?.scrollIntoView({ block: 'nearest', inline: 'center' })
}

function preload(i: number) {
  const it = items.get(i)
  if (!it) { void loadAround(i); return }
  const img = new Image()
  img.src = urls.image(it.path)
}

function next() {
  if (index.value < total.value - 1) void goTo(index.value + 1)
  else if (slideshow.value) {
    if (mediaSettings.value.imageSlideLoop) void goTo(0)
    else stopSlideshow()
  }
}
function prev() {
  if (index.value > 0) void goTo(index.value - 1)
  else if (slideshow.value && mediaSettings.value.imageSlideLoop) void goTo(Math.max(0, total.value - 1))
}

function savePosition() {
  scheduleSave()
}

function resetZoom() {
  zoom.value = 1
  offsetX.value = 0
  offsetY.value = 0
  rotate.value = 0
  dragging.value = false
  persistView()
}

function setFitMode(mode: FitMode) {
  fitMode.value = mode
  zoom.value = 1
  offsetX.value = 0
  offsetY.value = 0
  dragging.value = false
  void saveMedia({ imageFitMode: mode })
}

function toggleImmersive() {
  immersive.value = !immersive.value
  if (immersive.value) {
    infoOpen.value = false
    showSlideSettings.value = false
  }
  host.toggleFullscreen()
}

function onFullscreenEvent(ev: Event) {
  const full = !!(ev as CustomEvent).detail
  immersive.value = full
  if (full) { infoOpen.value = false; showSlideSettings.value = false }
}

function maxOffset() {
  const stage = container.value
  const img = imageEl.value
  if (!stage || !img) return { x: 0, y: 0 }
  return {
    x: Math.max(0, (img.offsetWidth * zoom.value - stage.clientWidth) / 2),
    y: Math.max(0, (img.offsetHeight * zoom.value - stage.clientHeight) / 2),
  }
}

function clampOffsets() {
  const m = maxOffset()
  offsetX.value = clamp(offsetX.value, -m.x, m.x)
  offsetY.value = clamp(offsetY.value, -m.y, m.y)
}

/** 以舞台中心为原点缩放；传入锚点可让光标下的像素保持不动。 */
function applyZoom(nextZoom: number, anchorX = 0, anchorY = 0) {
  const z = clamp(nextZoom, 0.2, 8)
  if (z <= 1.001) {
    zoom.value = Math.min(z, 1)
    offsetX.value = 0
    offsetY.value = 0
    persistView()
    return
  }
  const ratio = z / zoom.value
  offsetX.value = anchorX - ratio * (anchorX - offsetX.value)
  offsetY.value = anchorY - ratio * (anchorY - offsetY.value)
  zoom.value = z
  clampOffsets()
  persistView()
}

function persistView() {
  if (!folder.value) return
  if (saveTimer) clearTimeout(saveTimer)
  saveTimer = setTimeout(() => {
    api.saveMediaProgress({
      path: folder.value, kind: 'image', position: index.value, duration: total.value,
      scale: zoom.value, rotation: rotate.value,
    }).catch(() => { })
  }, 180)
}

function rotateView(delta: number) {
  rotate.value = (rotate.value + delta + 360) % 360
  persistView()
}

function zoomBy(delta: number) {
  if (comic.value) return
  applyZoom(zoom.value + delta)
}

function onWheel(ev: WheelEvent) {
  if (comic.value) return
  ev.preventDefault()
  const rect = container.value?.getBoundingClientRect()
  const cx = rect ? ev.clientX - rect.left - rect.width / 2 : 0
  const cy = rect ? ev.clientY - rect.top - rect.height / 2 : 0
  applyZoom(zoom.value + (ev.deltaY > 0 ? -0.12 : 0.12), cx, cy)
}

let dragStart = { x: 0, y: 0, ox: 0, oy: 0 }
function onPointerDown(ev: PointerEvent) {
  if (ev.button !== 0 || zoom.value <= 1 || comic.value) return
  dragging.value = true
  dragStart = { x: ev.clientX, y: ev.clientY, ox: offsetX.value, oy: offsetY.value }
  try { container.value?.setPointerCapture(ev.pointerId) } catch { }
  ev.preventDefault()
}
function onPointerMove(ev: PointerEvent) {
  if (!dragging.value) return
  const m = maxOffset()
  offsetX.value = clamp(dragStart.ox + (ev.clientX - dragStart.x), -m.x, m.x)
  offsetY.value = clamp(dragStart.oy + (ev.clientY - dragStart.y), -m.y, m.y)
}
function onPointerUp(ev: PointerEvent) {
  if (!dragging.value) return
  dragging.value = false
  try { container.value?.releasePointerCapture(ev.pointerId) } catch { }
}

function playTransition() {
  if (comic.value || slideTransition.value === 'none') return
  animClass.value = ''
  if (animTimer) { clearTimeout(animTimer); animTimer = null }
  void nextTick(() => {
    animClass.value = 'anim-' + slideTransition.value
    animTimer = setTimeout(() => { animClass.value = ''; animTimer = null }, 520)
  })
}

function startSlideTimer() {
  if (slideTimer) { clearInterval(slideTimer); slideTimer = null }
  const seconds = Math.max(0.1, Number(mediaSettings.value.imageSlideSeconds) || 5)
  slideTimer = setInterval(() => {
    if (!slideshow.value) return
    if (index.value >= total.value - 1) {
      if (mediaSettings.value.imageSlideLoop) void goTo(0)
      else stopSlideshow()
      return
    }
    void goTo(index.value + 1)
  }, seconds * 1000)
}

function playBgm() {
  const el = bgmEl.value
  if (!el || !bgmSrc.value) return
  try { el.volume = 0.75; void el.play().catch(() => { }) } catch { }
}

function pauseBgm() {
  try { bgmEl.value?.pause() } catch { }
}

function startSlideshow() {
  if (slideshow.value || empty.value || total.value === 0) return
  slideshow.value = true
  startSlideTimer()
  playBgm()
}

function stopSlideshow() {
  slideshow.value = false
  if (slideTimer) { clearInterval(slideTimer); slideTimer = null }
  pauseBgm()
}

function toggleSlideshow() {
  if (slideshow.value) stopSlideshow()
  else startSlideshow()
}

// ==================== 幻灯片设置 ====================

async function saveMedia(patch: Record<string, any>) {
  try {
    await api.saveAppSettings({ media: { ...state.settings.media, ...patch } })
    Object.assign(state.settings.media, patch)
  } catch (e) { toastError(e) }
}

function setSlideSeconds(v: number | null) {
  if (v === null || !Number.isFinite(v)) return
  void saveMedia({ imageSlideSeconds: Math.round(clamp(v, 0.1, 60) * 10) / 10 })
}

function setTransition(v: 'none' | 'fade' | 'slide' | 'zoom') {
  void saveMedia({ imageTransition: v })
}

function setSlideLoop(v: boolean) {
  void saveMedia({ imageSlideLoop: v })
}

async function chooseBgm() {
  const file = await host.pickFile('audio', '选择幻灯片背景音乐')
  if (!file) return
  bgmTracks.value = []
  bgmIndex.value = 0
  await saveMedia({ imageBackgroundMusic: file, imageBackgroundMusicFolder: null, imageBackgroundMusicPlaylist: null, imageBackgroundMusicIndex: 0 })
}

async function chooseBgmFolder() {
  const dir = await host.pickFolder('选择幻灯片背景音乐文件夹（递归）')
  if (!dir) return
  bgmTracks.value = []
  bgmIndex.value = 0
  await saveMedia({ imageBackgroundMusic: null, imageBackgroundMusicFolder: dir, imageBackgroundMusicPlaylist: null, imageBackgroundMusicIndex: 0 })
  await loadBgmFolder(dir)
  if (!bgmTracks.value.length) toast('这个文件夹里没有音乐', 'warning')
  else if (slideshow.value) playBgm()
}

async function loadBgmPlaylist(id: string) {
  if (!id) { bgmTracks.value = []; bgmIndex.value = 0; return }
  try {
    const rec = await api.playlist(id)
    bgmTracks.value = rec.items
    bgmIndex.value = clamp(mediaSettings.value.imageBackgroundMusicIndex || 0, 0, Math.max(0, rec.items.length - 1))
  } catch { bgmTracks.value = []; bgmIndex.value = 0 }
}

async function chooseBgmPlaylist(id: string | null) {
  if (!id) {
    bgmTracks.value = []
    bgmIndex.value = 0
    await saveMedia({ imageBackgroundMusicPlaylist: null, imageBackgroundMusicIndex: 0 })
    return
  }
  bgmTracks.value = []
  bgmIndex.value = 0
  await saveMedia({ imageBackgroundMusicPlaylist: id, imageBackgroundMusic: null, imageBackgroundMusicFolder: null, imageBackgroundMusicIndex: 0 })
  await loadBgmPlaylist(id)
  if (!bgmTracks.value.length) toast('这个播放列表里没有音乐', 'warning')
  else if (slideshow.value) playBgm()
}

async function loadBgmFolder(dir: string) {
  if (!dir) { bgmTracks.value = []; bgmIndex.value = 0; return }
  try {
    const res = await api.fsScan(dir, 'audio', 20000)
    bgmTracks.value = res.entries.filter(e => !e.isDir).map(e => e.path)
    bgmIndex.value = clamp(mediaSettings.value.imageBackgroundMusicIndex || 0, 0, Math.max(0, bgmTracks.value.length - 1))
  } catch { bgmTracks.value = []; bgmIndex.value = 0 }
}

function onBgmEnded() {
  if (!bgmTracks.value.length) return
  if (!slideshow.value) return
  bgmIndex.value = (bgmIndex.value + 1) % bgmTracks.value.length
  void saveMedia({ imageBackgroundMusicIndex: bgmIndex.value })
  void nextTick(() => { bgmEl.value?.load(); playBgm() })
}

function clearBgm() {
  pauseBgm()
  bgmTracks.value = []
  bgmIndex.value = 0
  void saveMedia({ imageBackgroundMusic: null, imageBackgroundMusicFolder: null, imageBackgroundMusicPlaylist: null, imageBackgroundMusicIndex: 0 })
}

const bgmName = computed(() => {
  if (bgmPlaylist.value) return `${bgmPlaylist.value.name} · ${bgmTracks.value.length} 首`
  if (bgmTracks.value.length) return `${basename(bgmFolder.value)} · ${bgmTracks.value.length} 首`
  return mediaSettings.value.imageBackgroundMusic ? basename(mediaSettings.value.imageBackgroundMusic) : ''
})

watch(bgmSrc, () => {
  if (slideshow.value) playBgm()
})

watch(() => mediaSettings.value.imageSlideSeconds, () => {
  if (slideshow.value) startSlideTimer()
})

function setComic(v: boolean) {
  comic.value = v
  if (v) { fitMode.value = 'width'; resetZoom() }
  void api.saveAppSettings({ media: { ...state.settings.media, comicMode: v } }).catch(() => { })
}

function setComicAuto(v: boolean) {
  void api.saveAppSettings({ media: { ...state.settings.media, comicAutoPage: v } }).catch(() => { })
}

function onScroll() {
  if (!comic.value || !comicTimer) {
    if (!comic.value || !state.settings.media.comicAutoPage) return
  }
  const el = container.value
  if (!el) return
  const atBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 5
  if (!atBottom) {
    if (comicTimer) { clearTimeout(comicTimer); comicTimer = null }
    return
  }
  if (comicTimer) return
  comicTimer = setTimeout(() => {
    comicTimer = null
    if (index.value < total.value - 1) void goTo(index.value + 1)
  }, 800)
}

function onImageLoad() {
  const img = imageEl.value
  if (img) { natural.w = img.naturalWidth; natural.h = img.naturalHeight }
  void nextTick(clampOffsets)
}

function openCtx(ev: MouseEvent) {
  if (empty.value) return
  ctx.x = Math.min(ev.clientX, window.innerWidth - 180)
  ctx.y = Math.min(ev.clientY, window.innerHeight - 170)
  ctx.show = true
}

function editCurrent() {
  ctx.show = false
  if (current.value) editorOpen.value = true
}

async function copyImagePath() {
  ctx.show = false
  try { await navigator.clipboard.writeText(current.value); toast('路径已复制') } catch { }
}

async function onEdited(savedPath: string, mode: 'copy' | 'overwrite') {
  editorOpen.value = false
  if (mode === 'overwrite') {
    imgBust.value++
    natural.w = 0; natural.h = 0
    void load(current.value, false, false)
  } else {
    // 副本已写入同一文件夹，刷新缩略图列表
    void load(current.value, true, true)
    void nextTick(scrollCurrentThumb)
    void savedPath
  }
}

function onKey(ev: KeyboardEvent) {
  if (editorOpen.value) return
  if (ev.key === 'ArrowLeft') { ev.preventDefault(); prev() }
  else if (ev.key === 'ArrowRight') { ev.preventDefault(); next() }
  else if (ev.key === '+' || ev.key === '=') { ev.preventDefault(); zoomBy(0.15) }
  else if (ev.key === '-') { ev.preventDefault(); zoomBy(-0.15) }
  else if (ev.key === '0') { ev.preventDefault(); resetZoom() }
  else if (ev.key.toLowerCase() === 'r') { ev.preventDefault(); rotateView(90) }
  else if (ev.key === ' ') { ev.preventDefault(); toggleSlideshow() }
  else if (ev.key === 'Home') { void goTo(0) }
  else if (ev.key === 'End') { void goTo(total.value - 1) }
  else if (ev.key === 'Escape') { if (immersive.value) toggleImmersive(); else host.closeWindow() }
  else if (ev.key === 'F11') { ev.preventDefault(); toggleImmersive() }
}

function resetEmpty() {
  stopSlideshow()
  resetZoom()
  current.value = ''
  rootFolder.value = ''
  page.value = null
  items.clear()
  total.value = 0
  index.value = -1
  error.value = ''
  loading.value = false
  host.setTitle('图片浏览器')
}

watch(() => route.query.path, (v) => {
  const p = String(v || '')
  if (p && p !== current.value) {
    rootFolder.value = String(route.query.root || dirname(p))
    current.value = p
    void restoreViewState().then(() => void load(p))
  } else if (!p) resetEmpty()
})

watch(() => route.query.root, async (v) => {
  const r = String(v || '')
  if (r && r.toLowerCase() !== rootFolder.value.toLowerCase()) {
    rootFolder.value = r
    await restoreViewState()
    if (current.value) void load(current.value, true)
  }
})

watch(() => route.query.folder, (v) => {
  const dir = String(v || '')
  if (dir) void loadImageFolder(dir)
})

onMounted(async () => {
  host.setTitle(basename(current.value) || '图片浏览器')
  const folderParam = String(route.query.folder || '')
  rootFolder.value = String(route.query.root || dirname(current.value))
  if (folderParam) await loadImageFolder(folderParam)
  else if (!current.value) {
    // 没带路径启动时自动恢复上次浏览的文件夹
    const last = mediaSettings.value.imageLastFolder
    if (last) await loadImageFolder(last, true)
    if (!current.value) { await restoreViewState(); await load('') }
  } else {
    rememberFolder(rootFolder.value)
    await restoreViewState()
    await load(current.value)
  }
  try { bgmPlaylists.value = (await api.playlists()).items.filter(p => p.kind === 'audio') } catch { }
  if (mediaSettings.value.imageBackgroundMusicPlaylist) await loadBgmPlaylist(mediaSettings.value.imageBackgroundMusicPlaylist)
  else if (mediaSettings.value.imageBackgroundMusicFolder) await loadBgmFolder(mediaSettings.value.imageBackgroundMusicFolder)
  window.addEventListener('keydown', onKey)
  window.addEventListener('moon-fullscreen', onFullscreenEvent as EventListener)
})

onBeforeUnmount(() => {
  stopSlideshow()
  clearTimers()
  if (sliderTimer) { clearTimeout(sliderTimer); sliderTimer = null }
  if (folder.value) rememberFolder(folder.value)
  if (saveTimer) clearTimeout(saveTimer)
  if (folder.value)
    void api.saveMediaProgressKeepalive({ path: folder.value, kind: 'image', position: index.value, duration: total.value, scale: zoom.value, rotation: rotate.value })
  window.removeEventListener('keydown', onKey)
  window.removeEventListener('moon-fullscreen', onFullscreenEvent as EventListener)
})
</script>

<template>
  <div class="viewer image-viewer" :class="{ immersive }">
    <header class="viewer-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="17" /></button>
        <div class="viewer-name">
          <strong :title="current">{{ empty ? '图片浏览器' : basename(current) }}</strong>
          <span>{{ empty ? '未打开图片' : `${index >= 0 ? index + 1 : '-'} / ${total || '-'}` }}<template v-if="!empty && currentItem"> · {{ formatBytes(currentItem.size) }}</template></span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="打开图片文件" @click="openImageFile"><file-image :size="16" /></button>
        <button class="viewer-btn" title="打开图片文件夹" @click="openImageFolder"><folder-open :size="16" /></button>
        <template v-if="!empty">
          <span class="bar-sep" />
          <button class="viewer-btn" title="上一张" :disabled="index <= 0" @click="prev"><chevron-left :size="18" /></button>
          <button class="viewer-btn" title="下一张" :disabled="index >= total - 1" @click="next"><chevron-right :size="18" /></button>
          <span class="bar-sep" />
          <button class="viewer-btn" title="缩小" @click="zoomBy(-0.15)"><minus :size="16" /></button>
          <span class="zoom-value">{{ Math.round(zoom * 100) }}%</span>
          <button class="viewer-btn" title="放大" @click="zoomBy(0.15)"><plus :size="16" /></button>
          <button class="viewer-btn" title="适应窗口" @click="setFitMode('contain')"><scan :size="16" /></button>
          <n-select size="small" style="width: 132px" :consistent-menu-width="false" :menu-props="{ style: { minWidth: '144px' } }" :value="fitMode" :options="[
            { label: '自动居中', value: 'center' }, { label: '适应窗口', value: 'contain' },
            { label: '适应宽度', value: 'width' }, { label: '适应高度', value: 'height' },
            { label: '原始大小', value: 'actual' },
          ]" @update:value="setFitMode" />
          <button class="viewer-btn" title="向右旋转 90°" @click="rotateView(90)"><rotate-cw :size="16" /></button>
          <button class="viewer-btn" title="向左旋转 90°" @click="rotateView(-90)"><rotate-cw :size="16" style="transform: scaleX(-1)" /></button>
          <button class="viewer-btn" :class="{ active: slideshow }" title="幻灯片播放（空格）" @click="toggleSlideshow">
            <pause v-if="slideshow" :size="16" /><play v-else :size="16" />
          </button>
          <button class="viewer-btn" :class="{ active: showSlideSettings }" title="幻灯片设置" @click="showSlideSettings = !showSlideSettings">
            <sliders-horizontal :size="16" />
          </button>
          <button class="viewer-btn" :class="{ active: comic }" title="漫画模式" @click="setComic(!comic)"><rows3 :size="17" /></button>
          <button class="viewer-btn" title="编辑图片" @click="editorOpen = true"><wand2 :size="16" /></button>
          <button class="viewer-btn" title="在文件夹中显示" @click="host.reveal(current)"><external-link :size="16" /></button>
          <button class="viewer-btn" title="信息" @click="infoOpen = !infoOpen"><info :size="16" /></button>
        </template>
        <span class="bar-sep" />
        <button class="viewer-btn" :class="{ active: immersive }" title="全屏（仅显示图片）" @click="toggleImmersive"><maximize :size="16" /></button>
      </div>
    </header>

    <div v-if="comic" class="comic-bar">
      <span>漫画宽度</span>
      <n-select v-model:value="comicWidth" size="small" style="width: 110px" :options="[
        { label: '自适应', value: 'default' }, { label: '全宽', value: 'full' },
        { label: '2/3', value: '2/3' }, { label: '1/2', value: '1/2' },
      ]" />
      <span>滑到底自动下一张</span>
      <n-switch :value="state.settings.media.comicAutoPage" size="small" @update:value="setComicAuto" />
    </div>

    <div v-if="showSlideSettings" class="slide-settings">
      <div class="ss-head">
        <strong>幻灯片设置</strong>
        <button class="icon-btn subtle" @click="showSlideSettings = false"><x :size="14" /></button>
      </div>
      <div class="ss-row">
        <span>间隔</span>
        <n-slider
          :value="mediaSettings.imageSlideSeconds"
          :min="0.1" :max="60" :step="0.1"
          style="flex: 1"
          @update:value="setSlideSeconds"
        />
        <b>{{ Number(mediaSettings.imageSlideSeconds).toFixed(1) }}s</b>
      </div>
      <div class="ss-row">
        <span>过渡</span>
        <n-select
          :value="slideTransition"
          size="small"
          style="flex: 1"
          :options="[
            { label: '淡入淡出', value: 'fade' },
            { label: '横向滑动', value: 'slide' },
            { label: '缩放进入', value: 'zoom' },
            { label: '无效果', value: 'none' },
          ]"
          @update:value="setTransition"
        />
      </div>
      <div class="ss-row">
        <span>循环播放</span>
        <n-switch :value="mediaSettings.imageSlideLoop" size="small" @update:value="setSlideLoop" />
        <span class="ss-hint">到末尾后从头开始</span>
      </div>
      <div class="ss-row">
        <span>音乐列表</span>
        <n-select
          size="small"
          style="flex: 1"
          clearable
          :value="mediaSettings.imageBackgroundMusicPlaylist || null"
          :options="bgmPlaylists.map(p => ({ label: `${p.name}（${p.count}）`, value: p.id }))"
          :placeholder="bgmPlaylists.length ? '选择已保存的音乐列表' : '暂无已保存的音乐列表'"
          :disabled="!bgmPlaylists.length"
          @update:value="chooseBgmPlaylist"
        />
      </div>
      <div class="ss-row bgm">
        <span>背景音乐</span>
        <div class="ss-bgm" :title="bgmName || '未设置'">
          <music :size="13" />
          <span>{{ bgmName || '未设置' }}</span>
        </div>
        <button class="icon-btn subtle" title="选择背景音乐文件" @click="chooseBgm"><music :size="14" /></button>
        <button class="icon-btn subtle" title="选择背景音乐文件夹（递归）" @click="chooseBgmFolder"><folder-open :size="14" /></button>
        <button v-if="bgmName" class="icon-btn subtle danger" title="清除背景音乐" @click="clearBgm"><trash2 :size="14" /></button>
      </div>
      <small class="ss-note">幻灯片播放时自动播放背景音乐；可直接选用音乐播放器保存的列表，也可指定文件或文件夹。</small>
    </div>

    <audio ref="bgmEl" :src="bgmSrc" :loop="bgmTracks.length <= 1" preload="auto" class="bgm-audio" @ended="onBgmEnded" />

    <div
      ref="container"
      class="image-stage"
      :class="[containerClass, { 'can-pan': canPan, grabbing: dragging }]"
      @wheel="onWheel"
      @scroll.passive="onScroll"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @pointercancel="onPointerUp"
      @contextmenu.prevent="openCtx"
    >
      <div v-if="empty" class="viewer-empty image-empty">
        <div class="ve-icon"><file-image :size="54" :stroke-width="1.1" /></div>
        <h2>图片浏览器</h2>
        <p>打开一张图片，或选择一个文件夹开始浏览、缩放与幻灯片播放。</p>
        <div class="ve-actions">
          <n-button type="primary" @click="openImageFile">
            <template #icon><file-image :size="15" /></template>打开图片文件
          </n-button>
          <n-button @click="openImageFolder">
            <template #icon><folder-open :size="15" /></template>打开文件夹
          </n-button>
        </div>
      </div>
      <div v-else-if="loading" class="viewer-loading"><n-spin size="large" /><span>{{ scanning ? '正在扫描文件夹…' : '正在加载图片…' }}</span></div>
      <div v-else-if="error" class="viewer-error">{{ error }}</div>
      <img
        v-else-if="imgSrc"
        ref="imageEl"
        class="viewer-image"
        :class="animClass"
        :src="imgSrc"
        :style="imageStyle"
        alt=""
        draggable="false"
        @load="onImageLoad"
        @dblclick="resetZoom"
      />
      <div v-if="scanning && !loading" class="scan-hint">正在后台扫描子文件夹… {{ total }} 张</div>
    </div>

    <div v-if="!empty && thumbStrip.length" class="image-strip-wrap">
      <div class="strip-head">
        <span>同一文件夹图片</span>
        <input
          v-if="total > 1"
          class="strip-slider"
          type="range"
          min="0"
          :max="total - 1"
          step="1"
          :value="index"
          :title="`拖动选择图片：${index + 1} / ${total}`"
          @input="onSliderInput"
          @change="onSliderChange"
        />
        <span>{{ total }} 张 · 当前 {{ index + 1 }}</span>
      </div>
      <div class="image-strip">
        <button
          v-for="t in thumbStrip"
          :key="t.item.path"
          class="thumb"
          :class="{ cur: t.i === index }"
          :title="t.item.name"
          @click="goTo(t.i)"
        >
          <img :src="urls.thumbPath(t.item.path, 104, 84)" loading="lazy" decoding="async" alt="" />
          <span>{{ t.i + 1 }}</span>
        </button>
      </div>
    </div>

    <div v-if="infoOpen && !empty" class="viewer-info">
      <div><span>文件</span><b>{{ current }}</b></div>
      <div><span>尺寸</span><b>{{ natural.w && natural.h ? `${natural.w} × ${natural.h}` : '-' }}</b></div>
      <div><span>大小</span><b>{{ currentItem ? formatBytes(currentItem.size) : '-' }}</b></div>
      <div><span>位置</span><b>{{ index + 1 }} / {{ total }}</b></div>
      <div><span>所在文件夹</span><b class="mono">{{ folder }}</b></div>
    </div>

    <div v-if="ctx.show" class="image-ctx-backdrop" @click="ctx.show = false" @contextmenu.prevent="ctx.show = false" />
    <div v-if="ctx.show" class="image-ctx" :style="{ left: ctx.x + 'px', top: ctx.y + 'px' }">
      <button @click="editCurrent"><wand2 :size="14" /><span>编辑图片</span></button>
      <button @click="copyImagePath"><copy :size="14" /><span>复制图片路径</span></button>
      <button @click="ctx.show = false; host.reveal(current)"><external-link :size="14" /><span>在文件夹中显示</span></button>
      <button @click="ctx.show = false; infoOpen = true"><info :size="14" /><span>图片信息</span></button>
    </div>

    <ImageEditor
      v-if="editorOpen && current"
      :path="current"
      :filename="basename(current)"
      @close="editorOpen = false"
      @saved="onEdited"
    />
  </div>
</template>
