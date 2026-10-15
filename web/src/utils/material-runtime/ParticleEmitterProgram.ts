import type { ParticleProgramEvidence } from './ParticleProgramDiagnostics'

/** Select by emitter semantics, never by the first material permutation. */
export function particleVertexFactory(typeDataClass: string | null | undefined): string | null {
  switch (typeDataClass) {
    case undefined: case null: case '': return 'FParticleSpriteVertexFactory'
    case 'ParticleModuleTypeDataGpu': return 'FGPUSpriteVertexFactory'
    case 'ParticleModuleTypeDataMesh': return 'FMeshParticleVertexFactory'
    default: return null
  }
}

export function selectEmitterProgram(programs: ParticleProgramEvidence[], typeDataClass: string | null | undefined): ParticleProgramEvidence | null {
  const factory = particleVertexFactory(typeDataClass)
  const matches = programs.filter(p => p.vertexFactory === factory)
  return matches.length === 1 ? matches[0]! : null
}
