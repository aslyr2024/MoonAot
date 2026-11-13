<script setup lang="ts">
import { computed } from 'vue'
import type { MedConcept, MedHit } from './api'
import { linkifyHtml, termIdFromEvent, termWordFromEvent } from './terms'

const props = defineProps<{ concept: MedConcept; compact?: boolean; favorited?: boolean; q?: string }>()
const emit = defineEmits<{
  open: []; search: [q: string]; favorite: [c: MedConcept]; graph: [c: MedConcept]
  openHit: [h: MedHit]; openTerm: [id: string]
}>()

const TYPE_LABEL: Record<string, string> = {
  disease: '疾病', symptom: '症状', drug: '药品', lab: '检验', procedure: '操作',
  guideline: '指南', document: '文书', template: '病历模板', leaflet: '说明书',
  yibao_drug: '医保药品', yibao_herb: '医保中草药', yibao_icd: '医保ICD编码',
  term: '术语', other: '其他',
}
const FIELD_LABEL: Record<string, string> = {
  summary: '概要', symptoms: '症状', diagnosis: '诊断', infusion: '输液方案',
  oral_medication: '口服药', treatment: '治疗', management: '管理', notes: '注意',
  emergency: '急救', common_causes: '常见病因', differential: '鉴别诊断', red_flags: '危险信号',
  recommended_tests: '推荐检查', key_points: '要点', spec: '规格', dose: '用法用量',
  indication: '适应症', abbr: '缩写', normal: '参考范围', unit: '单位', high: '升高', low: '降低',
  next_step: '下一步', contraindication: '禁忌', steps: '步骤', complications: '并发症',
  scope: '适用范围', staging: '分期', drug_recommendations: '用药推荐', monitoring: '随访监测',
  special_populations: '特殊人群', references: '参考文献', content: '正文',
  '通用名': '通用名', '商品名': '商品名', '规格': '规格', '适应症': '适应症',
  '用法用量': '用法用量', '禁忌': '禁忌', '不良反应': '不良反应', '相互作用': '相互作用',
  '贮藏': '贮藏', '备注': '备注', category: '分类', disease: '病种', department: '科室',
  caseName: '病例', docType: '文书类型',
}

const fields = computed(() => {
  try {
    const data = JSON.parse(props.concept.dataJson || '{}')
    return Object.entries(data)
      .filter(([k, v]) => v !== null && v !== '' && !(Array.isArray(v) && v.length === 0) &&
        k !== 'pinyin' && !k.startsWith('_'))
      .slice(0, props.compact ? 4 : 14)
      .map(([k, v]) => ({ key: k, label: FIELD_LABEL[k] || k, value: Array.isArray(v) ? v : [String(v)] }))
  } catch { return [] }
})

function short(v: string) { return v.length > 180 ? v.slice(0, 180) + '…' : v }
function linked(v: unknown) { return linkifyHtml(short(String(v)), props.q || '', props.concept.id) }
function linkedSummary() { return linkifyHtml(short(props.concept.summary || ''), props.q || '', props.concept.id) }

function onCardClick(e: MouseEvent) {
  // 字段里的概念链接优先：点「梅毒」跳梅毒词条，而不是重新打开当前卡片
  const id = termIdFromEvent(e)
  if (id) { e.preventDefault(); e.stopPropagation(); emit('openTerm', id); return }
  const word = termWordFromEvent(e)
  if (word) { e.preventDefault(); e.stopPropagation(); emit('search', word); return }
  if (props.compact) emit('open')
}
</script>

