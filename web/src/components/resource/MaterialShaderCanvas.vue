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
import { MaterialWebGLRenderer } from '../../utils/material-shader/MaterialWebGLRenderer'

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

// WebGL 着色器运行器
const webglRenderer = new MaterialWebGLRenderer()
let isWebGLReady = false

// 贴图缓存: path -> HTMLImageElement
const loadedImages = new Map<string, HTMLImageElement>()
// 遮罩贴图的像素亮度表缓存: path -> Uint8ClampedArray (width=64, height=64)
const maskLumaCache = new Map<string, Uint8ClampedArray>()
const imageUrls = new Map<string, string>()

// 判定材质本身是否含有时间驱动动画 (Time / Rotator / Panner / Glitch)
const hasTimeAnimation = computed(() => {
  const fnList = (props.functionNames || []).map(f => f.toLowerCase())
  const sKeys = Object.keys(props.scalars).map(k => k.toLowerCase())

  if (fnList.some(f => f.includes('rotator') || f.includes('panner') || f.includes('glitch') || f.includes('sine') || f.includes('crop'))) {
    return true
  }
  if (sKeys.some(k => k.includes('speed') || k.includes('pan') || k.includes('rotator') || k.includes('duration'))) {
    return true
  }
  return false
})

// 检测到的最佳模拟预设
const detectedPreset = computed(() => {
  const sKeys = Object.keys(props.scalars).map(k => k.toLowerCase())
  const fnList = (props.functionNames || []).map(f => f.toLowerCase())
  const nonMasks = props.textures.filter(t => !/mask/i.test(t.name) && !/mask/i.test(t.path))

  // 如果像 uM_Title008 这样既有多个图层又有旋转/Glitch，识别为多图层复合 (composite)
  if (nonMasks.length >= 2 && (fnList.some(f => f.includes('crop') || f.includes('rotator')) || props.textures.length >= 4)) {
    return 'composite'
  }
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
    sKeys.some(k => k.includes('rotate') || k.includes('speed') || k.includes('pan'))
  ) {
    return 'pan_rotate'
  }
  if (nonMasks.length >= 2) {
    return 'composite'
  }
  if (!hasTimeAnimation.value) {
    return 'static'
  }
  return 'color_tint'
})

const activeMode = computed(() => (simPreset.value === 'auto' ? detectedPreset.value : simPreset.value))

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

// 多图层选择: 主图、副图、遮罩图、物件图
const primaryTexPath = ref<string>('')
const secondaryTexPath = ref<string>('')
const maskTexPath = ref<string>('')
const objTexPath = ref<string>('')

