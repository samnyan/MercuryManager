<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import {
  NCard,
  NTag,
  NSpace,
  NButton,
  NButtonGroup,
  NIcon,
  NSlider,
  NSelect,
  NInputNumber,
  NDropdown,
  NModal
} from 'naive-ui'
import { Play, Pause, RotateCcw, Grid, Moon, Sun, Sparkles, Maximize2 } from '@lucide/vue'
import { materialPrograms } from '../../utils/material-runtime/MaterialProgramCache'
import { MaterialInstance } from '../../utils/material-runtime/MaterialInstance'
import { useProject } from '../../project'
import { tr } from '../../i18n'
import {
  normalizeMaterialEntry,
  renderUMGMaterialToCanvas,
  type NormalizedMaterialSpec
} from '../../utils/material-shader/UMGMaterialBridge'
import {
  MaterialWebGLRenderer,
  resolveContractSamplerSlots,
  type MaterialShaderContract
} from '../../utils/material-shader/MaterialWebGLRenderer'

export interface SimTextureItem {
  name: string
  path: string
}

const props = defineProps<{
  materialPath?: string
  blendMode?: string
  scalars: Record<string, number>
  vectors: Record<string, { r: number; g: number; b: number; a: number }>
  textures: SimTextureItem[]
  functionNames?: string[]
  collectionOverrides?: Record<string, { scalars?: Record<string, number>; vectors?: Record<string, [number, number, number, number]> }>
}>()

type CollectionValues = { scalars?: Record<string, number>; vectors?: Record<string, [number, number, number, number]> }
const localCollections = ref<Record<string, CollectionValues>>({})
const collectionControls = computed(() => shaderContract.value?.collectionInputs?.filter(c => c.defaults) || [])
function scalarValue(path: string, name: string, fallback: number) {
  return localCollections.value[path]?.scalars?.[name] ?? props.collectionOverrides?.[path]?.scalars?.[name] ?? fallback
}
function vectorValue(path: string, name: string, channel: number, fallback: number[]) {
  return (localCollections.value[path]?.vectors?.[name] ?? props.collectionOverrides?.[path]?.vectors?.[name] ?? fallback)[channel] ?? 0
}
function setScalar(path: string, name: string, value: number | null) {
  if (value === null || !Number.isFinite(value)) return
  const entry = localCollections.value[path] ??= {}
  ;(entry.scalars ??= {})[name] = value
}
function setVector(path: string, name: string, channel: number, value: number | null, fallback: number[]) {
  if (value === null || !Number.isFinite(value)) return
  const entry = localCollections.value[path] ??= {}
  const vector = [0, 1, 2, 3].map(i => vectorValue(path, name, i, fallback)) as [number, number, number, number]
  vector[channel] = value
  ;(entry.vectors ??= {})[name] = vector
}
const project = useProject()
watch(() => [project.id, props.materialPath], () => { localCollections.value = {} })
const webglCanvasRef = ref<HTMLCanvasElement | null>(null)
const fallbackCanvasRef = ref<HTMLCanvasElement | null>(null)

const playing = ref(false)
const simTime = ref(0)
const bgMode = ref<'checkerboard' | 'dark' | 'light'>('checkerboard')

// 渲染倍率 (记录至 localStorage)
const STORAGE_KEY_SCALE = 'mercurymanager_sim_render_scale'
const renderScale = ref<number>(
  (() => {
    try {
      const v = Number(localStorage.getItem(STORAGE_KEY_SCALE))
      if (v === 0.5 || v === 1 || v === 1.5 || v === 2 || v === 3) return v
    } catch {}
    return 1
  })()
)

function setRenderScale(scale: number) {
  renderScale.value = scale
  try {
    localStorage.setItem(STORAGE_KEY_SCALE, String(scale))
  } catch {}
}

// 宽高比 / 尺寸模式
type AspectMode = 'auto' | '1:1' | '4:3' | '16:9' | '2:1'
const aspectMode = ref<AspectMode>('auto')

// 弹窗放大预览
const showZoomModal = ref(false)
const zoomCanvasRef = ref<HTMLCanvasElement | null>(null)

