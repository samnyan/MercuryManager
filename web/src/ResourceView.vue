<script setup lang="ts">
import { ref, watch, onBeforeUnmount, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NImage, NButton, NButtonGroup, NSpace, NTabs, NTab, NSpin, NSelect, NCard, NIcon, NTag } from 'naive-ui'
import { ArrowLeft, ExternalLink, Grid, Moon, Sun } from '@lucide/vue'
import { api, useProject } from './project'
import { tr } from './i18n'
import TextureCreateDialog from './components/resource/TextureCreateDialog.vue'
import TexturePreviewModal from './components/resource/TexturePreviewModal.vue'
import AudioResourceViewer from './components/resource/AudioResourceViewer.vue'
import GenericResourceViewer from './components/resource/GenericResourceViewer.vue'
import BlueprintViewer from './components/resource/BlueprintViewer.vue'
import MerResourceViewer from './components/resource/MerResourceViewer.vue'
import MaterialResourceViewer from './components/resource/MaterialResourceViewer.vue'
import MaterialInstanceViewer from './components/resource/MaterialInstanceViewer.vue'
import MpcResourceViewer from './components/resource/MpcResourceViewer.vue'
import MovieResourceViewer from './components/resource/MovieResourceViewer.vue'

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
const isMpc = ref(false)
const isMovie = ref(false)
const spriteSrc = ref('')
const spriteError = ref('')
const texBgMode = ref<'checkerboard' | 'dark' | 'light'>('checkerboard')
const showTexModal = ref(false)
const modalTexPath = ref('')
const modalTexTitle = ref('')

function openExternalTexPreview(targetPath: string, label: string) {
  if (!project.id || !targetPath) return
  modalTexPath.value = targetPath
  modalTexTitle.value = label ? `${label} (${targetPath})` : targetPath
  showTexModal.value = true
}
const isDataTable = computed(() => {
  if (!data.value?.Exports) return false
  return data.value.Exports.some((e: any) => e.Class === 'DataTable')
})

