<script setup lang="ts">
import TexturePreview from '../resource/TexturePreview.vue'
import ArrayEditor from './ArrayEditor.vue'
import textureBindings from '../../textureBindings.json'
import { computed, watch } from 'vue'
import { NCheckbox, NInput, NInputNumber, NSelect } from 'naive-ui'
import type { Field } from '../../project'
import { useProject } from '../../project'
import { tr } from '../../i18n'
import { useReferenceStore } from '../../referenceStore'
import type { TableFieldSchema } from '../../tableSchema'
import enumsData from '../../enums.json'

const imageBindings = textureBindings as Record<string, Record<string, string>>

const props = defineProps<{
  field: Field
  table?: string
  fieldSchema?: TableFieldSchema
}>()

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
  return (
    typeof props.field.value === 'number' ||
    (props.fieldSchema?.type === 'number' && typeof props.field.value !== 'string')
  )
})

// 枚举判断
const isEnum = computed(() => {
  if (props.fieldSchema?.type === 'enum' || props.field.type === 'EnumPropertyData') return true
  if (props.fieldSchema?.enumName) return true
  if (
    typeof props.field.value === 'string' &&
    props.field.value.includes('::') &&
    props.field.value.startsWith('E')
  )
    return true
  return false
})

const enumName = computed(() => {
  if (props.fieldSchema?.enumName) return props.fieldSchema.enumName
  if (typeof props.field.value === 'string' && props.field.value.includes('::')) {
    return props.field.value.split('::')[0]
  }
  return null
})

const enumDef = computed(() => {
  if (!enumName.value) return null
  return (
    (enumsData as Record<
      string,
      { name: string; values: Array<{ name: string; value: number; full: string }> }
    >)[enumName.value] ?? null
  )
})

const enumOptions = computed(() => {
  const currentVal = props.field.value == null ? '' : String(props.field.value)
  const list: Array<{ label: string; value: string }> = []
  const seen = new Set<string>()

  if (enumDef.value?.values) {
    for (const item of enumDef.value.values) {
      list.push({
        label: `${item.name} (${item.full})`,
        value: item.full
      })
      seen.add(item.full)
    }
  }

  // 保证当前值即使不在预设里也能正确显示与选中
  if (currentVal && !seen.has(currentVal)) {
    list.unshift({
      label: `${currentVal} (Custom)`,
      value: currentVal
    })
  }

  return list
})

// 结构化 Array 判断
const isStructuredArray = computed(() => {
  if (props.fieldSchema?.type !== 'array' && props.field.type !== 'ArrayPropertyData') return false
  const itemType = props.fieldSchema?.itemType
  return itemType === 'number' || itemType === 'string' || itemType === 'boolean'
})

const isTextarea = computed(() => {
  if (['ArrayPropertyData', 'MapPropertyData', 'StrPropertyData'].includes(props.field.type)) return true
  return props.fieldSchema?.type === 'array' || props.fieldSchema?.type === 'map'
})
</script>

<template>
  <div style="width: 100%">
    <!-- 布尔类型 -->
    <n-checkbox
      v-if="isBoolean"
      v-model:checked="field.value as boolean"
      :disabled="field.readOnly || fieldSchema?.readOnly"
    />

    <!-- 数值类型 -->
    <n-input-number
      v-else-if="isNumber"
      v-model:value="field.value as number"
      :disabled="field.readOnly || fieldSchema?.readOnly"
      :precision="field.type === 'FloatPropertyData' ? undefined : 0"
      :min="['UInt32PropertyData', 'BytePropertyData'].includes(field.type) ? 0 : undefined"
      :max="field.type === 'BytePropertyData' ? 255 : undefined"
      style="width: 100%"
    />

    <!-- 枚举下拉选择（支持 tag 自定义输入） -->
    <div v-else-if="isEnum">
      <n-select
        :value="field.value == null ? null : String(field.value)"
        :options="enumOptions"
        filterable
        tag
        :placeholder="enumName ? `选择或输入 ${enumName}` : '选择枚举值'"
        :disabled="field.readOnly || fieldSchema?.readOnly"
        @update:value="field.value = $event"
      />
      <div v-if="enumName" class="enum-hint">
        关联枚举: <code>{{ enumName }}</code>
      </div>
    </div>

    <!-- 一维简单类型数组（支持 +- 列表项编辑与类型保护） -->
    <array-editor
      v-else-if="isStructuredArray && fieldSchema?.itemType"
      :value="field.value"
      :item-type="fieldSchema.itemType"
      :disabled="field.readOnly || fieldSchema?.readOnly"
      @update:value="field.value = $event"
    />

    <!-- 复杂嵌套结构数组/Map/多行字符串等 -->
    <n-input
      v-else
      :type="isTextarea ? 'textarea' : 'text'"
      :autosize="{ minRows: 2, maxRows: 12 }"
      :value="field.value == null ? '' : String(field.value)"
      :disabled="field.readOnly || fieldSchema?.readOnly"
      @update:value="field.value = $event"
    />

    <texture-preview
      v-if="table ? imageBindings[table]?.[field.name] !== undefined : field.name === 'JacketAssetName'"
      :field="field.name"
      :table="table ?? 'MusicParameterTable'"
      :value="field.value"
    />

    <!-- 关联 Message 预览面板 -->
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
.enum-hint {
  margin-top: 4px;
  font-size: 11px;
  color: #888;
}
.enum-hint code {
  color: #2080f0;
  background: #f0f4fa;
  padding: 1px 4px;
  border-radius: 3px;
}
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
