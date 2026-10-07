<script setup lang="ts">
import { NCheckbox, NInput, NInputNumber } from 'naive-ui'
import type { Field } from './project'
defineProps<{field:Field}>()
</script>
<template><n-checkbox v-if="field.type === 'BoolPropertyData'" v-model:checked="field.value as boolean" :disabled="field.readOnly"/><n-input-number v-else-if="typeof field.value === 'number'" v-model:value="field.value as number" :disabled="field.readOnly" :precision="field.type === 'FloatPropertyData' ? undefined : 0" :min="['UInt32PropertyData','BytePropertyData'].includes(field.type) ? 0 : undefined" :max="field.type === 'BytePropertyData' ? 255 : undefined" style="width:100%"/><n-input v-else :type="['ArrayPropertyData','MapPropertyData','StrPropertyData'].includes(field.type) ? 'textarea' : 'text'" :autosize="{minRows:2,maxRows:12}" :value="field.value == null ? '' : String(field.value)" :disabled="field.readOnly" @update:value="field.value = $event"/></template>
