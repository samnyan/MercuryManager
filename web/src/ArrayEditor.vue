<script setup lang="ts">
import ArrayEditorImpl from './components/table/ArrayEditor.vue'
import type { FieldValue } from './project'

defineProps<{
  value: FieldValue
  itemType: 'string' | 'number' | 'boolean' | 'object'
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:value': [val: FieldValue[]]
}>()
</script>

<template>
  <ArrayEditorImpl
    :value="value"
    :item-type="itemType"
    :disabled="disabled"
    @update:value="emit('update:value', $event)"
  />
</template>
