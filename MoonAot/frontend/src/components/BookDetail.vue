<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  BookOpen, Copy, FolderOpen, Heart, ImagePlus, Loader2, Save, Trash2,
} from 'lucide-vue-next'
import { NButton, NDrawer, NDrawerContent, NInput, NSpin } from 'naive-ui'
import type { BookDetail, LibraryItem } from '../types'
import { api, urls } from '../api'
import { state } from '../store'
import { confirmDialog, toast, toastError } from '../ui'
import { formatBytes, formatDate, formatKind, hashHue, isCoverCapable, percent, timeAgo } from '../utils'
import { host } from '../host'

const props = defineProps<{ show: boolean; item: LibraryItem | null }>()
const emit = defineEmits<{ 'update:show': [boolean]; changed: []; deleted: [string] }>()

const detail = ref<BookDetail | null>(null)
const loading = ref(false)
const saving = ref(false)
const editTitle = ref('')
const editAuthor = ref('')
const coverInput = ref<HTMLInputElement | null>(null)
const coverVersion = ref(0)

const coverable = computed(() => detail.value ? isCoverCapable(detail.value.format) : false)
const thumbFailed = ref(false)
const showThumb = computed(() => !!detail.value && detail.value.hasCover && !thumbFailed.value)
const useDefault = computed(() => !!detail.value && !detail.value.hasCover && coverable.value &&
  state.settings.useDefaultCover && !!state.settings.defaultCoverPath)
const hue = computed(() => hashHue(detail.value?.hash || ''))
const phStyle = computed(() => ({
  background: `radial-gradient(120% 100% at 20% 0%, hsl(${hue.value} 42% 30%), hsl(${(hue.value + 46) % 360} 36% 16%) 70%)`,
}))
const coverSrc = computed(() => detail.value ? `${urls.thumb(detail.value)}&d=${coverVersion.value}` : '')

async function load() {
  if (!props.item) return
  loading.value = true
  detail.value = null
  try {
    detail.value = await api.detail(props.item.hash)
    thumbFailed.value = false
    editTitle.value = detail.value.title
    editAuthor.value = detail.value.author
  } catch (e) {
    toastError(e)
    emit('update:show', false)
  } finally {
    loading.value = false
  }
}

watch(() => [props.show, props.item?.hash], () => {
  if (props.show) load()
})

async function saveMeta() {
  if (!detail.value) return
  const t = editTitle.value.trim()
  if (!t) { toast('书名不能为空', 'warning'); return }
  saving.value = true
  try {
    await api.updateBook(detail.value.hash, { title: t, author: editAuthor.value.trim() })
    detail.value.title = t
    detail.value.author = editAuthor.value.trim()
    toast('已保存')
    emit('changed')
  } catch (e) { toastError(e) }
  finally { saving.value = false }
}

async function toggleFavorite() {
  if (!detail.value) return
  try {
    const next = !detail.value.favorite
    await api.favorite(detail.value.hash, next)
    detail.value.favorite = next
    toast(next ? '已加入收藏' : '已取消收藏')
    emit('changed')
  } catch (e) { toastError(e) }
}

function pickCover() { coverInput.value?.click() }

async function onCoverFile(ev: Event) {
  const input = ev.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file || !detail.value) return
  try {
    const dataUrl = await new Promise<string>((resolve, reject) => {
      const reader = new FileReader()
      reader.onload = () => resolve(String(reader.result || ''))
      reader.onerror = () => reject(new Error('读取图片失败'))
      reader.readAsDataURL(file)
    })
    await api.updateBook(detail.value.hash, { coverDataUrl: dataUrl })
    detail.value.hasCover = true
    thumbFailed.value = false
    coverVersion.value++
    toast('封面已更换')
    emit('changed')
  } catch (e) { toastError(e) }
}

async function removeBook() {
  if (!detail.value) return
  const ok = await confirmDialog({
    title: '从书架移除',
    content: `确定移除《${detail.value.title}》吗？${
      detail.value.sourcePath ? '原文件不会被删除。' : '这本书是导入副本，将从程序数据目录删除。'
    }阅读进度与笔记也会一并移除。`,
    positiveText: '移除',
    danger: true,
  })
  if (!ok || !detail.value) return
  try {
    await api.deleteBook(detail.value.hash)
    emit('deleted', detail.value.hash)
    emit('update:show', false)
    toast('已从书架移除')
  } catch (e) { toastError(e) }
}

