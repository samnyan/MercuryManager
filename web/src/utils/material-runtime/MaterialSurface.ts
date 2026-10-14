import { MaterialInstance } from './MaterialInstance'
import { MaterialWebGLRenderer, resolveContractSamplerSlots, type MaterialShaderContract } from '../material-shader/MaterialWebGLRenderer'

/** Persistent offscreen execution surface. Never performs network requests during draw. */
export class MaterialSurface {
  readonly canvas = document.createElement('canvas')
  readonly instance: MaterialInstance
  private renderer = new MaterialWebGLRenderer()
  private slots: ReturnType<typeof resolveContractSamplerSlots>['slots']
  private disposed = false
  private lastTime: number | undefined
  constructor(readonly program: MaterialShaderContract, width: number, height: number, private additiveDestination: 'opaque-stage' | undefined = undefined) {
    const resolved = resolveContractSamplerSlots(program)
    if (resolved.error) throw new Error(resolved.error)
    if (program.blendMode === 'BLEND_Additive' && !additiveDestination) throw new Error('Additive requires destination-aware stage composition')
    this.slots = resolved.slots
    this.instance = new MaterialInstance(program)
    this.resize(width, height)
    if (!this.renderer.init(this.canvas)) throw new Error('WebGL2 unavailable')
  }
  resize(width: number, height: number) {
    if (this.disposed) throw new Error('Material surface disposed')
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0)
      throw new Error('Invalid material surface dimensions')
    const scale = Math.min(1, 1024 / Math.max(width, height))
    const w = Math.max(1, Math.round(width * scale))
    const h = Math.max(1, Math.round(height * scale))
    if (this.canvas.width !== w) this.canvas.width = w
    if (this.canvas.height !== h) this.canvas.height = h
  }
  async prepare(loadImage: (path: string) => Promise<HTMLImageElement | null>) {
    const images = await Promise.all(this.slots.map(s => s.generatedSource ? null : loadImage(s.assetPath)))
    if (this.disposed) throw new Error('Material surface disposed')
    images.forEach((image, i) => {
      if (this.renderer.updateGeneratedTexture(this.slots[i]!)) return
      if (!image) throw new Error(`Missing material texture: ${this.slots[i]!.assetPath}`)
      const slot = this.slots[i]!
      this.renderer.updateTexture(slot.assetPath, image, slot.samplerSource)
    })
  }
  draw(time: number, previousTime = this.lastTime ?? time): HTMLCanvasElement {
    if (this.disposed) throw new Error('Material surface disposed')
    this.instance.update({time, previousTime})
    if (!this.renderer.renderContract(this.canvas.width, this.canvas.height,
      {...this.program, time, previousTime, runtimeWrites:this.instance.writes()}, this.slots,
      this.program.blendMode || 'BLEND_Opaque', this.additiveDestination ? 'emissive' : 'dark')) throw new Error('Material draw failed')
    this.lastTime = time
    return this.canvas
  }
  dispose() { this.disposed = true; this.renderer.destroy() }
}
