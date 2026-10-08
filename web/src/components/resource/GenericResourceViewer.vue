<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import {
  NTree,
  NInput,
  NBreadcrumb,
  NBreadcrumbItem,
  NModal,
  NImage,
  NButton,
  NTag,
  type TreeOption
} from 'naive-ui'
import { AgGridVue } from 'ag-grid-vue3'
import {
  themeQuartz,
  type GridApi,
  type GridReadyEvent,
  type RowDoubleClickedEvent,
  type CellClickedEvent,
  type ColDef
} from 'ag-grid-community'
import { tr } from '../../i18n'

const props = defineProps<{
  data: unknown
  rawUrl?: string
  previewSrc?: string
  previewError?: string
}>()

const gridApi = ref<GridApi | null>(null)
const previewOpen = ref(false)
const selected = ref<unknown>()
const search = ref('')
const segments = ref<string[]>([])
const copiedPath = ref(false)

const rawMode = computed(
  () =>
    segments.value.length === 3 &&
    segments.value[0] === 'Exports' &&
    segments.value[2] === 'CustomSerialization'
)

const rawDownload = computed(() =>
  props.rawUrl
    ? props.rawUrl + '&exportIndex=' + encodeURIComponent(segments.value[1] ?? '')
    : ''
)

const location = computed(() => '/' + segments.value.map(encodeURIComponent).join('/'))
const displayLocation = computed(() => '/' + segments.value.join('/'))

const crumbs = computed(() => [
  { label: '根目录', depth: 0 },
  ...segments.value.map((label, index) => ({ label, depth: index + 1 }))
])

function navigate(parts: string[]) {
  let value: unknown = props.data
  for (const key of parts) {
    if (value === null || typeof value !== 'object') return
    value = (value as Record<string, unknown>)[key]
  }
  segments.value = parts
  selected.value = value
  search.value = ''
  nextTick(() => {
    gridApi.value?.sizeColumnsToFit()
  })
}

function goUp() {
  if (segments.value.length > 0) {
    navigate(segments.value.slice(0, -1))
  }
}

function copyCurrentPath() {
  navigator.clipboard?.writeText(displayLocation.value)
  copiedPath.value = true
  setTimeout(() => {
    copiedPath.value = false
  }, 1500)
}

function openRow(row: { name: string; item: unknown }) {
  if (row.item !== null && typeof row.item === 'object') {
    navigate([...segments.value, row.name])
  }
}

function type(value: unknown): string {
  return value === null
    ? 'null'
    : Array.isArray(value)
    ? `Array (${value.length})`
    : typeof value === 'object'
    ? 'Object'
    : typeof value
}

function fieldLabel(name: string) {
  return ['Parser', 'Tables'].includes(name) ? `[${name}]` : name
}

function summary(value: unknown): string {
  if (value === null) return 'null'
  if (Array.isArray(value)) return `[Array: ${value.length} 项]`
  if (typeof value === 'object') {
    const keys = Object.keys(value as Record<string, unknown>)
    return `{Object: ${keys.length} 属性}`
  }
  return String(value)
}

