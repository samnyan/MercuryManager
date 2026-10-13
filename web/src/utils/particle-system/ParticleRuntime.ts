// 通用粒子系统运行时与渲染桥接模块 (ParticleRuntime.ts)
// 供 ParticleSystemViewer (Cascade 面板) 与 UMG / Blueprint 预览器共同复用

import { normalizeMaterialEntry, renderUMGMaterialToCanvas } from '../material-shader/UMGMaterialBridge'

export interface ParticleModuleProp {
  Name: string
  Kind: string
  Value: string
  Number?: number
  Bool?: boolean
  ContentFile?: string
  Vector?: number[]
  Distribution?: {
    StructType?: string
    Kind: string
    Summary: string
    MinValue?: number
    MaxValue?: number
    MinVector?: number[]
    MaxVector?: number[]
    TableValues?: number[]
    EntryCount?: number
    EntryStride?: number
  }
  Bursts?: Array<{ Count: number; CountLow: number; Time: number }>
  Reference?: {
    Index: number
    ObjectName: string
    ContentFile?: string
    Class?: string
  }
}

export interface ParticleModuleInfo {
  ExportIndex: number
  Name: string
  Class: string
  Category: string
  Summary: string
  Properties: ParticleModuleProp[]
  Distributions?: Record<string, any>
  References?: Record<string, any>
  RawProperties?: Record<string, any>
}

export interface ParticleLODInfo {
  ExportIndex: number
  Name: string
  Level: number
  Enabled: boolean
  PeakActiveParticles: number
  RequiredModule?: ParticleModuleInfo | null
  SpawnModule?: ParticleModuleInfo | null
  TypeDataModule?: ParticleModuleInfo | null
  Modules: ParticleModuleInfo[]
}

export interface ParticleEmitterInfo {
  ExportIndex: number
  Name: string
  EmitterName: string
  Class: string
  EmitterType: string
  InitialAllocationCount: number
  PeakActiveParticles: number
  LODLevels: ParticleLODInfo[]
}

export interface ParticleSystemData {
  ExportIndex: number
  Name: string
  WarmupTime: number
  ResetPeakCounts: boolean
  LODDistances: number[]
  Emitters: ParticleEmitterInfo[]
  Materials?: Record<string, any>
}

export interface SimParticle {
  emitterIdx: number
  isMesh: boolean
  materialPath?: string
  blendMode: string
  subImagesH: number
  subImagesV: number
  subImageIndex: number
  x: number
  y: number
  z: number
  vx: number
  vy: number
  vz: number
  ax: number
  ay: number
  az: number
  sizeX: number
  sizeY: number
  sizeZ: number
  baseSizeX: number
  baseSizeY: number
  baseSizeZ: number
  r: number
  g: number
  b: number
  a: number
  age: number
  lifetime: number
  rotX: number
  rotY: number
  rotZ: number
  vRotX: number
  vRotY: number
  vRotZ: number
  colorOverLifeDist?: any
  alphaOverLifeDist?: any
  sizeByLifeDist?: any
}

interface EmitterRuntimeState {
  accumSpawn: number
  cycleTime: number
  loopsDone: number
  firedBursts: Set<number>
}

function randRange(min: number, max: number): number {
  if (min === max) return min
  return min + Math.random() * (max - min)
}

export function sampleFloatDist(dist: any, fallback = 0, ratio?: number): number {
  if (!dist) return fallback
  const vals = dist.TableValues
  if (typeof ratio === 'number' && Array.isArray(vals) && vals.length > 1) {
    const idx = Math.min(vals.length - 1, Math.max(0, Math.floor(ratio * (vals.length - 1))))
    return vals[idx]
  }
  const mn = typeof dist.MinValue === 'number' ? dist.MinValue : fallback
  const mx = typeof dist.MaxValue === 'number' ? dist.MaxValue : mn
  return randRange(mn, mx)
}

