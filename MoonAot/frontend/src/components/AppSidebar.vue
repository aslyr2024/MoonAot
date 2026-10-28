<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  BarChart3, BookMarked, BookOpen, ChevronLeft, ChevronRight, Clock3, CloudDownload, FolderOpen, Gamepad2, Globe2,
  LibraryBig, MoonStar, NotebookPen, Plus, Search as SearchIcon, Settings, Sparkles, Wrench,
} from 'lucide-vue-next'
import { state } from '../store'
import { PALETTES, themeNames } from '../theme'
import { saveSettings } from '../store'
import { toast } from '../ui'

const props = defineProps<{ collapsed: boolean }>()
const emit = defineEmits<{ toggle: [] }>()
const route = useRoute()
const router = useRouter()

const items = computed(() => [
  { name: 'shelf', label: '书架', icon: LibraryBig, badge: state.facets?.total ?? state.appInfo?.bookCount ?? 0 },
  { name: 'recent', label: '最近阅读', icon: Clock3, badge: 0 },
  { name: 'notes', label: '笔记', icon: NotebookPen, badge: 0 },
  { name: 'files', label: '媒体库', icon: FolderOpen, badge: 0 },
  { name: 'macro', label: '按键精灵', icon: Gamepad2, badge: 0 },
  { name: 'search', label: '全盘搜索', icon: SearchIcon, badge: 0 },
  { name: 'wiki', label: '维基百科', icon: BookMarked, badge: 0 },
  { name: 'browser', label: '浏览器', icon: Globe2, badge: 0 },
  { name: 'toolbox', label: '工具箱', icon: Wrench, badge: 0 },
  { name: 'sitetools', label: '网站工具', icon: CloudDownload, badge: 0 },
  { name: 'stats', label: '统计', icon: BarChart3, badge: 0 },
  { name: 'settings', label: '设置', icon: Settings, badge: 0 },
])

function isActive(name: string) {
  return route.name === name
}

async function quickImport() {
  if (route.name !== 'shelf') {
    await router.push('/')
    setTimeout(() => window.dispatchEvent(new Event('moonaot-import')), 140)
  } else {
    window.dispatchEvent(new Event('moonaot-import'))
  }
}

async function pickTheme(name: string) {
  if (state.settings.appTheme === name) return
  await saveSettings({ appTheme: name as any })
  toast(`已切换主题：${PALETTES[name as keyof typeof PALETTES]?.label ?? name}`)
}
</script>

<template>
  <aside class="sidebar" :class="{ collapsed }">
    <div class="brand">
      <div class="brand-mark"><MoonStar :size="22" :stroke-width="1.7" /></div>
      <div v-show="!collapsed" class="brand-text">
        <strong>静读天下</strong>
        <span>MoonAot · Native</span>
      </div>
    </div>

    <button class="quick-import" :title="collapsed ? '导入书籍' : undefined" @click="quickImport">
      <Plus :size="17" />
      <span v-show="!collapsed">导入书籍</span>
    </button>

    <nav class="nav">
      <button
        v-for="item in items"
        :key="item.name"
        class="nav-item"
        :class="{ active: isActive(item.name) }"
        :title="collapsed ? item.label : undefined"
        @click="router.push({ name: item.name })"
      >
        <component :is="item.icon" :size="18" :stroke-width="1.8" />
        <span v-show="!collapsed" class="nav-label">{{ item.label }}</span>
        <span v-if="!collapsed && item.badge" class="nav-badge">{{ item.badge > 9999 ? '9999+' : item.badge }}</span>
      </button>
    </nav>

    <div class="sidebar-spacer" />

    <div v-show="!collapsed" class="theme-dots">
      <span class="theme-label"><Sparkles :size="13" /> 外观</span>
      <div class="dots">
        <button
          v-for="t in themeNames"
          :key="t"
          class="dot"
          :class="{ active: state.settings.appTheme === t }"
          :style="{ background: PALETTES[t].accent }"
          :title="PALETTES[t].label"
          @click="pickTheme(t)"
        />
      </div>
    </div>

    <div v-show="!collapsed" class="side-foot">
      <div class="foot-stat">
        <BookOpen :size="13" />
        <span>{{ state.facets?.total ?? 0 }} 本</span>
      </div>
      <div class="foot-stat muted">
        <span>{{ state.appInfo?.isAot ? 'AOT' : 'JIT' }} · WebView2</span>
      </div>
    </div>

    <button class="collapse-btn" :title="collapsed ? '展开侧栏' : '收起侧栏'" @click="emit('toggle')">
      <ChevronRight v-if="collapsed" :size="16" />
      <ChevronLeft v-else :size="16" />
    </button>
  </aside>
</template>