// 主贴图宽高跟踪
const primaryTextureSize = ref<{ width: number; height: number } | null>(null)

// 计算当前应采用的基础渲染尺寸 (基于基准边长 240)
const baseStageSize = computed<{ width: number; height: number }>(() => {
  if (aspectMode.value === 'auto') {
    if (primaryTextureSize.value && primaryTextureSize.value.width > 0 && primaryTextureSize.value.height > 0) {
      const { width: tw, height: th } = primaryTextureSize.value
      const maxDim = 240
      if (tw >= th) {
        return { width: maxDim, height: Math.max(32, Math.round((maxDim * th) / tw)) }
      } else {
        return { width: Math.max(32, Math.round((maxDim * tw) / th)), height: maxDim }
      }
    }
    return { width: 240, height: 240 }
  }
  if (aspectMode.value === '1:1') return { width: 240, height: 240 }
  if (aspectMode.value === '4:3') return { width: 240, height: 180 }
  if (aspectMode.value === '16:9') return { width: 240, height: 135 }
  if (aspectMode.value === '2:1') return { width: 240, height: 120 }
  return { width: 240, height: 240 }
})

// 实际 canvas internal pixel 分辨率 (乘以渲染倍率)
const canvasPixelSize = computed(() => {
  const scale = renderScale.value || 1
  return {
    width: Math.round(baseStageSize.value.width * scale),
    height: Math.round(baseStageSize.value.height * scale)
  }
})

// 下拉层叠菜单选项 (类似 WebGL2 tag 样式，hover 展开层叠菜单)
const settingsDropdownOptions = computed(() => [
  {
    type: 'group',
    key: 'grp_scale',
    label: `${tr('ui.simScale')}: ${renderScale.value}x`,
    children: [
      { label: '0.5x', key: 'scale_0.5' },
      { label: '1.0x (100%)', key: 'scale_1.0' },
      { label: '1.5x', key: 'scale_1.5' },
      { label: '2.0x (HiDPI)', key: 'scale_2.0' },
      { label: '3.0x (Ultra)', key: 'scale_3.0' }
    ]
  },
  {
    type: 'group',
    key: 'grp_aspect',
    label: `${tr('ui.simAspectRatio')}: ${aspectMode.value.toUpperCase()}`,
    children: [
      {
        label: primaryTextureSize.value
          ? `${tr('ui.simAspectAuto')} (${primaryTextureSize.value.width}×${primaryTextureSize.value.height})`
          : tr('ui.simAspectAuto'),
        key: 'aspect_auto'
      },
      { label: tr('ui.simAspectSquare'), key: 'aspect_1:1' },
      { label: tr('ui.simAspect4_3'), key: 'aspect_4:3' },
      { label: tr('ui.simAspect16_9'), key: 'aspect_16:9' },
      { label: tr('ui.simAspect2_1'), key: 'aspect_2:1' }
    ]
  },
  {
    key: 'action_zoom',
    label: tr('ui.simZoomModal')
  }
])

function handleSettingsSelect(key: string) {
  if (key.startsWith('scale_')) {
    const val = Number(key.replace('scale_', ''))
    setRenderScale(val)
  } else if (key.startsWith('aspect_')) {
    aspectMode.value = key.replace('aspect_', '') as AspectMode
  } else if (key === 'action_zoom') {
    showZoomModal.value = true
  }
}

// Cooked WebGL2 shader contract state
const shaderContract = ref<MaterialShaderContract | null>(null)
const contractError = ref<string | null>(null)
const usingCookedWebGL2 = computed(() => {
  const c = shaderContract.value
  if (!c || c.schemaVersion !== 1 || c.renderingReady !== true) return false
  const { error } = resolveContractSamplerSlots(c)
  return !error
})

let webglRenderer: MaterialWebGLRenderer | null = null
let materialInstance: MaterialInstance | null = null
let fetchController: AbortController | null = null
let debounceTimer: number | undefined

// 贴图缓存: path -> HTMLImageElement
const loadedImages = new Map<string, HTMLImageElement>()
const imageUrls = new Map<string, string>()

