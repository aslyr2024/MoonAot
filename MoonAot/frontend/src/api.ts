import type {
  AppInfo, AppSettings, AudioMeta, BookDetail, Facets, FavoriteFolder, FontsDto, FsList, FsRoots, HashResult, ImagePage,
  ImageItem, ImportResult, JobStatus, LibraryItem, LibraryPage, Lyrics, MacroList, MacroRecord, MediaProgressItem,
  NoteView, PlaylistList, PlaylistRecord, PortItem, ProcessItem, SiteDownloadResult, SiteFetchResult, SiteHostInfo, SiteHostList, StatsDto,
  SystemInfo, ZipList,
} from './types'

const jsonHeaders = { 'Content-Type': 'application/json' }

function queryString(params?: Record<string, any>): string {
  if (!params) return ''
  const parts: string[] = []
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === '') continue
    parts.push(encodeURIComponent(k) + '=' + encodeURIComponent(String(v)))
  }
  return parts.length ? '?' + parts.join('&') : ''
}

async function parseError(res: Response): Promise<Error> {
  let text = ''
  try { text = await res.text() } catch { /* ignore */ }
  try {
    const j = JSON.parse(text)
    if (j && (j.error || j.message)) return new Error(j.error || j.message)
  } catch { /* ignore */ }
  return new Error(text || `请求失败 (${res.status})`)
}

export async function apiGet<T>(path: string, params?: Record<string, any>, init?: RequestInit): Promise<T> {
  const res = await fetch(path + queryString(params), { cache: 'no-cache', ...init })
  if (!res.ok) throw await parseError(res)
  if (res.status === 204) return undefined as unknown as T
  const text = await res.text()
  return (text ? JSON.parse(text) : null) as T
}

export async function apiPost<T = any>(path: string, body?: any, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    headers: jsonHeaders,
    body: body === undefined ? undefined : JSON.stringify(body),
    ...init,
  })
  if (!res.ok) throw await parseError(res)
  const text = await res.text()
  return (text ? JSON.parse(text) : null) as T
}

export async function apiPut<T = any>(path: string, body?: any): Promise<T> {
  const res = await fetch(path, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(body ?? {}) })
  if (!res.ok) throw await parseError(res)
  const text = await res.text()
  return (text ? JSON.parse(text) : null) as T
}

export async function apiDelete<T = any>(path: string): Promise<T> {
  const res = await fetch(path, { method: 'DELETE' })
  if (!res.ok) throw await parseError(res)
  const text = await res.text()
  return (text ? JSON.parse(text) : null) as T
}

export async function postKeepalive(path: string, body: any): Promise<void> {
  try {
    await fetch(path, {
      method: 'POST',
      headers: jsonHeaders,
      body: JSON.stringify(body),
      keepalive: true,
    })
  } catch { /* 页面关闭时忽略 */ }
}

