import { evaluateUniform, type UniformProgram } from './MaterialUniformRuntime'

/** Material-only persistent diagnostic UBO. Does not enable native particle drawing. */
export class ParticleMaterialBuffer {
  private buffer: WebGLBuffer | null
  private readonly bytes: Uint8Array
  constructor(private readonly gl: WebGL2RenderingContext, base64: string, private readonly program: UniformProgram) {
    if (program.version !== 1) throw new Error('Unsupported uniform program')
    this.bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0))
    if (!this.bytes.length || this.bytes.length % 16 !== 0) throw new Error('Invalid Material buffer size')
    const slots = new Set(program.writes.map(w => w.bufferIndex))
    if (slots.size > 1) throw new Error('Frame/scene writes require separate buffers')
    for (const w of program.writes) {
      if (![4,16].includes(w.width) || !Number.isInteger(w.byteOffset) || w.byteOffset < 0 || w.byteOffset % 4 || w.byteOffset + w.width > this.bytes.length)
        throw new Error('Material write exceeds buffer')
    }
    this.buffer = gl.createBuffer()
    if (!this.buffer) throw new Error('Material buffer allocation failed')
    const prior = gl.getParameter(gl.UNIFORM_BUFFER_BINDING)
    try {
      gl.bindBuffer(gl.UNIFORM_BUFFER, this.buffer)
      gl.bufferData(gl.UNIFORM_BUFFER, this.bytes, gl.DYNAMIC_DRAW)
    } catch (error) { this.dispose(); throw error }
    finally { gl.bindBuffer(gl.UNIFORM_BUFFER, prior) }
  }
  update(time: number, previousTime: number, scalars: Record<string,number> = {}, vectors: Record<string,number[]> = {}) {
    if (!this.buffer) throw new Error('Material buffer disposed')
    const next = this.bytes.slice()
    const view = new DataView(next.buffer)
    for (const w of this.program.writes) {
      const values = evaluateUniform(w.expression, {time:w.previous?previousTime:time,
        scalars:{...this.program.scalars,...scalars},vectors:{...this.program.vectors,...vectors}})
      for (let i=0;i<w.width/4;i++) view.setFloat32(w.byteOffset+i*4,values[i]!,true)
    }
    let first = 0, last = next.length
    while(first<last && next[first]===this.bytes[first]) first++
    while(last>first && next[last-1]===this.bytes[last-1]) last--
    if(first===last) return false
    first -= first % 4
    last = Math.ceil(last/4)*4
    const gl=this.gl, prior=gl.getParameter(gl.UNIFORM_BUFFER_BINDING)
    try { gl.bindBuffer(gl.UNIFORM_BUFFER,this.buffer); gl.bufferSubData(gl.UNIFORM_BUFFER,first,next.subarray(first,last)) }
    finally { gl.bindBuffer(gl.UNIFORM_BUFFER,prior) }
    this.bytes.set(next)
    return true
  }
  /** Caller explicitly selects the WebGL binding point; native cb index is not one. */
  bind(bindingPoint: number) {
    if (!this.buffer) throw new Error('Material buffer disposed')
    this.gl.bindBufferBase(this.gl.UNIFORM_BUFFER,bindingPoint,this.buffer)
  }
  dispose() { if(this.buffer) this.gl.deleteBuffer(this.buffer); this.buffer=null }
}
