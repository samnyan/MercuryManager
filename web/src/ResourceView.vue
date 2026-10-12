<script setup lang="ts">
import { ref, watch, onBeforeUnmount, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NImage, NButton, NSpace, NTabs, NTab, NSpin, NSelect, NCard, NIcon, NModal } from 'naive-ui'
import { ArrowLeft } from '@lucide/vue'
import { api, useProject } from './project'
import { tr } from './i18n'
import TextureCreateDialog from './components/resource/TextureCreateDialog.vue'
import AudioResourceViewer from './components/resource/AudioResourceViewer.vue'
import GenericResourceViewer from './components/resource/GenericResourceViewer.vue'
import BlueprintViewer from './components/resource/BlueprintViewer.vue'
import MerResourceViewer from './components/resource/MerResourceViewer.vue'
import MaterialResourceViewer from './components/resource/MaterialResourceViewer.vue'

const router = useRouter()
const route = useRoute()
const project = useProject()

const src = ref('')
const selectedMip = ref<number>(0)
const mipCount = ref<number>(1)
const error = ref('')
const textureError = ref('')
const supported = ref(false)
const show = ref(false)
const path = ref('')
const rawUrl = computed(()=>'/api/projects/'+project.id+'/resource-raw?'+new URLSearchParams({path:path.value}))
const mode = ref('generic')
const data = ref<any>()
const isTexture = ref(false)
const busy = ref(false)
const isAudio = ref(false)
const isBlueprint = ref(false)
const isMer = ref(false)
const blueprint = ref<any>()
watch(mode,async value=>{if(value==='generic'&&isBlueprint.value&&!data.value){const request=generation;busy.value=true;try{const result=await api<{data:any}>(`/projects/${project.id}/resource-data?`+new URLSearchParams({path:path.value}));if(request===generation)data.value=result.data}catch(e){if(request===generation)error.value=String(e)}finally{if(request===generation)busy.value=false}}})
const isMaterialInstance = ref(false)
const isMaterial = ref(false)
const spriteSrc = ref('')
const spriteError = ref('')
const showTexModal = ref(false)
const modalTexSrc = ref('')
const modalTexTitle = ref('')
const modalTexLoading = ref(false)

async function openExternalTexPreview(targetPath: string, label: string) {
  if (!project.id || !targetPath) return
  showTexModal.value = true
  modalTexTitle.value = `${label} (${targetPath})`
  modalTexLoading.value = true
  if (modalTexSrc.value) {
    URL.revokeObjectURL(modalTexSrc.value)
    modalTexSrc.value = ''
  }
  try {
    const res = await fetch(`/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: targetPath, mip: '0' }))
    if (!res.ok) throw new Error((await res.json()).message)
    const blob = await res.blob()
    modalTexSrc.value = URL.createObjectURL(blob)
  } catch (e) {
    error.value = String(e)
  } finally {
    modalTexLoading.value = false
  }
}
const jsonInput=ref<HTMLInputElement>()
const jsonBusy=ref(false)
const jsonUrl=computed(()=>'/api/projects/'+project.id+'/resource-json?'+new URLSearchParams({path:path.value}))

const mipOptions = computed(() => {
  const options = []
  let w = data.value?.Exports?.[0]?.CustomSerialization?.SizeX || 0
  let h = data.value?.Exports?.[0]?.CustomSerialization?.SizeY || 0
  for (let i = 0; i < mipCount.value; i++) {
    options.push({
      label: `Mip ${i} (${w || '?'} × ${h || '?'})`,
      value: i
    })
    w = Math.max(1, Math.floor(w / 2))
    h = Math.max(1, Math.floor(h / 2))
  }
  return options
})

async function updateMipImage(mip: number) {
  if (!path.value || !project.id) return
  try {
    const res = await fetch(`/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: path.value, mip: String(mip) }))
    if (!res.ok) throw new Error((await res.json()).message)
    const blob = await res.blob()
    if (src.value) URL.revokeObjectURL(src.value)
    src.value = URL.createObjectURL(blob)
  } catch (e) {
    textureError.value = String(e)
  }
}

watch(selectedMip, (mip) => {
  if (isTexture.value) {
    updateMipImage(mip)
  }
})
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
  if (spriteSrc.value) URL.revokeObjectURL(spriteSrc.value)
  src.value = ''
  spriteSrc.value = ''
}

const miInfo = computed(() => data.value?.MaterialInstanceInfo)

