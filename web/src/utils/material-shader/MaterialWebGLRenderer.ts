// WebGL2 Cooked Material Shader Pipeline (UE4.19 Slate UI Material Renderer)
// Supports both cooked GLSL ES 300 shader contracts (/api/projects/{id}/resource-material-shader)
// and legacy fallback rendering when a cooked shader contract is unavailable.

export interface MaterialCombinedSampler {
  uniformName: string
  imageName: string
  samplerName: string
}

export interface LegacyConstantBinding {
  baseIndex: number
  numBytes: number
  bufferIndex: number
}

export interface LegacyResourceBinding {
  baseIndex: number
  numResources: number
}

export interface LegacyMaterialShaderBindings {
  materialBufferIndex: number
  materialBufferBound: boolean
  vectorCount: number
  scalarCount: number
  texture2DCount: number
  textureCubeCount: number
  perFrameScalarCount: number
  perFrameVectorCount: number
}

export interface LegacySlatePixelBindings {
  material: LegacyMaterialShaderBindings
  gammaValues: LegacyConstantBinding
  shaderParams: LegacyConstantBinding
  elementTextureSampler: LegacyResourceBinding
  elementTexture: LegacyResourceBinding
  frameBindings: LegacyConstantBinding[][]
}

export interface LegacyFrameConstantWrite {
  bufferIndex: number
  byteOffset: number
  data: string // base64
}

export interface LegacyMaterialTextureExpression {
  expressionIndex: number
  assetPath: string
  referencedTextureIndex: number
  samplerSource: number
  parameterName: string | null
}

export interface ReflectedUbo {
  type: string
  name: string
  block_size: number
  set: number
  binding: number
}

export interface ReflectedResource {
  type: string
  name: string
  set?: number
  binding?: number
  location?: number
}

export interface ShaderReflection {
  ubos?: ReflectedUbo[]
  inputs?: ReflectedResource[]
  outputs?: ReflectedResource[]
  separate_images?: ReflectedResource[]
  separate_samplers?: ReflectedResource[]
}

export interface MaterialShaderContract {
  schemaVersion: number
  glsl: string
  combinedSamplers?: MaterialCombinedSampler[]
  reflection: ShaderReflection
  materialBindings: LegacyMaterialShaderBindings
  materialConstants: string // base64
  nativeMaterialConstantBytes: number
  frameWrites: LegacyFrameConstantWrite[]
  slateBindings: LegacySlatePixelBindings
  time: number
  previousTime: number
  referencedTextures: string[]
  textureExpressions: LegacyMaterialTextureExpression[]
  samplerSlots?: ResolvedSamplerSlot[]
  textureMappingReady?: boolean
  textureMappingError?: string | null
  blendMode?: string
  previewProfile?: string
  sceneInputs?: { bufferIndex: number; data: string }[]
  collectionInputs?: { bufferIndex: number; data: string; assetPath: string; valueSource: string; defaults?: { scalars: Record<string, number>; vectors: Record<string, [number, number, number, number]> } }[]
  renderingReady: boolean
  limitations?: string[]
}

export interface WebGLTextureBinding {
  samplerName: string
  slot: number
  image?: HTMLImageElement
}

export interface WebGLMaterialUniforms {
  time: number
  resolution: [number, number]
  scalars: Record<string, number>
  vectors: Record<string, { r: number; g: number; b: number; a: number }>
}

const COOKED_VERTEX_SHADER_ES300 = `#version 300 es
precision highp float;
precision highp int;

layout(location = 0) in vec2 a_position;
out highp vec4 v0;
out highp vec4 v1;
out highp vec4 v2;
out highp vec4 v3;
out highp vec4 v4;

void main() {
  vec2 uv = (a_position + 1.0) * 0.5;
  uv.y = 1.0 - uv.y;
  v0 = vec4(0.0, 0.0, 0.0, 1.0);
  v1 = vec4(1.0, 1.0, 1.0, 1.0);
  v2 = vec4(0.0, 0.0, 0.0, 0.0);
  v3 = vec4(uv, 0.0, 0.0);
  v4 = vec4(uv.x, uv.y, 1.0, 1.0);
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`

