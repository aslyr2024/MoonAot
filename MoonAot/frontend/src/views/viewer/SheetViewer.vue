<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import * as XLSX from 'xlsx'
import { RefreshCw, X } from 'lucide-vue-next'
import { urls } from '../../api'
import { host } from '../../host'
import { basename } from '../../utils'

const route = useRoute()
const path = ref(String(route.query.path || ''))
const loading = ref(true)
const error = ref('')
const sheetNames = ref<string[]>([])
const active = ref('')
const rows = ref<string[][]>([])
const truncated = ref(false)
const cols = ref(0)
const MAX_ROWS = 5000
const MAX_COLS = 200

let workbook: XLSX.WorkBook | null = null

function renderSheet(name: string) {
  active.value = name
  const ws = workbook?.Sheets[name]
  if (!ws) { rows.value = []; cols.value = 0; return }
  const data = XLSX.utils.sheet_to_json<any[]>(ws, { header: 1, raw: false, defval: '' }) as any[][]
  truncated.value = data.length > MAX_ROWS || (data[0]?.length || 0) > MAX_COLS
  const out: string[][] = []
  for (let r = 0; r < Math.min(data.length, MAX_ROWS); r++) {
    const src = data[r] || []
    const line: string[] = []
    for (let c = 0; c < Math.min(src.length, MAX_COLS); c++) line.push(src[c] == null ? '' : String(src[c]))
    out.push(line)
  }
  cols.value = out.reduce((m, r) => Math.max(m, r.length), 0)
  rows.value = out
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const ext = path.value.toLowerCase().slice(path.value.lastIndexOf('.'))
    const res = await fetch(urls.media(path.value))
    if (!res.ok) throw new Error('读取文件失败：' + res.status)
    if (ext === '.csv') {
      const text = await res.text()
      workbook = XLSX.read(text, { type: 'string', raw: false })
    } else {
      const buf = await res.arrayBuffer()
      workbook = XLSX.read(buf, { type: 'array', cellDates: true })
    }
    sheetNames.value = workbook.SheetNames
    if (sheetNames.value.length) renderSheet(sheetNames.value[0])
    host.setTitle(basename(path.value))
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

const extLabel = computed(() => path.value.toLowerCase().endsWith('.csv') ? 'CSV' : 'EXCEL')

onMounted(load)
</script>

<template>
  <div class="viewer sheet-viewer">
    <header class="viewer-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="host.closeWindow()"><x :size="17" /></button>
        <div class="viewer-name">
          <strong :title="path">{{ basename(path) || '表格' }}</strong>
          <span>{{ extLabel }} · {{ rows.length }} 行 × {{ cols }} 列<template v-if="truncated"> · 已截断</template></span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="刷新" @click="load"><refresh-cw :size="16" /></button>
      </div>
    </header>
    <div v-if="sheetNames.length > 1" class="sheet-tabs">
      <button v-for="n in sheetNames" :key="n" :class="{ cur: n === active }" @click="renderSheet(n)">{{ n }}</button>
    </div>
    <div v-if="loading" class="viewer-loading"><span>正在解析表格…</span></div>
    <div v-else-if="error" class="viewer-error">{{ error }}</div>
    <div v-else class="sheet-scroll">
      <table class="sheet-table">
        <tbody>
          <tr v-for="(r, ri) in rows" :key="ri">
            <th class="row-no">{{ ri + 1 }}</th>
            <td v-for="(c, ci) in r" :key="ci">{{ c }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
.sheet-viewer { display: flex; flex-direction: column; height: 100%; background: #10141b; }
.sheet-tabs { flex: none; display: flex; gap: 4px; padding: 6px 10px; overflow-x: auto; border-bottom: 1px solid rgba(255,255,255,.08); }
.sheet-tabs button { flex: none; padding: 4px 12px; border-radius: 6px; border: 1px solid rgba(255,255,255,.1); background: #171d27; color: #aab3c0; font-size: 11px; cursor: pointer; }
.sheet-tabs button.cur { border-color: var(--accent); color: #fff; background: rgba(88,166,255,.16); }
.sheet-scroll { flex: 1; min-height: 0; overflow: auto; }
.sheet-table { border-collapse: collapse; font-size: 12px; color: #d7dee8; }
.sheet-table th, .sheet-table td { border: 1px solid rgba(255,255,255,.08); padding: 4px 8px; white-space: nowrap; max-width: 420px; overflow: hidden; text-overflow: ellipsis; }
.sheet-table th.row-no { position: sticky; left: 0; background: #171d27; color: #6d7887; font-weight: 500; min-width: 44px; }
.sheet-table tr:first-child td { background: #171d27; font-weight: 600; position: sticky; top: 0; }
.sheet-table tr:nth-child(even) td { background: rgba(255,255,255,.015); }
</style>
