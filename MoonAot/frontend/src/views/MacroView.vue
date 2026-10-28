<script setup lang="ts">
defineOptions({ name: 'MacroView' })

import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  AlertTriangle, ArrowDown, ArrowUp, CircleDot, Copy, Gamepad2, Info, Keyboard,
  ListOrdered, MousePointerClick, Play, Plus, Power, Radio, Save, Square, Timer, Trash2, Type, Wand2,
  Zap, RefreshCw,
} from 'lucide-vue-next'
import { NButton, NInput, NInputNumber, NSelect, NSwitch, NTag } from 'naive-ui'
import PageHeader from '../components/PageHeader.vue'
import { api } from '../api'
import { host, onMacroEvent } from '../host'
import { confirmDialog, toast, toastError } from '../ui'
import { clamp } from '../utils'
import type { MacroList, MacroRecord, MacroStep, MacroStepKind } from '../types'

const loading = ref(true)
const macros = ref<MacroRecord[]>([])
const globalEnabled = ref(true)
const recording = ref(false)
const running = ref(false)
const draft = ref<MacroRecord | null>(null)
const selectedId = ref('')
const selectedIndex = ref(-1)
const quickText = ref('s sd  d 延迟200ms ik 延迟50ms k k k')
const keysEdit = ref('')
const keysError = ref('')
const capturing = ref(false)
const capturePreview = ref('')
const saveBusy = ref(false)
const dirty = ref(false)
const timeline = ref<HTMLElement | null>(null)
const liveSteps = ref<MacroStep[]>([])

let suppressDirty = false
let runTimer: ReturnType<typeof setInterval> | null = null
let unsubscribe: (() => void) | null = null

const selectedStep = computed<MacroStep | null>(() => {
  const d = draft.value
  if (!d || selectedIndex.value < 0 || selectedIndex.value >= d.steps.length) return null
  return d.steps[selectedIndex.value]
})

const stepKindOptions = [
  { label: '按键 / 组合键', value: 'key' },
  { label: '等待', value: 'delay' },
  { label: '鼠标点击', value: 'mouse_click' },
  { label: '鼠标滚轮', value: 'mouse_wheel' },
  { label: '鼠标移动', value: 'mouse_move' },
  { label: '输入文本', value: 'text' },
]

const buttonOptions = [
  { label: '左键', value: 'left' }, { label: '右键', value: 'right' },
  { label: '中键', value: 'middle' }, { label: '侧键 1', value: 'x1' }, { label: '侧键 2', value: 'x2' },
]

const modeLabel = (m: MacroRecord) => m.triggerMode === 'remap' ? '拦截键' : '全局热键'
const hotkeyWarning = computed(() => {
  const d = draft.value
  if (!d || d.triggerMode !== 'hotkey') return ''
  const t = d.trigger.trim()
  if (!t) return '还没有设置触发键'
  if (t.includes('+')) return ''
  if (/^[A-Z0-9]$/i.test(t)) return '单键热键会全局占用该键，游戏中打字也会被拦截；建议加 Ctrl / Shift。'
  return ''
})

// ==================== 键名 ====================

const SPECIAL = new Set([
  'SPACE', 'ENTER', 'ESC', 'TAB', 'BACKSPACE', 'DELETE', 'INSERT', 'HOME', 'END', 'PGUP', 'PGDN',
  'UP', 'DOWN', 'LEFT', 'RIGHT', 'CAPSLOCK', 'NUMLOCK', 'SCROLLLOCK', 'PRINTSCREEN', 'PAUSE', 'APPS',
  'PLUS', 'MINUS', 'COMMA', 'PERIOD', 'SLASH', 'BACKSLASH', 'SEMICOLON', 'QUOTE', 'LBRACKET', 'RBRACKET',
  'BACKQUOTE', 'NUMPADMULTIPLY', 'NUMPADADD', 'NUMPADSUBTRACT', 'NUMPADDECIMAL', 'NUMPADDIVIDE',
  ...Array.from({ length: 24 }, (_, i) => 'F' + (i + 1)),
  ...Array.from({ length: 10 }, (_, i) => 'NUMPAD' + i),
])

const ALIAS: Record<string, string> = {
  '空格': 'SPACE', '回车': 'ENTER', '确认': 'ENTER', '退出': 'ESC', '上': 'UP', '下': 'DOWN',
  '左': 'LEFT', '右': 'RIGHT', '控制': 'CTRL', '换挡': 'SHIFT',
  'CTRL': 'CTRL', 'CONTROL': 'CTRL', 'SHIFT': 'SHIFT', 'ALT': 'ALT', 'WIN': 'WIN', 'WINDOWS': 'WIN',
  '+': 'PLUS', '=': 'PLUS', '-': 'MINUS', ',': 'COMMA', '.': 'PERIOD', '/': 'SLASH', '\\': 'BACKSLASH',
  ';': 'SEMICOLON', "'": 'QUOTE', '[': 'LBRACKET', ']': 'RBRACKET', '`': 'BACKQUOTE',
}
for (let i = 0; i <= 9; i++) {
  ALIAS['小键盘' + i] = 'NUMPAD' + i
  ALIAS['NP' + i] = 'NUMPAD' + i
}

const MODIFIERS = new Set(['CTRL', 'SHIFT', 'ALT', 'WIN'])

function normalizeKey(raw: string): string | null {
  const s = raw.trim()
  if (!s) return null
  const up = s.toUpperCase()
  if (ALIAS[up]) return ALIAS[up]
  if (ALIAS[s]) return ALIAS[s]
  if (SPECIAL.has(up)) return up
  if (s.length === 1) {
    const ch = s.toUpperCase()
    if (/[A-Z0-9]/.test(ch)) return ch
    if ('`-=[]\\;\',./'.includes(s)) return s
  }
  return null
}

/** 把 "S+D" / "sd" / "CTRL+K" 转成按键数组；无法识别返回 null。 */
function parseKeyToken(token: string): string[] | null {
  const raw = token.trim()
  if (!raw) return null
  const parts = raw.split('+').filter(p => p.length > 0)
  if (parts.length > 1) {
    const keys = parts.map(normalizeKey)
    return keys.every(k => !!k) ? keys as string[] : null
  }
  const single = normalizeKey(raw)
  if (single) return [single]
  if (raw.length > 1) {
    const chars = Array.from(raw)
    const keys = chars.map(normalizeKey)
    if (keys.every(k => !!k)) return keys as string[]
  }
  return null
}

function isModifierName(name: string) { return MODIFIERS.has(name.toUpperCase()) }

// ==================== 快速解析 ====================

