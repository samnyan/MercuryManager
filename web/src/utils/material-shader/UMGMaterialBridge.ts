import { MaterialInstance } from '../material-runtime/MaterialInstance'
import {
  MaterialWebGLRenderer,
  resolveContractSamplerSlots,
  type MaterialShaderContract
} from './MaterialWebGLRenderer'

export interface NormalizedMaterialSpec {
  name: string
  blendMode: string
  scalars: Record<string, number>
  vectors: Record<string, { r: number; g: number; b: number; a: number }>
  textures: Array<{ name: string; path: string }>
  functionNames: string[]
  preset: 'composite' | 'uv_crop' | 'glitch' | 'ring_gauge' | 'pan_rotate' | 'static' | 'color_tint'
  assetPath?: string
}

export function normalizeMaterialEntry(rawEntry: any, assetPath?: string): NormalizedMaterialSpec | null {
  if (!rawEntry) return null

  const type = rawEntry.Type
  const matInfo = type === 'Material' ? rawEntry.MaterialInfo : rawEntry.MaterialInstanceInfo?.ParentMaterialInfo
  const miInfo = type === 'MaterialInstance' ? rawEntry.MaterialInstanceInfo : null

  if (!matInfo && !miInfo) return null

  const name = miInfo?.Name || matInfo?.Name || 'Material'
  const blendMode =
    miInfo?.BasePropertyOverrides?.BlendMode ||
    matInfo?.BlendMode ||
    'BLEND_Opaque'

  // 1. 汇总 Scalars (先放父材质默认值，再用 MaterialInstance 覆写)
  const scalars: Record<string, number> = {}
  if (Array.isArray(matInfo?.ScalarParameters)) {
    for (const sp of matInfo.ScalarParameters) {
      if (sp?.ParameterName && typeof sp.DefaultValue === 'number') {
        scalars[sp.ParameterName] = sp.DefaultValue
      }
    }
  }
  if (miInfo?.Scalars && typeof miInfo.Scalars === 'object') {
    for (const [k, v] of Object.entries(miInfo.Scalars)) {
      if (typeof v === 'number') scalars[k] = v
    }
  }
  if (Array.isArray(miInfo?.ScalarOverrides)) {
    for (const so of miInfo.ScalarOverrides) {
      if (so?.ParameterName && typeof so.Value === 'number') {
        scalars[so.ParameterName] = so.Value
      }
    }
  }

  // 2. 汇总 Vectors (先放父材质默认值，再用 MaterialInstance 覆写)
  const vectors: Record<string, { r: number; g: number; b: number; a: number }> = {}
  if (Array.isArray(matInfo?.VectorParameters)) {
    for (const vp of matInfo.VectorParameters) {
      if (vp?.ParameterName) {
        vectors[vp.ParameterName] = {
          r: Number(vp.R ?? 1),
          g: Number(vp.G ?? 1),
          b: Number(vp.B ?? 1),
          a: Number(vp.A ?? 1)
        }
      }
    }
  }
  if (Array.isArray(miInfo?.VectorOverrides)) {
    for (const vo of miInfo.VectorOverrides) {
      if (vo?.ParameterName) {
        vectors[vo.ParameterName] = {
          r: Number(vo.R ?? 1),
          g: Number(vo.G ?? 1),
          b: Number(vo.B ?? 1),
          a: Number(vo.A ?? 1)
        }
      }
    }
  }

  // 3. 汇总 Textures (优先 MaterialInstance 覆写的贴图，再合并父材质 TextureNodes / ImportedTextures)
  const textures: Array<{ name: string; path: string }> = []
  const seenPaths = new Set<string>()
  const addTex = (tName: string, tPath?: string | null) => {
    if (!tPath || !tPath.endsWith('.uasset') || seenPaths.has(tPath)) return
    seenPaths.add(tPath)
    textures.push({ name: tName, path: tPath })
  }

  // MaterialInstance TextureOverrides
  const overriddenParams = new Set<string>()
  if (Array.isArray(miInfo?.TextureOverrides)) {
    for (const to of miInfo.TextureOverrides) {
      if (to?.ParameterName && to?.ContentFile) {
        overriddenParams.add(to.ParameterName)
        addTex(to.ParameterName, to.ContentFile)
      }
    }
  }
  if (miInfo?.Textures && typeof miInfo.Textures === 'object') {
    for (const [k, v] of Object.entries(miInfo.Textures)) {
      if (typeof v === 'string') {
        overriddenParams.add(k)
        addTex(k, v)
      }
    }
  }
  if (Array.isArray(matInfo?.TextureNodes)) {
    const counts = new Map<string, number>()
    for (const tn of matInfo.TextureNodes) {
      if (tn?.ContentFile) counts.set(tn.ContentFile, (counts.get(tn.ContentFile) || 0) + 1)
    }
    const sortedNodes = [...matInfo.TextureNodes].sort((a, b) => {
      const aSample = a?.Class === 'MaterialExpressionTextureSample' ? 1 : 0
      const bSample = b?.Class === 'MaterialExpressionTextureSample' ? 1 : 0
      if (aSample !== bSample) return bSample - aSample
      const ca = counts.get(a?.ContentFile) || 0
      const cb = counts.get(b?.ContentFile) || 0
      return cb - ca
    })
    for (const tn of sortedNodes) {
      if (tn?.ParameterName && overriddenParams.has(tn.ParameterName)) continue
      addTex(tn?.ParameterName || tn?.TextureName || tn?.NodeName || 'Texture', tn?.ContentFile)
    }
  }
  if (Array.isArray(matInfo?.ImportedTextures)) {
    for (const imp of matInfo.ImportedTextures) {
      addTex(imp?.ObjectName || 'Texture', imp?.ContentFile)
    }
  }

  // 4. 汇总 FunctionCalls
  const functionNames: string[] = []
  if (Array.isArray(matInfo?.FunctionCalls)) {
    for (const fc of matInfo.FunctionCalls) {
      if (fc?.FunctionName) functionNames.push(String(fc.FunctionName))
    }
  }

  // 5. 自动识别材质渲染预设
  const sKeys = Object.keys(scalars).map(k => k.toLowerCase())
  const fnList = functionNames.map(f => f.toLowerCase())
  const nonMasks = textures.filter(t => !/mask/i.test(t.name) && !/mask/i.test(t.path))

  const hasTimeAnim =
    fnList.some(f => f.includes('rotator') || f.includes('panner') || f.includes('glitch') || f.includes('sine') || f.includes('crop')) ||
    sKeys.some(k => k.includes('speed') || k.includes('pan') || k.includes('rotator') || k.includes('duration') || k.includes('move_'))

  let preset: NormalizedMaterialSpec['preset'] = 'color_tint'
  if (
    nonMasks.length >= 2 &&
    textures.some(t => /ttlogo/i.test(t.name) || /ttlogo/i.test(t.path)) &&
    (fnList.some(f => f.includes('crop') || f.includes('rotator')) || textures.length >= 4)
  ) {
    preset = 'composite'
  } else if (sKeys.includes('u0') && sKeys.includes('v0')) {
    preset = 'uv_crop'
  } else if (
    fnList.some(f => f.includes('glitch')) ||
    sKeys.some(k => k.includes('glitch') || k.includes('timespanh') || k.includes('timedurationh'))
  ) {
    preset = 'glitch'
  } else if (sKeys.some(k => k.includes('gauge') || k.includes('ring'))) {
    preset = 'ring_gauge'
  } else if (
    fnList.some(f => f.includes('rotator') || f.includes('panner') || f.includes('texscale')) ||
    sKeys.some(k => k.includes('rotate') || k.includes('speed') || k.includes('pan'))
  ) {
    preset = 'pan_rotate'
  } else if (!hasTimeAnim) {
    preset = 'static'
  }

  const effectivePath = assetPath || (type === 'Material' ? rawEntry.MaterialInfo?.ContentFile : rawEntry.MaterialInstanceInfo?.ContentFile)

  return {
    name,
    blendMode,
    scalars,
    vectors,
    textures,
    functionNames,
    preset,
    assetPath: effectivePath
  }
}