<template>
  <div class="med-card" :class="{ compact, clickable: compact }" @click="onCardClick">
    <div class="med-card-head">
      <span class="med-type" :data-type="concept.type">{{ TYPE_LABEL[concept.type] || concept.type }}</span>
      <strong class="med-card-title">{{ concept.name }}</strong>
      <span v-if="concept.category" class="med-cat">{{ concept.category }}</span>
      <span v-if="concept.hits" class="med-hits">书中 {{ concept.hits }} 处</span>
    </div>
    <p v-if="concept.summary" class="med-summary" v-html="linkedSummary()"></p>
    <div class="med-fields">
      <div v-for="f in fields" :key="f.key" class="med-field">
        <span class="med-field-k">{{ f.label }}</span>
        <div class="med-field-v">
          <span v-for="(line, i) in f.value" :key="i" v-html="linked(line)"></span>
        </div>
      </div>
    </div>
    <div v-if="!compact && concept.occurrences?.length" class="med-occ">
      <div class="med-occ-title">在书中出现</div>
      <button v-for="(h, i) in concept.occurrences.slice(0, 12)" :key="i" class="med-occ-item" @click.stop="emit('openHit', h)">
        <span>{{ h.volume || h.bookTitle }} · {{ h.chapterTitle }}</span>
        <em>{{ h.snippet.slice(0, 44) }}…</em>
      </button>
    </div>
    <div v-if="!compact && concept.related?.length" class="med-related">
      <button v-for="r in concept.related.slice(0, 12)" :key="r.id" @click.stop="emit('search', r.name)">{{ r.name }}</button>
    </div>
    <div v-if="!compact" class="med-card-actions">
      <button @click.stop="emit('search', concept.name)">检索全书</button>
      <button @click.stop="emit('graph', concept)">关系图谱</button>
      <button :class="{ on: favorited }" @click.stop="emit('favorite', concept)">{{ favorited ? '★ 已收藏' : '☆ 收藏' }}</button>
    </div>
  </div>
</template>

<style scoped>
.med-card { border: 1px solid var(--border); border-radius: 13px; background: var(--panel); padding: 14px 16px; }
.med-card.compact { padding: 10px 12px; }
.med-card.clickable { cursor: pointer; transition: border-color .16s, transform .16s, background .16s; }
.med-card.clickable:hover { border-color: var(--accent); transform: translateY(-1px); background: var(--panel-2); }
.med-card-head { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.med-card-title { font-size: 15px; color: var(--text); }
.med-type { font-size: 10.5px; padding: 2px 8px; border-radius: 999px; background: rgba(110,168,254,.14); color: var(--accent); }
.med-type[data-type="disease"] { background: rgba(255,123,138,.14); color: #ff7b8a; }
.med-type[data-type="drug"] { background: rgba(62,207,142,.14); color: #3ddc97; }
.med-type[data-type="lab"] { background: rgba(255,180,84,.14); color: #ffb454; }
.med-type[data-type="symptom"] { background: rgba(165,123,255,.14); color: #a57bff; }
.med-cat, .med-hits { font-size: 10.5px; color: var(--text-3); }
.med-hits { margin-left: auto; }
.med-summary { margin: 10px 0 8px; color: var(--text-2); font-size: 12.5px; line-height: 1.7; }
.med-fields { display: flex; flex-direction: column; gap: 7px; margin-top: 8px; }
.med-field { display: grid; grid-template-columns: 76px 1fr; gap: 10px; font-size: 12px; line-height: 1.65; }
.med-field-k { color: var(--text-3); }
.med-field-v { color: var(--text-2); display: flex; flex-direction: column; white-space: pre-wrap; word-break: break-word; }
.med-occ { margin-top: 12px; border-top: 1px solid var(--border); padding-top: 10px; }
.med-occ-title { font-size: 11px; color: var(--text-3); margin-bottom: 6px; }
.med-occ-item { display: flex; flex-direction: column; width: 100%; text-align: left; border: none; background: none; padding: 6px 8px; border-radius: 7px; color: var(--accent); cursor: pointer; }
.med-occ-item:hover { background: var(--panel-2); }
.med-occ-item em { font-style: normal; font-size: 11px; color: var(--text-3); }
.med-related { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 12px; }
.med-related button, .med-card-actions button { border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 8px; padding: 4px 10px; font-size: 11.5px; cursor: pointer; }
.med-related button:hover, .med-card-actions button:hover { border-color: var(--accent); color: var(--accent); }
.med-card-actions button.on { border-color: var(--warning); color: var(--warning); }
.med-card-actions { display: flex; gap: 8px; margin-top: 14px; }
.med-card :deep(.med-term) { color: var(--accent-2); background: rgba(110, 168, 254, .08); text-decoration: underline dotted; text-underline-offset: 3px; cursor: pointer; border-radius: 3px; padding: 0 1px; }
.med-card :deep(.med-hit) { background: rgba(255, 200, 87, .24); color: #ffd479; border-radius: 3px; padding: 0 2px; text-decoration: underline dotted; text-underline-offset: 3px; cursor: pointer; }
</style>
