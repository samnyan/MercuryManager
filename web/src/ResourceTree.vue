<script setup lang="ts">
import ResourceTreeImpl from './components/resource/ResourceTree.vue'

const emit = defineEmits<{
  select: []
}>()
</script>

<template>
  <ResourceTreeImpl @select="emit('select')" />
</template>