function getScalar(scalars: Record<string, number>, pattern: RegExp, fallback: number): number {
  for (const [k, v] of Object.entries(scalars)) {
    if (pattern.test(k) && typeof v === 'number' && Number.isFinite(v)) return v
  }
  return fallback
}

function getPrimaryColor(vectors: Record<string, { r: number; g: number; b: number; a: number }>) {
  const entries = Object.entries(vectors)
  if (!entries.length) return { r: 1, g: 1, b: 1, a: 1 }
  const preferred =
    entries.find(([k]) => /on_color|color0|color1|color_1|c0|tint|main/i.test(k)) || entries[0]
  return preferred[1]
}

function getSecondaryColor(vectors: Record<string, { r: number; g: number; b: number; a: number }>) {
  const entries = Object.entries(vectors)
  if (entries.length < 2) return null
  const sec = entries.find(([k]) => /off_color|color1|color2|color_2|c2|sub/i.test(k)) || entries[1]
  return sec ? sec[1] : null
}

function linearToSrgb255(c: number): number {
  const v = Math.max(0, Math.min(1, c))
  const srgb = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.pow(v, 1 / 2.4) - 0.055
  return Math.round(Math.max(0, Math.min(255, srgb * 255)))
}

/**
 * 将解析后的 UI 材质或材质实例烘焙为可供 UMG 画布/缩略图直接使用的 Canvas 图像源。
 * 优先使用后端 /api/projects/{id}/resource-material-shader 的真实 WebGL2 着色器与 UBO 运行，
 * 若未就绪或非主材质则无缝回退到高保真 2D 复合模拟。
 */
