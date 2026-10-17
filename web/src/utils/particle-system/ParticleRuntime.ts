import { particleSubImageFrame } from './ParticleSubUV'
// 通用粒子系统运行时与渲染桥接模块 (ParticleRuntime.ts)
// 供 ParticleSystemViewer (Cascade 面板) 与 UMG / Blueprint 预览器共同复用

import { normalizeMaterialEntry } from '../material-shader/UMGMaterialBridge'
import type { ParticleMeshGeometry } from './ParticleMeshGeometry'
import { drawParticleMeshPreview } from './ParticleMeshPreview'
import { ParticleWebGlRenderer } from './ParticleWebGlRenderer'
import { createParticleMaterialCompatibility, particleMaterialCompatibility } from './ParticleMaterialCompatibility'
import { createBgnSurface, updateBgnSurface, disposeBgnSurface } from './BgnCompatibilitySurface'
import { MaterialSurface } from '../material-runtime/MaterialSurface'
import { materialPrograms } from '../material-runtime/MaterialProgramCache'

import {
  type NonSlateParticleDiagnostics,
  type ParticleCutoutConfiguration,
  hasNonSlateParticleDiagnostics
} from '../material-runtime/ParticleProgramDiagnostics'

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

export interface ParticleDynamicParameter {
  ValueMethod: string
  SpawnTimeOnly: boolean
  UseEmitterTime: boolean
  Distribution: any
}

