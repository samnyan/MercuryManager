<script setup lang="ts">
import FieldInputImpl from './components/table/FieldInput.vue'
import type { Field } from './project'
import type { TableFieldSchema } from './tableSchema'

defineProps<{
  field: Field
  table?: string
  fieldSchema?: TableFieldSchema
}>()
</script>

<template>
  <FieldInputImpl
    :field="field"
    :table="table"
    :field-schema="fieldSchema"
  />
</template>
