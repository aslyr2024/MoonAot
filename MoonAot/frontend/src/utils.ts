export function formatBytes(n?: number | null): string {
  if (n === undefined || n === null || Number.isNaN(n)) return '-'
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} MB`
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} GB`
}

export function formatDuration(seconds?: number | null): string {
  const s = Math.max(0, Math.floor(seconds ?? 0))
  if (s < 60) return `${s} 秒`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m} 分钟`
  const h = Math.floor(m / 60)
  const rm = m % 60
  return rm ? `${h} 小时 ${rm} 分` : `${h} 小时`
}

export function formatClock(seconds?: number | null): string {
  const s = Math.max(0, Math.floor(seconds ?? 0))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const sec = s % 60
  return h > 0
    ? `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`
    : `${m}:${String(sec).padStart(2, '0')}`
}

export function pad2(n: number): string { return String(n).padStart(2, '0') }

export function formatDate(ms?: number | null): string {
  if (!ms) return '-'
  const d = new Date(ms)
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`
}

export function formatDay(ms?: number | null): string {
  if (!ms) return '-'
  const d = new Date(ms)
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`
}

export function timeAgo(ms?: number | null): string {
  if (!ms) return '未读'
  const diff = Date.now() - ms
  if (diff < 60_000) return '刚刚'
  if (diff < 3600_000) return `${Math.floor(diff / 60_000)} 分钟前`
  if (diff < 86400_000) return `${Math.floor(diff / 3600_000)} 小时前`
  if (diff < 7 * 86400_000) return `${Math.floor(diff / 86400_000)} 天前`
  return formatDay(ms)
}

export function percent(progress?: number | null): number {
  return Math.max(0, Math.min(100, Math.round((progress ?? 0) * 100)))
}

export function clamp(v: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, v))
}

export function basename(path?: string | null): string {
  if (!path) return ''
  const s = path.replace(/\\/g, '/')
  const i = s.lastIndexOf('/')
  return i >= 0 ? s.slice(i + 1) : s
}

export function dirname(path?: string | null): string {
  if (!path) return ''
  const s = path.replace(/\\/g, '/')
  const i = s.lastIndexOf('/')
  return i > 0 ? s.slice(0, i) : s
}

export function extname(path?: string | null): string {
  const name = basename(path)
  const i = name.lastIndexOf('.')
  return i >= 0 ? name.slice(i + 1).toLowerCase() : ''
}

export function debounce<T extends (...args: any[]) => void>(fn: T, delay = 250) {
  let timer: ReturnType<typeof setTimeout> | null = null
  return (...args: Parameters<T>) => {
    if (timer) clearTimeout(timer)
    timer = setTimeout(() => { timer = null; fn(...args) }, delay)
  }
}

export function hashHue(text: string): number {
  let h = 0
  for (let i = 0; i < text.length; i++) h = (h * 31 + text.charCodeAt(i)) >>> 0
  return h % 360
}

export function fileKindLabel(kind: string): string {
  switch (kind) {
    case 'book': return '图书'
    case 'audio': return '音乐'
    case 'video': return '视频'
    case 'image': return '图片'
    case 'zip': return '压缩包'
    case 'dir': return '文件夹'
    default: return '文件'
  }
}

export function formatKind(k: string): string {
  const s = (k || '').toUpperCase()
  if (s === 'AZW3') return 'AZW3'
  if (s === 'MD' || s === 'TXT') return s
  return s
}

export function isCoverCapable(format: string): boolean {
  const f = (format || '').toUpperCase()
  return f === 'EPUB' || f === 'PDF' || f === 'MOBI' || f === 'AZW3' || f === 'CBZ'
}
