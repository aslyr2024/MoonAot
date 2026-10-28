<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  CloudDownload, ExternalLink, FolderOpen, Globe2, Play, RefreshCw, Send, Square,
} from 'lucide-vue-next'
import { NButton, NInput, NInputNumber, NSelect } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import { api } from '../api'
import { host } from '../host'
import { toast, toastError } from '../ui'
import type { SiteDownloadResult, SiteFetchResult, SiteHostInfo } from '../types'
import { basename, formatBytes } from '../utils'

const tab = ref<'download' | 'host' | 'http'>('download')

// 网页下载
const dlUrl = ref('https://example.com')
const dlFolder = ref('')
const dlBusy = ref(false)
const dlMaxPages = ref(20)
const dlMaxDepth = ref(2)
const dlResult = ref<SiteDownloadResult | null>(null)

// 托管
const hostFolder = ref('')
const hosts = ref<SiteHostInfo[]>([])
const hostBusy = ref(false)

// HTTP 调试
const httpUrl = ref('https://example.com')
const httpMethod = ref('GET')
const httpHeaders = ref('')
const httpBody = ref('')
const httpBusy = ref(false)
const httpResult = ref<SiteFetchResult | null>(null)

const methods = ['GET', 'POST', 'PUT', 'DELETE', 'HEAD'].map(m => ({ label: m, value: m }))

async function loadHosts() {
  try { hosts.value = (await api.siteHosts()).items } catch { }
}

async function pickDownloadFolder() {
  const dir = await host.pickFolder('选择网页保存目录')
  if (dir) dlFolder.value = dir
}

async function pickHostFolder() {
  const dir = await host.pickFolder('选择要托管的站点文件夹')
  if (dir) hostFolder.value = dir
}

async function download() {
  if (!dlUrl.value.trim()) { toast('请输入网址', 'warning'); return }
  if (!dlFolder.value) { toast('请选择保存目录', 'warning'); return }
  dlBusy.value = true
  dlResult.value = null
  try {
    dlResult.value = await api.siteDownload(dlUrl.value.trim(), dlFolder.value, dlMaxPages.value, dlMaxDepth.value)
    if (dlResult.value.ok) toast(`已保存 ${dlResult.value.files} 个文件（${formatBytes(dlResult.value.bytes)}）`)
    else toast(dlResult.value.error || '下载失败', 'error')
  } catch (e) { toastError(e) } finally { dlBusy.value = false }
}

async function hostDownloaded() {
  if (!dlFolder.value) return
  await startHost(dlFolder.value)
  tab.value = 'host'
}

async function startHost(folder?: string) {
  const dir = folder || hostFolder.value
  if (!dir) { toast('请选择文件夹', 'warning'); return }
  hostBusy.value = true
  try {
    const info = await api.startSiteHost(dir)
    await loadHosts()
    toast('已开始托管：' + info.url)
    if (!folder) hostFolder.value = dir
  } catch (e) { toastError(e) } finally { hostBusy.value = false }
}

async function stopHost(h: SiteHostInfo) {
  try {
    await api.stopSiteHost(h.id)
    await loadHosts()
    toast('已停止托管')
  } catch (e) { toastError(e) }
}

function openHost(h: SiteHostInfo) {
  host.openBrowser(h.url)
}

async function copyUrl(h: SiteHostInfo) {
  try { await navigator.clipboard.writeText(h.url); toast('地址已复制') } catch { }
}

function parseHeaders(): Record<string, string> {
  const out: Record<string, string> = {}
  for (const line of httpHeaders.value.split('\n')) {
    const i = line.indexOf(':')
    if (i > 0) out[line.slice(0, i).trim()] = line.slice(i + 1).trim()
  }
  return out
}

async function sendHttp() {
  if (!httpUrl.value.trim()) { toast('请输入网址', 'warning'); return }
  httpBusy.value = true
  httpResult.value = null
  try {
    httpResult.value = await api.siteFetch({
      url: httpUrl.value.trim(),
      method: httpMethod.value,
      headers: parseHeaders(),
      body: httpBody.value || undefined,
    })
    if (!httpResult.value.ok) toast(httpResult.value.error || '请求失败', 'error')
  } catch (e) { toastError(e) } finally { httpBusy.value = false }
}

onMounted(loadHosts)
</script>

