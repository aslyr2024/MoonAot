import { reactive, ref } from 'vue'
import { api } from './api'
import { applyTheme } from './theme'
import { toast, toastError } from './ui'
import type {
  AppInfo, AppSettings, Facets, FontsDto, ImportResult, MediaSettings, StatsDto,
} from './types'

const defaultMedia = (): MediaSettings => ({
  imageSlideSeconds: 5,
  comicMode: false,
  comicWidth: 'default',
  comicAutoPage: false,
  recursiveImages: false,
  musicBackgroundSeconds: 8,
  musicShowLyrics: true,
  videoAutoResume: true,
  imageTransition: 'fade',
  imageBackgroundMusic: null,
  imageBackgroundMusicFolder: null,
  imageBackgroundMusicPlaylist: null,
  imageBackgroundMusicIndex: 0,
  imageSlideLoop: true,
  musicVolume: 0.9,
  musicMode: 'order',
  musicTab: 'list',
  videoVolume: 1,
  videoShowList: true,
  musicBackgroundFolder: null,
  musicBackgroundIndex: 0,
  musicBackgroundRandom: false,
  musicBackgroundFit: 'cover',
  musicBackgroundBlur: 5,
})

export const defaultSettings = (): AppSettings => ({
  appTheme: 'dark',
  sortMode: 'recent',
  sortDesc: true,
  showCovers: true,
  libraryView: 'grid',
  gridSize: 168,
  readerSettingsJson: '{}',
  defaultCoverPath: null,
  importCovers: true,
  useDefaultCover: true,
  autoRefreshLibrary: true,
  recentFolders: [],
  lastFolder: null,
  media: defaultMedia(),
})

export const READER_DEFAULTS = {
  defaultFontSize: 18,
  minimumFontSize: 8,
  fontWeight: 400,
  lineHeight: '1.8',
  defaultFont: 'Sans-Serif Font',
  defaultCJKFont: 'FZKeBenFangSong',
  serifFont: 'Bitter',
  sansSerifFont: 'Roboto',
  monospaceFont: 'Consolas',
  fullJustification: true,
  textIndent: 0,
  paragraphMargin: 0.8,
  letterSpacing: 0,
  theme: 'light',
  scrolled: false,
  continuousScroll: false,
  maxColumnCount: 2,
  marginTopPx: 40,
  marginBottomPx: 40,
  marginLeftPx: 20,
  marginRightPx: 20,
  animated: false,
  showHeader: true,
  showFooter: true,
  showPageNumber: true,
}

export const state = reactive({
  settings: defaultSettings(),
  ready: false,
  facets: null as Facets | null,
  stats: null as StatsDto | null,
  fonts: { fonts: [], embedded: [] } as FontsDto,
  appInfo: null as AppInfo | null,
  importState: {
    active: false,
    done: 0,
    total: 0,
    current: '',
    result: null as ImportResult | null,
  },
})

/** 外部（阅读器进度/导入完成）导致书库变化时递增。 */
export const libraryTick = ref(0)
let refreshTimer: ReturnType<typeof setTimeout> | null = null

export function scheduleLibraryRefresh(delay = 350) {
  if (refreshTimer) clearTimeout(refreshTimer)
  refreshTimer = setTimeout(() => {
    refreshTimer = null
    libraryTick.value++
    loadFacets().catch(() => { })
  }, delay)
}

export async function loadSettings(force = false): Promise<void> {
  if (state.ready && !force) return
  try {
    const s = await api.appSettings()
    const merged: AppSettings = {
      ...defaultSettings(),
      ...s,
      media: { ...defaultMedia(), ...(s.media || {}) },
      recentFolders: s.recentFolders || [],
    }
    state.settings = merged
    applyTheme(merged.appTheme || 'dark')
    state.ready = true
  } catch (e) {
    toastError(e)
    applyTheme(state.settings.appTheme)
    state.ready = true
  }
}

export async function saveSettings(patch: Partial<AppSettings>): Promise<void> {
  const next: AppSettings = {
    ...state.settings,
    ...patch,
    media: { ...state.settings.media, ...(patch.media || {}) },
  }
  state.settings = next
  if (patch.appTheme) applyTheme(next.appTheme)
  try {
    await api.saveAppSettings(patch as Record<string, any>)
  } catch (e) {
    toastError(e)
  }
}

export async function loadFacets(): Promise<void> {
  try { state.facets = await api.facets() } catch { /* ignore */ }
}

export async function loadStats(): Promise<StatsDto | null> {
  try {
    state.stats = await api.stats()
    return state.stats
  } catch (e) { toastError(e); return null }
}

export async function loadFonts(): Promise<void> {
  if (state.fonts.fonts.length) return
  try { state.fonts = await api.fonts() } catch { /* ignore */ }
}

export async function loadAppInfo(): Promise<AppInfo | null> {
  try {
    state.appInfo = await api.appInfo()
    return state.appInfo
  } catch { return null }
}

export function setImportProgress(p: { done: number; total: number; current: string }) {
  state.importState.active = true
  state.importState.done = p.done || 0
  state.importState.total = p.total || 0
  state.importState.current = p.current || ''
  state.importState.result = null
}

export function finishImport(r: { ok: boolean; imported: number; skipped: number; failed: number; message?: string }) {
  state.importState.active = true
  state.importState.result = r as ImportResult
  if (r.ok) {
    const parts = [`已导入 ${r.imported} 本`]
    if (r.skipped) parts.push(`跳过 ${r.skipped} 本`)
    if (r.failed) parts.push(`失败 ${r.failed} 本`)
    toast(parts.join('，'))
  } else {
    toast(r.message || '导入失败', 'error')
  }
  scheduleLibraryRefresh(200)
  const snapshot = state.importState.result
  setTimeout(() => {
    if (state.importState.result === snapshot) {
      state.importState.active = false
      state.importState.result = null
    }
  }, 4200)
}

export function closeImportPanel() {
  state.importState.active = false
  state.importState.result = null
}

export function notifyReaderSettingsSaved() {
  toast('排版设置已保存，新打开的阅读器会立即生效')
}
