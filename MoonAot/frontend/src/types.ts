export type AppTheme = 'dark' | 'light' | 'forest' | 'violet' | 'warm'

export interface MediaSettings {
  imageSlideSeconds: number
  comicMode: boolean
  comicWidth: 'full' | '2/3' | '1/2' | 'default'
  comicAutoPage: boolean
  recursiveImages: boolean
  musicBackgroundSeconds: number
  musicShowLyrics: boolean
  videoAutoResume: boolean
  imageTransition: 'none' | 'fade' | 'slide' | 'zoom'
  imageFitMode?: 'contain' | 'width' | 'height' | 'center' | 'actual'
  imageBackgroundMusic?: string | null
  imageBackgroundMusicFolder?: string | null
  imageBackgroundMusicPlaylist?: string | null
  imageBackgroundMusicIndex?: number
  imageSlideLoop: boolean
  musicVolume: number
  musicMode: 'order' | 'repeat' | 'shuffle'
  musicTab: 'list' | 'lyrics'
  videoVolume: number
  videoShowList: boolean
  musicBackgroundFolder?: string | null
  musicBackgroundIndex: number
  musicBackgroundRandom?: boolean
  musicBackgroundFit?: 'cover' | 'contain' | 'stretch' | 'center'
  musicBackgroundBlur?: number
  imageLastFolder?: string | null
}

export interface AppSettings {
  appTheme: AppTheme
  sortMode: string
  sortDesc: boolean
  showCovers: boolean
  libraryView: 'grid' | 'list'
  gridSize: number
  readerSettingsJson: string
  defaultCoverPath?: string | null
  importCovers: boolean
  useDefaultCover: boolean
  autoRefreshLibrary: boolean
  recentFolders: string[]
  lastFolder?: string | null
  media: MediaSettings
}

export interface LibraryItem {
  hash: string
  title: string
  author: string
  format: string
  createdAt: number
  updatedAt: number
  progress: number
  coverVersion: number
  hasCover: boolean
  favorite: boolean
  fileSize: number
  category?: string | null
  folder?: string | null
  missing: boolean
  finished: boolean
}

export interface LibraryPage {
  total: number
  offset: number
  limit: number
  version: number
  items: LibraryItem[]
}

export interface BookDetail extends LibraryItem {
  id: string
  addedAt: number
  lastReadAt?: number | null
  location?: string | null
  filePath: string
  sourcePath?: string | null
  notes: number
  description?: string | null
  publisher?: string | null
  language?: string | null
  readingSeconds: number
}

export interface NoteView {
  id: string
  bookId: string
  bookTitle: string
  type: string
  cfi: string
  text?: string | null
  note: string
  style?: string | null
  color?: string | null
  page?: number | null
  progress: number
  label?: string | null
  createdAt: number
  updatedAt: number
  chapter?: string | null
}

export interface CountItem { name: string; count: number }

export interface Facets {
  total: number
  favorites: number
  finished: number
  reading: number
  unread: number
  formats: CountItem[]
  authors: CountItem[]
  folders: CountItem[]
}

export interface StatsDto {
  totalBooks: number
  finishedBooks: number
  readingBooks: number
  unreadBooks: number
  notes: number
  favorites: number
  readingSeconds: number
  covers: number
  coversBytes: number
  storageBytes?: number
  missing?: number
  formats: CountItem[]
  recentDays: CountItem[]
}

export interface AudioMeta {
  ok: boolean
  title?: string | null
  artist?: string | null
  album?: string | null
  hasCover: boolean
  error?: string | null
}

export interface FsEntry {
  name: string
  path: string
  isDir: boolean
  size: number
  modified: number
  kind: string
  ext: string
}

export interface FsList {
  path: string
  parent?: string | null
  total: number
  truncated: boolean
  entries: FsEntry[]
}

export interface FsRoot { name: string; path: string; kind: string }
export interface FsRoots { roots: FsRoot[]; drives: FsRoot[]; recent: string[] }

