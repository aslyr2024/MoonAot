<script setup lang="ts">
import { computed, ref } from 'vue'
import { AlertTriangle, Heart, MoreHorizontal } from 'lucide-vue-next'
import type { LibraryItem } from '../types'
import { urls } from '../api'
import { state, saveSettings } from '../store'
import { api } from '../api'
import { formatKind, hashHue, isCoverCapable, percent } from '../utils'
import { toastError } from '../ui'

const props = defineProps<{ item: LibraryItem; width: number }>()
const emit = defineEmits<{
  open: [item: LibraryItem]
  menu: [item: LibraryItem, event: MouseEvent]
  favorite: [item: LibraryItem]
}>()

const thumbFailed = ref(false)
const defaultFailed = ref(false)
const loaded = ref(false)

const coverable = computed(() => isCoverCapable(props.item.format))
// 只有已提取封面的书才请求缩略图；未提取的书显示缺省封面/占位，
// 绝不在滚动时逐本触发服务端封面提取（10 万本会拖死书架）。
const wantCover = computed(() => state.settings.showCovers && props.item.hasCover)
const useDefault = computed(() => coverable.value && !props.item.hasCover && state.settings.useDefaultCover && !!state.settings.defaultCoverPath)
const thumbSrc = computed(() => urls.thumb(props.item))
const pct = computed(() => percent(props.item.progress))
const hue = computed(() => hashHue(props.item.hash || props.item.title))
const phStyle = computed(() => ({
  background: `radial-gradient(120% 100% at 20% 0%, hsl(${hue.value} 42% 30%), hsl(${(hue.value + 46) % 360} 36% 16%) 70%)`,
}))
const showOverlay = computed(() => props.item.missing || props.item.favorite)

function onThumbError() {
  thumbFailed.value = true
  loaded.value = true
}

async function toggleFavorite(ev: Event) {
  ev.stopPropagation()
  try {
    const next = !props.item.favorite
    await api.favorite(props.item.hash, next)
    emit('favorite', props.item)
  } catch (e) { toastError(e) }
}
</script>

<template>
  <article
    class="book-card"
    :style="{ width: width + 'px' }"
    @click="emit('open', item)"
    @contextmenu.prevent="emit('menu', item, $event)"
  >
    <div class="cover" :style="{ height: Math.round(width * 1.5) + 'px' }">
      <img
        v-if="wantCover && !thumbFailed"
        class="cover-img"
        :class="{ loaded }"
        :src="thumbSrc"
        loading="lazy"
        decoding="async"
        alt=""
        @load="loaded = true"
        @error="onThumbError"
      />
      <img
        v-else-if="useDefault && !defaultFailed"
        class="cover-img cover-default"
        :src="urls.defaultCover()"
        loading="lazy"
        decoding="async"
        alt=""
        @error="defaultFailed = true"
      />
      <div v-else class="cov-ph" :style="phStyle">
        <span class="ph-format">{{ formatKind(item.format) }}</span>
        <strong class="ph-title">{{ item.title || '未命名' }}</strong>
        <small class="ph-author">{{ item.author || '' }}</small>
        <i class="ph-spine" />
      </div>

      <div v-if="item.favorite" class="fav-mark" title="已收藏"><Heart :size="13" fill="currentColor" /></div>
      <div v-if="item.missing" class="miss-mark" title="文件缺失"><AlertTriangle :size="12" /> 文件缺失</div>

      <div class="cover-actions">
        <button class="cover-btn" title="收藏" @click="toggleFavorite">
          <Heart :size="14" :fill="item.favorite ? 'currentColor' : 'none'" />
        </button>
        <button class="cover-btn" title="更多" @click.stop="emit('menu', item, $event)"><MoreHorizontal :size="15" /></button>
      </div>

      <div v-if="pct > 0" class="cover-progress"><i :style="{ width: pct + '%' }" /></div>
      <div v-if="pct > 0" class="pct-badge">{{ pct }}%</div>
    </div>

    <div class="book-meta">
      <div class="book-title" :title="item.title">{{ item.title || '未命名' }}</div>
      <div class="book-row">
        <span class="book-author" :title="item.author">{{ item.author || '未知作者' }}</span>
        <span class="fmt-chip">{{ formatKind(item.format) }}</span>
      </div>
      <div class="book-bar"><i :style="{ width: pct + '%' }" /></div>
    </div>
  </article>
</template>
