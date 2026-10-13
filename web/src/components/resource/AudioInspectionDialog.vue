<script setup lang="ts">
import {ref,computed,watch} from 'vue'
import {NButton,NModal,NCard,NTag,NCheckbox,NCheckboxGroup,NInputNumber,NInput,NTable,NCollapse,NCollapseItem,NPopconfirm} from 'naive-ui'
import {api,useProject} from '../../project'
import {tr} from '../../i18n'
const props=defineProps<{path:string;source:'game'|'project';disabled:boolean}>()
const emit=defineEmits<{working:[value:boolean];repaired:[]}>()
const project=useProject()
type Issue={code:string;severity:string;table:string;row:number|null;message:string;repair:string|null;port:number|null}
type Report={revision:string;scope:string;deep:boolean;checksPassed:boolean;issues:Issue[];repairs:{id:string;description:string;recommended:boolean;requiresRecovery:boolean;damagedIds:number[]|null}[];streams:{port:number;name:string;available:boolean;entries:number|null;cachedHeaderBytes:number;headerMatches:boolean|null;fileBytes:string|null}[];cueNames:{row:number;name:string;cueIndex:number;lookupMatches:boolean}[];work:{ports:number;bytes:number;alignmentLoss:number;requiredBytes:number;recommendedBytes:number}|null;numSlots:number|null}
const show=ref(false),working=ref(false),error=ref(''),report=ref<Report>(),selected=ref<string[]>([]),workBytes=ref<number|null>(null),search=ref(''),lastFiles=ref<string[]>([])
const recoveryBanks=ref<Record<string,string>>({})
const recoveryActions=computed(()=>report.value?.repairs.filter(a=>a.requiresRecovery&&selected.value.includes(a.id))??[])
const recoveryMissing=computed(()=>recoveryActions.value.some(a=>!recoveryBanks.value[a.id]?.trim()))
let generation=0
watch(()=>[props.path,props.source,project.id],()=>{generation++;show.value=false;report.value=undefined;selected.value=[];lastFiles.value=[];recoveryBanks.value={}})
const query=()=>new URLSearchParams({path:props.path,source:props.source})
const visibleNames=computed(()=>report.value?.cueNames.filter(n=>n.name.toLowerCase().includes(search.value.toLowerCase()))??[])
function actionLabel(id:string){const [key,port]=id.split(':');return `${tr('ui.inspectionAction'+key)}${port!==undefined?' · port '+port:''}`}
async function inspect(deep=false){const g=++generation;show.value=true;working.value=true;emit('working',true);error.value='';try{const q=query();q.set('deep',String(deep));const r=await api<Report>(`/projects/${project.id}/audio-inspection?${q}`);if(g===generation){report.value=r;selected.value=r.repairs.filter(a=>a.recommended).map(a=>a.id);workBytes.value=null;lastFiles.value=[];recoveryBanks.value={}}}catch(e){if(g===generation)error.value=String(e)}finally{working.value=false;emit('working',false)}}
async function repair(){if(!report.value||!selected.value.length||working.value||recoveryMissing.value)return;const g=++generation;working.value=true;emit('working',true);error.value='';try{const result=await api<{after:Report;changedFiles:string[]}>(`/projects/${project.id}/audio-repair?${query()}`,'POST',{revision:report.value.revision,actions:selected.value,workBytes:selected.value.includes('rebuild-stream-work')?workBytes.value:null,recoveryBanks:Object.fromEntries(recoveryActions.value.map(a=>[a.id,recoveryBanks.value[a.id]!.trim()]))});if(g===generation){report.value=result.after;lastFiles.value=result.changedFiles;selected.value=[];recoveryBanks.value={};emit('repaired')}}catch(e){if(g===generation)error.value=String(e)}finally{working.value=false;emit('working',false)}}
function saveReport(){if(!report.value)return;const url=URL.createObjectURL(new Blob([JSON.stringify(report.value,null,2)],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='audio-inspection.json';a.click();URL.revokeObjectURL(url)}
</script>
<template>
<n-button size="small" :disabled="disabled || working" @click="inspect()">{{tr('ui.audioInspection')}}</n-button>
<n-modal :show="show" :mask-closable="!working" :close-on-esc="!working" @update:show="show=$event">
<n-card :title="tr('ui.audioInspection')" closable :on-close="()=>{if(!working)show=false}" style="width:min(1050px,96vw)" :bordered="false">
<div class="audio-inspection-body">
<p>{{tr('ui.inspectionScope')}}</p>
<div class="inspection-buttons"><n-button :loading="working" :disabled="working" @click="inspect(false)">{{tr('ui.inspectionFast')}}</n-button><n-button :disabled="working" @click="inspect(true)">{{tr('ui.inspectionDeep')}}</n-button><n-button :disabled="!report || working" @click="saveReport">{{tr('ui.inspectionExport')}}</n-button></div>
<p v-if="error" role="alert" class="inspection-error">{{error}}</p>
<template v-if="report">
<p><n-tag :type="report.checksPassed?'success':'error'">{{tr(report.checksPassed?'ui.inspectionPassed':'ui.inspectionBlocked')}}</n-tag> · {{tr(report.deep?'ui.inspectionDeep':'ui.inspectionFast')}} · NumSlots={{report.numSlots??'—'}}</p>
<p v-if="lastFiles.length">{{tr('ui.inspectionDraftWritten')}}: {{lastFiles.join(', ')}}</p>
<ul class="inspection-issues"><li v-for="(issue,i) in report.issues" :key="i"><n-tag size="small" :type="issue.severity==='error'?'error':'warning'">{{issue.severity}}</n-tag> <strong>{{issue.code}}</strong> · {{issue.table}}{{issue.row===null?'':' ['+issue.row+']'}}<p>{{issue.message}}</p></li></ul>
<n-collapse :default-expanded-names="['work','streams','repair']">
<n-collapse-item v-if="report.work" name="work" :title="tr('ui.inspectionWork')">
<p>{{report.work.ports}} ports · {{tr('ui.inspectionCurrentBytes')}} {{report.work.bytes}} · {{tr('ui.inspectionAlignment')}} {{report.work.alignmentLoss}} · {{tr('ui.inspectionRequired')}} {{report.work.requiredBytes}} · {{tr('ui.inspectionRecommended')}} {{report.work.recommendedBytes}}</p>
<p>{{tr('ui.inspectionWorkHelp')}}</p><n-input-number v-model:value="workBytes" :disabled="working" :min="report.work.requiredBytes+7" :max="16*1024*1024" :precision="0" :placeholder="String(report.work.recommendedBytes)" />
</n-collapse-item>
<n-collapse-item name="streams" :title="tr('ui.inspectionStreams')"><n-table size="small" :single-line="false"><thead><tr><th>Port</th><th>Name</th><th>AWB entries</th><th>Cached header bytes</th><th>{{tr('ui.inspectionMatch')}}</th></tr></thead><tbody><tr v-for="bank in report.streams" :key="bank.port"><td>{{bank.port}}</td><td>{{bank.name}}</td><td>{{bank.available?bank.entries:tr('ui.inspectionMissing')}}</td><td>{{bank.cachedHeaderBytes}}</td><td>{{bank.headerMatches===null?'—':String(bank.headerMatches)}}</td></tr></tbody></n-table></n-collapse-item>
<n-collapse-item name="names" :title="'CueName / CueIndex ('+report.cueNames.length+')'"><p>{{tr('ui.inspectionNamesHelp')}}</p><n-input v-model:value="search" :placeholder="tr('ui.inspectionSearch')" /><div class="inspection-names"><n-table size="small"><thead><tr><th>Row</th><th>CueName</th><th>CueIndex</th><th>Lookup</th></tr></thead><tbody><tr v-for="name in visibleNames" :key="name.row"><td>{{name.row}}</td><td>{{name.name}}</td><td>{{name.cueIndex}}</td><td>{{name.lookupMatches}}</td></tr></tbody></n-table></div></n-collapse-item>
<n-collapse-item name="repair" :title="tr('ui.inspectionRepairs')"><p>{{tr('ui.inspectionRepairHelp')}}</p><n-checkbox-group v-model:value="selected"><div class="inspection-repairs"><n-checkbox v-for="action in report.repairs" :key="action.id" :value="action.id" :disabled="working"><span :title="action.description">{{actionLabel(action.id)}}</span></n-checkbox></div></n-checkbox-group><div v-for="action in recoveryActions" :key="action.id" class="inspection-recovery"><p>{{actionLabel(action.id)}} · {{tr('ui.inspectionRecoveryIds')}}: {{action.damagedIds?.join(', ')}}</p><p>{{tr('ui.inspectionRecoveryHelp')}}</p><n-input v-model:value="recoveryBanks[action.id]" :disabled="working" :placeholder="tr('ui.inspectionRecoveryPath')" /></div><n-popconfirm @positive-click="repair"><template #trigger><n-button type="primary" :loading="working" :disabled="working || !selected.length || recoveryMissing">{{tr('ui.inspectionRepairSelected')}}</n-button></template>{{tr('ui.inspectionConfirm')}}</n-popconfirm></n-collapse-item>
</n-collapse>
</template>
</div>
</n-card>
</n-modal>
</template>
<style scoped>
.audio-inspection-body{max-height:calc(88dvh - 110px);overflow:auto;padding-right:8px}.inspection-buttons{display:flex;gap:8px;flex-wrap:wrap}.inspection-issues{padding-left:20px}.inspection-issues li{margin:12px 0}.inspection-issues p{margin:4px 0;overflow-wrap:anywhere}.inspection-error{color:#d03050}.inspection-repairs{display:flex;flex-direction:column;gap:8px;margin-bottom:16px}.inspection-names{max-height:320px;overflow:auto;margin-top:10px}
</style>
