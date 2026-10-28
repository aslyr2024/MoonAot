<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  Binary, Camera, Check, Clock3, Cpu, FileArchive, FileKey2, FolderOpen, HardDrive, Hash, ListTree,
  MemoryStick, Network, Power, RefreshCw, Search, Trash2, Wrench, Zap,
} from 'lucide-vue-next'
import { NButton, NInput, NSelect } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import { api } from '../api'
import { host } from '../host'
import { confirmDialog, toast, toastError } from '../ui'
import type { HashResult, PortItem, ProcessItem, SystemInfo } from '../types'
import { basename, dirname, formatBytes } from '../utils'

const sys = ref<SystemInfo | null>(null)
const loading = ref(false)

const hashPath = ref('')
const hashAlgo = ref('sha256')
const hashBusy = ref(false)
const hashResult = ref<HashResult | null>(null)
const algos = [
  { label: 'SHA-256', value: 'sha256' },
  { label: 'SHA-1', value: 'sha1' },
  { label: 'MD5', value: 'md5' },
  { label: 'SHA-512', value: 'sha512' },
]

const textIn = ref('')
const textOut = ref('')

const procQuery = ref('')
const processes = ref<ProcessItem[]>([])
const procBusy = ref(false)
const ports = ref<PortItem[]>([])
const portBusy = ref(false)
const shotPath = ref('')
const archiveFolder = ref('')
const archiveZip = ref('')
const extractZip = ref('')
const extractDest = ref('')
const archiveBusy = ref(false)

const tsInput = ref(String(Date.now()))
const tsResult = computed(() => {
  const raw = tsInput.value.trim()
  if (!raw) return null
  let n = Number(raw)
  if (!Number.isFinite(n)) return null
  if (n > 1e12) n = Math.floor(n / 1000)
  const d = new Date(n * 1000)
  if (Number.isNaN(d.getTime())) return null
  return { local: d.toLocaleString(), utc: d.toUTCString(), iso: d.toISOString(), seconds: n }
})

function memoryPct() {
  if (!sys.value?.memoryTotal) return 0
  return Math.round(((sys.value.memoryTotal - sys.value.memoryAvailable) / sys.value.memoryTotal) * 100)
}
function diskPct(total: number, free: number) {
  if (!total) return 0
  return Math.round(((total - free) / total) * 100)
}
function uptime(sec: number) {
  const d = Math.floor(sec / 86400), h = Math.floor((sec % 86400) / 3600), m = Math.floor((sec % 3600) / 60)
  return `${d ? d + '天 ' : ''}${h}小时 ${m}分`
}

async function loadProcesses() {
  procBusy.value = true
  try { processes.value = await api.processes(procQuery.value) } catch (e) { toastError(e) } finally { procBusy.value = false }
}

async function killProcess(p: ProcessItem) {
  const ok = await confirmDialog({
    title: '结束进程', content: `确定结束 ${p.name} (PID ${p.id}) 吗？未保存的数据会丢失。`,
    positiveText: '结束进程', danger: true,
  })
  if (!ok) return
  try { await api.killProcess(p.id); toast('进程已结束'); await loadProcesses() } catch (e) { toastError(e) }
}

async function loadPorts() {
  portBusy.value = true
  try { ports.value = await api.ports() } catch (e) { toastError(e) } finally { portBusy.value = false }
}

async function killPort(p: PortItem) {
  const ok = await confirmDialog({
    title: '结束端口进程', content: `端口 ${p.port} 由 PID ${p.pid} 监听，确定结束吗？`,
    positiveText: '结束进程', danger: true,
  })
  if (!ok) return
  try { await api.killPort(p.port); toast('已结束'); await loadPorts() } catch (e) { toastError(e) }
}

async function screenshot() {
  try {
    const r = await api.screenshot()
    if (!r.ok) { toast(r.error || '截图失败', 'error'); return }
    shotPath.value = r.path || ''
    toast('截图已保存')
  } catch (e) { toastError(e) }
}

