<script setup lang="ts">
import {tr,i18n} from './i18n'
import {computed,ref,watch} from 'vue'
import {AgGridVue} from 'ag-grid-vue3'
import {themeQuartz,type GridApi,type GridReadyEvent} from 'ag-grid-community'
import type {Row} from './project'
import {useReferenceStore} from './referenceStore'
import {type TableSchema,type TableFieldSchema,formatFieldHeader,generateFallbackSchema} from './tableSchema'

const props=defineProps<{rows:Row[];search:string;schema?:TableSchema}>()
const emit=defineEmits<{select:[key:string]}>()

const refStore=useReferenceStore()
const gridApi=ref<GridApi|null>(null)

function onGridReady(params:GridReadyEvent){
  gridApi.value=params.api
}

watch([()=>refStore.version,()=>refStore.resolveEnabled,()=>i18n.global.locale.value],()=>{
  gridApi.value?.refreshCells({force:true})
})

function escapeHtml(str:string):string{
  return str
    .replace(/&/g,'&amp;')
    .replace(/</g,'&lt;')
    .replace(/>/g,'&gt;')
    .replace(/"/g,'&quot;')
    .replace(/'/g,'&#39;')
}

const activeSchema=computed<TableSchema>(()=>{
  if(props.schema)return props.schema
  const sampleFields=(props.rows[0]?.fields??[]).map(f=>({name:f.name,type:f.type}))
  return generateFallbackSchema('Unknown',sampleFields)
})

const data=computed(()=>props.rows.map(r=>({
  rowName:r.rowName,
  ...Object.fromEntries(r.fields.map(f=>[f.name,typeof f.value==='object'?JSON.stringify(f.value):f.value]))
})))

const columns=computed(()=>{
  const locale=i18n.global.locale.value
  const schemaFields=activeSchema.value.fields
  const fieldSchemaMap=new Map<string,TableFieldSchema>()
  for(const f of schemaFields){
    fieldSchemaMap.set(f.key,f)
  }

  // RowName 作为第一列
  const rowNameSchema=fieldSchemaMap.get('RowName')
  const rowNameHeader=rowNameSchema ? formatFieldHeader(rowNameSchema,locale) : tr('ui.rowKey')
  const result:any[]=[
    {
      field:'rowName',
      headerName:rowNameHeader,
      flex:1.3,
      minWidth:rowNameSchema?.tableMinWidth??140
    }
  ]

  // 其余字段
  const rowFields=props.rows[0]?.fields??[]
  for(const rf of rowFields){
    const fSchema=fieldSchemaMap.get(rf.name)
    const headerName=fSchema ? formatFieldHeader(fSchema,locale) : rf.name
    const minWidth=fSchema?.tableMinWidth ?? (rf.type==='BoolPropertyData'?100:['Int64PropertyData','UInt64PropertyData'].includes(rf.type)?150:120)
    const flex=fSchema?.tableWidthFluid ? 2 : 1

    const colDef:any={
      field:rf.name,
      headerName,
      flex,
      minWidth,
      wrapHeaderText:true,
      autoHeaderHeight:true
    }

    if(fSchema?.messageLink){
      const targetMessage=fSchema.messageLink.messageTable
      colDef.cellRenderer=(params:any)=>{
        const val=params.value
        if(val==null||val==='')return ''
        const str=String(val)
        if(!refStore.resolveEnabled)return escapeHtml(str)

        const res=refStore.getText(targetMessage,str)
        if(res.status==='found'){
          const text=escapeHtml(res.text||'')
          const title=escapeHtml(`${str} ${tr('ui.referencedFrom')} ${targetMessage}`)
          return `<span class="ref-link" title="${title}">${text}</span>`
        }
        if(res.status==='not_found'){
          const title=escapeHtml(`${targetMessage}: ${tr('ui.referenceNotFound')}`)
          return `<span class="ref-not-found" title="${title}">${escapeHtml(str)} <small class="ref-tag">(${tr('ui.referenceNotFound')})</small></span>`
        }
        return `<span class="ref-pending">${escapeHtml(str)}</span>`
      }
    }

    result.push(colDef)
  }

  return result
})
</script>

<template>
  <ag-grid-vue
    :theme="themeQuartz"
    style="height:calc(100vh - 210px)"
    :row-data="data"
    :column-defs="columns"
    :default-col-def="{sortable:true,filter:true,resizable:true}"
    :quick-filter-text="search"
    @grid-ready="onGridReady"
    @row-clicked="emit('select',$event.data.rowName)"
  />
</template>

<style>
.ref-link {
  color: #18a058;
  text-decoration: underline;
  text-decoration-style: dashed;
  cursor: help;
  font-weight: 500;
  display: inline-block;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ref-link:hover {
  color: #36ad6a;
}
.ref-not-found {
  color: #d03050;
  cursor: help;
  display: inline-block;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ref-not-found .ref-tag {
  font-size: 11px;
  color: #d03050;
  opacity: 0.85;
}
.ref-pending {
  color: inherit;
}
</style>