const parseResult = computed(() => parseQuick(quickText.value))

function blankStep(kind: MacroStepKind): MacroStep {
  return {
    kind,
    keys: kind === 'key' ? ['K'] : [],
    holdMs: 30,
    delayMs: kind === 'delay' ? 200 : 0,
    x: -1,
    y: -1,
    button: 'left',
    wheel: 120,
    text: '',
  }
}

function parseQuick(text: string): { steps: MacroStep[]; unknown: string[] } {
  const steps: MacroStep[] = []
  const unknown: string[] = []
  const tokens = text.split(/\s+/).filter(Boolean)

  const addDelay = (ms: number) => {
    if (steps.length === 0) {
      const s = blankStep('delay'); s.delayMs = Math.round(ms); steps.push(s)
    } else {
      const last = steps[steps.length - 1]
      last.delayMs = clamp(Math.round(last.delayMs + ms), 0, 600000)
    }
  }

  const parseOne = (token: string): boolean => {
    // 重复：k*3 / k×3 / k x3
    const rep = token.match(/^(.+?)[*×xX](\d{1,3})$/)
    if (rep && rep[1] && rep[1] !== token) {
      const inner = parseKeyToken(rep[1])
      if (!inner) return false
      const n = clamp(parseInt(rep[2], 10) || 1, 1, 99)
      for (let i = 0; i < n; i++) {
        const s = blankStep('key')
        s.keys = [...inner]
        s.holdMs = inner.length > 1 ? 45 : 30
        steps.push(s)
      }
      return true
    }

    // 延迟：延迟200ms / 等200 / +200ms / 200毫秒
    const dm = token.match(/^(?:(?:延迟|等待|延时|delay|sleep|wait|等)\s*(\d+(?:\.\d+)?)\s*(ms|毫秒|s|秒)?|\+(\d+(?:\.\d+)?)\s*(ms|毫秒|s|秒)?|(\d+(?:\.\d+)?)\s*(ms|毫秒|s|秒))$/i)
    if (dm) {
      const num = Number(dm[1] ?? dm[3] ?? dm[5])
      const unit = (dm[2] ?? dm[4] ?? dm[6] ?? 'ms').toLowerCase()
      if (Number.isFinite(num)) {
        addDelay(unit === 's' || unit === '秒' ? num * 1000 : num)
        return true
      }
    }

    // 鼠标
    if (token === '左键' || token === '点击' || token.toLowerCase() === 'lclick') {
      const s = blankStep('mouse_click'); steps.push(s); return true
    }
    if (token === '右键' || token.toLowerCase() === 'rclick') {
      const s = blankStep('mouse_click'); s.button = 'right'; steps.push(s); return true
    }
    if (token === '中键') {
      const s = blankStep('mouse_click'); s.button = 'middle'; steps.push(s); return true
    }
    if (token === '滚轮上' || token.toLowerCase() === 'wheelup') {
      const s = blankStep('mouse_wheel'); s.wheel = 120; steps.push(s); return true
    }
    if (token === '滚轮下' || token.toLowerCase() === 'wheeldown') {
      const s = blankStep('mouse_wheel'); s.wheel = -120; steps.push(s); return true
    }

    const keys = parseKeyToken(token)
    if (!keys) return false
    const s = blankStep('key')
    s.keys = keys
    s.holdMs = keys.length > 1 ? 45 : 30
    steps.push(s)
    return true
  }

  for (const token of tokens) {
    if (!parseOne(token)) unknown.push(token)
  }
  return { steps, unknown }
}

function applyQuick(replace: boolean) {
  const d = draft.value
  if (!d) return
  const res = parseResult.value
  if (res.unknown.length) toast('无法识别：' + res.unknown.join('、'), 'warning')
  if (!res.steps.length) return
  d.steps = replace ? res.steps : [...d.steps, ...res.steps]
  selectedIndex.value = replace ? 0 : d.steps.length - res.steps.length
  dirty.value = true
  void nextTick(() => scrollTimeline(true))
  toast(replace ? `已替换为 ${res.steps.length} 步` : `已追加 ${res.steps.length} 步`)
}

// ==================== 列表 / 编辑 ====================

function deepClone<T>(v: T): T { return JSON.parse(JSON.stringify(v)) as T }

function setDraft(rec: MacroRecord | null) {
  suppressDirty = true
  draft.value = rec ? deepClone(rec) : null
  suppressDirty = false
  dirty.value = false
  selectedIndex.value = -1
  keysEdit.value = ''
  keysError.value = ''
}

function newDraft(): MacroRecord {
  return {
    id: '', name: '新宏', enabled: true, triggerMode: 'hotkey', trigger: 'F1', repeat: 1,
    repeatDelayMs: 500, leadInMs: 1500, sendMode: 'scan', steps: [], createdAt: 0, updatedAt: 0,
  }
}

async function load(initial = false) {
  loading.value = true
  try {
    const res: MacroList = await api.macros()
    macros.value = res.items
    globalEnabled.value = res.globalEnabled
    recording.value = res.recording
    running.value = res.running
    if (initial) {
      if (res.items.length) {
        selectedId.value = res.items[0].id
        setDraft(res.items[0])
      } else {
        selectedId.value = ''
        setDraft(newDraft())
      }
    }
  } catch (e) { toastError(e) } finally { loading.value = false }
}

async function refreshRegistration() {
  try {
    const res = await api.macros()
    const map = new Map(res.items.map(m => [m.id, m]))
    for (const m of macros.value) {
      const fresh = map.get(m.id)
      if (fresh) m.hotkeyError = fresh.hotkeyError
    }
    if (draft.value && draft.value.id) {
      const fresh = map.get(draft.value.id)
      if (fresh) draft.value.hotkeyError = fresh.hotkeyError
    }
    globalEnabled.value = res.globalEnabled
    recording.value = res.recording
    running.value = res.running
  } catch { }
}

async function selectMacro(m: MacroRecord) {
  if (m.id === selectedId.value) return
  if (recording.value) { toast('请先结束录制', 'warning'); return }
  if (dirty.value && draft.value) {
    const ok = await confirmDialog({
      title: '放弃未保存的修改？', content: '当前宏还有未保存的修改，切换后会丢失。',
      positiveText: '放弃并切换', danger: true,
    })
    if (!ok) return
  }
  selectedId.value = m.id
  setDraft(m)
}

async function newMacro() {
  if (recording.value) { toast('请先结束录制', 'warning'); return }
  if (dirty.value && draft.value) {
    const ok = await confirmDialog({ title: '新建宏？', content: '当前宏的未保存修改会丢失。', positiveText: '新建' })
    if (!ok) return
  }
  selectedId.value = ''
  setDraft(newDraft())
  quickText.value = ''
}