export function sampleVecDist(
  dist: any,
  fallback: [number, number, number] = [0, 0, 0],
  ratio?: number
): [number, number, number] {
  if (!dist) return fallback
  const vals = dist.TableValues
  const stride = dist.EntryStride || 3
  if (typeof ratio === 'number' && Array.isArray(vals) && vals.length >= stride * 2) {
    const entryCount = Math.floor(vals.length / stride)
    const idx = Math.min(entryCount - 1, Math.max(0, Math.floor(ratio * (entryCount - 1))))
    const base = idx * stride
    return [vals[base] ?? fallback[0], vals[base + 1] ?? fallback[1], vals[base + 2] ?? fallback[2]]
  }
  const mn = Array.isArray(dist.MinVector) && dist.MinVector.length >= 3 ? dist.MinVector : fallback
  const mx = Array.isArray(dist.MaxVector) && dist.MaxVector.length >= 3 ? dist.MaxVector : mn
  return [
    randRange(mn[0], mx[0]),
    randRange(mn[1], mx[1]),
    randRange(mn[2], mx[2])
  ]
}

export function getModulePropNumber(
  mod: ParticleModuleInfo | null | undefined,
  name: string,
  fallback: number
): number {
  if (!mod?.Properties) return fallback
  const p = mod.Properties.find(x => x.Name === name)
  return typeof p?.Number === 'number' ? p.Number : fallback
}

export function getEmitterMaterialPath(em: ParticleEmitterInfo): string | null {
  const lod = em.LODLevels?.[0]
  const ref = lod?.RequiredModule?.References?.Material
  return ref?.ContentFile || null
}

/**
 * 构建发射器关联材质的渲染贴图缓存
 */
export async function buildParticleMaterialSprites(
  info: ParticleSystemData,
  loadImage: (texPath: string) => Promise<HTMLImageElement | null>,
  externalMaterials?: Record<string, any>
): Promise<Map<string, HTMLCanvasElement>> {
  const result = new Map<string, HTMLCanvasElement>()
  const matPool: Record<string, any> = {
    ...(externalMaterials || {}),
    ...(info.Materials || {})
  }

  for (const em of info.Emitters || []) {
    const matPath = getEmitterMaterialPath(em)
    if (!matPath || result.has(matPath)) continue
    const rawMat = matPool[matPath]
    if (!rawMat) continue
    const spec = normalizeMaterialEntry(rawMat)
    if (!spec) continue
    try {
      const canvas = await renderUMGMaterialToCanvas(spec, loadImage, 128, 128)
      if (canvas) {
        result.set(matPath, canvas)
      }
    } catch {
      // ignore failed material sprite
    }
  }
  return result
}

/**
 * 可复用的 UE Cascade 粒子模拟引擎（支持 3D 视口与 2D UMG 画布投影）
 */
export class ParticleSimulationEngine {
  public info: ParticleSystemData
  public simTime = 0
  public particles: SimParticle[] = []
  public emitterStates: EmitterRuntimeState[] = []
  public materialSprites = new Map<string, HTMLCanvasElement>()
  public maxParticles = 400

  constructor(info: ParticleSystemData) {
    this.info = info
    this.reset()
  }

  public setInfo(info: ParticleSystemData) {
    this.info = info
    this.reset()
  }

  public setMaterialSprites(sprites: Map<string, HTMLCanvasElement>) {
    this.materialSprites = sprites
  }

  public reset(activeFilter?: (emIdx: number) => boolean) {
    this.simTime = 0
    this.particles = []
    const emitters = this.info.Emitters || []
    this.emitterStates = emitters.map(() => ({
      accumSpawn: 0,
      cycleTime: 0,
      loopsDone: 0,
      firedBursts: new Set<number>()
    }))
    this.step(0.016, activeFilter)
  }

