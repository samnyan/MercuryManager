<script setup lang="ts">
import {NFormItem} from 'naive-ui'
import FieldInput from './FieldInput.vue'
import {fieldLabel} from './fieldLabels'
import type {Field} from './project'
defineProps<{fields:Field[]}>()
</script>
<template><div class="asset-form"><n-form-item v-for="field in fields" :key="field.name" :label="fieldLabel(field.name)"><field-input :field="field"/></n-form-item></div></template>
<style scoped>.asset-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:0 20px}@media(max-width:800px){.asset-form{grid-template-columns:1fr}}</style>