async function powerAction(action: 'lock' | 'sleep' | 'screenoff' | 'shutdown' | 'restart') {
  if (action === 'shutdown' || action === 'restart' || action === 'sleep') {
    const ok = await confirmDialog({
      title: action === 'shutdown' ? '关机' : action === 'restart' ? '重启' : '睡眠',
      content: '请先保存所有工作，操作会立即执行。',
      positiveText: '继续', danger: true,
    })
    if (!ok) return
  }
  try { await api.power(action); toast('操作已发送') } catch (e) { toastError(e) }
}

async function pickArchiveFolder() {
  const dir = await host.pickFolder('选择要压缩的文件夹')
  if (!dir) return
  archiveFolder.value = dir
  if (!archiveZip.value) archiveZip.value = dir.replace(/[\\/]+$/, '') + '.zip'
}

async function compressFolder() {
  if (!archiveFolder.value) { toast('请选择源文件夹', 'warning'); return }
  if (!archiveZip.value) { toast('请填写 ZIP 路径', 'warning'); return }
  archiveBusy.value = true
  try {
    await api.archiveCompress(archiveFolder.value, archiveZip.value)
    toast('压缩完成')
  } catch (e) { toastError(e) } finally { archiveBusy.value = false }
}

async function pickExtractZip() {
  const file = await host.pickFile('any', '选择 ZIP 文件')
  if (!file) return
  extractZip.value = file
  const name = basename(file).replace(/\.zip$/i, '')
  extractDest.value = dirname(file) + '\\' + name
}

async function extractZipNow() {
  if (!extractZip.value || !extractDest.value) { toast('请选择 ZIP 与目标目录', 'warning'); return }
  archiveBusy.value = true
  try { await api.archiveExtract(extractZip.value, extractDest.value); toast('解压完成') }
  catch (e) { toastError(e) } finally { archiveBusy.value = false }
}

async function loadSystem() {
  loading.value = true
  try { sys.value = await api.systemInfo() } catch (e) { toastError(e) } finally { loading.value = false }
}

async function pickHashFile() {
  const file = await host.pickFile('any', '选择要计算哈希的文件')
  if (!file) return
  hashPath.value = file
  hashResult.value = null
}

async function doHash() {
  if (!hashPath.value) { toast('请先选择文件', 'warning'); return }
  hashBusy.value = true
  try {
    hashResult.value = await api.fileHash(hashPath.value, hashAlgo.value)
    if (hashResult.value.ok) toast('哈希计算完成')
    else toast(hashResult.value.error || '计算失败', 'error')
  } catch (e) { toastError(e) } finally { hashBusy.value = false }
}

async function copy(text: string, label = '已复制') {
  try { await navigator.clipboard.writeText(text); toast(label) } catch { toast('复制失败', 'warning') }
}

function textTransform(kind: 'b64e' | 'b64d' | 'urle' | 'urld' | 'json' | 'minify') {
  try {
    const src = textIn.value
    if (kind === 'b64e') textOut.value = btoa(unescape(encodeURIComponent(src)))
    else if (kind === 'b64d') textOut.value = decodeURIComponent(escape(atob(src.trim())))
    else if (kind === 'urle') textOut.value = encodeURIComponent(src)
    else if (kind === 'urld') textOut.value = decodeURIComponent(src)
    else if (kind === 'json') textOut.value = JSON.stringify(JSON.parse(src), null, 2)
    else textOut.value = JSON.stringify(JSON.parse(src))
  } catch (e) {
    toast(e instanceof Error ? e.message : '转换失败', 'error')
  }
}

onMounted(loadSystem)
</script>

