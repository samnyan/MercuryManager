<script setup lang="ts">
import { MaterialSurface } from '../../../utils/material-runtime/MaterialSurface'
import { materialPrograms } from '../../../utils/material-runtime/MaterialProgramCache'
import { tr } from '../../../i18n'
import { computed, ref, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { NButton } from 'naive-ui'
import { Canvas, Rect, Polygon, Circle, FabricImage, FabricText, Point, util, type FabricObject } from 'fabric'
import type { UMGNode, ComputedLayout } from './types'
import { useProject, api } from '../../../project'
import { computeRenderTransforms, multiply, type Matrix } from './transforms'
import { widgetClip } from './clipping'
import { normalizeMaterialEntry, renderUMGMaterialToCanvas } from '../../../utils/material-shader/UMGMaterialBridge'
import { renderUMGParticleToCanvas } from '../../../utils/particle-system/ParticleRuntime'

const props = defineProps<{
  nodes: UMGNode[]
  layouts: Map<string, ComputedLayout>
  materials?: Record<string, any>
  particles?: Record<string, any>
  animationMaterialParameters?: Record<string, Record<string, number>>
  materialTime?: number
  selectedId: string | null
  hiddenIds: Set<string>
  viewport: { width: number; height: number }
  viewMode: 'appearance' | 'structure'
}>()

const fpsDisplay = ref<number>(0)
let fpsFrameCount = 0
let fpsLastTime = performance.now()
let stageRafId = 0
let autoShaderClock = 0
let lastRafTime = performance.now()

const emit = defineEmits<{
  'select-node': [id: string]
}>()

const project = useProject()
const stageViewport = ref<HTMLElement | null>(null)
const canvasEl = ref<HTMLCanvasElement | null>(null)
const zoomPercent = ref(50)

let fabricCanvas: Canvas | null = null
let resizeObserver: ResizeObserver | null = null
let renderGeneration = 0
let syncingSelection = false

// 节点 ID <-> FabricObject 映射
const objectByNodeId = new Map<string, FabricObject>()
const nodeIdByObject = new WeakMap<FabricObject, string>()

// 图像与着色缓存
const rawImageCache = new Map<string, Promise<HTMLImageElement | null>>()
const tintedSourceCache = new Map<string, HTMLImageElement | HTMLCanvasElement>()
const materialSurfaces = new Map<string, Promise<MaterialSurface>>()
const particleCanvasCache = new Map<string, Promise<HTMLCanvasElement | null>>()

const finite = (value: unknown, fallback: number) =>
  typeof value === 'number' && Number.isFinite(value) ? value : fallback

function linearToSrgbChannel(value: number): number {
  const v = Math.max(0, Math.min(1, value))
  const srgb = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.pow(v, 1 / 2.4) - 0.055
  return Math.round(Math.max(0, Math.min(255, srgb * 255)))
}

function extractColor(raw: any): { r: number; g: number; b: number; a: number } | null {
  const c = raw?.SpecifiedColor ?? raw
  if (!c || (c.R === undefined && c.G === undefined && c.B === undefined)) return null
  return {
    r: linearToSrgbChannel(finite(c.R, 1)),
    g: linearToSrgbChannel(finite(c.G, 1)),
    b: linearToSrgbChannel(finite(c.B, 1)),
    a: Math.max(0, Math.min(1, finite(c.A, 1)))
  }
}

function getImageUrl(node: UMGNode): string | null {
  const brushPath = node.visual.Brush?.ResourceObject?.assetPath
  const texRef =
    node.resources?.find(r => r.className === 'Texture2D' && r.assetPath === brushPath)
  if (!texRef || !project.id) return null
  return `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: texRef.assetPath, mip: '0' })
}

function getMaterialRef(node: UMGNode): { assetPath: string; className: string } | null {
  const brushPath = node.visual.Brush?.ResourceObject?.assetPath
  const isMat = (c?: string | null) => c === 'Material' || c === 'MaterialInstanceConstant' || c === 'MaterialInstance'
  const matRef =
    (brushPath ? node.resources?.find(r => isMat(r.className) && r.assetPath === brushPath) : null) ||
    node.resources?.find(r => isMat(r.className))
  return matRef && matRef.assetPath ? { assetPath: matRef.assetPath, className: matRef.className || 'Material' } : null
}

async function getMaterialCanvasSource(
  matPath: string,
  targetW: number,
  targetH: number,
  animatedScalars?: Record<string, number>,
  nodeId = matPath
): Promise<HTMLCanvasElement | null> {
  if (!props.materials || !props.materials[matPath]) return null
  const cacheKey = `${project.id}|${nodeId}|${matPath}`

  const spec = normalizeMaterialEntry(props.materials[matPath], matPath)
  if (!spec) return null
  if(animatedScalars) spec.scalars={...spec.scalars,...animatedScalars}

  // 尝试获取后端 WebGL2 Shader 契约 (若为 Master Material)
  let contract: any = null
  if (project.id && matPath.endsWith('.uasset')) {
    try {
      const q = new URLSearchParams({ path: matPath, time: '0', previousTime: '0' })
      if (Object.keys(spec.scalars).length > 0) {
        q.set('scalarOverrides', JSON.stringify(spec.scalars))
      }
      const vectors = Object.fromEntries(Object.entries(spec.vectors).map(([name, v]) => [name, [v.r, v.g, v.b, v.a]]))
      if (Object.keys(vectors).length > 0) q.set('vectorOverrides', JSON.stringify(vectors))
      contract = await materialPrograms.load(project.id, matPath)
    } catch {}
  }

  if (contract?.renderingReady && contract.uniformProgram) {
    let pending = materialSurfaces.get(cacheKey)
    if (!pending) {
      pending = (async () => {
        const surface = new MaterialSurface(contract, targetW, targetH, 'opaque-stage')
        try {
          await surface.prepare(texPath => loadRawImage(`/api/projects/${project.id}/resource-image?` + new URLSearchParams({path:texPath,mip:'0'})))
          return surface
        } catch (error) { surface.dispose(); throw error }
      })()
      materialSurfaces.set(cacheKey, pending)
      pending.catch(() => { if (materialSurfaces.get(cacheKey) === pending) materialSurfaces.delete(cacheKey) })
    }
    try {
      const surface = await pending
      if (materialSurfaces.get(cacheKey) !== pending) return null
      surface.resize(targetW, targetH)
      surface.instance.setParameters(spec.scalars, Object.fromEntries(Object.entries(spec.vectors).map(([k,v])=>[k,[v.r,v.g,v.b,v.a]])))
      return surface.draw(props.materialTime ?? 0)
    } catch { return null }
  }
  const promise = renderUMGMaterialToCanvas(
    spec,
    async (texPath: string) => {
      if (!project.id || !texPath) return null
      const url = `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: texPath, mip: '0' })
      return await loadRawImage(url)
    },
    targetW,
    targetH,
    props.materialTime ?? 0,
    contract
  )

  return promise
}

