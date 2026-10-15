import { ParticleSimulationEngine, buildParticleMaterialSprites, type ParticleSystemData } from './ParticleRuntime'
import { loadParticleMesh, meshOriginRadius, type ParticleMeshGeometry } from './ParticleMeshGeometry'
import { fitParticleCamera } from './ParticleCamera'

/** APPROXIMATION: persistent CPU simulation, baked materials and neutral mesh preview.
 * Large seeks replay at most 10 seconds; not deterministic UE simulation or Blueprint execution.
 */
export class ParticlePreviewSession {
  readonly canvas = document.createElement('canvas')
  engine: ParticleSimulationEngine
  private meshes = new Map<number, ParticleMeshGeometry>()
  private radii = new Map<number, number>()
  private abort = new AbortController()
  private disposed = false
  private time = 0
  private camera: ReturnType<typeof fitParticleCamera> = null
  constructor(private info: ParticleSystemData) { this.engine = new ParticleSimulationEngine(info); this.canvas.dataset.particlePreview = 'dynamic' }
  async prepare(loadImage: (path: string) => Promise<HTMLImageElement | null>, materials?: Record<string, any>, projectId?: string) {
    const sprites = buildParticleMaterialSprites(this.info, loadImage, materials, projectId)
    const requests = new Map<string, Promise<ParticleMeshGeometry>>()
    await Promise.all(this.info.Emitters.map(async (emitter, index) => {
      const type = emitter.LODLevels?.[0]?.TypeDataModule
      const path = type?.References?.Mesh?.ContentFile
      if (!projectId || type?.Class !== 'ParticleModuleTypeDataMesh' || !path) return
      let request = requests.get(path)
      if (!request) { request = loadParticleMesh(projectId, path, this.abort.signal); requests.set(path, request) }
      try {
        const mesh = await request
        if (!this.disposed) { this.meshes.set(index, mesh); this.radii.set(index, meshOriginRadius(mesh)) }
      } catch { /* APPROXIMATION: missing mesh is omitted, never replaced by a rectangle. */ }
    }))
    const result = await sprites
    if (!this.disposed) this.engine.setMaterialSprites(result)
  }
  draw(time: number, width: number, height: number) {
    if (this.disposed) return null
    time = Number.isFinite(time) ? Math.max(0, time) : 0
    if (time < this.time || time - this.time > 10) {
      const sprites = this.engine.materialSprites
      this.engine = new ParticleSimulationEngine(this.info)
      this.engine.setMaterialSprites(sprites)
      this.time = Math.max(0, time - 10)
      this.camera = null
    }
    while (this.time + 1e-6 < time) {
      const dt = Math.min(1 / 30, time - this.time)
      this.engine.step(dt); this.time += dt
    }
    this.canvas.width = Math.max(32, Math.min(1024, Math.round(width)))
    this.canvas.height = Math.max(32, Math.min(1024, Math.round(height)))
    this.camera ??= fitParticleCamera(this.engine.particles, p => this.radii.get(p.emitterIdx))
    const ctx = this.canvas.getContext('2d')!
    this.engine.renderToCanvas(ctx, this.canvas.width, this.canvas.height, {
      transparentBg: true, showGrid: false, useMaterialSprites: true,
      meshGeometry: this.meshes, camTarget: this.camera?.target, camDistance: this.camera?.distance ?? 1400
    })
    return this.canvas
  }
  dispose() { this.disposed = true; this.abort.abort(); this.meshes.clear(); this.radii.clear(); this.engine.materialSprites.clear(); this.canvas.width = this.canvas.height = 1 }
}
