// APPROXIMATION: bounded BGN material-color slice from cooked Mesh PS, not native
// lighting/geometry. Preserves actual material math; preview beat/bar are synthetic.
// Returned canvas uses the existing affine mesh mapping; vertex displacement omitted.
const updates = new WeakMap<HTMLCanvasElement, (time: number) => void>()
export function updateBgnSurface(canvas: HTMLCanvasElement, time: number) { updates.get(canvas)?.(time) }

export async function createBgnSurface(project: string, path: string,
  loadImage: (path: string) => Promise<HTMLImageElement | null>): Promise<HTMLCanvasElement | null> {
  if (!path.endsWith('/vMX_3_BGN_OUS_m_BGN001.uasset')) return null
  const response = await fetch(`/api/projects/${encodeURIComponent(project)}/resource-material-shader?includeParticleStages=true&path=${encodeURIComponent(path)}`)
  if (!response.ok) return null
  const payload = await response.json()
  const stage = payload.data?.particleStages?.find((p: any) => p.vertexFactory === 'FMeshParticleVertexFactory')?.stages?.find((s: any) => s.stage === 'fragment')
  const source: string = stage?.translation?.glsl ?? ''
  // Exact anchors delimit material color/emissive arithmetic before lighting. Unknown
  // permutations are rejected rather than guessing register meanings from parameters.
  const start = '    highp vec3 _334 = texture(SPIRV_Cross_Combinedt2s1,'
  const end = '    bvec3 _4118 = isnan(r5.xyz);'
  const a = source.indexOf(start), b = source.indexOf(end)
  if (a < 0 || b <= a || source.indexOf(start, a + 1) >= 0) return null
  const slice = source.slice(a, b)
  const refs = [...slice.matchAll(/cb(\d+)_0/g)].map(m => m[1])
  if (refs.some(x => !['1', '5', '6'].includes(x))) return null
  const input = stage.materialNumericInput
  if (!input?.uploadValidated || input.bufferIndex !== 6) return null
  const slots = stage.materialTextureInput?.samplerSlots ?? []
  const names = ['SPIRV_Cross_Combinedt2s1', 'SPIRV_Cross_Combinedt3s2']
  const images = await Promise.all(names.map(async name => {
    const slot = slots.find((s: any) => s.uniformName === name)
    return slot ? loadImage(slot.assetPath) : null
  }))
  if (images.some(i => !i)) return null
  const gpu = document.createElement('canvas'); gpu.width = gpu.height = 256
  const gl = gpu.getContext('webgl2', { alpha: false, preserveDrawingBuffer: true })
  if (!gl) return null
  const resources: WebGLTexture[] = [], buffers: WebGLBuffer[] = [], shaders: WebGLShader[] = []
  const program = gl.createProgram()!
  const cleanup = () => { resources.forEach(x => gl.deleteTexture(x)); buffers.forEach(x => gl.deleteBuffer(x)); shaders.forEach(x => gl.deleteShader(x)); gl.deleteProgram(program); gl.getExtension('WEBGL_lose_context')?.loseContext() }
  try {
    const vs = `#version 300 es
out vec4 mercury_varying_2;
void main(){vec2 p=vec2(float((gl_VertexID<<1)&2),float(gl_VertexID&2)); mercury_varying_2=vec4(p,0,0);gl_Position=vec4(p*2.0-1.0,0,1);}`
    const fs = `#version 300 es
precision highp float;
precision highp int;
in vec4 mercury_varying_2;
out vec4 outColor;
uniform sampler2D SPIRV_Cross_Combinedt2s1;
uniform sampler2D SPIRV_Cross_Combinedt3s2;
layout(std140) uniform ViewBlock { vec4 _m0[134]; } cb1_0;
layout(std140) uniform BeatBlock { vec4 _m0[1]; } cb5_0;
layout(std140) uniform MaterialBlock { vec4 _m0[9]; } cb6_0;
void main(){vec4 r0=vec4(0),r1=vec4(0),r3=vec4(0),r4=vec4(0),r5=vec4(0);
${slice}
// APPROXIMATION: display material contribution without UE scene lighting.
vec3 color=max(r6.xyz,vec3(0));outColor=vec4(pow(color/(vec3(1)+color),vec3(1.0/2.2)),1);}`
    for (const [type, code] of [[gl.VERTEX_SHADER, vs], [gl.FRAGMENT_SHADER, fs]] as const) {
      const sh = gl.createShader(type)!; shaders.push(sh); gl.shaderSource(sh, code); gl.compileShader(sh)
      if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(sh) ?? 'BGN compile failed')
      gl.attachShader(program, sh)
    }
    gl.linkProgram(program)
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program) ?? 'BGN link failed')
    gl.useProgram(program)
    const bind = (name: string, binding: number, data: ArrayBufferView) => {
      const buffer = gl.createBuffer()!; buffers.push(buffer); gl.bindBuffer(gl.UNIFORM_BUFFER, buffer)
      gl.bufferData(gl.UNIFORM_BUFFER, data, gl.DYNAMIC_DRAW)
      gl.uniformBlockBinding(program, gl.getUniformBlockIndex(program, name), binding); gl.bindBufferBase(gl.UNIFORM_BUFFER, binding, buffer)
      return buffer
    }
    bind('ViewBlock', 0, new Float32Array(134 * 4)) // only material texture mip bias [133].x is used
    const beat = bind('BeatBlock', 1, new Float32Array(4))
    const bytes = Uint8Array.from(atob(input.data), c => c.charCodeAt(0))
    if (bytes.length !== 144) throw new Error('Unexpected BGN material layout')
    bind('MaterialBlock', 2, bytes)
    images.forEach((image, i) => {
      const t = gl.createTexture()!; resources.push(t); gl.activeTexture(gl.TEXTURE0 + i); gl.bindTexture(gl.TEXTURE_2D, t)
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image!)
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT)
      gl.uniform1i(gl.getUniformLocation(program, names[i]!), i)
    })
    // Pre-render a bounded cycle and release GL ownership immediately. Playback uses
    // cached GPU results, not a permanently allocated context for each emitter.
    const frames: HTMLCanvasElement[] = []
    for (let i = 0; i < 24; i++) {
      gl.bindBuffer(gl.UNIFORM_BUFFER, beat)
      gl.bufferSubData(gl.UNIFORM_BUFFER, 0, new Float32Array([(i % 6) / 6, (i + 0.5) / 24, 0, 0]))
      gl.viewport(0, 0, 256, 256); gl.drawArrays(gl.TRIANGLES, 0, 3)
      const frame = document.createElement('canvas'); frame.width = frame.height = 256
      frame.getContext('2d')!.drawImage(gpu, 0, 0); frames.push(frame)
    }
    const result = document.createElement('canvas'); result.width = result.height = 256
    result.dataset.previewProfile = 'approximate-bgn-material-cycle'
    updates.set(result, time => result.getContext('2d')!.drawImage(frames[Math.floor(Math.max(0, time) * 12) % frames.length]!, 0, 0))
    updateBgnSurface(result, 0)
    return result
  } finally { cleanup() }
}
