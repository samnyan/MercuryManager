<script setup lang="ts">
import {ref,watch,onBeforeUnmount} from 'vue'
import {useRoute} from 'vue-router'
import {NImage,NButton,NSpace} from 'naive-ui'
import {api,useProject} from './project'
import {tr} from './i18n'
import TextureCreateDialog from './TextureCreateDialog.vue'
const route=useRoute(),project=useProject(),src=ref(''),error=ref(''),supported=ref(false),show=ref(false),path=ref('')
let controller:AbortController|undefined
function cleanup(){controller?.abort();if(src.value)URL.revokeObjectURL(src.value);src.value=''}
watch(()=>[route.query.path,project.id],async()=>{cleanup();error.value='';supported.value=false;path.value=String(route.query.path??'');if(!path.value||!project.id)return;const current=new AbortController();controller=current;try{const response=await fetch(`/api/projects/${project.id}/resource-image?`+new URLSearchParams({path:path.value}),{signal:current.signal});if(!response.ok)throw new Error((await response.json()).message);const blob=await response.blob();if(current.signal.aborted)return;src.value=URL.createObjectURL(blob);const info=await api<{supported:boolean}>(`/projects/${project.id}/resource-info?`+new URLSearchParams({path:path.value}));if(!current.signal.aborted)supported.value=info.supported}catch(e){if(!current.signal.aborted)error.value=String(e)}},{immediate:true})
onBeforeUnmount(cleanup)
</script>
<template><n-space vertical><h2 class="page-title">{{path||tr('ui.selectResource')}}</h2><n-image v-if="src" :src="src" width="512" style="max-width:100%"/><div v-if="error">{{error}}</div><n-button v-if="src" :disabled="!supported" @click="show=true">{{tr('ui.useTextureBase')}}</n-button><div v-if="src&&!supported">{{tr('ui.unsupportedTexture')}}</div><texture-create-dialog v-model:show="show" :base="path" @created="project.revision++"/></n-space></template>
