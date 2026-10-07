<script setup lang="ts">
import {tr} from './i18n'
import {computed} from 'vue'
import {AgGridVue} from 'ag-grid-vue3'
import {themeQuartz} from 'ag-grid-community'
import type {Row} from './project'
import {fieldTitle} from './fieldLabels'
const props=defineProps<{rows:Row[];search:string}>()
const emit=defineEmits<{select:[key:string]}>()
const data=computed(()=>props.rows.map(r=>({rowName:r.rowName,...Object.fromEntries(r.fields.map(f=>[f.name,typeof f.value==='object'?JSON.stringify(f.value):f.value]))})))
const columns=computed(()=>[{field:'rowName',headerName:tr('ui.rowKey'),flex:1.3,minWidth:140},...(props.rows[0]?.fields??[]).map(f=>({field:f.name,headerName:fieldTitle(f.name),flex:/Name|Message|Path|Text|Directory/.test(f.name)?2:1,minWidth:/Name|Message|Path|Text|Directory/.test(f.name)?180:f.type==='BoolPropertyData'?100:['Int64PropertyData','UInt64PropertyData'].includes(f.type)?150:120,wrapHeaderText:true,autoHeaderHeight:true}))])
</script>
<template><ag-grid-vue :theme="themeQuartz" style="height:calc(100vh - 210px)" :row-data="data" :column-defs="columns" :default-col-def="{sortable:true,filter:true,resizable:true}" :quick-filter-text="search" @row-clicked="emit('select',$event.data.rowName)"/></template>
