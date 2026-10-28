<script setup lang="ts">
defineOptions({ name: 'RecentView' })

import { computed, onActivated, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { BookOpen, Clock3, Copy, FolderOpen, Heart, Info, RefreshCw, Trash2 } from 'lucide-vue-next'
import { NButton } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import BookCard from '../components/BookCard.vue'
import BookDetail from '../components/BookDetail.vue'
import ContextMenu from '../components/ContextMenu.vue'
import type { MenuItem } from '../components/ContextMenu.vue'
import { api } from '../api'
import { libraryTick, scheduleLibraryRefresh, state } from '../store'
import { host } from '../host'
import { confirmDialog, toast, toastError } from '../ui'
import type { LibraryItem } from '../types'

const router = useRouter()
const items = ref<LibraryItem[]>([])
const loading = ref(true)
const detailShow = ref(false)
const detailItem = ref<LibraryItem | null>(null)
const menu = reactive({ show: false, x: 0, y: 0, item: null as LibraryItem | null })

async function load() {
  loading.value = true
  try { items.value = await api.recent(80) } catch (e) { toastError(e) } finally { loading.value = false }
}

function dayKey(ms: number): string {
  const d = new Date(ms)
  const now = new Date()
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()
  const day = new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime()
  const diff = Math.round((start - day) / 86400000)
  if (diff <= 0) return '今天'
  if (diff === 1) return '昨天'
  if (diff < 7) return '本周'
  if (diff < 30) return '本月'
  return `${d.getFullYear()} 年 ${d.getMonth() + 1} 月`
}

const groups = computed(() => {
  const map = new Map<string, LibraryItem[]>()
  for (const b of items.value) {
    const k = dayKey(b.updatedAt || b.createdAt)
    if (!map.has(k)) map.set(k, [])
    map.get(k)!.push(b)
  }
  return [...map.entries()].map(([label, list]) => ({ label, items: list }))
})

function openItem(item: LibraryItem) {
  if (item.missing) {
    detailItem.value = item
    detailShow.value = true
    toast('文件缺失，可在详情中查看路径', 'warning')
    return
  }
  host.openBook(item.hash)
}

function showDetail(item: LibraryItem) {
  detailItem.value = item
  detailShow.value = true
}

function openMenu(item: LibraryItem, ev: MouseEvent) {
  menu.item = item
  menu.x = ev?.clientX ?? 0
  menu.y = ev?.clientY ?? 0
  menu.show = true
}

const menuItems = computed<MenuItem[]>(() => {
  const it = menu.item
  if (!it) return []
  return [
    { key: 'open', label: '打开阅读', icon: BookOpen, disabled: it.missing },
    { key: 'detail', label: '书籍详情', icon: Info },
    { key: 'fav', label: it.favorite ? '取消收藏' : '加入收藏', icon: Heart },
    { key: 'reveal', label: '在文件夹中显示', icon: FolderOpen },
    { key: 'copy', label: '复制文件路径', icon: Copy },
    { key: 'd', label: '', divider: true },
    { key: 'delete', label: '从书架移除', icon: Trash2, danger: true },
  ]
})

async function onMenuSelect(key: string) {
  const it = menu.item
  if (!it) return
  try {
    if (key === 'open') openItem(it)
    else if (key === 'detail') showDetail(it)
    else if (key === 'fav') {
      await api.favorite(it.hash, !it.favorite)
      it.favorite = !it.favorite
      scheduleLibraryRefresh(200)
    } else if (key === 'reveal') {
      const d = await api.detail(it.hash)
      host.reveal(d.filePath)
    } else if (key === 'copy') {
      const d = await api.detail(it.hash)
      await navigator.clipboard.writeText(d.filePath)
      toast('路径已复制')
    } else if (key === 'delete') {
      const ok = await confirmDialog({
        title: '从书架移除',
        content: `确定移除《${it.title}》吗？阅读进度与笔记会一并移除，原文件不会被删除。`,
        positiveText: '移除', danger: true,
      })
      if (!ok) return
      await api.deleteBook(it.hash)
      toast('已移除')
      scheduleLibraryRefresh(0)
      void load()
    }
  } catch (e) { toastError(e) }
}

function onFavorite(item: LibraryItem) {
  item.favorite = !item.favorite
  scheduleLibraryRefresh(260)
}

onMounted(load)
onActivated(() => { void load() })
watch(() => libraryTick.value, () => { void load() })
</script>

<template>
  <div class="page recent-page">
    <PageHeader title="最近阅读" :subtitle="`${items.length} 本 · 继续上次的进度`">
      <n-button @click="load"><template #icon><refresh-cw :size="15" /></template>刷新</n-button>
      <n-button @click="router.push({ name: 'shelf' })"><template #icon><book-open :size="15" /></template>返回书架</n-button>
    </PageHeader>

    <div class="page-scroll">
      <div v-if="loading" class="recent-loading">正在读取最近阅读…</div>
      <template v-else-if="items.length">
        <section v-for="g in groups" :key="g.label" class="recent-group">
          <div class="recent-group-head">
            <clock3 :size="14" />
            <h2>{{ g.label }}</h2>
            <span>{{ g.items.length }} 本</span>
          </div>
          <div class="book-flex">
            <BookCard
              v-for="b in g.items"
              :key="b.hash"
              :item="b"
              :width="state.settings.gridSize"
              @open="openItem"
              @menu="openMenu"
              @favorite="onFavorite"
            />
          </div>
        </section>
      </template>
      <div v-else class="empty-state inline">
        <div class="empty-icon"><clock3 :size="42" :stroke-width="1.2" /></div>
        <h3>还没有阅读记录</h3>
        <p>打开一本书开始阅读后，最近阅读会自动出现在这里。</p>
        <n-button type="primary" @click="router.push({ name: 'shelf' })">去书架看看</n-button>
      </div>
    </div>

    <ContextMenu :show="menu.show" :x="menu.x" :y="menu.y" :items="menuItems" @select="onMenuSelect" @close="menu.show = false" />
    <BookDetail v-model:show="detailShow" :item="detailItem" @changed="load" @deleted="load" />
  </div>
</template>
