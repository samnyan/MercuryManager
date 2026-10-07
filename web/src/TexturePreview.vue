<script setup lang="ts">
import TexturePreviewImpl from './components/resource/TexturePreview.vue'

defineProps<{ table?: string; field: string; value: unknown }>()
</script>

<template>
  <TexturePreviewImpl :table="table" :field="field" :value="value" />
</template>
