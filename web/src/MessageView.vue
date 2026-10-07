<script setup lang="ts">
import {ref,computed,watch} from 'vue'
import {useRoute} from 'vue-router'
import {NButton,NInput,NModal,NCard,NFormItem,NSpace,useMessage} from 'naive-ui'
import AssetGrid from './AssetGrid.vue'
import AssetForm from './AssetForm.vue'
import {tableCatalog} from './tableCatalog'
import {api,useProject,type Row,type Field} from './project'
const project=useProject(),route=useRoute(),message=useMessage()
const rows=ref<Row[]>([]),fields=ref<Field[]>([]),rowName=ref(''),editor=ref(false),adding=ref(false),busy=ref(false),search=ref('')
const title=computed(()=>tableCatalog.find(t=>t.name===route.params.name)?.title??String(route.params.name??'请选择左侧数据表'))
const name=computed(()=>(route.path.startsWith('/table/')?'Table__':'')+String(route.params.name??''))
async function load(){ rows.value=[];if(!project.id||!name.value)return;try{rows.value=await api<Row[]>(`/workspaces/${project.id}/messages/${encodeURIComponent(name.value)}`)}catch(e){message.error(String(e))} }
watch(()=>[project.id,name.value],load,{immediate:true})
function select(key:string){const row=rows.value.find(r=>r.rowName===key)!;adding.value=false;rowName.value=key;fields.value=row.fields.map(f=>({...f,value:['ArrayPropertyData','MapPropertyData'].includes(f.type)?JSON.stringify(f.value):f.value}));editor.value=true}
function add(){if(!rows.value.length)return;adding.value=true;rowName.value='';fields.value=rows.value[0]!.fields.map(f=>({...f,value:['ArrayPropertyData','MapPropertyData'].includes(f.type)?'[]':f.type==='Int64PropertyData'||f.type==='UInt64PropertyData'?'0':f.type==='EnumPropertyData'?f.value:f.type==='BoolPropertyData'?false:typeof f.value==='number'?0:''}));editor.value=true}
async function save(){busy.value=true;try{const values=Object.fromEntries(fields.value.map(f=>[f.name,['ArrayPropertyData','MapPropertyData'].includes(f.type)?JSON.parse(String(f.value)):f.value]));await api(`/workspaces/${project.id}/messages/${encodeURIComponent(name.value)}/rows`,adding.value?'POST':'PATCH',{rowName:rowName.value,fields:values});await load();editor.value=false;message.success('数据表草稿已更新')}catch(e){message.error(String(e))}finally{busy.value=false}}
</script>
<template><n-space vertical><h2 class="page-title">{{title}} / {{name.replace('Table__','')}}</h2><n-space><n-button :disabled="!rows.length" @click="add">新增行</n-button><n-input v-model:value="search" placeholder="搜索字段 / 行键"/></n-space><asset-grid :rows="rows" :search="search" @select="select"/></n-space><n-modal v-model:show="editor" :mask-closable="false"><n-card :title="title+' / '+name.replace('Table__','')+' · '+(adding?'新增行':rowName)" style="width:calc(100vw - 32px);height:calc(100vh - 32px)" closable @close="editor=false"><div style="height:calc(100vh - 190px);overflow:auto"><n-form-item label="行键 / RowName"><n-input v-model:value="rowName" :disabled="!adding"/></n-form-item><asset-form :fields="fields"/></div><template #footer><n-button type="primary" :loading="busy" @click="save">应用到草稿</n-button></template></n-card></n-modal></template>
