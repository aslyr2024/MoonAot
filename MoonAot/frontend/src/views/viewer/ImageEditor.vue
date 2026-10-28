<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import {
  Check, Crop, FlipHorizontal2, FlipVertical2, Redo2, RotateCcw, RotateCw, Save, Undo2, X,
} from 'lucide-vue-next'
import { NButton, NSelect, NSlider, NSwitch } from 'naive-ui'
import { api, urls } from '../../api'
import { toast, toastError } from '../../ui'
import { basename, clamp } from '../../utils'

const props = defineProps<{ path: string; filename?: string }>()
const emit = defineEmits<{ (e: 'close'): void; (e: 'saved', path: string, mode: 'copy' | 'overwrite'): void }>()

const loading = ref(true)
const error = ref('')
const saving = ref(false)
const stage = ref<HTMLElement | null>(null)
const canvas = ref<HTMLCanvasElement | null>(null)

const rotation = ref(0)
const flipH = ref(false)
const flipV = ref(false)
const brightness = ref(100)
const contrast = ref(100)
const saturation = ref(100)
const grayscale = ref(false)
const sepia = ref(false)
const invert = ref(false)
const scalePct = ref(100)
const format = ref<'image/png' | 'image/jpeg'>('image/png')

const cropMode = ref(false)
const crop = reactive({ x: 0, y: 0, w: 0, h: 0, active: false })
const previewScale = ref(1)

let base: HTMLCanvasElement | null = null
let original: HTMLCanvasElement | null = null
let history: Snapshot[] = []
let cursor = -1
let stageSize = { w: 0, h: 0 }
let resizeObs: ResizeObserver | null = null
let dragStart: { x: number; y: number } | null = null

interface Snapshot {
  base: HTMLCanvasElement
  rotation: number
  flipH: boolean
  flipV: boolean
  brightness: number
  contrast: number
  saturation: number
  grayscale: boolean
  sepia: boolean
  invert: boolean
  scalePct: number
}

const dirty = computed(() =>
  rotation.value !== 0 || flipH.value || flipV.value ||
  brightness.value !== 100 || contrast.value !== 100 || saturation.value !== 100 ||
  grayscale.value || sepia.value || invert.value || scalePct.value !== 100)

const filterCss = computed(() => {
  const parts: string[] = []
  if (brightness.value !== 100) parts.push(`brightness(${brightness.value}%)`)
  if (contrast.value !== 100) parts.push(`contrast(${contrast.value}%)`)
  if (saturation.value !== 100) parts.push(`saturate(${saturation.value}%)`)
  if (grayscale.value) parts.push('grayscale(1)')
  if (sepia.value) parts.push('sepia(1)')
  if (invert.value) parts.push('invert(1)')
  return parts.join(' ') || 'none'
})

function snapshot(): Snapshot {
  return {
    base: base!, rotation: rotation.value, flipH: flipH.value, flipV: flipV.value,
    brightness: brightness.value, contrast: contrast.value, saturation: saturation.value,
    grayscale: grayscale.value, sepia: sepia.value, invert: invert.value, scalePct: scalePct.value,
  }
}

function pushHistory() {
  if (!base) return
  history = history.slice(0, cursor + 1)
  history.push(snapshot())
  if (history.length > 40) history.shift()
  cursor = history.length - 1
}

function restore(s: Snapshot) {
  base = s.base
  rotation.value = s.rotation
  flipH.value = s.flipH
  flipV.value = s.flipV
  brightness.value = s.brightness
  contrast.value = s.contrast
  saturation.value = s.saturation
  grayscale.value = s.grayscale
  sepia.value = s.sepia
  invert.value = s.invert
  scalePct.value = s.scalePct
  cropMode.value = false
  crop.active = false
  render()
}

function undo() { if (cursor > 0) { cursor--; restore(history[cursor]) } }
function redo() { if (cursor < history.length - 1) { cursor++; restore(history[cursor]) } }