export async function renderUMGMaterialToCanvas(
  spec: NormalizedMaterialSpec,
  loadImage: (assetPath: string) => Promise<HTMLImageElement | null>,
  targetW = 512,
  targetH = 512,
  simTime = 0.35,
  shaderContract?: MaterialShaderContract | null
): Promise<HTMLCanvasElement | null> {
  const W = Math.max(64, Math.min(1024, Math.round(targetW)))
  const H = Math.max(64, Math.min(1024, Math.round(targetH)))

  // 1. 若提供了已验证的 Cooked Material Shader 契约，直接使用 WebGL2 物理着色管线渲染
  if (shaderContract && shaderContract.glsl && shaderContract.blendMode !== 'BLEND_Additive') {
    const { slots, error } = resolveContractSamplerSlots(shaderContract)
    if (!error) {
      // 加载所有解析出的槽位纹理
      const requiredTextures = new Map<string, HTMLImageElement>()
      await Promise.all(
        slots.map(async slot => {
          if (slot.generatedSource) return
          if (!requiredTextures.has(slot.assetPath)) {
            const img = await loadImage(slot.assetPath)
            if (img && img.naturalWidth > 0) {
              requiredTextures.set(slot.assetPath, img)
            }
          }
        })
      )

      const glCanvas = document.createElement('canvas')
      glCanvas.width = W
      glCanvas.height = H
      const webgl2 = new MaterialWebGLRenderer()
      if (webgl2.init(glCanvas)) {
        for (const slot of slots) {
          const img = requiredTextures.get(slot.assetPath)
          if (img) {
            webgl2.updateTexture(slot.assetPath, img, slot.samplerSource)
          }
        }

        const instance = shaderContract.uniformProgram ? new MaterialInstance(shaderContract) : null
        instance?.setParameters(spec.scalars, Object.fromEntries(Object.entries(spec.vectors).map(([k,v])=>[k,[v.r,v.g,v.b,v.a]])))
        instance?.update({time:simTime,previousTime:simTime})
        const success = webgl2.renderContract(
          W,
          H,
          { ...shaderContract, time: simTime, runtimeWrites: instance?.writes() },
          slots,
          spec.blendMode
        )

        if (success) {
          const out2d = document.createElement('canvas')
          out2d.width = W
          out2d.height = H
          const outCtx = out2d.getContext('2d')
          if (outCtx) {
            outCtx.drawImage(glCanvas, 0, 0)
            webgl2.destroy()
            return out2d
          }
        }
        webgl2.destroy()
      }
    }
  }

  // 2. 回退：高保真模拟管线 (处理未提供契约、或材质函数/实例预览时的 2D 模拟)
  const loadedImages = new Map<string, HTMLImageElement>()
  await Promise.all(
    spec.textures.map(async t => {
      const img = await loadImage(t.path)
      if (img && img.naturalWidth > 0) {
        loadedImages.set(t.path, img)
      }
    })
  )

  const list = spec.textures
  const masks = list.filter(t => /mask/i.test(t.name) || /mask/i.test(t.path) || /menuobj10/i.test(t.path) || /glitchmask/i.test(t.path) || /cm_glitch/i.test(t.path))
  const nonMasks = list.filter(t => !masks.includes(t))

  const maskPath = masks.length > 0 ? masks[0].path : ''
  const objItem = nonMasks.find(t => /obj|ring|shape|star/i.test(t.name) || /obj/i.test(t.path))
  const objPath = objItem ? objItem.path : ''

  const priItem =
    nonMasks.find(t => t.path !== objPath && (/applyto|chara|bg|main|tex01|tex0/i.test(t.name) || /chara|bg/i.test(t.path))) ||
    nonMasks.find(t => t.path !== objPath) ||
    list[0]
  const primaryPath = priItem ? priItem.path : ''

  const secItem =
    nonMasks.find(t => t.path !== primaryPath && t.path !== objPath && (/logo|title|icon|tex_v|tex02/i.test(t.name) || /logo|title/i.test(t.path))) ||
    nonMasks.find(t => t.path !== primaryPath && t.path !== objPath)
  const secondaryPath = secItem ? secItem.path : ''

  const canvas = document.createElement('canvas')
  canvas.width = W
  canvas.height = H
  const ctx = canvas.getContext('2d')
  if (!ctx) return null

  const imgPrimary = primaryPath ? loadedImages.get(primaryPath) : undefined
  const imgSecondary = secondaryPath ? loadedImages.get(secondaryPath) : undefined
  const imgObj = objPath ? loadedImages.get(objPath) : undefined

  const col1 = getPrimaryColor(spec.vectors)
  const col2 = getSecondaryColor(spec.vectors)
  const isAdditive = (spec.blendMode || '').toUpperCase().includes('ADDITIVE')

  if (spec.preset === 'ring_gauge') {
    const baseGauge = getScalar(spec.scalars, /gauge$/i, 0.75)
    const gauge = Math.min(1, Math.max(0.15, baseGauge > 0.01 ? baseGauge : 0.75))
    const widthRatio = Math.min(0.9, Math.max(0.08, getScalar(spec.scalars, /gauge_width/i, 0.45)))
    const cx = W / 2
    const cy = H / 2
    const outerR = Math.min(W, H) * 0.48
    const innerR = outerR * (1 - widthRatio * 0.45)
    const lineW = Math.max(2, outerR - innerR)
    const midR = (outerR + innerR) / 2

    if (col2) {
      ctx.beginPath()
      ctx.arc(cx, cy, midR, 0, Math.PI * 2)
      ctx.strokeStyle = `rgba(${linearToSrgb255(col2.r)}, ${linearToSrgb255(col2.g)}, ${linearToSrgb255(col2.b)}, ${Math.max(0.18, col2.a * 0.35)})`
      ctx.lineWidth = lineW
      ctx.stroke()
    }

    const startAngle = -Math.PI / 2
    const endAngle = startAngle + Math.PI * 2 * gauge
    ctx.beginPath()
    ctx.arc(cx, cy, midR, startAngle, endAngle)
    ctx.strokeStyle = `rgba(${linearToSrgb255(col1.r)}, ${linearToSrgb255(col1.g)}, ${linearToSrgb255(col1.b)}, ${Math.max(0.75, col1.a)})`
    ctx.lineWidth = lineW
    ctx.stroke()
    return canvas
  }

  if (spec.preset === 'uv_crop' && imgPrimary) {
    const u0 = getScalar(spec.scalars, /^u0$/i, 0)
    const v0 = getScalar(spec.scalars, /^v0$/i, 0)
    const cellW = imgPrimary.naturalWidth / 8
    const cellH = imgPrimary.naturalHeight / 8
    const sx = u0 * imgPrimary.naturalWidth
    const sy = v0 * imgPrimary.naturalHeight
    ctx.drawImage(imgPrimary, sx, sy, cellW, cellH, 0, 0, W, H)
    return canvas
  }

  if (spec.preset === 'glitch') {
    const mainImg = imgPrimary || imgObj || imgSecondary
    if (mainImg) {
      const isVertical = spec.functionNames.some(f => /verticalglitch/i.test(f)) ||
        spec.textures.some(t => /glitchmaskv/i.test(t.path)) ||
        Object.keys(spec.scalars).some(k => /spanv|durationv|alphav/i.test(k))
      const isHorizontal = spec.functionNames.some(f => /horizontalglitch/i.test(f)) ||
        spec.textures.some(t => /glitchmaskh/i.test(t.path)) ||
        Object.keys(spec.scalars).some(k => /spanh|durationh|alphah/i.test(k)) ||
        (!isVertical) // 默认横向为主

      const span1 = getScalar(spec.scalars, /timespanh|span_1|span/i, 2.5)
      const dur1 = getScalar(spec.scalars, /timedurationh|duration_1|duration/i, 0.8)
      const offset1 = Math.abs(getScalar(spec.scalars, /glitchalphah|offset_1|offset/i, 0.03))
      const width1 = getScalar(spec.scalars, /widht_1|width_1|widht|width/i, 8)

      // 计算当前是否处于故障触发窗口内
      const cycleTime = span1 > 0 ? (simTime % span1) : simTime
      const isGlitching = cycleTime < dur1 || dur1 <= 0

      ctx.clearRect(0, 0, W, H)
      if (!isGlitching) {
        ctx.drawImage(mainImg, 0, 0, W, H)
      } else {
        const sliceCount = Math.max(4, Math.min(32, Math.round(width1)))
        const step = isHorizontal ? H / sliceCount : W / sliceCount

        for (let i = 0; i < sliceCount; i++) {
          // 基于伪随机噪声哈希生成切片位移
          const seed = Math.sin(i * 127.1 + Math.floor(simTime * 15) * 311.7) * 43758.5453
          const rand = seed - Math.floor(seed)
          const shiftFactor = (rand - 0.5) * 2.0 * offset1 * (isHorizontal ? W : H) * 2.0

          if (isHorizontal) {
            const sy = i * step
            const sh = Math.min(step, H - sy)
            ctx.drawImage(mainImg, 0, (sy / H) * mainImg.naturalHeight, mainImg.naturalWidth, (sh / H) * mainImg.naturalHeight,
              shiftFactor, sy, W, sh)
          } else {
            const sx = i * step
            const sw = Math.min(step, W - sx)
            ctx.drawImage(mainImg, (sx / W) * mainImg.naturalWidth, 0, (sw / W) * mainImg.naturalWidth, mainImg.naturalHeight,
              sx, shiftFactor, sw, H)
          }
        }

        // 添加轻微色差 RGB 分离效果 (Chromatic Aberration)
        try {
          ctx.save()
          ctx.globalCompositeOperation = 'screen'
          ctx.globalAlpha = 0.4
          if (col1 && (col1.r > 0 || col1.g > 0 || col1.b > 0)) {
            ctx.fillStyle = `rgb(${linearToSrgb255(col1.r)}, 0, 0)`
            ctx.fillRect(0, 0, W, H)
          }
          ctx.restore()
        } catch {}
      }

      if (imgSecondary && imgSecondary !== mainImg) {
        ctx.save()
        ctx.globalCompositeOperation = 'screen'
        ctx.globalAlpha = 0.6
        ctx.drawImage(imgSecondary, 0, 0, W, H)
        ctx.restore()
      }

      return canvas
    }
  }

  const mainImg = imgPrimary || imgObj || imgSecondary
  if (mainImg) {
    ctx.drawImage(mainImg, 0, 0, W, H)

    if (imgSecondary && imgSecondary !== mainImg && !/ttlogo/i.test(secondaryPath)) {
      ctx.save()
      ctx.globalCompositeOperation = 'screen'
      ctx.globalAlpha = 0.75
      ctx.drawImage(imgSecondary, 0, 0, W, H)
      ctx.restore()
    }

    const hasTint =
      Object.keys(spec.vectors).length > 0 &&
      (col1.r > 0.02 || col1.g > 0.02 || col1.b > 0.02) &&
      (col1.r < 0.98 || col1.g < 0.98 || col1.b < 0.98)

    if (hasTint) {
      ctx.save()
      ctx.globalCompositeOperation = 'multiply'
      ctx.fillStyle = `rgb(${linearToSrgb255(col1.r)}, ${linearToSrgb255(col1.g)}, ${linearToSrgb255(col1.b)})`
      ctx.fillRect(0, 0, W, H)
      ctx.globalCompositeOperation = 'destination-in'
      ctx.drawImage(mainImg, 0, 0, W, H)
      ctx.restore()
    }

    if (isAdditive) {
      try {
        const imgData = ctx.getImageData(0, 0, W, H)
        const d = imgData.data
        for (let i = 0; i < d.length; i += 4) {
          const lum = Math.max(d[i], d[i + 1], d[i + 2])
          d[i + 3] = Math.round((d[i + 3] * lum) / 255)
        }
        ctx.putImageData(imgData, 0, 0)
      } catch {}
    }

    if (!isAdditive && (spec.blendMode || '').includes('Translucent')) {
      try {
        const imgData = ctx.getImageData(0, 0, W, H)
        const d = imgData.data
        const corners = [0, (W - 1) * 4, ((H - 1) * W) * 4, ((H - 1) * W + (W - 1)) * 4]
        const allCornersBlack = corners.every(idx => d[idx] < 15 && d[idx + 1] < 15 && d[idx + 2] < 15 && d[idx + 3] > 200)
        if (allCornersBlack) {
          for (let i = 0; i < d.length; i += 4) {
            const lum = Math.max(d[i], d[i + 1], d[i + 2])
            d[i + 3] = Math.round((d[i + 3] * lum) / 255)
          }
          ctx.putImageData(imgData, 0, 0)
        }
      } catch {}
    }

    return canvas
  }

  const r = linearToSrgb255(col1.r)
  const g = linearToSrgb255(col1.g)
  const b = linearToSrgb255(col1.b)
  ctx.fillStyle = `rgba(${r}, ${g}, ${b}, ${col1.a})`
  ctx.fillRect(0, 0, W, H)
  return canvas
}
