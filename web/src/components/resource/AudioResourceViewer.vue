<script setup lang="ts">
import {ref,computed,watch,h,defineComponent,onBeforeUnmount,nextTick} from 'vue'
import {NInput,NButton,NModal,NCard,NInputNumber,NSelect,NTag,NProgress,NFormItem} from 'naive-ui'
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
const cues=ref<Cue[]>([]),error=ref(''),src=ref(''),selected=ref('')
const player=ref<HTMLAudioElement>(),upload=ref<HTMLInputElement>(),busy=ref(false)
let replacing:{waveId:number;bank:string;path:string;projectId:string}|undefined
const adding=ref(false),newName=ref(''),newId=ref<number|null>(null),templateId=ref<number|null>(null),newBank=ref(''),speakerFile=ref<File>(),headphoneFile=ref<File>()
const templates=computed(()=>cues.value.filter(c=>c.tracks.length===2&&!c.error).map(c=>({label:`${c.cueId} · ${c.name}`,value:c.cueId})))
const banks=computed(()=>[...new Set(cues.value.flatMap(c=>c.tracks.map(t=>t.bank)))].map(b=>({label:b,value:b})))
function openAdd(){newName.value='';newId.value=null;templateId.value=templates.value[0]?.value??null;newBank.value=banks.value[0]?.value??'';speakerFile.value=undefined;headphoneFile.value=undefined;adding.value=true}
type Action={operation:string;bank:string;cueId?:number;waveId?:number;templateId?:number;name?:string;uploadId?:string;speakerId?:string;headphoneId?:string;extensions?:LoopEdit[]}
const pending=ref<Action[]>([]),batchMode=ref(false),selectedCues=ref<Cue[]>([]),applyDialog=ref(false),progress=ref(0),stage=ref(''),applying=ref(false)
type LoopEdit={waveformIndex:number;loopStart:number;loopEnd:number;loopFlag:number}
type Extension=LoopEdit & {extensionIndex:number|null;fields:Record<string,string>;samples:number;sampleRate:number}
const replaceDialog=ref(false),replacement=ref<Action>(),extensionRows=ref<Extension[]>([]),editLoops=ref(0)
function queueReplace(){if(!replacement.value)return;pending.value.push({...replacement.value,extensions:editLoops.value?extensionRows.value.filter(r=>r.extensionIndex!==null).map(r=>({waveformIndex:r.waveformIndex,loopStart:r.loopStart,loopEnd:r.loopEnd,loopFlag:r.loopFlag})):undefined});replaceDialog.value=false}
let events:EventSource|undefined
const queueKey=()=>`mercury-audio-queue:${project.id}:${props.path}`
let queueStorageKey=''
watch(()=>[project.id,props.path],()=>{queueStorageKey=queueKey();try{pending.value=JSON.parse(localStorage.getItem(queueStorageKey)||'[]')}catch{pending.value=[]}},{immediate:true})
watch(pending,()=>{if(queueStorageKey)try{localStorage.setItem(queueStorageKey,JSON.stringify(pending.value))}catch{}},{deep:true,flush:'sync'})
const stageText=computed(()=>{const [key,detail]=stage.value.split(':');return key?`${tr('ui.audioStage'+key)}${detail?' · '+detail:''}`:''})
async function uploadAudio(file:File){if(file.size>128*1024*1024)throw new Error(tr('ui.audioUploadLimit'));const r=await fetch(`/api/projects/${project.id}/audio-upload`,{method:'POST',headers:{'X-Mercury-Local':'1','Content-Type':'application/octet-stream'},body:file});const j=await r.json();if(!r.ok||j.code!==0)throw new Error(j.message);return j.data.uploadId as string}
async function addCue(){if(!speakerFile.value||!headphoneFile.value||newId.value===null||templateId.value===null)return;busy.value=true;try{const speakerId=await uploadAudio(speakerFile.value),headphoneId=await uploadAudio(headphoneFile.value);pending.value.push({operation:'add',bank:newBank.value,cueId:newId.value,templateId:templateId.value,name:newName.value,speakerId,headphoneId});adding.value=false}catch(e){error.value=String(e)}finally{busy.value=false}}
function deleteCue(){for(const c of selectedCues.value){if(!c.tracks.length||pending.value.some(a=>a.operation==='delete'&&a.cueId===c.cueId))continue;pending.value.push({operation:'delete',bank:c.tracks[0]!.bank,cueId:c.cueId,name:c.name})}}
async function apply(){if(busy.value||!pending.value.length)return;progress.value=0;stage.value='queued';applying.value=true;busy.value=true;error.value='';try{const result=await api<{jobId:string}>(`/projects/${project.id}/audio-apply?`+new URLSearchParams({path:props.path}),'POST',{actions:pending.value});events=new EventSource(`/api/projects/${project.id}/audio-events?jobId=${result.jobId}`);events.onmessage=async e=>{const p=JSON.parse(e.data);progress.value=p.percent;stage.value=p.stage;if(p.done){events?.close();busy.value=false;applying.value=false;if(p.error)error.value=p.error;else{pending.value=[];await project.status();project.revision++;await load()}}};events.onerror=()=>{stage.value='reconnect'}}catch(e){error.value=String(e);busy.value=false;applying.value=false}}
function selectionChanged(e:any){selectedCues.value=[...new Map<number,Cue>(e.api.getSelectedRows().map((r:AudioRow)=>[r.cue.cueId,r.cue] as [number,Cue])).values()]}

