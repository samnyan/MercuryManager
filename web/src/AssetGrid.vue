<script setup lang="ts">
import {computed} from 'vue'
import {AgGridVue} from 'ag-grid-vue3'
import {themeQuartz} from 'ag-grid-community'
import type {Row} from './project'
import {fieldLabel} from './fieldLabels'
const props=defineProps<{rows:Row[];search:string}>()
const emit=defineEmits<{select:[key:string]}>()
const data=computed(()=>props.rows.map(r=>({rowName:r.rowName,...Object.fromEntries(r.fields.map(f=>[f.name,typeof f.value==='object'?JSON.stringify(f.value):f.value]))})))
const columns=computed(()=>[{field:'rowName',headerName:'行键 / RowName',width:160},...(props.rows[0]?.fields??[]).map(f=>({field:f.name,headerName:fieldLabel(f.name),width:f.type==='BoolPropertyData'?110:f.type==='StrPropertyData'?220:['ArrayPropertyData','MapPropertyData'].includes(f.type)?220:['Int64PropertyData','UInt64PropertyData'].includes(f.type)?150:120,minWidth:80,wrapHeaderText:true,autoHeaderHeight:true}))])
</script>
<template><ag-grid-vue :theme="themeQuartz" style="height:calc(100vh - 210px)" :row-data="data" :column-defs="columns" :default-col-def="{sortable:true,filter:true,resizable:true}" :quick-filter-text="search" @row-clicked="emit('select',$event.data.rowName)"/></template>
