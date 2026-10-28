<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { BookMarked, FolderOpen, RefreshCw, X } from 'lucide-vue-next'
import { NButton } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import { host } from '../host'
import { toast, toastError } from '../ui'

const frameKey = ref(0)
const info = ref<any>(null)
const busy = ref(false)

const zimName = computed(() => info.value?.open ? (info.value.name || info.value.fileName) : '未打开 ZIM')

async function loadInfo() {
  try {
    const res = await fetch('/zimapi/info')
    info.value = await res.json()
  } catch { }
}

async function openZim() {
  const file = await host.pickFile('any', '选择百科文件（.zim，如离线维基百科）')
  if (!file) return
  busy.value = true
  try {
    const res = await fetch('/zimapi/open', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path: file }),
    }).then(r => r.json())
    if (!res.ok) { toast(res.error || '打开失败', 'error'); return }
    await loadInfo()
    frameKey.value++
    toast('百科已打开')
  } catch (e) { toastError(e) } finally { busy.value = false }
}

async function closeZim() {
  try { await fetch('/zimapi/close'); await loadInfo(); frameKey.value++; toast('已关闭') } catch (e) { toastError(e) }
}

function reload() { frameKey.value++ }

onMounted(loadInfo)
</script>

<template>
  <div class="page wiki-page">
    <PageHeader title="维基百科" :subtitle="`离线 ZIM 百科 · ${zimName}`">
      <n-button type="primary" :loading="busy" @click="openZim">
        <template #icon><folder-open :size="15" /></template>打开 ZIM 文件
      </n-button>
      <n-button :disabled="!info?.open" @click="closeZim"><template #icon><x :size="15" /></template>关闭</n-button>
      <n-button @click="reload"><template #icon><refresh-cw :size="15" /></template></n-button>
    </PageHeader>
    <div class="wiki-frame-wrap">
      <iframe :key="frameKey" class="wiki-frame" src="/wiki/" title="维基百科" />
    </div>
  </div>
</template>

<style scoped>
.wiki-page { display: flex; flex-direction: column; min-height: 0; padding: 14px 18px 18px; gap: 10px; }
.wiki-frame-wrap { flex: 1; min-height: 0; border: 1px solid var(--border); border-radius: 12px; overflow: hidden; background: #fff; }
.wiki-frame { width: 100%; height: 100%; border: 0; display: block; }
</style>
