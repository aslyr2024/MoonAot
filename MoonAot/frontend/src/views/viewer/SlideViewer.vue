<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { init } from 'pptx-preview'
import { Minus, Plus, RefreshCw, X } from 'lucide-vue-next'
import { urls } from '../../api'
import { host } from '../../host'
import { basename } from '../../utils'

const route = useRoute()
const path = ref(String(route.query.path || ''))
const loading = ref(true)
const error = ref('')
const zoom = ref(100)
const holder = ref<HTMLElement | null>(null)
let previewer: ReturnType<typeof init> | null = null

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await fetch(urls.media(path.value))
    if (!res.ok) throw new Error('读取文件失败：' + res.status)
    const buf = await res.arrayBuffer()
    if (holder.value) holder.value.innerHTML = ''
    if (!previewer && holder.value) previewer = init(holder.value, { width: 960, height: 540, mode: 'list' })
    await previewer?.preview(buf)
    host.setTitle(basename(path.value))
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

function zoomBy(d: number) { zoom.value = Math.min(250, Math.max(40, zoom.value + d)) }

onMounted(load)
onBeforeUnmount(() => { previewer = null })
</script>

<template>
  <div class="viewer slide-viewer">
    <header class="viewer-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="17" /></button>
        <div class="viewer-name">
          <strong :title="path">{{ basename(path) || '演示文稿' }}</strong>
          <span>PPTX · {{ zoom }}%</span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="刷新" @click="load"><refresh-cw :size="16" /></button>
        <button class="viewer-btn" title="缩小" @click="zoomBy(-10)"><minus :size="16" /></button>
        <button class="viewer-btn" title="放大" @click="zoomBy(10)"><plus :size="16" /></button>
      </div>
    </header>
    <div v-if="loading" class="viewer-loading"><span>正在解析演示文稿…</span></div>
    <div v-else-if="error" class="viewer-error">{{ error }}</div>
    <div class="slide-scroll">
      <div class="slide-zoom" :style="{ zoom: zoom / 100 }">
        <div ref="holder" class="slide-holder" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.slide-viewer { display: flex; flex-direction: column; height: 100%; background: #33363c; }
.slide-scroll { flex: 1; min-height: 0; overflow: auto; padding: 18px; }
.slide-zoom { width: 960px; margin: 0 auto; }
.slide-holder { display: flex; flex-direction: column; align-items: center; gap: 16px; }
.slide-holder :deep(.pptx-preview-wrapper) { background: transparent !important; }
.slide-holder :deep(.pptx-preview-slide-wrapper) { box-shadow: 0 6px 26px rgba(0,0,0,.4); margin-bottom: 6px; }
</style>
