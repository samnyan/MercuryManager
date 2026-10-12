// 通用 WebGL 材质着色管线 (Material WebGL Pipeline)
// 可供 MaterialShaderCanvas 与后续 UMG / Blueprint 预览器复用

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

const VERTEX_SHADER_SRC = `
attribute vec2 a_position;
varying vec2 v_uv;

void main() {
  v_uv = (a_position + 1.0) * 0.5;
  v_uv.y = 1.0 - v_uv.y; // 保持与 UE / WebGL UV 坐标系一致 (原点左上角)
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`

// UE Slate UI 材质的片元模拟着色器 (支持多贴图采样、CustomRotator、Panner、Mask Glitch 扰动与多图层 Lerp 混色)
const FRAGMENT_SHADER_SRC = `
precision mediump float;

varying vec2 v_uv;

uniform float u_time;
uniform vec2 u_resolution;

// 常用标量与向量参数
uniform float u_scalar_Ran_1;
uniform float u_scalar_Ran_2;
uniform float u_scalar_Ran_3;
uniform float u_scalar_Duration_1;
uniform float u_scalar_Duration_2;
uniform float u_scalar_Duration_3;
uniform float u_scalar_Span_1;
uniform float u_scalar_Span_2;
uniform float u_scalar_Span_3;
uniform float u_scalar_offset_1;
uniform float u_scalar_offset_2;
uniform float u_scalar_offset_3;
uniform float u_scalar_Speed_1;
uniform float u_scalar_Speed_2;
uniform float u_scalar_Speed_3;
uniform float u_scalar_Rotator_0;
uniform float u_scalar_Rotator_3;
uniform float u_scalar_widht_1;
uniform float u_scalar_widht_2;
uniform float u_scalar_widht_3;
uniform float u_scalar_move_1;

// 向量参数 (调色盘)
uniform vec4 u_vec_color_1;
uniform vec4 u_vec_color_2;
uniform vec4 u_vec_color_4;
uniform vec4 u_vec_R_Color;
uniform vec4 u_vec_R_Color_1;

// 纹理采样器
uniform sampler2D u_tex_primary;    // 立绘或主图 (uT_Chara000_EV000)
uniform sampler2D u_tex_secondary;  // 标题 Logo 或装饰图 (uT_TTlogo07)
uniform sampler2D u_tex_mask;       // 故障遮罩 (uT_GlitchMaskV)
uniform sampler2D u_tex_obj;        // 装饰物件 (uT_CM_obj05)

uniform int u_has_tex_primary;
uniform int u_has_tex_secondary;
uniform int u_has_tex_mask;
uniform int u_has_tex_obj;
uniform int u_blend_mode; // 0=Opaque, 1=Masked, 2=Translucent, 3=Additive, 4=Modulate

// UE CustomRotator: 围绕 center 顺时针旋转 angle (0~1 为 0~2PI)
vec2 customRotator(vec2 uv, vec2 center, float angle) {
  float rad = angle * 6.28318530718;
  float c = cos(rad);
  float s = sin(rad);
  vec2 p = uv - center;
  return vec2(p.x * c - p.y * s, p.x * s + p.y * c) + center;
}

// 模拟 UE uMF_TexCropRotate 中心缩放展开
vec2 cropRotateUV(vec2 uv, float t, float speed) {
  vec2 p = uv - vec2(0.5, 0.5);
  float scale = 1.0 + mod(t * speed, 0.35);
  return p / scale + vec2(0.5, 0.5);
}

void main() {
  vec2 uv = v_uv;

  // 1. 底层背景色彩渲染 (由 color_4 与 R_Color / R_Color_1 渐变构成)
  vec4 bgColor = mix(u_vec_color_4, u_vec_R_Color, uv.y);
  bgColor = mix(bgColor, u_vec_R_Color_1, uv.x * 0.5);

  // 2. 几何物件层 (uT_CM_obj05: 经 uMF_TexCropRotate 旋转展开)
  vec4 objLayer = vec4(0.0);
  if (u_has_tex_obj > 0) {
    vec2 objUV = cropRotateUV(uv, u_time, max(0.05, u_scalar_move_1 * 10.0));
    if (objUV.x >= 0.0 && objUV.x <= 1.0 && objUV.y >= 0.0 && objUV.y <= 1.0) {
      vec4 o = texture2D(u_tex_obj, objUV);
      objLayer = o * u_vec_color_2;
    }
  }

  // 3. 故障遮罩采样 (uMF_HorizontalGlitch / uMF_VerticalGlitch)
  vec2 glitchOffset = vec2(0.0);
  if (u_has_tex_mask > 0) {
    float cycle = max(0.5, u_scalar_Duration_1 + u_scalar_Span_1);
    float inGlitch = step(mod(u_time, cycle), u_scalar_Duration_1);
    
    // 纵向滚动的遮罩采样
    vec2 maskUV_H = vec2(0.5, uv.y + u_time * (u_scalar_Speed_1 + 0.05));
    vec4 maskSampleH = texture2D(u_tex_mask, maskUV_H);
    
    if (maskSampleH.r > 0.15) {
      glitchOffset.x = (maskSampleH.r - 0.5) * 2.0 * max(0.01, u_scalar_offset_1 * 5.0) * inGlitch;
    }

    vec2 maskUV_V = vec2(uv.x + u_time * (u_scalar_Speed_2 + 0.02), 0.5);
    vec4 maskSampleV = texture2D(u_tex_mask, maskUV_V);
    if (maskSampleV.r > 0.3) {
      glitchOffset.y = (maskSampleV.r - 0.5) * max(0.005, u_scalar_offset_2 * 3.0) * inGlitch;
    }
  }

  // 4. 主立绘层采样 (接受 Glitch 偏移与 Rotator_0 / Rotator_3 旋转)
  vec4 primaryLayer = vec4(0.0);
  if (u_has_tex_primary > 0) {
    vec2 charaUV = uv + glitchOffset;
    if (u_scalar_Rotator_3 != 0.0) {
      charaUV = customRotator(charaUV, vec2(0.5, 0.5), u_scalar_Rotator_3);
    }
    if (charaUV.x >= 0.0 && charaUV.x <= 1.0 && charaUV.y >= 0.0 && charaUV.y <= 1.0) {
      primaryLayer = texture2D(u_tex_primary, charaUV);
    }
  }

  // 5. 顶层标题 Logo 层 (uT_TTlogo07: 带有双向流光 Panner 与 color_1 调色)
  vec4 secLayer = vec4(0.0);
  if (u_has_tex_secondary > 0) {
    vec2 pannerUV = uv + vec2(u_time * u_scalar_Speed_1 * 0.5, u_time * u_scalar_Speed_2 * 0.2);
    vec4 s1 = texture2D(u_tex_secondary, fract(pannerUV));
    vec4 s2 = texture2D(u_tex_secondary, uv);
    secLayer = mix(s2, s1 * u_vec_color_1, 0.45);
  }

  // 复合混合 (Composite Blend)
  vec4 finalColor = bgColor;
  if (objLayer.a > 0.0) {
    finalColor = mix(finalColor, objLayer, objLayer.a * 0.6);
  }
  if (primaryLayer.a > 0.0) {
    finalColor = mix(finalColor, primaryLayer, primaryLayer.a);
  }
  if (secLayer.a > 0.0) {
    finalColor = mix(finalColor, secLayer, secLayer.a);
  }

  // 混合模式输出过滤
  if (u_blend_mode == 3) {
    // Additive
    gl_FragColor = finalColor;
  } else if (u_blend_mode == 4) {
    // Modulate
    gl_FragColor = finalColor * bgColor;
  } else {
    // Opaque / Translucent
    gl_FragColor = finalColor;
  }
}
`