<template>
  <div class="page site-page">
    <PageHeader title="网站工具" subtitle="网页离线保存 · 本地站点托管 · HTTP 调试">
      <div class="site-tabs">
        <button :class="{ active: tab === 'download' }" @click="tab = 'download'"><cloud-download :size="14" />网页下载</button>
        <button :class="{ active: tab === 'host' }" @click="tab = 'host'"><globe2 :size="14" />站点托管</button>
        <button :class="{ active: tab === 'http' }" @click="tab = 'http'"><send :size="14" />HTTP 调试</button>
      </div>
    </PageHeader>

    <template v-if="tab === 'download'">
      <section class="panel site-card">
        <div class="panel-head"><h2><cloud-download :size="15" /> 递归保存同源站点</h2></div>
        <div class="site-row">
          <n-input v-model:value="dlUrl" placeholder="https://example.com/page.html" />
        </div>
        <div class="site-row">
          <n-input :value="dlFolder" readonly placeholder="保存目录" />
          <n-button @click="pickDownloadFolder"><template #icon><folder-open :size="14" /></template>选择目录</n-button>
          <span class="site-limit">最多页面</span>
          <n-input-number v-model:value="dlMaxPages" :min="1" :max="200" style="width: 100px" />
          <span class="site-limit">深度</span>
          <n-input-number v-model:value="dlMaxDepth" :min="0" :max="6" style="width: 90px" />
          <n-button type="primary" :loading="dlBusy" @click="download"><template #icon><cloud-download :size="14" /></template>开始抓取</n-button>
        </div>
        <div class="site-note">递归抓取同源页面（含链接页），下载 CSS/JS/图片/字体等静态资源并改写本地路径；页面链接也会改写为本地页面。单页上限 5MB，资源总量上限 300MB。</div>
        <div v-if="dlResult" class="site-result" :class="{ bad: !dlResult.ok }">
          <template v-if="dlResult.ok">
            <strong>保存完成</strong>
            <span>入口：{{ dlResult.entry }}</span>
            <span>{{ dlResult.pages || 1 }} 个页面 · {{ dlResult.files }} 个文件 · {{ formatBytes(dlResult.bytes) }}</span>
            <div class="site-actions">
              <n-button size="small" @click="hostDownloaded"><template #icon><play :size="13" /></template>托管并浏览</n-button>
              <n-button size="small" @click="host.reveal(dlFolder)"><template #icon><folder-open :size="13" /></template>打开目录</n-button>
            </div>
          </template>
          <span v-else>{{ dlResult.error }}</span>
        </div>
      </section>
    </template>

    <template v-if="tab === 'host'">
      <section class="panel site-card">
        <div class="panel-head">
          <h2><globe2 :size="15" /> 本地站点托管</h2>
          <n-button size="small" :loading="hostBusy" @click="loadHosts"><template #icon><refresh-cw :size="13" /></template>刷新</n-button>
        </div>
        <div class="site-row">
          <n-input :value="hostFolder" readonly placeholder="选择要托管的文件夹（需包含 index.html）" />
          <n-button @click="pickHostFolder"><template #icon><folder-open :size="14" /></template>选择目录</n-button>
          <n-button type="primary" :loading="hostBusy" @click="startHost()"><template #icon><play :size="14" /></template>开始托管</n-button>
        </div>
        <div class="host-list">
          <div v-for="h in hosts" :key="h.id" class="host-row">
            <globe2 :size="15" />
            <div class="host-info">
              <strong>{{ basename(h.path) || h.path }}</strong>
              <span class="mono">{{ h.url }}</span>
            </div>
            <n-button size="small" @click="openHost(h)"><template #icon><external-link :size="12" /></template>浏览</n-button>
            <n-button size="small" quaternary @click="copyUrl(h)">复制</n-button>
            <n-button size="small" quaternary type="error" @click="stopHost(h)"><template #icon><square :size="12" /></template></n-button>
          </div>
          <div v-if="!hosts.length" class="site-empty">还没有托管的站点。选择文件夹后点“开始托管”。</div>
        </div>
        <div class="site-note">站点只在应用运行期间通过 127.0.0.1 随机端口访问；停止托管或退出应用即失效。</div>
      </section>
    </template>

    <template v-if="tab === 'http'">
      <section class="panel site-card">
        <div class="panel-head"><h2><send :size="15" /> HTTP 调试</h2></div>
        <div class="site-row">
          <n-select v-model:value="httpMethod" :options="methods" style="width: 110px" />
          <n-input v-model:value="httpUrl" placeholder="https://example.com/api" />
          <n-button type="primary" :loading="httpBusy" @click="sendHttp"><template #icon><send :size="14" /></template>发送</n-button>
        </div>
        <div class="http-cols">
          <div class="http-side">
            <label>请求头（每行 Key: Value）</label>
            <n-input v-model:value="httpHeaders" type="textarea" :rows="4" placeholder="Content-Type: application/json" />
            <label>请求体</label>
            <n-input v-model:value="httpBody" type="textarea" :rows="6" placeholder="POST / PUT 的 body" />
          </div>
          <div class="http-main">
            <div v-if="httpResult" class="http-result" :class="{ bad: !httpResult.ok }">
              <div class="http-status">
                <strong>{{ httpResult.ok ? 'HTTP ' + httpResult.status : '请求失败' }}</strong>
                <span>{{ httpResult.contentType || '' }}</span>
                <span>{{ formatBytes(httpResult.length) }}</span>
              </div>
              <div v-if="httpResult.error" class="site-error">{{ httpResult.error }}</div>
              <details v-if="Object.keys(httpResult.headers || {}).length" open>
                <summary>响应头</summary>
                <pre class="mono">{{ Object.entries(httpResult.headers).map(([k, v]) => k + ': ' + v).join('\n') }}</pre>
              </details>
              <pre class="http-body mono">{{ httpResult.body }}</pre>
            </div>
            <div v-else class="site-empty">发送请求后在这里查看状态、响应头与响应体。</div>
          </div>
        </div>
      </section>
    </template>
  </div>
