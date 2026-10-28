<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useRoute } from 'vue-router'
import ImageViewer from './viewer/ImageViewer.vue'
import MusicPlayer from './viewer/MusicPlayer.vue'
import VideoPlayer from './viewer/VideoPlayer.vue'
import ZipBrowser from './viewer/ZipBrowser.vue'
import { host } from '../host'

const CodeEditor = defineAsyncComponent(() => import('./viewer/CodeEditor.vue'))
const DocViewer = defineAsyncComponent(() => import('./viewer/DocViewer.vue'))
const SheetViewer = defineAsyncComponent(() => import('./viewer/SheetViewer.vue'))
const SlideViewer = defineAsyncComponent(() => import('./viewer/SlideViewer.vue'))
const ModelViewer = defineAsyncComponent(() => import('./viewer/ModelViewer.vue'))
const CadViewer = defineAsyncComponent(() => import('./viewer/CadViewer.vue'))

const route = useRoute()
const kind = computed(() => String(route.params.kind || ''))
const comp = computed(() => {
  if (kind.value === 'image') return ImageViewer
  if (kind.value === 'music' || kind.value === 'audio') return MusicPlayer
  if (kind.value === 'video') return VideoPlayer
  if (kind.value === 'zip') return ZipBrowser
  if (kind.value === 'code') return CodeEditor
  if (kind.value === 'doc') return DocViewer
  if (kind.value === 'sheet') return SheetViewer
  if (kind.value === 'slide') return SlideViewer
  if (kind.value === 'model') return ModelViewer
  if (kind.value === 'cad') return CadViewer
  return null
})
</script>

<template>
  <component :is="comp" v-if="comp" />
  <div v-else class="viewer-missing">
    <p>未知的播放器类型：{{ kind }}</p>
    <button class="viewer-btn" @click="host.closeWindow()">关闭</button>
  </div>
</template>
