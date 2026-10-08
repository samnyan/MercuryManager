<script setup lang="ts">
import { ref, computed, watch, h } from 'vue'
import { NTree, NDataTable, NInput, NBreadcrumb, NBreadcrumbItem, NModal, NImage, type TreeOption } from 'naive-ui'
import { tr } from '../../i18n'

const props = defineProps<{ data: unknown; rawUrl?: string; previewSrc?: string; previewError?: string }>()
const previewOpen = ref(false)
const rawMode = computed(() => segments.value.length===3 && segments.value[0]==='Exports' && segments.value[2]==='CustomSerialization')
const rawDownload = computed(() => props.rawUrl ? props.rawUrl+'&exportIndex='+encodeURIComponent(segments.value[1] ?? '') : '')
const selected = ref<unknown>()
const search = ref('')
const segments = ref<string[]>([])
const location = computed(() => '/' + segments.value.map(encodeURIComponent).join('/'))
const crumbs = computed(() => [{label: '/', depth: 0}, ...segments.value.map((label,index) => ({label,depth:index+1}))])
function navigate(parts: string[]) {
  let value: unknown = props.data
  for (const key of parts) {
    if (value === null || typeof value !== 'object') return
    value = (value as Record<string,unknown>)[key]
  }
  segments.value = parts; selected.value = value; search.value = ''
}
function openRow(row: {name:string;item:unknown}) {
  if (row.item !== null && typeof row.item === 'object') navigate([...segments.value,row.name])
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

function summary(value: unknown): string {
  return value !== null && typeof value === 'object' ? type(value) : String(value ?? 'null')
}

function children(value: unknown, path: string): TreeOption[] {
  if (value === null || typeof value !== 'object') return []
  return Object.entries(value).map(([key, item]) => ({
    key: path + '/' + encodeURIComponent(key),
    label: Array.isArray(value) && item && typeof item === 'object' && (item.Name || item.ObjectName) ? `${key} (${item.Name || item.ObjectName})` : key,
    value: item,
    isLeaf: path === '' && ['NameMap','Imports'].includes(key) || item === null || typeof item !== 'object'
  }))
}

const tree = ref<TreeOption[]>([])
watch(() => props.data, value => {tree.value = children(value, '')}, {immediate:true})

function load(node: TreeOption) {
  const items = children(node.value, String(node.key))
  node.children = items.length ? items : [{key:String(node.key)+'/__empty__',label:tr('ui.viewerEmpty'),isLeaf:true,disabled:true}]
  return Promise.resolve()
}

function select(keys: (string | number)[], nodes: (TreeOption | null)[]) {
  if (!nodes[0]) return
  navigate(String(keys[0]).split('/').slice(1).map(decodeURIComponent))
}

watch(
  () => props.data,
  value => {
    selected.value = value
    segments.value = []
    search.value = ''
  },
  { immediate: true }
)

const importMode = computed(() => segments.value.length===1 && segments.value[0]==='Imports')
const rows = computed(() => {
  if(importMode.value && Array.isArray(selected.value))return selected.value.map((item,index)=>({...item,name:String(index),item})).filter(item=>JSON.stringify(item).toLowerCase().includes(search.value.toLowerCase()))
  const value = selected.value
  const entries =
    value !== null && typeof value === 'object' ? Object.entries(value) : [['Value', value]]
  if(rawMode.value && props.rawUrl)entries.push(['[RawData]', null])
  return entries
    .map(([name, item]) => ({
      name,
      type: name==='[RawData]' && rawMode.value ? '—' : type(item),
      value: summary(item),
      item
    }))
    .filter(r => `${r.name} ${r.type} ${r.value}`.toLowerCase().includes(search.value.toLowerCase()))
})

const columns = computed(() => importMode.value ? ['Index','ObjectName','ClassPackage','ClassName','OuterIndex','OuterName','Optional'].map(key=>({title:key,key,width:key==='ObjectName'||key==='ClassPackage'?250:160,render:(row:Record<string,unknown>)=>String(row[key]??'')})) : [
  { title: tr('ui.viewerName'), key: 'name', width: 230, render:(row:{name:string})=>row.name==='[RawData]'&&rawMode.value?h('i',row.name):row.name },
  { title: tr('ui.viewerType'), key: 'type', width: 150 },
  {
    title: tr('ui.viewerValue'),
    key: 'value',
    render: (row: {name:string;item:unknown;value:string}) =>
      row.name==='[RawData]' && rawMode.value
        ? h('div',{style:'display:flex;gap:12px'},[
            props.previewSrc || props.previewError ? h('button',{class:'object-link',onClick:()=>previewOpen.value=true},tr('ui.rawPreview')) : null,
            h('a',{class:'object-link',href:rawDownload.value,download:''},tr('ui.rawDownload'))])
        : row.item !== null && typeof row.item === 'object'
        ? h('button', {class:'object-link',onClick:()=>openRow(row)},row.value)
        : h('span', { style: 'white-space:pre-wrap;overflow-wrap:anywhere' }, row.value)
  }
])
</script>

<template>
  <n-modal v-model:show="previewOpen" preset="card" :title="tr('ui.rawPreview')" style="width:min(90vw,1000px)">
    <div v-if="previewSrc" class="raw-preview"><n-image :src="previewSrc" :img-props="{style: 'max-width:100%;max-height:70vh;width:auto;height:auto;object-fit:contain;display:block'}" /></div>
    <div v-else>{{previewError}}</div>
  </n-modal>
  <div class="generic-viewer">
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
    <div class="generic-details">
      <n-breadcrumb style="margin-bottom:8px;flex-wrap:wrap"><n-breadcrumb-item v-for="crumb in crumbs" :key="crumb.depth" @click="navigate(segments.slice(0,crumb.depth))">{{crumb.label}}</n-breadcrumb-item></n-breadcrumb>
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
:row-props="(r: any) => ({onDblclick: () => openRow(r)})"
        :scroll-x="650"
        style="margin-top: 8px"
      ><template #empty>{{tr('ui.viewerEmpty')}}</template></n-data-table>
    </div>
  </div>
</template>

<style scoped>
.raw-preview {display:flex;justify-content:center;align-items:center;max-width:100%;max-height:70vh;overflow:hidden}
.raw-preview :deep(.n-image) {max-width:100%;min-width:0}
:deep(.object-link) {color:#2080f0;text-decoration:underline;cursor:pointer;background:none;border:0;padding:0;font:inherit;text-align:left}
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
