<script setup lang="ts">
import { computed } from 'vue'
import { CheckCircle2, Loader2, X, AlertCircle } from 'lucide-vue-next'
import { closeImportPanel, state } from '../store'
import { host } from '../host'
import { basename } from '../utils'

const s = computed(() => state.importState)
const pct = computed(() => {
  if (!s.value.total) return 0
  return Math.max(2, Math.min(100, Math.round((s.value.done / s.value.total) * 100)))
})
const failed = computed(() => s.value.result && !s.value.result.ok)
</script>

<template>
  <transition name="slide-up">
    <div v-if="s.active" class="import-overlay">
      <div class="import-head">
        <div class="import-title">
          <CheckCircle2 v-if="s.result && s.result.ok" :size="18" class="ok" />
          <AlertCircle v-else-if="failed" :size="18" class="bad" />
          <Loader2 v-else :size="18" class="spin" />
          <span v-if="s.result && s.result.ok">导入完成</span>
          <span v-else-if="failed">导入失败</span>
          <span v-else>正在导入…</span>
        </div>
        <button class="icon-btn subtle" title="关闭" @click="closeImportPanel"><X :size="15" /></button>
      </div>

      <template v-if="s.result">
        <div class="import-summary" :class="{ bad: failed }">
          <span>导入 {{ s.result.imported }}</span>
          <span>跳过 {{ s.result.skipped }}</span>
          <span>失败 {{ s.result.failed }}</span>
        </div>
        <p v-if="s.result.error" class="import-error">{{ s.result.error }}</p>
      </template>
      <template v-else>
        <div class="import-bar"><i :style="{ width: pct + '%' }" /></div>
        <div class="import-meta">
          <span>{{ s.total ? `${s.done} / ${s.total}` : '正在扫描…' }}</span>
          <span v-if="s.total">{{ pct }}%</span>
        </div>
        <p class="import-current" :title="s.current">{{ basename(s.current) || '准备中…' }}</p>
      </template>

      <button v-if="!s.result && host.available" class="import-cancel" @click="host.cancelImport()">取消导入</button>
    </div>
  </transition>
</template>
