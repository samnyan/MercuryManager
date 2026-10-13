import { evaluateUniform, type UniformProgram } from './MaterialUniformRuntime'
import type { MaterialShaderContract } from '../material-shader/MaterialWebGLRenderer'

/** Per-consumer state: shared immutable program, independent parameters and clock. */
export class MaterialInstance {
  private scalars: Record<string, number> = {}
  private vectors: Record<string, number[]> = {}
  private time = 0
  private previousTime = 0
  constructor(readonly program: MaterialShaderContract) {
    if (program.uniformProgram?.version !== 1) throw new Error('Browser uniform program unavailable')
  }
  setParameters(scalars: Record<string, number>, vectors: Record<string, number[]>) {
    this.scalars = { ...scalars }
    this.vectors = Object.fromEntries(Object.entries(vectors).map(([k,v])=>[k,v.slice()]))
  }
  update(frame: { time: number; previousTime: number }) {
    this.time = frame.time
    this.previousTime = frame.previousTime
  }
  collections: Record<string, { scalars?: Record<string, number>; vectors?: Record<string, number[]> }> = {}
  setCollections(values: typeof this.collections) { this.collections = values }
  collectionWrites() {
    const writes: { bufferIndex: number; byteOffset: number; values: number[] }[] = []
    for (const c of this.program.collectionInputs || []) {
      if (!c.defaults) continue
      const override = this.collections[c.assetPath]
      if (!override) continue
      const names = Object.keys(c.defaults.scalars)
      for (const [name,value] of Object.entries(override.scalars || {})) {
        const i = names.indexOf(name)
        if (i < 0 || !Number.isFinite(value)) throw new Error('Invalid collection scalar')
        if (i * 4 + 4 <= atob(c.data).length) writes.push({bufferIndex:c.bufferIndex,byteOffset:i*4,values:[value]})
      }
      const vectors = Object.keys(c.defaults.vectors)
      const offset = Math.ceil(names.length / 4) * 16
      for (const [name,value] of Object.entries(override.vectors || {})) {
        const i = vectors.indexOf(name)
        if(i<0 || value.length!==4 || value.some(v=>!Number.isFinite(v))) throw new Error('Invalid collection vector')
        if(offset+i*16+16<=atob(c.data).length) writes.push({bufferIndex:c.bufferIndex,byteOffset:offset+i*16,values:value})
      }
    }
    return writes
  }
  writes(): { bufferIndex: number; byteOffset: number; values: number[] }[] {
    const p = this.program.uniformProgram as UniformProgram
    const scalars = { ...p.scalars, ...this.scalars }
    const vectors = { ...p.vectors, ...this.vectors }
    return p.writes.map(w => ({ bufferIndex: w.bufferIndex, byteOffset: w.byteOffset,
      values: evaluateUniform(w.expression, {time:w.previous?this.previousTime:this.time,scalars,vectors}).slice(0,w.width/4) })).concat(this.collectionWrites())
  }
}