  public spawnParticleForEmitter(em: ParticleEmitterInfo, emIdx: number) {
    const lod = em.LODLevels?.[0]
    if (!lod) return

    let lifetime = 5.0
    let size: [number, number, number] = [55, 55, 55]
    let vel: [number, number, number] = [0, 0, 0]
    let accel: [number, number, number] = [0, 0, 0]
    let color: [number, number, number] = [1, 1, 1]
    let alpha = 1.0
    let pos: [number, number, number] = [0, 0, 0]
    let rot: [number, number, number] = [0, 0, 0]
    let rotRate: [number, number, number] = [
      (Math.random() - 0.5) * 0.8,
      (Math.random() - 0.5) * 0.8,
      (Math.random() - 0.5) * 0.8
    ]

    let colorOverLifeDist: any = null
    let alphaOverLifeDist: any = null
    let sizeByLifeDist: any = null

    const matPath = getEmitterMaterialPath(em) || undefined
    const rawMat = matPath ? this.info.Materials?.[matPath] : null
    const blendMode =
      rawMat?.MaterialInstanceInfo?.BasePropertyOverrides?.BlendMode ||
      rawMat?.MaterialInfo?.BlendMode ||
      'BLEND_Additive'

    const subImagesH = Math.max(1, getModulePropNumber(lod.RequiredModule, 'SubImages_Horizontal', 1))
    const subImagesV = Math.max(1, getModulePropNumber(lod.RequiredModule, 'SubImages_Vertical', 1))
    let subImageIndex = 0

    const reqOrigin = lod.RequiredModule?.Properties?.find(p => p.Name === 'EmitterOrigin')?.Vector
    if (Array.isArray(reqOrigin) && reqOrigin.length >= 3) {
      const scaleOrigin = Math.abs(reqOrigin[1]) > 5000 ? 0.01 : 0.25
      pos = [reqOrigin[0] * scaleOrigin, reqOrigin[1] * scaleOrigin, reqOrigin[2] * scaleOrigin]
    }

    for (const mod of lod.Modules || []) {
      const cls = mod.Class || ''
      if (cls.includes('Lifetime')) {
        lifetime = Math.max(0.2, sampleFloatDist(mod.Distributions?.Lifetime, 3.0))
        if (lifetime > 15) lifetime = 8.0
      } else if (cls.includes('SizeMultiplyLife') || cls.includes('SizeScaleByLife')) {
        sizeByLifeDist = mod.Distributions?.LifeMultiplier
      } else if (cls.includes('Size')) {
        size = sampleVecDist(mod.Distributions?.StartSize, [55, 55, 55])
      } else if (cls.includes('Velocity')) {
        vel = sampleVecDist(mod.Distributions?.StartVelocity, [0, 0, 0])
        const rad = sampleFloatDist(mod.Distributions?.StartVelocityRadial, 0)
        if (rad !== 0) {
          const theta = Math.random() * Math.PI * 2
          vel[0] += Math.cos(theta) * rad
          vel[1] += Math.sin(theta) * rad
        }
      } else if (cls.includes('Acceleration')) {
        accel = sampleVecDist(
          mod.Distributions?.Acceleration || mod.Distributions?.DragCoefficient,
          [0, 0, 0]
        )
      } else if (cls.includes('ColorOverLife') || cls.includes('ColorScaleOverLife')) {
        colorOverLifeDist = mod.Distributions?.ColorOverLife || mod.Distributions?.ColorScaleOverLife
        alphaOverLifeDist = mod.Distributions?.AlphaOverLife || mod.Distributions?.AlphaScaleOverLife
        if (colorOverLifeDist) color = sampleVecDist(colorOverLifeDist, [1, 1, 1], 0)
        if (alphaOverLifeDist) alpha = sampleFloatDist(alphaOverLifeDist, 1.0, 0)
      } else if (cls.includes('Color')) {
        const cDist = mod.Distributions?.StartColor
        if (cDist) color = sampleVecDist(cDist, [1, 1, 1])
        const aDist = mod.Distributions?.StartAlpha
        if (aDist) alpha = sampleFloatDist(aDist, 1.0)
      } else if (cls.includes('Cylinder')) {
        const radius = Math.min(600, sampleFloatDist(mod.Distributions?.StartRadius, 150))
        const height = Math.min(600, sampleFloatDist(mod.Distributions?.StartHeight, 200))
        const angle = Math.random() * Math.PI * 2
        const r = Math.sqrt(Math.random()) * radius
        pos[0] += Math.cos(angle) * r
        pos[1] += Math.sin(angle) * r
        pos[2] += (Math.random() - 0.5) * height
      } else if (cls.includes('Location')) {
        const locOffset = sampleVecDist(mod.Distributions?.StartLocation, [0, 0, 0])
        pos[0] += locOffset[0] * 0.25
        pos[1] += locOffset[1] * 0.25
        pos[2] += locOffset[2] * 0.25
      } else if (cls.includes('RotationRate')) {
        const rr = sampleVecDist(mod.Distributions?.StartRotationRate, [0, 0, 0])
        rotRate = [rr[0] * Math.PI * 2, rr[1] * Math.PI * 2, rr[2] * Math.PI * 2]
      } else if (cls.includes('Rotation')) {
        rot = sampleVecDist(mod.Distributions?.StartRotation, [0, 0, 0])
      } else if (cls.includes('SubUV')) {
        subImageIndex = Math.floor(sampleFloatDist(mod.Distributions?.SubImageIndex, 0))
      }
    }

    const isStatic = Math.abs(vel[0]) + Math.abs(vel[1]) + Math.abs(vel[2]) < 0.01
    if (isStatic) {
      pos[0] += (emIdx - ((this.info.Emitters?.length || 1) - 1) / 2) * 140
    }

    const sx = Math.max(10, Math.min(180, Math.abs(size[0]) || 45))
    const sy = Math.max(10, Math.min(180, Math.abs(size[1]) || sx))
    const sz = Math.max(10, Math.min(180, Math.abs(size[2]) || sx))

    this.particles.push({
      emitterIdx: emIdx,
      isMesh: em.EmitterType === 'Mesh',
      materialPath: matPath,
      blendMode,
      subImagesH,
      subImagesV,
      subImageIndex,
      x: pos[0],
      y: pos[1],
      z: pos[2],
      vx: vel[0] * 0.25,
      vy: vel[1] * 0.25,
      vz: vel[2] * 0.25,
      ax: accel[0] * 0.25,
      ay: accel[1] * 0.25,
      az: accel[2] * 0.25,
      sizeX: sx,
      sizeY: sy,
      sizeZ: sz,
      baseSizeX: sx,
      baseSizeY: sy,
      baseSizeZ: sz,
      r: Math.min(1, Math.max(0.05, color[0])),
      g: Math.min(1, Math.max(0.05, color[1])),
      b: Math.min(1, Math.max(0.05, color[2])),
      a: Math.min(1, Math.max(0.05, alpha)),
      age: 0,
      lifetime,
      rotX: rot[0] * Math.PI * 2,
      rotY: rot[1] * Math.PI * 2,
      rotZ: rot[2] * Math.PI * 2,
      vRotX: rotRate[0],
      vRotY: rotRate[1],
      vRotZ: rotRate[2],
      colorOverLifeDist,
      alphaOverLifeDist,
      sizeByLifeDist
    })
  }