function resetAll() {
  if (!original) return
  base = original
  rotation.value = 0; flipH.value = false; flipV.value = false
  brightness.value = 100; contrast.value = 100; saturation.value = 100
  grayscale.value = false; sepia.value = false; invert.value = false
  scalePct.value = 100
  cropMode.value = false; crop.active = false
  pushHistory()
  render()
}

function canvasSize() {
  if (!base) return { w: 1, h: 1, outW: 1, outH: 1 }
  const outW = rotation.value % 180 === 0 ? base.width : base.height
  const outH = rotation.value % 180 === 0 ? base.height : base.width
  const maxW = Math.max(200, (stageSize.w || 800) - 24)
  const maxH = Math.max(200, (stageSize.h || 600) - 24)
  const s = Math.min(maxW / outW, maxH / outH, 1)
  previewScale.value = s
  return { w: base.width, h: base.height, outW: Math.max(1, Math.round(outW * s)), outH: Math.max(1, Math.round(outH * s)) }
}

/** 把原图画到目标画布（含旋转/翻转/滤镜），按目标尺寸缩放。 */
function drawTo(ctx: CanvasRenderingContext2D, targetW: number, targetH: number) {
  if (!base) return
  const swap = rotation.value % 180 !== 0
  const baseW = swap ? base.height : base.width
  const baseH = swap ? base.width : base.height
  const scale = Math.min(targetW / baseW, targetH / baseH)
  const dw = base.width * scale
  const dh = base.height * scale
  ctx.save()
  ctx.filter = filterCss.value
  ctx.translate(targetW / 2, targetH / 2)
  ctx.rotate(rotation.value * Math.PI / 180)
  ctx.scale(flipH.value ? -1 : 1, flipV.value ? -1 : 1)
  ctx.drawImage(base, -dw / 2, -dh / 2, dw, dh)
  ctx.restore()
}

function render() {
  const el = canvas.value
  if (!el || !base) return
  const { outW, outH } = canvasSize()
  el.width = outW
  el.height = outH
  el.style.width = outW + 'px'
  el.style.height = outH + 'px'
  const ctx = el.getContext('2d')
  if (!ctx) return
  ctx.clearRect(0, 0, outW, outH)
  drawTo(ctx, outW, outH)
}

function rotateBy(d: number) {
  rotation.value = ((rotation.value + d) % 360 + 360) % 360
  pushHistory()
  render()
}

function flip(horizontal: boolean) {
  if (horizontal) flipH.value = !flipH.value
  else flipV.value = !flipV.value
  pushHistory()
  render()
}

function anyFilterChanged() {
  pushHistory()
  render()
}

function toggleCrop() {
  cropMode.value = !cropMode.value
  crop.active = false
}

function onCropDown(ev: PointerEvent) {
  if (!cropMode.value) return
  const el = canvas.value
  if (!el) return
  const rect = el.getBoundingClientRect()
  dragStart = { x: ev.clientX - rect.left, y: ev.clientY - rect.top }
  crop.x = dragStart.x; crop.y = dragStart.y; crop.w = 0; crop.h = 0; crop.active = true
  try { el.setPointerCapture(ev.pointerId) } catch { }
}

function onCropMove(ev: PointerEvent) {
  if (!cropMode.value || !dragStart || !crop.active || !canvas.value) return
  const rect = canvas.value.getBoundingClientRect()
  const x = clamp(ev.clientX - rect.left, 0, rect.width)
  const y = clamp(ev.clientY - rect.top, 0, rect.height)
  crop.x = Math.min(dragStart.x, x)
  crop.y = Math.min(dragStart.y, y)
  crop.w = Math.abs(x - dragStart.x)
  crop.h = Math.abs(y - dragStart.y)
}

function onCropUp(ev: PointerEvent) {
  if (!dragStart) return
  dragStart = null
  try { canvas.value?.releasePointerCapture(ev.pointerId) } catch { }
}

