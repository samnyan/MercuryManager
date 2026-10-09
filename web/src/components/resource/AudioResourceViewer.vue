<script setup lang="ts">
import {ref,computed,watch,h,defineComponent,onBeforeUnmount,nextTick} from 'vue'
import {NInput,NButton,NModal,NCard,NInputNumber,NSelect,NTag,NProgress,NFormItem,NRadioGroup,NRadio} from 'naive-ui'
import {AgGridVue} from 'ag-grid-vue3'
import {themeQuartz,type ColDef,type ICellRendererParams,type SpanRowsParams} from 'ag-grid-community'
import {Play} from '@lucide/vue'
import {api,useProject} from '../../project'
import {tr} from '../../i18n'
import AudioTrackUpload from './AudioTrackUpload.vue'
const props=defineProps<{path:string}>()
const project=useProject()
type Track={waveId:number;route:string;bank:string;trackIndex:number;waveformIndex:number;channels:number;sampleRate:number}
type Cue={index:number;name:string;cueId:number;waveIds:number[];error:string|null;tracks:Track[]}
type AudioRow={key:string;cue:Cue;waveId:number|null;part:number;track?:Track}
const cues=ref<Cue[]>([]),error=ref(''),src=ref(''),selected=ref('')
const player=ref<HTMLAudioElement>(),busy=ref(false)
const adding=ref(false),newName=ref(''),newId=ref<number|null>(null),templateId=ref<number|null>(null),newBank=ref('')
type TrackUpload={trackIndex:number;uploadId:string;loop?:LoopEdit}
const addSession=ref(0)
const trackUploads=ref<Record<number,TrackUpload|null>>({})
const addTracks=computed(()=>sheetCues.value.find(c=>c.cueId===templateId.value)?.tracks??[])
watch(templateId,()=>{trackUploads.value={}})
const sheetCues=ref<Cue[]>([])
const cueConflicts=computed(()=>{const active=new Map(sheetCues.value.map(c=>[c.cueId,c.name]));for(const a of pending.value){if(a.operation==='delete')active.delete(a.cueId!);if(a.operation==='add')active.set(a.cueId!,a.name!);if(a.operation==='metadata'&&a.metadata?.cueName)active.set(a.metadata.cueId,a.metadata.cueName)}return {name:[...active.values()].includes(newName.value),id:newId.value!==null&&active.has(newId.value)}})
const addValid=computed(()=>!!newName.value&&/^[A-Za-z0-9_]+$/.test(newName.value)&&newName.value.length<=128&&newId.value!==null&&templateId.value!==null&&!cueConflicts.value.name&&!cueConflicts.value.id&&(createBank.value?/^[A-Za-z0-9_-]+\.awb$/i.test(newBankName.value)&&!banks.value.some(b=>b.value===newBankName.value):!!newBank.value)&&addTracks.value.length>0&&addTracks.value.every(t=>!!trackUploads.value[t.trackIndex]))
const templates=computed(()=>sheetCues.value.filter(c=>c.tracks.length>0&&!c.error&&new Set(c.tracks.map(t=>t.trackIndex)).size===c.tracks.length).map(c=>({label:`${c.cueId} · ${c.name}`,value:c.cueId})))
const bankNames=ref<string[]>([])
const banks=computed(()=>[...new Set([...bankNames.value,...cues.value.flatMap(c=>c.tracks.map(t=>t.bank)),...pending.value.map(a=>a.targetBank||a.bank)])].map(b=>({label:b,value:b})))
async function openAdd(){try{sheetCues.value=await api<Cue[]>(`/projects/${project.id}/resource-cues?`+new URLSearchParams({path:props.path,all:'true'}))}catch(e){error.value=String(e);return;}addSession.value++;trackUploads.value={};newName.value='';newId.value=null;templateId.value=templates.value[0]?.value??null;newBank.value=banks.value[0]?.value??'';createBank.value=0;newBankName.value='';adding.value=true}
type Action={tracks?:TrackUpload[];operation:string;bank:string;cueId?:number;waveId?:number;templateId?:number;name?:string;uploadId?:string;speakerId?:string;headphoneId?:string;extensions?:LoopEdit[];targetBank?:string;createBank?:boolean;speakerLoop?:LoopEdit;headphoneLoop?:LoopEdit;metadata?:{waveformIndex:number;targetBank:string;targetWaveId:number;cueId:number;cueFields?:Record<string,number>;loops?:LoopEdit[];cueName?:string;newCueId?:number;uploadId?:string}}
const pending=ref<Action[]>([]),batchMode=ref(false),selectedCues=ref<Cue[]>([]),applyDialog=ref(false),progress=ref(0),stage=ref(''),applying=ref(false)
type LoopEdit={waveformIndex:number;loopStart:number;loopEnd:number;loopFlag:number}
type Extension=LoopEdit & {extensionIndex:number|null;fields:Record<string,string>;samples:number;sampleRate:number}
const createBank=ref(0),newBankName=ref('')
const bankDialog=ref(false),bankDonor=ref(''),bankName=ref('')
function queueBank(){pending.value.push({operation:'create-bank',bank:bankDonor.value,targetBank:bankName.value,createBank:true,name:bankName.value});bankDialog.value=false}
const metadataDialog=ref(false),metadataRow=ref<AudioRow>(),metadataBank=ref(''),metadataWave=ref<number|null>(null),metadataName=ref(''),metadataId=ref<number|null>(null),metadataLength=ref<number|null>(null),metadataFields=ref<Record<string,number>>({}),audioMode=ref(0),editUpload=ref<TrackUpload|null>(null),editInfo=ref<import('./AudioTrackUpload.vue').AudioInfo>(),editSession=ref(0),editReading=ref(false)
const editConflict=computed(()=>sheetCues.value.some(c=>c.cueId!==metadataRow.value?.cue.cueId&&(c.name===metadataName.value||c.cueId===metadataId.value))||pending.value.some(a=>a.operation==='add'&&(a.name===metadataName.value||a.cueId===metadataId.value)))
const editValid=computed(()=>!!metadataName.value&&metadataName.value.length<=256&&!metadataName.value.includes('\0')&&metadataId.value!==null&&metadataId.value>=0&&!editConflict.value&&!!editUpload.value&&!editReading.value&&metadataWave.value!==null&&!!metadataBank.value)
let inspectGeneration=0
async function readLinked(){const g=++inspectGeneration;editInfo.value=undefined;editUpload.value=null;editReading.value=true;try{if(metadataWave.value===null)return;const i=await api<import('./AudioTrackUpload.vue').AudioInfo>(`/projects/${project.id}/audio-wave-info?`+new URLSearchParams({path:props.path,bank:metadataBank.value,waveId:String(metadataWave.value)}));if(g===inspectGeneration){editInfo.value=i;editSession.value++;editUpload.value={trackIndex:metadataRow.value!.track!.trackIndex,uploadId:''}}}catch(e){if(g===inspectGeneration)error.value=String(e)}finally{if(g===inspectGeneration)editReading.value=false}}
watch([metadataBank,metadataWave,audioMode],()=>{if(metadataDialog.value){if(audioMode.value!==1&&metadataRow.value?.track){metadataBank.value=metadataRow.value.track.bank;metadataWave.value=metadataRow.value.track.waveId;}editUpload.value=null;if(audioMode.value!==2)void readLinked();else {inspectGeneration++;editReading.value=false;editSession.value++}}})
async function openMetadata(row:AudioRow){if(!row.track)return;busy.value=true;try{metadataRow.value=row;metadataName.value=row.cue.name;metadataId.value=row.cue.cueId;metadataBank.value=row.track.bank;metadataWave.value=row.track.waveId;metadataLength.value=null;audioMode.value=0;sheetCues.value=await api<Cue[]>(`/projects/${project.id}/resource-cues?`+new URLSearchParams({path:props.path,all:'true'}));const meta=await api<{fields:Record<string,number>}>(`/projects/${project.id}/audio-metadata?`+new URLSearchParams({path:props.path,cueId:String(row.cue.cueId)}));metadataFields.value=meta.fields;await readLinked();metadataDialog.value=true}catch(e){error.value=String(e)}finally{busy.value=false}}
function queueMetadata(){const row=metadataRow.value,u=editUpload.value;if(!editValid.value||!row?.track||!u)return;pending.value.push({operation:'metadata',bank:row.track.bank,cueId:row.cue.cueId,name:metadataName.value,metadata:{waveformIndex:row.track.waveformIndex,targetBank:metadataBank.value,targetWaveId:metadataWave.value!,cueId:row.cue.cueId,newCueId:metadataId.value!,cueName:metadataName.value,cueFields:{...Object.fromEntries(Object.entries(metadataFields.value).filter(([k])=>k!=='Length')),...(metadataLength.value===null?{}:{Length:metadataLength.value})},uploadId:audioMode.value===2?u.uploadId:undefined,loops:u.loop?[{...u.loop,waveformIndex:row.track.waveformIndex}]:undefined}});metadataDialog.value=false}
let events:EventSource|undefined
const queueKey=()=>`mercury-audio-queue:${project.id}:${props.path}`
let queueStorageKey=''
watch(()=>[project.id,props.path],()=>{queueStorageKey=queueKey();try{pending.value=JSON.parse(localStorage.getItem(queueStorageKey)||'[]')}catch{pending.value=[]}},{immediate:true})
watch(pending,()=>{if(queueStorageKey)try{localStorage.setItem(queueStorageKey,JSON.stringify(pending.value))}catch{}},{deep:true,flush:'sync'})
const stageText=computed(()=>{const [key,detail]=stage.value.split(':');return key?`${tr('ui.audioStage'+key)}${detail?' · '+detail:''}`:''})
async function uploadAudio(file:File){if(file.size>128*1024*1024)throw new Error(tr('ui.audioUploadLimit'));const r=await fetch(`/api/projects/${project.id}/audio-upload`,{method:'POST',headers:{'X-Mercury-Local':'1','Content-Type':'application/octet-stream'},body:file});const j=await r.json();if(!r.ok||j.code!==0)throw new Error(j.message);return j.data.uploadId as string}
async function addCue(){if(!addValid.value)return;pending.value.push({operation:'add',bank:createBank.value?(addTracks.value[0]?.bank||bankNames.value[0]||''):newBank.value,cueId:newId.value!,templateId:templateId.value!,name:newName.value,tracks:addTracks.value.map(t=>trackUploads.value[t.trackIndex]!),targetBank:createBank.value?newBankName.value:newBank.value,createBank:!!createBank.value});adding.value=false}
function deleteCue(){for(const c of selectedCues.value){if(!c.tracks.length||pending.value.some(a=>a.operation==='delete'&&a.cueId===c.cueId))continue;pending.value.push({operation:'delete',bank:c.tracks[0]!.bank,cueId:c.cueId,name:c.name})}}
async function apply(){if(busy.value||!pending.value.length)return;progress.value=0;stage.value='queued';applying.value=true;busy.value=true;error.value='';try{const result=await api<{jobId:string}>(`/projects/${project.id}/audio-apply?`+new URLSearchParams({path:props.path}),'POST',{actions:pending.value});events=new EventSource(`/api/projects/${project.id}/audio-events?jobId=${result.jobId}`);events.onmessage=async e=>{const p=JSON.parse(e.data);progress.value=p.percent;stage.value=p.stage;if(p.done){events?.close();busy.value=false;applying.value=false;if(p.error)error.value=p.error;else{pending.value=[];await project.status();project.revision++;await load()}}};events.onerror=()=>{stage.value='reconnect'}}catch(e){error.value=String(e);busy.value=false;applying.value=false}}
function selectionChanged(e:any){selectedCues.value=[...new Map<number,Cue>(e.api.getSelectedRows().map((r:AudioRow)=>[r.cue.cueId,r.cue] as [number,Cue])).values()]}

