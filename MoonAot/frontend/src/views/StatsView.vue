<script setup lang="ts">
defineOptions({ name: 'StatsView' })

import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import {
  BarChart3, BookOpen, CheckCircle2, Database, Eraser, FolderOpen, HardDrive, Heart,
  ImagePlus, NotebookPen, RefreshCw, Timer,
} from 'lucide-vue-next'
import { NButton, NProgress } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import { api } from '../api'
import { loadStats, scheduleLibraryRefresh, state } from '../store'
import { host } from '../host'
import { confirmDialog, toast, toastError } from '../ui'
import type { JobStatus, StatsDto } from '../types'
import { formatBytes, formatDuration } from '../utils'

const stats = ref<StatsDto | null>(null)
const loading = ref(true)
const job = ref<JobStatus | null>(null)
let timer: ReturnType<typeof setTimeout> | null = null

async function load() {
  loading.value = true
  const s = await loadStats()
  stats.value = s
  loading.value = false
}

const cards = computed(() => {
  const s = stats.value
  if (!s) return []
  return [
    { label: '总藏书', value: s.totalBooks.toLocaleString(), icon: BookOpen, color: 'blue' },
    { label: '已读完', value: s.finishedBooks.toLocaleString(), icon: CheckCircle2, color: 'green' },
    { label: '在读', value: s.readingBooks.toLocaleString(), icon: Timer, color: 'orange' },
    { label: '阅读时长', value: formatDuration(s.readingSeconds), icon: Timer, color: 'violet' },
    { label: '笔记与标注', value: (s.notes ?? 0).toLocaleString(), icon: NotebookPen, color: 'yellow' },
    { label: '收藏', value: (s.favorites ?? 0).toLocaleString(), icon: Heart, color: 'red' },
    { label: '封面缓存', value: `${s.covers} 张 · ${formatBytes(s.coversBytes)}`, icon: ImagePlus, color: 'teal' },
    { label: '数据占用', value: formatBytes((s.storageBytes ?? 0) + (s.coversBytes ?? 0)), icon: HardDrive, color: 'gray' },
  ]
})

const maxDay = computed(() => Math.max(1, ...(stats.value?.recentDays ?? []).map(d => d.count)))
const maxFormat = computed(() => Math.max(1, ...(stats.value?.formats ?? []).map(f => f.count)))
const totalFormats = computed(() => (stats.value?.formats ?? []).reduce((n, f) => n + f.count, 0) || 1)

async function runJob(kind: 'covers' | 'purge' | 'metadata') {
  if (job.value?.running) return
  if (kind === 'purge') {
    const ok = await confirmDialog({
      title: '清理失效记录',
      content: '将检查全部书籍文件，并移除文件已不存在的书架记录。只删除数据库记录，不会删除任何文件。11 万本书可能需要数秒。',
      positiveText: '开始清理', danger: true,
    })
    if (!ok) return
  }
  try {
    job.value = kind === 'covers' ? await api.rebuildCovers()
      : kind === 'purge' ? await api.purgeMissing()
      : await api.repairMetadata()
    toast(kind === 'covers' ? '已开始重建封面' : kind === 'purge' ? '已开始清理失效记录' : '已开始补齐元数据')
    pollJob()
  } catch (e) { toastError(e) }
}

function pollJob() {
  if (!job.value) return
  if (timer) clearTimeout(timer)
  timer = setTimeout(async () => {
    try {
      if (!job.value) return
      const j = await api.job(job.value.id)
      job.value = j
      if (j.running) pollJob()
      else {
        toast(j.message || '任务完成')
        await load()
        scheduleLibraryRefresh(100)
      }
    } catch {
      timer = null
    }
  }, 800)
}

const jobPct = computed(() => {
  const j = job.value
  if (!j) return 0
  if (!j.total) return 5
  return Math.max(2, Math.min(100, Math.round((j.done / j.total) * 100)))
})

onMounted(async () => {
  await load()
  try {
    const latest = await api.latestJob('covers')
    if (latest?.running) { job.value = latest; pollJob() }
  } catch { }
})

onBeforeUnmount(() => { if (timer) clearTimeout(timer) })
</script>

