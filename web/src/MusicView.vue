<script setup lang="ts">
import {tr} from './i18n'
import { ref, computed, watch, onBeforeUnmount } from 'vue'
import { NButton, NInput, NSpace, useMessage } from 'naive-ui'
import {fieldTitle} from './fieldLabels'
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
const columns = computed<ColDef[]>(() => [
  {field:'rowName',headerName:'ID',flex:1,minWidth:90}, {field:'MusicMessage',headerName:fieldTitle('MusicMessage'),flex:3,minWidth:220}, {field:'ArtistMessage',headerName:fieldTitle('ArtistMessage'),flex:2,minWidth:160},
  {field:'VersionNo',headerName:fieldTitle('VersionNo'),flex:1,minWidth:90}, {field:'ScoreGenre',headerName:fieldTitle('ScoreGenre'),flex:1,minWidth:90},
  {field:'DifficultyNormalLv',headerName:'Normal',flex:1,minWidth:100}, {field:'DifficultyHardLv',headerName:'Hard',flex:1,minWidth:100},
  {field:'DifficultyExtremeLv',headerName:'Expert',flex:1,minWidth:100}, {field:'DifficultyInfernoLv',headerName:'Inferno',flex:1,minWidth:100}
])
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
    await project.refresh();await project.status(); editor.value=false; message.success(tr('ui.newSongAddedToDraft')); return
  }
  if (!selected.value) return
  const changes = Object.fromEntries(fields.value.filter(f => !f.readOnly && f.value !== selected.value!.fields.find(old => old.name === f.name)?.value).map(f => [f.name, f.value]))
  if (!Object.keys(changes).length) { message.info(tr('ui.noChanges')); return }
  await api(`/workspaces/${project.id}/music/${selected.value.rowName}`, 'PATCH', changes)
  await project.refresh();await project.status(); editor.value = false; message.success(tr('ui.draftSavedToWorkspace'))
}) }

onBeforeUnmount(()=>{project.pending=false})
const originalForm=ref('')
watch(editor,value=>{if(value)originalForm.value=JSON.stringify(fields.value);else project.pending=false},{flush:'sync'})
watch(fields,()=>{project.pending=editor.value&&JSON.stringify(fields.value)!==originalForm.value},{deep:true,flush:'sync'})
</script>
<template>
<n-space vertical :size="10">
<h2 class="page-title">{{tr('ui.musicParameters')}}</h2>

<n-space justify="space-between"><n-button :disabled="!project.id" @click="add">{{tr('ui.addSong')}}</n-button><span>{{ project.rows.length }} {{tr('ui.songs')}}</span><n-input v-model:value="search" :placeholder="tr('ui.searchSongArtistID')" style="width:260px"/></n-space>
<ag-grid-vue :theme="themeQuartz" style="height:calc(100vh - 210px);min-height:280px" :row-data="data" :column-defs="columns" :default-col-def="{sortable:true,filter:true,resizable:true}" :quick-filter-text="search" :get-row-id="params => params.data.rowName" @row-clicked="select"/>
</n-space>
<song-editor v-model:show="editor" :fields="fields" :title="tr('ui.editSong') + (selected?.rowName ?? '')" :busy="busy" @save="save"/>
</template>
