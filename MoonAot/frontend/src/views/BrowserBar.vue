<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import {
  ArrowLeft, ArrowRight, Bug, Code2, ExternalLink, Globe2, History, Languages, Loader2, Plus,
  RotateCw, Search, Star, Trash2, X,
} from 'lucide-vue-next'

interface TabInfo { id: string; title: string; url: string; loading: boolean; active: boolean }
interface Mark { url: string; title: string; at: number }

const BAR_MIN = 78
const BAR_OPEN = 360

const url = ref('')
const title = ref('')
const loading = ref(false)
const canBack = ref(false)
const canForward = ref(false)
const editing = ref(false)
const input = ref<HTMLInputElement | null>(null)
const tabs = ref<TabInfo[]>([])
const menuOpen = ref(false)
const menuTab = ref<'marks' | 'history' | 'tools'>('tools')
const bookmarks = ref<Mark[]>([])
const history = ref<Mark[]>([])
const injectCode = ref('')
const injectMsg = ref('')
let lastHistory = ''

try { bookmarks.value = JSON.parse(localStorage.getItem('moonaot-browser-bookmarks') || '[]') } catch { }
try { history.value = JSON.parse(localStorage.getItem('moonaot-browser-history') || '[]') } catch { }
try { injectCode.value = localStorage.getItem('moonaot-browser-inject') || '' } catch { }

function post(type: string, data: Record<string, any> = {}) {
  try { (window as any).chrome?.webview?.postMessage(JSON.stringify({ type, ...data })) } catch { }
}
function saveMarks() {
  try { localStorage.setItem('moonaot-browser-bookmarks', JSON.stringify(bookmarks.value)) } catch { }
}
function saveHistory() {
  try { localStorage.setItem('moonaot-browser-history', JSON.stringify(history.value)) } catch { }
}

const activeTab = computed(() => tabs.value.find(t => t.active))
const bookmarked = computed(() => !!url.value && bookmarks.value.some(b => b.url === url.value))
const currentTitle = computed(() => title.value || activeTab.value?.title || '新标签页')

function onState(s: any) {
  if (!editing.value) url.value = s?.url || ''
  title.value = s?.title || ''
  loading.value = !!s?.loading
  canBack.value = !!s?.canBack
  canForward.value = !!s?.canForward
  if (Array.isArray(s?.tabs)) tabs.value = s.tabs
  recordHistory()
}
;(window as any).__browser = { onState }

function recordHistory() {
  const u = url.value
  if (!u || u === 'about:blank' || u.startsWith('data:')) return
  if (u === lastHistory) return
  lastHistory = u
  const entry = { url: u, title: title.value || u, at: Date.now() }
  history.value = [entry, ...history.value.filter(h => h.url !== u)].slice(0, 500)
  saveHistory()
}

function go() {
  const value = url.value.trim()
  if (!value) return
  post('navigate', { url: value })
  input.value?.blur()
}
function back() { post('back') }
function forward() { post('forward') }
function reload() { post(loading.value ? 'stop' : 'reload') }
function home() { post('home') }
function external() { if (url.value) post('external', { url: url.value }) }
function newTab() { post('newTab', { url: 'https://www.bing.com' }) }
function activate(id: string) { post('activateTab', { id }) }
function closeTab(id: string, ev: MouseEvent) { ev.stopPropagation(); post('closeTab', { id }) }
function openUrl(u: string) { menuOpen.value = false; post('navigate', { url: u }) }
function openTab(u: string) { menuOpen.value = false; post('newTab', { url: u }) }

function toggleBookmark() {
  if (!url.value) return
  if (bookmarked.value) {
    bookmarks.value = bookmarks.value.filter(b => b.url !== url.value)
  } else {
    bookmarks.value = [{ url: url.value, title: currentTitle.value, at: Date.now() }, ...bookmarks.value].slice(0, 300)
  }
  saveMarks()
}
function removeBookmark(u: string) {
  bookmarks.value = bookmarks.value.filter(b => b.url !== u)
  saveMarks()
}
function removeHistory(u: string) {
  history.value = history.value.filter(h => h.url !== u)
  saveHistory()
}
function clearHistory() { history.value = []; saveHistory() }

function toggleMenu(tab: 'marks' | 'history' | 'tools') {
  if (menuOpen.value && menuTab.value === tab) { menuOpen.value = false; return }
  menuTab.value = tab
  menuOpen.value = true
}
function closeMenu() { menuOpen.value = false }