export const api = {
  // ---------- 书库 ----------
  libraryPage: (params: Record<string, any>) => apiGet<LibraryPage>('/api/library/page', params),
  recent: (limit = 12, inProgress = false) =>
    apiGet<LibraryItem[]>('/api/library/recent', { limit, inProgress: inProgress ? 1 : undefined }),
  facets: () => apiGet<Facets>('/api/library/facets'),
  stats: () => apiGet<StatsDto>('/api/library/stats'),
  detail: (id: string) => apiGet<BookDetail>(`/api/books/${encodeURIComponent(id)}`),
  updateBook: (id: string, patch: Record<string, any>) =>
    apiPost(`/api/books/${encodeURIComponent(id)}`, patch),
  favorite: (id: string, favorite: boolean) =>
    apiPost(`/api/books/${encodeURIComponent(id)}/favorite`, { favorite }),
  deleteBook: (id: string) => apiDelete(`/api/books/${encodeURIComponent(id)}`),
  revealBook: (id: string) => apiPost(`/api/books/${encodeURIComponent(id)}/reveal`),
  bookConfig: (id: string) => apiGet<any>(`/api/bookconfig/${encodeURIComponent(id)}`),

  // ---------- 笔记 ----------
  notes: (bookId?: string) =>
    apiGet<NoteView[]>(bookId ? `/api/notes/${encodeURIComponent(bookId)}` : '/api/notes'),
  updateNote: (id: string, note: string) =>
    apiPut(`/api/notes/${encodeURIComponent(id)}`, { note }),
  deleteNote: (id: string) => apiDelete(`/api/notes/${encodeURIComponent(id)}`),

  // ---------- 设置 ----------
  appSettings: () => apiGet<AppSettings>('/api/appsettings'),
  saveAppSettings: (patch: Record<string, any>) => apiPost('/api/appsettings', patch),
  readerSettings: () => apiGet<{ json: string }>('/api/settings'),
  saveReaderSettings: (json: string) => apiPost('/api/settings', { json }),
  fonts: () => apiGet<FontsDto>('/api/fonts'),
  appInfo: () => apiGet<AppInfo>('/api/appinfo'),

  // ---------- 文件/媒体 ----------
  fsList: (path: string, kind = 'all', limit = 4000) =>
    apiGet<FsList>('/api/fs/list', { path, kind, limit }),
  fsScan: (path: string, kind = 'media', limit = 20000) =>
    apiGet<FsList>('/api/fs/scan', { path, kind, limit }),
  fsRoots: () => apiGet<FsRoots>('/api/fs/roots'),
  fsOpen: (path: string) => apiPost('/api/fs/open', { path }),
  fsReveal: (path: string) => apiPost('/api/fs/reveal', { path }),
  fsRemember: (path: string) => apiPost('/api/fs/remember', { path }),
  images: (params: Record<string, any>) => apiGet<ImagePage>('/api/images', params),
  fileText: (path: string) => apiGet<{ ok: boolean; path: string; name: string; ext: string; size: number; encoding: string; text: string; modified: number }>('/api/filetext', { path }),
  saveFileText: (path: string, text: string, encoding = 'utf-8') =>
    apiPost('/api/filetext', { path, text, encoding }),
  saveEditedImage: (path: string, dataUrl: string, mode: 'copy' | 'overwrite' = 'copy') =>
    apiPost<{ ok: boolean; path: string; size: number }>('/api/imageedit', { path, dataUrl, mode }),
  siblings: (path: string, kind = 'media') => apiGet<ImageItem[]>('/api/siblings', { path, kind }),
  mediaProgressAll: () => apiGet<MediaProgressItem[]>('/api/media/progress'),
  mediaProgress: (path: string) => apiGet<MediaProgressItem | null>('/api/media/progress', { path }),
  saveMediaProgress: (item: Partial<MediaProgressItem> & { path: string }) =>
    apiPost('/api/media/progress', item),
  saveMediaProgressKeepalive: (item: Partial<MediaProgressItem> & { path: string }) =>
    postKeepalive('/api/media/progress', item),
  removeMediaProgress: (path: string) =>
    apiDelete('/api/media/progress?path=' + encodeURIComponent(path)),
  playlists: () => apiGet<PlaylistList>('/api/media/playlists'),
  playlist: (id: string) => apiGet<PlaylistRecord>(`/api/media/playlists/${encodeURIComponent(id)}`),
  createPlaylist: (rec: Partial<PlaylistRecord>) => apiPost<PlaylistRecord>('/api/media/playlists', rec),
  updatePlaylist: (id: string, rec: Partial<PlaylistRecord>) =>
    apiPut<PlaylistRecord>(`/api/media/playlists/${encodeURIComponent(id)}`, rec),
  deletePlaylist: (id: string) => apiDelete(`/api/media/playlists/${encodeURIComponent(id)}`),
  selectPlaylist: (id: string | null) => apiPost('/api/media/playlists/last', { id }),
  audioMeta: (path: string) => apiGet<AudioMeta>('/api/audio/meta', { path }),
  lyrics: (path: string) => apiGet<Lyrics>('/api/lyrics', { path }),
  zipList: (path: string, prefix = '') => apiGet<ZipList>('/api/zip/list', { path, prefix }),
  zipExtract: (path: string, entry: string) =>
    apiPost<{ ok: boolean; path?: string; error?: string }>('/api/zip/extract', { path, entry }),
  zipLast: (path: string) => apiGet<{ path: string; entry?: string | null }>('/api/zip/last', { path }),
  saveZipLast: (path: string, entry: string | null) => apiPost('/api/zip/last?path=' + encodeURIComponent(path), { entry }),

  // ---------- 按键精灵 ----------
  macros: () => apiGet<MacroList>('/api/macros'),
  saveMacro: (rec: MacroRecord) => apiPost<MacroRecord>('/api/macros', rec),
  deleteMacro: (id: string) => apiDelete(`/api/macros/${encodeURIComponent(id)}`),
  runMacro: (id: string, leadIn?: number) =>
    apiPost(`/api/macros/${encodeURIComponent(id)}/run` + (leadIn === undefined ? '' : `?leadIn=${leadIn}`)),
  stopMacro: () => apiPost('/api/macros/stop'),
  setMacroEnabled: (id: string, enabled: boolean) =>
    apiPost(`/api/macros/${encodeURIComponent(id)}/enabled`, { enabled }),
  setMacrosGlobal: (enabled: boolean) => apiPost<MacroList>('/api/macros/global', { enabled }),
  recordMacro: (start: boolean) => apiPost('/api/macros/record/' + (start ? 'start' : 'stop')),
  stopMacroRecording: () => apiPost('/api/macros/record/stop'),

  // ---------- 媒体库收藏夹 ----------
  favorites: () => apiGet<FavoriteFolder[]>('/api/favorites'),
  addFavorite: (path: string, name?: string) => apiPost<FavoriteFolder[]>('/api/favorites', { path, name }),
  removeFavorite: (path: string) => apiDelete('/api/favorites?path=' + encodeURIComponent(path)),

  // ---------- 工具箱 ----------
  systemInfo: () => apiGet<SystemInfo>('/api/tools/system'),
  fileHash: (path: string, algo = 'sha256') => apiGet<HashResult>('/api/tools/hash', { path, algo }),
  processes: (q = '') => apiGet<ProcessItem[]>('/api/tools/processes', { q }),
  killProcess: (id: number) => apiPost(`/api/tools/processes/${id}/kill`, {}),
  ports: () => apiGet<PortItem[]>('/api/tools/ports'),
  killPort: (port: number) => apiPost(`/api/tools/ports/${port}/kill`, {}),
  screenshot: () => apiPost<{ ok: boolean; path?: string; error?: string }>('/api/tools/screenshot', {}),
  power: (action: 'lock' | 'sleep' | 'screenoff' | 'shutdown' | 'restart') =>
    apiPost('/api/tools/power', { action }),
  archiveCompress: (folder: string, zip: string) =>
    apiPost('/api/tools/archive/compress', { folder, zip }),
  archiveExtract: (zip: string, dest: string) =>
    apiPost('/api/tools/archive/extract', { zip, dest }),

  // ---------- 网站工具 ----------
  siteHosts: () => apiGet<SiteHostList>('/api/sites/hosts'),
  startSiteHost: (path: string) => apiPost<SiteHostInfo>('/api/sites/hosts', { path }),
  stopSiteHost: (id: string) => apiDelete(`/api/sites/hosts/${encodeURIComponent(id)}`),
  siteFetch: (req: { url: string; method?: string; body?: string; headers?: Record<string, string> }) =>
    apiPost<SiteFetchResult>('/api/sites/fetch', req),
  siteDownload: (url: string, folder: string, maxPages = 1, maxDepth = 2) =>
    apiPost<SiteDownloadResult>('/api/sites/download', { url, folder, maxPages, maxDepth }),

  // ---------- 后台任务 ----------
  latestJob: (kind: string) => apiGet<JobStatus | null>('/api/jobs/latest', { kind }),
  job: (id: string) => apiGet<JobStatus>(`/api/jobs/${encodeURIComponent(id)}`),
  cancelJob: (id: string) => apiPost(`/api/jobs/${encodeURIComponent(id)}/cancel`),

  rebuildCovers: () => apiPost<JobStatus>('/api/library/rebuild-covers'),
  purgeMissing: () => apiPost<JobStatus>('/api/library/purge-missing'),
  repairMetadata: () => apiPost<JobStatus>('/api/library/repair-metadata'),
  cleanupLegacy: () => apiPost<{ ok: boolean; count: number; message?: string; error?: string }>('/api/library/cleanup-legacy'),

  defaultCover: () => apiPost<{ ok: boolean; applied: number }>('/api/defaultcover', {}),
  setDefaultCover: (dataUrl: string) => apiPost<{ ok: boolean; applied: number; error?: string }>('/api/defaultcover', { dataUrl }),
}

