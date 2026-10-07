<script setup lang="ts">
import {ref,watch,onBeforeUnmount} from 'vue'
import {NImage} from 'naive-ui'
import {useProject} from './project'
import {tr} from './i18n'
const props=defineProps<{table:string;field:string;value:unknown}>(),project=useProject(),src=ref(''),state=ref(''),error=ref('')
let timer:ReturnType<typeof setTimeout>|undefined,controller:AbortController|undefined
function cleanup(){clearTimeout(timer);controller?.abort();if(src.value)URL.revokeObjectURL(src.value);src.value=''}
watch(()=>[props.value,props.field,props.table,project.id,project.revision],()=>{
 cleanup();error.value='';const value=String(props.value??'').trim();if(!value||!project.id){state.value='';return}state.value='loading'
 timer=setTimeout(async()=>{const current=new AbortController();controller=current;try{
 const query=new URLSearchParams({table:props.table,field:props.field,value});const response=await fetch(`/api/projects/${project.id}/texture?${query}`,{signal:current.signal});
 if(!response.ok){const result=await response.json();throw new Error(result.message)}const blob=await response.blob();if(current.signal.aborted)return;src.value=URL.createObjectURL(blob);state.value='found'
 }catch(e){if(current.signal.aborted)return;state.value='error';error.value=String(e)}},300)
},{immediate:true})
onBeforeUnmount(cleanup)
</script>
<template><div v-if="state" style="margin-top:8px"><span v-if="state==='loading'">{{tr('ui.textureLoading')}}</span><n-image v-else-if="src" :src="src" width="128" style="max-height:160px" object-fit="contain"/><span v-else :title="error">{{tr('ui.textureUnavailable')}}</span></div></template>
