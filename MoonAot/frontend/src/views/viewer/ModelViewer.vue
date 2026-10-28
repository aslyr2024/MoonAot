<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js'
import { OBJLoader } from 'three/examples/jsm/loaders/OBJLoader.js'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js'
import { STLLoader } from 'three/examples/jsm/loaders/STLLoader.js'
import { ColladaLoader } from 'three/examples/jsm/loaders/ColladaLoader.js'
import { PLYLoader } from 'three/examples/jsm/loaders/PLYLoader.js'
import { TDSLoader } from 'three/examples/jsm/loaders/TDSLoader.js'
import { ThreeMFLoader } from 'three/examples/jsm/loaders/3MFLoader.js'
import { Box, RefreshCw, RotateCw, X } from 'lucide-vue-next'
import { urls } from '../../api'
import { host } from '../../host'
import { basename } from '../../utils'

const route = useRoute()
const path = ref(String(route.query.path || ''))
const loading = ref(true)
const error = ref('')
const stats = ref('')
const autoRotate = ref(true)
const wireframe = ref(false)
const viewport = ref<HTMLElement | null>(null)

let renderer: THREE.WebGLRenderer | null = null
let scene: THREE.Scene | null = null
let camera: THREE.PerspectiveCamera | null = null
let controls: OrbitControls | null = null
let root: THREE.Object3D | null = null
let raf = 0
let resizeObs: ResizeObserver | null = null

const materials: THREE.MeshStandardMaterial[] = []

function fitView() {
  if (!root || !camera || !controls) return
  const box = new THREE.Box3().setFromObject(root)
  if (box.isEmpty()) return
  const size = box.getSize(new THREE.Vector3())
  const center = box.getCenter(new THREE.Vector3())
  const maxDim = Math.max(size.x, size.y, size.z) || 1
  const dist = maxDim * 1.8
  camera.position.set(center.x + dist, center.y + dist * 0.7, center.z + dist)
  camera.near = maxDim / 1000
  camera.far = maxDim * 100
  camera.updateProjectionMatrix()
  controls.target.copy(center)
  controls.update()
  stats.value = `${size.x.toFixed(1)} × ${size.y.toFixed(1)} × ${size.z.toFixed(1)}`
}

function applyWireframe() {
  for (const m of materials) { m.wireframe = wireframe.value; m.needsUpdate = true }
}

async function parseModel(buf: ArrayBuffer, text: string): Promise<THREE.Object3D> {
  const ext = path.value.toLowerCase().slice(path.value.lastIndexOf('.'))
  if (ext === '.fbx') return new FBXLoader().parse(buf, '')
  if (ext === '.obj') return new OBJLoader().parse(text)
  if (ext === '.glb' || ext === '.gltf') {
    return await new Promise((resolve, reject) => {
      new GLTFLoader().parse(buf, '', g => resolve(g.scene), e => reject(e))
    })
  }
  if (ext === '.stl') {
    const geo = new STLLoader().parse(buf)
    const mesh = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ color: 0x9fb3c8, metalness: 0.15, roughness: 0.55, flatShading: false }))
    return mesh
  }
  if (ext === '.dae') return new ColladaLoader().parse(text, '').scene
  if (ext === '.ply') {
    const geo = new PLYLoader().parse(buf)
    geo.computeVertexNormals()
    return new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ color: 0xa8b8c8, roughness: 0.6 }))
  }
  if (ext === '.3ds') return new TDSLoader().parse(buf, '')
  if (ext === '.3mf') return new ThreeMFLoader().parse(buf)
  throw new Error('暂不支持该 3D 格式：' + ext)
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await fetch(urls.media(path.value))
    if (!res.ok) throw new Error('读取文件失败：' + res.status)
    const buf = await res.arrayBuffer()
    const lower = path.value.toLowerCase()
    const text = lower.endsWith('.obj') || lower.endsWith('.dae') || lower.endsWith('.gltf')
      ? new TextDecoder('utf-8').decode(buf)
      : ''
    const obj = await parseModel(buf, text)
    if (root && scene) { scene.remove(root); disposeObj(root) }
    root = obj
    scene?.add(obj)
    obj.traverse(o => {
      const mesh = o as THREE.Mesh
      if (mesh.isMesh && mesh.material) {
        const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material]
        for (const m of mats) {
          if (m instanceof THREE.MeshStandardMaterial) { m.wireframe = wireframe.value; materials.push(m) }
        }
      }
    })
    if (!obj.children.length && !(obj as THREE.Mesh).isMesh) throw new Error('模型为空')
    fitView()
    host.setTitle(basename(path.value))
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

