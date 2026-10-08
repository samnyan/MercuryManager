<script setup lang="ts">
import {ref,computed,watch,h,onBeforeUnmount,nextTick} from 'vue'
import {NDataTable,NInput,NButton} from 'naive-ui'
import {api,useProject} from '../../project'
import {tr} from '../../i18n'
const props=defineProps<{path:string}>()
const project=useProject()
type Cue={index:number;name:string;cueId:number;waveIds:number[];error:string|null;tracks:{waveId:number;route:string;bank:string;trackIndex:number;channels:number;sampleRate:number}[]}
const cues=ref<Cue[]>([]),search=ref(''),error=ref(''),src=ref(''),selected=ref('')
const player=ref<HTMLAudioElement>()
let generation=0
function stop(){player.value?.pause();src.value='';player.value?.removeAttribute('src');player.value?.load()}
watch(()=>[props.path,project.id],async()=>{const g=++generation;stop();cues.value=[];error.value='';try{const result=await api<Cue[]>(`/projects/${project.id}/resource-cues?`+new URLSearchParams({path:props.path}));if(g===generation)cues.value=result}catch(e){if(g===generation)error.value=String(e)}},{immediate:true})
async function play(row:Cue,part:number){stop();error.value='';selected.value=row.name;src.value=`/api/projects/${project.id}/resource-audio?`+new URLSearchParams({path:props.path,index:String(row.index),part:String(part)});await nextTick();try{await player.value?.play()}catch(e){error.value=String(e)}}
const rows=computed(()=>cues.value.filter(c=>`${c.name} ${c.cueId} ${c.waveIds}`.toLowerCase().includes(search.value.toLowerCase())))
const columns=computed(()=>[
{title:'Index',key:'index',width:80},{title:'Cue',key:'name',minWidth:280},{title:'Cue ID',key:'cueId',width:100},{title:'Wave ID',key:'waveIds',width:180,render:(r:Cue)=>r.waveIds.join(', ')},
{title:'Track / Bus / Bank',key:'tracks',width:650,render:(r:Cue)=>h('div',r.tracks.map(t=>h('div',`Track ${t.trackIndex} · Wave ${t.waveId} · ${t.route || 'Unknown'} · ${t.bank} · ${t.channels}ch / ${t.sampleRate}Hz`)))},
{title:tr('ui.audioPreview'),key:'play',width:300,render:(r:Cue)=>r.error?h('span',r.error):h('div',{style:'display:flex;gap:8px;flex-wrap:wrap'},r.waveIds.map((id,i)=>h(NButton,{size:'small',onClick:()=>play(r,i)},()=>`${tr('ui.audioPlay')} ${id} ${r.tracks[i]?.route || ''}`)))}])
onBeforeUnmount(()=>{generation++;stop()})
</script>
<template>
<div>
<n-input v-model:value="search" :placeholder="tr('ui.audioSearch')" clearable />
<div style="margin:12px 0"><span>{{selected}}</span><audio ref="player" :src="src || undefined" controls preload="none" style="display:block;width:min(100%,600px)" @error="error=tr('ui.audioFailed')" /></div>
<div v-if="error" style="color:#d03050">{{error}}</div>
<n-data-table :columns="columns" :data="rows" :row-key="(r:Cue)=>r.index" :pagination="{pageSize:50}" :scroll-x="1610" />
</div>
</template>
