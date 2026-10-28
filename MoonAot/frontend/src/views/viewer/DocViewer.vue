<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { renderAsync } from 'docx-preview'
import { Minus, Plus, RefreshCw, X } from 'lucide-vue-next'
import { urls } from '../../api'
import { host } from '../../host'
import { basename } from '../../utils'

const route = useRoute()
const path = ref(String(route.query.path || ''))
const loading = ref(true)
const error = ref('')
const zoom = ref(100)
const wrap = ref<HTMLElement | null>(null)
const body = ref<HTMLElement | null>(null)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await fetch(urls.media(path.value))
    if (!res.ok) throw new Error('读取文件失败：' + res.status)
    const buf = await res.arrayBuffer()
    if (!body.value) throw new Error('容器未就绪')
    body.value.innerHTML = ''
    await renderAsync(buf, body.value, undefined, {
      className: 'docx',
      inWrapper: true,
      breakPages: true,
      ignoreWidth: false,
      ignoreHeight: false,
      renderHeaders: true,
      renderFooters: true,
      renderFootnotes: true,
      useBase64URL: true,
    })
    host.setTitle(basename(path.value))
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

function zoomBy(d: number) { zoom.value = Math.min(300, Math.max(40, zoom.value + d)) }

onMounted(load)
</script>

<template>
  <div class="viewer doc-viewer">
    <header class="viewer-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="17" /></button>
        <div class="viewer-name">
          <strong :title="path">{{ basename(path) || 'Word 文档' }}</strong>
          <span>DOCX · {{ zoom }}%</span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="刷新" @click="load"><refresh-cw :size="16" /></button>
        <button class="viewer-btn" title="缩小" @click="zoomBy(-10)"><minus :size="16" /></button>
        <button class="viewer-btn" title="放大" @click="zoomBy(10)"><plus :size="16" /></button>
      </div>
    </header>
    <div v-if="loading" class="viewer-loading"><span>正在解析 Word 文档…</span></div>
    <div v-else-if="error" class="viewer-error">{{ error }}</div>
    <div ref="wrap" class="doc-scroll">
      <div ref="body" class="doc-body" :style="{ zoom: zoom / 100 }" />
    </div>
  </div>
</template>

<style scoped>
.doc-viewer { display: flex; flex-direction: column; height: 100%; background: #3a3d44; }
.doc-scroll { flex: 1; min-height: 0; overflow: auto; padding: 20px 0 40px; }
.doc-body { display: flex; flex-direction: column; align-items: center; }
.doc-body :deep(.docx-wrapper) { background: transparent; padding: 0; display: flex; flex-direction: column; align-items: center; gap: 18px; }
.doc-body :deep(.docx) { box-shadow: 0 6px 24px rgba(0,0,0,.35); margin: 0 auto; }
.doc-body :deep(.docx-wrapper > section.docx) { background: #fff; }
</style>
