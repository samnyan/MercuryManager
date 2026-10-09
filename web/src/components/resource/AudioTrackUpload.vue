<script setup lang="ts">
import {ref,watch} from 'vue'
import {NCard,NFormItem,NRadioGroup,NRadio,NSelect,NInputNumber} from 'naive-ui'
import {useProject} from '../../project'
import {tr} from '../../i18n'
export type AudioInfo={format:string;channels:number;sampleRate:number;samples:number;loopEnabled:boolean;loopStart:number;loopEnd:number;encrypted:boolean}
const props=defineProps<{route:string;trackIndex:number;initial?:AudioInfo;existing?:boolean}>()
const emit=defineEmits<{change:[value:{trackIndex:number;uploadId:string;loop?:{waveformIndex:number;loopStart:number;loopEnd:number;loopFlag:number}}|null]}>()
const project=useProject()
type Info=AudioInfo
const info=ref<Info>(),token=ref(''),mode=ref(0),loopMode=ref(0),start=ref<number|null>(0),end=ref<number|null>(0),error=ref(''),loading=ref(false)
watch(loopMode,value=>{if(value===1&&info.value){start.value=0;end.value=info.value.samples}})
watch(()=>props.initial,i=>{info.value=i;token.value='';mode.value=0;loopMode.value=i?.loopEnabled?(i.loopStart===0&&i.loopEnd===i.samples?1:2):0;start.value=i?.loopStart??0;end.value=i?.loopEnd??0;if(i&&props.existing)emit('change',{trackIndex:props.trackIndex,uploadId:''})},{immediate:true})
let generation=0
async function choose(event:Event){const file=(event.target as HTMLInputElement).files?.[0];const g=++generation;emit('change',null);info.value=undefined;token.value='';error.value='';if(!file)return;loading.value=true;try{if(file.size>128*1024*1024)throw new Error(tr('ui.audioUploadLimit'));const r=await fetch(`/api/projects/${project.id}/audio-upload`,{method:'POST',headers:{'X-Mercury-Local':'1','Content-Type':'application/octet-stream'},body:file});const j=await r.json();if(!r.ok||j.code!==0)throw new Error(j.message);if(g!==generation)return;info.value=j.data.info;token.value=j.data.uploadId;mode.value=info.value!.format==='hca'?0:1;loopMode.value=info.value!.loopEnabled?(info.value!.loopStart===0&&info.value!.loopEnd===info.value!.samples?1:2):0;start.value=info.value!.loopStart;end.value=info.value!.loopEnd}catch(e){if(g===generation)error.value=String(e)}finally{if(g===generation)loading.value=false}}
watch([info,token,mode,loopMode,start,end],()=>{const i=info.value;if(!i||!token.value&&!props.existing){emit('change',null);return;}if(i.format==='hca'&&mode.value===0){emit('change',{trackIndex:props.trackIndex,uploadId:token.value});return;}const s=loopMode.value===1?0:start.value,e=loopMode.value===1?i.samples:end.value;if(i.encrypted||loopMode.value!==0&&(s===null||e===null||s<0||e<=s||e>i.samples)){emit('change',null);return;}emit('change',{trackIndex:props.trackIndex,uploadId:token.value,loop:{waveformIndex:0,loopFlag:loopMode.value===0?0:2,loopStart:s??0,loopEnd:e??i.samples}})})
</script>
<template>
<n-card :title="route || 'Track '+trackIndex" size="small" class="audio-track-upload">
<input v-if="!existing" type="file" accept=".hca,.wav" :disabled="loading" @change="choose" />
<p v-if="loading">{{tr('ui.audioReadingFile')}}</p><p v-if="error" role="alert" class="audio-field-error">{{error}}</p>
<template v-if="info">
<p>{{info.format.toUpperCase()}} · {{info.channels}} ch · {{info.sampleRate}} Hz · {{info.samples}} samples</p>
<n-form-item v-if="info.format==='hca'" :label="tr('ui.audioLoopSource')"><n-radio-group v-model:value="mode"><n-radio :value="0">{{tr('ui.audioHcaLoops')}}</n-radio><n-radio :value="1" :disabled="info.encrypted">{{tr('ui.audioModifyLoops')}}</n-radio></n-radio-group></n-form-item>
<p v-if="info.encrypted">{{tr('ui.audioEncryptedLoop')}}</p>
<n-form-item :label="tr('ui.audioLoopMode')"><n-select v-model:value="loopMode" :disabled="info.format==='hca' && mode===0" :options="[{label:tr('ui.audioNoLoop'),value:0},{label:tr('ui.audioWholeLoop'),value:1},{label:tr('ui.audioCustomLoop'),value:2}]" /></n-form-item>
<n-form-item label="LoopStart (samples)"><n-input-number v-model:value="start" :disabled="info.format==='hca' && mode===0 || loopMode!==2" :min="0" :max="info.samples" :precision="0" /></n-form-item>
<n-form-item label="LoopEnd (samples, exclusive)"><n-input-number v-model:value="end" :disabled="info.format==='hca' && mode===0 || loopMode!==2" :min="1" :max="info.samples" :precision="0" /></n-form-item>
<p v-if="loopMode===2 && (start===null || end===null || end<=start)" role="alert">{{tr('ui.audioLoopInvalid')}}</p>
</template>
</n-card>
</template>
<style>.audio-track-upload{margin:10px 0}.audio-track-upload .n-form-item{margin:0}</style>