  public step(dt: number, activeFilter?: (emIdx: number) => boolean) {
    this.simTime += dt
    const emitters = this.info.Emitters || []

    emitters.forEach((em, idx) => {
      if (activeFilter && !activeFilter(idx)) return
      const lod = em.LODLevels?.[0]
      if (!lod) return
      const state = this.emitterStates[idx]
      if (!state) return

      const duration = Math.max(0.5, getModulePropNumber(lod.RequiredModule, 'EmitterDuration', 2.0))
      const loops = getModulePropNumber(lod.RequiredModule, 'EmitterLoops', 0)

      state.cycleTime += dt
      if (state.cycleTime >= duration) {
        state.cycleTime %= duration
        state.loopsDone++
        state.firedBursts.clear()
      }
      if (loops > 0 && state.loopsDone >= loops) return

      const rateFromSpawn = sampleFloatDist(lod.SpawnModule?.Distributions?.Rate, 0)
      const rateFromReq = getModulePropNumber(lod.RequiredModule, 'SpawnRate', 0)
      const effectiveRate = Math.max(rateFromSpawn, rateFromReq)

      if (effectiveRate > 0) {
        state.accumSpawn += effectiveRate * dt
        while (state.accumSpawn >= 1 && this.particles.length < this.maxParticles) {
          state.accumSpawn -= 1
          this.spawnParticleForEmitter(em, idx)
        }
      }

      const burstProp = lod.SpawnModule?.Properties?.find(p => p.Name === 'BurstList')
      const bursts = burstProp?.Bursts || []
      bursts.forEach((b, bIdx) => {
        if (!state.firedBursts.has(bIdx) && state.cycleTime >= (b.Time || 0)) {
          state.firedBursts.add(bIdx)
          const cnt = Math.min(30, Math.max(1, b.Count || 1))
          for (let i = 0; i < cnt && this.particles.length < this.maxParticles; i++) {
            this.spawnParticleForEmitter(em, idx)
          }
        }
      })

      if (effectiveRate === 0 && bursts.length === 0) {
        const hasAny = this.particles.some(p => p.emitterIdx === idx)
        if (!hasAny) this.spawnParticleForEmitter(em, idx)
      }
    })

    const alive: SimParticle[] = []
    for (const p of this.particles) {
      if (activeFilter && !activeFilter(p.emitterIdx)) continue
      p.age += dt
      if (p.age >= p.lifetime) continue
      const lifeRatio = p.age / p.lifetime

      p.vx += p.ax * dt
      p.vy += p.ay * dt
      p.vz += p.az * dt
      p.x += p.vx * dt
      p.y += p.vy * dt
      p.z += p.vz * dt
      p.rotX += p.vRotX * dt
      p.rotY += p.vRotY * dt
      p.rotZ += p.vRotZ * dt

      if (p.colorOverLifeDist) {
        const c = sampleVecDist(p.colorOverLifeDist, [p.r, p.g, p.b], lifeRatio)
        p.r = Math.min(1, Math.max(0, c[0]))
        p.g = Math.min(1, Math.max(0, c[1]))
        p.b = Math.min(1, Math.max(0, c[2]))
      }
      if (p.alphaOverLifeDist) {
        p.a = Math.min(1, Math.max(0, sampleFloatDist(p.alphaOverLifeDist, p.a, lifeRatio)))
      }
      if (p.sizeByLifeDist) {
        const mul = sampleVecDist(p.sizeByLifeDist, [1, 1, 1], lifeRatio)
        p.sizeX = Math.max(4, p.baseSizeX * Math.abs(mul[0] || 1))
        p.sizeY = Math.max(4, p.baseSizeY * Math.abs(mul[1] || mul[0] || 1))
      }

      alive.push(p)
    }
    this.particles = alive
  }