watch(
  uniqueTextures,
  list => {
    if (!list?.length) {
      primaryTexPath.value = ''
      secondaryTexPath.value = ''
      maskTexPath.value = ''
      objTexPath.value = ''
      return
    }

    const masks = list.filter(t => /mask/i.test(t.name) || /mask/i.test(t.path) || /menuobj10/i.test(t.path))
    const nonMasks = list.filter(t => !masks.includes(t))

    // 遮罩图
    maskTexPath.value = masks.length > 0 ? masks[0].path : ''

    // 物件图 (如 uT_CM_obj05)
    const obj = nonMasks.find(t => /obj|ring|shape|star/i.test(t.name) || /obj/i.test(t.path))
    objTexPath.value = obj ? obj.path : ''

    // 主底图: 优先角色立绘、ApplyTo、主纹理 (applyto, chara, bg, tex01, tex_h)
    const pri =
      nonMasks.find(t => t.path !== objTexPath.value && (/applyto|chara|bg|main|tex01/i.test(t.name) || /chara|bg/i.test(t.path))) ||
      nonMasks.find(t => t.path !== objTexPath.value) ||
      list[0]
    primaryTexPath.value = pri ? pri.path : ''

    // 叠层图: 优先 Logo、装饰文字、次级纹理 (logo, icon, tex_v)
    const sec =
      nonMasks.find(t => t.path !== primaryTexPath.value && t.path !== objTexPath.value && (/logo|title|icon|tex_v|tex02/i.test(t.name) || /logo|title/i.test(t.path))) ||
      nonMasks.find(t => t.path !== primaryTexPath.value && t.path !== objTexPath.value)
    secondaryTexPath.value = sec ? sec.path : ''

    // 预加载所有关联贴图
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

function buildMaskLumaTable(path: string, img: HTMLImageElement) {
  try {
    const off = document.createElement('canvas')
    off.width = 64
    off.height = 64
    const octx = off.getContext('2d')
    if (!octx) return
    octx.drawImage(img, 0, 0, 64, 64)
    const data = octx.getImageData(0, 0, 64, 64).data
    const luma = new Uint8ClampedArray(64 * 64)
    for (let i = 0; i < 64 * 64; i++) {
      // 取 R 通道与 Alpha 的乘积作为遮罩强度
      const r = data[i * 4]
      const g = data[i * 4 + 1]
      const b = data[i * 4 + 2]
      const a = data[i * 4 + 3] / 255
      luma[i] = Math.round(Math.max(r, g, b) * a)
    }
    maskLumaCache.set(path, luma)
  } catch {
    // ignore
  }
}

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
      buildMaskLumaTable(path, img)
      if (webglRenderer) {
        webglRenderer.updateTexture(path, img)
      }
    }
    img.src = url
  } catch {
    // ignore
  }
}

function sampleMask(path: string, u: number, v: number): number {
  const table = maskLumaCache.get(path)
  if (!table) return 0
  const x = ((Math.floor(u * 64) % 64) + 64) % 64
  const y = ((Math.floor(v * 64) % 64) + 64) % 64
  return (table[y * 64 + x] / 255)
}

function getScalar(pattern: RegExp, fallback: number): number {
  for (const [k, v] of Object.entries(props.scalars)) {
    if (pattern.test(k)) return Number(v) ?? fallback
  }
  return fallback
}

function getPrimaryColor(): { r: number; g: number; b: number; a: number } {
  const entries = Object.entries(props.vectors)
  if (!entries.length) return { r: 1, g: 1, b: 1, a: 1 }
  const preferred =
    entries.find(([k]) => /on_color|color0|color1|color_1|tint|main/i.test(k)) || entries[0]
  return preferred[1]
}