const matchedTableRoute = computed(() => {
  if (!isDataTable.value || !path.value) return null
  const filename = path.value.split('/').pop()?.replace('.uasset', '') || ''
  if (path.value.startsWith('Table/')) {
    if (filename === 'MusicParameterTable') {
      return { path: '/table/MusicParameterTable', label: `${tr('ui.openTableEditor')} (MusicParameterTable)`, type: 'music' }
    }
    return { path: `/table/${filename}`, label: `${tr('ui.openTableEditor')} (${filename})`, type: 'table' }
  }
  if (path.value.startsWith('Message/')) {
    return { path: `/message/${filename}`, label: `${tr('ui.openMessageEditor')} (${filename})`, type: 'message' }
  }
  return null
})

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
    isMpc.value = false
    isMovie.value = false
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
      isMpc.value = !!result.data?.MpcInfo
      isMovie.value = !!result.data?.MovieInfo

      if (isMovie.value) {
        mode.value = 'movie'
      }

      if (isMpc.value) {
        mode.value = 'mpc'
      }

      if (isMaterial.value) {
        mode.value = 'material'
      }

      if (isMaterialInstance.value) {
        const mi = result.data.MaterialInstanceInfo
        const hasUvCrop = mi.Scalars?.U0 !== undefined || mi.Scalars?.V0 !== undefined
        mode.value = hasUvCrop ? 'sprite' : 'materialInstance'
        const targetTex = mi.Textures?.tex || mi.Textures?.Texture
        if (targetTex && hasUvCrop) {
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
        <n-button
          v-if="matchedTableRoute"
          type="primary"
          secondary
          @click="router.push(matchedTableRoute.path)"
        >
          <template #icon><n-icon :component="ExternalLink" /></template>
          {{ matchedTableRoute.label }}
        </n-button>
        <n-button tag="a" :href="jsonUrl" download :disabled="busy || jsonBusy">{{tr('ui.jsonExport')}}</n-button>
        <n-button :loading="jsonBusy" :disabled="busy" @click="jsonInput?.click()">{{tr('ui.jsonImport')}}</n-button>
      </n-space>
      <input ref="jsonInput" type="file" accept=".json,application/json" hidden @change="uploadJson" />
    </div>

    <div v-if="matchedTableRoute" class="datatable-route-banner view-header-fixed">
      <div class="banner-left">
        <n-tag type="success" size="small" :bordered="false">{{ tr('ui.dataTableDedicatedTag') }}</n-tag>
        <span>{{ tr('ui.dataTableDedicatedDesc') }}</span>
      </div>
      <n-button size="small" type="primary" @click="router.push(matchedTableRoute.path)">
        <template #icon><n-icon :component="ExternalLink" /></template>
        {{ tr('ui.gotoPage') }} {{ matchedTableRoute.path }}
      </n-button>
    </div>

    <!-- 仅在存在对应功能时才显示可选 Tab，且不显示不兼容的 Tab -->
    <n-tabs v-if="((isMovie || isMpc || isMaterial || isMaterialInstance || isTexture || isAudio) && data) || isBlueprint" v-model:value="mode" type="line" class="resource-tabs view-header-fixed">
      <n-tab v-if="isBlueprint" name="blueprint">Blueprint / Widget Tree</n-tab>
      <n-tab v-if="isMovie" name="movie">{{ tr('ui.movieTab') }}</n-tab>
      <n-tab v-if="isMpc" name="mpc">{{ tr('ui.mpcTab') }}</n-tab>
      <n-tab v-if="isMaterial" name="material">{{ tr('ui.materialTab') }}</n-tab>
      <n-tab v-if="isMaterialInstance" name="materialInstance">{{ tr('ui.materialInstanceTab') }}</n-tab>
      <n-tab v-if="isMaterialInstance && (miInfo?.Scalars?.U0 !== undefined || miInfo?.Scalars?.V0 !== undefined || spriteSrc)" name="sprite">{{ tr('ui.spriteTab') }}</n-tab>
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
        <movie-resource-viewer
          v-if="data?.MovieInfo && mode === 'movie'"
          :info="data.MovieInfo"
          :path="path"
        />
        <mpc-resource-viewer
          v-if="data?.MpcInfo && mode === 'mpc'"
          :info="data.MpcInfo"
          :path="path"
        />
        <material-resource-viewer
          v-if="data?.MaterialInfo && mode === 'material'"
          :info="data.MaterialInfo"
          :path="path"
          @navigate-path="jumpToResource"
          @preview-texture="openExternalTexPreview"
        />
        <material-instance-viewer
          v-if="data?.MaterialInstanceInfo && mode === 'materialInstance'"
          :info="data.MaterialInstanceInfo"
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
          <div class="tex-toolbar-row">
            <span class="bg-label">{{ tr('ui.texBgLabel') }}:</span>
            <n-button-group size="tiny">
              <n-button :type="texBgMode === 'checkerboard' ? 'primary' : 'default'" :secondary="texBgMode !== 'checkerboard'" @click="texBgMode = 'checkerboard'">
                <template #icon><n-icon :component="Grid" /></template>
                {{ tr('ui.texBgChecker') }}
              </n-button>
              <n-button :type="texBgMode === 'dark' ? 'primary' : 'default'" :secondary="texBgMode !== 'dark'" @click="texBgMode = 'dark'">
                <template #icon><n-icon :component="Moon" /></template>
                {{ tr('ui.texBgDark') }}
              </n-button>
              <n-button :type="texBgMode === 'light' ? 'primary' : 'default'" :secondary="texBgMode !== 'light'" @click="texBgMode = 'light'">
                <template #icon><n-icon :component="Sun" /></template>
                {{ tr('ui.texBgLight') }}
              </n-button>
            </n-button-group>
          </div>

          <div v-if="spriteSrc" class="sprite-preview-box" :class="`bg-${texBgMode}`">
            <img :src="spriteSrc" width="128" height="128" class="sprite-img" alt="Sprite" />
          </div>
          <div v-else-if="spriteError" class="error-banner">{{ spriteError }}</div>
          <div v-else class="sprite-placeholder">
            <span>{{ tr('ui.spritePlaceholder') }}</span>
          </div>

          <n-card :title="tr('ui.atlasMetaTitle')" size="small" class="mi-meta-card">
            <div class="mi-meta-row">
              <span class="mi-meta-label">{{ tr('ui.parentMaterial') }}:</span>
              <a v-if="miInfo?.ParentPackage" href="javascript:void(0)" class="resource-link" @click="jumpToResource(miInfo.ParentPackage)">
                {{ miInfo.Parent }} ({{ miInfo.ParentPackage }})
              </a>
              <span v-else>{{ miInfo?.Parent || '—' }}</span>
            </div>
            <div class="mi-meta-row">
              <span class="mi-meta-label">{{ tr('ui.baseTexture') }}:</span>
              <a v-if="miInfo?.Textures?.tex || miInfo?.Textures?.Texture" href="javascript:void(0)" class="resource-link" @click="jumpToResource(miInfo.Textures.tex || miInfo.Textures.Texture)">
                {{ miInfo.Textures.tex || miInfo.Textures.Texture }}
              </a>
              <span v-else>—</span>
            </div>
            <div class="mi-meta-row">
              <span class="mi-meta-label">{{ tr('ui.uvParams') }}:</span>
              <code>U0: {{ miInfo?.Scalars?.U0 ?? 0 }}, V0: {{ miInfo?.Scalars?.V0 ?? 0 }}</code>
            </div>
          </n-card>
        </div>

        <div v-if="mode === 'texture'" class="texture-pane">
          <div class="texture-controls">
            <div v-if="mipCount > 1" class="mip-selector-box">
              <span style="font-size: 13px; font-weight: 500">{{ tr('ui.viewMipLevel') }}:</span>
              <n-select v-model:value="selectedMip" :options="mipOptions" size="small" style="width: 220px" />
            </div>

            <div class="tex-toolbar-row">
              <span class="bg-label">{{ tr('ui.texBgLabel') }}:</span>
              <n-button-group size="tiny">
                <n-button :type="texBgMode === 'checkerboard' ? 'primary' : 'default'" :secondary="texBgMode !== 'checkerboard'" @click="texBgMode = 'checkerboard'">
                  <template #icon><n-icon :component="Grid" /></template>
                  {{ tr('ui.texBgChecker') }}
                </n-button>
                <n-button :type="texBgMode === 'dark' ? 'primary' : 'default'" :secondary="texBgMode !== 'dark'" @click="texBgMode = 'dark'">
                  <template #icon><n-icon :component="Moon" /></template>
                  {{ tr('ui.texBgDark') }}
                </n-button>
                <n-button :type="texBgMode === 'light' ? 'primary' : 'default'" :secondary="texBgMode !== 'light'" @click="texBgMode = 'light'">
                  <template #icon><n-icon :component="Sun" /></template>
                  {{ tr('ui.texBgLight') }}
                </n-button>
              </n-button-group>
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

          <div v-if="src" class="texture-stage-box" :class="`bg-${texBgMode}`">
            <img
              :src="src"
              class="texture-img-element"
              alt="Texture Preview"
            />
          </div>
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

    <texture-preview-modal
      v-model:show="showTexModal"
      :title="modalTexTitle"
      :asset-path="modalTexPath"
      @navigate-path="jumpToResource"
    />
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
.datatable-route-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: 6px;
  padding: 8px 14px;
  font-size: 13px;
  color: #166534;
}
.banner-left {
  display: flex;
  align-items: center;
  gap: 10px;
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
.tex-toolbar-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.bg-label {
  font-size: 12px;
  color: #666;
}
.texture-stage-box {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
  border-radius: 6px;
  border: 1px solid #dcdfe6;
  max-width: 100%;
  min-height: 240px;
  overflow: auto;
}
.texture-img-element {
  display: block;
  max-width: 100%;
  max-height: 65vh;
  width: auto;
  height: auto;
  object-fit: contain;
  image-rendering: pixelated;
  border-radius: 4px;
}
.sprite-preview-box.bg-checkerboard,
.texture-stage-box.bg-checkerboard {
  background-color: #ffffff;
  background-image:
    linear-gradient(45deg, #cccccc 25%, transparent 25%),
    linear-gradient(-45deg, #cccccc 25%, transparent 25%),
    linear-gradient(45deg, transparent 75%, #cccccc 75%),
    linear-gradient(-45deg, transparent 75%, #cccccc 75%);
  background-size: 16px 16px;
  background-position: 0 0, 0 8px, 8px -8px, -8px 0;
}
.sprite-preview-box.bg-dark,
.texture-stage-box.bg-dark {
  background-color: #141418;
  background-image: none;
}
.sprite-preview-box.bg-light,
.texture-stage-box.bg-light {
  background-color: #ffffff;
  background-image: none;
}
.unsupported-hint {
  font-size: 12px;
  color: #888;
}
</style>