async function saveMacro(): Promise<boolean> {
  const d = draft.value
  if (!d) return false
  if (!d.name.trim()) { toast('请填写宏名称', 'warning'); return false }
  saveBusy.value = true
  try {
    const saved = await api.saveMacro(d)
    selectedId.value = saved.id
    setDraft(saved)
    const i = macros.value.findIndex(m => m.id === saved.id)
    if (i >= 0) macros.value[i] = saved
    else macros.value.unshift(saved)
    toast('已保存并重新注册热键')
    setTimeout(() => { void refreshRegistration() }, 350)
    return true
  } catch (e) { toastError(e); return false } finally { saveBusy.value = false }
}

async function removeMacro() {
  const d = draft.value
  if (!d) return
  if (!d.id) { newMacro(); return }
  const ok = await confirmDialog({ title: '删除宏', content: `确定删除「${d.name}」吗？`, positiveText: '删除', danger: true })
  if (!ok) return
  try {
    await api.deleteMacro(d.id)
    macros.value = macros.value.filter(m => m.id !== d.id)
    toast('已删除')
    selectedId.value = ''
    if (macros.value.length) {
      selectedId.value = macros.value[0].id
      setDraft(macros.value[0])
    } else {
      setDraft(newDraft())
    }
  } catch (e) { toastError(e) }
}

async function toggleEnabled(m: MacroRecord) {
  const next = !m.enabled
  try {
    await api.setMacroEnabled(m.id, next)
    m.enabled = next
    if (draft.value?.id === m.id) draft.value.enabled = next
    toast(next ? '已启用' : '已停用')
    setTimeout(() => { void refreshRegistration() }, 320)
  } catch (e) { toastError(e) }
}

async function toggleGlobal(v: boolean) {
  try {
    const res = await api.setMacrosGlobal(v)
    globalEnabled.value = res.globalEnabled
    toast(v ? '按键精灵已启用' : '按键精灵已暂停（热键与拦截全部停用）', v ? 'success' : 'warning')
    setTimeout(() => { void refreshRegistration() }, 320)
  } catch (e) {
    globalEnabled.value = !v
    toastError(e)
  }
}

// ==================== 运行 ====================

async function testRun() {
  const d = draft.value
  if (!d) return
  if (recording.value) { toast('录制中不能测试运行', 'warning'); return }
  if (!d.id || dirty.value) {
    const ok = await saveMacro()
    if (!ok) return
  }
  try {
    const id = draft.value!.id
    await api.runMacro(id, draft.value!.leadInMs)
    running.value = true
    startRunPolling()
    toast(`宏将在 ${(draft.value!.leadInMs / 1000).toFixed(1)} 秒后执行，请切换到游戏窗口`, 'info')
  } catch (e) { toastError(e) }
}

async function stopRun() {
  try { await api.stopMacro(); running.value = false; toast('已停止') } catch (e) { toastError(e) }
}

function startRunPolling() {
  if (runTimer) clearInterval(runTimer)
  let ticks = 0
  runTimer = setInterval(async () => {
    ticks++
    try {
      const res = await api.macros()
      running.value = res.running
      recording.value = res.recording
      if (!res.running || ticks > 120) stopRunPolling()
    } catch { stopRunPolling() }
  }, 700)
}
function stopRunPolling() { if (runTimer) { clearInterval(runTimer); runTimer = null } }

// ==================== 录制 ====================

function startRecording() {
  if (!host.available) { toast('浏览器模式不支持全局录制，请在桌面版中使用', 'warning'); return }
  if (!draft.value) setDraft(newDraft())
  liveSteps.value = []
  if (!host.macroRecordStart()) { toast('无法开始录制', 'error'); return }
  toast('录制开始：请在目标窗口操作，按 F12 结束', 'info')
  setTimeout(() => host.macroState(), 300)
}

function stopRecording() {
  if (!host.macroRecordStop()) toast('无法连接到宿主', 'warning')
}

function onRecordStep(step: any) {
  if (!recording.value) return
  const s = normStep(step)
  liveSteps.value.push(s)
  void nextTick(() => scrollTimeline(true))
}

function onRecordState(r: { recording: boolean; reason?: string | null; steps: any[] }) {
  recording.value = r.recording
  if (!r.recording) {
    const steps = (r.steps || []).map(normStep)
    if (steps.length) {
      if (!draft.value) setDraft(newDraft())
      draft.value!.steps = steps
      selectedIndex.value = steps.length - 1
      dirty.value = true
      void nextTick(() => scrollTimeline(true))
    }
    liveSteps.value = []
    toast(`录制结束${r.reason ? '（' + r.reason + '）' : ''}：共 ${steps.length} 步`, steps.length ? 'success' : 'warning')
  }
}

function normStep(s: any): MacroStep {
  return {
    kind: (s.kind || 'key') as MacroStepKind,
    keys: Array.isArray(s.keys) ? s.keys.map((k: any) => String(k)) : [],
    holdMs: Number(s.holdMs ?? 30),
    delayMs: Number(s.delayMs ?? 0),
    x: Number(s.x ?? 0),
    y: Number(s.y ?? 0),
    button: s.button ?? 'left',
    wheel: Number(s.wheel ?? 120),
    text: s.text ?? '',
  }
}

// ==================== 步骤操作 ====================

function addStep(kind: MacroStepKind) {
  const d = draft.value
  if (!d) return
  const s = blankStep(kind)
  const at = selectedIndex.value >= 0 ? selectedIndex.value + 1 : d.steps.length
  d.steps.splice(at, 0, s)
  selectedIndex.value = at
  dirty.value = true
  void nextTick(() => scrollTimeline(true))
}

function moveStep(i: number, delta: number) {
  const d = draft.value
  if (!d) return
  const j = i + delta
  if (j < 0 || j >= d.steps.length) return
  const [s] = d.steps.splice(i, 1)
  d.steps.splice(j, 0, s)
  selectedIndex.value = j
  dirty.value = true
}

function duplicateStep(i: number) {
  const d = draft.value
  if (!d) return
  d.steps.splice(i + 1, 0, deepClone(d.steps[i]))
  selectedIndex.value = i + 1
  dirty.value = true
}

function removeStep(i: number) {
  const d = draft.value
  if (!d) return
  d.steps.splice(i, 1)
  if (selectedIndex.value >= d.steps.length) selectedIndex.value = d.steps.length - 1
  dirty.value = true
}

function selectStep(i: number) {
  selectedIndex.value = i
  keysEdit.value = draft.value?.steps[i]?.keys.join(' + ') ?? ''
  keysError.value = ''
}

