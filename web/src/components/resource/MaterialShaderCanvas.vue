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
import { Play, Pause, RotateCcw, Grid, Moon, Sun, Sparkles } from '@lucide/vue'
import { useProject } from '../../project'
import { tr } from '../../i18n'

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

const playing = ref(true)
const simTime = ref(0)
const bgMode = ref<'checkerboard' | 'dark' | 'light'>('checkerboard')
const simPreset = ref<string>('auto')

// 贴图缓存: path -> HTMLImageElement
const loadedImages = new Map<string, HTMLImageElement>()
const imageUrls = new Map<string, string>()

// 检测到的最佳模拟预设
const detectedPreset = computed(() => {
  const sKeys = Object.keys(props.scalars).map(k => k.toLowerCase())
  const fnList = (props.functionNames || []).map(f => f.toLowerCase())

  if (sKeys.includes('u0') && sKeys.includes('v0')) return 'uv_crop'
  if (
    fnList.some(f => f.includes('glitch')) ||
    sKeys.some(k => k.includes('glitch') || k.includes('timespanh') || k.includes('timedurationh'))
  ) {
    return 'glitch'
  }
  if (sKeys.some(k => k.includes('gauge') || k.includes('ring'))) {
    return 'ring_gauge'
  }
  if (
    fnList.some(f => f.includes('rotator') || f.includes('panner') || f.includes('texscale')) ||
    sKeys.some(k => k.includes('rotate') || k.includes('speed') || k.includes('position_u') || k.includes('pan'))
  ) {
    return 'pan_rotate'
  }
  return 'color_tint'
})

const activeMode = computed(() => (simPreset.value === 'auto' ? detectedPreset.value : simPreset.value))

const presetOptions = computed(() => [
  { label: `${tr('ui.simModeAuto')} (${detectedPreset.value})`, value: 'auto' },
  { label: tr('ui.simModePanRotate'), value: 'pan_rotate' },
  { label: tr('ui.simModeGlitch'), value: 'glitch' },
  { label: tr('ui.simModeRingGauge'), value: 'ring_gauge' },
  { label: tr('ui.simModeUvCrop'), value: 'uv_crop' },
  { label: tr('ui.simModeColorTint'), value: 'color_tint' }
])

// 选取主贴图
const selectedTexPath = ref<string>('')

watch(
  () => props.textures,
  list => {
    if (!list?.length) {
      selectedTexPath.value = ''
      return
    }
    // 优先选择非 GlitchMask 的主贴图
    const preferred =
      list.find(t => /applyto|tex01|^tex$|texture/i.test(t.name)) ||
      list.find(t => !/mask/i.test(t.path)) ||
      list[0]
    if (preferred && (!selectedTexPath.value || !list.some(t => t.path === selectedTexPath.value))) {
      selectedTexPath.value = preferred.path
    }
    // 预加载所有关联贴图
    for (const item of list) {
      if (item.path && !loadedImages.has(item.path)) {
        loadTextureImage(item.path)
      }
    }
  },
  { immediate: true, deep: true }
)

