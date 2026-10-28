<script setup lang="ts">
import { computed, onBeforeUnmount, watch } from 'vue'
import type { Component } from 'vue'

export interface MenuItem {
  key: string
  label: string
  icon?: Component
  danger?: boolean
  disabled?: boolean
  divider?: boolean
}

const props = defineProps<{ show: boolean; x: number; y: number; items: MenuItem[] }>()
const emit = defineEmits<{ select: [string]; close: [] }>()

const style = computed(() => {
  const w = 216
  const h = props.items.reduce((n, it) => n + (it.divider ? 9 : 34), 12)
  const x = Math.min(Math.max(8, props.x), Math.max(8, window.innerWidth - w - 10))
  const y = Math.min(Math.max(8, props.y), Math.max(8, window.innerHeight - h - 10))
  return { left: x + 'px', top: y + 'px', width: w + 'px' }
})

function onDocClick(ev: MouseEvent) {
  const el = ev.target as HTMLElement | null
  if (el && el.closest('.ctx-menu')) return
  emit('close')
}

function onKey(ev: KeyboardEvent) {
  if (ev.key === 'Escape') emit('close')
}

watch(() => props.show, (v) => {
  if (v) {
    setTimeout(() => document.addEventListener('click', onDocClick, true), 0)
    document.addEventListener('keydown', onKey)
    window.addEventListener('blur', () => emit('close'), { once: true })
  } else {
    document.removeEventListener('click', onDocClick, true)
    document.removeEventListener('keydown', onKey)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('click', onDocClick, true)
  document.removeEventListener('keydown', onKey)
})

function pick(item: MenuItem) {
  if (item.disabled) return
  emit('select', item.key)
  emit('close')
}
</script>

<template>
  <teleport to="body">
    <transition name="ctx">
      <div v-if="show" class="ctx-menu" :style="style" @contextmenu.prevent>
        <template v-for="item in items" :key="item.key">
          <div v-if="item.divider" class="ctx-divider" />
          <button
            v-else
            class="ctx-item"
            :class="{ danger: item.danger, disabled: item.disabled }"
            :disabled="item.disabled"
            @click.stop="pick(item)"
          >
            <component :is="item.icon" v-if="item.icon" :size="15" />
            <span>{{ item.label }}</span>
          </button>
        </template>
      </div>
    </transition>
  </teleport>
</template>