<template>
  <div class="page toolbox-page">
    <PageHeader title="工具箱" subtitle="系统信息 · 文件哈希 · 文本编码 · 时间戳">
      <n-button :loading="loading" @click="loadSystem"><template #icon><refresh-cw :size="15" /></template>刷新系统信息</n-button>
    </PageHeader>

    <div class="toolbox-grid">
      <section class="panel tool-card sys-card">
        <div class="panel-head"><h2><cpu :size="15" /> 系统信息</h2><span>{{ sys?.appVersion }}</span></div>
        <div class="sys-grid" v-if="sys">
          <div><span>系统</span><b>{{ sys.os }}</b></div>
          <div><span>架构 / CPU</span><b>{{ sys.machine }} · {{ sys.cpuCount }} 线程</b></div>
          <div class="wide"><span>处理器</span><b>{{ sys.cpuName || '-' }}</b></div>
          <div><span>用户</span><b>{{ sys.user }}</b></div>
          <div><span>进程</span><b>#{{ sys.processId }} {{ sys.processName }}</b></div>
          <div><span>运行时长</span><b>{{ uptime(sys.uptimeSeconds) }}</b></div>
          <div><span>当前占用</span><b>{{ formatBytes(sys.workingSet) }}</b></div>
          <div class="wide"><span>数据目录</span><b class="mono" :title="sys.dataDir">{{ sys.dataDir }}</b></div>
        </div>
        <div class="mem-line" v-if="sys">
          <memory-stick :size="14" />
          <span>内存</span>
          <div class="bar"><i :style="{ width: memoryPct() + '%' }" /></div>
          <b>{{ memoryPct() }}% · 可用 {{ formatBytes(sys.memoryAvailable) }} / {{ formatBytes(sys.memoryTotal) }}</b>
        </div>
        <div class="disks" v-if="sys">
          <div v-for="d in sys.disks" :key="d.name" class="disk-row">
            <hard-drive :size="14" /><span>{{ d.name }}</span>
            <div class="bar"><i :style="{ width: diskPct(d.total, d.free) + '%' }" /></div>
            <b>可用 {{ formatBytes(d.free) }} / {{ formatBytes(d.total) }}</b>
          </div>
        </div>
      </section>

      <section class="panel tool-card">
        <div class="panel-head"><h2><hash :size="15" /> 文件哈希</h2></div>
        <div class="hash-row">
          <n-input :value="hashPath" readonly placeholder="选择文件后计算 MD5 / SHA" />
          <n-button @click="pickHashFile"><template #icon><folder-open :size="14" /></template>选择文件</n-button>
        </div>
        <div class="hash-row">
          <n-select v-model:value="hashAlgo" :options="algos" style="width: 150px" />
          <n-button type="primary" :loading="hashBusy" @click="doHash"><template #icon><file-key2 :size="14" /></template>计算</n-button>
        </div>
        <div v-if="hashResult?.ok" class="hash-result">
          <div class="hash-name"><file-key2 :size="14" /><span>{{ basename(hashResult.path || '') }}</span><em>{{ formatBytes(hashResult.size) }}</em></div>
          <div class="hash-value mono">{{ hashResult.hash }}</div>
          <n-button size="small" @click="copy(hashResult.hash || '')"><template #icon><check :size="13" /></template>复制哈希</n-button>
        </div>
        <div v-else-if="hashResult?.error" class="hash-error">{{ hashResult.error }}</div>
      </section>

      <section class="panel tool-card text-card">
        <div class="panel-head"><h2><binary :size="15" /> 文本 / 编码</h2></div>
        <div class="text-cols">
          <n-input v-model:value="textIn" type="textarea" :rows="7" placeholder="输入文本或 JSON / Base64…" />
          <div class="text-actions">
            <n-button size="small" @click="textTransform('b64e')">Base64 编码</n-button>
            <n-button size="small" @click="textTransform('b64d')">Base64 解码</n-button>
            <n-button size="small" @click="textTransform('urle')">URL 编码</n-button>
            <n-button size="small" @click="textTransform('urld')">URL 解码</n-button>
            <n-button size="small" @click="textTransform('json')">JSON 格式化</n-button>
            <n-button size="small" @click="textTransform('minify')">JSON 压缩</n-button>
          </div>
          <n-input v-model:value="textOut" type="textarea" :rows="7" readonly placeholder="结果" />
        </div>
        <n-button size="small" :disabled="!textOut" @click="copy(textOut)"><template #icon><check :size="13" /></template>复制结果</n-button>
      </section>

      <section class="panel tool-card">
        <div class="panel-head"><h2><clock3 :size="15" /> 时间戳</h2></div>
        <div class="hash-row">
          <n-input v-model:value="tsInput" placeholder="Unix 秒/毫秒 或 ISO 时间" />
          <n-button @click="tsInput = String(Date.now())">当前时间</n-button>
        </div>
        <div v-if="tsResult" class="ts-grid">
          <div><span>本地时间</span><b>{{ tsResult.local }}</b></div>
          <div><span>UTC</span><b>{{ tsResult.utc }}</b></div>
          <div><span>ISO 8601</span><b class="mono">{{ tsResult.iso }}</b></div>
          <div><span>Unix 秒</span><b>{{ tsResult.seconds }}</b></div>
        </div>
        <div v-else class="hash-error">输入数字时间戳或 ISO 字符串</div>
        <p class="tool-note"><wrench :size="13" /> 摄像头/录屏等需要额外设备权限的工具仍在迁移中；这里已覆盖系统、进程、端口、电源、截图、压缩包与文本工具。</p>
      </section>

      <section class="panel tool-card">
        <div class="panel-head">
          <h2><list-tree :size="15" /> 进程管理</h2>
          <n-button size="small" :loading="procBusy" @click="loadProcesses"><template #icon><refresh-cw :size="13" /></template>刷新</n-button>
        </div>
        <div class="hash-row">
          <n-input v-model:value="procQuery" placeholder="进程名筛选" @keyup.enter="loadProcesses" />
          <n-button :loading="procBusy" @click="loadProcesses"><template #icon><search :size="14" /></template>搜索</n-button>
        </div>
        <div class="proc-list">
          <div v-for="p in processes" :key="p.id" class="proc-row">
            <div class="proc-name"><strong>{{ p.name }}</strong><span>PID {{ p.id }} · {{ formatBytes(p.memory) }}</span></div>
            <button class="mini dangerous" title="结束进程" @click="killProcess(p)"><trash2 :size="12" /></button>
          </div>
          <div v-if="!processes.length" class="site-empty">点击刷新加载进程（按内存占用排序，最多 400 个）。</div>
        </div>
      </section>

      <section class="panel tool-card">
        <div class="panel-head">
          <h2><network :size="15" /> 端口</h2>
          <n-button size="small" :loading="portBusy" @click="loadPorts"><template #icon><refresh-cw :size="13" /></template>刷新</n-button>
        </div>
        <div class="proc-list port-list">
          <div v-for="p in ports.filter(x => x.state === 'LISTENING')" :key="p.local + p.pid" class="proc-row">
            <div class="proc-name"><strong>{{ p.local }}</strong><span>{{ p.state }} · PID {{ p.pid }}</span></div>
            <button class="mini dangerous" title="结束占用进程" @click="killPort(p)"><trash2 :size="12" /></button>
          </div>
          <div v-if="!ports.length" class="site-empty">点击刷新读取 TCP 监听端口。</div>
        </div>
      </section>

      <section class="panel tool-card">
        <div class="panel-head"><h2><power :size="15" /> 电源与屏幕</h2></div>
        <div class="power-grid">
          <n-button @click="powerAction('lock')"><template #icon><zap :size="14" /></template>锁屏</n-button>
          <n-button @click="powerAction('screenoff')"><template #icon><power :size="14" /></template>关闭屏幕</n-button>
          <n-button @click="powerAction('sleep')"><template #icon><power :size="14" /></template>睡眠</n-button>
          <n-button @click="screenshot"><template #icon><camera :size="14" /></template>全屏截图</n-button>
          <n-button type="error" secondary @click="powerAction('restart')">重启</n-button>
          <n-button type="error" @click="powerAction('shutdown')">关机</n-button>
        </div>
        <div v-if="shotPath" class="hash-result">
          <div class="hash-name"><camera :size="14" /><span>{{ shotPath }}</span></div>
          <n-button size="small" @click="host.reveal(shotPath)"><template #icon><folder-open :size="13" /></template>打开所在目录</n-button>
        </div>
      </section>

      <section class="panel tool-card">
        <div class="panel-head"><h2><file-archive :size="15" /> 压缩包</h2></div>
        <div class="hash-row">
          <n-input v-model:value="archiveFolder" placeholder="源文件夹" />
          <n-button @click="pickArchiveFolder"><template #icon><folder-open :size="14" /></template>选择</n-button>
        </div>
        <div class="hash-row">
          <n-input v-model:value="archiveZip" placeholder="输出 ZIP 路径" />
          <n-button type="primary" :loading="archiveBusy" @click="compressFolder">压缩</n-button>
        </div>
        <div class="hash-row">
          <n-input v-model:value="extractZip" placeholder="ZIP 文件" />
          <n-button @click="pickExtractZip"><template #icon><folder-open :size="14" /></template>选择</n-button>
        </div>
        <div class="hash-row">
          <n-input v-model:value="extractDest" placeholder="解压目标目录" />
          <n-button :loading="archiveBusy" @click="extractZipNow">解压</n-button>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.toolbox-page { display: flex; flex-direction: column; gap: 14px; padding: 18px 22px 22px; overflow-y: auto; }