export interface ParticleModuleInfo {
  DynamicParameters?: ParticleDynamicParameter[]
  ExportIndex: number
  Name: string
  Class: string
  Category: string
  Summary: string
  Properties: ParticleModuleProp[]
  Distributions?: Record<string, any>
  References?: Record<string, any>
  RawProperties?: Record<string, any>
  CutoutConfiguration?: ParticleCutoutConfiguration | null
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
  pivotOffset?: [number, number]
  subImageDist?: any
  subImageRandom?: number
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
  /** APPROXIMATION: stable per-particle random, not native RNG sequence. */
  opacityMaskClipValue?: number
  shaderRandom?: number
  dynamicParameters?: ParticleDynamicParameter[]
  dynamicValues?: number[]
  dynamicRandom?: number[]
  meshPreRotation?: [number, number, number]
  vRotX: number
  vRotY: number
  vRotZ: number
  baseColor?: [number, number, number, number]
  colorScaleModules?: Array<{ color: any; alpha: any; emitterTime: boolean }>
  colorOverLifeDist?: any
  alphaOverLifeDist?: any
  sizeByLifeDist?: any
  sizeLifeModules?: Array<{ distribution: any; axes: boolean[]; replace: boolean }>
  velocityOverLifeDist?: any
  velocityAbsolute?: boolean
  baseVelocity?: [number, number, number]
  accelerationOverLifeDist?: any
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

// Native scalar lookup: Mercury 0x1415935F0 (op1/op3), 0x141593020 (op2).
// Random vector sampling preserves authored bounds and axis correlation.
// APPROXIMATION: RNG stream/order is not UE seeded parity; vector op3 remains unsupported.
function sampleCookedTable(dist: any, dimensions: number, time = 0, random = Math.random): number[] | null {
  const values = dist.TableValues
  const count = dist.EntryCount
  const stride = dist.EntryStride
  const op = dist.TableOp
  if (![1, 2, 3].includes(op) || (dimensions !== 1 && op === 3) ||
      !Number.isInteger(count) || count < 1 ||
      stride !== dimensions * (op === 1 ? 1 : 2) ||
      !Array.isArray(values) || values.length !== count * stride ||
      !values.every(Number.isFinite)) return null
  // Do not invent a normalized time range for old/incomplete DTOs.
  if (count > 1 && (!Number.isFinite(dist.TimeScale) || !Number.isFinite(dist.TimeBias))) return null
  const position = count === 1 ? 0 : Math.max(0, (time - dist.TimeBias) * dist.TimeScale)
  if (!Number.isFinite(position)) return null
  const lower = Math.floor(position)
  const a = Math.min(count - 1, lower) * stride
  const b = Math.min(count - 1, lower + 1) * stride
  const fraction = position - lower
  const interpolate = (column: number) => values[a + column] +
    (values[b + column] - values[a + column]) * fraction
  if (op === 2) {
    if (dimensions === 1) return [interpolate(0) + (interpolate(1) - interpolate(0)) * random()]
    if (dist.SubEntryStride !== 3 || ![0, 1, 2, 3, 4].includes(dist.LockFlag)) return null
    // Each axis belongs to a random group; locked axes share a fraction, not a value.
    const groups = [[0, 1, 2], [0, 0, 2], [0, 1, 0], [0, 1, 1], [0, 0, 0]][dist.LockFlag]!
    const fractions = new Map<number, number>()
    return groups.map((group, axis) => {
      if (!fractions.has(group)) fractions.set(group, random())
      const low = interpolate(axis), high = interpolate(dist.SubEntryStride + axis)
      return low + (high - low) * fractions.get(group)!
    })
  }
  if (op === 3) return [interpolate(random() > 0.5 ? 1 : 0)]
  return Array.from({ length: dimensions }, (_, i) => interpolate(i))
}

export function sampleFloatDist(dist: any, fallback = 0, ratio?: number, random = Math.random): number {
  if (!dist) return fallback
  const cooked = sampleCookedTable(dist, 1, ratio, random)
  if (cooked) return cooked[0]
  // APPROXIMATION: unsupported/incomplete tables use bounds, never flatten random columns into time.
  const mn = typeof dist.MinValue === 'number' ? dist.MinValue : fallback
  const mx = typeof dist.MaxValue === 'number' ? dist.MaxValue : mn
  return mn === mx ? mn : mn + random() * (mx - mn)
}

export function sampleVecDist(
  dist: any,
  fallback: [number, number, number] = [0, 0, 0],
  ratio?: number
): [number, number, number] {
  if (!dist) return fallback
  const cooked = sampleCookedTable(dist, 3, ratio)
  if (cooked) return cooked as [number, number, number]
  // APPROXIMATION: random vector locking/extremes are not yet decoded; use summary bounds.
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

function moduleEnabled(mod: ParticleModuleInfo | null | undefined): boolean {
  return !!mod && mod.Properties?.find(p => p.Name === 'bEnabled')?.Bool !== false
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
  externalMaterials?: Record<string, any>,
  projectId?: string
): Promise<Map<string, HTMLCanvasElement>> {
  const result = new Map<string, HTMLCanvasElement>()
  const matPool: Record<string, any> = {
    ...(externalMaterials || {}),
    ...(info.Materials || {})
  }

  for (const em of info.Emitters || []) {
    const matPath = getEmitterMaterialPath(em)
    if (!matPath || result.has(matPath)) continue
    if (projectId) {
      try {
        const compatible = await createBgnSurface(projectId, matPath, loadImage)
        if (compatible) { result.set(matPath, compatible); continue }
      } catch (error) { console.warn('BGN compatibility preview unavailable', error) }
    }
    const rawMat = matPool[matPath]
    if (!rawMat) continue
    try {
      const family = await createParticleMaterialCompatibility(rawMat, matPath, loadImage)
      if (family) { result.set(matPath, family); continue }
    } catch (error) { console.warn('Particle family preview unavailable', error) }
    const spec = normalizeMaterialEntry(rawMat, matPath)
    if (!spec) continue

    // 1. 尝试从 fac847c 的 MaterialSurface / MaterialProgramCache 获取真实 WebGL2 着色器渲染（仅当 Slate 真实就绪且非粒子专有诊断时）
    if (projectId) {
      try {
        const contract = await materialPrograms.load(projectId, matPath)
        // 关键守则：particlePrograms/非 Slate 诊断或者 renderingReady=false 绝对不作为真实着色器运行，仅渲染就绪的 Slate 程序
        if (!hasNonSlateParticleDiagnostics(contract) && 'renderingReady' in contract && contract.renderingReady && contract.uniformProgram && contract.blendMode !== 'BLEND_Additive') {
          const surface = new MaterialSurface(contract, 128, 128)
          try {
            await surface.prepare(loadImage)
            surface.instance.setParameters(
              spec.scalars,
              Object.fromEntries(Object.entries(spec.vectors).map(([k, v]) => [k, [v.r, v.g, v.b, v.a]]))
            )
            const drawn = surface.draw(0.35)
            // 复制到独立 canvas 避免 surface 销毁后丢失
            const outCanvas = document.createElement('canvas')
            outCanvas.width = drawn.width
            outCanvas.height = drawn.height
            const ctx = outCanvas.getContext('2d')
            if (ctx) {
              ctx.drawImage(drawn, 0, 0)
              result.set(matPath, outCanvas)
              surface.dispose()
              continue
            }
          } catch {
            surface.dispose()
          }
        }
      } catch {
        // shader 不支持或加载失败时，平滑回退到下方的 2D 模拟管线
      }
    }

    // APPROXIMATION: raw texture reference, NOT execution of the material graph.
    // Prefer an explicit base/color slot; do not reinterpret arbitrary vectors
    // (including world-position offsets) as tint or invent a generic UMG effect.
    const candidates = [...spec.textures].sort((a, b) => {
      const rank = (name: string) => /^(BaseTexture|RGBTexture|Color_texture)$/i.test(name) ? 0
        : /mask|normal|noise|object|shape/i.test(name) ? 2 : 1
      return rank(a.name) - rank(b.name)
    })
    for (const candidate of candidates) {
      try {
        const image = await loadImage(candidate.path)
        if (!image?.naturalWidth || !image.naturalHeight) continue
        const canvas = document.createElement('canvas')
        const scale = Math.min(1, 512 / Math.max(image.naturalWidth, image.naturalHeight))
        canvas.width = Math.max(1, Math.round(image.naturalWidth * scale))
        canvas.height = Math.max(1, Math.round(image.naturalHeight * scale))
        canvas.getContext('2d')!.drawImage(image, 0, 0, canvas.width, canvas.height)
        canvas.dataset.previewProfile = 'approximate-raw-texture'
        canvas.dataset.sourceTexture = candidate.path
        result.set(matPath, canvas)
        break
      } catch { /* Try the next real texture; no fabricated fallback image. */ }
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
  private meshRenderer: ParticleWebGlRenderer | null = null
  private meshRendererFailed = false
  public dispose(keepSurfaces = false) {
    this.meshRenderer?.dispose(); this.meshRenderer = null
    if (!keepSurfaces) this.materialSprites.forEach(disposeBgnSurface)
  }

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
    if (!lod || lod.Enabled === false) return

    let lifetime = 5.0
    let size: [number, number, number] = [55, 55, 55]
    let vel: [number, number, number] = [0, 0, 0]
    let accel: [number, number, number] = [0, 0, 0]
    let color: [number, number, number] = [1, 1, 1]
    let alpha = 1.0
    let pos: [number, number, number] = [0, 0, 0]
    let rot: [number, number, number] = [0, 0, 0]
    // No rotation-rate module means no angular motion, not a decorative random spin.
    let rotRate: [number, number, number] = [0, 0, 0]

    const colorScaleModules: NonNullable<SimParticle['colorScaleModules']> = []
    let colorOverLifeDist: any = null
    let alphaOverLifeDist: any = null
    const sizeLifeModules: NonNullable<SimParticle['sizeLifeModules']> = []
    let velocityOverLifeDist: any = null
    let accelerationOverLifeDist: any = null
    let velocityAbsolute = false
    let dynamicParameters: ParticleDynamicParameter[] | undefined
    let dynamicModuleCount = 0

    const matPath = getEmitterMaterialPath(em) || undefined
    const rawMat = matPath ? this.info.Materials?.[matPath] : null
    const overrides = rawMat?.MaterialInstanceInfo?.BasePropertyOverrides
    const master = rawMat?.MaterialInstanceInfo?.ParentMaterialInfo ?? rawMat?.MaterialInfo
    const blendMode = (overrides?.bOverride_BlendMode === true ? overrides.BlendMode : undefined)
      ?? master?.BlendMode ?? 'BLEND_Opaque'
    const clip = overrides?.bOverride_OpacityMaskClipValue === true
      ? overrides.OpacityMaskClipValue : master?.OpacityMaskClipValue
    const opacityMaskClipValue = Number.isFinite(clip) ? clip : 1 / 3

    const subImagesH = Math.max(1, getModulePropNumber(lod.RequiredModule, 'SubImages_Horizontal', 1))
    const subImagesV = Math.max(1, getModulePropNumber(lod.RequiredModule, 'SubImages_Vertical', 1))
    const pivotOffset: [number, number] = [0, 0]
    let subImageIndex = 0
    let subImageDist: any
    let subImageRandom: number | undefined

    const reqOrigin = lod.RequiredModule?.Properties?.find(p => p.Name === 'EmitterOrigin')?.Vector
    if (Array.isArray(reqOrigin) && reqOrigin.length >= 3) {
      pos = [reqOrigin[0], reqOrigin[1], reqOrigin[2]]
    }

    for (const mod of lod.Modules || []) {
      if (!moduleEnabled(mod)) continue
      const cls = mod.Class || ''
      if (cls === 'ParticleModulePivotOffset') {
        const offset = mod.Properties?.find(p => p.Name === 'PivotOffset')?.Vector
        if (offset?.length === 2 && offset.every(Number.isFinite)) {
          pivotOffset[0] += offset[0]!; pivotOffset[1] += offset[1]!
        }
      }
      if (cls === 'ParticleModuleParameterDynamic') {
        // Unknown value methods stay unsupported rather than treated as user-set.
        const params = mod.DynamicParameters
        dynamicModuleCount++
        dynamicParameters = dynamicModuleCount === 1 && params?.length === 4 && params.every(p =>
          /(^|::)EDPV_UserSet$/.test(p.ValueMethod) &&
          Number.isFinite(sampleFloatDist(p.Distribution, NaN, 0, () => 0.5))) ? params : undefined
      }
      if (cls === 'ParticleModuleVelocityOverLifetime') {
        velocityOverLifeDist = mod.Distributions?.VelOverLife
        velocityAbsolute = mod.Properties?.find(p => p.Name === 'Absolute')?.Bool ?? false
      } else if (cls === 'ParticleModuleAccelerationOverLifetime') {
        accelerationOverLifeDist = mod.Distributions?.AccelOverLife
      } else if (cls === 'ParticleModuleSizeScale') {
        sizeLifeModules.push({ distribution: mod.Distributions?.SizeScale, axes: [true,true,true], replace: true })
      } else if (cls === 'ParticleModuleLifetime') {
        lifetime = Math.max(0.2, sampleFloatDist(mod.Distributions?.Lifetime, 3.0))
        // Preserve authored lifetime; do not shorten long-lived background effects.
      } else if (cls === 'ParticleModuleSizeMultiplyLife') {
        sizeLifeModules.push({ distribution: mod.Distributions?.LifeMultiplier,
          axes: ['MultiplyX','MultiplyY','MultiplyZ'].map(name => mod.Properties?.find(p => p.Name === name)?.Bool ?? true),
          replace: false })
      } else if (cls === 'ParticleModuleSize') {
        size = sampleVecDist(mod.Distributions?.StartSize, [55, 55, 55])
      } else if (cls === 'ParticleModuleVelocity') {
        vel = sampleVecDist(mod.Distributions?.StartVelocity, [0, 0, 0])
        const rad = sampleFloatDist(mod.Distributions?.StartVelocityRadial, 0)
        if (rad !== 0) {
          const theta = Math.random() * Math.PI * 2
          vel[0] += Math.cos(theta) * rad
          vel[1] += Math.sin(theta) * rad
        }
      } else if (cls === 'ParticleModuleAcceleration') {
        accel = sampleVecDist(mod.Distributions?.Acceleration, [0, 0, 0])
      } else if (cls === 'ParticleModuleAccelerationConstant') {
        const value = mod.Properties?.find(p => p.Name === 'Acceleration')?.Vector
        if (value?.length === 3 && value.every(Number.isFinite)) accel = [value[0]!, value[1]!, value[2]!]
      } else if (cls === 'ParticleModuleColorScaleOverLife') {
        colorScaleModules.push({ color: mod.Distributions?.ColorScaleOverLife,
          alpha: mod.Distributions?.AlphaScaleOverLife,
          emitterTime: mod.Properties?.find(p => p.Name === 'bEmitterTime')?.Bool ?? false })
      } else if (cls === 'ParticleModuleColorOverLife') {
        colorOverLifeDist = mod.Distributions?.ColorOverLife
        alphaOverLifeDist = mod.Distributions?.AlphaOverLife
        if (colorOverLifeDist) color = sampleVecDist(colorOverLifeDist, [1, 1, 1], 0)
        if (alphaOverLifeDist) alpha = sampleFloatDist(alphaOverLifeDist, 1.0, 0)
      } else if (cls === 'ParticleModuleColor') {
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
        pos[0] += locOffset[0]
        pos[1] += locOffset[1]
        pos[2] += locOffset[2]
      } else if (cls === 'ParticleModuleRotationRate' || cls === 'ParticleModuleMeshRotationRate') {
        const rr = cls.includes('Mesh')
          ? sampleVecDist(mod.Distributions?.StartRotationRate, [0, 0, 0])
          : [0, 0, sampleFloatDist(mod.Distributions?.StartRotationRate, 0)]
        rotRate = [rr[0] * Math.PI * 2, rr[1] * Math.PI * 2, rr[2] * Math.PI * 2]
      } else if (cls === 'ParticleModuleRotation' || cls === 'ParticleModuleMeshRotation') {
        rot = cls.includes('Mesh')
          ? sampleVecDist(mod.Distributions?.StartRotation, [0, 0, 0])
          : [0, 0, sampleFloatDist(mod.Distributions?.StartRotation, 0)]
      } else if (cls === 'ParticleModuleSubUV') {
        // APPROXIMATION: discrete curve-driven frames; interpolation/random modes pending.
        subImageDist = mod.Distributions?.SubImageIndex
        subImageRandom = Math.random()
        subImageIndex = sampleFloatDist(subImageDist, 0, 0, () => subImageRandom!)
      }
    }

    // Emitter arrangement is authored data, not a preview grid.

    // Keep authored scale; framing belongs to the preview camera, not simulation data.
    const sx = Number.isFinite(size[0]) ? Math.abs(size[0]) : 55
    const sy = Number.isFinite(size[1]) ? Math.abs(size[1]) : 55
    const sz = Number.isFinite(size[2]) ? Math.abs(size[2]) : 55

    // APPROXIMATION: retain one random fraction per dynamic channel across updates.
    // This prevents flicker; it does not reproduce the native seeded RNG stream.
    const dynamicRandom = dynamicParameters?.map(() => Math.random())
    this.particles.push({
      emitterIdx: emIdx,
      isMesh: em.EmitterType === 'Mesh',
      materialPath: matPath,
      blendMode,
      opacityMaskClipValue,
      subImagesH,
      subImagesV,
      pivotOffset,
      subImageIndex,
      subImageDist,
      subImageRandom,
      x: pos[0],
      y: pos[1],
      z: pos[2],
      vx: vel[0],
      vy: vel[1],
      vz: vel[2],
      ax: accel[0],
      ay: accel[1],
      az: accel[2],
      sizeX: sx,
      sizeY: sy,
      sizeZ: sz,
      baseSizeX: sx,
      baseSizeY: sy,
      baseSizeZ: sz,
      r: Math.max(0, color[0]),
      g: Math.max(0, color[1]),
      b: Math.max(0, color[2]),
      a: alpha,
      dynamicParameters,
      dynamicRandom,
      dynamicValues: dynamicParameters?.map((p, channel) => sampleFloatDist(p.Distribution, NaN,
        p.UseEmitterTime ? (this.emitterStates[emIdx]?.cycleTime ?? 0) : 0, () => dynamicRandom![channel]!)),
      shaderRandom: Math.random(),
      age: 0,
      lifetime,
      // APPROXIMATION: mesh-local pre-rotation in degrees, separate from particle turns.
      meshPreRotation: sampleVecDist(lod.TypeDataModule?.Distributions?.RollPitchYawRange)
        .map(degrees => degrees * Math.PI / 180) as [number, number, number],
      rotX: rot[0] * Math.PI * 2,
      rotY: rot[1] * Math.PI * 2,
      rotZ: rot[2] * Math.PI * 2,
      vRotX: rotRate[0],
      vRotY: rotRate[1],
      vRotZ: rotRate[2],
      baseColor: [color[0], color[1], color[2], alpha],
      colorScaleModules,
      colorOverLifeDist,
      alphaOverLifeDist,
      sizeLifeModules,
      velocityOverLifeDist,
      velocityAbsolute,
      baseVelocity: [...vel],
      accelerationOverLifeDist
    })
  }

  public step(dt: number, activeFilter?: (emIdx: number) => boolean) {
    if (!Number.isFinite(dt) || dt <= 0) return
    this.simTime += dt
    const emitters = this.info.Emitters || []

    emitters.forEach((em, idx) => {
      if (activeFilter && !activeFilter(idx)) return
      const lod = em.LODLevels?.[0]
      if (!lod || lod.Enabled === false) return
      const state = this.emitterStates[idx]
      if (!state) return

      const duration = Math.max(0.5, getModulePropNumber(lod.RequiredModule, 'EmitterDuration', 2.0))
      const loops = getModulePropNumber(lod.RequiredModule, 'EmitterLoops', 0)

      state.cycleTime += dt
      if (state.cycleTime >= duration) {
        state.loopsDone += Math.floor(state.cycleTime / duration)
        state.cycleTime %= duration
        state.firedBursts.clear()
      }
      if (loops > 0 && state.loopsDone >= loops) return

      const spawnEnabled = moduleEnabled(lod.SpawnModule)
      const rate = lod.SpawnModule
        ? (spawnEnabled ? sampleFloatDist(lod.SpawnModule.Distributions?.Rate, 0, state.cycleTime) : 0)
        : getModulePropNumber(lod.RequiredModule, 'SpawnRate', 0)
      const scale = spawnEnabled ? sampleFloatDist(lod.SpawnModule?.Distributions?.RateScale, 1, state.cycleTime) : 1
      const effectiveRate = Number.isFinite(rate * scale) ? Math.max(0, rate * scale) : 0

      if (effectiveRate > 0) {
        const requested = state.accumSpawn + effectiveRate * dt
        if (Number.isFinite(requested)) {
          const whole = Math.floor(requested)
          // APPROXIMATION: the preview cap drops excess births rather than queueing
          // a burst for later frames. Keep only the fractional spawn remainder.
          state.accumSpawn = requested - whole
          const available = Math.max(0, this.maxParticles - this.particles.length)
          for (let i = 0; i < Math.min(whole, available); i++) this.spawnParticleForEmitter(em, idx)
        } else {
          state.accumSpawn = 0
        }
      }

      const burstProp = spawnEnabled ? lod.SpawnModule?.Properties?.find(p => p.Name === 'BurstList') : undefined
      const bursts = burstProp?.Bursts || []
      bursts.forEach((b, bIdx) => {
        if (!state.firedBursts.has(bIdx) && state.cycleTime >= (b.Time || 0)) {
          state.firedBursts.add(bIdx)
          // APPROXIMATION: inclusive uniform burst range; native RNG sequence unverified.
          const high = Number.isFinite(b.Count) ? Math.max(0, Math.floor(b.Count)) : 0
          const low = Number.isFinite(b.CountLow) && b.CountLow >= 0 ? Math.min(high, Math.floor(b.CountLow)) : high
          const cnt = low + Math.floor(Math.random() * (high - low + 1))
          for (let i = 0; i < cnt && this.particles.length < this.maxParticles; i++) {
            this.spawnParticleForEmitter(em, idx)
          }
        }
      })

    })

    const alive: SimParticle[] = []
    for (const p of this.particles) {
      if (activeFilter && !activeFilter(p.emitterIdx)) continue
      p.age += dt
      if (p.age >= p.lifetime) continue
      const lifeRatio = p.age / p.lifetime
      if (p.subImageDist) p.subImageIndex = sampleFloatDist(p.subImageDist, 0, lifeRatio, () => p.subImageRandom ?? 0.5)
      p.dynamicParameters?.forEach((param, channel) => {
        if (!param.SpawnTimeOnly && p.dynamicValues) p.dynamicValues[channel] = sampleFloatDist(
          param.Distribution, NaN, param.UseEmitterTime ? (this.emitterStates[p.emitterIdx]?.cycleTime ?? 0) : lifeRatio,
          () => p.dynamicRandom![channel]!)
      })

      // APPROXIMATION: CPU integration in emitter coordinates; no UE seeded parity.
      const acceleration = p.accelerationOverLifeDist
        ? sampleVecDist(p.accelerationOverLifeDist, [0, 0, 0], lifeRatio)
        : [p.ax, p.ay, p.az]
      const base = p.baseVelocity ?? [p.vx, p.vy, p.vz]
      for (let axis = 0; axis < 3; axis++) base[axis] += acceleration[axis]! * dt
      p.baseVelocity = base as [number, number, number]
      const factor = p.velocityOverLifeDist
        ? sampleVecDist(p.velocityOverLifeDist, [1, 1, 1], lifeRatio) : [1, 1, 1]
      p.vx = p.velocityAbsolute ? factor[0]! : base[0]! * factor[0]!
      p.vy = p.velocityAbsolute ? factor[1]! : base[1]! * factor[1]!
      p.vz = p.velocityAbsolute ? factor[2]! : base[2]! * factor[2]!
      p.x += p.vx * dt
      p.y += p.vy * dt
      p.z += p.vz * dt
      p.rotX += p.vRotX * dt
      p.rotY += p.vRotY * dt
      p.rotZ += p.vRotZ * dt

      // APPROXIMATION: evaluate replacement before scaling; native arbitrary module
      // ordering is not reproduced. Rebuild from base to avoid cumulative multiplication.
      if (p.colorScaleModules?.length && p.baseColor) {
        [p.r, p.g, p.b, p.a] = p.baseColor
      }
      if (p.colorOverLifeDist) {
        const c = sampleVecDist(p.colorOverLifeDist, [p.r, p.g, p.b], lifeRatio)
        p.r = Math.max(0, c[0])
        p.g = Math.max(0, c[1])
        p.b = Math.max(0, c[2])
      }
      if (p.alphaOverLifeDist) {
        p.a = sampleFloatDist(p.alphaOverLifeDist, p.a, lifeRatio)
      }
      for (const scale of p.colorScaleModules ?? []) {
        const time = scale.emitterTime ? (this.emitterStates[p.emitterIdx]?.cycleTime ?? 0) : lifeRatio
        const rgb = sampleVecDist(scale.color, [1, 1, 1], time)
        p.r *= rgb[0]; p.g *= rgb[1]; p.b *= rgb[2]
        p.a *= sampleFloatDist(scale.alpha, 1, time)
      }
      if (p.sizeLifeModules?.length || p.sizeByLifeDist) {
        const base = [p.baseSizeX, p.baseSizeY, p.baseSizeZ]
        const size = [...base]
        // APPROXIMATION: ordered CPU evaluation; native random curve parity pending.
        const modules = p.sizeLifeModules ?? [{ distribution: p.sizeByLifeDist, axes: [true,true,true], replace: true }]
        for (const module of modules) {
          const multiplier = sampleVecDist(module.distribution, [1,1,1], lifeRatio)
          for (let axis = 0; axis < 3; axis++) if (module.axes[axis]) {
            size[axis] = (module.replace ? base[axis]! : size[axis]!) * multiplier[axis]!
          }
        }
        p.sizeX = size[0]!; p.sizeY = size[1]!; p.sizeZ = size[2]!
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
      camTarget?: [number, number, number]
      meshGeometry?: ReadonlyMap<number, ParticleMeshGeometry>
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
    if (useMat) for (const surface of this.materialSprites.values()) updateBgnSurface(surface, this.simTime)

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

      const target = options.camTarget ?? [0, 0, 0]
      x -= target[0]; y -= target[1]; z -= target[2]
      const rx = x * cy - y * sy
      const ry = x * sy + y * cy
      const rz = z

      const camZ = ry * cp + rz * sp + camDistance
      if (camZ <= 50) return null
      // 保持等比投影，避免视口宽度拉伸或高宽变化时粒子与网格形变
      const baseDim = Math.min(w, h)
      const scale = (baseDim * 1.5) / camZ
      return {
        sx: w / 2 + rx * scale,
        sy: h / 2 - (rz * cp - ry * sp) * scale,
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

    let gpuMeshes = false
    const gpuParticles = this.particles.filter(p => p.isMesh || (useMat && !!particleMaterialCompatibility(p.materialPath ? this.materialSprites.get(p.materialPath) : undefined)))
    if ((options.meshGeometry?.size || gpuParticles.length) && !this.meshRendererFailed) {
      try {
        this.meshRenderer ??= new ParticleWebGlRenderer()
        const image = this.meshRenderer.draw(gpuParticles, options.meshGeometry ?? new Map(),
          useMat ? this.materialSprites : new Map(), w, h, camYaw, camPitch, camDistance, options.camTarget ?? [0, 0, 0], this.simTime)
        if (image) { ctx.drawImage(image, 0, 0); gpuMeshes = true }
      } catch (error) {
        console.warn('Particle mesh GPU preview unavailable; using Canvas approximation', error)
        this.dispose(true); this.meshRendererFailed = true
      }
    }
    // APPROXIMATION: Sprite overlays still use Canvas, without shared mesh depth.
    for (const { p, proj } of projected) {
      if (gpuMeshes && gpuParticles.includes(p)) continue
      const sourceSurface = useMat && p.materialPath ? this.materialSprites.get(p.materialPath) : undefined
      // Family canvases contain shader inputs, NOT baked output (procedural families
      // even use an empty carrier). Never present those inputs as a reconstructed material.
      const fallbackSurface = particleMaterialCompatibility(sourceSurface) ? undefined : sourceSurface
      if (p.isMesh) {
        const mesh = options.meshGeometry?.get(p.emitterIdx)
        if (mesh) drawParticleMeshPreview(ctx, mesh, p, project3D, fallbackSurface)
        continue // Missing mesh geometry is unsupported, never a rectangle substitute.
      }
      // Canvas is an approximate fallback, but must not invent lifetime fading or
      // keep zero-opacity particles visible. Material families interpret raw alpha on GPU.
      const alpha = Number.isFinite(p.a) ? Math.min(1, Math.max(0, p.a)) : 0
      if (alpha === 0) continue
      const r = Math.round(p.r * 255)
      const g = Math.round(p.g * 255)
      const b = Math.round(p.b * 255)
      const radius = Math.max(0, p.sizeX * proj.scale)
      const halfHeight = Math.max(0, p.sizeY * proj.scale)

      ctx.save()
      ctx.translate(proj.sx, proj.sy)
      ctx.rotate(p.rotZ)
      // APPROXIMATION: normalized sprite pivot offset, before local rotation.
      ctx.translate((p.pivotOffset?.[0] ?? 0) * radius * 2, -(p.pivotOffset?.[1] ?? 0) * halfHeight * 2)

      if (p.blendMode.includes('Additive')) {
        ctx.globalCompositeOperation = 'lighter'
      } else {
        ctx.globalCompositeOperation = 'source-over'
      }

      const matSprite = fallbackSurface

      if (matSprite) {
        ctx.globalAlpha = alpha
        const totalSub = p.subImagesH * p.subImagesV
        if (totalSub > 1) {
          const idx = particleSubImageFrame(p.subImageIndex, p.subImagesH, p.subImagesV)
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
            -halfHeight,
            radius * 2,
            halfHeight * 2
          )
        } else {
          ctx.drawImage(matSprite, -radius, -halfHeight, radius * 2, halfHeight * 2)
        }

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
  simSeconds = 0.6,
  projectId?: string
): Promise<HTMLCanvasElement | null> {
  if (!info || !Array.isArray(info.Emitters)) return null
  const sprites = await buildParticleMaterialSprites(info, loadImage, externalMaterials, projectId)
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
