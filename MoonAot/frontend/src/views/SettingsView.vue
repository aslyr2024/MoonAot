<script setup lang="ts">
defineOptions({ name: 'SettingsView' })

import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import {
  BookOpen, Braces, Database, FolderOpen, ImagePlus, Info, MonitorCog, Palette,
  RefreshCw, RotateCcw, Save, Sparkles, Type, Volume2,
} from 'lucide-vue-next'
import {
  NAlert, NButton, NInputNumber, NProgress, NSelect, NSlider, NSwitch, NTabPane, NTabs,
} from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import { api } from '../api'
import {
  READER_DEFAULTS, loadAppInfo, loadFonts, loadSettings, saveSettings,
  scheduleLibraryRefresh, state,
} from '../store'
import { formatBytes, basename } from '../utils'
import { PALETTES, themeNames } from '../theme'
import { host } from '../host'
import { confirmDialog, toast, toastError } from '../ui'
import type { JobStatus } from '../types'

const tab = ref('appearance')
const system = ref<any>({})
const loading = ref(true)
const savingReader = ref(false)
const job = ref<JobStatus | null>(null)
let jobTimer: ReturnType<typeof setTimeout> | null = null
const coverNonce = ref(Date.now())
const coverInput = ref<HTMLInputElement | null>(null)
const saveTimer: Record<string, ReturnType<typeof setTimeout>> = {}

const s = computed(() => state.settings)
const gv = (key: string) => system.value?.globalViewSettings?.[key] ?? (READER_DEFAULTS as any)[key]
function setGv(key: string, value: any) {
  if (!system.value.globalViewSettings) system.value.globalViewSettings = {}
  system.value.globalViewSettings[key] = value
}

const fontOptions = computed(() => {
  const embedded = state.fonts.embedded || []
  const fonts = state.fonts.fonts || []
  const list = [
    ...embedded.map(f => ({ label: `${f}（内置）`, value: f })),
    ...fonts.map(f => ({ label: f, value: f })),
  ]
  return list
})
const latinCategoryOptions = [
  { label: '无衬线（Sans-Serif Font）', value: 'Sans-Serif Font' },
  { label: '衬线（Serif Font）', value: 'Serif Font' },
  { label: '等宽（Monospace Font）', value: 'Monospace Font' },
]
const themeOptions = [
  { label: '浅色纸张', value: 'light' },
  { label: '深色夜间', value: 'dark' },
]
const pageMode = computed(() => {
  if (gv('scrolled')) return 'scroll'
  return Number(gv('maxColumnCount')) >= 2 ? 'double' : 'single'
})
function setPageMode(mode: string) {
  if (mode === 'scroll') {
    setGv('scrolled', true); setGv('continuousScroll', true); setGv('maxColumnCount', 1)
  } else if (mode === 'double') {
    setGv('scrolled', false); setGv('continuousScroll', false); setGv('maxColumnCount', 2)
  } else {
    setGv('scrolled', false); setGv('continuousScroll', false); setGv('maxColumnCount', 1)
  }
}
const pageModeOptions = [
  { label: '单页翻页', value: 'single' },
  { label: '双页翻页', value: 'double' },
  { label: '连续滚动', value: 'scroll' },
]

async function loadSystem() {
  try {
    const r = await api.readerSettings()
    const parsed = JSON.parse(r.json || '{}')
    if (!parsed.globalViewSettings) parsed.globalViewSettings = {}
    system.value = parsed
  } catch (e) { toastError(e) }
}

async function saveReader() {
  savingReader.value = true
  try {
    const g = system.value.globalViewSettings || {}
    system.value.themeMode = g.theme === 'dark' ? 'dark' : 'light'
    if (!system.value.language) system.value.language = 'zh-CN'
    const json = JSON.stringify(system.value)
    await api.saveReaderSettings(json)
    await saveSettings({ readerSettingsJson: json })
    toast('排版设置已保存，新打开的阅读器会立即使用')
  } catch (e) { toastError(e) } finally { savingReader.value = false }
}

