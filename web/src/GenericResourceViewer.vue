<script setup lang="ts">
import GenericResourceViewerImpl from './components/resource/GenericResourceViewer.vue'

defineProps<{
  data: unknown
  path?: string
  rawUrl?: string
  previewSrc?: string
  previewError?: string
}>()
</script>

<template>
  <GenericResourceViewerImpl
    :data="data"
    :path="path"
    :preview-src="previewSrc"
    :preview-error="previewError"
    :raw-url="rawUrl"
  />
</template>
