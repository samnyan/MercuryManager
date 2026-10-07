<script setup lang="ts">
import {ref,watch,computed,onBeforeUnmount} from 'vue'
import {NModal,NCard,NInput,NButton,NImage,NSpace,NFormItem,useMessage} from 'naive-ui'
import {api,useProject} from './project'
import {tr} from './i18n'
const props=defineProps<{show:boolean;base?:string;target?:string}>(),emit=defineEmits(['update:show','created']),project=useProject(),message=useMessage()
const base=ref(''),target=ref(''),step=ref(1),busy=ref(false),image=ref(''),preview=ref(''),width=ref(0),height=ref(0)
const output=computed(()=>`workspace/${project.id}/working/Resources/${target.value}`)
function clean(){if(preview.value)URL.revokeObjectURL(preview.value);preview.value='';image.value=''}
watch(()=>props.show,show=>{if(show){clean();base.value=props.base??'';target.value=props.target??'';step.value=1}})
async function next(){busy.value=true;try{const info=await api<{width:number;height:number;supported:boolean}>(`/projects/${project.id}/resource-info?`+new URLSearchParams({path:base.value}));if(!info.supported)throw new Error(tr('ui.unsupportedTexture'));width.value=info.width;height.value=info.height;step.value=2}catch(e){message.error(String(e))}finally{busy.value=false}}
async function upload(event:Event){clean();const file=(event.target as HTMLInputElement).files?.[0];if(!file)return;try{if(file.size>16*1024*1024)throw new Error(tr('ui.imageTooLarge'));const bitmap=await createImageBitmap(file);if(bitmap.width*bitmap.height>16777216){bitmap.close();throw new Error(tr('ui.imageTooLarge'))}const canvas=document.createElement('canvas');canvas.width=width.value;canvas.height=height.value;canvas.getContext('2d')!.drawImage(bitmap,0,0,width.value,height.value);bitmap.close();const blob=await new Promise<Blob>((resolve,reject)=>canvas.toBlob(b=>b?resolve(b):reject(new Error('Image encoding failed')),'image/png'));preview.value=URL.createObjectURL(blob);const reader=new FileReader();image.value=await new Promise<string>((resolve,reject)=>{reader.onload=()=>resolve(String(reader.result).split(',')[1]!);reader.onerror=reject;reader.readAsDataURL(blob)})}catch(e){message.error(String(e))}}
async function save(){busy.value=true;try{const result=await api<{workspacePath:string}>(`/projects/${project.id}/resources`,'POST',{template:base.value,target:target.value,imageBase64:image.value});await project.status();message.info(result.workspacePath);emit('created');emit('update:show',false);message.success(tr('ui.textureCreated'))}catch(e){message.error(String(e))}finally{busy.value=false}}
onBeforeUnmount(clean)
</script>
<template><n-modal :show="show" :mask-closable="!busy" @update:show="emit('update:show',$event)"><n-card :title="tr('ui.createTexture')" style="width:660px;max-width:95vw" closable @close="emit('update:show',false)"><n-space vertical>
<template v-if="step===1"><n-form-item :label="tr('ui.textureBase')"><n-input v-model:value="base" placeholder="UI/Textures/JACKET/S02/uT_J_S02_240.uasset"/></n-form-item><n-button :loading="busy" :disabled="!base" @click="next">{{tr('ui.nextStep')}}</n-button></template>
<template v-else><div>{{tr('ui.textureSize')}}: {{width}} × {{height}}</div><input type="file" accept="image/png,image/jpeg" @change="upload"/><n-image v-if="preview" :src="preview" width="256"/><n-form-item :label="tr('ui.outputAssetPath')"><n-input v-model:value="target" :disabled="!!props.target"/></n-form-item><div style="overflow-wrap:anywhere">{{output}}</div><n-space><n-button :disabled="busy" @click="step=1">{{tr('ui.previousStep')}}</n-button><n-button :loading="busy" :disabled="!image||!target" @click="save">{{tr('ui.createTexture')}}</n-button></n-space></template>
</n-space></n-card></n-modal></template>