function getParticleRef(node: UMGNode): { assetPath: string; className: string } | null {
  const isPart = (r: { className?: string | null; assetPath?: string }) =>
    r.className === 'ParticleSystem' ||
    (typeof r.assetPath === 'string' && r.assetPath.toLowerCase().includes('particle'))
  const ref = node.resources?.find(isPart)
  return ref && ref.assetPath ? { assetPath: ref.assetPath, className: ref.className || 'ParticleSystem' } : null
}

async function getParticleCanvasSource(
  particlePath: string,
  targetW: number,
  targetH: number
): Promise<HTMLCanvasElement | null> {
  if (!props.particles || !props.particles[particlePath]) return null
  const cacheKey = `${particlePath}|${Math.round(targetW)}x${Math.round(targetH)}`
  const cached = particleCanvasCache.get(cacheKey)
  if (cached) return cached

  const promise = renderUMGParticleToCanvas(
    props.particles[particlePath],
    async (texPath: string) => {
      if (!project.id || !texPath) return null
      const url = `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: texPath, mip: '0' })
      return await loadRawImage(url)
    },
    targetW,
    targetH,
    props.materials,
    0.6,
    project.id
  )

  particleCanvasCache.set(cacheKey, promise)
  return promise
}

function loadRawImage(url: string): Promise<HTMLImageElement | null> {
  const cached = rawImageCache.get(url)
  if (cached) return cached
  const promise = new Promise<HTMLImageElement | null>(resolve => {
    const img = new Image()
    img.crossOrigin = 'anonymous'
    img.onload = () => resolve(img)
    img.onerror = () => resolve(null)
    img.src = url
  })
  rawImageCache.set(url, promise)
  return promise
}

async function getTintedImageSource(
  url: string,
  tint: { r: number; g: number; b: number; a: number } | null
): Promise<HTMLImageElement | HTMLCanvasElement | null> {
  const tintKey = tint ? `${url}|${tint.r},${tint.g},${tint.b}` : url
  const cached = tintedSourceCache.get(tintKey)
  if (cached) return cached

  const img = await loadRawImage(url)
  if (!img || !img.naturalWidth || !img.naturalHeight) return null

  if (!tint || (tint.r >= 250 && tint.g >= 250 && tint.b >= 250)) {
    tintedSourceCache.set(tintKey, img)
    return img
  }

  const offscreen = document.createElement('canvas')
  offscreen.width = img.naturalWidth
  offscreen.height = img.naturalHeight
  const ctx = offscreen.getContext('2d')
  if (!ctx) return img

  ctx.drawImage(img, 0, 0)
  ctx.globalCompositeOperation = 'multiply'
  ctx.fillStyle = `rgb(${tint.r}, ${tint.g}, ${tint.b})`
  ctx.fillRect(0, 0, offscreen.width, offscreen.height)
  ctx.globalCompositeOperation = 'destination-in'
  ctx.drawImage(img, 0, 0)

  tintedSourceCache.set(tintKey, offscreen)
  return offscreen
}

const nodeById = computed(() => new Map(props.nodes.map(n => [n.id, n])))

function effectiveOpacity(node: UMGNode): number {
  const seen = new Set<string>()
  let opacity = 1
  let current: UMGNode | undefined = node
  while (current && !seen.has(current.id)) {
    seen.add(current.id)
    opacity *= finite(current.visual.RenderOpacity, 1)
    const color = extractColor(current.visual.ColorAndOpacity)
    if (color) opacity *= color.a
    current = current.parentId ? nodeById.value.get(current.parentId) : undefined
  }
  return Math.max(0, Math.min(1, opacity))
}

function accumulatedTransform(node: UMGNode) {
  const chain: UMGNode[] = []
  const seen = new Set<string>()
  let cur: UMGNode | undefined = node
  while (cur && !seen.has(cur.id)) {
    seen.add(cur.id)
    chain.unshift(cur)
    cur = cur.parentId ? nodeById.value.get(cur.parentId) : undefined
  }

  let tx = 0
  let ty = 0
  let sx = 1
  let sy = 1
  let angle = 0
  for (const item of chain) {
    const t = item.visual.RenderTransform
    if (!t) continue
    tx += finite(t.Translation?.X, 0) * sx
    ty += finite(t.Translation?.Y, 0) * sy
    sx *= finite(t.Scale?.X, 1)
    sy *= finite(t.Scale?.Y, 1)
    angle += finite(t.Angle, 0)
  }

  const pivotX = finite(node.visual.RenderTransformPivot?.X, 0.5)
  const pivotY = finite(node.visual.RenderTransformPivot?.Y, 0.5)
  return { tx, ty, sx, sy, angle, pivotX, pivotY }
}

function fit() {
  if (!fabricCanvas || !stageViewport.value) return
  const cw = stageViewport.value.clientWidth || 800
  const ch = stageViewport.value.clientHeight || 600
  const vw = props.viewport.width
  const vh = props.viewport.height
  const s = Math.max(0.1, Math.min((cw - 24) / vw, (ch - 24) / vh))
  const panX = (cw - vw * s) / 2
  const panY = (ch - vh * s) / 2
  fabricCanvas.setViewportTransform([s, 0, 0, s, panX, panY])
  zoomPercent.value = Math.round(s * 100)
  fabricCanvas.requestRenderAll()
}

function zoomBy(delta: number) {
  if (!fabricCanvas || !stageViewport.value) return
  const current = fabricCanvas.getZoom()
  const next = Math.max(0.1, Math.min(4, Number((current + delta).toFixed(2))))
  const center = new Point(stageViewport.value.clientWidth / 2, stageViewport.value.clientHeight / 2)
  fabricCanvas.zoomToPoint(center, next)
  zoomPercent.value = Math.round(next * 100)
  fabricCanvas.requestRenderAll()
}

function applySelectionStyle(obj: FabricObject) {
  obj.set({
    hasControls: false,
    lockMovementX: true,
    lockMovementY: true,
    lockRotation: true,
    lockScalingX: true,
    lockScalingY: true,
    borderColor: '#18a058',
    borderScaleFactor: 2,
    padding: 0
  })
}

async function rebuildScene() {
  if (!fabricCanvas) return
  const gen = ++renderGeneration

  fabricCanvas.discardActiveObject()
  fabricCanvas.clear()
  objectByNodeId.clear()

  const vw = props.viewport.width
  const vh = props.viewport.height

  // 1. 1080 × 1920 竖屏标准舞台底板
  const artboard = new Rect({
    left: 0,
    top: 0,
    width: vw,
    height: vh,
    originX: 0,
    originY: 0,
    fill: '#232329',
    stroke: '#3f3f48',
    strokeWidth: 2,
    selectable: false,
    evented: false
  })
  fabricCanvas.add(artboard)

  // 2. WACCA 1080 × 1080 中心圆环区域参考线
  const guideCircle = new Circle({
    left: vw / 2,
    top: vh / 2,
    radius: Math.min(vw, vh) / 2,
    originX: 0.5,
    originY: 0.5,
    fill: 'transparent',
    stroke: 'rgba(255, 255, 255, 0.14)',
    strokeWidth: 1.5,
    strokeDashArray: [8, 6],
    selectable: false,
    evented: false
  })
  fabricCanvas.add(guideCircle)

  const nodesMap = nodeById.value
  const entries = [...props.layouts.entries()]
    .map(([id, layout]) => ({ node: nodesMap.get(id), layout }))
    .filter((item): item is { node: UMGNode; layout: ComputedLayout } => !!item.node)

  const worldTransforms = computeRenderTransforms(props.nodes, props.layouts, props.layouts.keys().next().value ?? '')

  const clipRect =
    props.viewMode === 'appearance'
      ? new Rect({
          left: 0,
          top: 0,
          width: vw,
          height: vh,
          originX: 0,
          originY: 0,
          absolutePositioned: true
        })
      : undefined

  for (const { node, layout } of entries) {
    if (gen !== renderGeneration) return
    if (props.hiddenIds.has(node.id)) continue
    if (props.viewMode === 'appearance' && !layout.visible) continue

    const clipPoints = widgetClip(node, nodesMap, props.layouts, worldTransforms, props.viewport)
    if (props.viewMode === 'appearance' && clipPoints.length < 3) continue
    const nodeClip = props.viewMode === 'appearance' ? new Polygon(clipPoints, { absolutePositioned: true, fill: '#000', strokeWidth: 0 }) : undefined
    const { rect } = layout
    const w = Math.max(1, rect.width)
    const h = Math.max(1, rect.height)
    const tf = accumulatedTransform(node)
    const opacity = props.viewMode === 'structure' ? 1 : effectiveOpacity(node)

    // 在外观模式下，完全透明（例如动画初始态 RenderOpacity=0）的节点默认不遮挡画面，仅在被选中时显示外框
    if (props.viewMode === 'appearance' && opacity <= 0.001 && props.selectedId !== node.id) {
      continue
    }

    const left = rect.x + w * tf.pivotX + tf.tx
    const top = rect.y + h * tf.pivotY + tf.ty
    const absSx = Math.max(0.001, Math.abs(tf.sx))
    const absSy = Math.max(0.001, Math.abs(tf.sy))
    const flipX = tf.sx < 0
    const flipY = tf.sy < 0

    let fabricObj: FabricObject | null = null
    const imageUrl = props.viewMode === 'appearance' ? getImageUrl(node) : null
    const matRef = props.viewMode === 'appearance' ? getMaterialRef(node) : null
    const partRef = props.viewMode === 'appearance' ? getParticleRef(node) : null

    // 优先粒子系统预览渲染 (ParticleSystem)
    if (partRef && rect.width > 0 && rect.height > 0) {
      const partCanvas = await getParticleCanvasSource(partRef.assetPath, w, h)
      if (gen !== renderGeneration) return
      if (partCanvas) {
        fabricObj = new FabricImage(partCanvas, {
          left,
          top,
          originX: tf.pivotX,
          originY: tf.pivotY,
          scaleX: (w / partCanvas.width) * absSx,
          scaleY: (h / partCanvas.height) * absSy,
          flipX,
          flipY,
          angle: tf.angle,
          opacity,
          clipPath: nodeClip
        })
      }
    }

    // 其次材质解析渲染 (Material / MaterialInstanceConstant)
    if (!fabricObj && matRef && rect.width > 0 && rect.height > 0) {
      const animatedScalars=props.animationMaterialParameters?.[node.id]
      const matCanvas = await getMaterialCanvasSource(matRef.assetPath, w, h, animatedScalars, node.id)
      if (gen !== renderGeneration) return
      if (matCanvas) {
        const additive = normalizeMaterialEntry(props.materials?.[matRef.assetPath], matRef.assetPath)?.blendMode === 'BLEND_Additive'
        fabricObj = new FabricImage(matCanvas, {
          // Black-backed emissive output adds RGB onto the opaque artboard; never source-over it.
          globalCompositeOperation: additive ? 'lighter' : 'source-over',
          objectCaching: false,
          left,
          top,
          originX: tf.pivotX,
          originY: tf.pivotY,
          scaleX: (w / matCanvas.width) * absSx,
          scaleY: (h / matCanvas.height) * absSy,
          flipX,
          flipY,
          angle: tf.angle,
          opacity,
          clipPath: nodeClip
        })
      }
    }

    if (!fabricObj && imageUrl && rect.width > 0 && rect.height > 0) {
      const tint = extractColor(node.visual.ColorAndOpacity ?? node.visual.Brush?.TintColor)
      const source = await getTintedImageSource(imageUrl, tint)
      if (gen !== renderGeneration) return
      if (source) {
        const srcW = (source as HTMLImageElement).naturalWidth || source.width || w
        const srcH = (source as HTMLImageElement).naturalHeight || source.height || h
        fabricObj = new FabricImage(source, {
          left,
          top,
          originX: tf.pivotX,
          originY: tf.pivotY,
          scaleX: (w / srcW) * absSx,
          scaleY: (h / srcH) * absSy,
          flipX,
          flipY,
          angle: tf.angle,
          opacity,
          clipPath: nodeClip
        })
      }
    }

    if (!fabricObj && typeof node.visual.Text === 'string' && node.visual.Text.trim().length > 0) {
      const color = extractColor(node.visual.ColorAndOpacity)
      const fontSize = finite(node.visual.Font?.Size, 14) * (96 / 72)
      fabricObj = new FabricText(node.visual.Text, {
        left,
        top,
        originX: tf.pivotX,
        originY: tf.pivotY,
        fontSize,
        fontFamily: 'sans-serif',
        fill: color ? `rgb(${color.r}, ${color.g}, ${color.b})` : '#ffffff',
        textAlign:
          node.visual.Justification === 'Center'
            ? 'center'
            : node.visual.Justification === 'Right'
              ? 'right'
              : 'left',
        scaleX: absSx,
        scaleY: absSy,
        flipX,
        flipY,
        angle: tf.angle,
        opacity,
        clipPath: nodeClip
      })
    }

    if (!fabricObj) {
      const isContainer = node.children.length > 0
      const borderBrush = extractColor(node.visual.BrushColor ?? node.visual.Background?.TintColor)
      const hasSolidFill =
        props.viewMode === 'appearance' &&
        node.className === 'Border' &&
        borderBrush &&
        borderBrush.a > 0.01

      fabricObj = new Rect({
        left,
        top,
        width: w,
        height: h,
        originX: tf.pivotX,
        originY: tf.pivotY,
        scaleX: absSx,
        scaleY: absSy,
        flipX,
        flipY,
        angle: tf.angle,
        fill: hasSolidFill
          ? `rgba(${borderBrush!.r}, ${borderBrush!.g}, ${borderBrush!.b}, ${borderBrush!.a})`
          : 'rgba(0, 0, 0, 0)',
        stroke:
          props.viewMode === 'structure'
            ? isContainer
              ? 'rgba(64, 158, 255, 0.45)'
              : 'rgba(255, 255, 255, 0.45)'
            : props.selectedId === node.id
              ? '#18a058'
              : 'transparent',
        strokeWidth: props.viewMode === 'structure' ? 1.5 : 1,
        strokeDashArray: props.viewMode === 'structure' && isContainer ? [6, 4] : undefined,
        opacity: props.viewMode === 'structure' ? 1 : Math.max(opacity, props.selectedId === node.id ? 1 : 0),
        // 外观模式下纯容器不拦截底层图片点击
        evented: props.viewMode === 'structure' || !isContainer || props.selectedId === node.id,
        selectable: props.viewMode === 'structure' || !isContainer || props.selectedId === node.id,
        clipPath: nodeClip
      })
    }

    // Fabric matrices are center-based; map the intrinsic center into arranged
    // layout space, then compose the complete ancestor render transform.
    const world = worldTransforms.get(node.id) ?? [1,0,0,1,0,0] as Matrix
    const intrinsicWidth = fabricObj.width || w, intrinsicHeight = fabricObj.height || h
    const isText = fabricObj instanceof FabricText
    const local: Matrix = [isText ? 1 : w/intrinsicWidth,0,0,isText ? 1 : h/intrinsicHeight,
      rect.x+(isText ? intrinsicWidth : w)/2,rect.y+(isText ? intrinsicHeight : h)/2]
    util.applyTransformToObject(fabricObj, multiply(world,local))
    fabricObj.setCoords()
    fabricObj.set({ perPixelTargetFind: props.viewMode === 'appearance' })
    applySelectionStyle(fabricObj)
    objectByNodeId.set(node.id, fabricObj)
    nodeIdByObject.set(fabricObj, node.id)
    fabricCanvas.add(fabricObj)

    // 结构线框模式下绘制轻量节点名称标签
    if (props.viewMode === 'structure' && w >= 40 && h >= 18) {
      const label = new FabricText(node.name, {
        left: rect.x + 4,
        top: rect.y + 2,
        originX: 0,
        originY: 0,
        fontSize: 13,
        fontFamily: 'monospace',
        fill: 'rgba(255, 255, 255, 0.78)',
        backgroundColor: 'rgba(0, 0, 0, 0.55)',
        selectable: false,
        evented: false
      })
      const labelLocal: Matrix = [1,0,0,1,rect.x+4+label.width/2,rect.y+2+label.height/2]
      util.applyTransformToObject(label,multiply(world,labelLocal))
      fabricCanvas.add(label)
    }
  }

  syncSelectedObject()
  fabricCanvas.requestRenderAll()
}

function syncSelectedObject() {
  if (!fabricCanvas) return
  syncingSelection = true
  try {
    if (!props.selectedId) {
      fabricCanvas.discardActiveObject()
    } else {
      const obj = objectByNodeId.get(props.selectedId)
      if (obj) {
        obj.set({ selectable: true, evented: true })
        fabricCanvas.setActiveObject(obj)
      } else {
        fabricCanvas.discardActiveObject()
      }
    }
    fabricCanvas.requestRenderAll()
  } finally {
    syncingSelection = false
  }
}

onMounted(async () => {
  await nextTick()
  if (!canvasEl.value || !stageViewport.value) return

  const cw = stageViewport.value.clientWidth || 800
  const ch = stageViewport.value.clientHeight || 600

  fabricCanvas = new Canvas(canvasEl.value, {
    width: cw,
    height: ch,
    backgroundColor: '#141418',
    preserveObjectStacking: true,
    selection: false
  })

  // 多层穿透点击命中逻辑：若在同一位置重叠多个组件，连续点击可循环向下选取底层元素
  fabricCanvas.on('mouse:down', opt => {
    const evt = opt.e as MouseEvent
    if (evt.button !== 0 || evt.shiftKey || evt.altKey) return
    if (!fabricCanvas) return

    const pointer = fabricCanvas.getScenePoint(opt.e)
    const hitObjects: { id: string; obj: FabricObject }[] = []

    for (const obj of fabricCanvas.getObjects()) {
      const id = nodeIdByObject.get(obj)
      if (!id) continue
      if (obj.containsPoint(pointer)) {
        hitObjects.push({ id, obj })
      }
    }

    if (hitObjects.length === 0) return

    const currentIndex = hitObjects.findIndex(h => h.id === props.selectedId)
    const nextTarget =
      currentIndex === -1
        ? hitObjects[hitObjects.length - 1]
        : hitObjects[(currentIndex - 1 + hitObjects.length) % hitObjects.length]

    if (nextTarget && nextTarget.id !== props.selectedId) {
      emit('select-node', nextTarget.id)
    }
  })

  // 滚轮缩放
  fabricCanvas.on('mouse:wheel', opt => {
    const delta = opt.e.deltaY
    let zoom = fabricCanvas!.getZoom()
    zoom *= 0.999 ** delta
    zoom = Math.max(0.1, Math.min(4, zoom))
    fabricCanvas!.zoomToPoint(new Point(opt.e.offsetX, opt.e.offsetY), zoom)
    zoomPercent.value = Math.round(zoom * 100)
    opt.e.preventDefault()
    opt.e.stopPropagation()
  })

  // 拖拽平移（中键 / Shift / Alt / 空白处拖动）
  let isDragging = false
  let lastX = 0
  let lastY = 0

  fabricCanvas.on('mouse:down', opt => {
    const evt = opt.e as MouseEvent
    if (evt.button === 1 || evt.shiftKey || evt.altKey || !opt.target) {
      isDragging = true
      lastX = evt.clientX
      lastY = evt.clientY
    }
  })

  fabricCanvas.on('mouse:move', opt => {
    if (!isDragging || !fabricCanvas) return
    const evt = opt.e as MouseEvent
    const vpt = fabricCanvas.viewportTransform
    if (vpt) {
      vpt[4] += evt.clientX - lastX
      vpt[5] += evt.clientY - lastY
      fabricCanvas.requestRenderAll()
    }
    lastX = evt.clientX
    lastY = evt.clientY
  })

  fabricCanvas.on('mouse:up', () => {
    isDragging = false
  })

  resizeObserver = new ResizeObserver(entries => {
    const entry = entries[0]
    if (!entry || !fabricCanvas) return
    const { width, height } = entry.contentRect
    if (width > 0 && height > 0) {
      fabricCanvas.setDimensions({ width, height })
      fit()
    }
  })
  resizeObserver.observe(stageViewport.value)

  // 启动常驻 Stage 渲染帧循环 (驱动时间 Shader 动画以及统计 FPS)
  function stageFrameLoop(now: number) {
    const dt = Math.min(0.1, (now - lastRafTime) / 1000)
    lastRafTime = now
    autoShaderClock += dt

    // 帧率统计
    fpsFrameCount++
    if (now - fpsLastTime >= 500) {
      fpsDisplay.value = Math.round((fpsFrameCount * 1000) / (now - fpsLastTime))
      fpsFrameCount = 0
      fpsLastTime = now
    }

    // 当未处于主动时间轴播放 (或显式传入固定 materialTime) 时，若舞台上存活活跃的 MaterialSurface，使用真实时间驱动更新
    if (props.materialTime === undefined && materialSurfaces.size > 0 && fabricCanvas) {
      const tasks = [...materialSurfaces.values()]
      Promise.all(tasks).then(surfaces => {
        let anyRendered = false
        for (const surface of surfaces) {
          try {
            surface.draw(autoShaderClock)
            anyRendered = true
          } catch {}
        }
        if (anyRendered && fabricCanvas) {
          fabricCanvas.requestRenderAll()
        }
      }).catch(() => {})
    }

    stageRafId = requestAnimationFrame(stageFrameLoop)
  }
  stageRafId = requestAnimationFrame(stageFrameLoop)

  fit()
  await rebuildScene()
})

watch(
  [() => props.nodes, () => props.layouts, () => props.viewMode, () => props.viewport, () => props.hiddenIds, () => props.materialTime, () => props.animationMaterialParameters],
  () => {
    rebuildScene()
  }
)

watch(
  () => props.selectedId,
  () => {
    // 若选中的节点在外观模式下原本透明被跳过，重新构建一次使其选中框可见
    if (props.selectedId && !objectByNodeId.has(props.selectedId)) {
      rebuildScene()
    } else {
      syncSelectedObject()
    }
  }
)

function releaseInactiveMaterialSurfaces() {
  const nodes = nodeById.value
  for (const [key, task] of materialSurfaces) {
    const nodeId = key.split('|')[1]!
    let node = nodes.get(nodeId)
    let inactive = !node || props.viewMode !== 'appearance'
    const visited = new Set<string>()
    while (node && !visited.has(node.id)) {
      visited.add(node.id)
      if (props.hiddenIds.has(node.id)) { inactive = true; break }
      node = node.parentId ? nodes.get(node.parentId) : undefined
    }
    if (inactive) {
      materialSurfaces.delete(key)
      task.then(surface => surface.dispose()).catch(() => {})
    }
  }
}
watch(() => [props.hiddenIds, props.nodes, props.viewMode], releaseInactiveMaterialSurfaces, { deep: true })

function disposeMaterialSurfaces() {
  for (const task of materialSurfaces.values()) task.then(s => s.dispose()).catch(() => {})
  materialSurfaces.clear()
}
const stopMaterialInvalidation = materialPrograms.onInvalidate(changedProject => {
  if (changedProject && changedProject !== project.id) return
  ++renderGeneration
  disposeMaterialSurfaces()
  rawImageCache.clear()
  tintedSourceCache.clear()
  particleCanvasCache.clear()
  rebuildScene()
})
watch(() => [project.id, props.materials], disposeMaterialSurfaces)
onBeforeUnmount(() => {
  if (stageRafId) cancelAnimationFrame(stageRafId)
  stopMaterialInvalidation()
  ++renderGeneration
  disposeMaterialSurfaces()
  resizeObserver?.disconnect()
  fabricCanvas?.dispose()
  fabricCanvas = null
})
</script>

<template>
  <div class="umg-stage-container">
    <div class="stage-toolbar">
      <div class="stage-info">
        <span>{{ tr('ui.umgCanvas') }}: {{ viewport.width }} × {{ viewport.height }}</span>
        <span v-if="fpsDisplay > 0" class="fps-badge">{{ fpsDisplay }} FPS</span>
      </div>
      <div class="zoom-controls">
        <n-button size="tiny" @click="zoomBy(-0.1)">-</n-button>
        <span class="zoom-label">{{ zoomPercent }}%</span>
        <n-button size="tiny" @click="zoomBy(0.1)">+</n-button>
        <n-button size="tiny" @click="fit">{{ tr('ui.umgResetZoom') }}</n-button>
      </div>
    </div>

    <div ref="stageViewport" class="stage-viewport">
      <canvas ref="canvasEl" />
    </div>
  </div>
</template>

<style scoped>
.umg-stage-container {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  min-height: 0;
  width: 100%;
  height: 100%;
  background: #141418;
  overflow: hidden;
}
.stage-toolbar {
  padding: 6px 12px;
  background: rgba(255, 255, 255, 0.04);
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 12px;
  color: #aaa;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  flex-shrink: 0;
}
.stage-info {
  display: flex;
  align-items: center;
  gap: 8px;
}
.fps-badge {
  background: rgba(16, 185, 129, 0.15);
  color: #10b981;
  border: 1px solid rgba(16, 185, 129, 0.3);
  border-radius: 3px;
  padding: 1px 5px;
  font-family: monospace;
  font-size: 11px;
  font-weight: 600;
}
.zoom-controls {
  display: flex;
  align-items: center;
  gap: 8px;
}
.zoom-label {
  min-width: 44px;
  text-align: center;
  font-family: monospace;
}
.stage-viewport {
  flex: 1;
  width: 100%;
  height: 100%;
  min-height: 0;
  position: relative;
  overflow: hidden;
}
</style>