function escapeHtml(str: string): string {
  return str
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function children(value: unknown, path: string): TreeOption[] {
  if (value === null || typeof value !== 'object') return []
  return Object.entries(value).map(([key, item]) => ({
    key: path + '/' + encodeURIComponent(key),
    label:
      Array.isArray(value) && item && typeof item === 'object' && ((item as any).Name || (item as any).ObjectName)
        ? `${key} (${(item as any).Name || (item as any).ObjectName})`
        : fieldLabel(key),
    value: item,
    isLeaf:
      (path === '' && ['NameMap', 'Imports'].includes(key)) ||
      item === null ||
      typeof item !== 'object'
  }))
}

const tree = ref<TreeOption[]>([])
watch(
  () => props.data,
  value => {
    tree.value = children(value, '')
    selected.value = value
    segments.value = []
    search.value = ''
  },
  { immediate: true }
)

function load(node: TreeOption) {
  const items = children(node.value, String(node.key))
  node.children = items.length
    ? items
    : [{ key: String(node.key) + '/__empty__', label: tr('ui.viewerEmpty'), isLeaf: true, disabled: true }]
  return Promise.resolve()
}

function select(keys: (string | number)[], nodes: (TreeOption | null)[]) {
  if (!nodes[0]) return
  navigate(String(keys[0]).split('/').slice(1).map(decodeURIComponent))
}

const importMode = computed(() => segments.value.length === 1 && segments.value[0] === 'Imports')
const acbTableMode = computed(
  () =>
    segments.value.length === 5 &&
    segments.value[2] === 'CustomSerialization' &&
    segments.value[3] === 'Tables' &&
    Array.isArray(selected.value)
)
const acbKeys = computed(() =>
  acbTableMode.value
    ? [...new Set((selected.value as Record<string, unknown>[]).flatMap(r => Object.keys(r)))]
    : []
)

export interface ViewerRow {
  name: string
  type: string
  value: string
  item: unknown
  isNavigable: boolean
  [key: string]: unknown
}

const rows = computed<ViewerRow[]>(() => {
  if (acbTableMode.value) {
    return (selected.value as Record<string, unknown>[]).map((item, index) => ({
      ...item,
      name: String(index),
      type: 'Object',
      value: summary(item),
      item,
      isNavigable: false
    }))
  }

  if (importMode.value && Array.isArray(selected.value)) {
    return (selected.value as Record<string, unknown>[]).map((item, index) => ({
      ...item,
      name: String(index),
      type: 'Object',
      value: String(item.ObjectName ?? ''),
      item,
      isNavigable: false
    }))
  }

  const value = selected.value
  const entries: [string, unknown][] =
    value !== null && typeof value === 'object'
      ? Object.entries(value as Record<string, unknown>)
      : [['Value', value]]

  if (rawMode.value && props.rawUrl) {
    entries.push(['[RawData]', null])
  }

  return entries.map(([name, item]) => {
    const isNav = item !== null && typeof item === 'object'
    const typeStr = name === '[RawData]' && rawMode.value ? 'RawData' : type(item)
    const valStr =
      name === '[Reference]' && item && typeof item === 'object'
        ? `${(item as any).ObjectName} · ${(item as any).ContentFile || (item as any).Package || (item as any).Scope || ''}`
        : summary(item)
    return {
      name: String(name),
      type: typeStr,
      value: valStr,
      item,
      isNavigable: isNav
    }
  })
})

function onGridReady(params: GridReadyEvent) {
  gridApi.value = params.api
}

function onRowDoubleClicked(event: RowDoubleClickedEvent) {
  if (event.data?.isNavigable) {
    openRow(event.data)
  }
}

function onCellClicked(event: CellClickedEvent) {
  const target = event.event?.target as HTMLElement | null
  if (!target) return

  if (target.closest('[data-action="preview"]')) {
    previewOpen.value = true
    return
  }

  if (target.closest('[data-action="drill"]') || target.closest('.object-link')) {
    if (event.data?.isNavigable) {
      openRow(event.data)
    }
  }
}

const columns = computed<ColDef[]>(() => {
  if (acbTableMode.value) {
    return [
      {
        field: 'name',
        headerName: 'Index',
        width: 90,
        pinned: 'left',
        sortable: true
      },
      ...acbKeys.value.map(key => ({
        field: key,
        headerName: key,
        minWidth: 160,
        flex: 1,
        cellRenderer: (params: any) => {
          const val = params.value
          if (val == null) return '<span style="color:#aaa">—</span>'
          const str = typeof val === 'object' ? JSON.stringify(val) : String(val)
          return `<span title="${escapeHtml(str)}">${escapeHtml(str)}</span>`
        }
      }))
    ]
  }

  if (importMode.value) {
    return [
      { field: 'name', headerName: 'Index', width: 90, pinned: 'left', sortable: true },
      { field: 'ObjectName', headerName: 'Object Name', minWidth: 220, flex: 1.5 },
      { field: 'ClassPackage', headerName: 'Class Package', minWidth: 180, flex: 1 },
      { field: 'ClassName', headerName: 'Class Name', minWidth: 160, flex: 1 },
      { field: 'OuterName', headerName: 'Outer Name', width: 160 },
      { field: 'OuterIndex', headerName: 'Outer Index', width: 110 },
      { field: 'Optional', headerName: 'Optional', width: 100 }
    ]
  }

  // 默认普通对象/数组层级视图
  return [
    {
      field: 'name',
      headerName: tr('ui.viewerName'),
      minWidth: 200,
      flex: 1.2,
      cellRenderer: (params: any) => {
        const row = params.data
        if (!row) return ''
        const rawName = row.name
        const label = fieldLabel(rawName)
        const isSpecial = /^\\[.*\\]$/.test(label)

        if (row.isNavigable) {
          const isArr = Array.isArray(row.item)
          const icon = isArr ? '🗂️' : '📁'
          return `<div class="ag-name-cell ag-name-navigable" title="双击或点击展开此项">
            <span class="ag-folder-icon">${icon}</span>
            <span class="ag-name-text">${escapeHtml(label)}</span>
          </div>`
        }

        if (isSpecial) {
          return `<span class="ag-special-name">${escapeHtml(label)}</span>`
        }
        return `<span>${escapeHtml(label)}</span>`
      }
    },
    {
      field: 'type',
      headerName: tr('ui.viewerType'),
      width: 140,
      cellRenderer: (params: any) => {
        const t = String(params.value || '')
        let cls = 'badge-other'
        if (t.startsWith('Array')) cls = 'badge-array'
        else if (t === 'Object') cls = 'badge-object'
        else if (t === 'string') cls = 'badge-string'
        else if (t === 'number') cls = 'badge-number'
        else if (t === 'boolean') cls = 'badge-boolean'
        else if (t === 'null') cls = 'badge-null'
        else if (t === 'RawData') cls = 'badge-raw'
        return `<span class="type-badge ${cls}">${escapeHtml(t)}</span>`
      }
    },
    {
      field: 'value',
      headerName: tr('ui.viewerValue'),
      minWidth: 280,
      flex: 2,
      cellRenderer: (params: any) => {
        const row = params.data
        if (!row) return ''

        if (row.name === '[RawData]' && rawMode.value) {
          let html = '<div class="ag-raw-actions">'
          if (props.previewSrc || props.previewError) {
            html += `<button type="button" class="ag-btn-preview" data-action="preview">🔍 ${escapeHtml(
              tr('ui.rawPreview')
            )}</button>`
          }
          if (rawDownload.value) {
            html += `<a class="ag-btn-download" href="${rawDownload.value}" download>⬇️ ${escapeHtml(
              tr('ui.rawDownload')
            )}</a>`
          }
          html += '</div>'
          return html
        }

        if (row.isNavigable) {
          const summaryStr = escapeHtml(row.value)
          return `<button type="button" class="ag-drill-btn" data-action="drill" title="进入查看内部数据">
            <span>${summaryStr}</span>
            <span class="drill-arrow">➔</span>
          </button>`
        }

        const safeVal = escapeHtml(String(row.value ?? ''))
        return `<span class="ag-val-text" title="${safeVal}">${safeVal}</span>`
      }
    },
    {
      headerName: '',
      width: 80,
      pinned: 'right',
      sortable: false,
      filter: false,
      resizable: false,
      cellRenderer: (params: any) => {
        if (params.data?.isNavigable) {
          return `<button type="button" class="ag-quick-enter-btn" data-action="drill" title="查看内部属性">进入 ➔</button>`
        }
        return ''
      }
    }
  ]
})
</script>

<template>
  <n-modal
    v-model:show="previewOpen"
    preset="card"
    :title="tr('ui.rawPreview')"
    style="width: min(90vw, 1000px)"
  >
    <div v-if="previewSrc" class="raw-preview">
      <n-image
        :src="previewSrc"
        :img-props="{
          style:
            'max-width:100%;max-height:70vh;width:auto;height:auto;object-fit:contain;display:block'
        }"
      />
    </div>
    <div v-else>{{ previewError }}</div>
  </n-modal>

  <div class="generic-viewer">
    <!-- 左侧树状目录导航 -->
    <div class="generic-tree">
      <n-tree
        :data="tree"
        :on-load="load"
        expand-on-click
        :selected-keys="segments.length ? [location] : []"
        block-line
        @update:selected-keys="select"
      />
    </div>

    <!-- 右侧 AG Grid 详情区 -->
    <div class="generic-details">
      <!-- 顶部路径面包屑与快捷导航 -->
      <div class="viewer-toolbar">
        <div class="crumb-container">
          <n-button
            size="tiny"
            secondary
            :disabled="segments.length === 0"
            class="back-btn"
            @click="goUp"
          >
            ⇡ 上级
          </n-button>
          <n-breadcrumb class="breadcrumb-bar">
            <n-breadcrumb-item
              v-for="crumb in crumbs"
              :key="crumb.depth"
              @click="navigate(segments.slice(0, crumb.depth))"
            >
              {{ crumb.label }}
            </n-breadcrumb-item>
          </n-breadcrumb>
        </div>

        <div class="actions-right">
          <n-tag size="small" :bordered="false" class="count-tag">
            共 {{ rows.length }} 项
          </n-tag>
          <n-button size="tiny" quaternary @click="copyCurrentPath">
            {{ copiedPath ? '已复制路径 ✓' : '复制路径' }}
          </n-button>
        </div>
      </div>

      <!-- 搜索过滤 -->
      <div class="search-bar">
        <n-input
          v-model:value="search"
          :placeholder="tr('ui.searchFieldsRowKey')"
          clearable
          size="small"
        />
      </div>

      <!-- AG Grid 主格 -->
      <div class="grid-wrapper">
        <ag-grid-vue
          :theme="themeQuartz"
          class="generic-ag-grid"
          :row-data="rows"
          :column-defs="columns"
          :default-col-def="{
            sortable: true,
            filter: true,
            resizable: true
          }"
          :quick-filter-text="search"
          @grid-ready="onGridReady"
          @row-double-clicked="onRowDoubleClicked"
          @cell-clicked="onCellClicked"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.generic-viewer {
  display: flex;
  gap: 14px;
  height: calc(100vh - 220px);
  min-height: 360px;
}
.generic-tree {
  width: 280px;
  flex-shrink: 0;
  overflow: auto;
  border-right: 1px solid #e0e0e6;
  padding-right: 6px;
}
.generic-details {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.viewer-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
  gap: 8px;
  flex-wrap: wrap;
}
.crumb-container {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
  min-width: 0;
  overflow-x: auto;
}
.back-btn {
  font-weight: 500;
  flex-shrink: 0;
}
.breadcrumb-bar {
  white-space: nowrap;
}
.actions-right {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}
.count-tag {
  background-color: #f2f4f8;
  color: #666;
}
.search-bar {
  margin-bottom: 8px;
}
.grid-wrapper {
  flex: 1;
  min-height: 250px;
  width: 100%;
}
.generic-ag-grid {
  width: 100%;
  height: 100%;
}
.raw-preview {
  display: flex;
  justify-content: center;
  align-items: center;
  max-width: 100%;
  max-height: 70vh;
  overflow: hidden;
}

