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
  textureMappingError?: string | null
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

function parseImageRegisterIndex(imageName: string): number | null {
  const m = /^t(\d+)$/.exec(imageName)
  if (!m) return null
  const idx = Number.parseInt(m[1], 10)
  return Number.isFinite(idx) && idx >= 0 ? idx : null
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

/**
 * Maps SPIR-V combined samplers (t0/s0, t1/s1...) to resolved UE4 material 2D texture expressions.
 * Accounts for Slate ElementTexture occupying base slots when bound.
 */
export function resolveContractSamplerSlots(
  contract: MaterialShaderContract,
  textureOverrides?: Record<string, string>
): { slots: ResolvedSamplerSlot[]; error: string | null } {
  if (contract.schemaVersion !== 1 || contract.renderingReady !== true) {
    return { slots: [], error: contract.limitations?.join('; ') || 'Cooked material binding contract is not rendering-ready.' }
  }
  if (contract.textureMappingError) {
    return { slots: [], error: contract.textureMappingError }
  }

  const samplers = contract.combinedSamplers ? [...contract.combinedSamplers] : []
  if (samplers.length === 0 && contract.reflection?.separate_images?.length) {
    // Fallback if server binary has not yet populated combinedSamplers
    const imgs = contract.reflection.separate_images
    const smps = contract.reflection.separate_samplers || []
    for (let i = 0; i < imgs.length; i++) {
      const imgName = imgs[i].name
      const smpName = smps[i]?.name || imgName.replace(/^t/, 's')
      const uniformName = `SPIRV_Cross_Combined${imgName}${smpName}`
      if (contract.glsl.includes(uniformName)) {
        samplers.push({ uniformName, imageName: imgName, samplerName: smpName })
      }
    }
  }

  const elementTexCount = contract.slateBindings?.elementTexture?.numResources || 0
  const elementTexBase = contract.slateBindings?.elementTexture?.baseIndex || 0
  const exprByIndex = new Map<number, LegacyMaterialTextureExpression>()
  for (const expr of contract.textureExpressions || []) {
    exprByIndex.set(expr.expressionIndex, expr)
  }

  const slots: ResolvedSamplerSlot[] = []
  for (const item of samplers) {
    const regIdx = parseImageRegisterIndex(item.imageName)
    if (regIdx === null) {
      return { slots: [], error: `Unsupported image register name: ${item.imageName}` }
    }

    // Check if this register is occupied by Slate's ElementTexture
    if (elementTexCount > 0 && regIdx >= elementTexBase && regIdx < elementTexBase + elementTexCount) {
      return { slots: [], error: `Slate ElementTexture slot (${item.imageName}) requires external brush texture.` }
    }

    const exprIdx = elementTexCount > 0 && regIdx >= elementTexBase + elementTexCount
      ? regIdx - elementTexCount
      : regIdx

    const expr = exprByIndex.get(exprIdx)
    if (!expr) {
      return {
        slots: [],
        error: `Sampler ${item.uniformName} (${item.imageName}) has no matching Texture2D expression at index ${exprIdx}.`
      }
    }

    let assetPath = expr.assetPath
    if (expr.parameterName && textureOverrides && textureOverrides[expr.parameterName]) {
      assetPath = textureOverrides[expr.parameterName]
    }

    slots.push({
      uniformName: item.uniformName,
      imageName: item.imageName,
      samplerName: item.samplerName,
      expressionIndex: exprIdx,
      assetPath,
      samplerSource: expr.samplerSource,
      parameterName: expr.parameterName
    })
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
  private fallbackWhiteTex: WebGLTexture | null = null

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

    // 1x1 white fallback texture only when an image is still loading
    const white = gl.createTexture()
    if (white) {
      gl.bindTexture(gl.TEXTURE_2D, white)
      gl.texImage2D(
        gl.TEXTURE_2D,
        0,
        gl.RGBA,
        1,
        1,
        0,
        gl.RGBA,
        gl.UNSIGNED_BYTE,
        new Uint8Array([255, 255, 255, 255])
      )
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT)
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT)
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST)
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST)
      this.fallbackWhiteTex = white
    }

    return true
  }

  updateTexture(key: string, image: HTMLImageElement, samplerSource = 0) {
    if (!this.gl || !image.complete || image.naturalWidth === 0) return
    const gl = this.gl

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
    blendMode = 'BLEND_Opaque'
  ): boolean {
    const gl = this.gl
    if (!gl || !this.vao || contract.schemaVersion !== 1 || contract.renderingReady !== true) return false

    const prog = this.ensureProgram(contract.glsl)
    if (!prog) return false

    gl.viewport(0, 0, width, height)
    gl.clearColor(0, 0, 0, 0)
    gl.clear(gl.COLOR_BUFFER_BIT)

    // Configure blend state
    const upperBlend = (blendMode || '').toUpperCase()
    if (upperBlend.includes('ADDITIVE')) {
      gl.enable(gl.BLEND)
      gl.blendFunc(gl.ONE, gl.ONE)
    } else if (upperBlend.includes('MODULATE')) {
      gl.enable(gl.BLEND)
      gl.blendFunc(gl.DST_COLOR, gl.ZERO)
    } else if (upperBlend.includes('TRANSLUCENT') || upperBlend.includes('ALPHA')) {
      gl.enable(gl.BLEND)
      gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA)
    } else {
      gl.disable(gl.BLEND)
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
      if (blockIndex === gl.INVALID_INDEX) continue
      gl.uniformBlockBinding(prog, blockIndex, bindingSlot)

      const data = new Uint8Array(blockSize)
      const view = new DataView(data.buffer)

      if (contract.materialBindings?.materialBufferBound && bindingSlot === matBindingIndex) {
        // Material numeric constant buffer
        data.set(matBytes.subarray(0, Math.min(matBytes.length, blockSize)), 0)
      } else {
        // Populate View buffer (when block_size === 2128 / _m0[133])
        // cb1_0._m0[132].z is View.RealTime / GameTime at byte offset 132 * 16 + 8 = 2120
        if (blockSize >= 2128) {
          const t = Number.isFinite(contract.time) ? contract.time : 0
          view.setFloat32(132 * 16 + 0, t, true)
          view.setFloat32(132 * 16 + 4, t, true)
          view.setFloat32(132 * 16 + 8, t, true)
          view.setFloat32(132 * 16 + 12, 0, true)
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
      const tex = this.textures.get(slot.assetPath) || this.fallbackWhiteTex
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
    if (this.fallbackWhiteTex) {
      gl.deleteTexture(this.fallbackWhiteTex)
      this.fallbackWhiteTex = null
    }
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
