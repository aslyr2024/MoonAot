<script setup lang="ts">
import { computed, ref, watch } from 'vue'

const props = withDefaults(defineProps<{
  total: number
  page: number
  pageSize: number
  sizes?: number[]
  label?: string
}>(), {
  sizes: () => [4, 8, 12, 20, 50],
  label: '条',
})

const emit = defineEmits<{
  'update:page': [n: number]
  'update:pageSize': [n: number]
  change: []
}>()

const jump = ref('')
const pages = computed(() => Math.max(1, Math.ceil(Math.max(0, props.total) / Math.max(1, props.pageSize))))

const pageList = computed<(number | string)[]>(() => {
  const total = pages.value
  const cur = Math.min(Math.max(1, props.page), total)
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1)
  const set = new Set<number>([1, total, cur, cur - 1, cur + 1])
  if (cur <= 3) [2, 3, 4].forEach(n => set.add(n))
  if (cur >= total - 2) [total - 1, total - 2, total - 3].forEach(n => set.add(n))
  const nums = [...set].filter(n => n >= 1 && n <= total).sort((a, b) => a - b)
  const out: (number | string)[] = []
  let last = 0
  for (const n of nums) {
    if (last && n - last > 1) out.push('…')
    out.push(n)
    last = n
  }
  return out
})

function go(n: number) {
  const p = Math.min(pages.value, Math.max(1, n))
  jump.value = ''
  if (p === props.page) return
  emit('update:page', p)
  emit('change')
}

function onSize(e: Event) {
  const n = Number((e.target as HTMLSelectElement).value)
  if (!n || n === props.pageSize) return
  emit('update:pageSize', n)
  emit('update:page', 1)
  emit('change')
}

function doJump() {
  const n = Number.parseInt(jump.value, 10)
  if (Number.isFinite(n)) go(n)
  jump.value = ''
}

// 结果数变少时（换关键词/换筛选）把超出范围的页码收回来
watch(() => props.total, () => {
  if (props.page > pages.value) {
    emit('update:page', pages.value)
    emit('change')
  }
})
</script>

<template>
  <div class="med-pager">
    <span class="med-pager-total">共 <b>{{ total }}</b> {{ label }} · {{ pages }} 页</span>
    <label class="med-pager-size">
      每页
      <select :value="pageSize" @change="onSize">
        <option v-for="s in sizes" :key="s" :value="s">{{ s }}</option>
      </select>
      {{ label }}
    </label>
    <div class="med-pager-btns">
      <button :disabled="page <= 1" title="首页" @click="go(1)">«</button>
      <button :disabled="page <= 1" title="上一页" @click="go(page - 1)">‹</button>
      <template v-for="(p, i) in pageList" :key="i">
        <span v-if="p === '…'" class="med-pager-ell">…</span>
        <button v-else :class="{ on: p === page }" @click="go(Number(p))">{{ p }}</button>
      </template>
      <button :disabled="page >= pages" title="下一页" @click="go(page + 1)">›</button>
      <button :disabled="page >= pages" title="末页" @click="go(pages)">»</button>
    </div>
    <div class="med-pager-jump">
      跳至 <input v-model="jump" inputmode="numeric" placeholder="页码" @keydown.enter="doJump" /> 页
      <button @click="doJump">GO</button>
    </div>
  </div>
</template>

<style scoped>
.med-pager { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; padding: 12px 2px 4px; font-size: 11.5px; color: var(--text-3); }
.med-pager-total b { color: var(--text-2); font-weight: 600; }
.med-pager-size { display: inline-flex; align-items: center; gap: 5px; }
.med-pager-size select { height: 26px; border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 6px; padding: 0 5px; font-size: 11px; }
.med-pager-btns { display: inline-flex; gap: 3px; }
.med-pager-btns button { min-width: 27px; height: 27px; border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 7px; font-size: 11.5px; cursor: pointer; padding: 0 5px; }
.med-pager-btns button:hover:not(:disabled) { border-color: var(--accent); color: var(--accent); }
.med-pager-btns button.on { background: var(--accent); border-color: var(--accent); color: #0a0c10; font-weight: 600; }
.med-pager-btns button:disabled { opacity: .38; cursor: default; }
.med-pager-ell { min-width: 18px; text-align: center; color: var(--text-3); }
.med-pager-jump { display: inline-flex; align-items: center; gap: 5px; margin-left: auto; }
.med-pager-jump input { width: 52px; height: 26px; border: 1px solid var(--border); background: var(--panel-2); color: var(--text); border-radius: 6px; padding: 0 7px; font-size: 11px; text-align: center; }
.med-pager-jump button { height: 26px; border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); border-radius: 6px; padding: 0 9px; font-size: 11px; cursor: pointer; }
.med-pager-jump button:hover { border-color: var(--accent); color: var(--accent); }
</style>
