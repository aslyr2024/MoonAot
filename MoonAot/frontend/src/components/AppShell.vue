<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppSidebar from './AppSidebar.vue'
import { loadFacets, loadFonts } from '../store'

const collapsed = ref(false)
try { collapsed.value = localStorage.getItem('moonaot-sidebar') === '1' } catch { }

function toggleSidebar() {
  collapsed.value = !collapsed.value
  try { localStorage.setItem('moonaot-sidebar', collapsed.value ? '1' : '0') } catch { }
}

onMounted(() => {
  loadFonts().catch(() => { })
  loadFacets().catch(() => { })
})
</script>

<template>
  <div class="app-shell" :class="{ 'is-collapsed': collapsed }">
    <AppSidebar :collapsed="collapsed" @toggle="toggleSidebar" />
    <main class="app-main">
      <div class="bg-aurora" aria-hidden="true">
        <i class="aurora a1" /><i class="aurora a2" /><i class="aurora a3" />
      </div>
      <div class="page-host">
        <router-view v-slot="{ Component }">
          <keep-alive :include="['ShelfView', 'RecentView']">
            <component :is="Component" :key="$route.name" />
          </keep-alive>
        </router-view>
      </div>
    </main>
  </div>
</template>