const historyStack = ref<string[]>(
  route.query.from === 'link'
    ? JSON.parse(sessionStorage.getItem('mercury-resource-history') || '[]')
    : []
)
if (route.query.from !== 'link') {
  sessionStorage.removeItem('mercury-resource-history')
}

function jumpToResource(targetPath: string) {
  if (!targetPath || targetPath === path.value) return
  historyStack.value.push(path.value)
  sessionStorage.setItem('mercury-resource-history', JSON.stringify(historyStack.value))
  router.push({ path: '/resource', query: { path: targetPath, from: 'link' } })
}

function goBackResource() {
  const prev = historyStack.value.pop()
  sessionStorage.setItem('mercury-resource-history', JSON.stringify(historyStack.value))
  if (prev) {
    router.push({
      path: '/resource',
      query: historyStack.value.length > 0 ? { path: prev, from: 'link' } : { path: prev }
    })
  }
}

watch(
  () => [route.query.path, project.id,reload.value],
  async () => {
    cleanup()
    const request = ++generation
    error.value = ''
    textureError.value = ''
    spriteError.value = ''
    supported.value = false
    data.value = undefined
    isTexture.value = false
    isAudio.value = false
    isBlueprint.value = false
    isMer.value = false
    blueprint.value = undefined
    isMaterialInstance.value = false
    isMaterial.value = false
    selectedMip.value = 0
    mipCount.value = 1
    mode.value = 'generic'
    path.value = String(route.query.path ?? '')
    if (!path.value || !project.id) return

    busy.value = true
    const current = new AbortController()
    controller = current

    try {
      if(path.value.endsWith('.mer')){isMer.value=true;mode.value='mer';busy.value=false;return}
      if(path.value.endsWith('.awb')){isAudio.value=true;mode.value='awb';busy.value=false;return}
      if(/(^|\/)BP_[^/]+\.uasset$/i.test(path.value)){
        const summary=await api<any>(`/projects/${project.id}/resource-blueprint?`+new URLSearchParams({path:path.value}));
        if(request!==generation)return;
        blueprint.value=summary;isBlueprint.value=true;mode.value='blueprint';return;
      }
      const result = await api<{ isTexture: boolean; data: any }>(
        `/projects/${project.id}/resource-data?` + new URLSearchParams({ path: path.value })
      )
      if (request !== generation) return
      data.value = result.data
      isAudio.value = (result.data as any)?.Exports?.some((e:any)=>['SoundAtomCueSheet','SoundAtomCue'].includes(e.Class)) ?? false
      if(isAudio.value)mode.value='awb'
      isTexture.value = result.isTexture
      isMaterialInstance.value = !!result.data?.MaterialInstanceInfo
      isMaterial.value = !!result.data?.MaterialInfo

      if (isMaterial.value) {
        mode.value = 'material'
      }

      if (isMaterialInstance.value) {
        mode.value = 'sprite'
        const mi = result.data.MaterialInstanceInfo
        const targetTex = mi.Textures?.tex || mi.Textures?.Texture
        if (targetTex) {
          try {
            const texInfo = await api<{ width: number; height: number }>(
              `/projects/${project.id}/resource-info?` + new URLSearchParams({ path: targetTex })
            )
            const u0 = mi.Scalars?.U0 ?? 0
            const v0 = mi.Scalars?.V0 ?? 0
            // WACCA CMbtn_icn_UV 网格通常为 8列 x 4行 (1024x512, 单格 128x128)
            const cropW = 128
            const cropH = 128
            const cropX = Math.round(u0 * texInfo.width)
            const cropY = Math.round(v0 * texInfo.height)

            const cropRes = await fetch(
              `/api/projects/${project.id}/resource-image-crop?` +
                new URLSearchParams({
                  path: targetTex,
                  x: String(cropX),
                  y: String(cropY),
                  w: String(cropW),
                  h: String(cropH)
                }),
              { signal: current.signal }
            )
            if (!cropRes.ok) throw new Error((await cropRes.json()).message)
            const cropBlob = await cropRes.blob()
            if (request !== generation) return
            spriteSrc.value = URL.createObjectURL(cropBlob)
          } catch (e) {
            if (request === generation) spriteError.value = String(e)
          }
        }
      }

      if (!isTexture.value) return
      mode.value = 'texture'

      try {
        const info = await api<{ width: number; height: number; mips?: number; supported: boolean }>(
          `/projects/${project.id}/resource-info?` + new URLSearchParams({ path: path.value })
        )
        if (request === generation) {
          supported.value = info.supported
          mipCount.value = info.mips || 1
        }
        const response = await fetch(
          `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: path.value, mip: '0' }),
          { signal: current.signal }
        )
        if (!response.ok) throw new Error((await response.json()).message)
        const blob = await response.blob()
        if (request !== generation) return
        src.value = URL.createObjectURL(blob)
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
  <div class="resource-view-container view-fill-container">
    <div class="resource-title-row view-header-fixed">
      <div class="title-with-back">
        <n-button
          v-if="historyStack.length > 0"
          size="small"
          secondary
          class="back-history-btn"
          title="返回上一级资产"
          @click="goBackResource"
        >
          <template #icon>
            <n-icon :component="ArrowLeft" />
          </template>
          返回
        </n-button>
        <h2 class="page-title resource-path-title">{{ path || tr('ui.selectResource') }}</h2>
      </div>
      <n-space v-if="project.id && path.endsWith('.uasset')" :wrap="false">
        <n-button tag="a" :href="jsonUrl" download :disabled="busy || jsonBusy">{{tr('ui.jsonExport')}}</n-button>
        <n-button :loading="jsonBusy" :disabled="busy" @click="jsonInput?.click()">{{tr('ui.jsonImport')}}</n-button>
      </n-space>
      <input ref="jsonInput" type="file" accept=".json,application/json" hidden @change="uploadJson" />
    </div>

    <!-- 仅在存在对应功能时才显示可选 Tab，且不显示不兼容的 Tab -->
    <n-tabs v-if="((isMaterial || isMaterialInstance || isTexture || isAudio) && data) || isBlueprint" v-model:value="mode" type="line" class="resource-tabs view-header-fixed">
      <n-tab v-if="isBlueprint" name="blueprint">Blueprint / Widget Tree</n-tab>
      <n-tab v-if="isMaterial" name="material">材质属性 (Material)</n-tab>
      <n-tab v-if="isMaterialInstance" name="sprite">Sprite 预览</n-tab>
      <n-tab v-if="isTexture" name="texture">
        {{ tr('ui.textureMode') }}
      </n-tab>
      <n-tab v-if="isAudio" name="awb">AWB</n-tab>
      <n-tab name="generic">
        {{ tr('ui.genericMode') }}
      </n-tab>
    </n-tabs>

    <div class="resource-main-pane view-grid-fill">
      <mer-resource-viewer v-if="isMer && !busy" :path="path" class="resource-fill-item" />
      <audio-resource-viewer v-else-if="isAudio && !busy && mode === 'awb'" :path="path" class="resource-fill-item" />
      <div v-else-if="busy" class="resource-loading-pane">
        <n-spin size="large" />
      </div>
      <div v-else class="resource-fill-item">
        <div v-if="error" class="error-banner">{{ error }}</div>

        <blueprint-viewer v-if="isBlueprint && blueprint && mode === 'blueprint'" :path="path" :summary="blueprint" @navigate-path="jumpToResource" />
        <material-resource-viewer
          v-if="data?.MaterialInfo && mode === 'material'"
          :info="data.MaterialInfo"
          :path="path"
          @navigate-path="jumpToResource"
          @preview-texture="openExternalTexPreview"
        />
        <generic-resource-viewer
          v-if="data && mode === 'generic'"
          :data="data"
          :path="path"
          :preview-src="src"
          :preview-error="textureError"
          :raw-url="rawUrl"
          @navigate-path="jumpToResource"
        />

        <!-- Material Instance Sprite 裁切预览面板 -->
        <div v-if="mode === 'sprite' && isMaterialInstance" class="sprite-pane">
          <div v-if="spriteSrc" class="sprite-preview-box">
            <n-image :src="spriteSrc" width="128" height="128" class="sprite-img" />
          </div>
          <div v-else-if="spriteError" class="error-banner">{{ spriteError }}</div>
          <div v-else class="sprite-placeholder">
            <span>未关联贴图或无法解析图集参数</span>
          </div>

          <n-card title="图集关联信息" size="small" class="mi-meta-card">
            <div class="mi-meta-row">
              <span class="mi-meta-label">母版材质 (Parent):</span>
              <a v-if="miInfo?.ParentPackage" href="javascript:void(0)" class="resource-link" @click="jumpToResource(miInfo.ParentPackage)">
                {{ miInfo.Parent }} ({{ miInfo.ParentPackage }})
              </a>
              <span v-else>{{ miInfo?.Parent || '—' }}</span>
            </div>
            <div class="mi-meta-row">
              <span class="mi-meta-label">底图资产 (Texture):</span>
              <a v-if="miInfo?.Textures?.tex || miInfo?.Textures?.Texture" href="javascript:void(0)" class="resource-link" @click="jumpToResource(miInfo.Textures.tex || miInfo.Textures.Texture)">
                {{ miInfo.Textures.tex || miInfo.Textures.Texture }}
              </a>
              <span v-else>—</span>
            </div>
            <div class="mi-meta-row">
              <span class="mi-meta-label">UV 参数:</span>
              <code>U0: {{ miInfo?.Scalars?.U0 ?? 0 }}, V0: {{ miInfo?.Scalars?.V0 ?? 0 }}</code>
            </div>
          </n-card>
        </div>

        <div v-if="mode === 'texture'" class="texture-pane">
          <div class="texture-controls">
            <div v-if="mipCount > 1" class="mip-selector-box">
              <span style="font-size: 13px; font-weight: 500">查看 Mip 层级:</span>
              <n-select v-model:value="selectedMip" :options="mipOptions" size="small" style="width: 220px" />
            </div>
            <n-button
              v-if="src"
              type="primary"
              size="small"
              :disabled="!supported"
              @click="show = true"
            >
              {{ tr('ui.useTextureBase') }}
            </n-button>
          </div>

          <n-image
            v-if="src"
            :src="src"
            width="512"
            class="texture-img"
          />
          <div v-if="textureError" class="error-banner">{{ textureError }}</div>
          <div v-if="src && !supported" class="unsupported-hint">
            {{ tr('ui.unsupportedTexture') }}
          </div>
        </div>
      </div>
    </div>

    <texture-create-dialog
      v-model:show="show"
      :base="path"
      @created="project.revision++"
    />

    <n-modal v-model:show="showTexModal" preset="card" :title="modalTexTitle" style="width: 600px; max-width: 90vw">
      <div style="display: flex; justify-content: center; align-items: center; min-height: 200px; background: #18181c; padding: 16px; border-radius: 6px">
        <n-spin v-if="modalTexLoading" />
        <n-image v-else-if="modalTexSrc" :src="modalTexSrc" style="max-width: 100%; max-height: 500px; image-rendering: pixelated" />
      </div>
    </n-modal>
  </div>
</template>

<style scoped>
.resource-title-row {display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap}
.title-with-back {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  flex: 1;
  min-width: 0;
}
.back-history-btn {
  font-weight: 500;
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
}
.resource-title-row h2 {margin:0;flex:1;min-width:0;display:flex;align-items:center}
.resource-view-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  flex: 1;
  gap: 10px;
}
.resource-path-title {
  word-break: break-all;
}
.resource-tabs {
  margin-bottom: 2px;
}
.resource-main-pane {
  flex: 1;
  min-height: 0;
  height: 100%;
  display: flex;
  flex-direction: column;
  position: relative;
  overflow: hidden;
}
.resource-loading-pane {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
}
.resource-fill-item {
  flex: 1;
  min-height: 0;
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.error-banner {
  color: #d03050;
  margin-bottom: 8px;
  flex-shrink: 0;
}
.texture-pane {
  display: flex;
  flex-direction: column;
  gap: 12px;
  align-items: flex-start;
  overflow-y: auto;
  flex: 1;
}
.texture-controls {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}
.mip-selector-box {
  display: flex;
  align-items: center;
  gap: 8px;
}
.sprite-pane {
  display: flex;
  flex-direction: column;
  gap: 16px;
  align-items: flex-start;
  overflow-y: auto;
  flex: 1;
  padding: 8px 4px;
}
.sprite-preview-box {
  padding: 16px;
  background-color: #f5f6f9;
  border: 1px dashed #c0c4cc;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.sprite-img {
  border-radius: 4px;
  image-rendering: pixelated;
}
.sprite-placeholder {
  padding: 24px;
  color: #888;
  font-size: 13px;
  background-color: #fafafc;
  border: 1px dashed #e0e0e6;
  border-radius: 6px;
}
.mi-meta-card {
  max-width: 600px;
  width: 100%;
}
.mi-meta-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  font-size: 13px;
}
.mi-meta-label {
  font-weight: 600;
  color: #555;
  min-width: 130px;
}
.resource-link {
  color: #2080f0;
  text-decoration: underline;
  cursor: pointer;
  word-break: break-all;
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