export interface ImageItem { name: string; path: string; size: number }
export interface ImagePage {
  path: string
  total: number
  offset: number
  limit: number
  index: number
  scanning: boolean
  message?: string | null
  items: ImageItem[]
}

export interface MediaProgressItem {
  path: string
  kind: string
  position: number
  duration: number
  updatedAt: number
  title?: string | null
  artist?: string | null
  scale?: number
  rotation?: number
}

export interface PlaylistRecord {
  id: string
  name: string
  kind: string
  items: string[]
  current: number
  updatedAt: number
}
export interface PlaylistSummary {
  id: string
  name: string
  kind: string
  count: number
  updatedAt: number
}
export interface PlaylistList { lastId?: string | null; items: PlaylistSummary[] }

export interface LyricLine { t: number; text: string }
export interface Lyrics {
  ok: boolean
  source: string
  synced: boolean
  plain: string
  lines: LyricLine[]
  title?: string | null
  artist?: string | null
}

export interface ZipItem {
  name: string
  path: string
  isDir: boolean
  size: number
  compressedSize: number
}
export interface ZipList {
  path: string
  prefix: string
  total: number
  items: ZipItem[]
}

export interface JobStatus {
  id: string
  kind: string
  running: boolean
  done: number
  total: number
  message?: string | null
  startedAt: number
  finishedAt: number
}

export interface AppInfo {
  version: string
  dataDir: string
  baseUrl: string
  bookCount: number
  isAot: boolean
  os: string
  webViewVersion?: string | null
  startedAt: number
}

export interface FontsDto { fonts: string[]; embedded: string[] }

export interface ImportResult {
  ok: boolean
  imported: number
  skipped: number
  failed: number
  error?: string | null
}

export interface ImportState {
  active: boolean
  done: number
  total: number
  current: string
  result?: ImportResult | null
}

// ---------- 按键精灵 ----------

export type MacroStepKind = 'key' | 'delay' | 'mouse_move' | 'mouse_click' | 'mouse_wheel' | 'text'

export interface MacroStep {
  kind: MacroStepKind
  keys: string[]
  holdMs: number
  delayMs: number
  x: number
  y: number
  button?: string | null
  wheel: number
  text?: string | null
}

export interface MacroRecord {
  id: string
  name: string
  enabled: boolean
  triggerMode: 'hotkey' | 'remap'
  trigger: string
  repeat: number
  repeatDelayMs: number
  leadInMs: number
  sendMode: 'scan' | 'virtual'
  steps: MacroStep[]
  createdAt: number
  updatedAt: number
  hotkeyError?: string | null
}

export interface MacroList {
  items: MacroRecord[]
  globalEnabled: boolean
  recording: boolean
  running: boolean
}

export interface FavoriteFolder {
  path: string
  name: string
  addedAt: number
}

// ---------- 工具箱 / 网站工具 ----------

export interface DiskInfo {
  name: string
  total: number
  free: number
}

export interface SystemInfo {
  os: string
  machine: string
  user: string
  processId: number
  processName: string
  cpuCount: number
  cpuName: string
  memoryTotal: number
  memoryAvailable: number
  workingSet: number
  uptimeSeconds: number
  appVersion: string
  dataDir: string
  disks: DiskInfo[]
}

export interface HashResult {
  ok: boolean
  error?: string | null
  hash?: string | null
  algo: string
  size: number
  path?: string | null
}

export interface ProcessItem {
  id: number
  name: string
  title?: string | null
  memory: number
  startedAt: number
}

export interface PortItem {
  proto: string
  local: string
  remote: string
  state: string
  pid: number
  port: number
}

export interface SiteHostInfo {
  id: string
  path: string
  url: string
  startedAt: number
}
export interface SiteHostList { items: SiteHostInfo[] }

export interface SiteFetchResult {
  ok: boolean
  error?: string | null
  status: number
  contentType?: string | null
  length: number
  body?: string | null
  headers: Record<string, string>
}

export interface SiteDownloadResult {
  ok: boolean
  error?: string | null
  files: number
  pages?: number
  bytes: number
  entry?: string | null
  written?: string[]
}
