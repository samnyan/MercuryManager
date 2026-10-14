<script setup lang="ts">
import { ref, computed, watch, onMounted, nextTick } from 'vue'
import { tr } from '../../i18n'
import type { ParticleCutoutConfiguration } from '../../utils/material-runtime/ParticleProgramDiagnostics'

const props = defineProps<{
  cutout?: ParticleCutoutConfiguration | null
}>()

const canvasRef = ref<HTMLCanvasElement | null>(null)

const coordinates = computed(() => props.cutout?.CookedGeometry?.Coordinates || [])
const vertexCount = computed(() => props.cutout?.CookedGeometry?.VertexCount || 0)

function renderOutline() {
  const canvas = canvasRef.value
  if (!canvas) return
  const ctx = canvas.getContext('2d')
  if (!ctx) return

  const width = canvas.width
  const height = canvas.height
  ctx.clearRect(0, 0, width, height)

  // 绘制深色背景与诊断网格
  ctx.fillStyle = '#18181c'
  ctx.fillRect(0, 0, width, height)

  ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)'
  ctx.lineWidth = 1
  const step = 20
  for (let x = 0; x <= width; x += step) {
    ctx.beginPath()
    ctx.moveTo(x, 0)
    ctx.lineTo(x, height)
    ctx.stroke()
  }
  for (let y = 0; y <= height; y += step) {
    ctx.beginPath()
    ctx.moveTo(0, y)
    ctx.lineTo(width, y)
    ctx.stroke()
  }

  const coords = coordinates.value
  if (coords.length < 4) {
    // 无足够几何坐标时的提示
    ctx.fillStyle = '#888'
    ctx.font = '11px sans-serif'
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.fillText(tr('ui.particleCutoutEmpty'), width / 2, height / 2)
    return
  }

  // 提取点
  const points: { x: number; y: number }[] = []
  let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity
  for (let i = 0; i < coords.length; i += 2) {
    const x = coords[i]
    const y = coords[i + 1]
    points.push({ x, y })
    if (x < minX) minX = x
    if (x > maxX) maxX = x
    if (y < minY) minY = y
    if (y > maxY) maxY = y
  }

  // 坐标映射到画布（带边距 16px，保持纵横比与严格序列，UE UV 空间 Y 轴向下）
  const pad = 16
  const spanX = Math.max(maxX - minX, 0.0001)
  const spanY = Math.max(maxY - minY, 0.0001)
  const scale = Math.min((width - pad * 2) / spanX, (height - pad * 2) / spanY)
  const offX = (width - spanX * scale) / 2 - minX * scale
  const offY = (height - spanY * scale) / 2 - minY * scale

  // 绘制轮廓连线 (保持原始坐标序列)
  ctx.strokeStyle = '#2080f0'
  ctx.lineWidth = 2
  ctx.fillStyle = 'rgba(32, 128, 240, 0.15)'
  ctx.beginPath()
  points.forEach((p, idx) => {
    const cx = p.x * scale + offX
    const cy = p.y * scale + offY
    if (idx === 0) ctx.moveTo(cx, cy)
    else ctx.lineTo(cx, cy)
  })
  ctx.closePath()
  ctx.fill()
  ctx.stroke()

  // 绘制顶点与顶点序号
  points.forEach((p, idx) => {
    const cx = p.x * scale + offX
    const cy = p.y * scale + offY
    ctx.fillStyle = '#10b981'
    ctx.beginPath()
    ctx.arc(cx, cy, 3, 0, Math.PI * 2)
    ctx.fill()

    ctx.fillStyle = '#a1a1aa'
    ctx.font = '9px monospace'
    ctx.textAlign = 'left'
    ctx.fillText(`${idx}`, cx + 5, cy - 3)
  })
}

watch(coordinates, () => {
  nextTick(renderOutline)
}, { immediate: true })

onMounted(() => {
  nextTick(renderOutline)
})
</script>

<template>
  <div class="cutout-preview-box">
    <div class="cutout-header">
      <span class="cutout-title">{{ tr('ui.particleCutoutTitle') }}</span>
      <span class="cutout-badge">
        {{ tr('ui.particleCutoutPoints') }}: {{ vertexCount }}
      </span>
    </div>
    <div class="cutout-canvas-wrap">
      <canvas ref="canvasRef" width="340" height="180" class="cutout-canvas" />
    </div>
    <div class="cutout-footer">
      <span class="cutout-hint">{{ tr('ui.particleCutoutDesc') }}</span>
      <span v-if="cutout?.State" class="cutout-state">State: {{ cutout.State }}</span>
    </div>
  </div>
</template>

<style scoped>
.cutout-preview-box {
  background: rgba(128, 128, 128, 0.05);
  border: 1px solid rgba(128, 128, 128, 0.16);
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.cutout-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 11px;
}

.cutout-title {
  font-weight: 600;
  color: #a1a1aa;
}

.cutout-badge {
  font-family: monospace;
  font-size: 10px;
  color: #10b981;
  background: rgba(16, 185, 129, 0.1);
  padding: 1px 6px;
  border-radius: 4px;
}

.cutout-canvas-wrap {
  width: 100%;
  display: flex;
  justify-content: center;
  background: #18181c;
  border-radius: 4px;
  overflow: hidden;
}

.cutout-canvas {
  width: 100%;
  height: auto;
  max-height: 180px;
  display: block;
}

.cutout-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 10px;
  color: #71717a;
  gap: 8px;
}

.cutout-hint {
  font-style: italic;
}

.cutout-state {
  font-family: monospace;
  color: #eab308;
}
</style>