function openBook() {
  if (!detail.value) return
  emit('update:show', false)
  host.openBook(detail.value.hash)
}

function reveal() {
  if (!detail.value) return
  host.reveal(detail.value.filePath)
}

async function copyPath() {
  if (!detail.value) return
  try { await navigator.clipboard.writeText(detail.value.filePath); toast('路径已复制') }
  catch { toast('复制失败', 'error') }
}
</script>

<template>
  <n-drawer :show="show" :width="440" placement="right" @update:show="emit('update:show', $event)">
    <n-drawer-content title="书籍详情" closable>
      <div v-if="loading" class="detail-loading"><n-spin size="medium" /></div>
      <div v-else-if="detail" class="detail">
        <div class="detail-cover-wrap">
          <div class="detail-cover">
            <img
              v-if="showThumb || useDefault"
              :src="showThumb ? coverSrc : urls.defaultCover()"
              alt=""
              @error="thumbFailed = true"
            />
            <div v-else class="cov-ph" :style="phStyle">
              <span class="ph-format">{{ formatKind(detail.format) }}</span>
              <strong class="ph-title">{{ detail.title }}</strong>
              <small class="ph-author">{{ detail.author }}</small>
              <i class="ph-spine" />
            </div>
          </div>
          <div class="detail-cover-side">
            <n-button size="small" @click="pickCover"><template #icon><image-plus /></template>更换封面</n-button>
            <n-button size="small" :type="detail.favorite ? 'primary' : 'default'" @click="toggleFavorite">
              <template #icon><heart :size="14" /></template>{{ detail.favorite ? '已收藏' : '收藏' }}
            </n-button>
            <input ref="coverInput" type="file" accept="image/png,image/jpeg,image/webp" hidden @change="onCoverFile" />
            <div class="detail-badges">
              <span class="fmt-chip">{{ formatKind(detail.format) }}</span>
              <span v-if="detail.missing" class="warn-chip">文件缺失</span>
              <span v-if="detail.finished" class="ok-chip">已读</span>
            </div>
          </div>
        </div>

        <div class="detail-progress-row">
          <span>阅读进度</span>
          <strong>{{ percent(detail.progress) }}%</strong>
        </div>
        <div class="book-bar big"><i :style="{ width: percent(detail.progress) + '%' }" /></div>

        <div class="field">
          <label>书名</label>
          <n-input v-model:value="editTitle" placeholder="书名" />
        </div>
        <div class="field">
          <label>作者</label>
          <n-input v-model:value="editAuthor" placeholder="作者" />
        </div>
        <n-button block :loading="saving" @click="saveMeta"><template #icon><save :size="15" /></template>保存修改</n-button>

        <div class="detail-grid">
          <div><span>格式</span><b>{{ formatKind(detail.format) }}</b></div>
          <div><span>大小</span><b>{{ formatBytes(detail.fileSize) }}</b></div>
          <div><span>笔记</span><b>{{ detail.notes }} 条</b></div>
          <div><span>阅读时长</span><b>{{ Math.round(detail.readingSeconds / 60) }} 分钟</b></div>
          <div><span>导入时间</span><b>{{ formatDate(detail.addedAt) }}</b></div>
          <div><span>最近阅读</span><b>{{ timeAgo(detail.lastReadAt) }}</b></div>
          <div class="wide"><span>文件位置</span><b class="path" :title="detail.filePath">{{ detail.filePath }}</b></div>
        </div>

        <div class="detail-path-actions">
          <n-button size="small" @click="reveal"><template #icon><folder-open :size="14" /></template>在文件夹中显示</n-button>
          <n-button size="small" @click="copyPath"><template #icon><copy :size="14" /></template>复制路径</n-button>
        </div>

        <div class="detail-actions">
          <n-button type="primary" size="large" block :disabled="detail.missing" @click="openBook">
            <template #icon><book-open :size="17" /></template>开始阅读
          </n-button>
          <n-button quaternary type="error" size="small" @click="removeBook">
            <template #icon><trash2 :size="14" /></template>从书架移除
          </n-button>
        </div>
      </div>
    </n-drawer-content>
  </n-drawer>
</template>