let generation=0
function stop(){player.value?.pause();src.value='';player.value?.removeAttribute('src');player.value?.load()}
async function load(){const g=++generation;stop();selectedCues.value=[];cues.value=[];bankNames.value=[];error.value='';try{const names=await api<string[]>(`/projects/${project.id}/audio-banks?`+new URLSearchParams({path:props.path}));if(g===generation)bankNames.value=names;const result=await api<Cue[]>(`/projects/${project.id}/resource-cues?`+new URLSearchParams({path:props.path}));if(g===generation)cues.value=result}catch(e){if(g===generation)error.value=String(e)}}
watch(()=>[props.path,project.id],load,{immediate:true})
async function play(row:AudioRow){stop();error.value='';selected.value=`${row.cue.name} · Wave ${row.waveId}`;src.value=`/api/projects/${project.id}/resource-audio?`+new URLSearchParams({path:props.path,index:String(row.cue.index),part:String(row.part)});await nextTick();try{await player.value?.play()}catch(e){error.value=String(e)}}
const rows=computed<AudioRow[]>(()=>cues.value.flatMap<AudioRow>(c=>c.waveIds.length?c.waveIds.map((waveId,part)=>({key:`${c.index}:${part}`,cue:c,waveId,part,track:c.tracks[part]})):[{key:`${c.index}:empty`,cue:c,waveId:null,part:0}]))
const spanCue=(p:SpanRowsParams<AudioRow>)=>p.nodeA?.data?.cue.index===p.nodeB?.data?.cue.index
const TrackCell=defineComponent({props:['params'],setup(p){return()=>{const row=(p.params as ICellRendererParams<AudioRow>).data;if(!row)return null;const t=row.track;return h('div',{class:'audio-track-cell'},[h('button',{class:'audio-play-icon',type:'button',title:tr('ui.audioPlay'),'aria-label':`${tr('ui.audioPlay')} ${row.waveId??''}`,disabled:row.waveId===null||!!row.cue.error,onClick:()=>play(row)},[h(Play,{size:18})]),h('span',{title:row.cue.error??undefined},row.cue.error||(t?`Track ${t.trackIndex} · ${t.route || 'Unknown'} · ${t.bank} · ${t.channels}ch / ${t.sampleRate}Hz`:'—'))])}}})
const ActionCell=defineComponent({props:['params'],setup(p){return()=>{const row=(p.params as ICellRendererParams<AudioRow>).data;return row?h('div',{class:'audio-action-cell'},[h(NButton,{size:'small',disabled:busy.value||!row.track||!props.path.endsWith('.uasset')||!!row.cue.error,onClick:()=>openMetadata(row)},()=>tr('ui.audioMetadata'))]):null}}})
const columns=computed<ColDef<AudioRow>[]>(()=>[
{headerName:'Index',valueGetter:p=>p.data?.cue.index,minWidth:75,maxWidth:100,spanRows:spanCue},
{headerName:'Cue',valueGetter:p=>p.data?.cue.name,minWidth:230,flex:2,spanRows:spanCue},
{headerName:'Cue ID',valueGetter:p=>p.data?.cue.cueId,minWidth:95,maxWidth:115,spanRows:spanCue},
{headerName:'Wave ID',field:'waveId',minWidth:100,maxWidth:120},
{headerName:'Track / Bus / Bank',valueGetter:p=>p.data?.track?`${p.data.track.route} ${p.data.track.bank}`:'',colId:'track',cellRenderer:TrackCell,minWidth:440,flex:3},
{headerName:tr('ui.actions'),filter:false,colId:'actions',cellRenderer:ActionCell,width:230,minWidth:230,pinned:'right'}])
const defaultColDef={resizable:true,sortable:false,filter:'agTextColumnFilter',floatingFilter:true}
onBeforeUnmount(()=>{generation++;stop();events?.close()})
</script>
<template>
<div class="audio-view-container view-fill-container">