function disposeObj(o: THREE.Object3D) {
  o.traverse(n => {
    const m = n as THREE.Mesh
    if (m.geometry) m.geometry.dispose()
  })
}

function initThree() {
  if (!viewport.value) return
  scene = new THREE.Scene()
  scene.background = new THREE.Color(0x0e131a)
  camera = new THREE.PerspectiveCamera(50, 1, 0.1, 5000)
  camera.position.set(3, 3, 3)
  renderer = new THREE.WebGLRenderer({ antialias: true })
  renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1))
  viewport.value.appendChild(renderer.domElement)
  scene.add(new THREE.HemisphereLight(0xffffff, 0x223344, 1.5))
  const dir = new THREE.DirectionalLight(0xffffff, 2.2)
  dir.position.set(5, 10, 7)
  scene.add(dir)
  const dir2 = new THREE.DirectionalLight(0x88aaff, 1.0)
  dir2.position.set(-6, -4, -5)
  scene.add(dir2)
  const grid = new THREE.GridHelper(20, 20, 0x334155, 0x1e293b)
  grid.position.y = -0.01
  scene.add(grid)
  controls = new OrbitControls(camera, renderer.domElement)
  controls.enableDamping = true
  controls.dampingFactor = 0.08
  controls.autoRotate = autoRotate.value
  controls.autoRotateSpeed = 1.2
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
  camera.aspect = w / h
  camera.updateProjectionMatrix()
}

function setAutoRotate(v: boolean) {
  autoRotate.value = v
  if (controls) controls.autoRotate = v
}

function toggleWireframe() {
  wireframe.value = !wireframe.value
  applyWireframe()
}

onMounted(async () => {
  initThree()
  await load()
})

onBeforeUnmount(() => {
  cancelAnimationFrame(raf)
  resizeObs?.disconnect()
  controls?.dispose()
  if (root) disposeObj(root)
  renderer?.dispose()
  if (renderer?.domElement.parentElement) renderer.domElement.parentElement.removeChild(renderer.domElement)
  renderer = null
  scene = null
  camera = null
})
</script>

<template>
  <div class="viewer model-viewer">
    <header class="viewer-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="17" /></button>
        <div class="viewer-name">
          <strong :title="path">{{ basename(path) || '3D 模型' }}</strong>
          <span><box :size="12" /> {{ stats || '3D / CAD 模型' }}</span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="刷新" @click="load"><refresh-cw :size="16" /></button>
        <button class="viewer-btn" :class="{ active: autoRotate }" title="自动旋转" @click="setAutoRotate(!autoRotate)"><rotate-cw :size="16" /></button>
        <button class="viewer-btn" :class="{ active: wireframe }" title="线框模式" @click="toggleWireframe"><box :size="16" /></button>
      </div>
    </header>
    <div v-if="loading" class="viewer-loading"><span>正在加载 3D 模型…</span></div>
    <div v-else-if="error" class="viewer-error">{{ error }}</div>
    <div ref="viewport" class="model-viewport" :class="{ hidden: loading || !!error }" />
  </div>
</template>

<style scoped>
.model-viewer { display: flex; flex-direction: column; height: 100%; background: #0e131a; }
.model-viewport { flex: 1; min-height: 0; position: relative; }
.model-viewport :deep(canvas) { display: block; width: 100%; height: 100%; }
.model-viewport.hidden { visibility: hidden; position: absolute; }
</style>