@media (max-width: 768px) {
  .generic-viewer {
    flex-direction: column;
    height: auto;
    min-height: calc(100vh - 200px);
  }
  .generic-tree {
    width: 100%;
    max-height: 180px;
    border-right: none;
    border-bottom: 1px solid #e0e0e6;
    padding-bottom: 6px;
  }
  .grid-wrapper {
    min-height: 400px;
  }
}
</style>

<style>
/* AG Grid 自定义单元格样式 */
.ag-name-cell {
  display: flex;
  align-items: center;
  gap: 6px;
  font-weight: 500;
}
.ag-name-navigable {
  color: #18a058;
  cursor: pointer;
}
.ag-name-navigable:hover .ag-name-text {
  text-decoration: underline;
}
.ag-folder-icon {
  font-size: 14px;
}
.ag-special-name {
  font-style: italic;
  color: #8c8c8c;
}
.type-badge {
  display: inline-block;
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 11px;
  font-family: monospace;
  font-weight: 500;
  line-height: 1.4;
}
.badge-array {
  background: #e6f7ff;
  color: #096dd9;
  border: 1px solid #91d5ff;
}
.badge-object {
  background: #f9f0ff;
  color: #722ed1;
  border: 1px solid #d3adf7;
}
.badge-string {
  background: #fafafc;
  color: #555;
  border: 1px solid #d9d9d9;
}
.badge-number {
  background: #f6ffed;
  color: #389e0d;
  border: 1px solid #b7eb8f;
}
.badge-boolean {
  background: #fff7e6;
  color: #d46b08;
  border: 1px solid #ffd591;
}
.badge-null {
  background: #f5f5f5;
  color: #8c8c8c;
  border: 1px solid #e8e8e8;
}
.badge-raw {
  background: #fff0f6;
  color: #c41d7f;
  border: 1px solid #ffadd2;
}
.badge-other {
  background: #fafafc;
  color: #666;
  border: 1px solid #e0e0e0;
}

