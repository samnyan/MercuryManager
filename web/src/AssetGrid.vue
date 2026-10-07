<script setup lang="ts">
import AssetGridImpl from './components/table/AssetGrid.vue'
import type { Row } from './project'
import type { TableSchema } from './tableSchema'

defineProps<{ rows: Row[]; search: string; schema?: TableSchema }>()
const emit = defineEmits<{ select: [key: string] }>()
</script>

<template>
  <AssetGridImpl
    :rows="rows"
    :search="search"
    :schema="schema"
    @select="emit('select', $event)"
  />
</template>