function applyCrop() {
  if (!base || !canvas.value || !crop.active || crop.w < 6 || crop.h < 6) {
    toast('请先在图片上拖出裁剪区域', 'warning')
    return
  }
  const s = previewScale.value || 1
  const resultW = rotation.value % 180 === 0 ? base.width : base.height
  const resultH = rotation.value % 180 === 0 ? base.height : base.width
  const full = document.createElement('canvas')
  full.width = resultW
  full.height = resultH
  const fctx = full.getContext('2d')
  if (!fctx) return
  drawTo(fctx, resultW, resultH)
  const cx = Math.round(crop.x / s), cy = Math.round(crop.y / s)
  const cw = Math.max(1, Math.round(crop.w / s)), ch = Math.max(1, Math.round(crop.h / s))
  const out = document.createElement('canvas')
  out.width = Math.min(cw, resultW - cx)
  out.height = Math.min(ch, resultH - cy)
  out.getContext('2d')?.drawImage(full, cx, cy, out.width, out.height, 0, 0, out.width, out.height)
  base = out
  rotation.value = 0
  flipH.value = false
  flipV.value = false
  brightness.value = 100
  contrast.value = 100
  saturation.value = 100
  grayscale.value = false
  sepia.value = false
  invert.value = false
  cropMode.value = false
  crop.active = false
  pushHistory()
  render()
  toast('裁剪已应用')
}

function buildOutput(): HTMLCanvasElement | null {
  if (!base) return null
  const scale = scalePct.value / 100
  const outW = Math.round((rotation.value % 180 === 0 ? base.width : base.height) * scale)
  const outH = Math.round((rotation.value % 180 === 0 ? base.height : base.width) * scale)
  const out = document.createElement('canvas')
  out.width = Math.max(1, outW)
  out.height = Math.max(1, outH)
  const ctx = out.getContext('2d')
  if (!ctx) return null
  drawTo(ctx, out.width, out.height)
  return out
}

async function save(mode: 'copy' | 'overwrite') {
  if (saving.value) return
  if (mode === 'overwrite' && !window.confirm('确定覆盖原图吗？此操作不可撤销。')) return
  const out = buildOutput()
  if (!out) return
  saving.value = true
  try {
    const dataUrl = out.toDataURL(format.value, 0.92)
    const res = await api.saveEditedImage(props.path, dataUrl, mode)
    if (!res.ok) throw new Error('保存失败')
    toast(mode === 'overwrite' ? '已覆盖原图' : '已保存副本：' + basename(res.path))
    emit('saved', res.path, mode)
    emit('close')
  } catch (e) { toastError(e) } finally { saving.value = false }
}

function onKey(ev: KeyboardEvent) {
  if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'z') { ev.preventDefault(); undo() }
  else if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'y') { ev.preventDefault(); redo() }
  else if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 's') { ev.preventDefault(); void save('copy') }
  else if (ev.key === 'Escape') { ev.preventDefault(); emit('close') }
}

async function loadImage() {
  loading.value = true
  error.value = ''
  try {
    const img = new Image()
    img.decoding = 'async'
    await new Promise<void>((resolve, reject) => {
      img.onload = () => resolve()
      img.onerror = () => reject(new Error('图片加载失败'))
      img.src = urls.image(props.path) + '&v=' + Date.now()
    })
    const c = document.createElement('canvas')
    c.width = img.naturalWidth
    c.height = img.naturalHeight
    c.getContext('2d')?.drawImage(img, 0, 0)
    base = c
    original = c
    history = []
    cursor = -1
    pushHistory()
    const lower = props.path.toLowerCase()
    format.value = lower.endsWith('.jpg') || lower.endsWith('.jpeg') ? 'image/jpeg' : 'image/png'
    render()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally { loading.value = false }
}

onMounted(async () => {
  window.addEventListener('keydown', onKey)
  resizeObs = new ResizeObserver(() => {
    if (!stage.value) return
    stageSize = { w: stage.value.clientWidth, h: stage.value.clientHeight }
    render()
  })
  if (stage.value) {
    resizeObs.observe(stage.value)
    stageSize = { w: stage.value.clientWidth, h: stage.value.clientHeight }
  }
  await loadImage()
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKey)
  resizeObs?.disconnect()
})
</script>