// 构造统一材质规格
const normalizedSpec = computed<NormalizedMaterialSpec>(() => {
  const rawEntry = {
    Type: 'Material',
    MaterialInfo: {
      Name: 'MaterialSim',
      BlendMode: props.blendMode || 'BLEND_Opaque',
      ScalarParameters: Object.entries(props.scalars).map(([k, v]) => ({ ParameterName: k, DefaultValue: v })),
      VectorParameters: Object.entries(props.vectors).map(([k, v]) => ({ ParameterName: k, ...v })),
      TextureNodes: (props.textures || []).map(t => ({ ParameterName: t.name, ContentFile: t.path })),
      FunctionCalls: (props.functionNames || []).map(f => ({ FunctionName: f }))
    }
  }
  return (
    normalizeMaterialEntry(rawEntry, props.materialPath) || {
      name: 'MaterialSim',
      blendMode: props.blendMode || 'BLEND_Opaque',
      scalars: props.scalars,
      vectors: props.vectors,
      textures: (props.textures || []).map(t => ({ name: t.name, path: t.path })),
      functionNames: props.functionNames || [],
      preset: 'static',
      assetPath: props.materialPath
    }
  )
})

const detectedPreset = computed(() => {
  const rawEntry = {
    Type: 'Material',
    MaterialInfo: {
      BlendMode: props.blendMode || 'BLEND_Opaque',
      ScalarParameters: Object.entries(props.scalars).map(([k, v]) => ({ ParameterName: k, DefaultValue: v })),
      VectorParameters: Object.entries(props.vectors).map(([k, v]) => ({ ParameterName: k, ...v })),
      TextureNodes: (props.textures || []).map(t => ({ ParameterName: t.name, ContentFile: t.path })),
      FunctionCalls: (props.functionNames || []).map(f => ({ FunctionName: f }))
    }
  }
  const spec = normalizeMaterialEntry(rawEntry)
  return spec?.preset || 'static'
})

const hasPerFrameExpressions = computed(() => {
  const mb = shaderContract.value?.materialBindings
  if (mb && (mb.perFrameScalarCount > 0 || mb.perFrameVectorCount > 0)) return true
  if (shaderContract.value?.glsl?.includes('cb1_0._m0[132u]')) return true
  return false
})

const hasTimeAnimation = computed(() => {
  if (usingCookedWebGL2.value) {
    return hasPerFrameExpressions.value
  }
  return detectedPreset.value === 'glitch' || detectedPreset.value === 'pan_rotate'
})

const uniqueTextures = computed(() => {
  const seen = new Set<string>()
  const out: SimTextureItem[] = []
  for (const expr of shaderContract.value?.textureExpressions || []) {
    if (expr.assetPath && !seen.has(expr.assetPath)) {
      seen.add(expr.assetPath)
      out.push({ name: expr.parameterName || expr.assetPath.split('/').pop() || 'Texture', path: expr.assetPath })
    }
  }
  for (const t of props.textures || []) {
    if (!t.path || seen.has(t.path)) continue
    seen.add(t.path)
    out.push(t)
  }
  return out
})

function buildOverridesQuery(timeVal: number, prevTimeVal: number): URLSearchParams {
  const scalarObj: Record<string, number> = {}
  for (const [k, v] of Object.entries(props.scalars || {})) {
    if (typeof v === 'number' && Number.isFinite(v)) scalarObj[k] = v
  }
  const vectorObj: Record<string, [number, number, number, number]> = {}
  for (const [k, v] of Object.entries(props.vectors || {})) {
    if (v && Number.isFinite(v.r) && Number.isFinite(v.g) && Number.isFinite(v.b) && Number.isFinite(v.a)) {
      vectorObj[k] = [v.r, v.g, v.b, v.a]
    }
  }
  const params = new URLSearchParams({
    path: props.materialPath || '',
    time: String(timeVal),
    previousTime: String(prevTimeVal)
  })
  if (Object.keys(scalarObj).length > 0) {
    params.set('scalarOverrides', JSON.stringify(scalarObj))
  }
  if (Object.keys(vectorObj).length > 0) {
    params.set('vectorOverrides', JSON.stringify(vectorObj))
  }
  const collections: Record<string, CollectionValues> = { ...props.collectionOverrides }
  for (const [path, local] of Object.entries(localCollections.value)) {
    collections[path] = {
      scalars: { ...collections[path]?.scalars, ...local.scalars },
      vectors: { ...collections[path]?.vectors, ...local.vectors }
    }
  }
  if (Object.keys(collections).length > 0) params.set('collectionOverrides', JSON.stringify(collections))
  return params
}

