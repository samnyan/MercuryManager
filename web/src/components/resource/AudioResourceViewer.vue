<script setup lang="ts">
import {ref,computed,watch,h,defineComponent,onBeforeUnmount,nextTick} from 'vue'
import {NInput,NButton} from 'naive-ui'
import {AgGridVue} from 'ag-grid-vue3'
import {themeQuartz,type ColDef,type ICellRendererParams,type SpanRowsParams} from 'ag-grid-community'
import {Play} from '@lucide/vue'
import {api,useProject} from '../../project'
import {tr} from '../../i18n'
const props=defineProps<{path:string}>()
const project=useProject()
type Track={waveId:number;route:string;bank:string;trackIndex:number;channels:number;sampleRate:number}
type Cue={index:number;name:string;cueId:number;waveIds:number[];error:string|null;tracks:Track[]}
type AudioRow={key:string;cue:Cue;waveId:number|null;part:number;track?:Track}
const cues=ref<Cue[]>([]),search=ref(''),error=ref(''),src=ref(''),selected=ref('')
const player=ref<HTMLAudioElement>(),upload=ref<HTMLInputElement>(),busy=ref(false)
let replacing:{waveId:number;bank:string;path:string;projectId:string}|undefined
let generation=0
function stop(){player.value?.pause();src.value='';player.value?.removeAttribute('src');player.value?.load()}
async function load(){const g=++generation;stop();cues.value=[];error.value='';try{const result=await api<Cue[]>(`/projects/${project.id}/resource-cues?`+new URLSearchParams({path:props.path}));if(g===generation)cues.value=result}catch(e){if(g===generation)error.value=String(e)}}
watch(()=>[props.path,project.id],load,{immediate:true})
function chooseReplace(row:AudioRow){if(busy.value||!row.track||!props.path.endsWith('.uasset'))return;if(!confirm(tr('ui.audioReplaceConfirm')))return;replacing={waveId:row.track.waveId,bank:row.track.bank,path:props.path,projectId:project.id};upload.value?.click()}
async function replaceFile(event:Event){const input=event.target as HTMLInputElement,file=input.files?.[0],target=replacing;input.value='';if(!file||!target)return;busy.value=true;stop();try{if(file.size>32*1024*1024)throw new Error(tr('ui.audioUploadLimit'));const base64=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result).split(',')[1]!);reader.onerror=reject;reader.readAsDataURL(file)});await api(`/projects/${target.projectId}/resource-audio-replace?`+new URLSearchParams({path:target.path}),'POST',{bank:target.bank,waveId:target.waveId,hcaBase64:base64});if(project.id===target.projectId){await project.status();project.revision++}if(project.id===target.projectId&&props.path===target.path)await load()}catch(e){error.value=String(e)}finally{busy.value=false}}
async function play(row:AudioRow){stop();error.value='';selected.value=`${row.cue.name} · Wave ${row.waveId}`;src.value=`/api/projects/${project.id}/resource-audio?`+new URLSearchParams({path:props.path,index:String(row.cue.index),part:String(row.part)});await nextTick();try{await player.value?.play()}catch(e){error.value=String(e)}}
const rows=computed<AudioRow[]>(()=>cues.value.filter(c=>`${c.name} ${c.cueId} ${c.waveIds} ${c.tracks.map(t=>`${t.route} ${t.bank}`)}`.toLowerCase().includes(search.value.toLowerCase())).flatMap<AudioRow>(c=>c.waveIds.length?c.waveIds.map((waveId,part)=>({key:`${c.index}:${part}`,cue:c,waveId,part,track:c.tracks[part]})):[{key:`${c.index}:empty`,cue:c,waveId:null,part:0}]))
const spanCue=(p:SpanRowsParams<AudioRow>)=>p.nodeA?.data?.cue.index===p.nodeB?.data?.cue.index
const TrackCell=defineComponent({props:['params'],setup(p){return()=>{const row=(p.params as ICellRendererParams<AudioRow>).data;if(!row)return null;const t=row.track;return h('div',{class:'audio-track-cell'},[h('button',{class:'audio-play-icon',type:'button',title:tr('ui.audioPlay'),'aria-label':`${tr('ui.audioPlay')} ${row.waveId??''}`,disabled:row.waveId===null||!!row.cue.error,onClick:()=>play(row)},[h(Play,{size:18})]),h('span',{title:row.cue.error??undefined},row.cue.error||(t?`Track ${t.trackIndex} · ${t.route || 'Unknown'} · ${t.bank} · ${t.channels}ch / ${t.sampleRate}Hz`:'—'))])}}})
const ActionCell=defineComponent({props:['params'],setup(p){return()=>{const row=(p.params as ICellRendererParams<AudioRow>).data;return row?h(NButton,{size:'small',disabled:busy.value||!row.track||!props.path.endsWith('.uasset')||!!row.cue.error,onClick:()=>chooseReplace(row)},()=>tr('ui.audioReplace')):null}}})
const columns=computed<ColDef<AudioRow>[]>(()=>[
{headerName:'Index',valueGetter:p=>p.data?.cue.index,minWidth:75,maxWidth:100,spanRows:spanCue},
{headerName:'Cue',valueGetter:p=>p.data?.cue.name,minWidth:230,flex:2,spanRows:spanCue},
{headerName:'Cue ID',valueGetter:p=>p.data?.cue.cueId,minWidth:95,maxWidth:115,spanRows:spanCue},
{headerName:'Wave ID',field:'waveId',minWidth:100,maxWidth:120},
{headerName:'Track / Bus / Bank',colId:'track',cellRenderer:TrackCell,minWidth:440,flex:3},
{headerName:tr('ui.actions'),colId:'actions',cellRenderer:ActionCell,width:150,minWidth:150,pinned:'right'}])
const defaultColDef={resizable:true,sortable:false}
onBeforeUnmount(()=>{generation++;stop()})
</script>
<template>
<div>
<input ref="upload" type="file" accept=".hca" style="display:none" @change="replaceFile" />
<n-input v-model:value="search" :placeholder="tr('ui.audioSearch')" clearable />
<div style="margin:12px 0"><span>{{selected}}</span><audio ref="player" :src="src || undefined" controls preload="none" style="display:block;width:min(100%,600px)" @error="error=tr('ui.audioFailed')" /></div>
<div v-if="error" role="alert" style="color:#d03050">{{error}}</div>
<ag-grid-vue class="audio-grid" :theme="themeQuartz" :column-defs="columns" :default-col-def="defaultColDef" :row-data="rows" :get-row-id="p=>p.data.key" :enable-cell-span="true" :row-height="44" />
</div>
</template>
<style>
.audio-grid{height:65vh;min-height:320px;width:100%}
.audio-track-cell{display:flex;align-items:center;gap:10px;height:100%}
.audio-track-cell span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.audio-play-icon{border:0;background:transparent;color:#2080f0;cursor:pointer;display:inline-flex;align-items:center;justify-content:center;padding:6px;flex-shrink:0}
.audio-play-icon:disabled{opacity:.4;cursor:default}
.audio-play-icon:focus-visible{outline:2px solid #2080f0;border-radius:4px}
</style>
