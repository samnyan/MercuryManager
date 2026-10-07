<script setup lang="ts">
import {tr,i18n} from './i18n'
import {ref,computed,watch,onBeforeUnmount} from 'vue'
import {useRoute} from 'vue-router'
import {NButton,NInput,NModal,NCard,NFormItem,NSpace,NCheckbox,useMessage} from 'naive-ui'
import AssetGrid from './AssetGrid.vue'
import AssetForm from './AssetForm.vue'
import {tableCatalog} from './tableCatalog'
import {api,useProject,type Row,type Field} from './project'
import {useReferenceStore} from './referenceStore'
import {getTableSchema} from './tableSchema'

const project=useProject(),route=useRoute(),message=useMessage()
const refStore=useReferenceStore()
const rows=ref<Row[]>([]),fields=ref<Field[]>([]),rowName=ref(''),editor=ref(false),adding=ref(false),busy=ref(false),search=ref('')

const isTable=computed(()=>route.path.startsWith('/table/'))
const tableName=computed(()=>isTable.value ? String(route.params.name ?? '') : '')
const currentSchema=computed(()=>isTable.value && tableName.value ? getTableSchema(tableName.value) : undefined)

const title=computed(()=>{
  const raw=String(route.params.name??'')
  if(currentSchema.value){
    return i18n.global.locale.value==='zh'?currentSchema.value.nameCn:(currentSchema.value.nameEn||raw)
  }
  const key=(isTable.value?'tables.':'messages.')+raw
  return i18n.global.te(key)?tr(key):raw
})
const heading=computed(()=>{
  const raw=String(route.params.name??'')
  return title.value===raw?raw:`${title.value} / ${raw}`
})
const name=computed(()=>(isTable.value?'Table__':'')+String(route.params.name??''))

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

async function load(){
  rows.value=[]
  if(!project.id||!project.contentRoot||!name.value)return
  try{
    rows.value=await api<Row[]>(`/workspaces/${project.id}/messages/${encodeURIComponent(name.value)}`)
    preloadReferences()
  }catch(e){
    message.error(String(e))
  }
}

watch(()=>[project.id,name.value],load,{immediate:true})
watch(()=>refStore.resolveEnabled,(enabled)=>{if(enabled) preloadReferences()})

function select(key:string){const row=rows.value.find(r=>r.rowName===key)!;adding.value=false;rowName.value=key;fields.value=row.fields.map(f=>({...f,value:['ArrayPropertyData','MapPropertyData'].includes(f.type)?JSON.stringify(f.value):f.value}));editor.value=true}
function add(){if(!rows.value.length)return;adding.value=true;rowName.value='';fields.value=rows.value[0]!.fields.map(f=>({...f,value:['ArrayPropertyData','MapPropertyData'].includes(f.type)?'[]':f.type==='Int64PropertyData'||f.type==='UInt64PropertyData'?'0':f.type==='EnumPropertyData'?f.value:f.type==='BoolPropertyData'?false:typeof f.value==='number'?0:''}));editor.value=true}
async function save(){busy.value=true;try{const values=Object.fromEntries(fields.value.map(f=>[f.name,['ArrayPropertyData','MapPropertyData'].includes(f.type)?JSON.parse(String(f.value)):f.value]));await api(`/workspaces/${project.id}/messages/${encodeURIComponent(name.value)}/rows`,adding.value?'POST':'PATCH',{rowName:rowName.value,fields:values});await load();await project.status();editor.value=false;message.success(tr('ui.tableDraftUpdated'))}catch(e){message.error(String(e))}finally{busy.value=false}}

onBeforeUnmount(()=>{project.pending=false})
watch(rowName,()=>{if(editor.value&&adding.value)project.pending=true},{flush:'sync'})
const originalForm=ref('')
watch(editor,value=>{if(value)originalForm.value=JSON.stringify(fields.value);else project.pending=false},{flush:'sync'})
watch(fields,()=>{project.pending=editor.value&&JSON.stringify(fields.value)!==originalForm.value},{deep:true,flush:'sync'})
</script>

<template>
  <n-space vertical>
    <h2 class="page-title">{{heading}}</h2>
    <n-space align="center">
      <n-button :disabled="!rows.length" @click="add">{{tr('ui.addRow')}}</n-button>
      <n-input v-model:value="search" :placeholder="tr('ui.searchFieldsRowKey')"/>
      <n-checkbox v-if="isTable" v-model:checked="refStore.resolveEnabled">
        {{tr('ui.resolveReferences')}}
      </n-checkbox>
    </n-space>
    <asset-grid :rows="rows" :search="search" :schema="currentSchema" @select="select"/>
  </n-space>
  <n-modal v-model:show="editor" :mask-closable="false">
    <n-card :title="heading+' · '+(adding?tr('ui.addRow'):rowName)" style="width:calc(100vw - 32px);height:calc(100vh - 32px)" closable @close="editor=false">
      <div style="height:calc(100vh - 190px);overflow:auto">
        <n-form-item :label="tr('ui.rowKey')+' / RowName'">
          <n-input v-model:value="rowName" :disabled="!adding"/>
        </n-form-item>
        <asset-form :fields="fields" :schema="currentSchema"/>
      </div>
      <template #footer>
        <n-button type="primary" :loading="busy" @click="save">{{tr('ui.applyToDraft')}}</n-button>
      </template>
    </n-card>
  </n-modal>
</template>