export class MaterialWebGLRenderer {
  private gl: WebGLRenderingContext | null = null
  private program: WebGLProgram | null = null
  private posBuffer: WebGLBuffer | null = null
  private textures: Map<string, WebGLTexture> = new Map()

  init(canvas: HTMLCanvasElement): boolean {
    const gl = canvas.getContext('webgl', { alpha: true, premultipliedAlpha: false, antialias: true })
    if (!gl) return false
    this.gl = gl

    const vs = this.compileShader(gl.VERTEX_SHADER, VERTEX_SHADER_SRC)
    const fs = this.compileShader(gl.FRAGMENT_SHADER, FRAGMENT_SHADER_SRC)
    if (!vs || !fs) return false

    const prog = gl.createProgram()
    if (!prog) return false
    gl.attachShader(prog, vs)
    gl.attachShader(prog, fs)
    gl.linkProgram(prog)

    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
      console.error('WebGL Program Link Error:', gl.getProgramInfoLog(prog))
      return false
    }

    this.program = prog

    // 全屏矩形缓冲 (-1 到 1)
    const posBuf = gl.createBuffer()
    gl.bindBuffer(gl.ARRAY_BUFFER, posBuf)
    gl.bufferData(
      gl.ARRAY_BUFFER,
      new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]),
      gl.STATIC_DRAW
    )
    this.posBuffer = posBuf

    return true
  }

  updateTexture(name: string, image: HTMLImageElement) {
    if (!this.gl || !image.complete || image.naturalWidth === 0) return
    const gl = this.gl

    let tex = this.textures.get(name)
    if (!tex) {
      tex = gl.createTexture()!
      this.textures.set(name, tex)
    }

    gl.bindTexture(gl.TEXTURE_2D, tex)
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false)
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
  }

  render(
    width: number,
    height: number,
    uniforms: WebGLMaterialUniforms,
    texBindings: { primary?: string; secondary?: string; mask?: string; obj?: string },
    blendMode = 'BLEND_Opaque'
  ) {
    const gl = this.gl
    const prog = this.program
    if (!gl || !prog || !this.posBuffer) return

    gl.viewport(0, 0, width, height)
    gl.clearColor(0, 0, 0, 0)
    gl.clear(gl.COLOR_BUFFER_BIT)

    gl.useProgram(prog)

    // 顶点位置设置
    const posLoc = gl.getAttribLocation(prog, 'a_position')
    gl.bindBuffer(gl.ARRAY_BUFFER, this.posBuffer)
    gl.enableVertexAttribArray(posLoc)
    gl.vertexAttribPointer(posLoc, 2, gl.FLOAT, false, 0, 0)

    // 全局基础 Uniforms
    this.setUniform1f('u_time', uniforms.time)
    this.setUniform2f('u_resolution', width, height)

    // 绑定标量参数
    for (const [k, v] of Object.entries(uniforms.scalars)) {
      this.setUniform1f(`u_scalar_${k}`, v)
    }

    // 绑定向量参数
    for (const [k, v] of Object.entries(uniforms.vectors)) {
      this.setUniform4f(`u_vec_${k}`, v.r, v.g, v.b, v.a)
    }

    // 绑定纹理槽位
    const bindTexSlot = (
      uniformTex: string,
      uniformHas: string,
      slot: number,
      key?: string
    ) => {
      const tex = key ? this.textures.get(key) : undefined
      gl.activeTexture(gl.TEXTURE0 + slot)
      if (tex) {
        gl.bindTexture(gl.TEXTURE_2D, tex)
        this.setUniform1i(uniformTex, slot)
        this.setUniform1i(uniformHas, 1)
      } else {
        gl.bindTexture(gl.TEXTURE_2D, null)
        this.setUniform1i(uniformHas, 0)
      }
    }

    bindTexSlot('u_tex_primary', 'u_has_tex_primary', 0, texBindings.primary)
    bindTexSlot('u_tex_secondary', 'u_has_tex_secondary', 1, texBindings.secondary)
    bindTexSlot('u_tex_mask', 'u_has_tex_mask', 2, texBindings.mask)
    bindTexSlot('u_tex_obj', 'u_has_tex_obj', 3, texBindings.obj)

    // 混合模式配置
    let bMode = 0
    if (blendMode.includes('Additive')) bMode = 3
    else if (blendMode.includes('Modulate')) bMode = 4
    else if (blendMode.includes('Translucent')) bMode = 2
    else if (blendMode.includes('Masked')) bMode = 1
    this.setUniform1i('u_blend_mode', bMode)

    gl.drawArrays(gl.TRIANGLES, 0, 6)
  }

  destroy() {
    if (!this.gl) return
    const gl = this.gl
    for (const tex of this.textures.values()) {
      gl.deleteTexture(tex)
    }
    this.textures.clear()
    if (this.posBuffer) gl.deleteBuffer(this.posBuffer)
    if (this.program) gl.deleteProgram(this.program)
    this.gl = null
    this.program = null
    this.posBuffer = null
  }

  private setUniform1f(name: string, val: number) {
    if (!this.gl || !this.program) return
    const loc = this.gl.getUniformLocation(this.program, name)
    if (loc) this.gl.uniform1f(loc, val)
  }

  private setUniform2f(name: string, x: number, y: number) {
    if (!this.gl || !this.program) return
    const loc = this.gl.getUniformLocation(this.program, name)
    if (loc) this.gl.uniform2f(loc, x, y)
  }

  private setUniform4f(name: string, x: number, y: number, z: number, w: number) {
    if (!this.gl || !this.program) return
    const loc = this.gl.getUniformLocation(this.program, name)
    if (loc) this.gl.uniform4f(loc, x, y, z, w)
  }

  private setUniform1i(name: string, val: number) {
    if (!this.gl || !this.program) return
    const loc = this.gl.getUniformLocation(this.program, name)
    if (loc) this.gl.uniform1i(loc, val)
  }

  private compileShader(type: number, src: string): WebGLShader | null {
    if (!this.gl) return null
    const shader = this.gl.createShader(type)
    if (!shader) return null
    this.gl.shaderSource(shader, src)
    this.gl.compileShader(shader)
    if (!this.gl.getShaderParameter(shader, this.gl.COMPILE_STATUS)) {
      console.error('WebGL Shader Compile Error:', this.gl.getShaderInfoLog(shader))
      this.gl.deleteShader(shader)
      return null
    }
    return shader
  }
}
