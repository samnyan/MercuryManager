<script setup lang="ts">
import { computed } from 'vue'
import { NFormItem } from 'naive-ui'
import FieldInput from './FieldInput.vue'
import { i18n } from '../../i18n'
import type { Field } from '../../project'
import { type TableSchema, type TableFieldSchema, formatFieldLabel } from '../../tableSchema'

const props = defineProps<{ fields: Field[]; schema?: TableSchema }>()

const schemaFieldMap = computed(() => {
  const map = new Map<string, TableFieldSchema>()
  if (props.schema) {
    for (const f of props.schema.fields) {
      map.set(f.key, f)
    }
  }
  return map
})

function getLabel(fieldName: string): string {
  const fSchema = schemaFieldMap.value.get(fieldName)
  if (fSchema) {
    return formatFieldLabel(fSchema, i18n.global.locale.value)
  }
  return fieldName
}
</script>

<template>
  <div class="asset-form">
    <n-form-item
      v-for="field in fields"
      :key="field.name"
      :label="getLabel(field.name)"
      class="form-item-wrapper"
    >
      <field-input
        :field="field"
        :table="schema?.id"
        :field-schema="schemaFieldMap.get(field.name)"
      />
    </n-form-item>
  </div>
</template>

<style scoped>
.asset-form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 20px;
}
.form-item-wrapper {
  margin-bottom: 12px;
}
@media (max-width: 768px) {
  .asset-form {
    grid-template-columns: 1fr;
    gap: 0;
  }
}
</style>