<template>
  <div class="image-editor">
    <header class="ie-bar">
      <div class="ie-title">
        <strong>图片编辑</strong>
        <span>{{ filename || basename(path) }}<template v-if="dirty"> · 已修改</template></span>
      </div>
      <div class="ie-actions">
        <n-button size="small" quaternary :disabled="cursor <= 0" @click="undo"><template #icon><undo2 :size="14" /></template>撤销</n-button>
        <n-button size="small" quaternary :disabled="cursor >= history.length - 1" @click="redo"><template #icon><redo2 :size="14" /></template>重做</n-button>
        <n-button size="small" quaternary @click="resetAll"><template #icon><rotate-ccw :size="14" /></template>重置</n-button>
        <n-button size="small" type="primary" :loading="saving" @click="save('copy')"><template #icon><save :size="14" /></template>保存副本</n-button>
        <n-button size="small" :loading="saving" @click="save('overwrite')"><template #icon><check :size="14" /></template>覆盖原图</n-button>
        <button class="ie-close" title="关闭" @click="emit('close')"><x :size="17" /></button>
      </div>
    </header>

    <div class="ie-body">
      <div ref="stage" class="ie-stage">
        <div v-if="loading" class="viewer-loading"><span>正在载入图片…</span></div>
        <div v-else-if="error" class="viewer-error">{{ error }}</div>
        <div v-show="!loading && !error" class="ie-canvas-wrap" :class="{ cropping: cropMode }">
          <canvas
            ref="canvas"
            @pointerdown="onCropDown"
            @pointermove="onCropMove"
            @pointerup="onCropUp"
            @pointercancel="onCropUp"
          />
          <div
            v-if="cropMode && crop.active && crop.w > 2 && crop.h > 2"
            class="crop-box"
            :style="{ left: crop.x + 'px', top: crop.y + 'px', width: crop.w + 'px', height: crop.h + 'px' }"
          />
        </div>
      </div>

      <aside class="ie-panel">
        <section>
          <h4>方向</h4>
          <div class="ie-row">
            <n-button size="small" @click="rotateBy(-90)"><template #icon><rotate-ccw :size="14" /></template>左转</n-button>
            <n-button size="small" @click="rotateBy(90)"><template #icon><rotate-cw :size="14" /></template>右转</n-button>
          </div>
          <div class="ie-row">
            <n-button size="small" :type="flipH ? 'primary' : 'default'" @click="flip(true)"><template #icon><flip-horizontal2 :size="14" /></template>水平翻转</n-button>
            <n-button size="small" :type="flipV ? 'primary' : 'default'" @click="flip(false)"><template #icon><flip-vertical2 :size="14" /></template>垂直翻转</n-button>
          </div>
        </section>

        <section>
          <h4>裁剪</h4>
          <div class="ie-row">
            <n-button size="small" :type="cropMode ? 'primary' : 'default'" @click="toggleCrop"><template #icon><crop :size="14" /></template>{{ cropMode ? '取消裁剪' : '开始裁剪' }}</n-button>
            <n-button size="small" :disabled="!cropMode" @click="applyCrop"><template #icon><check :size="14" /></template>应用</n-button>
          </div>
          <small>在图片上拖动框选区域，点“应用”完成裁剪。</small>
        </section>

        <section>
          <h4>调整</h4>
          <div class="ie-slider"><span>亮度</span><n-slider :value="brightness" :min="0" :max="200" :step="1" @update:value="(v:number)=>{brightness=v; render()}" @update:value-end="anyFilterChanged" /></div>
          <div class="ie-slider"><span>对比度</span><n-slider :value="contrast" :min="0" :max="200" :step="1" @update:value="(v:number)=>{contrast=v; render()}" @update:value-end="anyFilterChanged" /></div>
          <div class="ie-slider"><span>饱和度</span><n-slider :value="saturation" :min="0" :max="200" :step="1" @update:value="(v:number)=>{saturation=v; render()}" @update:value-end="anyFilterChanged" /></div>
          <div class="ie-slider"><span>缩放 %</span><n-slider :value="scalePct" :min="10" :max="200" :step="1" @update:value="(v:number)=>{scalePct=v}" @update:value-end="anyFilterChanged" /><b>{{ scalePct }}%</b></div>
          <div class="ie-switches">
            <label>黑白 <n-switch size="small" :value="grayscale" @update:value="(v:boolean)=>{grayscale=v; anyFilterChanged()}" /></label>
            <label>怀旧 <n-switch size="small" :value="sepia" @update:value="(v:boolean)=>{sepia=v; anyFilterChanged()}" /></label>
            <label>反色 <n-switch size="small" :value="invert" @update:value="(v:boolean)=>{invert=v; anyFilterChanged()}" /></label>
          </div>
        </section>

        <section>
          <h4>输出</h4>
          <n-select v-model:value="format" size="small" :options="[
            { label: 'PNG（无损）', value: 'image/png' },
            { label: 'JPEG（有损 92%）', value: 'image/jpeg' },
          ]" />
          <small>保存副本不会修改原图；覆盖原图会直接写回文件。</small>
        </section>
      </aside>
    </div>
  </div>
