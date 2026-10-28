<script setup lang="ts">
import { computed, ref } from 'vue'
import { ArrowRight, Globe2, History, Search, ShieldCheck } from 'lucide-vue-next'
import { NButton, NInput } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import { host } from '../host'
import { toast } from '../ui'

const input = ref('https://www.bing.com')
const recent = ref<string[]>([])
try {
  const raw = localStorage.getItem('moonaot-browser-recent')
  if (raw) recent.value = JSON.parse(raw)
} catch { }

const quick = [
  { label: 'Bing', url: 'https://www.bing.com' },
  { label: '百度', url: 'https://www.baidu.com' },
  { label: '知乎', url: 'https://www.zhihu.com' },
  { label: '豆瓣', url: 'https://www.douban.com' },
  { label: 'GitHub', url: 'https://github.com' },
]

function normalize(value: string) {
  const t = value.trim()
  if (!t) return ''
  if (/^(https?|file|about|data):/i.test(t)) return t
  if (!t.includes(' ') && (t.includes('.') || t.includes(':'))) return 'https://' + t
  return 'https://www.bing.com/search?q=' + encodeURIComponent(t)
}

function open(url?: string) {
  const target = normalize(url ?? input.value)
  if (!target) return
  input.value = target
  const next = [target, ...recent.value.filter(x => x !== target)].slice(0, 10)
  recent.value = next
  try { localStorage.setItem('moonaot-browser-recent', JSON.stringify(next)) } catch { }
  host.openBrowser(target)
  if (!host.available) toast('浏览器模式已在新标签页打开', 'info')
}

function clearRecent() {
  recent.value = []
  try { localStorage.removeItem('moonaot-browser-recent') } catch { }
}
</script>

<template>
  <div class="page browser-page">
    <PageHeader title="浏览器" subtitle="内置 WebView2 单窗口浏览器 · 地址栏 / 前进后退 / 外部链接">
      <n-button type="primary" @click="open()"><template #icon><globe2 :size="15" /></template>打开浏览器</n-button>
    </PageHeader>

    <div class="browser-hero panel">
      <div class="bh-mark"><globe2 :size="40" :stroke-width="1.1" /></div>
      <div class="bh-main">
        <h2>输入网址或搜索内容</h2>
        <div class="bh-input">
          <n-input v-model:value="input" size="large" placeholder="https://example.com 或 搜索关键词" @keyup.enter="open()">
            <template #prefix><search :size="16" /></template>
          </n-input>
          <n-button type="primary" size="large" @click="open()"><template #icon><arrow-right :size="16" /></template>打开</n-button>
        </div>
        <div class="bh-quick">
          <button v-for="q in quick" :key="q.url" class="quick-link" @click="open(q.url)">{{ q.label }}</button>
        </div>
        <p class="bh-note"><shield-check :size="13" /> 页面由独立 WebView2 渲染；target=_blank 链接在当前窗口打开，Ctrl+L 聚焦地址栏。</p>
      </div>
    </div>

    <div class="browser-cols">
      <section class="panel browser-recent">
        <div class="panel-head">
          <h2><history :size="15" /> 最近访问</h2>
          <button v-if="recent.length" class="link-btn" @click="clearRecent">清空</button>
        </div>
        <button v-for="u in recent" :key="u" class="recent-row" :title="u" @click="open(u)">
          <globe2 :size="14" /><span>{{ u }}</span><arrow-right :size="13" />
        </button>
        <div v-if="!recent.length" class="recent-empty">还没有访问记录。地址只在本地保存。</div>
      </section>

      <section class="panel browser-about">
        <div class="panel-head"><h2>说明</h2></div>
        <p>这是一个单窗口内置浏览器：顶部地址栏、前进/后退/刷新/主页，页面内新链接会在同一窗口打开。</p>
        <p>正版阅读器/网站工具里的本地站点可以直接用内置浏览器预览；需要多标签或扩展时可点地址栏右侧按钮转到系统浏览器。</p>
      </section>
    </div>
  </div>
</template>

<style scoped>
.browser-page { display: flex; flex-direction: column; gap: 14px; padding: 18px 22px 22px; overflow-y: auto; }
.browser-hero { display: flex; gap: 22px; align-items: center; padding: 26px 28px; }
.bh-mark { width: 96px; height: 96px; flex: none; border-radius: 28px; display: grid; place-items: center; color: var(--accent); background: linear-gradient(145deg, var(--accent-soft), transparent); border: 1px solid var(--accent-ring); }
.bh-main { flex: 1; min-width: 0; }
.bh-main h2 { margin: 0 0 12px; font-size: 18px; }
.bh-input { display: flex; gap: 9px; }
.bh-quick { display: flex; gap: 7px; flex-wrap: wrap; margin-top: 12px; }
.quick-link { border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 999px; padding: 4px 12px; font-size: 11.5px; cursor: pointer; }
.quick-link:hover { border-color: var(--accent-ring); color: var(--accent); }
.bh-note { display: flex; align-items: center; gap: 6px; margin: 14px 0 0; font-size: 10.5px; color: var(--text-3); }
.browser-cols { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
.browser-recent, .browser-about { padding: 15px 17px; }
.recent-row { width: 100%; display: flex; align-items: center; gap: 9px; height: 34px; border: none; border-radius: 8px; background: transparent; color: var(--text-2); cursor: pointer; text-align: left; }
.recent-row:hover { background: var(--panel-2); color: var(--text); }
.recent-row span { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 11.5px; }
.recent-empty { padding: 20px 0; color: var(--text-3); font-size: 11.5px; }
.link-btn { border: none; background: transparent; color: var(--text-3); font-size: 10.5px; cursor: pointer; }
.link-btn:hover { color: var(--danger); }
.browser-about p { margin: 0 0 10px; font-size: 11.5px; color: var(--text-3); line-height: 1.7; }
@media (max-width: 900px) { .browser-cols { grid-template-columns: 1fr; } .browser-hero { flex-direction: column; align-items: flex-start; } }
</style>
