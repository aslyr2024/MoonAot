<script setup lang="ts">
defineOptions({ name: 'NotesView' })

import { computed, onMounted, ref } from 'vue'
import {
  Bookmark, BookOpen, Highlighter, Pencil, Quote, Search, StickyNote, Trash2,
} from 'lucide-vue-next'
import { NButton, NInput, NModal } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import { api } from '../api'
import { host } from '../host'
import { confirmDialog, toast, toastError } from '../ui'
import type { NoteView } from '../types'
import { formatDate } from '../utils'

const notes = ref<NoteView[]>([])
const loading = ref(true)
const q = ref('')
const type = ref('all')
const editing = ref<NoteView | null>(null)
const editText = ref('')
const saving = ref(false)

const typeOptions = [
  { key: 'all', label: '全部' },
  { key: 'annotation', label: '标注' },
  { key: 'bookmark', label: '书签' },
  { key: 'excerpt', label: '摘录' },
]

const filtered = computed(() => {
  const needle = q.value.trim().toLowerCase()
  return notes.value.filter(n => {
    if (type.value !== 'all' && n.type !== type.value) return false
    if (!needle) return true
    return (n.bookTitle || '').toLowerCase().includes(needle) ||
      (n.text || '').toLowerCase().includes(needle) ||
      (n.note || '').toLowerCase().includes(needle)
  })
})

const groups = computed(() => {
  const map = new Map<string, { bookId: string; title: string; notes: NoteView[] }>()
  for (const n of filtered.value) {
    const key = n.bookId
    if (!map.has(key)) map.set(key, { bookId: n.bookId, title: n.bookTitle || '未知书籍', notes: [] })
    map.get(key)!.notes.push(n)
  }
  return [...map.values()].sort((a, b) =>
    (b.notes[0]?.updatedAt || b.notes[0]?.createdAt || 0) - (a.notes[0]?.updatedAt || a.notes[0]?.createdAt || 0))
})

const counts = computed(() => ({
  all: notes.value.length,
  annotation: notes.value.filter(n => n.type === 'annotation').length,
  bookmark: notes.value.filter(n => n.type === 'bookmark').length,
  excerpt: notes.value.filter(n => n.type === 'excerpt').length,
}))

function iconOf(type: string) {
  if (type === 'bookmark') return Bookmark
  if (type === 'excerpt') return Quote
  return Highlighter
}

function labelOf(type: string) {
  if (type === 'bookmark') return '书签'
  if (type === 'excerpt') return '摘录'
  return '标注'
}

const colorMap: Record<string, string> = {
  yellow: '#f4c542', red: '#f06a70', green: '#43c59e', blue: '#5aa9ff', violet: '#a57bff',
}

async function load() {
  loading.value = true
  try { notes.value = await api.notes() } catch (e) { toastError(e) } finally { loading.value = false }
}

function openBook(n: NoteView) {
  host.openBook(n.bookId)
}

function closeEdit(v: boolean) {
  if (!v) editing.value = null
}

function startEdit(n: NoteView) {
  editing.value = n
  editText.value = n.note || ''
}

async function saveEdit() {
  if (!editing.value) return
  saving.value = true
  try {
    await api.updateNote(editing.value.id, editText.value)
    editing.value.note = editText.value
    toast('笔记已保存')
    editing.value = null
  } catch (e) { toastError(e) } finally { saving.value = false }
}

async function removeNote(n: NoteView) {
  const ok = await confirmDialog({
    title: '删除笔记',
    content: '删除后阅读器中的对应标注也会被移除，确定继续吗？',
    positiveText: '删除', danger: true,
  })
  if (!ok) return
  try {
    await api.deleteNote(n.id)
    notes.value = notes.value.filter(x => x.id !== n.id)
    toast('已删除')
  } catch (e) { toastError(e) }
}

onMounted(load)
</script>

<template>
  <div class="page notes-page">
    <PageHeader title="笔记与标注" :subtitle="`共 ${counts.all} 条 · ${counts.annotation} 条标注 · ${counts.bookmark} 个书签 · ${counts.excerpt} 条摘录`">
      <div class="head-search">
        <n-input v-model:value="q" placeholder="搜索笔记内容或书名…" clearable>
          <template #prefix><search :size="15" /></template>
        </n-input>
      </div>
    </PageHeader>

    <div class="filter-row">
      <div class="chips">
        <button
          v-for="t in typeOptions"
          :key="t.key"
          class="chip"
          :class="{ active: type === t.key }"
          @click="type = t.key"
        >
          {{ t.label }}<span class="chip-count">{{ counts[t.key as keyof typeof counts] }}</span>
        </button>
      </div>
    </div>

    <div class="page-scroll notes-scroll">
      <div v-if="loading" class="recent-loading">正在读取笔记…</div>
      <div v-else-if="!groups.length" class="empty-state inline">
        <div class="empty-icon"><sticky-note :size="42" :stroke-width="1.2" /></div>
        <h3>{{ notes.length ? '没有匹配的笔记' : '还没有笔记或标注' }}</h3>
        <p>{{ notes.length ? '换个关键词或类型试试。' : '在阅读器中选中文字即可高亮、写笔记或添加书签，内容会自动汇总到这里。' }}</p>
      </div>
      <section v-for="g in groups" :key="g.bookId" class="note-group">
        <div class="note-group-head">
          <button class="note-book" @click="openBook(g.notes[0])">
            <book-open :size="15" />
            <span :title="g.title">{{ g.title }}</span>
          </button>
          <span class="note-count">{{ g.notes.length }} 条</span>
        </div>
        <div class="note-list">
          <article
            v-for="n in g.notes"
            :key="n.id"
            class="note-card"
            :style="{ '--note-color': colorMap[n.color || 'yellow'] || colorMap.yellow }"
          >
            <div class="note-rail" />
            <div class="note-body">
              <div class="note-meta">
                <span class="note-type" :class="n.type">
                  <component :is="iconOf(n.type)" :size="13" />
                  {{ labelOf(n.type) }}
                </span>
                <span v-if="n.chapter || n.label" class="note-chapter">{{ n.chapter || n.label }}</span>
                <span class="note-time">{{ formatDate(n.updatedAt || n.createdAt) }}</span>
              </div>
              <blockquote v-if="n.text" class="note-quote">{{ n.text }}</blockquote>
              <p v-if="n.note" class="note-text">{{ n.note }}</p>
              <div class="note-actions">
                <button class="link-btn" @click="openBook(n)"><book-open :size="13" />打开原文</button>
                <button class="link-btn" @click="startEdit(n)"><pencil :size="13" />编辑笔记</button>
                <button class="link-btn danger" @click="removeNote(n)"><trash2 :size="13" />删除</button>
              </div>
            </div>
          </article>
        </div>
      </section>
    </div>

    <n-modal :show="!!editing" preset="card" title="编辑笔记" style="width: 520px; max-width: 92vw" @update:show="closeEdit">
      <div v-if="editing" class="note-edit">
        <blockquote v-if="editing.text" class="note-quote modal-quote">{{ editing.text }}</blockquote>
        <n-input v-model:value="editText" type="textarea" :autosize="{ minRows: 5, maxRows: 12 }" placeholder="写点什么…" />
      </div>
      <template #footer>
        <div class="modal-actions">
          <n-button @click="closeEdit(false)">取消</n-button>
          <n-button type="primary" :loading="saving" :disabled="!editing" @click="saveEdit">保存</n-button>
        </div>
      </template>
    </n-modal>
  </div>
</template>