function setStepKind(kind: MacroStepKind) {
  const s = selectedStep.value
  if (!s) return
  s.kind = kind
  if (kind === 'key' && !s.keys.length) s.keys = ['K']
  if (kind === 'delay' && s.delayMs <= 0) s.delayMs = 200
  if (kind === 'text' && !s.text) s.text = ''
  keysEdit.value = s.keys.join(' + ')
  dirty.value = true
}

function applyKeysEdit(v: string) {
  keysEdit.value = v
  const compact = v.replace(/\s+/g, '')
  const parsed = parseKeyToken(compact)
  if (!parsed) { keysError.value = '无法识别，示例：K、S + D、CTRL + K'; return }
  keysError.value = ''
  if (selectedStep.value) selectedStep.value.keys = parsed
}

function scrollTimeline(bottom = false) {
  const el = timeline.value
  if (!el) return
  if (bottom) el.scrollTop = el.scrollHeight
}

// ==================== 展示辅助 ====================

function stepLabel(s: MacroStep): string {
  switch (s.kind) {
    case 'delay': return `等待 ${s.delayMs} ms`
    case 'mouse_click': {
      const name = buttonOptions.find(b => b.value === s.button)?.label ?? '左键'
      return `${name}${s.x >= 0 && s.y >= 0 ? ` @ ${s.x}, ${s.y}` : '（当前位置）'}`
    }
    case 'mouse_wheel': return `滚轮${s.wheel >= 0 ? '上' : '下'} ${Math.abs(s.wheel) / 120} 格`
    case 'mouse_move': return `移动到 ${s.x}, ${s.y}`
    case 'text': return `输入「${(s.text || '').slice(0, 18)}${(s.text || '').length > 18 ? '…' : ''}」`
    default: return s.keys.length ? s.keys.join(' + ') : '（空按键）'
  }
}

function stepSub(s: MacroStep): string {
  const parts: string[] = []
  if (s.kind === 'key' || s.kind === 'mouse_click') parts.push(`按住 ${s.holdMs}ms`)
  if (s.kind !== 'delay' && s.delayMs > 0) parts.push(`之后等 ${s.delayMs}ms`)
  if (s.kind === 'text' && s.delayMs > 0) parts.push(`之后等 ${s.delayMs}ms`)
  return parts.join(' · ') || '无额外延时'
}

function stepIcon(s: MacroStep) {
  switch (s.kind) {
    case 'delay': return Timer
    case 'text': return Type
    case 'mouse_move': return MousePointerClick
    case 'mouse_click': return MousePointerClick
    case 'mouse_wheel': return MousePointerClick
    default: return Keyboard
  }
}

function triggerLabel(m: MacroRecord) {
  return m.trigger || '未设置'
}

// ==================== 触发键录入 ====================

function toggleCapture() {
  capturing.value = !capturing.value
  capturePreview.value = ''
}

function keyFromEvent(e: KeyboardEvent): string | null {
  const code = e.code
  if (code.startsWith('Key')) return code.slice(3).toUpperCase()
  if (code.startsWith('Digit')) return code.slice(5)
  if (code.startsWith('Numpad')) {
    const rest = code.slice(6)
    if (/^\d$/.test(rest)) return 'NUMPAD' + rest
    if (rest === 'Add') return 'NUMPADADD'
    if (rest === 'Subtract') return 'NUMPADSUBTRACT'
    if (rest === 'Multiply') return 'NUMPADMULTIPLY'
    if (rest === 'Divide') return 'NUMPADDIVIDE'
    if (rest === 'Decimal') return 'NUMPADDECIMAL'
    if (rest === 'Enter') return 'ENTER'
  }
  if (/^F\d{1,2}$/.test(code)) return code
  const map: Record<string, string> = {
    Space: 'SPACE', Enter: 'ENTER', NumpadEnter: 'ENTER', Escape: 'ESC', Tab: 'TAB', Backspace: 'BACKSPACE',
    Delete: 'DELETE', Insert: 'INSERT', Home: 'HOME', End: 'END', PageUp: 'PGUP', PageDown: 'PGDN',
    ArrowUp: 'UP', ArrowDown: 'DOWN', ArrowLeft: 'LEFT', ArrowRight: 'RIGHT',
    ControlLeft: 'CTRL', ControlRight: 'CTRL', ShiftLeft: 'SHIFT', ShiftRight: 'SHIFT',
    AltLeft: 'ALT', AltRight: 'ALT', MetaLeft: 'WIN', MetaRight: 'WIN',
    Backquote: '`', Minus: '-', Equal: '=', BracketLeft: '[', BracketRight: ']', Backslash: '\\',
    Semicolon: ';', Quote: "'", Comma: ',', Period: '.', Slash: '/',
    CapsLock: 'CAPSLOCK', NumLock: 'NUMLOCK', ScrollLock: 'SCROLLLOCK', PrintScreen: 'PRINTSCREEN', Pause: 'PAUSE',
  }
  return map[code] ?? null
}

function onCaptureKey(e: KeyboardEvent) {
  if (!capturing.value) return
  e.preventDefault()
  e.stopPropagation()
  if (e.key === 'Escape') { capturing.value = false; capturePreview.value = ''; return }
  const mods: string[] = []
  if (e.ctrlKey) mods.push('CTRL')
  if (e.shiftKey) mods.push('SHIFT')
  if (e.altKey) mods.push('ALT')
  if (e.metaKey) mods.push('WIN')
  const key = keyFromEvent(e)
  if (!key) return
  if (isModifierName(key)) {
    capturePreview.value = (mods.length ? mods.join('+') + '+' : '') + '…'
    return
  }
  const parts = [...mods.filter(m => m !== key), key]
  const trigger = parts.join('+')
  if (draft.value) {
    draft.value.trigger = trigger
    draft.value.triggerMode = parts.length > 1 ? 'hotkey' : draft.value.triggerMode
  }
  capturing.value = false
  capturePreview.value = ''
  toast('触发键：' + trigger, 'info')
}

function onCaptureMouse(e: MouseEvent) {
  if (!capturing.value) return
  if (e.button === 1) {
    e.preventDefault()
    if (draft.value) draft.value.trigger = 'MOUSEMIDDLE'
    capturing.value = false
    capturePreview.value = ''
    toast('触发键：鼠标中键', 'info')
  } else if (e.button === 3 || e.button === 4) {
    e.preventDefault()
    const trigger = e.button === 3 ? 'XBUTTON1' : 'XBUTTON2'
    if (draft.value) {
      draft.value.trigger = trigger
      draft.value.triggerMode = 'remap'
    }
    capturing.value = false
    capturePreview.value = ''
    toast('触发键：' + trigger + '（左侧键=XBUTTON1，右侧键=XBUTTON2）', 'info')
  }
}

