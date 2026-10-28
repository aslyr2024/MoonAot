<script setup lang="ts">
import { useDialog, useMessage } from 'naive-ui'
import { setUiApis } from '../ui'

setUiApis(useMessage(), useDialog())
</script>

<template><span style="display: none" /></template>