function decodeBase64Bytes(base64: string): Uint8Array {
  if (!base64) return new Uint8Array(0)
  const bin = atob(base64)
  const out = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) {
    out[i] = bin.charCodeAt(i)
  }
  return out
}

export interface ResolvedSamplerSlot {
  uniformName: string
  imageName: string
  samplerName: string
  expressionIndex: number
  assetPath: string
  samplerSource: number
  parameterName: string | null
}

/** Consume server-proven resource identities; never infer expression order from registers. */
export function resolveContractSamplerSlots(
  contract: MaterialShaderContract,
  textureOverrides?: Record<string, string>
): { slots: ResolvedSamplerSlot[]; error: string | null } {
  if (contract.schemaVersion !== 1 || contract.renderingReady !== true) {
    return { slots: [], error: contract.limitations?.join('; ') || 'Cooked material binding contract is not rendering-ready.' }
  }
  if (contract.textureMappingError || contract.textureMappingReady !== true || !contract.samplerSlots) {
    return { slots: [], error: contract.textureMappingError || 'Explicit material sampler mapping is unavailable.' }
  }
  const slots = contract.samplerSlots.map(slot => ({
    ...slot,
    assetPath: slot.parameterName && textureOverrides?.[slot.parameterName] || slot.assetPath
  }))
  const names = new Set(slots.map(slot => slot.uniformName))
  if (names.size !== slots.length || slots.length !== (contract.combinedSamplers || []).length ||
      (contract.combinedSamplers || []).some(pair => !slots.some(slot =>
        slot.uniformName === pair.uniformName && slot.imageName === pair.imageName && slot.samplerName === pair.samplerName))) {
    return { slots: [], error: 'Incomplete or inconsistent material sampler mapping.' }
  }
  return { slots, error: null }
}

export class MaterialWebGLRenderer {
  private gl: WebGL2RenderingContext | null = null
  private program: WebGLProgram | null = null
  private currentGlsl = ''
  private vao: WebGLVertexArrayObject | null = null
  private posBuffer: WebGLBuffer | null = null
  private uboBuffers: Map<number, WebGLBuffer> = new Map()
  private textures: Map<string, WebGLTexture> = new Map()