// ==================== 生命周期 ====================

watch(draft, () => {
  if (!suppressDirty && draft.value) dirty.value = true
}, { deep: true, flush: 'sync' })

watch(selectedStep, (s) => {
  if (s) { keysEdit.value = s.keys.join(' + '); keysError.value = '' }
})

onMounted(() => {
  void load(true)
  unsubscribe = onMacroEvent({ step: onRecordStep, recording: onRecordState, state: (s) => { recording.value = s.recording; running.value = s.running } })
  window.addEventListener('keydown', onCaptureKey, true)
  window.addEventListener('mousedown', onCaptureMouse, true)
  host.macroState()
})

onBeforeUnmount(() => {
  if (recording.value) host.macroRecordStop()
  stopRunPolling()
  unsubscribe?.()
  window.removeEventListener('keydown', onCaptureKey, true)
  window.removeEventListener('mousedown', onCaptureMouse, true)
})
</script>

<template>
  <div class="page macro-page">
    <PageHeader title="按键精灵" subtitle="全局热键、拦截键、录制与一键连招（SendInput 扫描码）">
      <div class="global-toggle" :class="{ off: !globalEnabled }">
        <power :size="15" />
        <span>{{ globalEnabled ? '总开关已启用' : '总开关已暂停' }}</span>
        <n-switch :value="globalEnabled" size="small" @update:value="toggleGlobal" />
      </div>
      <n-button @click="newMacro"><template #icon><plus :size="15" /></template>新建宏</n-button>
      <n-button :loading="loading" @click="load(false)"><template #icon><refresh-cw :size="15" /></template></n-button>
    </PageHeader>

    <div class="macro-layout">
      <aside class="macro-list panel">
        <div class="list-head">
          <span>我的宏</span>
          <span class="muted">{{ macros.length }} 个</span>
        </div>
        <div class="list-scroll">
          <button
            v-for="m in macros"
            :key="m.id"
            class="macro-card"
            :class="{ active: m.id === selectedId, disabled: !m.enabled }"
            @click="selectMacro(m)"
          >
            <div class="mc-top">
              <strong :title="m.name">{{ m.name }}</strong>
              <n-switch :value="m.enabled" size="small" @click.stop @update:value="toggleEnabled(m)" />
            </div>
            <div class="mc-meta">
              <kbd>{{ triggerLabel(m) }}</kbd>
              <span class="mode-tag">{{ modeLabel(m) }}</span>
              <span>{{ m.steps.length }} 步</span>
            </div>
            <div v-if="m.hotkeyError" class="mc-error" :title="m.hotkeyError">
              <alert-triangle :size="12" /> {{ m.hotkeyError }}
            </div>
          </button>
          <div v-if="!macros.length" class="list-empty">
            <gamepad2 :size="30" :stroke-width="1.3" />
            <p>还没有宏</p>
            <span>点右上角「新建宏」，或用下方示例快速开始。</span>
          </div>
        </div>
        <div class="list-foot">
          <info :size="12" />
          <span>热键在所有窗口生效；录制按 F12 结束。</span>
        </div>
      </aside>

      <section v-if="draft" class="macro-editor panel">
        <div v-if="recording" class="rec-banner">
          <span class="rec-dot" />
          <radio :size="16" />
          <strong>正在录制</strong>
          <span>已捕获 {{ liveSteps.length }} 步 · 在目标窗口操作，按 <kbd>F12</kbd> 结束</span>
          <n-button size="small" type="error" @click="stopRecording"><template #icon><square :size="13" /></template>停止录制</n-button>
        </div>

        <div class="editor-head">
          <n-input v-model:value="draft.name" class="name-input" placeholder="宏名称" />
          <n-tag v-if="dirty" type="warning" size="small" :bordered="false">未保存</n-tag>
          <div class="head-actions">
            <n-button type="primary" :loading="saveBusy" @click="saveMacro"><template #icon><save :size="15" /></template>保存</n-button>
            <n-button :disabled="recording" @click="testRun"><template #icon><play :size="15" /></template>测试运行</n-button>
            <n-button :disabled="!running" @click="stopRun"><template #icon><square :size="14" /></template>停止</n-button>
            <n-button quaternary type="error" @click="removeMacro"><template #icon><trash2 :size="15" /></template></n-button>
          </div>
        </div>

        <div class="editor-grid">
          <div class="field">
            <label>触发方式</label>
            <div class="seg">
              <button :class="{ active: draft.triggerMode === 'hotkey' }" @click="draft.triggerMode = 'hotkey'">
                <zap :size="13" /> 全局热键
              </button>
              <button :class="{ active: draft.triggerMode === 'remap' }" @click="draft.triggerMode = 'remap'">
                <gamepad2 :size="13" /> 拦截键
              </button>
            </div>
            <small>{{ draft.triggerMode === 'remap' ? '按下触发键会被本宏吞掉，替换为宏动作（可单键/鼠标侧键）。' : '保留触发键原本功能，另外执行宏。' }}</small>
          </div>

          <div class="field wide">
            <label>触发键</label>
            <div class="trigger-row">
              <n-input v-model:value="draft.trigger" placeholder="F1 / CTRL+SHIFT+1 / XBUTTON1" @blur="draft.trigger = draft.trigger.toUpperCase().replace(/\s+/g, '')" />
              <n-button :type="capturing ? 'primary' : 'default'" @click="toggleCapture">
                <template #icon><circle-dot :size="14" /></template>
                {{ capturing ? '请按键…' : '录入' }}
              </n-button>
            </div>
            <small v-if="capturing" class="capture-hint">正在等待按键：{{ capturePreview || '按下组合键；Esc 取消' }}（鼠标中键/侧键也可）</small>
            <small v-if="hotkeyWarning" class="warn"><alert-triangle :size="12" /> {{ hotkeyWarning }}</small>
            <small v-if="draft.hotkeyError" class="err"><alert-triangle :size="12" /> {{ draft.hotkeyError }}</small>
          </div>

          <div class="field">
            <label>重复次数</label>
            <n-input-number v-model:value="draft.repeat" :min="1" :max="999" style="width: 100%" />
          </div>
          <div class="field">
            <label>重复间隔 (ms)</label>
            <n-input-number v-model:value="draft.repeatDelayMs" :min="0" :max="600000" style="width: 100%" />
          </div>
          <div class="field">
            <label>测试准备时间 (ms)</label>
            <n-input-number v-model:value="draft.leadInMs" :min="0" :max="60000" style="width: 100%" />
          </div>
          <div class="field">
            <label>发送方式</label>
            <n-select v-model:value="draft.sendMode" :options="[
              { label: '扫描码（游戏兼容）', value: 'scan' },
              { label: '虚拟键（普通程序）', value: 'virtual' },
            ]" />
            <small>格斗游戏/原始输入用扫描码；网页或普通窗口收不到按键时改用虚拟键。</small>
          </div>
        </div>

        <div class="quick-panel">
          <div class="panel-head">
            <h2><wand2 :size="15" /> 快速输入连招</h2>
            <span>空格分隔步骤；<code>sd</code> 同时按 S+D；<code>延迟200ms</code>；<code>k*3</code> 重复</span>
          </div>
          <n-input
            v-model:value="quickText"
            type="textarea"
            :rows="2"
            placeholder="s sd d 延迟200ms ik 延迟50ms k k k"
            class="quick-input"
          />
          <div class="quick-foot">
            <div class="quick-preview">
              <template v-if="parseResult.steps.length">
                <span v-for="(s, i) in parseResult.steps.slice(0, 24)" :key="i" class="preview-chip" :class="'k-' + s.kind">
                  {{ stepLabel(s) }}<em v-if="s.delayMs">+{{ s.delayMs }}</em>
                </span>
                <span v-if="parseResult.steps.length > 24" class="preview-more">+{{ parseResult.steps.length - 24 }}</span>
              </template>
              <span v-else class="muted">输入后这里会显示解析结果</span>
              <span v-if="parseResult.unknown.length" class="preview-bad">
                <alert-triangle :size="12" /> 无法识别：{{ parseResult.unknown.join('、') }}
              </span>
            </div>
            <div class="quick-actions">
              <n-button size="small" @click="quickText = 's sd  d 延迟200ms ik 延迟50ms k k k'">拳皇示例</n-button>
              <n-button size="small" @click="applyQuick(false)">追加</n-button>
              <n-button size="small" type="primary" @click="applyQuick(true)">替换步骤</n-button>
            </div>
          </div>
        </div>

        <div class="steps-panel">
          <div class="panel-head">
            <h2><list-ordered :size="15" /> 步骤 <span class="step-count">{{ draft.steps.length }}</span></h2>
            <div class="step-add">
              <n-button size="small" @click="addStep('key')"><template #icon><keyboard :size="13" /></template>按键</n-button>
              <n-button size="small" @click="addStep('delay')"><template #icon><timer :size="13" /></template>延迟</n-button>
              <n-button size="small" @click="addStep('mouse_click')"><template #icon><mouse-pointer-click :size="13" /></template>鼠标</n-button>
              <n-button size="small" @click="addStep('text')"><template #icon><type :size="13" /></template>文本</n-button>
              <n-button size="small" type="error" secondary :disabled="recording" @click="startRecording">
                <template #icon><radio :size="13" /></template>录制
              </n-button>
            </div>
          </div>

          <div ref="timeline" class="timeline">
            <div
              v-for="(s, i) in draft.steps"
              :key="i"
              class="step-row"
              :class="{ active: selectedIndex === i }"
              @click="selectStep(i)"
            >
              <span class="step-no">{{ i + 1 }}</span>
              <span class="step-icon"><component :is="stepIcon(s)" :size="15" /></span>
              <div class="step-main">
                <strong>{{ stepLabel(s) }}</strong>
                <span>{{ stepSub(s) }}</span>
              </div>
              <div class="step-actions" @click.stop>
                <button class="mini" title="上移" :disabled="i === 0" @click="moveStep(i, -1)"><arrow-up :size="12" /></button>
                <button class="mini" title="下移" :disabled="i === draft.steps.length - 1" @click="moveStep(i, 1)"><arrow-down :size="12" /></button>
                <button class="mini" title="复制" @click="duplicateStep(i)"><copy :size="12" /></button>
                <button class="mini danger" title="删除" @click="removeStep(i)"><trash2 :size="12" /></button>
              </div>
            </div>
            <div v-if="!draft.steps.length" class="steps-empty">
              <keyboard :size="30" :stroke-width="1.3" />
              <p>还没有步骤</p>
              <span>用上方的快速输入框粘贴连招，或点「录制」实际打一遍。</span>
            </div>
          </div>

          <div v-if="selectedStep" class="step-editor">
            <div class="field">
              <label>步骤类型</label>
              <n-select :value="selectedStep.kind" :options="stepKindOptions" @update:value="setStepKind" />
            </div>

            <template v-if="selectedStep.kind === 'key'">
              <div class="field wide">
                <label>按键（<code>+</code> 表示同时按下）</label>
                <n-input :value="keysEdit" placeholder="K 或 S + D 或 CTRL + K" @update:value="applyKeysEdit" />
                <small v-if="keysError" class="err">{{ keysError }}</small>
              </div>
              <div class="field">
                <label>按住 (ms)</label>
                <n-input-number v-model:value="selectedStep.holdMs" :min="1" :max="10000" style="width: 100%" />
              </div>
            </template>

            <div v-else-if="selectedStep.kind === 'delay'" class="field">
              <label>等待 (ms)</label>
              <n-input-number v-model:value="selectedStep.delayMs" :min="0" :max="600000" style="width: 100%" />
            </div>

            <template v-else-if="selectedStep.kind === 'mouse_click'">
              <div class="field">
                <label>鼠标键</label>
                <n-select v-model:value="selectedStep.button" :options="buttonOptions" />
              </div>
              <div class="field">
                <label>X（-1 = 当前位置）</label>
                <n-input-number v-model:value="selectedStep.x" :min="-1" :max="20000" style="width: 100%" />
              </div>
              <div class="field">
                <label>Y（-1 = 当前位置）</label>
                <n-input-number v-model:value="selectedStep.y" :min="-1" :max="20000" style="width: 100%" />
              </div>
              <div class="field">
                <label>按住 (ms)</label>
                <n-input-number v-model:value="selectedStep.holdMs" :min="1" :max="10000" style="width: 100%" />
              </div>
            </template>

            <template v-else-if="selectedStep.kind === 'mouse_wheel'">
              <div class="field">
                <label>滚动量（120 = 一格）</label>
                <n-input-number v-model:value="selectedStep.wheel" :min="-2880" :max="2880" :step="120" style="width: 100%" />
              </div>
              <div class="field">
                <label>X（-1 = 当前位置）</label>
                <n-input-number v-model:value="selectedStep.x" :min="-1" :max="20000" style="width: 100%" />
              </div>
              <div class="field">
                <label>Y（-1 = 当前位置）</label>
                <n-input-number v-model:value="selectedStep.y" :min="-1" :max="20000" style="width: 100%" />
              </div>
            </template>

            <template v-else-if="selectedStep.kind === 'mouse_move'">
              <div class="field">
                <label>X</label>
                <n-input-number v-model:value="selectedStep.x" :min="0" :max="20000" style="width: 100%" />
              </div>
              <div class="field">
                <label>Y</label>
                <n-input-number v-model:value="selectedStep.y" :min="0" :max="20000" style="width: 100%" />
              </div>
            </template>

            <template v-else-if="selectedStep.kind === 'text'">
              <div class="field wide">
                <label>输入文本</label>
                <n-input v-model:value="selectedStep.text" type="textarea" :rows="2" placeholder="要输入的文本" />
              </div>
            </template>

            <div v-if="selectedStep.kind !== 'delay'" class="field">
              <label>之后等待 (ms)</label>
              <n-input-number v-model:value="selectedStep.delayMs" :min="0" :max="600000" style="width: 100%" />
            </div>
          </div>
        </div>

        <details class="help">
          <summary><info :size="13" /> 键名与写法参考</summary>
          <div class="help-body">
            <p><b>直接写</b>：<code>A-Z</code>、<code>0-9</code>、<code>F1-F24</code>、<code>SPACE</code>、<code>ENTER</code>、<code>ESC</code>、<code>TAB</code>、<code>UP/DOWN/LEFT/RIGHT</code>、<code>NUMPAD1</code>、<code>XTRA…</code></p>
            <p><b>组合键</b>：<code>CTRL+K</code>、<code>SHIFT+F1</code>；快速输入里 <code>sd</code> 表示同时按 S+D，<code>s d</code> 表示先后按。</p>
            <p><b>延迟</b>：<code>延迟200ms</code>、<code>等1s</code>、<code>+500</code>；<b>重复</b>：<code>k*3</code>。</p>
            <p><b>鼠标</b>：<code>左键</code>、<code>右键</code>、<code>中键</code>、<code>滚轮上</code>、<code>滚轮下</code>；录制会记录点击坐标。</p>
            <p><b>热键</b>：全局热键组合键建议加 Ctrl/Shift；单键或鼠标侧键请用「拦截键」模式。F12 保留给录制停止。</p>
          </div>
        </details>
      </section>

      <section v-else class="macro-editor panel editor-empty">
        <gamepad2 :size="44" :stroke-width="1.1" />
        <h2>按键精灵</h2>
        <p>新建一个宏，或用快速输入框直接粘贴连招。</p>
        <n-button type="primary" @click="newMacro"><template #icon><plus :size="15" /></template>新建宏</n-button>
      </section>
    </div>
  </div>
