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
  NSelect
} from 'naive-ui'
import { Play, Pause, RotateCcw, Grid, Moon, Sun, Sparkles, Layers, Cpu } from '@lucide/vue'
import { useProject } from '../../project'
import { tr } from '../../i18n'
import {
  normalizeMaterialEntry,
  renderUMGMaterialToCanvas,
  type NormalizedMaterialSpec
} from '../../utils/material-shader/UMGMaterialBridge'

export interface SimTextureItem {
  name: string
  path: string
}

const props = defineProps<{
  blendMode?: string
  scalars: Record<string, number>
  vectors: Record<string, { r: number; g: number; b: number; a: number }>
  textures: SimTextureItem[]
  functionNames?: string[]
}>()

const project = useProject()
const canvasRef = ref<HTMLCanvasElement | null>(null)
const webglCanvasRef = ref<HTMLCanvasElement | null>(null)

const playing = ref(false)
const simTime = ref(0)
const bgMode = ref<'checkerboard' | 'dark' | 'light'>('checkerboard')
const simPreset = ref<string>('auto')
const renderBackend = ref<'canvas' | 'webgl'>('webgl')

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
  const spec = normalizeMaterialEntry(rawEntry)
  if (spec && simPreset.value !== 'auto') {
    spec.preset = simPreset.value as any
  }
  return (
    spec || {
      name: 'MaterialSim',
      blendMode: props.blendMode || 'BLEND_Opaque',
      scalars: props.scalars,
      vectors: props.vectors,
      textures: (props.textures || []).map(t => ({ name: t.name, path: t.path })),
      functionNames: props.functionNames || [],
      preset: 'static'
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

const hasTimeAnimation = computed(() => {
  return detectedPreset.value !== 'static'
})

const presetOptions = computed(() => [
  { label: `${tr('ui.simModeAuto')} (${detectedPreset.value})`, value: 'auto' },
  { label: tr('ui.simModeStatic'), value: 'static' },
  { label: tr('ui.simModeComposite'), value: 'composite' },
  { label: tr('ui.simModeGlitch'), value: 'glitch' },
  { label: tr('ui.simModePanRotate'), value: 'pan_rotate' },
  { label: tr('ui.simModeRingGauge'), value: 'ring_gauge' },
  { label: tr('ui.simModeUvCrop'), value: 'uv_crop' },
  { label: tr('ui.simModeColorTint'), value: 'color_tint' }
])

// 去重后的可选贴图列表
const uniqueTextures = computed(() => {
  const seen = new Set<string>()
  const out: SimTextureItem[] = []
  for (const t of props.textures || []) {
    if (!t.path || seen.has(t.path)) continue
    seen.add(t.path)
    out.push(t)
  }
  return out
})

const primaryTexPath = ref<string>('')
const secondaryTexPath = ref<string>('')

watch(
  uniqueTextures,
  list => {
    if (!list?.length) {
      primaryTexPath.value = ''
      secondaryTexPath.value = ''
      return
    }
    primaryTexPath.value = list[0]?.path || ''
    secondaryTexPath.value = list[1]?.path || ''

    for (const item of list) {
      if (item.path && !loadedImages.has(item.path)) {
        loadTextureImage(item.path)
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

  if (playing.value) {
    simTime.value = (simTime.value + dt) % 60
  }

  drawCanvas()
  rafId = requestAnimationFrame(renderLoop)
}

let isRendering = false
async function drawCanvas() {
  if (isRendering) return
  const t = simTime.value

  const targetCanvas = renderBackend.value === 'webgl' ? webglCanvasRef.value : canvasRef.value
  if (!targetCanvas) return

  isRendering = true
  try {
    // 允许用户覆盖的主副贴图
    const specToRender = { ...normalizedSpec.value }
    if (primaryTexPath.value || secondaryTexPath.value) {
      const allTex = [...specToRender.textures]
      const ordered: Array<{ name: string; path: string }> = []
      const pri = allTex.find(x => x.path === primaryTexPath.value)
      if (pri) ordered.push(pri)
      const sec = allTex.find(x => x.path === secondaryTexPath.value && x.path !== primaryTexPath.value)
      if (sec) ordered.push(sec)
      for (const rest of allTex) {
        if (!ordered.some(o => o.path === rest.path)) {
          ordered.push(rest)
        }
      }
      specToRender.textures = ordered
    }

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
        </div>
        <n-space size="small" align="center">
          <n-button-group size="tiny">
            <n-button
              :type="renderBackend === 'webgl' ? 'primary' : 'default'"
              :secondary="renderBackend !== 'webgl'"
              @click="renderBackend = 'webgl'"
            >
              <template #icon><n-icon :component="Cpu" /></template>
              WebGL
            </n-button>
            <n-button
              :type="renderBackend === 'canvas' ? 'primary' : 'default'"
              :secondary="renderBackend !== 'canvas'"
              @click="renderBackend = 'canvas'"
            >
              <template #icon><n-icon :component="Layers" /></template>
              Canvas
            </n-button>
          </n-button-group>
          <n-select
            v-model:value="simPreset"
            :options="presetOptions"
            size="tiny"
            style="width: 160px"
          />
        </n-space>
      </div>
    </template>

    <div class="sim-body">
      <div class="canvas-stage" :class="`bg-${bgMode}`">
        <canvas
          v-show="renderBackend === 'canvas'"
          ref="canvasRef"
          width="240"
          height="240"
          class="sim-canvas"
        />
        <canvas
          v-show="renderBackend === 'webgl'"
          ref="webglCanvasRef"
          width="240"
          height="240"
          class="sim-canvas"
        />
      </div>

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

        <!-- 多图层复合分配管理 (Base / Overlay / Mask) -->
        <div v-if="uniqueTextures.length > 0" class="layer-section">
          <div class="layer-row">
            <span class="t-label">{{ tr('ui.simPrimaryLayer') }}:</span>
            <n-select
              v-model:value="primaryTexPath"
              :options="uniqueTextures.map(t => ({ label: t.name || t.path.split('/').pop(), value: t.path }))"
              size="tiny"
              style="flex: 1"
            />
          </div>

          <div v-if="uniqueTextures.length > 1" class="layer-row">
            <span class="t-label">{{ tr('ui.simSecondaryLayer') }}:</span>
            <n-select
              v-model:value="secondaryTexPath"
              :options="[{ label: tr('ui.viewerEmpty'), value: '' }, ...uniqueTextures.map(t => ({ label: t.name || t.path.split('/').pop(), value: t.path }))]"
              size="tiny"
              style="flex: 1"
            />
          </div>
        </div>
      </div>
    </div>
  </n-card>
</template>

<style scoped>
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
  width: 220px;
  height: 220px;
  border-radius: 4px;
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
.layer-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
  background: #f7f8fa;
  padding: 6px 8px;
  border-radius: 4px;
}
.layer-row {
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