<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import DxfParser from 'dxf-parser'
import { Maximize, Minus, Plus, RefreshCw, X } from 'lucide-vue-next'
import { urls } from '../../api'
import { host } from '../../host'
import { basename } from '../../utils'

const route = useRoute()
const path = ref(String(route.query.path || ''))
const loading = ref(true)
const error = ref('')
const stats = ref('')
const viewport = ref<HTMLElement | null>(null)

let renderer: THREE.WebGLRenderer | null = null
let scene: THREE.Scene | null = null
let camera: THREE.OrthographicCamera | null = null
let controls: OrbitControls | null = null
let raf = 0
let resizeObs: ResizeObserver | null = null
let allPoints: THREE.Vector3[] = []

function pushSeg(arr: number[], a: any, b: any) {
  arr.push(a.x || 0, a.y || 0, a.z || 0, b.x || 0, b.y || 0, b.z || 0)
}

function buildGeometry(entities: any[]): THREE.BufferGeometry {
  const arr: number[] = []
  const pts: THREE.Vector3[] = []
  const addPt = (x: number, y: number, z = 0) => pts.push(new THREE.Vector3(x, y, z))
  for (const e of entities) {
    const t = String(e.type || '').toUpperCase()
    if (t === 'LINE' && e.vertices?.length >= 2) {
      pushSeg(arr, e.vertices[0], e.vertices[1])
      addPt(e.vertices[0].x, e.vertices[0].y, e.vertices[0].z || 0)
      addPt(e.vertices[1].x, e.vertices[1].y, e.vertices[1].z || 0)
    } else if ((t === 'LWPOLYLINE' || t === 'POLYLINE') && e.vertices?.length >= 2) {
      for (let i = 0; i < e.vertices.length - 1; i++) pushSeg(arr, e.vertices[i], e.vertices[i + 1])
      if (e.shape || e.closed) pushSeg(arr, e.vertices[e.vertices.length - 1], e.vertices[0])
      for (const v of e.vertices) addPt(v.x, v.y, v.z || 0)
    } else if (t === 'CIRCLE' && e.center) {
      const r = e.radius || 0, n = 72
      for (let i = 0; i < n; i++) {
        const a1 = i / n * Math.PI * 2, a2 = (i + 1) / n * Math.PI * 2
        const p1 = { x: e.center.x + Math.cos(a1) * r, y: e.center.y + Math.sin(a1) * r }
        const p2 = { x: e.center.x + Math.cos(a2) * r, y: e.center.y + Math.sin(a2) * r }
        pushSeg(arr, p1, p2); addPt(p1.x, p1.y); addPt(p2.x, p2.y)
      }
    } else if (t === 'ARC' && e.center) {
      let a1 = e.startAngle || 0, a2 = e.endAngle || 0
      if (Math.abs(a2 - a1) > Math.PI * 3) { a1 = a1 * Math.PI / 180; a2 = a2 * Math.PI / 180 }
      if (a2 < a1) a2 += Math.PI * 2
      const r = e.radius || 0, n = 48
      for (let i = 0; i < n; i++) {
        const b1 = a1 + (a2 - a1) * i / n, b2 = a1 + (a2 - a1) * (i + 1) / n
        const p1 = { x: e.center.x + Math.cos(b1) * r, y: e.center.y + Math.sin(b1) * r }
        const p2 = { x: e.center.x + Math.cos(b2) * r, y: e.center.y + Math.sin(b2) * r }
        pushSeg(arr, p1, p2); addPt(p1.x, p1.y); addPt(p2.x, p2.y)
      }
    } else if (t === 'ELLIPSE' && e.center && e.majorAxisEndPoint) {
      const mx = e.majorAxisEndPoint.x, my = e.majorAxisEndPoint.y
      const rx = Math.hypot(mx, my), ry = rx * (e.axisRatio || 1)
      const rot = Math.atan2(my, mx)
      const a1 = e.startAngle || 0, a2 = e.endAngle ?? Math.PI * 2
      const n = 64
      for (let i = 0; i < n; i++) {
        const b1 = a1 + (a2 - a1) * i / n, b2 = a1 + (a2 - a1) * (i + 1) / n
        const p1 = { x: e.center.x + Math.cos(b1) * rx * Math.cos(rot) - Math.sin(b1) * ry * Math.sin(rot), y: e.center.y + Math.cos(b1) * rx * Math.sin(rot) + Math.sin(b1) * ry * Math.cos(rot) }
        const p2 = { x: e.center.x + Math.cos(b2) * rx * Math.cos(rot) - Math.sin(b2) * ry * Math.sin(rot), y: e.center.y + Math.cos(b2) * rx * Math.sin(rot) + Math.sin(b2) * ry * Math.cos(rot) }
        pushSeg(arr, p1, p2); addPt(p1.x, p1.y); addPt(p2.x, p2.y)
      }
    }
  }
  allPoints = pts
  const geo = new THREE.BufferGeometry()
  geo.setAttribute('position', new THREE.Float32BufferAttribute(arr, 3))
  return geo
}