function translate(lang: string) {
  post('translate', { lang })
  injectMsg.value = lang.startsWith('en') ? '正在翻译为中文…' : '正在翻译为英文…'
}
function runInject(persist: boolean) {
  const code = injectCode.value.trim()
  if (!code) return
  try { localStorage.setItem('moonaot-browser-inject', code) } catch { }
  post(persist ? 'persistInject' : 'inject', { code })
  injectMsg.value = persist ? '脚本已设置为每次加载时注入' : '脚本已执行'
}
function devtools() { post('devtools'); menuOpen.value = false }

watch(menuOpen, (open) => post('barHeight', { height: open ? BAR_OPEN : BAR_MIN }))
watch(menuTab, () => { if (menuOpen.value) post('barHeight', { height: BAR_OPEN }) })

function onKey(ev: KeyboardEvent) {
  if (ev.key === 'Escape') {
    if (menuOpen.value) closeMenu()
    else post('stop')
    ;(ev.target as HTMLElement)?.blur?.()
  }
}
function onGlobalKey(ev: KeyboardEvent) {
  if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'l') { ev.preventDefault(); input.value?.focus(); input.value?.select() }
  else if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 't') { ev.preventDefault(); newTab() }
  else if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'w') { ev.preventDefault(); if (activeTab.value) post('closeTab', { id: activeTab.value.id }) }
}

onMounted(() => {
  post('ready')
  window.addEventListener('keydown', onGlobalKey)
})
</script>

<template>
  <div class="bb-root">
    <div class="bb-tabs">
      <button
        v-for="t in tabs"
        :key="t.id"
        class="bb-tab"
        :class="{ active: t.active }"
        :title="t.url"
        @click="activate(t.id)"
        @auxclick.middle.prevent="post('closeTab', { id: t.id })"
      >
        <loader2 v-if="t.loading" :size="11" class="bb-spin" />
        <globe2 v-else :size="11" />
        <span>{{ t.title || '新标签页' }}</span>
        <span class="bb-tab-x" @click="closeTab(t.id, $event)"><x :size="11" /></span>
      </button>
      <button class="bb-newtab" title="新建标签页 (Ctrl+T)" @click="newTab"><plus :size="14" /></button>
      <div class="bb-tabs-space" />
    </div>

    <div class="bb-bar">
      <button class="bb-btn" :disabled="!canBack" title="后退" @click="back"><arrow-left :size="15" /></button>
      <button class="bb-btn" :disabled="!canForward" title="前进" @click="forward"><arrow-right :size="15" /></button>
      <button class="bb-btn" :title="loading ? '停止' : '刷新'" @click="reload">
        <x v-if="loading" :size="15" /><rotate-cw v-else :size="15" />
      </button>
      <button class="bb-btn" title="主页" @click="home"><globe2 :size="15" /></button>
      <div class="bb-address" :class="{ focused: editing }">
        <search :size="12" class="bb-lock" />
        <input
          ref="input"
          v-model="url"
          spellcheck="false"
          placeholder="输入网址或搜索内容，回车打开"
          @focus="editing = true"
          @blur="editing = false"
          @keydown.enter.prevent="go"
          @keydown="onKey"
        />
        <loader2 v-if="loading" :size="13" class="bb-spin" />
      </div>
      <button class="bb-btn" :class="{ starred: bookmarked }" :title="bookmarked ? '取消收藏' : '收藏此页'" @click="toggleBookmark">
        <star :size="15" :fill="bookmarked ? 'currentColor' : 'none'" />
      </button>
      <button class="bb-btn" :class="{ active: menuOpen }" title="菜单 / 书签 / 历史 / 工具" @click="toggleMenu('tools')">☰</button>
      <button class="bb-btn" title="在系统浏览器打开" @click="external"><external-link :size="15" /></button>
    </div>

    <div v-if="menuOpen" class="bb-panel">
      <div class="bb-panel-tabs">
        <button :class="{ active: menuTab === 'tools' }" @click="toggleMenu('tools')"><bug :size="13" />工具</button>
        <button :class="{ active: menuTab === 'marks' }" @click="toggleMenu('marks')"><star :size="13" />书签</button>
        <button :class="{ active: menuTab === 'history' }" @click="toggleMenu('history')"><history :size="13" />历史</button>
        <span class="bb-panel-space" />
        <button class="bb-panel-close" title="收起" @click="closeMenu"><x :size="13" /></button>
      </div>

      <div v-if="menuTab === 'marks'" class="bb-panel-body">
        <div v-for="b in bookmarks" :key="b.url" class="bb-mark">
          <button class="bb-mark-main" :title="b.url" @click="openUrl(b.url)">
            <globe2 :size="13" /><span>{{ b.title || b.url }}</span>
          </button>
          <button class="bb-mini" title="新标签打开" @click="openTab(b.url)"><plus :size="12" /></button>
          <button class="bb-mini danger" title="删除" @click="removeBookmark(b.url)"><trash2 :size="12" /></button>
        </div>
        <div v-if="!bookmarks.length" class="bb-empty">点地址栏右侧星标收藏当前页。</div>
      </div>

      <div v-else-if="menuTab === 'history'" class="bb-panel-body">
        <div class="bb-panel-head"><span>{{ history.length }} 条记录</span><button class="bb-link" @click="clearHistory">清空</button></div>
        <div v-for="h in history.slice(0, 120)" :key="h.url + h.at" class="bb-mark">
          <button class="bb-mark-main" :title="h.url" @click="openUrl(h.url)">
            <globe2 :size="13" /><span>{{ h.title || h.url }}</span>
          </button>
          <button class="bb-mini danger" title="删除" @click="removeHistory(h.url)"><trash2 :size="12" /></button>
        </div>
        <div v-if="!history.length" class="bb-empty">暂无历史记录。</div>
      </div>

      <div v-else class="bb-panel-body bb-tools">
        <div class="bb-tool-row">
          <button class="bb-tool" @click="translate('en|zh-CN')"><languages :size="15" /><span>翻译为中文</span></button>
          <button class="bb-tool" @click="translate('zh-CN|en')"><languages :size="15" /><span>翻译为英文</span></button>
          <button class="bb-tool" @click="devtools"><bug :size="15" /><span>开发者工具</span></button>
          <button class="bb-tool" @click="post('newTab', { url: 'https://www.bing.com' })"><plus :size="15" /><span>新建标签</span></button>
        </div>
        <div class="bb-inject">
          <div class="bb-inject-head"><code2 :size="14" /><span>页面脚本注入</span><span v-if="injectMsg" class="bb-msg">{{ injectMsg }}</span></div>
          <textarea v-model="injectCode" spellcheck="false" placeholder="输入 JavaScript，例如：document.body.style.filter='grayscale(1)'"></textarea>
          <div class="bb-inject-actions">
            <button class="bb-action" @click="runInject(false)">执行一次</button>
            <button class="bb-action" @click="runInject(true)">每次加载都注入</button>
          </div>
        </div>
        <div class="bb-tip">翻译使用 MyMemory 公共接口（限制单段 500 字，每页最多 120 段）；开发者工具仅对当前标签打开。</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