async function fetchShaderContract(timeVal = simTime.value, prevTimeVal = simTime.value) {
  if (!project.id || !props.materialPath || !props.materialPath.endsWith('.uasset')) {
    shaderContract.value = null
    contractError.value = null
    return
  }

  fetchController?.abort()
  const controller = new AbortController()
  fetchController = controller

  try {
    const data = await materialPrograms.load(project.id, props.materialPath)
    if (controller.signal.aborted) return
    shaderContract.value = data
    materialInstance = data.uniformProgram ? new MaterialInstance(data) : null
    const { slots, error } = resolveContractSamplerSlots(data)
    contractError.value = error

    for (const slot of slots) {
      if (slot.assetPath) {
        const img = await loadTextureImage(slot.assetPath)
        if (img && webglRenderer) {
          webglRenderer.updateTexture(slot.assetPath, img, slot.samplerSource)
        }
      }
    }
  } catch (e: any) {
    if (controller.signal.aborted) return
    shaderContract.value = null
    contractError.value = e?.message || null
  }
}

watch(
  () => [project.id, props.materialPath, project.revision],
  () => {
    if (debounceTimer) window.clearTimeout(debounceTimer)
    debounceTimer = window.setTimeout(() => {
      fetchShaderContract(simTime.value, simTime.value)
    }, 60)
  },
  { immediate: true, deep: true }
)

watch(
  uniqueTextures,
  list => {
    primaryTextureSize.value = null
    if (!list?.length) return
    for (const item of list) {
      if (item.path) {
        const cached = loadedImages.get(item.path)
        if (cached && cached.naturalWidth > 0 && !primaryTextureSize.value) {
          primaryTextureSize.value = { width: cached.naturalWidth, height: cached.naturalHeight }
        }
        if (!cached) loadTextureImage(item.path)
      }
    }
  },
  { immediate: true, deep: true }
)

watch(
  hasTimeAnimation,
  hasAnim => {
    playing.value = hasAnim
  },
  { immediate: true }
)