function getSecondaryColor(): { r: number; g: number; b: number; a: number } | null {
  const entries = Object.entries(props.vectors)
  if (entries.length < 2) return null
  const sec = entries.find(([k]) => /off_color|color1|color2|color_2|sub/i.test(k)) || entries[1]
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

function applyBlendMode(ctx: CanvasRenderingContext2D) {
  const bm = (props.blendMode || '').toUpperCase()
  if (bm.includes('ADDITIVE')) {
    ctx.globalCompositeOperation = 'lighter'
  } else if (bm.includes('MODULATE')) {
    ctx.globalCompositeOperation = 'multiply'
  } else {
    ctx.globalCompositeOperation = 'source-over'
  }
}

function drawAspectFit(
  ctx: CanvasRenderingContext2D,
  img: HTMLImageElement,
  W: number,
  H: number,
  pad = 12
) {
  const dstW = W - pad * 2
  const dstH = H - pad * 2
  const aspect = img.naturalWidth / Math.max(1, img.naturalHeight)
  let renderW = dstW
  let renderH = dstW / aspect
  if (renderH > dstH) {
    renderH = dstH
    renderW = dstH * aspect
  }
  const rx = (W - renderW) / 2
  const ry = (H - renderH) / 2
  ctx.drawImage(img, rx, ry, renderW, renderH)
}

function drawCanvas() {
  const t = simTime.value

  // 若处于 WebGL 模式，调用 WebGL 管线进行像素级物理着色渲染
  if (renderBackend.value === 'webgl') {
    const glCanvas = webglCanvasRef.value
    if (!glCanvas) return
    const W = glCanvas.width
    const H = glCanvas.height

    if (!isWebGLReady) {
      isWebGLReady = webglRenderer.init(glCanvas)
      // 同步当前所有已载入的贴图至 WebGL 纹理单元
      for (const [path, img] of loadedImages.entries()) {
        webglRenderer.updateTexture(path, img)
      }
    }
    if (isWebGLReady) {
      webglRenderer.render(
        W,
        H,
        {
          time: t,
          resolution: [W, H],
          scalars: props.scalars,
          vectors: props.vectors
        },
        {
          primary: primaryTexPath.value,
          secondary: secondaryTexPath.value,
          mask: maskTexPath.value,
          obj: objTexPath.value
        },
        props.blendMode
      )
      return
    }
  }

  // Fallback: Canvas 2D 模拟渲染
  const canvas = canvasRef.value
  if (!canvas) return
  const ctx = canvas.getContext('2d')
  if (!ctx) return

  const W = canvas.width
  const H = canvas.height
  ctx.clearRect(0, 0, W, H)

  const mode = activeMode.value
  const imgPrimary = primaryTexPath.value ? loadedImages.get(primaryTexPath.value) : undefined
  const imgSecondary = secondaryTexPath.value ? loadedImages.get(secondaryTexPath.value) : undefined

  const col1 = getPrimaryColor()
  const col2 = getSecondaryColor()
  const opacity = Math.min(1, Math.max(0, getScalar(/^opacity$/i, 1.0)))

  ctx.save()
  ctx.globalAlpha = opacity

  if (mode === 'static') {
    // 静态展示模式（无动画材质默认进入此模式）
    if (imgPrimary) {
      applyBlendMode(ctx)
      drawAspectFit(ctx, imgPrimary, W, H, 12)
      if (imgSecondary) {
        ctx.globalAlpha = opacity * 0.9
        drawAspectFit(ctx, imgSecondary, W, H, 12)
      }
    } else {
      ctx.fillStyle = `rgba(${Math.round(col1.r * 255)}, ${Math.round(col1.g * 255)}, ${Math.round(col1.b * 255)}, ${col1.a})`
      ctx.fillRect(24, 24, W - 48, H - 48)
    }
  } else if (mode === 'composite') {
    // 多图层复合渲染 (如 uM_Title008: 背景色彩 + 几何物件展开 + 角色立绘缩放/旋转 + Logo 叠层 + 局部 Glitch)
    const pad = 10
    const dstW = W - pad * 2
    const dstH = H - pad * 2

    // 1. 若是不透明背景材质且带背景色参数，铺设渐变底色
    if ((props.blendMode || '').includes('Opaque') && Object.keys(props.vectors).length > 0) {
      const c2 = col2 || col1
      const bgGrad = ctx.createLinearGradient(0, 0, W, H)
      bgGrad.addColorStop(0, `rgba(${Math.round(col1.r * 255)}, ${Math.round(col1.g * 255)}, ${Math.round(col1.b * 255)}, 0.85)`)
      bgGrad.addColorStop(1, `rgba(${Math.round(c2.r * 255)}, ${Math.round(c2.g * 255)}, ${Math.round(c2.b * 255)}, 0.85)`)
      ctx.fillStyle = bgGrad
      ctx.fillRect(pad, pad, dstW, dstH)
    }

    // 1.5. 几何物件层 (如 uT_CM_obj05)，受 uMF_TexCropRotate 与 move_1 / Rotator 驱动旋转缩放
    const imgObj = objTexPath.value ? loadedImages.get(objTexPath.value) : undefined
    if (imgObj) {
      ctx.save()
      const moveSpeed = Math.max(0.1, getScalar(/move_1|speed_1/i, 0.2))
      const objScale = 1.0 + (playing.value ? (t * moveSpeed) % 0.35 : 0)
      ctx.translate(W / 2, H / 2)
      ctx.scale(objScale, objScale)
      ctx.globalAlpha = opacity * 0.55
      ctx.drawImage(imgObj, -dstW / 2, -dstH / 2, dstW, dstH)
      ctx.restore()
    }

    // 2. 主图层（如 uT_Chara000_EV000 角色立绘），支持 uMF_TexCropRotate 呼吸放大动画与 CustomRotator
    if (imgPrimary) {
      ctx.save()
      const hasCropRotate = (props.functionNames || []).some(f => /texcroprotate/i.test(f))
      const zoom = hasCropRotate && playing.value ? 1.0 + (t * 0.12) % 0.18 : 1.0
      ctx.translate(W / 2, H / 2)
      ctx.scale(zoom, zoom)
      ctx.drawImage(imgPrimary, -dstW / 2, -dstH / 2, dstW, dstH)
      ctx.restore()
    }

    // 3. 叠层（如 uT_TTlogo07 标题 Logo），若带 Glitch 函数则对叠层施加遮罩 Glitch 偏移
    if (imgSecondary) {
      ctx.save()
      const hasGlitch = (props.functionNames || []).some(f => /glitch/i.test(f))
      const glitchAlpha = getScalar(/glitchalpha|offset_1|offset/i, 0.03)
      const slices = 16
      const sliceSrcH = imgSecondary.naturalHeight / slices
      const sliceDstH = dstH / slices

      for (let i = 0; i < slices; i++) {
        let offsetX = 0
        if (hasGlitch && playing.value) {
          if (maskTexPath.value && maskLumaCache.has(maskTexPath.value)) {
            const mVal = sampleMask(maskTexPath.value, 0.5, i / slices + t * 0.6)
            if (mVal > 0.2) {
              offsetX = (mVal - 0.5) * 2 * (glitchAlpha * 900)
            }
          } else if (Math.sin(t * 8 + i * 3) > 0.65) {
            offsetX = Math.sin(t * 25 + i) * (glitchAlpha * 600)
          }
        }
        ctx.drawImage(
          imgSecondary,
          0,
          i * sliceSrcH,
          imgSecondary.naturalWidth,
          sliceSrcH,
          pad + offsetX,
          pad + i * sliceDstH,
          dstW,
          sliceDstH + 0.5
        )
      }
      ctx.restore()
    }
  } else if (mode === 'glitch') {
    // 真实 Mask 遮罩采样驱动的 Glitch 故障模拟 (uM_GlitchSample / uM_Glitch2_1)
    const targetImg = imgPrimary || imgSecondary
    if (targetImg) {
      const glitchAlpha = getScalar(/glitchalpha|offseth|offsetv|offset/i, 0.03)
      const duration = Math.max(0.05, getScalar(/duration/i, 0.5))
      const span = Math.max(0, getScalar(/span/i, 0.5))
      const speed = getScalar(/speed/i, 0.5)
      const cycle = Math.max(0.1, duration + span)
      const inGlitch = !playing.value || (t % cycle) <= duration

      const pad = 14
      const dstW = W - pad * 2
      const dstH = H - pad * 2
      const slices = 24
      const sliceSrcH = targetImg.naturalHeight / slices
      const sliceDstH = dstH / slices

      applyBlendMode(ctx)

      for (let i = 0; i < slices; i++) {
        let offsetX = 0
        let offsetY = 0
        if (inGlitch) {
          const vCoord = i / slices
          if (maskTexPath.value && maskLumaCache.has(maskTexPath.value)) {
            // 采样真实的 GlitchMask (如 uT_GlitchMaskV / uT_STop_menuobj10) 随时间 Panner 滚动后的亮度值
            const maskLumaH = sampleMask(maskTexPath.value, t * speed * 2.0, vCoord + t * speed)
            const maskLumaV = sampleMask(maskTexPath.value, vCoord + t * speed, t * speed * 1.5)
            if (maskLumaH > 0.15) {
              offsetX = (maskLumaH - 0.5) * 2 * (glitchAlpha * 950)
            }
            if (maskLumaV > 0.35) {
              offsetY = (maskLumaV - 0.5) * (glitchAlpha * 300)
            }
          } else {
            const noise = Math.sin(i * 12.9898 + Math.floor(t * 18) * 78.233) * 43758.5453
            const frac = noise - Math.floor(noise)
            if (frac > 0.35) {
              offsetX = (frac - 0.5) * 2 * (glitchAlpha * 800)
            }
          }
        }

        ctx.drawImage(
          targetImg,
          0,
          i * sliceSrcH,
          targetImg.naturalWidth,
          sliceSrcH,
          pad + offsetX,
          pad + i * sliceDstH + offsetY,
          dstW,
          sliceDstH + 0.6
        )
      }

      // 如果有第二张贴图 (如 uM_Glitch2_1 的 Tex_H + Tex_V 双层交织)，叠加第二层
      if (imgSecondary && imgSecondary !== targetImg) {
        ctx.save()
        ctx.globalAlpha = opacity * 0.65
        ctx.globalCompositeOperation = 'lighter'
        for (let i = 0; i < slices; i += 2) {
          const vCoord = i / slices
          const mVal = maskTexPath.value
            ? sampleMask(maskTexPath.value, 0.3, vCoord - t * speed)
            : 0.5
          const shiftX = inGlitch ? (mVal - 0.5) * (glitchAlpha * 700) : 0
          ctx.drawImage(
            imgSecondary,
            0,
            (i / slices) * imgSecondary.naturalHeight,
            imgSecondary.naturalWidth,
            imgSecondary.naturalHeight / slices,
            pad + shiftX,
            pad + i * sliceDstH,
            dstW,
            sliceDstH
          )
        }
        ctx.restore()
      }
    }
  } else if (mode === 'ring_gauge') {
    // 环形进度条 / 音游外圈模拟 (uM_Ring00)
    const baseGauge = getScalar(/gauge$/i, 0.75)
    const gauge = playing.value
      ? Math.min(1, Math.max(0, Math.sin(t * 1.5) * 0.5 + 0.5))
      : Math.min(1, Math.max(0, baseGauge > 0 ? baseGauge : 0.75))
    const widthRatio = Math.min(0.9, Math.max(0.08, getScalar(/gauge_width/i, 0.35)))
    const cx = W / 2
    const cy = H / 2
    const outerR = Math.min(W, H) * 0.42
    const innerR = outerR * (1 - widthRatio * 0.6)
    const lineW = outerR - innerR
    const midR = (outerR + innerR) / 2

    if (col2) {
      ctx.beginPath()
      ctx.arc(cx, cy, midR, 0, Math.PI * 2)
      ctx.strokeStyle = `rgba(${Math.round(col2.r * 255)}, ${Math.round(col2.g * 255)}, ${Math.round(col2.b * 255)}, ${Math.max(0.25, col2.a)})`
      ctx.lineWidth = lineW
      ctx.stroke()
    }

    const startAngle = -Math.PI / 2
    const endAngle = startAngle + Math.PI * 2 * gauge
    ctx.beginPath()
    ctx.arc(cx, cy, midR, startAngle, endAngle)
    ctx.strokeStyle = `rgba(${Math.round(col1.r * 255)}, ${Math.round(col1.g * 255)}, ${Math.round(col1.b * 255)}, ${Math.max(0.85, col1.a)})`
    ctx.lineWidth = lineW
    ctx.lineCap = 'round'
    ctx.stroke()

    if (imgPrimary) {
      ctx.save()
      ctx.globalAlpha = 0.45
      ctx.translate(cx, cy)
      if (playing.value) ctx.rotate(t * 0.5)
      ctx.drawImage(imgPrimary, -outerR, -outerR, outerR * 2, outerR * 2)
      ctx.restore()
    }
  } else if (mode === 'uv_crop' && imgPrimary) {
    // 8x8 图集 UV 裁切预览
    const u0 = getScalar(/^u0$/i, 0)
    const v0 = getScalar(/^v0$/i, 0)
    const cellW = imgPrimary.naturalWidth / 8
    const cellH = imgPrimary.naturalHeight / 8
    const sx = u0 * imgPrimary.naturalWidth
    const sy = v0 * imgPrimary.naturalHeight
    const pad = 28
    ctx.imageSmoothingEnabled = false
    applyBlendMode(ctx)
    ctx.drawImage(imgPrimary, sx, sy, cellW, cellH, pad, pad, W - pad * 2, H - pad * 2)
  } else if (imgPrimary) {
    // UV 滚动 + CustomRotator 旋转 + 缩放 + 颜色混合模拟
    const speed = getScalar(/speed/i, 1.0)
    const rotParam = getScalar(/rotate/i, 0)
    const posU = getScalar(/position_u|u_offset|posu/i, 0)
    const posV = getScalar(/position_v|v_offset|posv/i, 0)
    const scaleU = Math.max(0.1, getScalar(/texcode_u|scale/i, 1.0))
    const scaleV = Math.max(0.1, getScalar(/texcode_v|scale/i, 1.0))

    const cx = W / 2
    const cy = H / 2
    const drawSize = Math.min(W, H) * 0.78

    ctx.save()
    ctx.translate(cx, cy)

    const hasRotator = (props.functionNames || []).some(f => /rotator/i.test(f)) || rotParam !== 0
    if (hasRotator) {
      const angle = rotParam * Math.PI * 2 + (playing.value ? t * (speed || 0.5) * 0.8 : 0)
      ctx.rotate(angle)
    }

    const shiftX = playing.value ? ((posU * t * (speed || 1) * 40) % drawSize) : posU * 40
    const shiftY = playing.value ? ((posV * t * (speed || 1) * 40) % drawSize) : posV * 40

    const wScaled = drawSize / scaleU
    const hScaled = drawSize / scaleV
    applyBlendMode(ctx)
    ctx.drawImage(
      imgPrimary,
      -wScaled / 2 + shiftX * 0.25,
      -hScaled / 2 + shiftY * 0.25,
      wScaled,
      hScaled
    )

    if (imgSecondary) {
      ctx.globalAlpha = opacity * 0.8
      ctx.drawImage(imgSecondary, -wScaled / 2, -hScaled / 2, wScaled, hScaled)
    }

    if (Object.keys(props.vectors).length > 0 && (col1.r < 0.98 || col1.g < 0.98 || col1.b < 0.98)) {
      ctx.globalCompositeOperation = 'source-atop'
      ctx.fillStyle = `rgba(${Math.round(col1.r * 255)}, ${Math.round(col1.g * 255)}, ${Math.round(col1.b * 255)}, 0.55)`
      ctx.fillRect(-drawSize, -drawSize, drawSize * 2, drawSize * 2)
    }

    ctx.restore()
  } else {
    // 无贴图时的纯色/双色动态渐变预览
    const speed = getScalar(/speed|move_flag/i, 1.0)
    const pulse = playing.value ? Math.sin(t * speed * 3.0) * 0.5 + 0.5 : 0.85
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
  if (webglRenderer) {
    webglRenderer.destroy()
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

          <div v-if="uniqueTextures.length > 1" class="layer-row">
            <span class="t-label">{{ tr('ui.simMaskLayer') }}:</span>
            <n-select
              v-model:value="maskTexPath"
              :options="[{ label: tr('ui.viewerEmpty'), value: '' }, ...uniqueTextures.map(t => ({ label: t.name || t.path.split('/').pop(), value: t.path }))]"
              size="tiny"
              style="flex: 1"
            />
          </div>

          <div v-if="uniqueTextures.length > 2" class="layer-row">
            <span class="t-label">{{ tr('ui.simObjLayer') }}:</span>
            <n-select
              v-model:value="objTexPath"
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