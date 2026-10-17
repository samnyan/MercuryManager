import { particleSubImageFrame } from './ParticleSubUV'
import type { ParticleMeshGeometry } from './ParticleMeshGeometry'
import type { SimParticle } from './ParticleRuntime'
import { transformMeshVertex } from './ParticleMeshWireframe'
import { particleMaterialCompatibility } from './ParticleMaterialCompatibility'

/** APPROXIMATION: perspective/depth-correct geometry with existing UV surfaces.
 * Not native material execution, WPO, scene lighting or section-material parity.
 * Dedicated context keeps the UMG compositor's GL state untouched.
 */
export class ParticleWebGlRenderer {
  readonly canvas = document.createElement('canvas')
  private gl: WebGL2RenderingContext
  private program: WebGLProgram
  private vertices: WebGLBuffer
  private indices: WebGLBuffer
  private vao: WebGLVertexArrayObject
  private repeatSampler: WebGLSampler
  private clampSampler: WebGLSampler
  private untextured: WebGLTexture
  private textures = new Map<HTMLCanvasElement, WebGLTexture>()
  constructor() {
    const gl = this.canvas.getContext('webgl2', { alpha: true, premultipliedAlpha: true, preserveDrawingBuffer: true })
    if (!gl) throw new Error('WebGL2 unavailable')
    this.gl = gl
    const program = gl.createProgram()!
    const shaders: WebGLShader[] = []
    try {
      for (const [type, code] of [[gl.VERTEX_SHADER, `#version 300 es
layout(location=0) in vec4 position;
layout(location=1) in vec2 uv;
out vec2 texCoord;
void main(){gl_Position=position;texCoord=uv;}`], [gl.FRAGMENT_SHADER, `#version 300 es
precision highp float;
in vec2 texCoord;
uniform sampler2D surface;
uniform sampler2D maskSurface;
uniform vec4 tiling;
uniform float strength;
uniform bool textured;
uniform bool masked;
uniform bool opaqueCoverage;
uniform float maskClip;
uniform vec4 tint;
uniform int family;
uniform vec3 palette[3];
uniform float threshold;
uniform float tap[9];
uniform float hold[25];
uniform float previewTime;
uniform float miss[15];
uniform float particleRandom;
uniform float rank[9];
uniform vec4 dynamicValues;
out vec4 color;
void main(){
 vec2 uv=texCoord;
 if(family==4)uv.y+=tint.a;
 vec4 sampled=textured?texture(surface,uv):vec4(1.0);
 vec4 c=sampled*tint;
 if(family==2)c=vec4(tint.rgb,sampled.r*tint.a);
 if(family==3){int index=tint.a>=0.5?2:1;if(tint.a>=threshold-0.00001)index=0;c=vec4(palette[index],sampled.r);}
 if(family==4)c=vec4(tint.rgb,sampled.r);
 if(family==5)c=vec4(texture(surface,uv*tiling.xy).rgb*tint.rgb*strength,texture(maskSurface,clamp(uv*tiling.zw,0.0,1.0)).g*tint.a);
 if(family==6)c=vec4(sampled.rgb*strength,sampled.a*texture(maskSurface,uv+vec2(0.0,tint.r)).r*tint.a);
 if(family==7){
  // APPROXIMATION: preview NoteHeight is 0.5; preserve authored glow and gap controls.
  vec3 ramp=texture(surface,clamp(uv+vec2(0.0,tap[0]+tap[1]),0.0,1.0)).rgb;
  float bottom=clamp((1.0-uv.y-tap[0])*(1.0+2.0*tap[3])-tap[3],0.0,1.0);
  float beam=uv.y*mix(1.0,bottom,tap[4]);
  float glow=(beam>0.0?pow(beam*0.5,tap[5])+pow(beam,tap[6]):0.0)*tint.a;
  float gap=ceil(smoothstep(tap[0]-0.1,tap[0],sin(6.28318530718*(uv.y*0.5+tap[7]))));
  c=vec4(tint.rgb*mix(vec3(1.0),ramp,tap[2]),glow*(1.0-tap[8]*gap));
 }
 if(family==8){
  // APPROXIMATION: authored procedural glow, independent of particle tint.
  float rise=clamp((uv.y+(hold[0]+hold[1])*0.5-hold[2])/(hold[3]-hold[2]),0.0,1.0);
  float fall=clamp((uv.y+(hold[4]+hold[5])*0.5-hold[6])/(hold[7]-hold[6]),0.0,1.0);
  float core=rise*rise*(3.0-2.0*rise)*(1.0-fall*fall*(3.0-2.0*fall))*hold[8];
  float v=uv.y+hold[10];
  float edge=clamp((2.0*v*(1.0-v)-hold[11])/(hold[12]-hold[11]),0.0,1.0);
  float angle=hold[15]*6.28318530718;
  float side=step(0.5,(uv.x+hold[14]-0.5)*cos(angle)-(uv.y-0.5)*sin(angle)+0.5);
  float pulse=hold[18]+(hold[17]-hold[18])*sin(previewTime*hold[16]*6.28318530718);
  c.rgb=mix(vec3(hold[22],hold[23],hold[24]),vec3(hold[19],hold[20],hold[21]),clamp(core*hold[9],0.0,1.0));
  c.a=(core+edge*edge*(3.0-2.0*edge)*hold[13])*side*pulse;
 }
 if(family==9){
  // APPROXIMATION: ring/sector material math; native random sequence is not reproduced.
  vec4 packed=texture(surface,clamp(uv,0.0,1.0));
  vec3 choice=particleRandom>0.50001?vec3(miss[3],miss[4],miss[5]):vec3(miss[6],miss[7],miss[8]);
  c.rgb=choice*(packed.r*vec3(miss[9],miss[10],miss[11])+packed.g*vec3(miss[12],miss[13],miss[14]));
  vec2 delta=uv-0.5;
  float azimuth=dot(delta,delta)>0.0?fract(atan(delta.y,delta.x)/6.28318530718):0.0;
  vec2 sectors=ceil(fract((azimuth+clamp(vec2(particleRandom),vec2(0.4,0.55),vec2(0.45,0.7)))/3.0)*3.0)-2.0;
  float radius=length(delta);
  float outer=max(0.0,1.0-radius/miss[0])*miss[1];
  float inner=tint.r>0.0?max(0.0,1.0-radius/tint.r)*miss[2]:0.0;
  float ring=exp(-inner*inner)-exp(-outer*outer);
  float cut=1.0;
  float span=1.0-2.0*tint.g;
  if(abs(span)>0.000001){
   vec2 inset=(uv-tint.g)/span;
   vec2 edge=clamp((1.0-2.0*abs(inset-0.5))*100000.0,0.0,1.0);
   cut-=texture(maskSurface,clamp(inset,0.0,1.0)).r*edge.x*edge.y;
  }
  c.a=(sectors.x< -0.00001&&sectors.y>= -0.00001?1.0:0.0)*ring*tint.a*packed.b*cut;
 }
 if(family==10){
  // APPROXIMATION: CPU dynamic parameters drive the authored square-ring wave.
  vec2 centered=uv-0.5;
  float turn=dynamicValues.w*6.28318530718;
  vec2 colorUv=vec2(centered.x*cos(turn)-centered.y*sin(turn),centered.x*sin(turn)+centered.y*cos(turn))+0.5;
  c.rgb=tint.rgb*rank[0]*mix(vec3(1),texture(surface,clamp(colorUv,0.0,1.0)).rgb,rank[1]);
  turn=rank[2]*6.28318530718;
  vec2 grid=(vec2(centered.x*cos(turn)-centered.y*sin(turn),centered.x*sin(turn)+centered.y*cos(turn))+0.5)*rank[3];
  float d=length((floor(grid)+0.5)/rank[3]-0.5);
  float outer=dynamicValues.x>0.0?max(0.0,1.0-d/dynamicValues.x)*rank[4]:0.0;
  float inner=dynamicValues.z>0.0?max(0.0,1.0-d/dynamicValues.z)*rank[4]:0.0;
  float wave=clamp((exp(-inner*inner)*(1.0-exp(-outer*outer))-rank[5])/(rank[6]-rank[5]),0.0,1.0);
  float inset=mix(rank[7],rank[8],wave*wave*(3.0-2.0*wave));
  float span=1.0-2.0*inset;
  c.a=0.0;
  if(abs(span)>0.000001){
   vec2 cell=(fract(grid)-inset)/span;
   vec2 edge=clamp((1.0-2.0*abs(cell-0.5))*100000.0,0.0,1.0);
   c.a=texture(maskSurface,clamp(cell,0.0,1.0)).r*edge.x*edge.y*tint.a;
  }
 }
 c.a=clamp(c.a,0.0,1.0);
 if(masked&&c.a<maskClip)discard;
 // Opaque/masked coverage is independent of material opacity after discard.
 float coverage=opaqueCoverage?1.0:c.a;
 color=vec4(max(c.rgb,vec3(0.0))*coverage,coverage);
}`]] as const) {
        const shader = gl.createShader(type)!; shaders.push(shader)
        gl.shaderSource(shader, code); gl.compileShader(shader)
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader) || 'Mesh shader compile failed')
        gl.attachShader(program, shader)
      }
      gl.linkProgram(program)
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program) || 'Mesh shader link failed')
    } catch (error) { gl.deleteProgram(program); gl.getExtension('WEBGL_lose_context')?.loseContext(); throw error }
    finally { shaders.forEach(s => gl.deleteShader(s)) }
    // Geometry-reference mode only: complete active samplers without borrowing a
    // previous material. This texture is never substituted for a missing asset slot.
    this.untextured = gl.createTexture()!
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, this.untextured)
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([255,255,255,255]))
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST)
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST)
    const sampler = (wrap: number) => {
      const value = gl.createSampler()!
      gl.samplerParameteri(value, gl.TEXTURE_MIN_FILTER, gl.LINEAR)
      gl.samplerParameteri(value, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
      gl.samplerParameteri(value, gl.TEXTURE_WRAP_S, wrap)
      gl.samplerParameteri(value, gl.TEXTURE_WRAP_T, wrap)
      return value
    }
    this.repeatSampler = sampler(gl.REPEAT)
    this.clampSampler = sampler(gl.CLAMP_TO_EDGE)
    this.program = program; this.vertices = gl.createBuffer()!; this.indices = gl.createBuffer()!; this.vao = gl.createVertexArray()!
    gl.bindVertexArray(this.vao); gl.bindBuffer(gl.ARRAY_BUFFER, this.vertices)
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 4, gl.FLOAT, false, 24, 0)
    gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 24, 16)
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.indices)
  }
  draw(particles: SimParticle[], meshes: ReadonlyMap<number, ParticleMeshGeometry>, surfaces: Map<string, HTMLCanvasElement>, width: number, height: number,
    yaw: number, pitch: number, distance: number, target: number[], time = 0) {
    const gl = this.gl
    if (gl.isContextLost()) return null
    if (this.canvas.width !== width) this.canvas.width = width
    if (this.canvas.height !== height) this.canvas.height = height
    gl.viewport(0, 0, width, height); gl.depthMask(true); gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT)
    gl.useProgram(this.program); gl.bindVertexArray(this.vao); gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LEQUAL); gl.disable(gl.CULL_FACE)
    gl.uniform1f(gl.getUniformLocation(this.program, 'previewTime'), time)
    gl.uniform1i(gl.getUniformLocation(this.program, 'surface'), 0)
    const cy = Math.cos(yaw), sy = Math.sin(yaw), cp = Math.cos(pitch), sp = Math.sin(pitch)
    const camera = (v: number[]) => {
      const x = v[0]! - target[0]!, y = v[1]! - target[1]!, z = v[2]! - target[2]!
      const ry = x * sy + y * cy
      return [x * cy - y * sy, z * cp - ry * sp, ry * cp + z * sp + distance]
    }
    const transparent = (p: SimParticle) => /Translucent|Additive|AlphaComposite/.test(p.blendMode)
    const ordered = particles.filter(p => !p.isMesh || meshes.has(p.emitterIdx)).slice().sort((a, b) =>
      Number(transparent(a)) - Number(transparent(b)) || camera([b.x,b.y,b.z])[2]! - camera([a.x,a.y,a.z])[2]!)
    const uploaded = new Set<HTMLCanvasElement>()
    for (const p of ordered) {
      const surface = p.materialPath ? surfaces.get(p.materialPath) : undefined
      const family = particleMaterialCompatibility(surface)
      if (family?.mode === 10 && (!p.dynamicValues || p.dynamicValues.length !== 4 || !p.dynamicValues.every(Number.isFinite))) continue
      // APPROXIMATION: camera-facing sprites; velocity alignment remains unsupported.
      const mesh = p.isMesh ? meshes.get(p.emitterIdx)! : {
        positions: [-1,0,-1, 1,0,-1, 1,0,1, -1,0,1], texCoords: [[0,1,1,1,1,0,0,0]],
        indices: [0,1,2,0,2,3], sections: [{firstIndex:0,numTriangles:2,materialIndex:0}]
      }
      const uv = mesh.texCoords[0]
      const data = new Float32Array(mesh.positions.length / 3 * 6)
      const near = 50, far = Math.max(1000000, distance * 100)
      for (let i = 0; i < mesh.positions.length / 3; i++) {
        const v = p.isMesh ? camera(transformMeshVertex(mesh.positions[i*3]!, mesh.positions[i*3+1]!, mesh.positions[i*3+2]!, p)) : camera([p.x,p.y,p.z])
        if (!p.isMesh) {
          const x=(mesh.positions[i*3]!+2*(p.pivotOffset?.[0] ?? 0))*p.sizeX, y=(mesh.positions[i*3+2]!+2*(p.pivotOffset?.[1] ?? 0))*p.sizeY
          v[0]! += x*Math.cos(p.rotZ)-y*Math.sin(p.rotZ)
          v[1]! += x*Math.sin(p.rotZ)+y*Math.cos(p.rotZ)
        }
        const columns = Math.max(1,p.subImagesH || 1), rows = Math.max(1,p.subImagesV || 1)
        const frame = particleSubImageFrame(p.subImageIndex, columns, rows)
        const u = ((uv?.[i*2] ?? 0)+(p.isMesh?0:frame%columns))/(p.isMesh?1:columns)
        const t = ((uv?.[i*2+1] ?? 0)+(p.isMesh?0:Math.floor(frame/columns)))/(p.isMesh?1:rows)
        data.set([v[0]! * Math.min(width,height)*3/width, v[1]! * Math.min(width,height)*3/height,
          (far+near)/(far-near)*v[2]! - 2*far*near/(far-near), v[2]!, u, t], i*6)
      }
      gl.bindBuffer(gl.ARRAY_BUFFER, this.vertices); gl.bufferData(gl.ARRAY_BUFFER, data, gl.DYNAMIC_DRAW)
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.indices); gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint32Array(mesh.indices), gl.DYNAMIC_DRAW)
      gl.activeTexture(gl.TEXTURE0)
      if (surface) {
        let texture = this.textures.get(surface)
        const needsUpload = !texture || !family
        if (!texture) { texture = gl.createTexture()!; this.textures.set(surface, texture) }
        gl.bindTexture(gl.TEXTURE_2D, texture)
        // Family canvases are immutable; other surfaces may animate in place.
        if (needsUpload && !uploaded.has(surface)) {
          gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false)
          gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, surface)
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
        }
        // A cached image may serve multiple material slots with different policies.
        // Apply addressing at binding time, not only on the first image upload.
        const wrap = (family?.mode === 7 || family?.mode === 9 || family?.mode === 10) ? gl.CLAMP_TO_EDGE : gl.REPEAT
        gl.bindSampler(0, wrap === gl.CLAMP_TO_EDGE ? this.clampSampler : this.repeatSampler)
        uploaded.add(surface)
      } else {
        gl.bindTexture(gl.TEXTURE_2D, this.untextured)
        gl.bindSampler(0, this.repeatSampler)
      }
      gl.uniform1i(gl.getUniformLocation(this.program, 'textured'), surface ? 1 : 0)
      gl.uniform1f(gl.getUniformLocation(this.program, 'maskClip'), p.opacityMaskClipValue ?? 1 / 3)
      gl.uniform1i(gl.getUniformLocation(this.program, 'opaqueCoverage'), p.blendMode === 'BLEND_Opaque' || p.blendMode === 'BLEND_Masked' ? 1 : 0)
      gl.uniform1i(gl.getUniformLocation(this.program, 'masked'), p.blendMode.includes('Masked') ? 1 : 0)
      gl.uniform1fv(gl.getUniformLocation(this.program, 'rank[0]'), family?.rank ?? new Float32Array(9))
      gl.uniform4fv(gl.getUniformLocation(this.program, 'dynamicValues'), p.dynamicValues ?? [0,0,0,0])
      // Share unit zero for single-texture families; never leave an active sampler incomplete.
      gl.uniform1i(gl.getUniformLocation(this.program, 'maskSurface'), family?.secondary ? 1 : 0)
      if (family?.secondary) {
        const mask = family.secondary
        gl.activeTexture(gl.TEXTURE1)
        let texture = this.textures.get(mask)
        const needsUpload = !texture
        if (!texture) { texture = gl.createTexture()!; this.textures.set(mask, texture) }
        gl.bindTexture(gl.TEXTURE_2D, texture)
        if (needsUpload) {
          gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,mask)
          gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR)
          gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR)
        }
        const wrap = (family.mode === 5 || family.mode === 9 || family.mode === 10) ? gl.CLAMP_TO_EDGE : gl.REPEAT
        gl.bindSampler(1, wrap === gl.CLAMP_TO_EDGE ? this.clampSampler : this.repeatSampler)
        uploaded.add(mask)
      }
      gl.uniform4fv(gl.getUniformLocation(this.program, 'tiling'), family?.tiling ?? [1,1,1,1])
      gl.uniform1f(gl.getUniformLocation(this.program, 'strength'), family?.strength ?? 1)
      gl.uniform1fv(gl.getUniformLocation(this.program, 'miss[0]'), family?.miss ?? new Float32Array(15))
      gl.uniform1f(gl.getUniformLocation(this.program, 'particleRandom'), p.shaderRandom ?? 0.5)
      gl.uniform1fv(gl.getUniformLocation(this.program, 'hold[0]'), family?.hold ?? new Float32Array(25))
      gl.uniform1fv(gl.getUniformLocation(this.program, 'tap[0]'), family?.tap ?? new Float32Array(9))
      gl.uniform1i(gl.getUniformLocation(this.program, 'family'), family?.mode ?? 0)
      gl.uniform1f(gl.getUniformLocation(this.program, 'threshold'), family?.threshold ?? 0.5)
      gl.uniform3fv(gl.getUniformLocation(this.program, 'palette[0]'), new Float32Array(family?.mode === 3 ? family.colors.flat() : [1,1,1,1,1,1,1,1,1]))
      // Family shaders consume particle color themselves; baked previews already contain it.
      const baked = surface && !family
      gl.uniform4f(gl.getUniformLocation(this.program, 'tint'), baked ? 1 : p.r, baked ? 1 : p.g, baked ? 1 : p.b, p.a)
      gl.depthMask(!transparent(p))
      if (transparent(p)) { gl.enable(gl.BLEND); gl.blendFunc(gl.ONE, p.blendMode.includes('Additive') ? gl.ONE : gl.ONE_MINUS_SRC_ALPHA) }
      else gl.disable(gl.BLEND)
      for (const section of mesh.sections) gl.drawElements(gl.TRIANGLES, section.numTriangles*3, gl.UNSIGNED_INT, section.firstIndex*4)
    }
    for (const [surface, texture] of this.textures) if (!uploaded.has(surface)) { gl.deleteTexture(texture); this.textures.delete(surface) }
    return this.canvas
  }
  dispose() {
    const gl = this.gl
    gl.deleteSampler(this.repeatSampler); gl.deleteSampler(this.clampSampler)
    gl.deleteTexture(this.untextured)
    this.textures.forEach(t => gl.deleteTexture(t)); this.textures.clear()
    gl.deleteBuffer(this.vertices); gl.deleteBuffer(this.indices); gl.deleteVertexArray(this.vao); gl.deleteProgram(this.program)
    gl.getExtension('WEBGL_lose_context')?.loseContext()
  }
}
