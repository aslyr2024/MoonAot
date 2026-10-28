<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { EditorView, basicSetup } from 'codemirror'
import { EditorState, Compartment } from '@codemirror/state'
import { keymap } from '@codemirror/view'
import { indentWithTab } from '@codemirror/commands'
import { json } from '@codemirror/lang-json'
import { javascript } from '@codemirror/lang-javascript'
import { python } from '@codemirror/lang-python'
import { cpp } from '@codemirror/lang-cpp'
import { java } from '@codemirror/lang-java'
import { xml } from '@codemirror/lang-xml'
import { css } from '@codemirror/lang-css'
import { sql } from '@codemirror/lang-sql'
import { html } from '@codemirror/lang-html'
import { rust } from '@codemirror/lang-rust'
import { php } from '@codemirror/lang-php'
import { go } from '@codemirror/lang-go'
import { oneDark } from '@codemirror/theme-one-dark'
import { Check, RefreshCw, Save, X } from 'lucide-vue-next'
import { api } from '../../api'
import { host } from '../../host'
import { toast, toastError } from '../../ui'
import { basename, formatBytes } from '../../utils'

const route = useRoute()
const path = ref(String(route.query.path || ''))
const loading = ref(true)
const error = ref('')
const dirty = ref(false)
const saving = ref(false)
const info = ref<{ name: string; ext: string; size: number; encoding: string; modified: number } | null>(null)
const hostEl = ref<HTMLElement | null>(null)

let view: EditorView | null = null
let savedText = ''

const langCompartment = new Compartment()

function languageFor(ext: string) {
  switch (ext) {
    case '.json': case '.jsonc': return json()
    case '.js': case '.mjs': case '.cjs': return javascript()
    case '.jsx': return javascript({ jsx: true })
    case '.ts': return javascript({ typescript: true })
    case '.tsx': return javascript({ typescript: true, jsx: true })
    case '.vue': return html()
    case '.py': case '.pyw': return python()
    case '.cs': case '.c': case '.h': case '.cpp': case '.hpp': case '.cc': return cpp()
    case '.java': case '.kt': case '.kts': return java()
    case '.xml': case '.csproj': case '.props': case '.xaml': return xml()
    case '.css': case '.scss': case '.less': return css()
    case '.sql': return sql()
    case '.html': case '.htm': return html()
    case '.rs': return rust()
    case '.php': return php()
    case '.go': return go()
    default: return []
  }
}

function setDoc(text: string) {
  if (!view) return
  view.dispatch({
    changes: { from: 0, to: view.state.doc.length, insert: text },
    effects: langCompartment.reconfigure(languageFor(info.value?.ext || '')),
  })
}

function render(text: string) {
  if (!hostEl.value) return
  view = new EditorView({
    parent: hostEl.value,
    state: EditorState.create({
      doc: text,
      extensions: [
        basicSetup,
        oneDark,
        keymap.of([indentWithTab]),
        langCompartment.of(languageFor(info.value?.ext || '')),
        EditorView.updateListener.of(u => {
          if (u.docChanged) dirty.value = u.state.doc.toString() !== savedText
        }),
        EditorView.theme({
          '&': { height: '100%', fontSize: '13px' },
          '.cm-scroller': { fontFamily: "'Cascadia Code','JetBrains Mono',Consolas,monospace", lineHeight: '1.55' },
          '.cm-content': { padding: '10px 0' },
        }),
      ],
    }),
  })
}

async function load(refresh = false) {
  if (!path.value) { loading.value = false; error.value = '未指定文件'; return }
  loading.value = true
  error.value = ''
  try {
    const res = await api.fileText(path.value)
    if (!res.ok) { error.value = '读取失败'; return }
    info.value = { name: res.name, ext: res.ext, size: res.size, encoding: res.encoding, modified: res.modified }
    savedText = res.text
    dirty.value = false
    if (view) setDoc(res.text)
    else render(res.text)
    host.setTitle(basename(path.value))
    if (refresh) toast('已重新载入')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

async function save() {
  if (!view || saving.value) return
  saving.value = true
  try {
    const text = view.state.doc.toString()
    await api.saveFileText(path.value, text, info.value?.encoding || 'utf-8')
    savedText = text
    dirty.value = false
    if (info.value) info.value.size = new TextEncoder().encode(text).length
    toast('已保存')
  } catch (e) { toastError(e) } finally { saving.value = false }
}

function close() {
  if (dirty.value && !window.confirm('有未保存的修改，仍要关闭吗？')) return
  host.closeWindow()
}

function onKey(ev: KeyboardEvent) {
  if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 's') {
    ev.preventDefault()
    void save()
  } else if (ev.key === 'Escape') {
    ev.preventDefault()
    close()
  }
}

const extLabel = computed(() => (info.value?.ext || '').replace('.', '').toUpperCase() || 'TEXT')

onMounted(async () => {
  window.addEventListener('keydown', onKey)
  await load()
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKey)
  view?.destroy()
  view = null
})
</script>

<template>
  <div class="viewer code-viewer">
    <header class="viewer-bar">
      <div class="viewer-left">
        <button class="viewer-btn" title="关闭" @click="close"><x :size="17" /></button>
        <div class="viewer-name">
          <strong :title="path">{{ basename(path) || '代码编辑器' }}</strong>
          <span v-if="info">{{ extLabel }} · {{ formatBytes(info.size) }} · {{ info.encoding }}<template v-if="dirty"> · 已修改</template></span>
        </div>
      </div>
      <div class="viewer-right">
        <button class="viewer-btn" title="重新载入" @click="load(true)"><refresh-cw :size="16" /></button>
        <button class="viewer-btn save-btn" :class="{ dirty }" title="保存（Ctrl+S）" :disabled="saving" @click="save">
          <check v-if="!saving && !dirty" :size="16" /><save v-else :size="16" />
        </button>
      </div>
    </header>
    <div v-if="loading" class="viewer-loading"><span>正在读取文件…</span></div>
    <div v-else-if="error" class="viewer-error">{{ error }}</div>
    <div v-show="!loading && !error" ref="hostEl" class="code-host" />
  </div>
</template>

<style scoped>
.code-viewer { display: flex; flex-direction: column; height: 100%; background: #0c1017; }
.code-host { flex: 1; min-height: 0; overflow: hidden; }
.code-host :deep(.cm-editor) { height: 100%; }
.code-host :deep(.cm-editor.cm-focused) { outline: none; }
.save-btn.dirty { color: var(--accent); }
</style>
