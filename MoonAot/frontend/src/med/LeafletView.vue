<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { MedConcept } from './api'

const props = defineProps<{ concept: MedConcept }>()
const emit = defineEmits<{ close: [] }>()

const ORDER = [
  '适应症', '用法用量', '药理作用', '禁忌', '不良反应', '注意事项',
  '药物相互作用', '孕妇及哺乳期妇女用药', '儿童用药', '老人用药',
  '药代动力学', '主要成份', '性状', '规格', '贮藏', '有效期',
  '批准文号', '生产企业', '药品分类', '药品性质', '相关疾病', '商品名',
]
const META = new Set(['通用名', '商品名', '汉语拼音', '批准文号', '生产企业', '药品分类', '药品性质', '相关疾病', '规格', '贮藏', '有效期', '来源'])

const data = computed<Record<string, string>>(() => {
  try {
    const o = JSON.parse(props.concept.dataJson || '{}')
    const out: Record<string, string> = {}
    for (const [k, v] of Object.entries(o)) {
      if (v == null || v === '') continue
      out[k] = Array.isArray(v) ? v.join('\n') : String(v)
    }
    return out
  } catch { return {} }
})

const sections = computed(() => {
  const keys = Object.keys(data.value)
  const ordered = ORDER.filter(k => data.value[k])
  const rest = keys.filter(k => !ORDER.includes(k) && !META.has(k))
  return [...ordered, ...rest].map(key => ({
    key,
    title: key,
    lines: (data.value[key] || '').split(/\r?\n/).map(x => x.trim()).filter(Boolean),
  }))
})

const metaSections = computed(() =>
  [...META].filter(k => data.value[k]).map(key => ({ key, title: key, value: data.value[key] })))

const active = ref(sections.value[0]?.key || '')
const body = ref<HTMLElement | null>(null)

function jump(key: string) {
  active.value = key
  const el = body.value?.querySelector(`[data-sec="${CSS.escape(key)}"]`) as HTMLElement | null
  el?.scrollIntoView({ block: 'start', behavior: 'smooth' })
}

function onScroll() {
  const root = body.value
  if (!root) return
  let current = sections.value[0]?.key || ''
  for (const s of sections.value) {
    const el = root.querySelector(`[data-sec="${CSS.escape(s.key)}"]`) as HTMLElement | null
    if (el && el.offsetTop - root.scrollTop <= 40) current = s.key
  }
  if (current) active.value = current
}

function onKey(e: KeyboardEvent) { if (e.key === 'Escape') emit('close') }
onMounted(() => window.addEventListener('keydown', onKey))
onBeforeUnmount(() => window.removeEventListener('keydown', onKey))
</script>

<template>
  <div class="leaflet-mask" @click.self="emit('close')">
    <div class="leaflet">
      <header class="leaflet-head">
        <div class="leaflet-title">
          <span class="leaflet-badge">药品说明书</span>
          <h2>{{ data['通用名'] || concept.name }}</h2>
          <p>
            <span v-if="data['商品名']">商品名：{{ data['商品名'] }}</span>
            <span v-if="data['规格']">规格：{{ data['规格'] }}</span>
            <span v-if="data['生产企业']">{{ data['生产企业'] }}</span>
          </p>
        </div>
        <button class="leaflet-close" title="关闭" @click="emit('close')">×</button>
      </header>

      <div class="leaflet-body">
        <nav class="leaflet-toc">
          <button v-for="s in sections" :key="s.key" :class="{ on: active === s.key }" @click="jump(s.key)">{{ s.title }}</button>
          <div v-if="metaSections.length" class="leaflet-toc-meta">其他信息</div>
        </nav>

        <div ref="body" class="leaflet-content" @scroll="onScroll">
          <article v-for="s in sections" :key="s.key" :data-sec="s.key" class="leaflet-sec">
            <h3>{{ s.title }}</h3>
            <p v-for="(line, i) in s.lines" :key="i">{{ line }}</p>
          </article>

          <article v-if="metaSections.length" class="leaflet-sec leaflet-meta" data-sec="其他信息">
            <h3>其他信息</h3>
            <div v-for="m in metaSections" :key="m.key" class="leaflet-meta-row">
              <span>{{ m.title }}</span><p>{{ m.value }}</p>
            </div>
          </article>

          <footer class="leaflet-foot">
            <p>本说明书来自本地药品数据，仅用于学习与资料查询，不作为用药依据。请以国家药监局批准的最新说明书为准。</p>
            <p class="leaflet-source">来源：{{ concept.source }} · {{ data['来源'] || '' }}</p>
          </footer>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.leaflet-mask { position: fixed; inset: 0; z-index: 90; background: rgba(4, 7, 11, .72); display: grid; place-items: center; padding: 3vh 3vw; }
