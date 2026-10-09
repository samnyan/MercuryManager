<script setup lang="ts">
import { ref, computed, watch, nextTick, h } from 'vue'
import {
  NTree,
  NInput,
  NBreadcrumb,
  NBreadcrumbItem,
  NModal,
  NImage,
  NButton,
  NTag,
  NCheckbox,
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
import VoiceMatchesPopover from './VoiceMatchesPopover.vue'

const props = defineProps<{
  data: unknown
  path?: string
  previewSrc?: string
  previewError?: string
  rawUrl?: string
}>()

const gridApi = ref<GridApi | null>(null)
const previewOpen = ref(false)
const selected = ref<unknown>()
const search = ref('')
const segments = ref<string[]>([])
const copiedPath = ref(false)
const asTable = ref(true)

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

const isVoiceDirectory = computed(() => {
  const p = props.path || ''
  return p.startsWith('Sound/Voice/') || p.startsWith('Sound/Voice\\')
})

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
    gridApi.value?.resetColumnState()
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

function isVirtualField(name: string) {
  const lbl = fieldLabel(name)
  return lbl.startsWith('[') && lbl.endsWith(']')
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

function formatHoverPreview(val: unknown): string {
  if (val === null || val === undefined) return String(val)
  if (typeof val === 'object') {
    try {
      const json = JSON.stringify(val, null, 2)
      if (json.length > 2000) {
        return json.slice(0, 2000) + '\n... (truncated)'
      }
      return json
    } catch {
      return String(val)
    }
  }
  return String(val)
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

// 判断当前是否可以以二维表格形式呈现（即当前为包含对象的 Array）
const canTableView = computed(() => {
  if (!Array.isArray(selected.value) || selected.value.length === 0) return false
  return selected.value.some(item => item !== null && typeof item === 'object')
})

const isTableDisplayActive = computed(() => {
  return (canTableView.value && asTable.value) || acbTableMode.value
})

// 对象数组表格模式下的所有动态列名
const tableColumnsKeys = computed(() => {
  if (!isTableDisplayActive.value || !Array.isArray(selected.value)) return []
  const keySet = new Set<string>()
  for (const item of selected.value as unknown[]) {
    if (item !== null && typeof item === 'object') {
      for (const k of Object.keys(item as Record<string, unknown>)) {
        keySet.add(k)
      }
    }
  }
  return Array.from(keySet)
})

export interface ViewerRow {
  name: string
  _index?: number
  type: string
  value: string
  item: unknown
  isNavigable: boolean
  [key: string]: unknown
}

const rows = computed<ViewerRow[]>(() => {
  // 表格展示模式（对象数组或 ACB Tables）
  if (isTableDisplayActive.value && Array.isArray(selected.value)) {
    return (selected.value as unknown[]).map((item, index) => {
      const isNav = item !== null && typeof item === 'object'
      const rowObj: any = {
        name: String(index),
        _index: index,
        type: type(item),
        value: summary(item),
        item,
        isNavigable: isNav
      }
      if (item !== null && typeof item === 'object') {
        for (const k of tableColumnsKeys.value) {
          rowObj[k] = (item as any)[k]
        }
      } else {
        rowObj['Value'] = item
      }
      return rowObj
    })
  }

  // 依赖导入表模式
  if (importMode.value && Array.isArray(selected.value)) {
    return (selected.value as Record<string, unknown>[]).map((item, index) => ({
      ...item,
      name: String(index),
      _index: index,
      type: 'Object',
      value: String(item.ObjectName ?? ''),
      item,
      isNavigable: false
    }))
  }

  // 默认普通键值对列表模式
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

  if (target.closest('[data-action="drill"]') || target.closest('.object-link') || target.closest('.ag-index-nav')) {
    if (event.data?.isNavigable) {
      openRow(event.data)
    }
  }
}

const columns = computed<ColDef[]>(() => {
  // 1. 表格形式展示（对象数组 / ACB Table）
  if (isTableDisplayActive.value) {
    return [
      {
        colId: 'col_index',
        field: 'name',
        headerName: 'Index',
        width: 85,
        pinned: 'left',
        sortable: true,
        cellRenderer: (params: any) => {
          const row = params.data
          if (row?.isNavigable) {
            return `<span class="ag-index-nav" data-action="drill" title="点击或双击进入此项详情">${escapeHtml(
              String(params.value ?? '')
            )}</span>`
          }
          return `<span>${escapeHtml(String(params.value ?? ''))}</span>`
        }
      },
      ...tableColumnsKeys.value.map(key => ({
        field: key,
        headerName: key,
        minWidth: 150,
        flex: 1,
        tooltipValueGetter: (params: any) => {
          return formatHoverPreview(params.value)
        },
        cellRenderer: (params: any) => {
          const val = params.value
          if (val === undefined) return '<span style="color:#ccc">—</span>'
          if (val === null) return '<span style="color:#999;font-style:italic">null</span>'
          const preview = formatHoverPreview(val)
          if (typeof val === 'object') {
            const isArr = Array.isArray(val)
            const icon = isArr ? '🗂️' : '📁'
            const text = summary(val)
            return `<span class="ag-cell-obj" title="${escapeHtml(preview)}">${icon} ${escapeHtml(
              text
            )}</span>`
          }
          const str = String(val)
          return `<span class="ag-val-text" title="${escapeHtml(preview)}">${escapeHtml(str)}</span>`
        }
      }))
    ]
  }

  // 2. 依赖导入列表模式
  if (importMode.value) {
    return [
      { colId: 'import_index', field: 'name', headerName: 'Index', width: 85, pinned: 'left', sortable: true },
      { field: 'ObjectName', headerName: 'Object Name', minWidth: 220, flex: 1.5 },
      { field: 'ClassPackage', headerName: 'Class Package', minWidth: 180, flex: 1 },
      { field: 'ClassName', headerName: 'Class Name', minWidth: 160, flex: 1 },
      { field: 'OuterName', headerName: 'Outer Name', width: 160 },
      { field: 'OuterIndex', headerName: 'Outer Index', width: 110 },
      { field: 'Optional', headerName: 'Optional', width: 100 }
    ]
  }

  // 3. 默认键值对属性视图
  return [
    {
      colId: 'prop_name',
      field: 'name',
      headerName: tr('ui.viewerName'),
      minWidth: 320,
      flex: 3,
      cellRenderer: (params: any) => {
        const row = params.data
        if (!row) return ''
        const rawName = row.name
        const label = fieldLabel(rawName)
        const isVirtual = isVirtualField(rawName)

        if (row.isNavigable) {
          const isArr = Array.isArray(row.item)
          const icon = isArr ? '🗂️' : '📁'

          if (isArr && typeof rawName === 'string' && isVoiceDirectory.value && /^MER_VO_\w+$/i.test(rawName)) {
            return h(VoiceMatchesPopover, { cueName: rawName })
          }

          return `<div class="ag-name-cell ag-name-navigable ${isVirtual ? 'ag-virtual-name' : ''}" title="点击或双击进入查看">
            <span class="ag-folder-icon">${icon}</span>
            <span class="ag-name-text">${escapeHtml(label)}</span>
          </div>`
        }

        if (typeof rawName === 'string' && isVoiceDirectory.value && /^MER_VO_\w+$/i.test(rawName)) {
          return h(VoiceMatchesPopover, { cueName: rawName })
        }

        if (isVirtual) {
          return `<span class="ag-virtual-name">${escapeHtml(label)}</span>`
        }
        return `<span>${escapeHtml(label)}</span>`
      }
    },
    {
      colId: 'prop_type',
      field: 'type',
      headerName: tr('ui.viewerType'),
      width: 145,
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
      colId: 'prop_value',
      field: 'value',
      headerName: tr('ui.viewerValue'),
      minWidth: 200,
      flex: 2,
      tooltipValueGetter: (params: any) => {
        const row = params.data
        if (row?.item !== undefined) {
          return formatHoverPreview(row.item)
        }
        return String(params.value ?? '')
      },
      cellRenderer: (params: any) => {
        const row = params.data
        if (!row) return ''
        const isVirtual = isVirtualField(row.name)

        if (row.name === '[RawData]' && rawMode.value) {
          let html = '<div class="ag-raw-actions ag-virtual-val">'
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
          const hoverText = escapeHtml(formatHoverPreview(row.item))
          return `<button type="button" class="ag-drill-btn ${isVirtual ? 'ag-virtual-val' : ''}" data-action="drill" title="${hoverText}">
            <span>${summaryStr}</span>
            <span class="drill-arrow">➔</span>
          </button>`
        }

        const safeVal = escapeHtml(String(row.value ?? ''))
        const hoverText = escapeHtml(formatHoverPreview(row.item ?? row.value))

        if (typeof row.value === 'string' && isVoiceDirectory.value && /^MER_VO_\w+$/i.test(row.value)) {
          return h(VoiceMatchesPopover, { cueName: row.value })
        }

        return `<span class="ag-val-text ${isVirtual ? 'ag-virtual-val' : ''}" title="${hoverText}">${safeVal}</span>`
      }
    }
  ]
})

watch(columns, () => {
  nextTick(() => {
    gridApi.value?.resetColumnState()
    gridApi.value?.sizeColumnsToFit()
  })
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
    <div class="generic-details view-fill-container">
      <!-- 顶部路径面包屑与快捷导航 -->
      <div class="viewer-toolbar view-header-fixed">
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
          <!-- 动态表格展示开关 -->
          <n-checkbox
            v-if="canTableView"
            v-model:checked="asTable"
            size="small"
            class="table-toggle-check"
          >
            表格形式展示
          </n-checkbox>

          <n-tag size="small" :bordered="false" class="count-tag">
            共 {{ rows.length }} 项
          </n-tag>
          <n-button size="tiny" quaternary @click="copyCurrentPath">
            {{ copiedPath ? '已复制路径 ✓' : '复制路径' }}
          </n-button>
        </div>
      </div>

      <!-- 搜索过滤 -->
      <div class="search-bar view-header-fixed">
        <n-input
          v-model:value="search"
          :placeholder="tr('ui.searchFieldsRowKey')"
          clearable
          size="small"
        />
      </div>

      <!-- AG Grid 主格 -->
      <div class="grid-wrapper view-grid-fill">
        <ag-grid-vue
          :theme="themeQuartz"
          class="generic-ag-grid ag-fill-grid"
          :row-data="rows"
          :column-defs="columns"
          :maintain-column-order="false"
          :default-col-def="{
            sortable: true,
            filter: true,
            resizable: true
          }"
          :quick-filter-text="search"
          :enable-browser-tooltips="true"
          :enable-cell-text-selection="true"
          :ensure-dom-order="true"
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
  flex-direction: row;
  gap: 14px;
  height: 100%;
  min-height: 0;
  flex: 1;
  overflow: hidden;
}
.generic-tree {
  width: 280px;
  flex-shrink: 0;
  height: 100%;
  overflow-y: auto;
  border-right: 1px solid #e0e0e6;
  padding-right: 6px;
}
.generic-details {
  flex: 1;
  min-width: 0;
  height: 100%;
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
  flex-shrink: 0;
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
  gap: 10px;
  flex-shrink: 0;
}
.table-toggle-check {
  font-size: 13px;
  color: #333;
}
.count-tag {
  background-color: #f2f4f8;
  color: #666;
}
.search-bar {
  margin-bottom: 8px;
  flex-shrink: 0;
}
.grid-wrapper {
  flex: 1;
  min-height: 0;
  width: 100%;
  height: 100%;
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
    height: 100%;
    min-height: 0;
    gap: 8px;
  }
  .generic-tree {
    width: 100%;
    height: auto;
    max-height: 160px;
    flex-shrink: 0;
    border-right: none;
    border-bottom: 1px solid #e0e0e6;
    padding-bottom: 6px;
  }
  .grid-wrapper {
    flex: 1;
    min-height: 0;
    height: 100%;
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
.ag-virtual-name {
  font-style: italic !important;
  color: #7f8c8d;
}
.ag-virtual-val {
  font-style: italic !important;
}
.ag-index-nav {
  color: #18a058;
  font-weight: 600;
  cursor: pointer;
}
.ag-index-nav:hover {
  text-decoration: underline;
}
.ag-cell-obj {
  color: #2080f0;
  font-weight: 500;
  cursor: pointer;
}
.ag-cell-obj:hover {
  text-decoration: underline;
}
.type-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 12.5px;
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