// ---------- URL 构造 ----------
export const urls = {
  // 书架/详情统一使用封面主文件（≤48KB），不再单独保存 320×480 缩略图
  thumb: (item: { hash: string; coverVersion?: number }) =>
    `/cover/${encodeURIComponent(item.hash)}?v=${item.coverVersion ?? 0}`,
  cover: (hash: string, v = 0) => `/cover/${encodeURIComponent(hash)}?v=${v}`,
  defaultCover: () => `/api/defaultcover`,
  thumbPath: (path: string, w = 320, h = 320) =>
    `/thumbpath?path=${encodeURIComponent(path)}&w=${w}&h=${h}`,
  image: (path: string) => `/image?path=${encodeURIComponent(path)}`,
  media: (path: string) => `/media?path=${encodeURIComponent(path)}`,
  book: (hash: string) => `/book/${encodeURIComponent(hash)}`,
  audioCover: (path: string) => `/api/audio/cover?path=${encodeURIComponent(path)}`,
  zipEntry: (path: string, entry: string) =>
    `/zip/entry?path=${encodeURIComponent(path)}&entry=${encodeURIComponent(entry)}`,
  reader: (hash: string) => `/reader.html?ids=${encodeURIComponent(hash)}`,
}

export interface UploadHandle { promise: Promise<string> }

/** 分块上传书籍副本（浏览器拖拽/文件选择用）。 */
export async function uploadBook(file: File, onProgress?: (done: number, total: number) => void): Promise<string> {
  if (file.size === 0) throw new Error('空文件')
  const begin = await apiPost<{ uploadId: string }>('/api/import/begin', { name: file.name, size: file.size })
  const uploadId = begin.uploadId
  const CHUNK = 2 * 1024 * 1024
  for (let off = 0; off < file.size; off += CHUNK) {
    const end = Math.min(off + CHUNK, file.size)
    const blob = file.slice(off, end)
    const res = await fetch(`/api/import/chunk/${uploadId}?offset=${off}`, { method: 'PUT', body: blob })
    if (!res.ok) throw await parseError(res)
    onProgress?.(end, file.size)
  }
  const fin = await apiPost<{ ok: boolean; id?: string; error?: string }>(`/api/import/finish/${uploadId}`)
  if (!fin.ok || !fin.id) throw new Error(fin.error || '导入失败')
  return fin.id
}
