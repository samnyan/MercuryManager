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
  writes(): { bufferIndex: number; byteOffset: number; values: number[] }[] {
    const p = this.program.uniformProgram as UniformProgram
    const scalars = { ...p.scalars, ...this.scalars }
    const vectors = { ...p.vectors, ...this.vectors }
    return p.writes.map(w => ({ bufferIndex: w.bufferIndex, byteOffset: w.byteOffset,
      values: evaluateUniform(w.expression, {time:w.previous?this.previousTime:this.time,scalars,vectors}).slice(0,w.width/4) }))
  }
}