  init(canvas: HTMLCanvasElement): boolean {
    if (this.gl && this.gl.canvas === canvas) return true
    const gl = canvas.getContext('webgl2', {
      alpha: true,
      premultipliedAlpha: false,
      antialias: true,
      preserveDrawingBuffer: true
    })
    if (!gl) return false
    this.gl = gl

    const vao = gl.createVertexArray()
    gl.bindVertexArray(vao)
    this.vao = vao

    const posBuf = gl.createBuffer()
    gl.bindBuffer(gl.ARRAY_BUFFER, posBuf)
    gl.bufferData(
      gl.ARRAY_BUFFER,
      new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]),
      gl.STATIC_DRAW
    )
    gl.enableVertexAttribArray(0)
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0)
    gl.bindVertexArray(null)
    this.posBuffer = posBuf

    return true
  }

  updateTexture(key: string, image: HTMLImageElement, samplerSource = 0) {
    if (!this.gl || !image.complete || image.naturalWidth === 0) return
    const gl = this.gl

    key = `${key}#sampler=${samplerSource}`
    let tex = this.textures.get(key)
    if (!tex) {
      tex = gl.createTexture()!
      this.textures.set(key, tex)
    }

    gl.bindTexture(gl.TEXTURE_2D, tex)
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false)
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image)

    // UE4 ESamplerSourceMode: 0=FromTextureAsset (default Repeat in UI/Vfx), 1=Wrap_WorldGroupSettings, 2=Clamp_WorldGroupSettings
    const wrapMode = samplerSource === 2 ? gl.CLAMP_TO_EDGE : gl.REPEAT
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, wrapMode)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, wrapMode)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
  }

  private ensureProgram(fragmentGlsl: string): WebGLProgram | null {
    if (!this.gl) return null
    if (this.program && this.currentGlsl === fragmentGlsl) {
      return this.program
    }

    const gl = this.gl
    if (this.program) {
      gl.deleteProgram(this.program)
      this.program = null
    }

    const vs = this.compileShader(gl.VERTEX_SHADER, COOKED_VERTEX_SHADER_ES300)
    const fs = this.compileShader(gl.FRAGMENT_SHADER, fragmentGlsl)
    if (!vs || !fs) {
      if (vs) gl.deleteShader(vs)
      if (fs) gl.deleteShader(fs)
      return null
    }

    const prog = gl.createProgram()
    if (!prog) return null
    gl.attachShader(prog, vs)
    gl.attachShader(prog, fs)
    gl.linkProgram(prog)
    gl.deleteShader(vs)
    gl.deleteShader(fs)

    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
      console.error('WebGL2 Cooked Program Link Error:', gl.getProgramInfoLog(prog))
      gl.deleteProgram(prog)
      return null
    }

    this.program = prog
    this.currentGlsl = fragmentGlsl
    return prog
  }

  /**
   * Renders a verified cooked material contract on WebGL2 using real UBOs and SPIR-V combined samplers.
   */
  renderContract(
    width: number,
    height: number,
    contract: MaterialShaderContract,
    samplerSlots: ResolvedSamplerSlot[],
    blendMode = 'BLEND_Opaque',
    background: 'checkerboard' | 'dark' | 'light' = 'dark'
  ): boolean {
    const gl = this.gl
    if (!gl || !this.vao || contract.schemaVersion !== 1 || contract.renderingReady !== true) return false

    const expected = resolveContractSamplerSlots(contract)
    if (expected.error || samplerSlots.length !== expected.slots.length ||
        expected.slots.some(slot => !samplerSlots.some(actual => actual.uniformName === slot.uniformName &&
          actual.imageName === slot.imageName && actual.samplerName === slot.samplerName && actual.assetPath === slot.assetPath))) return false
    const prog = this.ensureProgram(contract.glsl)
    if (!prog) return false

    gl.viewport(0, 0, width, height)
    if (contract.blendMode && blendMode !== contract.blendMode) return false
    gl.disable(gl.SCISSOR_TEST)
    gl.disable(gl.BLEND)
    if (blendMode === 'BLEND_Additive') {
      // Additive RGB has alpha zero: compose against an explicit opaque destination,
      // not a transparent canvas (browser premultiplication would erase the emissive RGB).
      const dark = background === 'dark'
      gl.clearColor(dark ? 20 / 255 : 1, dark ? 20 / 255 : 1, dark ? 24 / 255 : 1, 1)
      gl.clear(gl.COLOR_BUFFER_BIT)
      if (background === 'checkerboard') {
        gl.enable(gl.SCISSOR_TEST)
        gl.clearColor(0.8, 0.8, 0.8, 1)
        for (let y = 0; y < height; y += 8) for (let x = 0; x < width; x += 8) {
          if (((x / 8 + y / 8) % 2) === 0) continue
          gl.scissor(x, y, Math.min(8, width - x), Math.min(8, height - y))
          gl.clear(gl.COLOR_BUFFER_BIT)
        }
        gl.disable(gl.SCISSOR_TEST)
      }
      gl.enable(gl.BLEND)
      gl.blendEquation(gl.FUNC_ADD)
      gl.blendFuncSeparate(gl.ONE, gl.ONE, gl.ZERO, gl.ONE)
    } else {
      // Preserve straight RGBA for normal canvas composition.
      gl.clearColor(0, 0, 0, 0)
      gl.clear(gl.COLOR_BUFFER_BIT)
    }

    gl.useProgram(prog)
    gl.bindVertexArray(this.vao)

    // 1. Bind and populate reflected Uniform Buffer Objects (UBOs)
    const ubos = contract.reflection?.ubos || []
    const matBytes = decodeBase64Bytes(contract.materialConstants)
    const matBindingIndex = contract.materialBindings?.materialBufferIndex

    for (const ubo of ubos) {
      const bindingSlot = ubo.binding
      const blockSize = ubo.block_size
      if (blockSize <= 0) continue

      const blockIndex = gl.getUniformBlockIndex(prog, ubo.name)
      if (blockIndex === gl.INVALID_INDEX) { gl.bindVertexArray(null); return false }
      gl.uniformBlockBinding(prog, blockIndex, bindingSlot)

      const data = new Uint8Array(blockSize)
      const view = new DataView(data.buffer)

      if (contract.materialBindings?.materialBufferBound && bindingSlot === matBindingIndex) {
        // Material numeric constant buffer
        if (matBytes.length !== blockSize) { gl.bindVertexArray(null); return false }
        data.set(matBytes, 0)
      } else {
        const scene = (contract.sceneInputs || []).filter(input => input.bufferIndex === bindingSlot)
        if (scene.length > 1) { gl.bindVertexArray(null); return false }
        if (scene.length === 1) {
          const bytes = decodeBase64Bytes(scene[0]!.data)
          if (bytes.length !== blockSize) { gl.bindVertexArray(null); return false }
          data.set(bytes)
        }
        // Populate Slate GammaValues if bound to this buffer
        const gamma = contract.slateBindings?.gammaValues
        if (gamma && gamma.bufferIndex === bindingSlot && gamma.numBytes >= 8) {
          if (gamma.baseIndex + 8 <= blockSize) {
            // gammaValues.x = 1.0 (invDisplayGamma), gammaValues.y = 0.0 (enable built-in linear-to-sRGB conversion for browser display)
            view.setFloat32(gamma.baseIndex, 1.0, true)
            view.setFloat32(gamma.baseIndex + 4, 0.0, true)
          }
        }

        // Apply per-frame expression writes targeting this buffer
        for (const fw of contract.frameWrites || []) {
          if (fw.bufferIndex === bindingSlot) {
            const fwBytes = decodeBase64Bytes(fw.data)
            if (fw.byteOffset >= 0 && fw.byteOffset + fwBytes.length <= blockSize) {
              data.set(fwBytes, fw.byteOffset)
            }
          }
        }
      }

      let glBuf = this.uboBuffers.get(bindingSlot)
      if (!glBuf) {
        glBuf = gl.createBuffer()!
        this.uboBuffers.set(bindingSlot, glBuf)
      }
      gl.bindBuffer(gl.UNIFORM_BUFFER, glBuf)
      gl.bufferData(gl.UNIFORM_BUFFER, data, gl.DYNAMIC_DRAW)
      gl.bindBufferBase(gl.UNIFORM_BUFFER, bindingSlot, glBuf)
    }

    // 2. Bind combined texture samplers
    for (let i = 0; i < samplerSlots.length; i++) {
      const slot = samplerSlots[i]
      const loc = gl.getUniformLocation(prog, slot.uniformName)
      if (!loc) continue

      gl.activeTexture(gl.TEXTURE0 + i)
      const tex = this.textures.get(`${slot.assetPath}#sampler=${slot.samplerSource}`)
      if (!tex) { gl.bindVertexArray(null); return false }
      gl.bindTexture(gl.TEXTURE_2D, tex)
      gl.uniform1i(loc, i)
    }

    gl.drawArrays(gl.TRIANGLES, 0, 6)
    gl.bindVertexArray(null)
    return true
  }

  destroy() {
    if (!this.gl) return
    const gl = this.gl
    for (const tex of this.textures.values()) {
      gl.deleteTexture(tex)
    }
    this.textures.clear()
    for (const buf of this.uboBuffers.values()) {
      gl.deleteBuffer(buf)
    }
    this.uboBuffers.clear()
    if (this.posBuffer) gl.deleteBuffer(this.posBuffer)
    if (this.vao) gl.deleteVertexArray(this.vao)
    if (this.program) gl.deleteProgram(this.program)
    this.gl = null
    this.program = null
    this.posBuffer = null
    this.vao = null
  }

  private compileShader(type: number, src: string): WebGLShader | null {
    if (!this.gl) return null
    const shader = this.gl.createShader(type)
    if (!shader) return null
    this.gl.shaderSource(shader, src)
    this.gl.compileShader(shader)
    if (!this.gl.getShaderParameter(shader, this.gl.COMPILE_STATUS)) {
      console.error('WebGL2 Shader Compile Error:', this.gl.getShaderInfoLog(shader))
      this.gl.deleteShader(shader)
      return null
    }
    return shader
  }
}