async function loadTextureImage(path: string): Promise<HTMLImageElement | null> {
  if (!project.id || !path || !path.endsWith('.uasset')) return null
  const cached = loadedImages.get(path)
  if (cached && cached.naturalWidth > 0) return cached

  try {
    const res = await fetch(
      `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path, mip: '0' })
    )
    if (!res.ok) return null
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    imageUrls.set(path, url)
    return new Promise(resolve => {
      const img = new Image()
      img.onload = () => {
        loadedImages.set(path, img)
        if (!primaryTextureSize.value && img.naturalWidth > 0 && img.naturalHeight > 0) {
          primaryTextureSize.value = { width: img.naturalWidth, height: img.naturalHeight }
        }
        resolve(img)
      }
      img.onerror = () => resolve(null)
      img.src = url
    })
  } catch {
    return null
  }
}

let rafId = 0
let lastTs = 0


function renderLoop(ts: number) {
  if (!lastTs) lastTs = ts
  const dt = Math.min(0.1, (ts - lastTs) / 1000)
  lastTs = ts

  const prevT = simTime.value
  if (playing.value) {
    simTime.value = simTime.value + dt
    materialInstance?.update({ time: simTime.value, previousTime: prevT })
  }

  drawCanvas()
  rafId = requestAnimationFrame(renderLoop)
}

let isRendering = false
async function drawCanvas() {
  if (isRendering) return
  const t = simTime.value

  isRendering = true
  try {
    // 1. Cooked WebGL2 path
    if (usingCookedWebGL2.value && shaderContract.value && webglCanvasRef.value) {
      const glCanvas = webglCanvasRef.value
      if (!webglRenderer) {
        webglRenderer = new MaterialWebGLRenderer()
      }
      if (webglRenderer.init(glCanvas)) {
        const { slots, error } = resolveContractSamplerSlots(shaderContract.value)
        if (!error) {
          for (const slot of slots) {
            const img = loadedImages.get(slot.assetPath)
            if (img) {
              webglRenderer.updateTexture(slot.assetPath, img, slot.samplerSource)
            } else {
              loadTextureImage(slot.assetPath).then(loaded => {
                if (loaded && webglRenderer) {
                  webglRenderer.updateTexture(slot.assetPath, loaded, slot.samplerSource)
                }
              })
            }
          }
          materialInstance?.setParameters(props.scalars, Object.fromEntries(Object.entries(props.vectors).map(([k,v])=>[k,[v.r,v.g,v.b,v.a]])))
          const collections: Record<string, CollectionValues> = { ...props.collectionOverrides }
          for (const [path, local] of Object.entries(localCollections.value)) collections[path] = {
            scalars: { ...collections[path]?.scalars, ...local.scalars }, vectors: { ...collections[path]?.vectors, ...local.vectors }
          }
          materialInstance?.setCollections(collections)
          if (!playing.value) materialInstance?.update({ time: t, previousTime: t })
          const ok = webglRenderer.renderContract(
            glCanvas.width,
            glCanvas.height,
            { ...shaderContract.value, time: t, runtimeWrites: materialInstance?.writes() },
            slots,
            props.blendMode || 'BLEND_Opaque',
            bgMode.value
          )
          if (ok) {
            if (showZoomModal.value && zoomCanvasRef.value) {
              const zCanvas = zoomCanvasRef.value
              const zCtx = zCanvas.getContext('2d')
              if (zCtx) {
                zCtx.clearRect(0, 0, zCanvas.width, zCanvas.height)
                zCtx.drawImage(glCanvas, 0, 0, zCanvas.width, zCanvas.height)
              }
            }
            return
          }
        }
      }
    }

    // 2. Fallback Canvas 2D path
    const targetCanvas = fallbackCanvasRef.value
    if (!targetCanvas) return

    const specToRender = { ...normalizedSpec.value }
    const baked = await renderUMGMaterialToCanvas(
      specToRender,
      loadTextureImage,
      targetCanvas.width,
      targetCanvas.height,
      t
    )
    if (baked) {
      const ctx = targetCanvas.getContext('2d')
      if (ctx) {
        ctx.clearRect(0, 0, targetCanvas.width, targetCanvas.height)
        ctx.drawImage(baked, 0, 0, targetCanvas.width, targetCanvas.height)
      }
      if (showZoomModal.value && zoomCanvasRef.value) {
        const zCanvas = zoomCanvasRef.value
        const zCtx = zCanvas.getContext('2d')
        if (zCtx) {
          zCtx.clearRect(0, 0, zCanvas.width, zCanvas.height)
          zCtx.drawImage(baked, 0, 0, zCanvas.width, zCanvas.height)
        }
      }
    }
  } finally {
    isRendering = false
  }
}

onMounted(() => {
  rafId = requestAnimationFrame(renderLoop)
})

onUnmounted(() => {
  if (rafId) cancelAnimationFrame(rafId)
  if (debounceTimer) window.clearTimeout(debounceTimer)
  fetchController?.abort()
  if (webglRenderer) {
    webglRenderer.destroy()
    webglRenderer = null
  }
  for (const url of imageUrls.values()) {
    URL.revokeObjectURL(url)
  }
  imageUrls.clear()
})
</script>

<template>
  <n-card size="small" class="sim-card">
    <template #header>
      <div class="sim-header">
        <div class="sim-title">
          <n-icon :component="Sparkles" color="#18a058" />
          <span>{{ tr('ui.umgShaderSimulator') }}</span>
          <n-dropdown
            trigger="hover"
            placement="bottom-start"
            :options="settingsDropdownOptions"
            @select="handleSettingsSelect"
          >
            <n-tag
              v-if="usingCookedWebGL2"
              size="tiny"
              type="success"
              :bordered="false"
              class="clickable-tag"
            >
              WebGL2 GLSL <span class="tag-meta">({{ renderScale }}x · {{ baseStageSize.width }}×{{ baseStageSize.height }}) ▾</span>
            </n-tag>
            <n-tag
              v-else-if="shaderContract && (!shaderContract.renderingReady || shaderContract.limitations?.length)"
              size="tiny"
              type="warning"
              :bordered="false"
              :title="shaderContract.limitations?.join('; ') || 'Shader rendering not ready'"
              class="clickable-tag"
            >
              {{ tr('ui.simModeAuto') }} (Fallback) <span class="tag-meta">({{ renderScale }}x) ▾</span>
            </n-tag>
            <n-tag
              v-else-if="contractError"
              size="tiny"
              type="error"
              :bordered="false"
              :title="contractError"
              class="clickable-tag"
            >
              Shader Error (Fallback) ▾
            </n-tag>
          </n-dropdown>
        </div>
        <n-button size="tiny" quaternary circle :title="tr('ui.simZoomModal')" @click="showZoomModal = true">
          <template #icon><n-icon :component="Maximize2" /></template>
        </n-button>
      </div>
    </template>

    <div class="sim-body">
      <div class="canvas-stage" :class="`bg-${bgMode}`">
        <canvas
          v-show="usingCookedWebGL2"
          ref="webglCanvasRef"
          :width="canvasPixelSize.width"
          :height="canvasPixelSize.height"
          :style="{ width: `${baseStageSize.width}px`, height: `${baseStageSize.height}px` }"
          class="sim-canvas"
        />
        <canvas
          v-show="!usingCookedWebGL2"
          ref="fallbackCanvasRef"
          :width="canvasPixelSize.width"
          :height="canvasPixelSize.height"
          :style="{ width: `${baseStageSize.width}px`, height: `${baseStageSize.height}px` }"
          class="sim-canvas"
        />
      </div>

      <!-- 放大预览弹窗 -->
      <n-modal
        v-model:show="showZoomModal"
        preset="card"
        :title="`${tr('ui.simZoomModal')} - ${props.materialPath?.split('/').pop() || ''}`"
        style="width: 800px; max-width: 95vw;"
        size="small"
      >
        <div class="modal-zoom-container" :class="`bg-${bgMode}`">
          <canvas
            ref="zoomCanvasRef"
            :width="canvasPixelSize.width"
            :height="canvasPixelSize.height"
            :style="{
              width: `${Math.min(720, baseStageSize.width * 2)}px`,
              height: `${Math.min(540, baseStageSize.height * 2)}px`
            }"
            class="zoom-modal-canvas"
          />
        </div>
      </n-modal>

      <details v-if="collectionControls.length" class="collection-panel">
        <summary>{{ tr('ui.collectionPreview') }}</summary>
        <p>{{ tr('ui.collectionPreviewHint') }}</p>
        <n-button size="tiny" @click="localCollections = {}">{{ tr('ui.collectionReset') }}</n-button>
        <details v-for="collection in collectionControls" :key="collection.assetPath">
          <summary>{{ collection.assetPath }}</summary>
          <div v-for="(value, name) in collection.defaults!.scalars" :key="name" class="collection-row">
            <label>{{ name }}</label>
            <n-input-number size="tiny" :aria-label="`${collection.assetPath} ${name}`"
              :value="scalarValue(collection.assetPath, String(name), value)"
              @update:value="setScalar(collection.assetPath, String(name), $event)" />
          </div>
          <div v-for="(value, name) in collection.defaults!.vectors" :key="name" class="collection-vector">
            <label>{{ name }}</label>
            <n-input-number v-for="channel in 4" :key="channel" size="tiny"
              :aria-label="`${collection.assetPath} ${name} ${'RGBA'[channel - 1]}`"
              :value="vectorValue(collection.assetPath, String(name), channel - 1, value)"
              @update:value="setVector(collection.assetPath, String(name), channel - 1, $event, value)" />
          </div>
        </details>
      </details>
      <div class="sim-controls">
        <div class="ctrl-row">
          <n-space size="small" align="center">
            <n-button size="tiny" type="primary" @click="playing = !playing">
              <template #icon>
                <n-icon :component="playing ? Pause : Play" />
              </template>
              {{ playing ? tr('ui.pause') : tr('ui.play') }}
            </n-button>
            <n-button size="tiny" secondary @click="simTime = 0">
              <template #icon><n-icon :component="RotateCcw" /></template>
            </n-button>
            <n-tag v-if="!hasTimeAnimation && !playing" size="tiny" :bordered="false" type="info">
              {{ tr('ui.simModeStatic') }}
            </n-tag>
          </n-space>

          <n-button-group size="tiny">
            <n-button
              :type="bgMode === 'checkerboard' ? 'primary' : 'default'"
              :secondary="bgMode !== 'checkerboard'"
              @click="bgMode = 'checkerboard'"
            >
              <template #icon><n-icon :component="Grid" /></template>
            </n-button>
            <n-button
              :type="bgMode === 'dark' ? 'primary' : 'default'"
              :secondary="bgMode !== 'dark'"
              @click="bgMode = 'dark'"
            >
              <template #icon><n-icon :component="Moon" /></template>
            </n-button>
            <n-button
              :type="bgMode === 'light' ? 'primary' : 'default'"
              :secondary="bgMode !== 'light'"
              @click="bgMode = 'light'"
            >
              <template #icon><n-icon :component="Sun" /></template>
            </n-button>
          </n-button-group>
        </div>

        <div v-if="hasTimeAnimation || playing" class="time-row">
          <span class="t-label">Time: {{ simTime.toFixed(2) }}s</span>
          <n-slider v-model:value="simTime" :min="0" :max="10" :step="0.02" style="flex: 1" />
        </div>
      </div>
    </div>
  </n-card>
</template>

<style scoped>
.collection-panel { width: 100%; max-height: 300px; overflow: auto; font-size: 12px; }
.collection-panel summary { cursor: pointer; overflow-wrap: anywhere; }
.collection-panel p { color: #888; }
.collection-row { display: grid; grid-template-columns: 1fr 120px; align-items: center; gap: 8px; margin-top: 4px; }
.collection-vector { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 4px; margin-top: 8px; }
.collection-vector label { grid-column: 1 / -1; }
.sim-card {
  border-radius: 6px;
}
.sim-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.sim-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-weight: 600;
  font-size: 13px;
}
.sim-body {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
}
.canvas-stage {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 8px;
  border-radius: 6px;
  border: 1px solid #dcdfe6;
}
.canvas-stage.bg-checkerboard {
  background-color: #ffffff;
  background-image:
    linear-gradient(45deg, #cccccc 25%, transparent 25%),
    linear-gradient(-45deg, #cccccc 25%, transparent 25%),
    linear-gradient(45deg, transparent 75%, #cccccc 75%),
    linear-gradient(-45deg, transparent 75%, #cccccc 75%);
  background-size: 16px 16px;
  background-position: 0 0, 0 8px, 8px -8px, -8px 0;
}
.canvas-stage.bg-dark {
  background-color: #141418;
}
.canvas-stage.bg-light {
  background-color: #ffffff;
}
.sim-canvas {
  display: block;
  max-width: 100%;
  border-radius: 4px;
}
.clickable-tag {
  cursor: pointer;
  user-select: none;
}
.tag-meta {
  opacity: 0.8;
  font-size: 10px;
  margin-left: 2px;
}
.modal-zoom-container {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
  border-radius: 6px;
  border: 1px solid #dcdfe6;
  min-height: 280px;
}
.modal-zoom-container.bg-checkerboard {
  background-color: #ffffff;
  background-image:
    linear-gradient(45deg, #cccccc 25%, transparent 25%),
    linear-gradient(-45deg, #cccccc 25%, transparent 25%),
    linear-gradient(45deg, transparent 75%, #cccccc 75%),
    linear-gradient(-45deg, transparent 75%, #cccccc 75%);
  background-size: 16px 16px;
  background-position: 0 0, 0 8px, 8px -8px, -8px 0;
}
.modal-zoom-container.bg-dark {
  background-color: #141418;
}
.modal-zoom-container.bg-light {
  background-color: #ffffff;
}
.zoom-modal-canvas {
  display: block;
  max-width: 100%;
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}
.sim-controls {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.ctrl-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.time-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
}
.t-label {
  color: #666;
  font-family: monospace;
  white-space: nowrap;
  font-size: 11px;
  min-width: 44px;
}
</style>
