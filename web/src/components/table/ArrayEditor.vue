<script setup lang="ts">
import { ref, watch } from 'vue'
import { NButton, NInput, NInputNumber, NCheckbox } from 'naive-ui'

const props = defineProps<{
  value: string | number | boolean | unknown[] | null
  itemType: 'string' | 'number' | 'boolean' | 'object'
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:value': [val: string]
}>()

const rawMode = ref(false)
const rawJson = ref('')
const items = ref<any[]>([])

function parseValue(val: unknown): any[] {
  if (Array.isArray(val)) return [...val]
  if (typeof val === 'string') {
    try {
      const parsed = JSON.parse(val)
      if (Array.isArray(parsed)) return parsed
    } catch {
      // ignore
    }
  }
  return []
}

function syncFromProps() {
  const arr = parseValue(props.value)
  items.value = arr
  rawJson.value = JSON.stringify(arr, null, 2)
}

watch(() => props.value, syncFromProps, { immediate: true })

function emitChange(newItems: any[]) {
  items.value = newItems
  const jsonStr = JSON.stringify(newItems)
  rawJson.value = JSON.stringify(newItems, null, 2)
  emit('update:value', jsonStr)
}

function onRawInput(val: string) {
  rawJson.value = val
  try {
    const parsed = JSON.parse(val)
    if (Array.isArray(parsed)) {
      items.value = parsed
      emit('update:value', JSON.stringify(parsed))
    }
  } catch {
    emit('update:value', val)
  }
}

function addItem() {
  const defVal = props.itemType === 'number' ? 0 : props.itemType === 'boolean' ? false : ''
  const updated = [...items.value, defVal]
  emitChange(updated)
}

function removeItem(index: number) {
  const updated = items.value.filter((_, i) => i !== index)
  emitChange(updated)
}

function updateItem(index: number, val: any) {
  const updated = [...items.value]
  updated[index] = val
  emitChange(updated)
}
</script>

<template>
  <div class="array-editor">
    <div class="array-header">
      <span class="array-badge">{{ itemType }}[] ({{ items.length }})</span>
      <n-button size="tiny" quaternary @click="rawMode = !rawMode">
        {{ rawMode ? '切换为列表编辑' : 'JSON 源码编辑' }}
      </n-button>
    </div>

    <!-- 原始源码模式 -->
    <n-input
      v-if="rawMode"
      type="textarea"
      :autosize="{ minRows: 2, maxRows: 8 }"
      :value="rawJson"
      :disabled="disabled"
      @update:value="onRawInput"
    />

    <!-- 结构化列表模式 -->
    <div v-else class="array-items-list">
      <div v-if="items.length === 0" class="array-empty">
        暂无项目，点击下方添加
      </div>
      <div
        v-for="(item, idx) in items"
        :key="idx"
        class="array-row"
      >
        <span class="array-index">#{{ idx }}</span>

        <div class="array-control">
          <!-- 布尔类型 -->
          <n-checkbox
            v-if="itemType === 'boolean'"
            :checked="Boolean(item)"
            :disabled="disabled"
            @update:checked="updateItem(idx, $event)"
          >
            {{ Boolean(item) ? 'True' : 'False' }}
          </n-checkbox>

          <!-- 数值类型 -->
          <n-input-number
            v-else-if="itemType === 'number'"
            :value="Number(item)"
            :disabled="disabled"
            style="width: 100%"
            @update:value="updateItem(idx, $event ?? 0)"
          />

          <!-- 字符串类型 -->
          <n-input
            v-else
            :value="String(item ?? '')"
            :disabled="disabled"
            style="width: 100%"
            @update:value="updateItem(idx, $event)"
          />
        </div>

        <n-button
          size="tiny"
          type="error"
          quaternary
          :disabled="disabled"
          @click="removeItem(idx)"
        >
          ✕
        </n-button>
      </div>

      <div class="array-footer">
        <n-button size="small" dashed block :disabled="disabled" @click="addItem">
          + 添加项
        </n-button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.array-editor {
  width: 100%;
  border: 1px solid #e0e0e6;
  border-radius: 4px;
  padding: 8px;
  background-color: #fafafc;
}
.array-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
}
.array-badge {
  font-size: 11px;
  color: #666;
  font-family: monospace;
  background: #eee;
  padding: 1px 6px;
  border-radius: 3px;
}
.array-items-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.array-row {
  display: flex;
  align-items: center;
  gap: 8px;
  background: #fff;
  padding: 4px 6px;
  border-radius: 4px;
  border: 1px solid #efeff5;
}
.array-index {
  font-size: 11px;
  color: #999;
  font-family: monospace;
  min-width: 22px;
}
.array-control {
  flex: 1;
}
.array-empty {
  font-size: 12px;
  color: #999;
  text-align: center;
  padding: 10px 0;
}
.array-footer {
  margin-top: 4px;
}
</style>