async function loadTextureImage(path: string) {
  if (!project.id || !path || !path.endsWith('.uasset')) return
  try {
    const res = await fetch(
      `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path, mip: '0' })
    )
    if (!res.ok) return
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    imageUrls.set(path, url)
    const img = new Image()
    img.onload = () => {
      loadedImages.set(path, img)
    }
    img.src = url
  } catch {
    // ignore
  }
}

function getScalar(pattern: RegExp, fallback: number): number {
  for (const [k, v] of Object.entries(props.scalars)) {
    if (pattern.test(k)) return Number(v) || 0
  }
  return fallback
}

function getPrimaryColor(): { r: number; g: number; b: number; a: number } {
  const entries = Object.entries(props.vectors)
  if (!entries.length) return { r: 1, g: 1, b: 1, a: 1 }
  const preferred =
    entries.find(([k]) => /on_color|color0|color1|tint|main/i.test(k)) || entries[0]
  return preferred[1]
}

function getSecondaryColor(): { r: number; g: number; b: number; a: number } | null {
  const entries = Object.entries(props.vectors)
  if (entries.length < 2) return null
  const sec = entries.find(([k]) => /off_color|color1|color2|sub/i.test(k)) || entries[1]
  return sec ? sec[1] : null
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

function drawCanvas() {
  const canvas = canvasRef.value
  if (!canvas) return
  const ctx = canvas.getContext('2d')
  if (!ctx) return

  const W = canvas.width
  const H = canvas.height
  ctx.clearRect(0, 0, W, H)

  const t = simTime.value
  const mode = activeMode.value
  const img = selectedTexPath.value ? loadedImages.get(selectedTexPath.value) : undefined
  const col1 = getPrimaryColor()
  const col2 = getSecondaryColor()
  const opacity = Math.min(1, Math.max(0, getScalar(/^opacity$/i, 1.0)))

  ctx.save()
  ctx.globalAlpha = opacity

  // BlendMode 映射
  const bm = (props.blendMode || '').toUpperCase()
  if (bm.includes('ADDITIVE')) {
    ctx.globalCompositeOperation = 'lighter'
  } else if (bm.includes('MODULATE')) {
    ctx.globalCompositeOperation = 'multiply'
  } else {
    ctx.globalCompositeOperation = 'source-over'
  }

  if (mode === 'ring_gauge') {
    // WACCA 环形进度条 / 音游外圈模拟 (uM_Ring00 等)
    const gauge = Math.min(1, Math.max(0, getScalar(/gauge$/i, (Math.sin(t * 1.5) * 0.5 + 0.5))))
    const widthRatio = Math.min(0.9, Math.max(0.08, getScalar(/gauge_width/i, 0.35)))
    const cx = W / 2
    const cy = H / 2
    const outerR = Math.min(W, H) * 0.42
    const innerR = outerR * (1 - widthRatio * 0.6)
    const lineW = outerR - innerR
    const midR = (outerR + innerR) / 2

    // Off_Color 底环
    if (col2) {
      ctx.beginPath()
      ctx.arc(cx, cy, midR, 0, Math.PI * 2)
      ctx.strokeStyle = `rgba(${Math.round(col2.r * 255)}, ${Math.round(col2.g * 255)}, ${Math.round(col2.b * 255)}, ${Math.max(0.25, col2.a)})`
      ctx.lineWidth = lineW
      ctx.stroke()
    }

    // On_Color 进度环
    const startAngle = -Math.PI / 2
    const endAngle = startAngle + Math.PI * 2 * gauge
    ctx.beginPath()
    ctx.arc(cx, cy, midR, startAngle, endAngle)
    ctx.strokeStyle = `rgba(${Math.round(col1.r * 255)}, ${Math.round(col1.g * 255)}, ${Math.round(col1.b * 255)}, ${Math.max(0.85, col1.a)})`
    ctx.lineWidth = lineW
    ctx.lineCap = 'round'
    ctx.stroke()

    // 如果有贴图，叠加旋转纹理光效
    if (img) {
      ctx.save()
      ctx.globalAlpha = 0.45
      ctx.translate(cx, cy)
      ctx.rotate(t * 0.5)
      ctx.drawImage(img, -outerR, -outerR, outerR * 2, outerR * 2)
      ctx.restore()
    }
  } else if (mode === 'uv_crop' && img) {
    // 8x8 图集 UV 裁切预览
    const u0 = getScalar(/^u0$/i, 0)
    const v0 = getScalar(/^v0$/i, 0)
    const cellW = img.naturalWidth / 8
    const cellH = img.naturalHeight / 8
    const sx = u0 * img.naturalWidth
    const sy = v0 * img.naturalHeight
    const pad = 28
    ctx.imageSmoothingEnabled = false
    ctx.drawImage(img, sx, sy, cellW, cellH, pad, pad, W - pad * 2, H - pad * 2)
  } else if (mode === 'glitch' && img) {
    // WACCA UI Glitch 故障切片抖动模拟 (uMF_HorizontalGlitch / uMF_VerticalGlitch)
    const glitchAmpH = getScalar(/glitchalphah|glitch/i, 0.04)
    const durationH = Math.max(0.05, getScalar(/timedurationh/i, 0.4))
    const spanH = Math.max(0, getScalar(/timespanh/i, 0.5))
    const cycle = durationH + spanH
    const inGlitch = (t % cycle) <= durationH

    const pad = 20
    const dstW = W - pad * 2
    const dstH = H - pad * 2
    const slices = 16
    const sliceSrcH = img.naturalHeight / slices
    const sliceDstH = dstH / slices

    for (let i = 0; i < slices; i++) {
      let offsetX = 0
      if (inGlitch) {
        const noise = Math.sin(i * 12.9898 + Math.floor(t * 24) * 78.233) * 43758.5453
        const frac = noise - Math.floor(noise)
        if (frac > 0.35) {
          offsetX = (frac - 0.5) * 2 * (glitchAmpH * 800)
        }
      }
      ctx.drawImage(
        img,
        0,
        i * sliceSrcH,
        img.naturalWidth,
        sliceSrcH,
        pad + offsetX,
        pad + i * sliceDstH,
        dstW,
        sliceDstH + 0.5
      )
    }
  } else if (img) {
    // UV 滚动 + CustomRotator 旋转 + 缩放 + 颜色混合模拟
    const speed = getScalar(/speed/i, 1.0)
    const rotParam = getScalar(/rotate/i, 0)
    const posU = getScalar(/position_u|u_offset/i, 0)
    const posV = getScalar(/position_v|v_offset/i, 0)
    const scaleU = Math.max(0.1, getScalar(/texcode_u|scale/i, 1.0))
    const scaleV = Math.max(0.1, getScalar(/texcode_v|scale/i, 1.0))

    const cx = W / 2
    const cy = H / 2
    const drawSize = Math.min(W, H) * 0.78

    ctx.save()
    ctx.translate(cx, cy)

    // 如果有 Rotate 参数或 CustomRotator 函数，执行旋转
    const hasRotator = (props.functionNames || []).some(f => /rotator/i.test(f)) || rotParam !== 0
    if (hasRotator) {
      const angle = rotParam * Math.PI * 2 + (playing.value ? t * speed * 0.8 : 0)
      ctx.rotate(angle)
    }

    // UV 平移滚动
    const shiftX = ((posU * t * speed * 40) % drawSize)
    const shiftY = ((posV * t * speed * 40) % drawSize)

    const wScaled = drawSize / scaleU
    const hScaled = drawSize / scaleV
    ctx.drawImage(
      img,
      -wScaled / 2 + shiftX * 0.25,
      -hScaled / 2 + shiftY * 0.25,
      wScaled,
      hScaled
    )

    // 如果存在明显非白色的颜色参数，做一层色彩叠加
    if (Object.keys(props.vectors).length > 0 && (col1.r < 0.98 || col1.g < 0.98 || col1.b < 0.98)) {
      ctx.globalCompositeOperation = 'source-atop'
      ctx.fillStyle = `rgba(${Math.round(col1.r * 255)}, ${Math.round(col1.g * 255)}, ${Math.round(col1.b * 255)}, 0.55)`
      ctx.fillRect(-drawSize, -drawSize, drawSize * 2, drawSize * 2)
    }

    ctx.restore()
  } else {
    // 无贴图时的纯色/双色动态渐变预览 (如 uM_CM_active00 等纯色闪烁 UI 材质)
    const speed = getScalar(/speed|move_flag/i, 1.0)
    const pulse = Math.sin(t * speed * 3.0) * 0.5 + 0.5
    const c2 = col2 || { r: col1.r * 0.4, g: col1.g * 0.4, b: col1.b * 0.4, a: col1.a }

    const r = Math.round((col1.r * pulse + c2.r * (1 - pulse)) * 255)
    const g = Math.round((col1.g * pulse + c2.g * (1 - pulse)) * 255)
    const b = Math.round((col1.b * pulse + c2.b * (1 - pulse)) * 255)

    const grad = ctx.createRadialGradient(W / 2, H / 2, 10, W / 2, H / 2, W * 0.45)
    grad.addColorStop(0, `rgba(${r}, ${g}, ${b}, 0.95)`)
    grad.addColorStop(1, `rgba(${Math.round(c2.r * 255)}, ${Math.round(c2.g * 255)}, ${Math.round(c2.b * 255)}, 0.25)`)

    ctx.fillStyle = grad
    ctx.beginPath()
    ctx.arc(W / 2, H / 2, W * 0.4, 0, Math.PI * 2)
    ctx.fill()
  }

  ctx.restore()
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
        <n-select
          v-model:value="simPreset"
          :options="presetOptions"
          size="tiny"
          style="width: 175px"
        />
      </div>
    </template>

    <div class="sim-body">
      <div class="canvas-stage" :class="`bg-${bgMode}`">
        <canvas ref="canvasRef" width="240" height="240" class="sim-canvas" />
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

        <div class="time-row">
          <span class="t-label">Time: {{ simTime.toFixed(2) }}s</span>
          <n-slider v-model:value="simTime" :min="0" :max="10" :step="0.02" style="flex: 1" />
        </div>

        <div v-if="textures.length > 1" class="tex-select-row">
          <span class="t-label">{{ tr('ui.baseTexture') }}:</span>
          <n-select
            v-model:value="selectedTexPath"
            :options="textures.map(t => ({ label: t.name || t.path.split('/').pop(), value: t.path }))"
            size="tiny"
            style="flex: 1"
          />
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
.time-row,
.tex-select-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
}
.t-label {
  color: #666;
  font-family: monospace;
  white-space: nowrap;
}
</style>
