import type { UniformProgram } from './MaterialUniformRuntime'

/** Additive backend evidence DTOs. Identity and resource discovery never imply draw readiness. */
export interface ParticleConstantBinding { baseIndex: number; numBytes: number; bufferIndex: number }
export interface ParticleResourceBinding { baseIndex: number; numResources: number }
export interface ParticleMaterialBinding {
  materialBufferIndex: number
  materialBufferBound: boolean
  vectorCount: number
  scalarCount: number
  texture2DCount: number
  textureCubeCount: number
  perFrameScalarCount: number
  perFrameVectorCount: number
}
export interface ParticleProgramEvidence {
  vertexFactory: string
  identityVerified: boolean
  vertexOffset: number | null
  fragmentOffset: number | null
  fogBindings?: { stage: 'vertex' | 'fragment'; bindings: {
    constants: ParticleConstantBinding[]
    inscatteringCubemap: ParticleResourceBinding
    inscatteringSampler: ParticleResourceBinding
    integratedLightScattering: ParticleResourceBinding
    integratedLightScatteringSampler: ParticleResourceBinding
  }; materialBindings?: ParticleMaterialBinding | null; frameBindings?: ParticleConstantBinding[][] }[]
  spriteBindings: {
    numCutoutVerticesPerFrame: ParticleConstantBinding
    cutoutGeometry: ParticleResourceBinding
  } | null
  limitations: string[]
}
export interface ParticleSceneResourceRequirement {
  uniformName: string
  samplerType: string
  nativeResource: string
  imageRegister: number
  samplerRegister: number
  sourceProvided: boolean
  nativeSamplerVerified: boolean
}
export interface ParticleStageEvidence {
  stage: 'vertex' | 'fragment'
  offset: number
  translationSucceeded: boolean
  translation: { hash: string; stage: string; glsl: string; reflection: string; diagnostics: string[]; combinedSamplers: { uniformName: string; imageName: string; samplerName: string; samplerType: 'sampler2D' | 'sampler3D' | 'samplerCube' }[] } | null
  uniformProgram?: UniformProgram | null
  sceneResources?: ParticleSceneResourceRequirement[]
  /** Base64 bytes, never a float array. Validation does not imply draw readiness. */
  materialNumericInput?: { bound: boolean; bufferIndex: number; uploadValidated: boolean; nativeByteCount: number; data: string | null; limitations: string[] } | null
  materialFrameInput?: { writesValidated: boolean; writes: { bufferIndex: number; byteOffset: number; data: string }[]; limitations: string[] } | null
  materialTextureInput?: { samplerSlots: { uniformName: string; imageName: string; samplerName: string; expressionIndex: number; assetPath: string; samplerSource: number; parameterName: string | null }[]; limitations: string[] } | null
  limitations: string[]
}
export interface ParticleProgramStages {
  vertexFactory: string
  renderingReady: false
  stages: ParticleStageEvidence[]
  stageInterface?: { compatible: boolean; varyings: { location: number; type: string; vertexName: string; fragmentName: string; linkName: string }[]; limitations: string[] } | null
}
export interface NonSlateParticleDiagnostics {
  schemaVersion: 1
  renderingReady: false
  previewProfile: 'unsupported-non-slate-v1'
  particlePrograms: ParticleProgramEvidence[]
  particleStages?: ParticleProgramStages[] | null
  particleTextureError?: string | null
  limitations: string[]
}
/** Particle document uses Newtonsoft/property casing, unlike shader endpoint camelCase. */
export interface ParticleCutoutConfiguration {
  State: 'serialized-null-texture' | 'serialized-texture' | 'unverified'
  TextureIndex: number | null
  DefaultBufferEligible: boolean
  OpaqueBytes: number
  Limitation: string
  CookedGeometry: { VertexCount: number; Coordinates: number[] } | null
  HasCookedGeometryResource: boolean | null
}
export function hasNonSlateParticleDiagnostics(value: unknown): boolean {
  if (!value || typeof value !== 'object') return false
  const v = value as Record<string, unknown>
  return v.schemaVersion === 1 && v.renderingReady === false &&
    v.previewProfile === 'unsupported-non-slate-v1' && Array.isArray(v.particlePrograms) &&
    v.particlePrograms.every(p => p && typeof p === 'object' &&
      typeof p.vertexFactory === 'string' && typeof p.identityVerified === 'boolean' &&
      Array.isArray(p.limitations) && p.limitations.every((s: unknown) => typeof s === 'string')) &&
    Array.isArray(v.limitations) && v.limitations.every(s => typeof s === 'string')
}