  /**
   * 将当前粒子状态绘制到 2D / 3D 投影 Canvas 上
   */
  public renderToCanvas(
    ctx: CanvasRenderingContext2D,
    w: number,
    h: number,
    options: {
      camYaw?: number
      camPitch?: number
      camDistance?: number
      useMaterialSprites?: boolean
      selectedEmitterIdx?: number
      transparentBg?: boolean
      showGrid?: boolean
    } = {}
  ) {
    const camYaw = options.camYaw ?? 0.6
    const camPitch = options.camPitch ?? 0.35
    const camDistance = options.camDistance ?? 1800
    const useMat = options.useMaterialSprites ?? true

    ctx.clearRect(0, 0, w, h)

    if (!options.transparentBg) {
      const grad = ctx.createRadialGradient(w / 2, h / 2, 20, w / 2, h / 2, Math.max(w, h) * 0.7)
      grad.addColorStop(0, '#1a202c')
      grad.addColorStop(1, '#0b0e14')
      ctx.fillStyle = grad
      ctx.fillRect(0, 0, w, h)
    }

    const project3D = (x: number, y: number, z: number) => {
      const cy = Math.cos(camYaw)
      const sy = Math.sin(camYaw)
      const cp = Math.cos(camPitch)
      const sp = Math.sin(camPitch)

      const rx = x * cy - y * sy
      const ry = x * sy + y * cy
      const rz = z

      const camZ = ry * sp + rz * cp + camDistance
      if (camZ <= 50) return null
      const fov = 700
      const scale = fov / camZ
      return {
        sx: w / 2 + rx * scale,
        sy: h / 2 - (ry * sp - rz * cp) * scale,
        depth: camZ,
        scale
      }
    }

    if (options.showGrid) {
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.07)'
      ctx.lineWidth = 1
      const gridExtent = 600
      const step = 150
      for (let g = -gridExtent; g <= gridExtent; g += step) {
        const p1 = project3D(g, -gridExtent, 0)
        const p2 = project3D(g, gridExtent, 0)
        if (p1 && p2) {
          ctx.beginPath()
          ctx.moveTo(p1.sx, p1.sy)
          ctx.lineTo(p2.sx, p2.sy)
          ctx.stroke()
        }
        const p3 = project3D(-gridExtent, g, 0)
        const p4 = project3D(gridExtent, g, 0)
        if (p3 && p4) {
          ctx.beginPath()
          ctx.moveTo(p3.sx, p3.sy)
          ctx.lineTo(p4.sx, p4.sy)
          ctx.stroke()
        }
      }

      const origin = project3D(0, 0, 0)
      const axX = project3D(180, 0, 0)
      const axY = project3D(0, 180, 0)
      const axZ = project3D(0, 0, 180)
      if (origin) {
        ctx.lineWidth = 2
        if (axX) {
          ctx.strokeStyle = '#ef4444'
          ctx.beginPath()
          ctx.moveTo(origin.sx, origin.sy)
          ctx.lineTo(axX.sx, axX.sy)
          ctx.stroke()
        }
        if (axY) {
          ctx.strokeStyle = '#22c55e'
          ctx.beginPath()
          ctx.moveTo(origin.sx, origin.sy)
          ctx.lineTo(axY.sx, axY.sy)
          ctx.stroke()
        }
        if (axZ) {
          ctx.strokeStyle = '#3b82f6'
          ctx.beginPath()
          ctx.moveTo(origin.sx, origin.sy)
          ctx.lineTo(axZ.sx, axZ.sy)
          ctx.stroke()
        }
      }
    }