.toolbox-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; }
.tool-card { padding: 15px 17px; display: flex; flex-direction: column; gap: 10px; }
.tool-card .panel-head { margin-bottom: 2px; }
.sys-card { grid-column: span 2; }
.sys-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 9px 16px; }
.sys-grid > div { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.sys-grid > div.wide { grid-column: span 2; }
.sys-grid span, .ts-grid span { font-size: 10px; color: var(--text-3); }
.sys-grid b, .ts-grid b { font-size: 11.5px; font-weight: 500; color: var(--text-2); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mem-line, .disk-row { display: flex; align-items: center; gap: 8px; font-size: 10.5px; color: var(--text-3); }
.mem-line b, .disk-row b { font-weight: 500; color: var(--text-2); font-variant-numeric: tabular-nums; }
.mem-line .bar, .disk-row .bar { width: 140px; height: 5px; border-radius: 99px; background: var(--panel-3); overflow: hidden; }
.mem-line .bar i, .disk-row .bar i { display: block; height: 100%; background: var(--accent); }
.disks { display: flex; flex-direction: column; gap: 5px; }
.hash-row { display: flex; gap: 8px; align-items: center; }
.hash-row .n-input { flex: 1; }
.hash-result { display: flex; flex-direction: column; gap: 7px; padding: 10px 12px; border-radius: 10px; background: var(--panel-2); border: 1px solid var(--border); }
.hash-name { display: flex; align-items: center; gap: 7px; font-size: 11px; color: var(--text-2); }
.hash-name span { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.hash-name em { font-style: normal; color: var(--text-3); font-size: 10px; }
.hash-value { font-size: 11px; color: var(--success); word-break: break-all; background: rgba(61,220,151,.07); border-radius: 7px; padding: 8px 10px; }
.hash-error { font-size: 11px; color: var(--danger); }
.text-cols { display: grid; grid-template-columns: 1fr 150px 1fr; gap: 9px; align-items: stretch; }
.text-actions { display: flex; flex-direction: column; gap: 5px; justify-content: center; }
.ts-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px 16px; }
.ts-grid > div { display: flex; flex-direction: column; gap: 2px; }
.tool-note { display: flex; align-items: flex-start; gap: 6px; margin: 4px 0 0; font-size: 10px; color: var(--text-3); line-height: 1.5; }
.mono { font-family: ui-monospace, Consolas, monospace; }
.proc-list { max-height: 260px; overflow-y: auto; border: 1px solid var(--border); border-radius: 9px; background: rgba(0,0,0,.12); padding: 4px; display: flex; flex-direction: column; gap: 2px; }
.port-list { max-height: 260px; }
.proc-row { display: flex; align-items: center; gap: 8px; padding: 5px 8px; border-radius: 7px; }
.proc-row:hover { background: var(--panel-2); }
.proc-name { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
.proc-name strong { font-size: 11.5px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.proc-name span { font-size: 10px; color: var(--text-3); }
.mini { width: 24px; height: 24px; flex: none; display: grid; place-items: center; border: none; border-radius: 6px; background: transparent; color: var(--text-3); cursor: pointer; }
.mini:hover { background: var(--panel-3); color: var(--text); }
.mini.dangerous:hover { background: rgba(255,107,129,.14); color: var(--danger); }
.power-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 7px; }
.site-empty { padding: 20px 8px; text-align: center; color: var(--text-3); font-size: 11px; }
@media (max-width: 1000px) {
  .toolbox-grid { grid-template-columns: 1fr; }
  .sys-card { grid-column: span 1; }
  .sys-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .text-cols { grid-template-columns: 1fr; }
  .text-actions { flex-direction: row; flex-wrap: wrap; }
}
</style>
