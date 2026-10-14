/** Explicit cutout transport; this does not infer emitter count or enable real particle rendering. */
export class ParticleCutoutTexture {
  readonly texture: WebGLTexture
  readonly width: number
  private disposed = false

  constructor(private readonly gl: WebGL2RenderingContext, coordinates: Float32Array) {
    if (!coordinates.length || coordinates.length % 2 ||
        coordinates.some(value => !Number.isFinite(value))) throw new Error('Invalid cutout coordinates')
    this.width = coordinates.length / 2
    if (this.width > gl.getParameter(gl.MAX_TEXTURE_SIZE)) throw new Error('Cutout exceeds one-row texture limit')
    const texture = gl.createTexture()
    if (!texture) throw new Error('Unable to allocate cutout texture')
    this.texture = texture
    const binding = gl.getParameter(gl.TEXTURE_BINDING_2D)
    const unpackBuffer = gl.getParameter(gl.PIXEL_UNPACK_BUFFER_BINDING)
    const names = [gl.UNPACK_ALIGNMENT, gl.UNPACK_ROW_LENGTH, gl.UNPACK_SKIP_PIXELS, gl.UNPACK_SKIP_ROWS,
      gl.UNPACK_FLIP_Y_WEBGL, gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, gl.UNPACK_COLORSPACE_CONVERSION_WEBGL]
    const values = names.map(name => gl.getParameter(name))
    try {
      gl.bindBuffer(gl.PIXEL_UNPACK_BUFFER, null)
      gl.bindTexture(gl.TEXTURE_2D, texture)
      gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1)
      gl.pixelStorei(gl.UNPACK_ROW_LENGTH, 0)
      gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, 0)
      gl.pixelStorei(gl.UNPACK_SKIP_ROWS, 0)
      gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 0)
      gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0)
      gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE)
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST)
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST)
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RG32F, this.width, 1, 0, gl.RG, gl.FLOAT, coordinates)
    } catch (error) {
      gl.deleteTexture(texture)
      this.disposed = true
      throw error
    } finally {
      names.forEach((name, index) => gl.pixelStorei(name, values[index]))
      gl.bindBuffer(gl.PIXEL_UNPACK_BUFFER, unpackBuffer)
      gl.bindTexture(gl.TEXTURE_2D, binding)
    }
  }

  /** Leaves the chosen unit active, as required by the draw owner's binding lifecycle. */
  bind(unit: number): void {
    if (this.disposed) throw new Error('Cutout texture disposed')
    if (!Number.isInteger(unit) || unit < 0 || unit >= this.gl.getParameter(this.gl.MAX_COMBINED_TEXTURE_IMAGE_UNITS))
      throw new Error('Invalid cutout texture unit')
    this.gl.activeTexture(this.gl.TEXTURE0 + unit)
    this.gl.bindTexture(this.gl.TEXTURE_2D, this.texture)
  }

  dispose(): void {
    if (this.disposed) return
    this.gl.deleteTexture(this.texture)
    this.disposed = true
  }
}
