<script setup lang="ts">
import { computed, watch } from 'vue'
import { NCheckbox, NInput, NInputNumber } from 'naive-ui'
import type { Field } from './project'
import { useProject } from './project'
import { tr } from './i18n'
import { useReferenceStore } from './referenceStore'
import type { TableFieldSchema } from './tableSchema'

const props = defineProps<{ field: Field; fieldSchema?: TableFieldSchema }>()

const project = useProject()
const refStore = useReferenceStore()

const targetMessage = computed(() => {
  return props.fieldSchema?.messageLink?.messageTable ?? null
})

watch(
  [targetMessage, () => project.id],
  ([msg, pid]) => {
    if (msg && pid) {
      refStore.loadMessage(pid, msg)
    }
  },
  { immediate: true }
)

const previewState = computed(() => {
  if (!targetMessage.value) return null
  const str = props.field.value == null ? '' : String(props.field.value).trim()
  if (!str) return { status: 'empty' as const }
  return refStore.getText(targetMessage.value, str)
})

const isBoolean = computed(() => {
  return props.fieldSchema?.type === 'boolean' || props.field.type === 'BoolPropertyData'
})

const isNumber = computed(() => {
  return typeof props.field.value === 'number' || (props.fieldSchema?.type === 'number' && typeof props.field.value !== 'string')
})

const isTextarea = computed(() => {
  if (['ArrayPropertyData', 'MapPropertyData', 'StrPropertyData'].includes(props.field.type)) return true
  return props.fieldSchema?.type === 'array' || props.fieldSchema?.type === 'map'
})
</script>

<template>
  <div style="width: 100%">
    <n-checkbox
      v-if="isBoolean"
      v-model:checked="field.value as boolean"
      :disabled="field.readOnly || fieldSchema?.readOnly"
    />
    <n-input-number
      v-else-if="isNumber"
      v-model:value="field.value as number"
      :disabled="field.readOnly || fieldSchema?.readOnly"
      :precision="field.type === 'FloatPropertyData' ? undefined : 0"
      :min="['UInt32PropertyData', 'BytePropertyData'].includes(field.type) ? 0 : undefined"
      :max="field.type === 'BytePropertyData' ? 255 : undefined"
      style="width: 100%"
    />
    <n-input
      v-else
      :type="isTextarea ? 'textarea' : 'text'"
      :autosize="{ minRows: 2, maxRows: 12 }"
      :value="field.value == null ? '' : String(field.value)"
      :disabled="field.readOnly || fieldSchema?.readOnly"
      @update:value="field.value = $event"
    />

    <div v-if="targetMessage" class="ref-preview-container">
      <div v-if="!field.value || !String(field.value).trim()" class="ref-status-box ref-status-empty">
        <span class="ref-badge">{{ targetMessage }}</span>
        <span class="ref-text-empty">{{ tr('ui.referenceEmpty') }}</span>
      </div>
      <div v-else-if="previewState?.status === 'loading'" class="ref-status-box ref-status-loading">
        <span class="ref-badge">{{ targetMessage }}</span>
        <span>⏳ {{ tr('ui.referenceLoading') }}</span>
      </div>
      <div v-else-if="previewState?.status === 'found'" class="ref-status-box ref-status-found">
        <span class="ref-badge ref-badge-success">{{ targetMessage }}</span>
        <span class="ref-label">{{ tr('ui.referencePreview') }}:</span>
        <span class="ref-found-text">「{{ previewState.text }}」</span>
      </div>
      <div v-else class="ref-status-box ref-status-not-found">
        <span class="ref-badge ref-badge-error">{{ targetMessage }}</span>
        <span class="ref-not-found-text">⚠️ {{ tr('ui.referenceNotFound') }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ref-preview-container {
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.4;
}
.ref-status-box {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 8px;
  border-radius: 4px;
  border: 1px solid #e0e0e6;
  background-color: #fafafc;
  word-break: break-all;
}
.ref-badge {
  display: inline-block;
  font-size: 11px;
  padding: 1px 5px;
  border-radius: 3px;
  background: #eef0f3;
  color: #555;
  font-family: monospace;
}
.ref-badge-success {
  background: #e7f7ed;
  color: #18a058;
}
.ref-badge-error {
  background: #fdebee;
  color: #d03050;
}
.ref-text-empty {
  color: #999;
}
.ref-label {
  color: #666;
  font-weight: 500;
}
.ref-found-text {
  color: #18a058;
  font-weight: 600;
}
.ref-not-found-text {
  color: #d03050;
  font-weight: 500;
}
</style>
