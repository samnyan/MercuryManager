<script setup lang="ts">
import GenericResourceViewerImpl from './components/resource/GenericResourceViewer.vue'

defineProps<{
  data: unknown
  rawUrl?: string
  previewSrc?: string
  previewError?: string
}>()
</script>

<template>
  <GenericResourceViewerImpl
    :data="data"
    :raw-url="rawUrl"
    :preview-src="previewSrc"
    :preview-error="previewError"
  />
</template>