async function resetReader() {
  const ok = await confirmDialog({ title: '恢复默认排版', content: '将字号、行距、页边距、字体等恢复为默认值。', positiveText: '恢复' })
  if (!ok) return
  system.value = { version: 1, language: 'zh-CN', themeMode: 'light', globalViewSettings: { ...READER_DEFAULTS } }
  await saveReader()
}

function saveSoon(key: string, patch: Record<string, any>) {
  if (saveTimer[key]) clearTimeout(saveTimer[key])
  saveTimer[key] = setTimeout(() => { void saveSettings(patch) }, 260)
}

function setMedia(key: string, value: any) {
  void saveSettings({ media: { ...s.value.media, [key]: value } })
}

const slideBgmName = computed(() => s.value.media.imageBackgroundMusic
  ? basename(s.value.media.imageBackgroundMusic)
  : '')

async function chooseSlideBgm() {
  const file = await host.pickFile('audio', '选择幻灯片背景音乐')
  if (file) setMedia('imageBackgroundMusic', file)
}

function setTheme(name: string) {
  void saveSettings({ appTheme: name as any })
  toast(`已切换主题：${PALETTES[name as keyof typeof PALETTES]?.label ?? name}`)
}

async function onCoverFile(ev: Event) {
  const input = ev.target as HTMLInputElement
  const f = input.files?.[0]
  input.value = ''
  if (!f) return
  try {
    const dataUrl = await new Promise<string>((resolve, reject) => {
      const r = new FileReader()
      r.onload = () => resolve(String(r.result || ''))
      r.onerror = () => reject(new Error('读取图片失败'))
      r.readAsDataURL(f)
    })
    const res = await api.setDefaultCover(dataUrl)
    if (!res.ok) throw new Error(res.error || '设置失败')
    toast(`缺省封面已保存，将用于 ${res.applied} 本无封面书籍`)
    await loadSettings(true)
    coverNonce.value = Date.now()
  } catch (e) { toastError(e) }
}

const defaultCoverUrl = computed(() =>
  s.value.defaultCoverPath ? `/api/defaultcover?t=${coverNonce.value}` : '')
