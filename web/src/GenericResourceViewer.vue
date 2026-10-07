<script setup lang="ts">
import GenericResourceViewerImpl from './components/resource/GenericResourceViewer.vue'

defineProps<{ data: unknown }>()
</script>

<template>
  <GenericResourceViewerImpl :data="data" />
</template>
