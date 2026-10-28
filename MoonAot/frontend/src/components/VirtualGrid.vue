<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { LibraryItem } from '../types'
import BookCard from './BookCard.vue'

const props = withDefaults(defineProps<{
  items: (LibraryItem | undefined)[]
  total: number
  loading: boolean
  itemWidth: number
  gap?: number
  metaHeight?: number
  ensure: (start: number, end: number) => void
}>(), { gap: 22, metaHeight: 78 })

const emit = defineEmits<{
  open: [item: LibraryItem]
  menu: [item: LibraryItem, event: MouseEvent]
  favorite: [item: LibraryItem]
}>()

const scroller = ref<HTMLElement | null>(null)
const width = ref(0)
const viewportHeight = ref(0)
const scrollTop = ref(0)
const gap = computed(() => props.gap)
const cols = computed(() => {
  if (width.value <= 0) return 1
  return Math.max(1, Math.floor((width.value + gap.value) / (props.itemWidth + gap.value)))
})
const cellWidth = computed(() => {
  if (cols.value <= 1) return Math.max(80, width.value)
  return Math.floor((width.value - gap.value * (cols.value - 1)) / cols.value)
})
const rowHeight = computed(() => Math.round(cellWidth.value * 1.5) + props.metaHeight + gap.value)
const totalRows = computed(() => Math.max(0, Math.ceil(props.total / cols.value)))
const totalHeight = computed(() => Math.max(0, totalRows.value * rowHeight.value))

const MAX_RENDER_ROWS = 60

const visibleRows = computed<number[]>(() => {
  if (!totalRows.value || viewportHeight.value <= 0) return []
  // 防止父容器高度链失效时把 10 万行全部塞进 DOM
  const vh = Math.min(viewportHeight.value, Math.max(640, (typeof window !== 'undefined' ? window.innerHeight : 900) * 1.6))
  const overscan = 3
  const start = Math.max(0, Math.floor(scrollTop.value / rowHeight.value) - overscan)
  const end = Math.min(totalRows.value - 1, Math.ceil((scrollTop.value + vh) / rowHeight.value) + overscan)
  const rows: number[] = []
  for (let r = start; r <= end && rows.length < MAX_RENDER_ROWS; r++) rows.push(r)
  return rows
})

function rowIndexes(row: number): number[] {
  const start = row * cols.value
  const end = Math.min(props.total, start + cols.value)
  const arr: number[] = []
  for (let i = start; i < end; i++) arr.push(i)
  return arr
}

function emitEnsure() {
  const rows = visibleRows.value
  if (!rows.length) { props.ensure(0, Math.min(props.total, 24)); return }
  const start = rows[0] * cols.value
  const end = Math.min(props.total, (rows[rows.length - 1] + 1) * cols.value)
  props.ensure(start, end)
}

let raf = 0
function onScroll() {
  if (raf) return
  raf = requestAnimationFrame(() => {
    raf = 0
    scrollTop.value = scroller.value?.scrollTop ?? 0
  })
}

let ro: ResizeObserver | null = null
onMounted(() => {
  const el = scroller.value
  if (!el) return
  const measure = () => {
    width.value = el.clientWidth
    viewportHeight.value = el.clientHeight
  }
  measure()
  ro = new ResizeObserver(measure)
  ro.observe(el)
  nextTick(emitEnsure)
})

onBeforeUnmount(() => {
  ro?.disconnect()
  if (raf) cancelAnimationFrame(raf)
})

watch([visibleRows, cols, () => props.total], () => emitEnsure())
watch(() => props.itemWidth, () => nextTick(() => {
  if (scroller.value) { width.value = scroller.value.clientWidth; viewportHeight.value = scroller.value.clientHeight }
}))

defineExpose({
  scrollToTop() {
    if (scroller.value) scroller.value.scrollTop = 0
    scrollTop.value = 0
  },
  el: scroller,
})
</script>

<template>
  <div ref="scroller" class="vgrid-scroll" @scroll.passive="onScroll">
    <div v-if="loading && total === 0" class="vgrid-skeleton">
      <div v-for="i in 18" :key="i" class="sk-card" :style="{ width: Math.min(itemWidth, 220) + 'px' }">
        <div class="sk-cover" />
        <div class="sk-line w70" />
        <div class="sk-line w45" />
      </div>
    </div>

    <div v-else class="vgrid-canvas" :style="{ height: totalHeight + 'px' }">
      <div
        v-for="row in visibleRows"
        :key="row"
        class="vgrid-row"
        :style="{ transform: `translateY(${row * rowHeight}px)`, gap: gap + 'px', height: Math.round(cellWidth * 1.5) + metaHeight + 'px' }"
      >
        <template v-for="idx in rowIndexes(row)" :key="idx">
          <BookCard
            v-if="items[idx]"
            :item="items[idx]!"
            :width="cellWidth"
            @open="emit('open', $event)"
            @menu="(item, ev) => emit('menu', item, ev)"
            @favorite="emit('favorite', $event)"
          />
          <div v-else class="book-card sk-card" :style="{ width: cellWidth + 'px' }">
            <div class="sk-cover" :style="{ height: Math.round(cellWidth * 1.5) + 'px' }" />
            <div class="sk-line w70" />
            <div class="sk-line w45" />
          </div>
        </template>
      </div>
    </div>
  </div>
</template>