let generation=0
function stop(){player.value?.pause();src.value='';player.value?.removeAttribute('src');player.value?.load()}
async function load(){const g=++generation;stop();selectedCues.value=[];cues.value=[];error.value='';try{const result=await api<Cue[]>(`/projects/${project.id}/resource-cues?`+new URLSearchParams({path:props.path}));if(g===generation)cues.value=result}catch(e){if(g===generation)error.value=String(e)}}
watch(()=>[props.path,project.id],load,{immediate:true})
function chooseReplace(row:AudioRow){if(busy.value||!row.track||!props.path.endsWith('.uasset'))return;replacing={waveId:row.track.waveId,bank:row.track.bank,path:props.path,projectId:project.id};upload.value?.click()}
async function replaceFile(event:Event){const input=event.target as HTMLInputElement,file=input.files?.[0],target=replacing;input.value='';if(!file||!target||target.projectId!==project.id||target.path!==props.path)return;busy.value=true;try{const uploadId=await uploadAudio(file);replacement.value={operation:'replace',bank:target.bank,waveId:target.waveId,uploadId,name:file.name};extensionRows.value=await api<Extension[]>(`/projects/${target.projectId}/audio-extension?`+new URLSearchParams({path:target.path,bank:target.bank,waveId:String(target.waveId)}));editLoops.value=0;replaceDialog.value=true}catch(e){error.value=String(e)}finally{busy.value=false}}
async function play(row:AudioRow){stop();error.value='';selected.value=`${row.cue.name} · Wave ${row.waveId}`;src.value=`/api/projects/${project.id}/resource-audio?`+new URLSearchParams({path:props.path,index:String(row.cue.index),part:String(row.part)});await nextTick();try{await player.value?.play()}catch(e){error.value=String(e)}}
const rows=computed<AudioRow[]>(()=>cues.value.flatMap<AudioRow>(c=>c.waveIds.length?c.waveIds.map((waveId,part)=>({key:`${c.index}:${part}`,cue:c,waveId,part,track:c.tracks[part]})):[{key:`${c.index}:empty`,cue:c,waveId:null,part:0}]))
const spanCue=(p:SpanRowsParams<AudioRow>)=>p.nodeA?.data?.cue.index===p.nodeB?.data?.cue.index
const TrackCell=defineComponent({props:['params'],setup(p){return()=>{const row=(p.params as ICellRendererParams<AudioRow>).data;if(!row)return null;const t=row.track;return h('div',{class:'audio-track-cell'},[h('button',{class:'audio-play-icon',type:'button',title:tr('ui.audioPlay'),'aria-label':`${tr('ui.audioPlay')} ${row.waveId??''}`,disabled:row.waveId===null||!!row.cue.error,onClick:()=>play(row)},[h(Play,{size:18})]),h('span',{title:row.cue.error??undefined},row.cue.error||(t?`Track ${t.trackIndex} · ${t.route || 'Unknown'} · ${t.bank} · ${t.channels}ch / ${t.sampleRate}Hz`:'—'))])}}})
const ActionCell=defineComponent({props:['params'],setup(p){return()=>{const row=(p.params as ICellRendererParams<AudioRow>).data;return row?h(NButton,{size:'small',disabled:busy.value||!row.track||!props.path.endsWith('.uasset')||!!row.cue.error,onClick:()=>chooseReplace(row)},()=>tr('ui.audioReplace')):null}}})
const columns=computed<ColDef<AudioRow>[]>(()=>[
{headerName:'Index',valueGetter:p=>p.data?.cue.index,minWidth:75,maxWidth:100,spanRows:spanCue},
{headerName:'Cue',valueGetter:p=>p.data?.cue.name,minWidth:230,flex:2,spanRows:spanCue},
{headerName:'Cue ID',valueGetter:p=>p.data?.cue.cueId,minWidth:95,maxWidth:115,spanRows:spanCue},
{headerName:'Wave ID',field:'waveId',minWidth:100,maxWidth:120},
{headerName:'Track / Bus / Bank',valueGetter:p=>p.data?.track?`${p.data.track.route} ${p.data.track.bank}`:'',colId:'track',cellRenderer:TrackCell,minWidth:440,flex:3},
{headerName:tr('ui.actions'),filter:false,colId:'actions',cellRenderer:ActionCell,width:150,minWidth:150,pinned:'right'}])
const defaultColDef={resizable:true,sortable:false,filter:'agTextColumnFilter',floatingFilter:true}
onBeforeUnmount(()=>{generation++;stop();events?.close()})
</script>
<template>
<div>
<input ref="upload" type="file" accept=".hca,.wav" style="display:none" @change="replaceFile" />
<div class="audio-toolbar">
<div v-if="path.endsWith('.uasset')" class="audio-actions"><n-button :disabled="busy" @click="openAdd">{{tr('ui.audioAddCue')}}</n-button><n-button :disabled="busy" @click="batchMode=!batchMode">{{tr('ui.audioBatch')}} {{batchMode?'‹':'›'}}</n-button><n-button v-if="batchMode" type="error" :disabled="busy || !selectedCues.length" @click="deleteCue">{{tr('ui.audioDeleteCue')}}</n-button><n-tag>{{tr('ui.audioPending')}}: {{pending.length}}</n-tag><n-button type="primary" :disabled="busy || !pending.length" @click="applyDialog=true">{{tr('ui.audioApply')}}</n-button></div>
<div class="audio-player"><span>{{selected}}</span><audio ref="player" :src="src || undefined" controls preload="none" @error="error=tr('ui.audioFailed')" /></div></div>
<n-modal v-model:show="replaceDialog"><n-card :title="tr('ui.audioReplace')" style="width:min(680px,95vw)" :bordered="false">
<p>{{replacement?.name}} · {{replacement?.bank}} · Wave {{replacement?.waveId}}</p>
<n-form-item :label="tr('ui.audioExtensionMode')"><n-select v-model:value="editLoops" :options="[{label:tr('ui.audioKeepExtension'),value:0},{label:tr('ui.audioEditExtension'),value:1}]" /></n-form-item>
<p>{{tr('ui.audioLoopWarning')}}</p>
<div v-for="r in extensionRows" :key="r.waveformIndex">
<p>Waveform {{r.waveformIndex}} · Extension {{r.extensionIndex ?? '—'}} · {{r.sampleRate}} Hz · {{r.samples}} samples</p>
<template v-if="r.extensionIndex!==null">
<n-form-item label="LoopFlag"><n-input-number v-model:value="r.loopFlag" :disabled="!editLoops" :min="0" :max="2" :precision="0" /></n-form-item>
<n-form-item label="LoopStart (samples)"><n-input-number v-model:value="r.loopStart" :disabled="!editLoops" :min="0" :precision="0" /></n-form-item>
<n-form-item label="LoopEnd (samples)"><n-input-number v-model:value="r.loopEnd" :disabled="!editLoops" :min="0" :precision="0" /></n-form-item>
<div v-for="(value,key) in r.fields" :key="key"><span v-if="key!=='LoopStart' && key!=='LoopEnd'">{{key}}: {{value}} (hex)</span></div>
</template></div>
<n-button type="primary" :disabled="busy" @click="queueReplace">{{tr('ui.audioQueue')}}</n-button>
</n-card></n-modal>
<n-modal v-model:show="adding"><n-card :title="tr('ui.audioAddCue')" style="width:min(600px,95vw)" :bordered="false"><div class="audio-add-form">
<n-form-item label="CueName"><n-input v-model:value="newName" /></n-form-item><n-form-item label="Cue ID"><n-input-number v-model:value="newId" :min="0" :precision="0" /></n-form-item>
<n-form-item :label="tr('ui.audioTemplateCue')"><n-select v-model:value="templateId" :options="templates" /></n-form-item><n-form-item label="Bank"><n-select v-model:value="newBank" :options="banks" /></n-form-item>
<n-form-item label="BGM_SPEAKER"><input type="file" accept=".wav,.hca" @change="speakerFile=($event.target as HTMLInputElement).files?.[0]" /></n-form-item>
<n-form-item label="BGM_HEADPHONE"><input type="file" accept=".wav,.hca" @change="headphoneFile=($event.target as HTMLInputElement).files?.[0]" /></n-form-item>
<div v-if="error" role="alert">{{error}}</div><n-button :loading="busy" :disabled="!speakerFile || !headphoneFile || !newName || newId===null" @click="addCue">{{tr('ui.audioQueue')}}</n-button></div></n-card></n-modal>
<n-modal v-model:show="applyDialog" :mask-closable="!busy" :close-on-esc="!busy"><n-card :title="tr('ui.audioApply')" style="width:min(700px,95vw)" :bordered="false">
<ul class="audio-pending-list"><li v-for="(a,i) in pending" :key="i">{{tr('ui.audioOp'+a.operation)}} · {{a.bank}} · {{a.name || (a.operation==='replace'?a.waveId:a.cueId)}} <n-button v-if="!busy" size="tiny" @click="pending.splice(i,1)">×</n-button></li></ul>
<n-progress v-if="applying || progress>0" type="line" :percentage="progress" /><p>{{stageText}}</p><div v-if="error" role="alert">{{error}}</div>
<n-button type="primary" :loading="busy" :disabled="busy || !pending.length" @click="apply">{{tr('ui.confirm')}}</n-button></n-card></n-modal>
<div v-if="error" role="alert" style="color:#d03050">{{error}}</div>
<ag-grid-vue class="audio-grid" :theme="themeQuartz" :column-defs="columns" :default-col-def="defaultColDef" :row-data="rows" :get-row-id="p=>p.data.key" :enable-cell-span="true" :row-height="44" :row-selection="{mode:'multiRow',checkboxes:batchMode,headerCheckbox:batchMode,enableClickSelection:false}" @selection-changed="selectionChanged" />
</div>
</template>
<style>
.audio-toolbar{display:flex;gap:16px;margin-bottom:12px;align-items:center;justify-content:space-between;flex-wrap:wrap}.audio-actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.audio-player{margin-left:auto;text-align:right}.audio-player audio{display:block;width:320px;height:40px}.audio-pending-list{max-height:300px;overflow:auto}.audio-toolbar .n-input-number{width:140px}.audio-add-form{display:flex;flex-direction:column;gap:14px}
.audio-grid{height:65vh;min-height:320px;width:100%}
.audio-track-cell{display:flex;align-items:center;gap:10px;height:100%}
.audio-track-cell span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.audio-play-icon{border:0;background:transparent;color:#2080f0;cursor:pointer;display:inline-flex;align-items:center;justify-content:center;padding:6px;flex-shrink:0}
.audio-play-icon:disabled{opacity:.4;cursor:default}
.audio-play-icon:focus-visible{outline:2px solid #2080f0;border-radius:4px}
</style>
