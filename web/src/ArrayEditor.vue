<script setup lang="ts">
import ArrayEditorImpl from './components/table/ArrayEditor.vue'

defineProps<{
  value: string | number | boolean | unknown[] | null
  itemType: 'string' | 'number' | 'boolean' | 'object'
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:value': [val: string]
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
