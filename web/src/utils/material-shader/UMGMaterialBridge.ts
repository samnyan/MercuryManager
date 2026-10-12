import { MaterialWebGLRenderer } from './MaterialWebGLRenderer'

export interface NormalizedMaterialSpec {
  name: string
  blendMode: string
  scalars: Record<string, number>
  vectors: Record<string, { r: number; g: number; b: number; a: number }>
  textures: Array<{ name: string; path: string }>
  functionNames: string[]
  preset: 'composite' | 'uv_crop' | 'glitch' | 'ring_gauge' | 'pan_rotate' | 'static' | 'color_tint'
}

export function normalizeMaterialEntry(rawEntry: any): NormalizedMaterialSpec | null {
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
    // 优先排列被多次采样的核心纹理节点（如 uM_Title009 中采样 4 次的 uT_Chara000_RT000），再排列单次节点，且 MaterialExpressionTextureSample 优先于 TextureObjectParameter（通常为遮罩）
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

  // 5. 自动识别材质渲染预设（与 MaterialShaderCanvas 保持完全一致的规则）
  const sKeys = Object.keys(scalars).map(k => k.toLowerCase())
  const fnList = functionNames.map(f => f.toLowerCase())
  const nonMasks = textures.filter(t => !/mask/i.test(t.name) && !/mask/i.test(t.path))

  const hasTimeAnim =
    fnList.some(f => f.includes('rotator') || f.includes('panner') || f.includes('glitch') || f.includes('sine') || f.includes('crop')) ||
    sKeys.some(k => k.includes('speed') || k.includes('pan') || k.includes('rotator') || k.includes('duration') || k.includes('move_'))

  let preset: NormalizedMaterialSpec['preset'] = 'color_tint'
  // 必须真正有多个非遮罩纹理（如既有角色立绘又有独立 Logo 纹理）或显式 Glitch 函数才算 composite，不能仅因为有 move 参数误入
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

  return {
    name,
    blendMode,
    scalars,
    vectors,
    textures,
    functionNames,
    preset
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
 * 将解析后的 UI 材质或材质实例离线烘焙为可供 UMG 画布/缩略图直接使用的 Canvas 图像源
 */
export async function renderUMGMaterialToCanvas(
  spec: NormalizedMaterialSpec,
  loadImage: (assetPath: string) => Promise<HTMLImageElement | null>,
  targetW = 512,
  targetH = 512,
  simTime = 0.35
): Promise<HTMLCanvasElement | null> {
  const W = Math.max(64, Math.min(1024, Math.round(targetW)))
  const H = Math.max(64, Math.min(1024, Math.round(targetH)))

  // 并行加载材质引用的全部贴图
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
  const masks = list.filter(t => /mask/i.test(t.name) || /mask/i.test(t.path) || /menuobj10/i.test(t.path))
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

  // 1. 对于复合多图层材质（如 uM_Title008），优先使用已封装的 MaterialWebGLRenderer 渲染物理着色效果
  // 注意：仅当材质具有对应的多图层调色盘特征时启用（排除一般的角色立绘/特效材质，以保证 2D 烘焙高保真度）
  if (
    spec.preset === 'composite' &&
    Object.keys(spec.vectors).some(k => k.includes('color_') || k.includes('R_Color')) &&
    spec.textures.some(t => /ttlogo/i.test(t.name) || /ttlogo/i.test(t.path))
  ) {
    const glCanvas = document.createElement('canvas')
    glCanvas.width = W
    glCanvas.height = H
    const webgl = new MaterialWebGLRenderer()
    if (webgl.init(glCanvas)) {
      for (const [p, img] of loadedImages.entries()) {
        webgl.updateTexture(p, img)
      }
      webgl.render(
        W,
        H,
        {
          time: simTime,
          resolution: [W, H],
          scalars: spec.scalars,
          vectors: spec.vectors
        },
        {
          primary: primaryPath,
          secondary: secondaryPath,
          mask: maskPath,
          obj: objPath
        },
        spec.blendMode
      )
      // 拷贝到 2D Canvas 再销毁 WebGL 上下文，避免过多 WebGL Context 耗尽浏览器配额
      const out2d = document.createElement('canvas')
      out2d.width = W
      out2d.height = H
      const outCtx = out2d.getContext('2d')
      if (outCtx) {
        outCtx.drawImage(glCanvas, 0, 0)
        webgl.destroy()
        return out2d
      }
      webgl.destroy()
    }
  }

  // 2. Canvas 2D 精确材质合成（处理 RingGauge、UV_Crop、Additive 滤黑透明、单图着色、角色叠层等）
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
    // 环形进度条 / 音游外圈模拟 (如 uM_Ring00_3)
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

  const mainImg = imgPrimary || imgObj || imgSecondary
  if (mainImg) {
    ctx.drawImage(mainImg, 0, 0, W, H)

    // 如果有次级叠加层（且非 Logo 遮挡整幅角色），按滤色叠加
    if (imgSecondary && imgSecondary !== mainImg && !/ttlogo/i.test(secondaryPath)) {
      ctx.save()
      ctx.globalCompositeOperation = 'screen'
      ctx.globalAlpha = 0.75
      ctx.drawImage(imgSecondary, 0, 0, W, H)
      ctx.restore()
    }

    // 应用主向量颜色着色 (排除全黑或纯白无变化色)
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

    // 若材质为 BLEND_Additive（如光晕 uM_Title004、流光 uM_CM_active01_3）或特定发光特效，将黑色背景转为 Alpha 透明通道，使其自然发光叠加
    if (isAdditive) {
      try {
        const imgData = ctx.getImageData(0, 0, W, H)
        const d = imgData.data
        for (let i = 0; i < d.length; i += 4) {
          const lum = Math.max(d[i], d[i + 1], d[i + 2])
          d[i + 3] = Math.round((d[i + 3] * lum) / 255)
        }
        ctx.putImageData(imgData, 0, 0)
      } catch {
        // ignore cross-origin if any
      }
    }

    // 若材质为 BLEND_Translucent，但纹理本身是不透明黑底（如发光/光芒特效），检测是否需要基于明度滤黑
    if (!isAdditive && (spec.blendMode || '').includes('Translucent')) {
      try {
        const imgData = ctx.getImageData(0, 0, W, H)
        const d = imgData.data
        // 检查四角像素是否为纯黑或接近黑色
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

  // 无贴图时的纯色/渐变材质回退
  const r = linearToSrgb255(col1.r)
  const g = linearToSrgb255(col1.g)
  const b = linearToSrgb255(col1.b)
  ctx.fillStyle = `rgba(${r}, ${g}, ${b}, ${col1.a})`
  ctx.fillRect(0, 0, W, H)
  return canvas
}
