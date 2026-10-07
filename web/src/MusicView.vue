<script setup lang="ts">
import { ref, computed } from 'vue'
import { NButton, NInput, NSpace, useMessage } from 'naive-ui'
import SongEditor from './SongEditor.vue'
import { AgGridVue } from 'ag-grid-vue3'
import { themeQuartz, type ColDef, type RowClickedEvent } from 'ag-grid-community'
import { useProject, api, type Row, type Field } from './project'
const project = useProject()
const message = useMessage()
const search = ref('')
const busy = ref(false)
const selected = ref<Row | null>(null)
const editor = ref(false)
const fields = ref<Field[]>([])
const data = computed(() => project.rows.map(row => ({ rowName: row.rowName, ...Object.fromEntries(row.fields.map(f => [f.name, f.value])) })))
const columns: ColDef[] = [
  {field:'rowName',headerName:'ID',width:90}, {field:'MusicMessage',headerName:'歌曲名',flex:2}, {field:'ArtistMessage',headerName:'艺术家',flex:1},
  {field:'VersionNo',headerName:'版本',width:90}, {field:'ScoreGenre',headerName:'分类',width:90},
  {field:'DifficultyNormalLv',headerName:'Normal',width:100}, {field:'DifficultyHardLv',headerName:'Hard',width:100},
  {field:'DifficultyExtremeLv',headerName:'Expert',width:100}, {field:'DifficultyInfernoLv',headerName:'Inferno',width:100}
]
async function run(action: () => Promise<void>) { busy.value = true; try { await action() } catch(e) { message.error(e instanceof Error ? e.message : String(e)) } finally { busy.value = false } }
const adding = ref(false)
function add() {
  if (!project.rows.length) return
  adding.value = true
  selected.value = null
  fields.value = project.rows[0]!.fields.map(f => ({...f,value:f.type === 'BoolPropertyData' ? false : f.type === 'UInt64PropertyData' ? '0' : typeof f.value === 'number' ? 0 : '',readOnly:f.name === 'UniqueID' ? false : f.readOnly}))
  editor.value = true
}
function select(event: RowClickedEvent) {
  adding.value = false
  selected.value = project.rows.find(r => r.rowName === event.data.rowName) ?? null
  if (selected.value) { fields.value = selected.value.fields.map(field => ({ ...field })); editor.value = true }
}
async function save() { await run(async () => {
  if (adding.value) {
    const id = fields.value.find(f=>f.name==='UniqueID')?.value
    const changes = Object.fromEntries(fields.value.filter(f=>!f.readOnly && f.name!=='UniqueID').map(f=>[f.name,f.value]))
    await api(`/workspaces/${project.id}/music/${id}`, 'POST', changes)
    await project.refresh(); editor.value=false; message.success('新增歌曲已加入草稿'); return
  }
  if (!selected.value) return
  const changes = Object.fromEntries(fields.value.filter(f => !f.readOnly && f.value !== selected.value!.fields.find(old => old.name === f.name)?.value).map(f => [f.name, f.value]))
  if (!Object.keys(changes).length) { message.info('没有修改'); return }
  await api(`/workspaces/${project.id}/music/${selected.value.rowName}`, 'PATCH', changes)
  await project.refresh(); editor.value = false; message.success('草稿已保存到工作区')
}) }
</script>
<template>
<n-space vertical :size="10">
<h2 class="page-title">歌曲参数表</h2>

<n-space justify="space-between"><n-button :disabled="!project.id" @click="add">新增歌曲</n-button><span>{{ project.rows.length }} 首</span><n-input v-model:value="search" placeholder="搜索歌曲、作者、ID" style="width:260px"/></n-space>
<ag-grid-vue :theme="themeQuartz" style="height:calc(100vh - 210px);min-height:280px" :row-data="data" :column-defs="columns" :default-col-def="{sortable:true,filter:true,resizable:true}" :quick-filter-text="search" :get-row-id="params => params.data.rowName" @row-clicked="select"/>
</n-space>
<song-editor v-model:show="editor" :fields="fields" :title="'编辑歌曲 ' + (selected?.rowName ?? '')" :busy="busy" @save="save"/>
</template>