function fitView() {
  if (!camera || !controls) return
  if (!allPoints.length) return
  const box = new THREE.Box3().setFromPoints(allPoints)
  const center = box.getCenter(new THREE.Vector3())
  const size = box.getSize(new THREE.Vector3())
  const maxDim = Math.max(size.x, size.y, 1)
  const aspect = (viewport.value?.clientWidth || 1) / (viewport.value?.clientHeight || 1)
  const half = maxDim * 0.62
  camera.left = -half * aspect
  camera.right = half * aspect
  camera.top = half
  camera.bottom = -half
  camera.near = -maxDim * 10
  camera.far = maxDim * 10
  camera.zoom = 1
  camera.position.set(center.x, center.y, maxDim * 2)
  camera.up.set(0, 1, 0)
  camera.lookAt(center.x, center.y, 0)
  camera.updateProjectionMatrix()
  controls.target.set(center.x, center.y, 0)
  controls.update()
  stats.value = `${size.x.toFixed(1)} × ${size.y.toFixed(1)} · ${allPoints.length} 点`
}

function zoomBy(f: number) {
  if (!camera) return
  camera.zoom = Math.min(50, Math.max(0.02, camera.zoom * f))
  camera.updateProjectionMatrix()
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await fetch(urls.media(path.value))
    if (!res.ok) throw new Error('读取文件失败：' + res.status)
    const text = await res.text()
    const dxf = new DxfParser().parseSync(text) as any
    const entities = dxf?.entities || []
    if (!entities.length) throw new Error('DXF 中没有可显示的实体')
    if (scene) {
      const old = scene.getObjectByName('cad-lines')
      if (old) {
        scene.remove(old)
        const g = (old as THREE.LineSegments).geometry
        g.dispose()
      }
      const geo = buildGeometry(entities)
      const mat = new THREE.LineBasicMaterial({ color: 0xa8d8ff })
      const lines = new THREE.LineSegments(geo, mat)
      lines.name = 'cad-lines'
      scene.add(lines)
    }
    fitView()
    host.setTitle(basename(path.value))
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

function initThree() {
  if (!viewport.value) return
  scene = new THREE.Scene()
  scene.background = new THREE.Color(0x0a0f16)
  camera = new THREE.OrthographicCamera(-1, 1, 1, -1, -1000, 1000)
  renderer = new THREE.WebGLRenderer({ antialias: true })
  renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1))
  viewport.value.appendChild(renderer.domElement)
  controls = new OrbitControls(camera, renderer.domElement)
  controls.enableDamping = true
  controls.dampingFactor = 0.1
  controls.screenSpacePanning = true
  resizeObs = new ResizeObserver(resize)
  resizeObs.observe(viewport.value)
  resize()
  const loop = () => {
    raf = requestAnimationFrame(loop)
    controls?.update()
    if (renderer && scene && camera) renderer.render(scene, camera)
  }
  loop()
}

function resize() {
  if (!viewport.value || !renderer || !camera) return
  const w = viewport.value.clientWidth || 1
  const h = viewport.value.clientHeight || 1
  renderer.setSize(w, h, false)
  const aspect = w / h
  const halfH = (camera.top - camera.bottom) / 2 || 1
  camera.left = -halfH * aspect
  camera.right = halfH * aspect
  camera.updateProjectionMatrix()
}

onMounted(async () => { initThree(); await load() })
onBeforeUnmount(() => {
  cancelAnimationFrame(raf)
  resizeObs?.disconnect()
  controls?.dispose()
  if (scene) scene.traverse(o => { const m = o as THREE.Mesh; m.geometry?.dispose() })
  renderer?.dispose()
  if (renderer?.domElement.parentElement) renderer.domElement.parentElement.removeChild(renderer.domElement)
  renderer = null; scene = null; camera = null
})
</script>

<template>
  <div class="viewer cad-viewer">
    <header class="viewer-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="17" /></button>
        <div class="viewer-name">
          <strong :title="path">{{ basename(path) || 'CAD 图纸' }}</strong>
          <span>DXF · {{ stats || '二维图纸' }}</span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="刷新" @click="load"><refresh-cw :size="16" /></button>
        <button class="viewer-btn" title="缩小" @click="zoomBy(0.8)"><minus :size="16" /></button>
        <button class="viewer-btn" title="放大" @click="zoomBy(1.25)"><plus :size="16" /></button>
        <button class="viewer-btn" title="适应窗口" @click="fitView"><maximize :size="16" /></button>
      </div>
    </header>
    <div v-if="loading" class="viewer-loading"><span>正在解析 DXF 图纸…</span></div>
    <div v-else-if="error" class="viewer-error">{{ error }}</div>
    <div ref="viewport" class="cad-viewport" :class="{ hidden: loading || !!error }" />
  </div>
</template>

<style scoped>
.cad-viewer { display: flex; flex-direction: column; height: 100%; background: #0a0f16; }
.cad-viewport { flex: 1; min-height: 0; position: relative; }
.cad-viewport :deep(canvas) { display: block; width: 100%; height: 100%; }
.cad-viewport.hidden { visibility: hidden; position: absolute; }
</style>
