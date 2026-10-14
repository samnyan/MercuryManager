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
  private framebuffer?: WebGLFramebuffer
  texture?: WebGLTexture
  private targetSize = ''
  constructor(readonly program: MaterialShaderContract, width: number, height: number, private additiveDestination: 'opaque-stage' | undefined = undefined, private sharedContext?: WebGL2RenderingContext) {
    const resolved = resolveContractSamplerSlots(program)
    if (resolved.error) throw new Error(resolved.error)
    if (program.blendMode === 'BLEND_Additive' && !additiveDestination) throw new Error('Additive requires destination-aware stage composition')
    this.slots = resolved.slots
    this.instance = new MaterialInstance(program)
    this.resize(width, height)
    if (!this.renderer.init(this.canvas, sharedContext)) throw new Error('WebGL2 unavailable')
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
    const gl = this.sharedContext
    if (gl) {
      this.texture ??= gl.createTexture()!
      this.framebuffer ??= gl.createFramebuffer()!
      gl.bindTexture(gl.TEXTURE_2D, this.texture)
      const size = `${this.canvas.width}x${this.canvas.height}`
      if (this.targetSize !== size) {
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, this.canvas.width, this.canvas.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null)
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR)
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
        this.targetSize = size
      }
      gl.bindFramebuffer(gl.FRAMEBUFFER, this.framebuffer)
      gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, this.texture, 0)
      gl.disable(gl.STENCIL_TEST)
      gl.colorMask(true, true, true, true)
    }
    try {
      this.instance.update({time, previousTime})
      if (!this.renderer.renderContract(this.canvas.width, this.canvas.height,
        {...this.program, time, previousTime, runtimeWrites:this.instance.writes()}, this.slots,
        this.program.blendMode || 'BLEND_Opaque', this.additiveDestination ? 'emissive' : 'dark')) throw new Error('Material draw failed')
    } finally {
      if (gl) gl.bindFramebuffer(gl.FRAMEBUFFER, null)
    }
    this.lastTime = time
    return this.canvas
  }
  dispose() {
    this.disposed = true
    if (this.texture) this.sharedContext?.deleteTexture(this.texture)
    if (this.framebuffer) this.sharedContext?.deleteFramebuffer(this.framebuffer)
    this.renderer.destroy()
  }
}