.leaflet { width: min(1180px, 94vw); height: min(88vh, 900px); display: flex; flex-direction: column; overflow: hidden; border-radius: 16px; border: 1px solid var(--border-strong); background: var(--panel); box-shadow: 0 30px 90px rgba(0,0,0,.6); }
.leaflet-head { flex: none; display: flex; align-items: flex-start; justify-content: space-between; gap: 20px; padding: 18px 22px; border-bottom: 1px solid var(--border); background: linear-gradient(120deg, var(--accent-soft), transparent 60%); }
.leaflet-title h2 { margin: 4px 0 6px; font-size: 22px; letter-spacing: .01em; }
.leaflet-title p { margin: 0; display: flex; flex-wrap: wrap; gap: 14px; font-size: 11.5px; color: var(--text-3); }
.leaflet-badge { display: inline-block; font-size: 10px; letter-spacing: .12em; color: var(--accent); background: var(--accent-soft); border-radius: 999px; padding: 3px 9px; }
.leaflet-close { width: 32px; height: 32px; border: none; border-radius: 8px; background: transparent; color: var(--text-3); font-size: 22px; cursor: pointer; }
.leaflet-close:hover { background: var(--panel-2); color: var(--text); }
.leaflet-body { flex: 1; min-height: 0; display: grid; grid-template-columns: 190px minmax(0, 1fr); }
.leaflet-toc { overflow-y: auto; padding: 16px 10px; border-right: 1px solid var(--border); display: flex; flex-direction: column; gap: 2px; }
.leaflet-toc button { border: none; background: none; color: var(--text-3); text-align: left; border-radius: 7px; padding: 7px 10px; font-size: 12px; cursor: pointer; }
.leaflet-toc button:hover { background: var(--panel-2); color: var(--text); }
.leaflet-toc button.on { background: var(--accent-soft); color: var(--accent); font-weight: 600; }
.leaflet-toc-meta { margin: 12px 0 4px; padding: 0 10px; font-size: 10px; color: var(--text-3); letter-spacing: .1em; }
.leaflet-content { overflow-y: auto; padding: 24px clamp(20px, 4vw, 56px) 60px; scroll-behavior: smooth; }
.leaflet-sec { max-width: 820px; margin: 0 auto 26px; padding-bottom: 18px; border-bottom: 1px dashed var(--border); }
.leaflet-sec:last-of-type { border-bottom: none; }
.leaflet-sec h3 { margin: 0 0 10px; font-size: 16px; color: var(--accent-2); letter-spacing: .02em; }
.leaflet-sec p { margin: 0 0 8px; font-size: 13.5px; line-height: 1.95; color: var(--text); white-space: pre-wrap; }
.leaflet-meta-row { display: grid; grid-template-columns: 110px 1fr; gap: 12px; padding: 4px 0; }
.leaflet-meta-row span { color: var(--text-3); font-size: 12px; }
.leaflet-meta-row p { margin: 0; }
.leaflet-foot { max-width: 820px; margin: 30px auto 0; padding-top: 14px; border-top: 1px solid var(--border); font-size: 11px; line-height: 1.8; color: var(--text-3); }
.leaflet-source { color: var(--text-3); opacity: .7; }
@media (max-width: 760px) {
  .leaflet-body { grid-template-columns: 1fr; }
  .leaflet-toc { display: none; }
}
</style>