</template>

<style scoped>
.image-editor { position: absolute; inset: 0; z-index: 30; display: flex; flex-direction: column; background: #0b0f15; }
.ie-bar { flex: none; height: 48px; display: flex; align-items: center; justify-content: space-between; padding: 0 10px 0 14px; border-bottom: 1px solid rgba(255,255,255,.08); background: rgba(14,18,25,.98); }
.ie-title { display: flex; flex-direction: column; gap: 1px; font-size: 12px; color: #e6edf5; }
.ie-title span { font-size: 10px; color: rgba(255,255,255,.45); }
.ie-actions { display: flex; align-items: center; gap: 6px; }
.ie-close { width: 30px; height: 30px; border: 0; border-radius: 7px; background: transparent; color: #b9c3cf; cursor: pointer; display: grid; place-items: center; }
.ie-close:hover { background: rgba(255,255,255,.08); color: #fff; }
.ie-body { flex: 1; min-height: 0; display: flex; }
.ie-stage { flex: 1; min-width: 0; display: grid; place-items: center; overflow: auto; padding: 12px; background: #070a0f; }
.ie-canvas-wrap { position: relative; line-height: 0; }
.ie-canvas-wrap.cropping { cursor: crosshair; }
.ie-canvas-wrap canvas { display: block; max-width: none; }
.crop-box { position: absolute; border: 1px solid #58a6ff; box-shadow: 0 0 0 9999px rgba(0,0,0,.55); pointer-events: none; }
.ie-panel { flex: none; width: 260px; border-left: 1px solid rgba(255,255,255,.08); padding: 12px; overflow-y: auto; display: flex; flex-direction: column; gap: 16px; background: #0e131b; }
.ie-panel h4 { margin: 0 0 8px; font-size: 11px; font-weight: 600; color: rgba(255,255,255,.55); letter-spacing: .04em; }
.ie-panel section { display: flex; flex-direction: column; gap: 8px; }
.ie-panel small { font-size: 10px; line-height: 1.6; color: rgba(255,255,255,.35); }
.ie-row { display: flex; gap: 6px; flex-wrap: wrap; }
.ie-slider { display: grid; grid-template-columns: 46px 1fr 38px; align-items: center; gap: 8px; font-size: 11px; color: rgba(255,255,255,.55); }
.ie-slider b { font-size: 10px; text-align: right; color: rgba(255,255,255,.65); }
.ie-switches { display: flex; gap: 12px; font-size: 11px; color: rgba(255,255,255,.55); }
.ie-switches label { display: flex; align-items: center; gap: 6px; }
</style>