</template>

<style scoped>
.macro-page { display: flex; flex-direction: column; min-height: 0; padding: 18px 22px 22px; gap: 14px; overflow: hidden; }
.global-toggle { display: flex; align-items: center; gap: 8px; padding: 5px 10px; border-radius: 999px; border: 1px solid var(--border); background: var(--panel); font-size: 11px; color: var(--text-2); }
.global-toggle.off { border-color: rgba(255, 200, 87, .35); color: var(--warning); }
.macro-layout { flex: 1; min-height: 0; min-width: 0; display: grid; grid-template-columns: 268px minmax(0, 1fr); gap: 14px; }

.macro-list { display: flex; flex-direction: column; min-height: 0; min-width: 0; overflow: hidden; }
.list-head { flex: none; display: flex; justify-content: space-between; align-items: center; padding: 13px 14px 9px; font-size: 11px; letter-spacing: .06em; color: var(--text-3); text-transform: uppercase; }
.list-head .muted { color: var(--text-3); }
.list-scroll { flex: 1; min-height: 0; overflow-y: auto; padding: 0 9px 9px; display: flex; flex-direction: column; gap: 6px; }
.macro-card { appearance: none; text-align: left; border: 1px solid transparent; background: transparent; color: var(--text); border-radius: 11px; padding: 10px 11px; cursor: pointer; display: flex; flex-direction: column; gap: 6px; transition: background .15s, border-color .15s; }
.macro-card:hover { background: var(--panel-2); }
.macro-card.active { background: var(--accent-soft); border-color: var(--accent-ring); }
.macro-card.disabled { opacity: .58; }
.mc-top { display: flex; align-items: center; gap: 8px; }
.mc-top strong { flex: 1; font-size: 12.5px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mc-meta { display: flex; align-items: center; gap: 7px; font-size: 10.5px; color: var(--text-3); flex-wrap: wrap; }
.mc-meta kbd, .list-foot kbd, .help kbd, .rec-banner kbd { font-family: ui-monospace, Consolas, monospace; font-size: 10px; background: var(--panel-3); color: var(--text-2); border: 1px solid var(--border); border-bottom-color: var(--border-strong); border-radius: 5px; padding: 1px 5px; }
.mode-tag { color: var(--accent); background: var(--accent-soft); border-radius: 999px; padding: 0 6px; }
.mc-error { display: flex; align-items: center; gap: 5px; color: var(--warning); font-size: 10.5px; line-height: 1.3; }
.list-empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 40px 16px; text-align: center; color: var(--text-3); }
.list-empty p { margin: 0; color: var(--text-2); font-size: 12px; }
.list-empty span { font-size: 10.5px; line-height: 1.5; }
.list-foot { flex: none; display: flex; gap: 7px; align-items: flex-start; padding: 10px 13px; border-top: 1px solid var(--border); color: var(--text-3); font-size: 10.5px; line-height: 1.5; }