<div class="audio-toolbar view-header-fixed">
<div v-if="path.endsWith('.uasset')" class="audio-actions"><n-button :disabled="busy" @click="bankDonor=banks[0]?.value || '';bankName='';bankDialog=true">{{tr('ui.audioCreateBank')}}</n-button><n-button :disabled="busy" @click="openAdd">{{tr('ui.audioAddCue')}}</n-button><n-button :disabled="busy" @click="batchMode=!batchMode">{{tr('ui.audioBatch')}} {{batchMode?'‹':'›'}}</n-button><n-button v-if="batchMode" type="error" :disabled="busy || !selectedCues.length" @click="deleteCue">{{tr('ui.audioDeleteCue')}}</n-button><n-tag>{{tr('ui.audioPending')}}: {{pending.length}}</n-tag><n-button type="primary" :disabled="busy || !pending.length" @click="applyDialog=true">{{tr('ui.audioApply')}}</n-button></div>
<div class="audio-player"><span>{{selected}}</span><audio ref="player" :src="src || undefined" controls preload="none" @error="error=tr('ui.audioFailed')" /></div></div>
<n-modal v-model:show="bankDialog"><n-card :title="tr('ui.audioCreateBank')" style="width:min(600px,95vw)" :bordered="false">
<n-form-item :label="tr('ui.audioBankFormat')"><n-select v-model:value="bankDonor" :options="banks" /></n-form-item>
<n-form-item :label="tr('ui.audioBankName')"><n-input v-model:value="bankName" placeholder="MER_BGM_V4_01.awb" /></n-form-item>
<n-button type="primary" :disabled="busy || !bankDonor || !/^[A-Za-z0-9_-]+\.awb$/i.test(bankName) || banks.some(b=>b.value===bankName)" @click="queueBank">{{tr('ui.audioQueue')}}</n-button>
</n-card></n-modal>
<n-modal v-model:show="metadataDialog"><n-card :title="tr('ui.audioMetadata')" style="width:min(680px,95vw);max-height:90vh;overflow:auto" :bordered="false"><div class="audio-add-form">
<n-form-item label="CueName"><n-input v-model:value="metadataName" /></n-form-item>
<n-form-item label="Cue ID"><n-input-number v-model:value="metadataId" :min="0" :max="2147483647" :precision="0" /></n-form-item>
<p v-if="editConflict" class="audio-field-error">{{tr('ui.audioEditConflict')}}</p>
<n-form-item v-for="(_,field) in metadataFields" v-show="field!=='Length'" :key="field" :label="String(field)"><n-input-number v-model:value="metadataFields[field]" :min="0" :precision="0" /></n-form-item>
<n-form-item label="Cue Length (ms)"><n-input-number v-model:value="metadataLength" :min="0" :precision="0" :placeholder="tr('ui.audioAutoLength')" /></n-form-item>
<n-form-item :label="tr('ui.audioSection')"><n-radio-group v-model:value="audioMode"><n-radio :value="0">{{tr('ui.audioUnchanged')}}</n-radio><n-radio :value="1">{{tr('ui.audioRelink')}}</n-radio><n-radio :value="2">{{tr('ui.audioReplaceAudio')}}</n-radio></n-radio-group></n-form-item>
<template v-if="audioMode===1"><n-form-item label="AWB"><n-select v-model:value="metadataBank" :options="banks" filterable /></n-form-item><n-form-item label="Wave ID"><n-input-number v-model:value="metadataWave" :min="0" :max="65535" :precision="0" /></n-form-item></template>
<p>{{metadataBank}} · Wave {{metadataWave}}</p>
<p v-if="editReading">{{tr('ui.audioReadingFile')}}</p>
<audio-track-upload v-if="!editReading && metadataRow?.track" :key="editSession+':'+audioMode" :route="metadataRow.track.route" :track-index="metadataRow.track.trackIndex" :existing="audioMode!==2" :initial="audioMode===2?undefined:editInfo" @change="editUpload=$event" />
<p v-if="audioMode===2 || editUpload?.loop" role="alert">{{tr('ui.audioRebuildWarning')}} <strong>{{metadataBank}}</strong></p>
<p>{{tr('ui.audioSharedWarning')}}</p><div v-if="error" role="alert">{{error}}</div>
<n-button type="primary" :disabled="busy || !editValid" @click="queueMetadata">{{tr('ui.audioQueue')}}</n-button>
</div></n-card></n-modal>
<n-modal v-model:show="adding"><n-card :title="tr('ui.audioAddCue')" style="width:min(680px,95vw);max-height:90vh;overflow:auto" :bordered="false"><div class="audio-add-form">
<n-form-item label="AWB"><n-radio-group v-model:value="createBank"><n-radio :value="0">{{tr('ui.audioExistingBank')}}</n-radio><n-radio :value="1">{{tr('ui.audioCreateBank')}}</n-radio></n-radio-group></n-form-item>
<n-form-item v-if="!createBank" label="AWB"><n-select v-model:value="newBank" :options="banks" filterable /></n-form-item>
<n-form-item v-else :label="tr('ui.audioBankName')"><n-input v-model:value="newBankName" placeholder="MER_BGM_V4_01.awb" /><span v-if="banks.some(b=>b.value===newBankName)" role="alert" class="audio-field-error">{{tr('ui.audioBankConflict')}}</span></n-form-item>
<n-form-item label="CueName" :validation-status="cueConflicts.name?'error':undefined" :feedback="cueConflicts.name?tr('ui.audioNameConflict'):undefined"><n-input v-model:value="newName" :maxlength="128" /></n-form-item>
<n-form-item label="Cue ID" :validation-status="cueConflicts.id?'error':undefined" :feedback="cueConflicts.id?tr('ui.audioIdConflict'):undefined"><n-input-number v-model:value="newId" :min="0" :max="2147483647" :precision="0" /></n-form-item>
<n-form-item :label="tr('ui.audioTemplateCue')"><n-select v-model:value="templateId" :options="templates" filterable /></n-form-item><p class="audio-form-help">{{tr('ui.audioTemplateHelp')}}</p>
<audio-track-upload v-for="track in addTracks" :key="addSession+':'+String(templateId)+':'+track.trackIndex" :route="track.route" :track-index="track.trackIndex" @change="trackUploads[track.trackIndex]=$event" />
<div v-if="error" role="alert">{{error}}</div><n-button :loading="busy" :disabled="busy || !addValid" @click="addCue">{{tr('ui.audioQueue')}}</n-button></div></n-card></n-modal>
<n-modal v-model:show="applyDialog" :mask-closable="!busy" :close-on-esc="!busy"><n-card :title="tr('ui.audioApply')" style="width:min(700px,95vw)" :bordered="false">
<ul class="audio-pending-list"><li v-for="(a,i) in pending" :key="i">{{tr('ui.audioOp'+a.operation)}} · {{a.bank}}{{a.targetBank && a.targetBank!==a.bank ? ' → '+a.targetBank : ''}} · {{a.name || (a.operation==='replace'?a.waveId:a.cueId)}} <n-button v-if="!busy" size="tiny" @click="pending.splice(i,1)">×</n-button></li></ul>
<n-progress v-if="applying || progress>0" type="line" :percentage="progress" /><p>{{stageText}}</p><div v-if="error" role="alert">{{error}}</div>
<n-button type="primary" :loading="busy" :disabled="busy || !pending.length" @click="apply">{{tr('ui.confirm')}}</n-button></n-card></n-modal>
<div v-if="error" role="alert" style="color:#d03050;flex-shrink:0">{{error}}</div>
<div class="audio-grid-wrapper view-grid-fill">
<ag-grid-vue class="audio-grid ag-fill-grid" :theme="themeQuartz" :column-defs="columns" :default-col-def="defaultColDef" :row-data="rows" :get-row-id="p=>p.data.key" :enable-cell-span="true" :row-height="44" :enable-cell-text-selection="true" :ensure-dom-order="true" :row-selection="{mode:'multiRow',checkboxes:batchMode,headerCheckbox:batchMode,enableClickSelection:false}" @selection-changed="selectionChanged" />
</div>
</div>
</template>
<style>
.audio-view-container{display:flex;flex-direction:column;height:100%;min-height:0;flex:1;overflow:hidden}
.audio-toolbar{display:flex;gap:16px;margin-bottom:10px;align-items:center;justify-content:space-between;flex-wrap:wrap;flex-shrink:0}.audio-actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.audio-player{margin-left:auto;text-align:right}.audio-player audio{display:block;width:320px;height:40px}.audio-pending-list{max-height:300px;overflow:auto}.audio-toolbar .n-input-number{width:140px}.audio-add-form{display:flex;flex-direction:column}
.audio-form-help{margin:0;color:#888;font-size:12px}.audio-field-error{color:#d03050}.audio-loop-fields{padding:12px;border:1px solid #8884;border-radius:6px}.audio-add-form .n-form-item{margin:0}
.audio-grid-wrapper{flex:1;min-height:0;width:100%;height:100%}
.audio-grid{height:100%;width:100%}
.audio-track-cell{display:flex;align-items:center;gap:10px;height:100%}
.audio-action-cell{display:flex;align-items:center;gap:8px;height:100%}
.audio-track-cell span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.audio-play-icon{border:0;background:transparent;color:#2080f0;cursor:pointer;display:inline-flex;align-items:center;justify-content:center;padding:6px;flex-shrink:0}
.audio-play-icon:disabled{opacity:.4;cursor:default}
.audio-play-icon:focus-visible{outline:2px solid #2080f0;border-radius:4px}
</style>
