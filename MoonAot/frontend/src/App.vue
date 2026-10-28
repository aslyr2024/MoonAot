<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import {
  NConfigProvider, NDialogProvider, NMessageProvider, NNotificationProvider, dateZhCN, zhCN,
} from 'naive-ui'
import { naiveTheme, themeOverrides } from './theme'
import AppShell from './components/AppShell.vue'
import ImportOverlay from './components/ImportOverlay.vue'
import MessageBridge from './components/MessageBridge.vue'

const route = useRoute()
const isViewer = computed(() => !!route.meta.viewer)
</script>

<template>
  <n-config-provider
    :theme="naiveTheme"
    :theme-overrides="themeOverrides"
    :locale="zhCN"
    :date-locale="dateZhCN"
  >
    <n-message-provider placement="bottom" :max="3">
      <n-dialog-provider>
        <n-notification-provider>
          <MessageBridge />
          <div v-if="!isViewer" class="shell-slot">
            <AppShell />
          </div>
          <div v-else class="viewer-slot">
            <router-view />
          </div>
          <ImportOverlay />
        </n-notification-provider>
      </n-dialog-provider>
    </n-message-provider>
  </n-config-provider>
</template>
