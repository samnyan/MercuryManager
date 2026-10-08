<script setup lang="ts">
import { ref, watch, onBeforeUnmount, computed } from 'vue'
import { useRoute } from 'vue-router'
import { NImage, NButton, NSpace, NTabs, NTab, NSpin } from 'naive-ui'
import { api, useProject } from './project'
import { tr } from './i18n'
import TextureCreateDialog from './components/resource/TextureCreateDialog.vue'
import AudioResourceViewer from './components/resource/AudioResourceViewer.vue'
import GenericResourceViewer from './components/resource/GenericResourceViewer.vue'

const route = useRoute()
const project = useProject()

const src = ref('')
const error = ref('')
const textureError = ref('')
const supported = ref(false)
const show = ref(false)
const path = ref('')
const rawUrl = computed(()=>'/api/projects/'+project.id+'/resource-raw?'+new URLSearchParams({path:path.value}))
const mode = ref('generic')
const data = ref<unknown>()
const isTexture = ref(false)
const busy = ref(false)
const isAudio = ref(false)
const jsonInput=ref<HTMLInputElement>()
const jsonBusy=ref(false)
const jsonUrl=computed(()=>'/api/projects/'+project.id+'/resource-json?'+new URLSearchParams({path:path.value}))
async function uploadJson(event:Event){
 const input=event.target as HTMLInputElement,file=input.files?.[0];if(!file)return;
 if(file.size>32*1024*1024){error.value=tr('ui.jsonTooLarge');input.value='';return}
 if(!window.confirm(tr('ui.jsonImportConfirm'))){input.value='';return}
 jsonBusy.value=true;error.value='';
 try{await api(`/projects/${project.id}/resource-json?`+new URLSearchParams({path:path.value}),'POST',{json:await file.text()});await project.status();project.revision++;reload.value++}catch(e){error.value=String(e)}finally{jsonBusy.value=false;input.value=''}
}
const reload=ref(0)

let controller: AbortController | undefined
let generation = 0

function cleanup() {
  controller?.abort()
  if (src.value) URL.revokeObjectURL(src.value)
  src.value = ''
}

watch(
  () => [route.query.path, project.id,reload.value],
  async () => {
    cleanup()
    const request = ++generation
    error.value = ''
    textureError.value = ''
    supported.value = false
    data.value = undefined
    isTexture.value = false
    isAudio.value = false
    mode.value = 'generic'
    path.value = String(route.query.path ?? '')
    if (!path.value || !project.id) return

    busy.value = true
    const current = new AbortController()
    controller = current

    try {
      if(path.value.endsWith('.awb')){isAudio.value=true;mode.value='awb';return}
      const result = await api<{ isTexture: boolean; data: unknown }>(
        `/projects/${project.id}/resource-data?` + new URLSearchParams({ path: path.value })
      )
      if (request !== generation) return
      data.value = result.data
      isAudio.value = (result.data as any)?.Exports?.some((e:any)=>['SoundAtomCueSheet','SoundAtomCue'].includes(e.Class)) ?? false
      if(isAudio.value)mode.value='awb'
      isTexture.value = result.isTexture
      if (!isTexture.value) return
      mode.value = 'texture'

      try {
        const response = await fetch(
          `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: path.value }),
          { signal: current.signal }
        )
        if (!response.ok) throw new Error((await response.json()).message)
        const blob = await response.blob()
        if (request !== generation) return
        src.value = URL.createObjectURL(blob)
        const info = await api<{ supported: boolean }>(
          `/projects/${project.id}/resource-info?` + new URLSearchParams({ path: path.value })
        )
        if (request === generation) supported.value = info.supported
      } catch (e) {
        if (request === generation) textureError.value = String(e)
      }
    } catch (e) {
      if (request === generation) error.value = String(e)
    } finally {
      if (request === generation) busy.value = false
    }
  },
  { immediate: true }
)

onBeforeUnmount(() => {
  generation++
  cleanup()
})
</script>

<template>
  <div class="resource-view-container">
    <div class="resource-title-row">
      <h2 class="page-title resource-path-title">{{ path || tr('ui.selectResource') }}</h2>
      <n-space v-if="project.id && path.endsWith('.uasset')" :wrap="false">
        <n-button tag="a" :href="jsonUrl" download :disabled="busy || jsonBusy">{{tr('ui.jsonExport')}}</n-button>
        <n-button :loading="jsonBusy" :disabled="busy" @click="jsonInput?.click()">{{tr('ui.jsonImport')}}</n-button>
      </n-space>
      <input ref="jsonInput" type="file" accept=".json,application/json" hidden @change="uploadJson" />
    </div>

    <n-tabs v-if="data || isAudio" v-model:value="mode" type="line" class="resource-tabs">
      <n-tab name="texture" :disabled="!isTexture">
        {{ tr('ui.textureMode') }}
      </n-tab>
      <n-tab name="awb" :disabled="!isAudio">AWB</n-tab>
      <n-tab name="generic" :disabled="!data">
        {{ tr('ui.genericMode') }}
      </n-tab>
    </n-tabs>

    <audio-resource-viewer v-if="isAudio && !busy && mode === 'awb'" :path="path" />
    <n-spin :show="busy">
      <div v-if="error" class="error-banner">{{ error }}</div>

      <generic-resource-viewer
        v-if="data && mode === 'generic'"
        :data="data"
        :preview-src="src"
        :preview-error="textureError"
        :raw-url="rawUrl"
      />

      <div v-if="mode === 'texture'" class="texture-pane">
        <n-image
          v-if="src"
          :src="src"
          width="512"
          class="texture-img"
        />
        <div v-if="textureError" class="error-banner">{{ textureError }}</div>
        <n-button
          v-if="src"
          type="primary"
          :disabled="!supported"
          @click="show = true"
        >
          {{ tr('ui.useTextureBase') }}
        </n-button>
        <div v-if="src && !supported" class="unsupported-hint">
          {{ tr('ui.unsupportedTexture') }}
        </div>
      </div>
    </n-spin>

    <texture-create-dialog
      v-model:show="show"
      :base="path"
      @created="project.revision++"
    />
  </div>
</template>

<style scoped>
.resource-title-row {display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap}
.resource-title-row h2 {flex:1;min-width:0}
.resource-view-container {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.resource-path-title {
  word-break: break-all;
}
.resource-tabs {
  margin-bottom: 4px;
}
.error-banner {
  color: #d03050;
  margin-bottom: 8px;
}
.texture-pane {
  display: flex;
  flex-direction: column;
  gap: 12px;
  align-items: flex-start;
}
.texture-img {
  max-width: 100%;
  border-radius: 4px;
  border: 1px solid #eee;
}
.unsupported-hint {
  font-size: 12px;
  color: #888;
}
</style>