    const projected = this.particles
      .map(p => {
        const proj = project3D(p.x, p.y, p.z)
        return proj ? { p, proj } : null
      })
      .filter((x): x is { p: SimParticle; proj: NonNullable<ReturnType<typeof project3D>> } => x !== null)
      .sort((a, b) => b.proj.depth - a.proj.depth)

    for (const { p, proj } of projected) {
      const lifeRatio = p.age / p.lifetime
      const fade = lifeRatio > 0.8 ? (1 - lifeRatio) / 0.2 : 1.0
      const alpha = Math.max(0.05, p.a * fade)
      const r = Math.round(p.r * 255)
      const g = Math.round(p.g * 255)
      const b = Math.round(p.b * 255)
      const radius = Math.max(6, p.sizeX * proj.scale)

      ctx.save()
      ctx.translate(proj.sx, proj.sy)
      ctx.rotate(p.rotZ)

      if (p.blendMode.includes('Additive')) {
        ctx.globalCompositeOperation = 'lighter'
      } else {
        ctx.globalCompositeOperation = 'source-over'
      }

      const matSprite = useMat && p.materialPath ? this.materialSprites.get(p.materialPath) : null

      if (matSprite) {
        ctx.globalAlpha = alpha
        const totalSub = p.subImagesH * p.subImagesV
        if (totalSub > 1) {
          const idx = Math.abs(p.subImageIndex) % totalSub
          const col = idx % p.subImagesH
          const row = Math.floor(idx / p.subImagesH)
          const sw = matSprite.width / p.subImagesH
          const sh = matSprite.height / p.subImagesV
          ctx.drawImage(
            matSprite,
            col * sw,
            row * sh,
            sw,
            sh,
            -radius,
            -radius,
            radius * 2,
            radius * 2
          )
        } else {
          ctx.drawImage(matSprite, -radius, -radius, radius * 2, radius * 2)
        }

        // 若是 Mesh 粒子，叠加上色线框与色彩光晕以体现粒子顶点色和立体框
        if (p.isMesh) {
          ctx.strokeStyle = `rgba(${r}, ${g}, ${b}, ${Math.min(1, alpha * 0.9)})`
          ctx.lineWidth = p.emitterIdx === options.selectedEmitterIdx ? 2 : 1
          ctx.strokeRect(-radius, -radius, radius * 2, radius * 2)
        }
      } else if (p.isMesh) {
        ctx.fillStyle = `rgba(${r}, ${g}, ${b}, ${alpha * 0.35})`
        ctx.strokeStyle = `rgba(${r}, ${g}, ${b}, ${Math.min(1, alpha + 0.25)})`
        ctx.lineWidth = p.emitterIdx === options.selectedEmitterIdx ? 2 : 1.2
        const half = radius * 0.85
        ctx.beginPath()
        ctx.rect(-half, -half, half * 2, half * 2)
        ctx.fill()
        ctx.stroke()

        ctx.beginPath()
        ctx.moveTo(-half, -half)
        ctx.lineTo(half, half)
        ctx.stroke()
      } else {
        const radGrad = ctx.createRadialGradient(0, 0, 0, 0, 0, radius)
        radGrad.addColorStop(0, `rgba(255, 255, 255, ${alpha})`)
        radGrad.addColorStop(0.4, `rgba(${r}, ${g}, ${b}, ${alpha * 0.8})`)
        radGrad.addColorStop(1, `rgba(${r}, ${g}, ${b}, 0)`)
        ctx.fillStyle = radGrad
        ctx.beginPath()
        ctx.arc(0, 0, radius, 0, Math.PI * 2)
        ctx.fill()
      }
      ctx.restore()
    }
  }
}