<template>
  <div class="page stats-page">
    <PageHeader title="阅读统计" :subtitle="'所有数据均来自本地 SQLite 数据库'">
      <n-button @click="load"><template #icon><refresh-cw :size="15" /></template>刷新</n-button>
      <n-button @click="host.openDataFolder()"><template #icon><folder-open :size="15" /></template>数据目录</n-button>
    </PageHeader>

    <div class="page-scroll">
      <div v-if="loading" class="recent-loading">正在统计…</div>
      <template v-else-if="stats">
        <div class="stat-cards">
          <div v-for="c in cards" :key="c.label" class="stat-card" :class="'tone-' + c.color">
            <div class="stat-icon"><component :is="c.icon" :size="19" /></div>
            <div class="stat-body">
              <span>{{ c.label }}</span>
              <strong>{{ c.value }}</strong>
            </div>
          </div>
        </div>

        <div class="stats-split">
          <section class="panel">
            <div class="panel-head">
              <h2><bar-chart-3 :size="15" />最近 14 天阅读</h2>
              <span>单位：分钟</span>
            </div>
            <div class="day-chart">
              <div v-for="d in stats.recentDays" :key="d.name" class="day-col" :title="`${d.name}：${d.count} 分钟`">
                <div class="day-bar-wrap">
                  <i :style="{ height: Math.max(3, (d.count / maxDay) * 100) + '%' }" />
                </div>
                <span>{{ d.name.slice(5) }}</span>
              </div>
              <div v-if="!stats.recentDays.length" class="panel-empty">还没有累计阅读时长</div>
            </div>
          </section>

          <section class="panel">
            <div class="panel-head">
              <h2><book-open :size="15" />藏书格式</h2>
              <span>{{ totalFormats.toLocaleString() }} 本</span>
            </div>
            <div class="format-bars">
              <div v-for="f in stats.formats" :key="f.name" class="format-row">
                <span class="fmt-name">{{ f.name }}</span>
                <div class="fmt-bar"><i :style="{ width: Math.max(2, (f.count / maxFormat) * 100) + '%' }" /></div>
                <span class="fmt-count">{{ f.count.toLocaleString() }}</span>
              </div>
            </div>
          </section>
        </div>

        <section class="panel maintenance">
          <div class="panel-head">
            <h2><database :size="15" />数据维护</h2>
            <span>SQLite · data\moonaot.db</span>
          </div>
          <div class="maintain-row">
            <div class="maintain-info">
              <strong>重建封面缩略图</strong>
              <p>为 EPUB / MOBI / PDF 重新提取封面：封面压到 ≤48KB，缩略图 320×480 且 ≤40KB；TXT 保持渐变书脊占位。</p>
            </div>
            <n-button :disabled="!!job?.running" @click="runJob('covers')">
              <template #icon><image-plus :size="15" /></template>重建封面
            </n-button>
          </div>
          <div class="maintain-row">
            <div class="maintain-info">
              <strong>补齐书籍元数据</strong>
              <p>从 EPUB / MOBI / PDF 重新读取作者、出版社、简介；只填空字段，不覆盖已有内容。</p>
            </div>
            <n-button :disabled="!!job?.running" @click="runJob('metadata')">
              <template #icon><book-open :size="15" /></template>补齐元数据
            </n-button>
          </div>
          <div class="maintain-row">
            <div class="maintain-info">
              <strong>清理失效记录</strong>
              <p>扫描所有书籍路径，移除文件已不存在的记录（不会删除原文件）。</p>
            </div>
            <n-button :disabled="!!job?.running" type="error" ghost @click="runJob('purge')">
              <template #icon><eraser :size="15" /></template>清理失效
            </n-button>
          </div>
          <div v-if="job" class="job-box">
            <div class="job-head">
              <span>{{ job.running ? '任务进行中…' : '任务完成' }}</span>
              <span>{{ job.done }} / {{ job.total }}</span>
            </div>
            <n-progress type="line" :percentage="jobPct" :show-indicator="false" :height="6" />
            <p v-if="job.message">{{ job.message }}</p>
          </div>
        </section>

        <section class="panel about-data">
          <div class="panel-head"><h2><harddrive :size="15" />存储位置</h2></div>
          <p class="mono">{{ state.appInfo?.dataDir || 'data' }}</p>
          <p>书库、阅读进度、笔记、设置、播放列表全部保存在同目录的 <code>moonaot.db</code> 中；封面、缩略图、导入副本位于 <code>covers / thumbs / library</code>。旧版 JSON 数据已迁移并保留在原位置作为备份。</p>
        </section>
      </template>
    </div>
  </div>
</template>