</template>

<style scoped>
.site-page { display: flex; flex-direction: column; gap: 14px; padding: 18px 22px 22px; overflow-y: auto; }
.site-tabs { display: flex; gap: 6px; }
.site-tabs button { display: inline-flex; align-items: center; gap: 6px; height: 30px; padding: 0 12px; border-radius: 9px; border: 1px solid var(--border); background: var(--panel); color: var(--text-2); font-size: 11.5px; cursor: pointer; }
.site-tabs button.active { border-color: var(--accent-ring); background: var(--accent-soft); color: var(--accent); }
.site-card { padding: 16px 18px; display: flex; flex-direction: column; gap: 11px; }
.site-row { display: flex; gap: 8px; align-items: center; }
.site-row .n-input { flex: 1; }
.site-note { font-size: 10.5px; color: var(--text-3); line-height: 1.6; }
.site-limit { font-size: 10.5px; color: var(--text-3); }
.site-result { display: flex; flex-direction: column; gap: 6px; padding: 11px 13px; border-radius: 10px; background: var(--panel-2); border: 1px solid var(--border); font-size: 11.5px; color: var(--text-2); }
.site-result.bad { border-color: rgba(255,107,129,.35); color: var(--danger); }
.site-actions { display: flex; gap: 8px; margin-top: 4px; }
.host-list { display: flex; flex-direction: column; gap: 6px; }
.host-row { display: flex; align-items: center; gap: 10px; padding: 9px 11px; border-radius: 10px; background: var(--panel-2); border: 1px solid var(--border); }
.host-info { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px; }
.host-info strong { font-size: 12px; }
.host-info span { font-size: 10.5px; color: var(--text-3); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.site-empty { padding: 26px; text-align: center; color: var(--text-3); font-size: 11.5px; }
.http-cols { display: grid; grid-template-columns: 330px minmax(0, 1fr); gap: 14px; }
.http-side { display: flex; flex-direction: column; gap: 7px; }
.http-side label { font-size: 10.5px; color: var(--text-3); }
.http-main { min-width: 0; }
.http-result { display: flex; flex-direction: column; gap: 9px; }
.http-status { display: flex; gap: 12px; align-items: center; font-size: 11.5px; color: var(--text-2); }
.http-status strong { color: var(--success); }
.http-result.bad .http-status strong { color: var(--danger); }
.http-result details summary { cursor: pointer; font-size: 11px; color: var(--text-3); }
.http-result pre { margin: 0; font-size: 10.5px; line-height: 1.55; color: var(--text-2); background: var(--panel-2); border: 1px solid var(--border); border-radius: 9px; padding: 10px; overflow: auto; max-height: 360px; white-space: pre-wrap; word-break: break-word; }
.site-error { color: var(--danger); font-size: 11.5px; }
.mono { font-family: ui-monospace, Consolas, monospace; }
@media (max-width: 1000px) { .http-cols { grid-template-columns: 1fr; } .site-row { flex-wrap: wrap; } }
</style>
