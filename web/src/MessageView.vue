<script setup lang="ts">
import { tr, i18n } from './i18n'
import { ref, computed, watch, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import {
  NButton,
  NInput,
  NModal,
  NCard,
  NFormItem,
  NSpace,
  NCheckbox,
  useMessage
} from 'naive-ui'
import AssetGrid from './AssetGrid.vue'
import AssetForm from './AssetForm.vue'
import { api, useProject, type Row, type Field } from './project'
import { useReferenceStore } from './referenceStore'
import { getTableSchema } from './tableSchema'
import { useResponsive } from './composables/useResponsive'

const project = useProject()
const route = useRoute()
const message = useMessage()
const refStore = useReferenceStore()
const { isMobile } = useResponsive()

const rows = ref<Row[]>([])
const fields = ref<Field[]>([])
const rowName = ref('')
const editor = ref(false)
const adding = ref(false)
const busy = ref(false)
const search = ref('')

const isTable = computed(() => route.path.startsWith('/table/'))
const tableName = computed(() => (isTable.value ? String(route.params.name ?? '') : ''))
const currentSchema = computed(() =>
  isTable.value && tableName.value ? getTableSchema(tableName.value) : undefined
)

const title = computed(() => {
  const raw = String(route.params.name ?? '')
  if (currentSchema.value) {
    return i18n.global.locale.value === 'zh'
      ? currentSchema.value.nameCn
      : currentSchema.value.nameEn || raw
  }
  const key = (isTable.value ? 'tables.' : 'messages.') + raw
  return i18n.global.te(key) ? tr(key) : raw
})

const heading = computed(() => {
  const raw = String(route.params.name ?? '')
  return title.value === raw ? raw : `${title.value} / ${raw}`
})

const name = computed(() => (isTable.value ? 'Table__' : '') + String(route.params.name ?? ''))

async function preloadReferences() {
  if (!isTable.value || !project.id || !refStore.resolveEnabled) return
  const needed = new Set<string>()
  if (currentSchema.value) {
    for (const f of currentSchema.value.fields) {
      if (f.messageLink?.messageTable) {
        needed.add(f.messageLink.messageTable)
      }
    }
  }
  for (const msg of needed) {
    refStore.loadMessage(project.id, msg)
  }
}

async function load() {
  rows.value = []
  if (!project.id || !project.contentRoot || !name.value) return
  try {
    rows.value = await api<Row[]>(
      `/workspaces/${project.id}/messages/${encodeURIComponent(name.value)}`
    )
    preloadReferences()
  } catch (e) {
    message.error(String(e))
  }
}

watch(() => [project.id, name.value], load, { immediate: true })
watch(
  () => refStore.resolveEnabled,
  enabled => {
    if (enabled) preloadReferences()
  }
)

function select(key: string) {
  const row = rows.value.find(r => r.rowName === key)!
  adding.value = false
  rowName.value = key
  fields.value = row.fields.map(f => ({
    ...f,
    value: f.type === 'MapPropertyData' ? JSON.stringify(f.value) : JSON.parse(JSON.stringify(f.value))
  }))
  editor.value = true
}

function add() {
  if (!rows.value.length) return
  adding.value = true
  rowName.value = ''
  fields.value = rows.value[0]!.fields.map(f => ({
    ...f,
    value: f.type === 'ArrayPropertyData' ? [] : f.type === 'MapPropertyData'
      ? '[]'
      : f.type === 'Int64PropertyData' || f.type === 'UInt64PropertyData'
      ? '0'
      : f.type === 'EnumPropertyData'
      ? f.value
      : f.type === 'BoolPropertyData'
      ? false
      : typeof f.value === 'number'
      ? 0
      : f.value === null
      ? null
      : ''
  }))
  editor.value = true
}

async function save() {
  busy.value = true
  try {
    const values = Object.fromEntries(
      fields.value.map(f => [
        f.name,
        f.type === 'MapPropertyData'
          ? JSON.parse(String(f.value))
          : f.value
      ])
    )
    await api(
      `/workspaces/${project.id}/messages/${encodeURIComponent(name.value)}/rows`,
      adding.value ? 'POST' : 'PATCH',
      { rowName: rowName.value, fields: values }
    )
    await load()
    await project.status()
    editor.value = false
    message.success(tr('ui.tableDraftUpdated'))
  } catch (e) {
    message.error(String(e))
  } finally {
    busy.value = false
  }
}

onBeforeUnmount(() => {
  project.pending = false
})

watch(
  rowName,
  () => {
    if (editor.value && adding.value) project.pending = true
  },
  { flush: 'sync' }
)

const originalForm = ref('')
watch(
  editor,
  value => {
    if (value) originalForm.value = JSON.stringify(fields.value)
    else project.pending = false
  },
  { flush: 'sync' }
)

watch(
  fields,
  () => {
    project.pending = editor.value && JSON.stringify(fields.value) !== originalForm.value
  },
  { deep: true, flush: 'sync' }
)
</script>

<template>
  <div class="view-container view-fill-container">
    <!-- 顶部标题与响应式工具/搜索区 -->
    <div class="view-header view-header-fixed">
      <h2 class="page-title">{{ heading }}</h2>

      <div class="toolbar-wrapper">
        <div class="toolbar-actions">
          <n-button
            type="primary"
            size="small"
            :disabled="!rows.length"
            @click="add"
          >
            {{ tr('ui.addRow') }}
          </n-button>

          <n-checkbox
            v-if="isTable"
            v-model:checked="refStore.resolveEnabled"
            class="ref-checkbox"
          >
            {{ tr('ui.resolveReferences') }}
          </n-checkbox>
        </div>

        <div class="toolbar-search">
          <n-input
            v-model:value="search"
            :placeholder="tr('ui.searchFieldsRowKey')"
            clearable
            size="small"
          />
        </div>
      </div>
    </div>

    <!-- 表格区域 -->
    <div class="grid-wrapper view-grid-fill">
      <asset-grid
        :rows="rows"
        :search="search"
        :schema="currentSchema"
        @select="select"
      />
    </div>

    <!-- 响应式表单编辑 Dialog -->
    <n-modal
      v-model:show="editor"
      :mask-closable="false"
    >
      <n-card
        class="editor-dialog-card"
        :title="heading + ' · ' + (adding ? tr('ui.addRow') : rowName)"
        closable
        @close="editor = false"
      >
        <div class="editor-scroll-area">
          <n-form-item :label="tr('ui.rowKey') + ' / RowName'" style="margin-bottom: 16px">
            <n-input
              v-model:value="rowName"
              :disabled="!adding"
              placeholder="RowName"
            />
          </n-form-item>

          <asset-form :fields="fields" :schema="currentSchema" />
        </div>

        <template #footer>
          <div class="dialog-footer">
            <n-button :disabled="busy" @click="editor = false">
              {{ tr('ui.cancel') }}
            </n-button>
            <n-button
              type="primary"
              :loading="busy"
              @click="save"
            >
              {{ tr('ui.applyToDraft') }}
            </n-button>
          </div>
        </template>
      </n-card>
    </n-modal>
  </div>
</template>

<style scoped>
.view-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  flex: 1;
}
.view-header {
  flex-shrink: 0;
  margin-bottom: 10px;
}
.toolbar-wrapper {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.toolbar-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}
.toolbar-search {
  flex: 1;
  max-width: 320px;
  min-width: 180px;
}
.ref-checkbox {
  font-size: 13px;
  user-select: none;
}
.grid-wrapper {
  flex: 1;
  min-height: 0;
}

/* 响应式适配 */
@media (max-width: 768px) {
  .toolbar-wrapper {
    flex-direction: column;
    align-items: stretch;
    gap: 8px;
  }
  .toolbar-actions {
    justify-content: space-between;
    width: 100%;
  }
  .toolbar-search {
    max-width: 100%;
    width: 100%;
  }
}

/* 表单 Dialog 样式 */
:deep(.editor-dialog-card) {
  width: min(920px, 95vw);
  max-height: 90vh;
  display: flex;
  flex-direction: column;
}
:deep(.editor-dialog-card > .n-card__content) {
  flex: 1;
  overflow: hidden;
  padding: 16px 20px;
}
.editor-scroll-area {
  max-height: calc(90vh - 160px);
  overflow-y: auto;
  padding-right: 4px;
}
.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}
@media (max-width: 768px) {
  :deep(.editor-dialog-card) {
    width: 98vw;
    max-height: 94vh;
  }
  .editor-scroll-area {
    max-height: calc(94vh - 150px);
  }
}
</style>