.macro-editor { display: flex; flex-direction: column; min-height: 0; min-width: 0; overflow-y: auto; overflow-x: hidden; padding: 14px 16px 16px; gap: 13px; }
.editor-empty { align-items: center; justify-content: center; color: var(--text-3); text-align: center; }
.editor-empty h2 { margin: 6px 0 0; color: var(--text); font-size: 15px; }
.editor-empty p { margin: 0 0 8px; font-size: 12px; }

.rec-banner { display: flex; align-items: center; gap: 9px; padding: 9px 12px; border-radius: 11px; border: 1px solid rgba(255, 107, 129, .4); background: rgba(255, 107, 129, .1); color: #ffb3c0; font-size: 11.5px; }
.rec-banner strong { color: #ff8ca0; }
.rec-banner span:not(.rec-dot) { flex: 1; }
.rec-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--danger); box-shadow: 0 0 0 0 rgba(255, 107, 129, .6); animation: pulse 1.2s infinite; }
@keyframes pulse { 70% { box-shadow: 0 0 0 9px rgba(255, 107, 129, 0); } 100% { box-shadow: 0 0 0 0 rgba(255, 107, 129, 0); } }

.editor-head { display: flex; align-items: center; gap: 9px; flex-wrap: wrap; }
.name-input { flex: 1; min-width: 180px; }
.head-actions { display: flex; gap: 7px; align-items: center; flex-wrap: wrap; }