async function runJob(kind: 'covers' | 'purge' | 'metadata') {
  if (job.value?.running) return
  if (kind === 'purge') {
    const ok = await confirmDialog({
      title: '清理失效记录',
      content: '只移除文件已不存在的书架记录，不删除任何文件。确定继续吗？',
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
  if (jobTimer) clearTimeout(jobTimer)
  jobTimer = setTimeout(async () => {
    if (!job.value) return
    try {
      const j = await api.job(job.value.id)
      job.value = j
      if (j.running) pollJob()
      else { toast(j.message || '任务完成'); scheduleLibraryRefresh(100) }
    } catch { }
  }, 800)
}

const jobPct = computed(() => {
  const j = job.value
  if (!j) return 0
  if (!j.total) return 5
  return Math.max(2, Math.min(100, Math.round((j.done / j.total) * 100)))
})

async function cleanupLegacy() {
  const ok = await confirmDialog({
    title: '清理旧版 JSON 备份',
    content: '旧版 books/configs/appsettings JSON 已全部迁移到 SQLite。删除后不可恢复；不会删除原书文件、封面或当前数据库。确定删除吗？',
    positiveText: '删除备份', danger: true,
  })
  if (!ok) return
  try {
    const r = await api.cleanupLegacy()
    if (!r.ok) throw new Error(r.error || '清理失败')
    toast(`已删除 ${r.count} 个旧版文件（${formatBytes(Number(r.message || 0))}）`)
  } catch (e) { toastError(e) }
}

function openLogs() {
  const dir = state.appInfo?.dataDir
  if (dir) void api.fsOpen(dir + '\\logs')
}

function openData() {
  const dir = state.appInfo?.dataDir
  if (dir) void api.fsOpen(dir)
}

onMounted(async () => {
  loading.value = true
  await Promise.all([loadSettings(), loadFonts(), loadAppInfo(), loadSystem()])
  loading.value = false
})

onBeforeUnmount(() => {
  if (jobTimer) clearTimeout(jobTimer)
  for (const t of Object.values(saveTimer)) clearTimeout(t)
})
</script>

<template>
  <div class="page settings-page">
    <PageHeader title="设置" subtitle="外观、阅读排版、书库与媒体偏好" />

    <n-tabs v-model:value="tab" type="line" animated class="settings-tabs">
      <n-tab-pane name="appearance" tab="外观">
        <div class="page-scroll settings-scroll">
          <section class="panel settings-panel">
            <div class="panel-head"><h2><palette :size="15" />界面主题</h2><span>5 套主题，立即生效</span></div>
            <div class="theme-cards">
              <button
                v-for="t in themeNames"
                :key="t"
                class="theme-card"
                :class="{ active: s.appTheme === t }"
                @click="setTheme(t)"
              >
                <span class="theme-swatch" :style="{ background: `linear-gradient(135deg, ${PALETTES[t].panel}, ${PALETTES[t].accent})` }" />
                <span class="theme-name">{{ PALETTES[t].label }}</span>
                <span class="theme-check" v-if="s.appTheme === t">●</span>
              </button>
            </div>
          </section>

          <section class="panel settings-panel">
            <div class="panel-head"><h2><monitor-cog :size="15" />书架显示</h2></div>
            <div class="setting-row">
              <div class="setting-label"><strong>显示封面</strong><span>关闭后书架使用渐变书脊占位</span></div>
              <n-switch :value="s.showCovers" @update:value="v => saveSettings({ showCovers: v })" />
            </div>
            <div class="setting-row">
              <div class="setting-label"><strong>默认排序</strong><span>书架打开时使用的排序方式</span></div>
              <n-select
                :value="s.sortMode"
                style="width: 170px"
                :options="[
                  { label: '最近阅读', value: 'recent' },
                  { label: '导入时间', value: 'added' },
                  { label: '书名', value: 'title' },
                  { label: '作者', value: 'author' },
                  { label: '文件大小', value: 'size' },
                  { label: '格式', value: 'format' },
                ]"
                @update:value="v => saveSettings({ sortMode: v })"
              />
            </div>
            <div class="setting-row">
              <div class="setting-label"><strong>封面宽度</strong><span>控制书架每列的书封大小</span></div>
              <div class="slider-inline">
                <n-slider :value="s.gridSize" :min="110" :max="260" :step="4" style="width: 200px"
                  @update:value="v => { s.gridSize = v; saveSoon('grid', { gridSize: v }) }" />
                <b>{{ s.gridSize }}px</b>
              </div>
            </div>
            <div class="setting-row">
              <div class="setting-label"><strong>无封面时显示缺省封面</strong><span>EPUB / PDF / MOBI / AZW3 没有提取到封面时使用</span></div>
              <n-switch :value="s.useDefaultCover" @update:value="v => saveSettings({ useDefaultCover: v })" />
            </div>
          </section>

          <section class="panel settings-panel">
            <div class="panel-head"><h2><image-plus :size="15" />缺省封面</h2><span v-if="s.defaultCoverPath">已设置</span></div>
            <div class="cover-setting">
              <div class="cover-preview" :class="{ empty: !s.defaultCoverPath }">
                <img v-if="s.defaultCoverPath" :src="defaultCoverUrl" alt="" />
                <span v-else>未设置</span>
              </div>
              <div class="cover-actions">
                <n-button @click="coverInput?.click()"><template #icon><image-plus :size="15" /></template>选择图片…</n-button>
                <p>用于无提取封面的书；TXT / Markdown 永远使用渐变占位。</p>
                <input ref="coverInput" type="file" accept="image/png,image/jpeg,image/webp" hidden @change="onCoverFile" />
              </div>
            </div>
          </section>
        </div>
      </n-tab-pane>

      <n-tab-pane name="reading" tab="阅读排版">
        <div class="page-scroll settings-scroll">
          <n-alert type="info" :bordered="false" class="settings-alert">
            以下设置写入 readest 的 <code>globalViewSettings</code>，新打开的阅读器会立即应用；已打开的阅读器可在其侧栏中单独调整。
          </n-alert>

          <section class="panel settings-panel">
            <div class="panel-head"><h2><type :size="15" />字体</h2></div>
            <div class="setting-grid">
              <label class="setting-field"><span>中文字体</span>
                <n-select :value="gv('defaultCJKFont')" :options="fontOptions" filterable @update:value="v => setGv('defaultCJKFont', v)" />
              </label>
              <label class="setting-field"><span>西文字体</span>
                <n-select :value="gv('defaultFont')" :options="latinCategoryOptions" @update:value="v => setGv('defaultFont', v)" />
              </label>
              <label class="setting-field"><span>衬线字体名</span>
                <n-select :value="gv('serifFont')" :options="fontOptions" filterable @update:value="v => setGv('serifFont', v)" />
              </label>
              <label class="setting-field"><span>无衬线字体名</span>
                <n-select :value="gv('sansSerifFont')" :options="fontOptions" filterable @update:value="v => setGv('sansSerifFont', v)" />
              </label>
              <label class="setting-field"><span>等宽字体名</span>
                <n-select :value="gv('monospaceFont')" :options="fontOptions" filterable @update:value="v => setGv('monospaceFont', v)" />
              </label>
              <label class="setting-field"><span>字号</span>
                <div class="slider-inline"><n-slider :value="Number(gv('defaultFontSize'))" :min="12" :max="40" :step="1" style="width: 150px" @update:value="v => setGv('defaultFontSize', v)" /><b>{{ gv('defaultFontSize') }}px</b></div>
              </label>
              <label class="setting-field"><span>字重</span>
                <n-select :value="Number(gv('fontWeight'))" :options="[
                  { label: '300 细', value: 300 }, { label: '400 常规', value: 400 },
                  { label: '500 中等', value: 500 }, { label: '600 半粗', value: 600 },
                  { label: '700 粗体', value: 700 },
                ]" @update:value="v => setGv('fontWeight', v)" />
              </label>
              <label class="setting-field"><span>行距</span>
                <div class="slider-inline"><n-slider :value="Number(gv('lineHeight'))" :min="1" :max="3" :step="0.05" style="width: 150px" @update:value="v => setGv('lineHeight', String(v))" /><b>{{ gv('lineHeight') }}</b></div>
              </label>
            </div>
          </section>

          <section class="panel settings-panel">
            <div class="panel-head"><h2><sparkles :size="15" />版式与主题</h2></div>
            <div class="setting-grid">
              <label class="setting-field"><span>阅读主题</span>
                <n-select :value="gv('theme')" :options="themeOptions" @update:value="v => setGv('theme', v)" />
              </label>
              <label class="setting-field"><span>翻页方式</span>
                <n-select :value="pageMode" :options="pageModeOptions" @update:value="setPageMode" />
              </label>
              <label class="setting-field"><span>两端对齐</span>
                <n-switch :value="!!gv('fullJustification')" @update:value="v => setGv('fullJustification', v)" />
              </label>
              <label class="setting-field"><span>自动断字</span>
                <n-switch :value="!!gv('hyphenation')" @update:value="v => setGv('hyphenation', v)" />
              </label>
              <label class="setting-field"><span>段间距</span>
                <div class="slider-inline"><n-slider :value="Number(gv('paragraphMargin'))" :min="0" :max="3" :step="0.1" style="width: 150px" @update:value="v => setGv('paragraphMargin', v)" /><b>{{ gv('paragraphMargin') }}</b></div>
              </label>
              <label class="setting-field"><span>首行缩进</span>
                <div class="slider-inline"><n-slider :value="Number(gv('textIndent'))" :min="0" :max="4" :step="0.5" style="width: 150px" @update:value="v => setGv('textIndent', v)" /><b>{{ gv('textIndent') }}em</b></div>
              </label>
            </div>
            <div class="setting-grid margins">
              <label class="setting-field"><span>上边距</span><n-input-number :value="Number(gv('marginTopPx'))" :min="0" :max="200" @update:value="v => setGv('marginTopPx', v ?? 0)" /></label>
              <label class="setting-field"><span>下边距</span><n-input-number :value="Number(gv('marginBottomPx'))" :min="0" :max="200" @update:value="v => setGv('marginBottomPx', v ?? 0)" /></label>
              <label class="setting-field"><span>左边距</span><n-input-number :value="Number(gv('marginLeftPx'))" :min="0" :max="200" @update:value="v => setGv('marginLeftPx', v ?? 0)" /></label>
              <label class="setting-field"><span>右边距</span><n-input-number :value="Number(gv('marginRightPx'))" :min="0" :max="200" @update:value="v => setGv('marginRightPx', v ?? 0)" /></label>
            </div>
            <div class="setting-grid">
              <label class="setting-field"><span>显示页眉</span><n-switch :value="!!gv('showHeader')" @update:value="v => setGv('showHeader', v)" /></label>
              <label class="setting-field"><span>显示页脚 / 页码</span><n-switch :value="!!gv('showPageNumber')" @update:value="v => setGv('showPageNumber', v)" /></label>
              <label class="setting-field"><span>翻页动画</span><n-switch :value="!!gv('animated')" @update:value="v => setGv('animated', v)" /></label>
              <label class="setting-field"><span>朗读语速</span>
                <div class="slider-inline"><n-slider :value="Number(gv('ttsRate') ?? 1.3)" :min="0.5" :max="2.5" :step="0.1" style="width: 150px" @update:value="v => setGv('ttsRate', v)" /><b>{{ Number(gv('ttsRate') ?? 1.3).toFixed(1) }}×</b></div>
              </label>
            </div>
            <div class="settings-save-row">
              <n-button @click="resetReader"><template #icon><rotate-ccw :size="15" /></template>恢复默认</n-button>
              <n-button type="primary" :loading="savingReader" @click="saveReader"><template #icon><save :size="15" /></template>保存排版设置</n-button>
            </div>
          </section>
        </div>
      </n-tab-pane>

      <n-tab-pane name="library" tab="书库与数据">
        <div class="page-scroll settings-scroll">
          <section class="panel settings-panel">
            <div class="panel-head"><h2><database :size="15" />SQLite 数据</h2><span>原生 AOT 实测可用</span></div>
            <div class="setting-row">
              <div class="setting-label"><strong>数据目录</strong><span class="mono">{{ state.appInfo?.dataDir }}</span></div>
              <div class="row-actions">
                <n-button size="small" @click="openData"><template #icon><folder-open :size="14" /></template>打开</n-button>
                <n-button size="small" @click="openLogs"><template #icon><braces :size="14" /></template>日志</n-button>
              </div>
            </div>
            <p class="setting-note">书库、进度、笔记、设置、播放列表保存在 <code>moonaot.db</code>；封面/缩略图/导入副本位于同目录。旧的 <code>%LOCALAPPDATA%\MoonAot</code> JSON 已迁移并保留作为备份，可在确认无误后删除。</p>
            <div class="setting-row">
              <div class="setting-label"><strong>清理旧版 JSON 备份</strong><span>删除 %LOCALAPPDATA%\MoonAot\data 下已迁移的 JSON（原书/封面不受影响）</span></div>
              <n-button size="small" type="error" ghost @click="cleanupLegacy">清理备份</n-button>
            </div>
          </section>

          <section class="panel settings-panel">
            <div class="panel-head"><h2><refresh-cw :size="15" />缓存维护</h2></div>
            <div class="maintain-row">
              <div class="maintain-info"><strong>重建封面缩略图</strong><p>重新提取 EPUB / MOBI / PDF 封面，封面压到 ≤48KB，书架构图 320×480 JPEG（≤40KB）。</p></div>
              <n-button :disabled="!!job?.running" @click="runJob('covers')">重建封面</n-button>
            </div>
            <div class="maintain-row">
              <div class="maintain-info"><strong>补齐书籍元数据</strong><p>从 EPUB / MOBI / PDF 重新读取作者、出版社、简介，只填空字段。</p></div>
              <n-button :disabled="!!job?.running" @click="runJob('metadata')">补齐元数据</n-button>
            </div>
            <div class="maintain-row">
              <div class="maintain-info"><strong>清理失效记录</strong><p>仅移除文件已不存在的记录，不删除原文件。</p></div>
              <n-button :disabled="!!job?.running" type="error" ghost @click="runJob('purge')">清理失效</n-button>
            </div>
            <div v-if="job" class="job-box">
              <div class="job-head"><span>{{ job.running ? '任务进行中…' : '任务完成' }}</span><span>{{ job.done }} / {{ job.total }}</span></div>
              <n-progress type="line" :percentage="jobPct" :show-indicator="false" :height="6" />
              <p v-if="job.message">{{ job.message }}</p>
            </div>
          </section>

          <section class="panel settings-panel">
            <div class="panel-head"><h2><book-open :size="15" />导入与刷新</h2></div>
            <div class="setting-row">
              <div class="setting-label"><strong>阅读器进度自动刷新书架</strong><span>阅读时书架上的进度与最近阅读会同步更新</span></div>
              <n-switch :value="s.autoRefreshLibrary" @update:value="v => saveSettings({ autoRefreshLibrary: v })" />
            </div>
            <div class="setting-row">
              <div class="setting-label"><strong>导入时提取封面</strong><span>单本导入时立即生成封面缩略图；批量导入按需生成</span></div>
              <n-switch :value="s.importCovers" @update:value="v => saveSettings({ importCovers: v })" />
            </div>
          </section>
        </div>
      </n-tab-pane>

      <n-tab-pane name="media" tab="媒体">
        <div class="page-scroll settings-scroll">
          <section class="panel settings-panel">
            <div class="panel-head"><h2><image-plus :size="15" />图片浏览</h2></div>
            <div class="setting-grid">
              <label class="setting-field"><span>幻灯片间隔</span>
                <div class="slider-inline"><n-slider :value="s.media.imageSlideSeconds" :min="2" :max="60" :step="1" style="width: 160px" @update:value="v => setMedia('imageSlideSeconds', v)" /><b>{{ s.media.imageSlideSeconds }}s</b></div>
              </label>
              <label class="setting-field"><span>幻灯片过渡效果</span>
                <n-select :value="s.media.imageTransition || 'fade'" :options="[
                  { label: '淡入淡出', value: 'fade' }, { label: '横向滑动', value: 'slide' },
                  { label: '缩放进入', value: 'zoom' }, { label: '无效果', value: 'none' },
                ]" @update:value="v => setMedia('imageTransition', v)" />
              </label>
              <label class="setting-field"><span>幻灯片循环</span><n-switch :value="s.media.imageSlideLoop" @update:value="v => setMedia('imageSlideLoop', v)" /></label>
              <label class="setting-field"><span>幻灯片背景音乐</span>
                <div class="slider-inline">
                  <b class="bgm-name" :title="slideBgmName || '未设置'">{{ slideBgmName || '未设置' }}</b>
                  <n-button size="tiny" @click="chooseSlideBgm">选择</n-button>
                  <n-button v-if="slideBgmName" size="tiny" quaternary @click="setMedia('imageBackgroundMusic', null)">清除</n-button>
                </div>
              </label>
              <label class="setting-field"><span>包含子文件夹</span><n-switch :value="s.media.recursiveImages" @update:value="v => setMedia('recursiveImages', v)" /></label>
              <label class="setting-field"><span>漫画模式</span><n-switch :value="s.media.comicMode" @update:value="v => setMedia('comicMode', v)" /></label>
              <label class="setting-field"><span>漫画宽度</span>
                <n-select :value="s.media.comicWidth" :options="[
                  { label: '自适应', value: 'default' }, { label: '全宽', value: 'full' },
                  { label: '2/3', value: '2/3' }, { label: '1/2', value: '1/2' },
                ]" @update:value="v => setMedia('comicWidth', v)" />
              </label>
              <label class="setting-field"><span>滑到底自动下一张</span><n-switch :value="s.media.comicAutoPage" @update:value="v => setMedia('comicAutoPage', v)" /></label>
            </div>
          </section>
          <section class="panel settings-panel">
            <div class="panel-head"><h2><volume2 :size="15" />音乐与视频</h2></div>
            <div class="setting-grid">
              <label class="setting-field"><span>背景图轮播间隔</span>
                <div class="slider-inline"><n-slider :value="s.media.musicBackgroundSeconds" :min="2" :max="60" :step="1" style="width: 160px" @update:value="v => setMedia('musicBackgroundSeconds', v)" /><b>{{ s.media.musicBackgroundSeconds }}s</b></div>
              </label>
              <label class="setting-field"><span>显示歌词面板</span><n-switch :value="s.media.musicShowLyrics" @update:value="v => setMedia('musicShowLyrics', v)" /></label>
              <label class="setting-field"><span>视频自动续播</span><n-switch :value="s.media.videoAutoResume" @update:value="v => setMedia('videoAutoResume', v)" /></label>
            </div>
          </section>
        </div>
      </n-tab-pane>

      <n-tab-pane name="about" tab="关于">
        <div class="page-scroll settings-scroll">
          <section class="panel about-panel">
            <div class="about-brand">
              <div class="brand-mark big">☾</div>
              <div>
                <h2>静读天下 · MoonAot</h2>
                <p>WebView2 + NativeAOT 本地阅读器 · SQLite 持久化</p>
              </div>
            </div>
            <div class="about-grid">
              <div><span>程序版本</span><b>{{ state.appInfo?.version || '-' }}</b></div>
              <div><span>WebView2</span><b>{{ state.appInfo?.webViewVersion || '未检测到' }}</b></div>
              <div><span>运行时</span><b>{{ state.appInfo?.isAot ? 'NativeAOT' : 'JIT / 开发模式' }}</b></div>
              <div><span>操作系统</span><b>{{ state.appInfo?.os || '-' }}</b></div>
              <div><span>藏书数量</span><b>{{ (state.appInfo?.bookCount ?? 0).toLocaleString() }} 本</b></div>
              <div><span>数据目录</span><b class="mono">{{ state.appInfo?.dataDir || '-' }}</b></div>
            </div>
            <div class="about-actions">
              <n-button @click="openData"><template #icon><folder-open :size="15" /></template>打开数据目录</n-button>
              <n-button @click="openLogs"><template #icon><braces :size="15" /></template>打开日志</n-button>
            </div>
            <div class="about-note">
              <info :size="15" />
              <p>本程序完全离线运行。阅读器由 readest/foliate-js 驱动，书库、笔记、设置与播放列表使用本地 SQLite 单文件数据库，便于 NativeAOT 单文件发布。</p>
            </div>
          </section>
        </div>
      </n-tab-pane>
    </n-tabs>
  </div>
</template>