:global(html), :global(body), :global(#app) { height: 100%; overflow: hidden; }
.bb-root { height: 100%; display: flex; flex-direction: column; background: #0b0e13; color: #cbd3e0; font-family: "Microsoft YaHei UI", "Segoe UI", sans-serif; border-bottom: 1px solid rgba(255,255,255,.09); }
.bb-tabs { height: 32px; flex: none; display: flex; align-items: flex-end; gap: 3px; padding: 0 6px; overflow-x: auto; scrollbar-width: none; }
.bb-tabs::-webkit-scrollbar { display: none; }
.bb-tab { height: 28px; max-width: 210px; min-width: 96px; flex: 0 0 auto; display: flex; align-items: center; gap: 6px; padding: 0 7px 0 9px; border: 1px solid transparent; border-bottom: none; border-radius: 8px 8px 0 0; background: rgba(255,255,255,.035); color: rgba(255,255,255,.58); font-size: 11.5px; cursor: pointer; }
.bb-tab.active { background: rgba(255,255,255,.09); color: #e9ecf3; border-color: rgba(255,255,255,.09); }
.bb-tab > span:first-of-type { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.bb-tab-x { flex: none; width: 16px; height: 16px; display: grid; place-items: center; border-radius: 4px; color: rgba(255,255,255,.35); }
.bb-tab-x:hover { background: rgba(255,255,255,.12); color: #fff; }
.bb-newtab { width: 28px; height: 28px; flex: none; border: none; border-radius: 7px; background: transparent; color: rgba(255,255,255,.55); display: grid; place-items: center; cursor: pointer; }
.bb-newtab:hover { background: rgba(255,255,255,.09); color: #fff; }
.bb-tabs-space { flex: 1; }
.bb-bar { height: 46px; flex: none; display: flex; align-items: center; gap: 4px; padding: 0 7px; background: #0b0e13; border-top: 1px solid rgba(255,255,255,.06); }
.bb-btn { width: 30px; height: 30px; flex: none; display: grid; place-items: center; border: none; border-radius: 8px; background: transparent; color: rgba(255,255,255,.62); cursor: pointer; font-size: 15px; }
.bb-btn:hover:not(:disabled) { background: rgba(255,255,255,.09); color: #fff; }
.bb-btn.active, .bb-btn.starred { background: rgba(110,168,254,.16); color: #93c1ff; }
.bb-btn:disabled { opacity: .28; cursor: default; }
.bb-address { flex: 1; min-width: 0; height: 32px; display: flex; align-items: center; gap: 7px; padding: 0 10px; border-radius: 9px; border: 1px solid rgba(255,255,255,.1); background: rgba(255,255,255,.055); }
.bb-address.focused { border-color: rgba(110,168,254,.55); background: rgba(110,168,254,.08); }
.bb-lock { flex: none; color: rgba(255,255,255,.34); }
.bb-address input { flex: 1; min-width: 0; height: 100%; border: none; outline: none; background: transparent; color: #e9ecf3; font-size: 12px; }
.bb-address input::placeholder { color: rgba(255,255,255,.3); }
.bb-spin { animation: spin 1s linear infinite; color: #6ea8fe; }
@keyframes spin { to { transform: rotate(360deg); } }
.bb-panel { flex: 1; min-height: 0; display: flex; flex-direction: column; border-top: 1px solid rgba(255,255,255,.08); background: #0e1218; }
.bb-panel-tabs { flex: none; height: 36px; display: flex; align-items: center; gap: 4px; padding: 0 8px; border-bottom: 1px solid rgba(255,255,255,.07); }
.bb-panel-tabs button { height: 26px; display: inline-flex; align-items: center; gap: 5px; padding: 0 10px; border: 1px solid transparent; border-radius: 7px; background: transparent; color: rgba(255,255,255,.55); font-size: 11px; cursor: pointer; }
.bb-panel-tabs button.active { background: rgba(110,168,254,.14); border-color: rgba(110,168,254,.3); color: #93c1ff; }
.bb-panel-space { flex: 1; }
.bb-panel-close { color: rgba(255,255,255,.5) !important; }
.bb-panel-body { flex: 1; min-height: 0; overflow-y: auto; padding: 8px 10px 12px; }
.bb-panel-head { display: flex; justify-content: space-between; font-size: 10px; color: rgba(255,255,255,.38); padding: 2px 4px 6px; }
.bb-link { border: none; background: transparent; color: rgba(255,120,140,.8); font-size: 10px; cursor: pointer; }
.bb-mark { display: flex; align-items: center; gap: 4px; border-radius: 7px; padding: 2px; }
.bb-mark:hover { background: rgba(255,255,255,.05); }
.bb-mark-main { flex: 1; min-width: 0; height: 30px; display: flex; align-items: center; gap: 7px; border: none; background: transparent; color: rgba(255,255,255,.68); font-size: 11.5px; cursor: pointer; text-align: left; padding: 0 7px; }
.bb-mark-main span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.bb-mini { width: 24px; height: 24px; flex: none; display: grid; place-items: center; border: none; border-radius: 6px; background: transparent; color: rgba(255,255,255,.45); cursor: pointer; }
.bb-mini:hover { background: rgba(255,255,255,.1); color: #fff; }
.bb-mini.danger:hover { color: #ff8a99; background: rgba(255,107,129,.12); }
.bb-empty { padding: 32px; text-align: center; color: rgba(255,255,255,.3); font-size: 11.5px; }
.bb-tools { display: flex; flex-direction: column; gap: 10px; }
.bb-tool-row { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 7px; }
.bb-tool { height: 38px; display: flex; align-items: center; justify-content: center; gap: 7px; border: 1px solid rgba(255,255,255,.1); border-radius: 9px; background: rgba(255,255,255,.045); color: rgba(255,255,255,.72); font-size: 11.5px; cursor: pointer; }
.bb-tool:hover { border-color: rgba(110,168,254,.45); color: #93c1ff; background: rgba(110,168,254,.08); }
.bb-inject { border: 1px solid rgba(255,255,255,.09); border-radius: 10px; background: rgba(255,255,255,.025); padding: 9px 10px; display: flex; flex-direction: column; gap: 7px; }
.bb-inject-head { display: flex; align-items: center; gap: 6px; font-size: 11px; color: rgba(255,255,255,.62); }
.bb-msg { margin-left: auto; color: #6ea8fe; font-size: 10px; }
.bb-inject textarea { height: 92px; resize: vertical; border: 1px solid rgba(255,255,255,.1); border-radius: 8px; background: rgba(0,0,0,.25); color: #dfe5ee; padding: 8px; outline: none; font: 11px ui-monospace, Consolas, monospace; }
.bb-inject-actions { display: flex; gap: 6px; }
.bb-action { height: 28px; padding: 0 10px; border: 1px solid rgba(255,255,255,.1); border-radius: 7px; background: rgba(255,255,255,.05); color: rgba(255,255,255,.7); font-size: 11px; cursor: pointer; }
.bb-action:hover { border-color: rgba(110,168,254,.45); color: #93c1ff; }
.bb-tip { font-size: 10px; color: rgba(255,255,255,.3); line-height: 1.6; }
</style>
