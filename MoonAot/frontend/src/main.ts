import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import './styles.css'
import { initThemeFromStorage } from './theme'
import { installHostBridge, notifyReady } from './host'
import {
  finishImport, loadAppInfo, loadFacets, loadSettings, scheduleLibraryRefresh, setImportProgress, state,
} from './store'

initThemeFromStorage()

installHostBridge({
  onLibraryChanged: () => scheduleLibraryRefresh(250),
  onImportProgress: (p) => setImportProgress(p),
  onImportFinished: (r) => finishImport(r),
})

async function boot() {
  try {
    await loadSettings()
  } catch { /* 设置加载失败也继续启动 */ }
  if (!state.settings.appTheme) state.settings.appTheme = 'dark'
  createApp(App).use(router).mount('#app')
  notifyReady()
  loadFacets().catch(() => { })
  loadAppInfo().catch(() => { })
}

void boot()