.ag-drill-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: none;
  border: none;
  padding: 0;
  color: #18a058;
  cursor: pointer;
  font: inherit;
  font-weight: 500;
  text-align: left;
}
.ag-drill-btn:hover {
  text-decoration: underline;
  color: #36ad6a;
}
.drill-arrow {
  font-size: 12px;
  opacity: 0.8;
  transition: transform 0.15s ease;
}
.ag-drill-btn:hover .drill-arrow {
  transform: translateX(2px);
}
.ag-quick-enter-btn {
  background: #e7f7ed;
  color: #18a058;
  border: 1px solid #b7eb8f;
  border-radius: 4px;
  padding: 1px 6px;
  font-size: 11px;
  cursor: pointer;
  transition: all 0.2s ease;
}
.ag-quick-enter-btn:hover {
  background: #18a058;
  color: #fff;
}
.ag-val-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ag-raw-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}
.ag-btn-preview {
  background: #2080f0;
  color: #fff;
  border: none;
  border-radius: 4px;
  padding: 2px 8px;
  font-size: 11px;
  cursor: pointer;
}
.ag-btn-preview:hover {
  background: #4098fc;
}
.ag-btn-download {
  color: #2080f0;
  text-decoration: underline;
  font-size: 12px;
  cursor: pointer;
}
</style>