/**
 * 供 UMG / Blueprint 预览器一键调用的静态或帧快照渲染函数
 */
export async function renderUMGParticleToCanvas(
  info: ParticleSystemData,
  loadImage: (texPath: string) => Promise<HTMLImageElement | null>,
  width = 256,
  height = 256,
  externalMaterials?: Record<string, any>,
  simSeconds = 0.6
): Promise<HTMLCanvasElement | null> {
  if (!info || !Array.isArray(info.Emitters)) return null
  const sprites = await buildParticleMaterialSprites(info, loadImage, externalMaterials)
  const engine = new ParticleSimulationEngine(info)
  engine.setMaterialSprites(sprites)

  const steps = Math.max(2, Math.min(30, Math.floor(simSeconds / 0.05)))
  for (let i = 0; i < steps; i++) {
    engine.step(0.05)
  }

  const canvas = document.createElement('canvas')
  canvas.width = Math.max(32, Math.min(1024, Math.round(width)))
  canvas.height = Math.max(32, Math.min(1024, Math.round(height)))
  const ctx = canvas.getContext('2d')
  if (!ctx) return null

  engine.renderToCanvas(ctx, canvas.width, canvas.height, {
    transparentBg: true,
    showGrid: false,
    useMaterialSprites: true,
    camDistance: 1400
  })
  return canvas
}
