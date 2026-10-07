<script setup lang="ts">
import { ref, computed, watch, h } from 'vue'
import { NTree, NDataTable, NInput, type TreeOption } from 'naive-ui'
import { tr } from '../../i18n'

const props = defineProps<{ data: unknown }>()
const selected = ref<unknown>()
const search = ref('')
const location = ref('/')

function type(value: unknown): string {
  return value === null
    ? 'null'
    : Array.isArray(value)
    ? `Array (${value.length})`
    : typeof value === 'object'
    ? 'Object'
    : typeof value
}

function summary(value: unknown): string {
  return value !== null && typeof value === 'object' ? type(value) : String(value ?? 'null')
}

function children(value: unknown, path: string): TreeOption[] {
  if (value === null || typeof value !== 'object') return []
  return Object.entries(value).map(([key, item]) => ({
    key: path + '/' + encodeURIComponent(key),
    label: key,
    value: item,
    isLeaf: item === null || typeof item !== 'object'
  }))
}

const tree = computed(() => children(props.data, ''))

function load(node: TreeOption) {
  node.children = children(node.value, String(node.key))
  return Promise.resolve()
}

function select(keys: (string | number)[], nodes: (TreeOption | null)[]) {
  if (!nodes[0]) return
  selected.value = nodes[0].value
  location.value = String(keys[0])
}

watch(
  () => props.data,
  value => {
    selected.value = value
    location.value = '/'
    search.value = ''
  },
  { immediate: true }
)

const rows = computed(() => {
  const value = selected.value
  const entries =
    value !== null && typeof value === 'object' ? Object.entries(value) : [['Value', value]]
  return entries
    .map(([name, item]) => ({
      name,
      type: type(item),
      value: summary(item),
      item
    }))
    .filter(r => `${r.name} ${r.type} ${r.value}`.toLowerCase().includes(search.value.toLowerCase()))
})

const columns = computed(() => [
  { title: tr('ui.viewerName'), key: 'name', width: 230 },
  { title: tr('ui.viewerType'), key: 'type', width: 150 },
  {
    title: tr('ui.viewerValue'),
    key: 'value',
    render: (row: { value: string }) =>
      h('span', { style: 'white-space:pre-wrap;overflow-wrap:anywhere' }, row.value)
  }
])
</script>

<template>
  <div class="generic-viewer">
    <div class="generic-tree">
      <n-tree
        :data="tree"
        :on-load="load"
        block-line
        @update:selected-keys="select"
      />
    </div>
    <div class="generic-details">
      <div style="overflow-wrap: anywhere; margin-bottom: 8px; font-weight: 500">
        {{ location }}
      </div>
      <n-input
        v-model:value="search"
        :placeholder="tr('ui.searchFieldsRowKey')"
        clearable
        size="small"
      />
      <n-data-table
        :columns="columns"
        :data="rows"
        :pagination="{ pageSize: 50 }"
        :row-key="(r: any) => r.name"
        :row-props="(r: any) => ({
          onDblclick: () => {
            if (r.item !== null && typeof r.item === 'object') {
              selected = r.item
              location += '/' + r.name
            }
          }
        })"
        :scroll-x="650"
        style="margin-top: 8px"
      />
    </div>
  </div>
</template>

<style scoped>
.generic-viewer {
  display: flex;
  gap: 12px;
  height: calc(100vh - 220px);
  min-height: 300px;
}
.generic-tree {
  width: 280px;
  flex-shrink: 0;
  overflow: auto;
  border-right: 1px solid #ddd;
}
.generic-details {
  flex: 1;
  min-width: 0;
  overflow: auto;
}
@media (max-width: 768px) {
  .generic-viewer {
    flex-direction: column;
  }
  .generic-tree {
    width: 100%;
    max-height: 200px;
    border-right: none;
    border-bottom: 1px solid #ddd;
  }
}
</style>
