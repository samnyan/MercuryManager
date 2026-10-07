<script setup lang="ts">
import AssetFormImpl from './components/table/AssetForm.vue'
import type { Field } from './project'
import type { TableSchema } from './tableSchema'

defineProps<{ fields: Field[]; schema?: TableSchema }>()
</script>

<template>
  <AssetFormImpl :fields="fields" :schema="schema" />
</template>
