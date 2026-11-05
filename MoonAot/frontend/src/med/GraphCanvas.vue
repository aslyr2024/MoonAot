<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, watch, nextTick } from 'vue'
import type { MedGraphNode, MedGraphEdge } from './api'

const props = defineProps<{ nodes: MedGraphNode[]; edges: MedGraphEdge[] }>()
const emit = defineEmits<{ select: [id: string] }>()
const host = ref<HTMLDivElement | null>(null)
const canvas = ref<HTMLCanvasElement | null>(null)
const hover = ref('')
let raf = 0
let ro: ResizeObserver | null = null
type P = { id: string; x: number; y: number; vx: number; vy: number; r: number; label: string; type: string }
let pts: P[] = []
let links: { a: number; b: number; w: number }[] = []

const TYPE_COLORS: Record<string, string> = {
  disease: '#ff7b8a', symptom: '#ffb454', drug: '#6ea8fe', lab: '#3ddc97',
  procedure: '#a57bff', guideline: '#59d0d0', document: '#9aa7b8', template: '#9aa7b8', leaflet: '#5ec8f0',
}

function rebuild() {
  const w = host.value?.clientWidth || 700
  const h = host.value?.clientHeight || 420
  const n = props.nodes.length
  pts = props.nodes.map((node, i) => {
    const a = (i / Math.max(1, n)) * Math.PI * 2
    const rad = Math.min(w, h) * (0.16 + (i % 5) * 0.045)
    return {
      id: node.id, x: w / 2 + Math.cos(a) * rad + (Math.random() - .5) * 20,
      y: h / 2 + Math.sin(a) * rad + (Math.random() - .5) * 20,
      vx: 0, vy: 0, r: 5 + Math.min(10, Math.sqrt(node.weight) * 1.5),
      label: node.label, type: node.type,
    }
  })
  const idx = new Map(pts.map((p, i) => [p.id, i]))
  links = props.edges.map(e => ({ a: idx.get(e.source) ?? -1, b: idx.get(e.target) ?? -1, w: e.weight }))
    .filter(l => l.a >= 0 && l.b >= 0)
}

function step(w: number, h: number) {
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i]
    for (let j = i + 1; j < pts.length; j++) {
      const q = pts[j]
      let dx = p.x - q.x, dy = p.y - q.y
      let d2 = dx * dx + dy * dy + 12
      const f = 2400 / d2
      const d = Math.sqrt(d2)
      p.vx += (dx / d) * f; p.vy += (dy / d) * f
      q.vx -= (dx / d) * f; q.vy -= (dy / d) * f
    }
  }
  for (const l of links) {
    const p = pts[l.a], q = pts[l.b]
    const dx = q.x - p.x, dy = q.y - p.y
    const d = Math.sqrt(dx * dx + dy * dy) + .01
    const target = 80 + Math.min(60, l.w * 2)
    const f = (d - target) * 0.0018
    p.vx += (dx / d) * f * d * .06; p.vy += (dy / d) * f * d * .06
    q.vx -= (dx / d) * f * d * .06; q.vy -= (dy / d) * f * d * .06
  }
  for (const p of pts) {
    p.vx += (w / 2 - p.x) * 0.0009; p.vy += (h / 2 - p.y) * 0.0009
    p.vx *= .86; p.vy *= .86
    p.x = Math.max(p.r + 4, Math.min(w - p.r - 4, p.x + p.vx))
    p.y = Math.max(p.r + 4, Math.min(h - p.r - 4, p.y + p.vy))
  }
}

function draw() {
  const cv = canvas.value
  if (!cv || !host.value) return
  const dpr = window.devicePixelRatio || 1
  const w = host.value.clientWidth, h = host.value.clientHeight
  if (cv.width !== Math.round(w * dpr) || cv.height !== Math.round(h * dpr)) {
    cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr)
    cv.style.width = w + 'px'; cv.style.height = h + 'px'
  }
  const g = cv.getContext('2d')
  if (!g) return
  g.setTransform(dpr, 0, 0, dpr, 0, 0)
  g.clearRect(0, 0, w, h)
  g.strokeStyle = 'rgba(150,170,200,.16)'; g.lineWidth = .7
  for (const l of links) {
    const p = pts[l.a], q = pts[l.b]
    g.beginPath(); g.moveTo(p.x, p.y); g.lineTo(q.x, q.y); g.stroke()
  }
  for (const p of pts) {
    const active = hover.value === p.id
    g.fillStyle = TYPE_COLORS[p.type] || '#6ea8fe'
    g.globalAlpha = active ? 1 : .82
    g.beginPath(); g.arc(p.x, p.y, p.r + (active ? 2 : 0), 0, Math.PI * 2); g.fill()
    g.globalAlpha = 1
    g.fillStyle = getComputedStyle(document.body).getPropertyValue('--text-2').trim() || '#aab2c2'
    g.font = '11px "Microsoft YaHei UI", sans-serif'
    g.textAlign = 'center'
    g.fillText(p.label.length > 12 ? p.label.slice(0, 11) + '…' : p.label, p.x, p.y + p.r + 13)
  }
}

function loop() {
  const w = host.value?.clientWidth || 700, h = host.value?.clientHeight || 420
  step(w, h); draw()
  raf = requestAnimationFrame(loop)
}

function onClick(e: MouseEvent) {
  const rect = canvas.value?.getBoundingClientRect()
  if (!rect) return
  const x = e.clientX - rect.left, y = e.clientY - rect.top
  const p = pts.find(p => (p.x - x) ** 2 + (p.y - y) ** 2 < (p.r + 6) ** 2)
  if (p) emit('select', p.id)
}
function onMove(e: MouseEvent) {
  const rect = canvas.value?.getBoundingClientRect()
  if (!rect) return
  const x = e.clientX - rect.left, y = e.clientY - rect.top
  const p = pts.find(p => (p.x - x) ** 2 + (p.y - y) ** 2 < (p.r + 6) ** 2)
  hover.value = p?.id || ''
  if (canvas.value) canvas.value.style.cursor = p ? 'pointer' : 'default'
}

onMounted(async () => {
  await nextTick()
  rebuild()
  ro = new ResizeObserver(() => { rebuild() })
  if (host.value) ro.observe(host.value)
  loop()
})
onBeforeUnmount(() => { cancelAnimationFrame(raf); ro?.disconnect() })
watch(() => [props.nodes, props.edges], () => rebuild())
</script>

<template>
  <div ref="host" class="med-graph-canvas">
    <canvas ref="canvas" @click="onClick" @mousemove="onMove"></canvas>
    <div v-if="!nodes.length" class="med-graph-empty">选择一个概念后展开关系图谱</div>
  </div>
</template>

<style scoped>
.med-graph-canvas { position: relative; width: 100%; height: 100%; min-height: 340px; }
canvas { display: block; }
.med-graph-empty { position: absolute; inset: 0; display: grid; place-items: center; color: var(--text-3); font-size: 12px; pointer-events: none; }
</style>