.editor-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 11px 13px; }
.field { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.field.wide { grid-column: span 2; }
.field > label { font-size: 10.5px; color: var(--text-3); letter-spacing: .03em; }
.field small { font-size: 10.5px; color: var(--text-3); line-height: 1.4; }
.field small.warn, small.warn { color: var(--warning); display: flex; align-items: center; gap: 5px; }
.field small.err, small.err { color: var(--danger); display: flex; align-items: center; gap: 5px; }
.field code, .help code, .quick-panel code { font-family: ui-monospace, Consolas, monospace; font-size: 10.5px; background: var(--panel-3); border-radius: 4px; padding: 0 4px; color: var(--text-2); }
.capture-hint { color: var(--accent) !important; }

.seg { display: flex; gap: 5px; }
.seg button { flex: 1; display: flex; align-items: center; justify-content: center; gap: 5px; padding: 7px 8px; border-radius: 9px; border: 1px solid var(--border); background: var(--panel-2); color: var(--text-2); font-size: 11px; cursor: pointer; }
.seg button.active { border-color: var(--accent-ring); background: var(--accent-soft); color: var(--accent); }
.trigger-row { display: flex; gap: 7px; }

.quick-panel { border: 1px solid var(--border); border-radius: 12px; background: var(--panel-2); padding: 11px 12px; display: flex; flex-direction: column; gap: 8px; }
.quick-panel .panel-head { margin: 0; }
.quick-input :deep(textarea) { font-family: ui-monospace, Consolas, monospace; font-size: 12px; }
.quick-foot { display: flex; align-items: flex-start; gap: 10px; flex-wrap: wrap; }
.quick-preview { flex: 1; display: flex; flex-wrap: wrap; gap: 4px; min-height: 22px; align-content: flex-start; }
.preview-chip { display: inline-flex; align-items: center; gap: 4px; font-size: 10.5px; color: var(--text-2); border: 1px solid var(--border); background: var(--panel); border-radius: 999px; padding: 1px 7px; }
.preview-chip em { font-style: normal; color: var(--warning); }
.preview-chip.k-key { border-color: var(--accent-ring); color: var(--accent); }
.preview-chip.k-delay { border-color: rgba(255, 200, 87, .35); color: var(--warning); }
.preview-bad { display: inline-flex; align-items: center; gap: 4px; color: var(--danger); font-size: 10.5px; }
.preview-more { font-size: 10.5px; color: var(--text-3); }
.quick-actions { display: flex; gap: 6px; flex: none; }

.panel-head { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.panel-head h2 { display: flex; align-items: center; gap: 6px; margin: 0; font-size: 12px; font-weight: 600; color: var(--text); }
.panel-head > span { font-size: 10.5px; color: var(--text-3); }
.step-count { font-size: 10px; color: var(--accent); background: var(--accent-soft); border-radius: 999px; padding: 0 6px; }
.step-add { display: flex; gap: 5px; flex-wrap: wrap; }
.steps-panel { display: flex; flex-direction: column; gap: 9px; }
.timeline { max-height: 300px; overflow-y: auto; border: 1px solid var(--border); border-radius: 11px; background: rgba(0, 0, 0, .14); padding: 6px; display: flex; flex-direction: column; gap: 3px; }
.step-row { display: flex; align-items: center; gap: 9px; padding: 6px 8px; border-radius: 8px; border: 1px solid transparent; cursor: pointer; }
.step-row:hover { background: var(--panel-2); }
.step-row.active { background: var(--accent-soft); border-color: var(--accent-ring); }
.step-no { width: 20px; flex: none; text-align: center; font-size: 10px; color: var(--text-3); font-variant-numeric: tabular-nums; }
.step-icon { width: 26px; height: 26px; flex: none; display: grid; place-items: center; border-radius: 7px; background: var(--panel-3); color: var(--text-2); }
.step-row.active .step-icon { background: color-mix(in srgb, var(--accent) 22%, transparent); color: var(--accent); }
.step-main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
.step-main strong { font-size: 11.5px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.step-main span { font-size: 10px; color: var(--text-3); }
.step-actions { display: flex; gap: 2px; opacity: .25; transition: opacity .12s; }
.step-row:hover .step-actions, .step-row.active .step-actions { opacity: 1; }
.mini { appearance: none; border: none; background: transparent; color: var(--text-3); width: 22px; height: 22px; border-radius: 6px; cursor: pointer; display: grid; place-items: center; }
.mini:hover { background: var(--panel-3); color: var(--text); }
.mini.danger:hover { background: rgba(255, 107, 129, .16); color: var(--danger); }
.mini:disabled { opacity: .25; cursor: default; }
.steps-empty { display: flex; flex-direction: column; align-items: center; gap: 5px; padding: 28px 10px; text-align: center; color: var(--text-3); }
.steps-empty p { margin: 0; color: var(--text-2); font-size: 12px; }
.steps-empty span { font-size: 10.5px; }

.step-editor { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px 12px; padding: 11px 12px; border: 1px dashed var(--border-strong); border-radius: 11px; background: rgba(255, 255, 255, .015); }
.step-editor .field.wide { grid-column: span 2; }

.help { border-top: 1px solid var(--border); padding-top: 8px; }
.help summary { display: flex; align-items: center; gap: 5px; cursor: pointer; font-size: 11px; color: var(--text-3); list-style: none; }
.help summary::-webkit-details-marker { display: none; }
.help-body { padding-top: 7px; display: flex; flex-direction: column; gap: 5px; font-size: 10.5px; color: var(--text-3); }
.help-body p { margin: 0; line-height: 1.6; }
.help-body b { color: var(--text-2); }

@media (max-width: 1180px) {
  .macro-layout { grid-template-columns: 230px minmax(0, 1fr); }
  .editor-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .field.wide { grid-column: span 2; }
  .step-editor { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}

@media (max-width: 1120px) {
  .macro-layout { grid-template-columns: minmax(0, 1fr); grid-template-rows: minmax(0, 250px) minmax(0, 1fr); overflow-y: auto; }
  .macro-list { max-height: 250px; }
  .macro-editor { overflow: visible; }
}
</style>
