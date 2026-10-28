import { urls } from './api'

export interface HostHandlers {
  onLibraryChanged?: () => void
  onImportProgress?: (p: { done: number; total: number; current: string }) => void
  onImportFinished?: (r: { ok: boolean; imported: number; skipped: number; failed: number; message?: string }) => void
  onDialogResult?: (id: string, path: string | null) => void
}

export interface MacroStepLike {
  kind: string
  keys: string[]
  holdMs: number
  delayMs: number
  x: number
  y: number
  button?: string | null
  wheel: number
  text?: string | null
}

export interface MacroEventHandlers {
  step?: (step: MacroStepLike) => void
  recording?: (state: { recording: boolean; reason?: string | null; steps: MacroStepLike[] }) => void
  state?: (state: { recording: boolean; running: boolean }) => void
}

interface WebViewHost {
  postMessage: (message: string) => void
}

export const isHost = (() => {
  try { return !!(window as any).chrome?.webview?.postMessage } catch { return false }
})()

function webview(): WebViewHost | null {
  try { return (window as any).chrome?.webview ?? null } catch { return null }
}

function post(message: any) {
  const wv = webview()
  if (!wv) return false
  try { wv.postMessage(JSON.stringify(message)); return true } catch { return false }
}

let handlers: HostHandlers = {}
const pendingFolders = new Map<string, (path: string | null) => void>()
const macroHandlers = new Set<MacroEventHandlers>()

/** 订阅按键精灵的录制/状态推送；返回取消订阅函数。 */
export function onMacroEvent(h: MacroEventHandlers): () => void {
  macroHandlers.add(h)
  return () => { macroHandlers.delete(h) }
}

/** 由 App 在挂载时调用；桌面版宿主通过这些回调通知前端。 */
export function installHostBridge(h: HostHandlers) {
  handlers = h
  const api = {
    onLibraryChanged: () => { try { handlers.onLibraryChanged?.() } catch { } },
    openTarget: (path: string) => { if (path) post({ type: 'openTarget', path }) },
    openViewer: (kind: string, path = '', extra?: Record<string, string>) => {
      if (kind) post({ type: 'openViewer', kind, path, extra: extra || {} })
    },
    openBrowser: (url: string) => { if (url) post({ type: 'openBrowser', url }) },
    onImportProgress: (p: any) => { try { handlers.onImportProgress?.(p) } catch { } },
    onImportFinished: (r: any) => {
      try {
        handlers.onImportFinished?.({
          ok: !!r?.ok, imported: Number(r?.imported ?? 0), skipped: Number(r?.skipped ?? 0),
          failed: Number(r?.failed ?? 0), message: r?.message ?? r?.error ?? null,
        })
      } catch { }
    },
    dialogResult: (id: string, path: string | null) => {
      const resolve = pendingFolders.get(id)
      if (resolve) { pendingFolders.delete(id); resolve(path || null) }
      try { handlers.onDialogResult?.(id, path || null) } catch { }
    },
    onOpenPath: (_path: string) => { },
    onMacroRecordStep: (step: MacroStepLike) => {
      macroHandlers.forEach(m => { try { m.step?.(step) } catch { } })
    },
    onMacroRecording: (r: any) => {
      const payload = {
        recording: !!r?.recording,
        reason: r?.reason ?? null,
        steps: Array.isArray(r?.steps) ? r.steps : [],
      }
      macroHandlers.forEach(m => { try { m.recording?.(payload) } catch { } })
    },
    onMacroState: (r: any) => {
      const payload = { recording: !!r?.recording, running: !!r?.running }
      macroHandlers.forEach(m => { try { m.state?.(payload) } catch { } })
    },
  }
  ;(window as any).__host = api
}

export function generateId(): string {
  return Math.random().toString(36).slice(2) + Date.now().toString(36)
}

/** 页面挂载完成后通知原生宿主（用于补发未加载完时排队的路由/命令）。 */
export function notifyReady() { post({ type: 'ready' }) }

function requestDialog(message: Record<string, any>): Promise<string | null> {
  if (!isHost) return Promise.resolve(null)
  const requestId = generateId()
  return new Promise((resolve) => {
    pendingFolders.set(requestId, resolve)
    const ok = post({ ...message, requestId })
    if (!ok) { pendingFolders.delete(requestId); resolve(null); return }
    setTimeout(() => {
      if (pendingFolders.delete(requestId)) resolve(null)
    }, 120000)
  })
}

export const host = {
  get available() { return isHost },

  openBook(hash: string) {
    if (!post({ type: 'openBook', bookId: hash })) location.href = urls.reader(hash)
  },

  openViewer(kind: string, path = '', extra?: Record<string, string>) {
    if (!post({ type: 'openViewer', kind, path, extra: extra || {} })) {
      const qs = new URLSearchParams()
      if (path) qs.set('path', path)
      for (const [k, v] of Object.entries(extra || {})) qs.set(k, v)
      const query = qs.toString()
      location.hash = `#/view/${encodeURIComponent(kind)}${query ? '?' + query : ''}`
    }
  },

  openBrowser(url = '') {
    if (!post({ type: 'openBrowser', url })) {
      if (url) window.open(url, '_blank', 'noopener')
      else location.hash = '#/browser'
    }
  },

  openTarget(path: string) {
    if (!post({ type: 'openTarget', path })) {
      // 浏览器模式无法把完整路径交给阅读器；媒体用系统默认程序打开
      fetch('/api/fs/open', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path }),
      }).catch(() => { })
    }
  },

  importFolder() {
    return post({ type: 'importFolder' })
  },

  importFiles() {
    return post({ type: 'importFiles' })
  },

  importPaths(paths: string[]) {
    return post({ type: 'importPaths', paths })
  },

  cancelImport() {
    return post({ type: 'cancelImport' })
  },

  pickFolder(title = '选择文件夹'): Promise<string | null> {
    return requestDialog({ type: 'pickFolder', title })
  },

  /** 弹出原生单文件选择框（音频/图片/视频/任意）。浏览器模式返回 null。 */
  pickFile(kind: 'audio' | 'image' | 'video' | 'media' | 'any' = 'any', title?: string): Promise<string | null> {
    return requestDialog({ type: 'pickFile', kind, title })
  },

  macroRecordStart() {
    if (!post({ type: 'macroRecordStart' })) return false
    return true
  },

  macroRecordStop() {
    if (!post({ type: 'macroRecordStop' })) return false
    return true
  },

  macroState() {
    post({ type: 'macroState' })
  },

  reveal(path: string) {
    if (!post({ type: 'reveal', path })) {
      fetch('/api/fs/reveal', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path }),
      }).catch(() => { })
    }
  },

  openDataFolder() {
    post({ type: 'openDataFolder' })
  },

  setTitle(title: string) {
    if (!post({ type: 'setTitle', title })) document.title = title
  },

  toggleFullscreen() {
    if (!post({ type: 'toggleFullscreen' })) {
      try {
        if (document.fullscreenElement) document.exitFullscreen()
        else document.documentElement.requestFullscreen()
      } catch { }
    }
  },

  windowAction(action: 'minimize' | 'maximize' | 'restore' | 'close') {
    post({ type: 'window', action })
  },

  closeWindow() {
    if (!post({ type: 'close' })) {
      if (location.hash.startsWith('#/view/')) location.hash = '#/'
      else history.back()
    }
  },

  refresh() {
    post({ type: 'refresh' })
  },
}
