import type { Matrix } from './transforms'
export type GpuItem = {
  source?: TexImageSource; texture?: WebGLTexture; flipY?: boolean
  width: number; height: number; matrix: Matrix; opacity: number; additive: boolean
  clip: {x:number;y:number}[]
}
/** Ordered shared-context compositor. Material FBOs stay on the GPU. */
export class GpuStage {
  readonly gl: WebGL2RenderingContext
  private program: WebGLProgram
  private buffer: WebGLBuffer
  private vao: WebGLVertexArrayObject
  private textures = new Map<TexImageSource, WebGLTexture>()
  constructor(readonly canvas: HTMLCanvasElement) {
    const gl = canvas.getContext('webgl2', {alpha:false, stencil:true, antialias:true})
    if (!gl) throw new Error('WebGL2 stage unavailable')
    this.gl = gl
    const shader = (type:number, source:string) => {
      const s=gl.createShader(type)!; gl.shaderSource(s,source);gl.compileShader(s)
      if(!gl.getShaderParameter(s,gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s) || 'Shader compilation failed')
      return s
    }
    const vs=shader(gl.VERTEX_SHADER,`#version 300 es
layout(location=0) in vec2 position; layout(location=1) in vec2 uv;
uniform vec2 resolution; out vec2 texcoord;
void main(){gl_Position=vec4(position.x/resolution.x*2.-1.,1.-position.y/resolution.y*2.,0.,1.);texcoord=uv;}`)
    const fs=shader(gl.FRAGMENT_SHADER,`#version 300 es
precision highp float; in vec2 texcoord; uniform sampler2D image; uniform float opacity; out vec4 color;
void main(){vec4 c=texture(image,texcoord);color=vec4(c.rgb,c.a*opacity);}`)
    this.program=gl.createProgram()!;gl.attachShader(this.program,vs);gl.attachShader(this.program,fs);gl.linkProgram(this.program)
    gl.deleteShader(vs);gl.deleteShader(fs)
    if(!gl.getProgramParameter(this.program,gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(this.program)||'Stage link failed')
    this.vao=gl.createVertexArray()!;this.buffer=gl.createBuffer()!
    gl.bindVertexArray(this.vao);gl.bindBuffer(gl.ARRAY_BUFFER,this.buffer)
    gl.enableVertexAttribArray(0);gl.vertexAttribPointer(0,2,gl.FLOAT,false,16,0)
    gl.enableVertexAttribArray(1);gl.vertexAttribPointer(1,2,gl.FLOAT,false,16,8)
    gl.bindVertexArray(null)
  }
  render(items:GpuItem[], viewport:Matrix, width:number,height:number) {
    const gl=this.gl
    if(this.canvas.width!==width)this.canvas.width=width
    if(this.canvas.height!==height)this.canvas.height=height
    gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.viewport(0,0,width,height)
    gl.disable(gl.SCISSOR_TEST);gl.disable(gl.DEPTH_TEST);gl.disable(gl.CULL_FACE)
    gl.colorMask(true,true,true,true);gl.stencilMask(255)
    gl.clearColor(20/255,20/255,24/255,1);gl.clear(gl.COLOR_BUFFER_BIT|gl.STENCIL_BUFFER_BIT)
    gl.useProgram(this.program);gl.bindVertexArray(this.vao);gl.bindBuffer(gl.ARRAY_BUFFER,this.buffer)
    gl.uniform2f(gl.getUniformLocation(this.program,'resolution'),width,height)
    gl.uniform1i(gl.getUniformLocation(this.program,'image'),0);gl.activeTexture(gl.TEXTURE0)
    const project=(x:number,y:number)=>[viewport[0]*x+viewport[2]*y+viewport[4],viewport[1]*x+viewport[3]*y+viewport[5]]
    for(const item of items){
      if(item.opacity<=0||item.clip.length<3)continue
      let texture=item.texture
      if(!texture&&item.source){
        texture=this.textures.get(item.source)
        if(!texture){
          texture=gl.createTexture()!;this.textures.set(item.source,texture)
          gl.bindTexture(gl.TEXTURE_2D,texture);gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,false);gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL,false)
          gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,item.source)
          gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR)
          gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE)
        }
      }
      if(!texture)continue
      gl.bindTexture(gl.TEXTURE_2D,texture)
      gl.enable(gl.STENCIL_TEST);gl.stencilMask(255);gl.clear(gl.STENCIL_BUFFER_BIT)
      gl.stencilFunc(gl.ALWAYS,1,255);gl.stencilOp(gl.KEEP,gl.KEEP,gl.REPLACE);gl.colorMask(false,false,false,false)
      const polygon=item.clip.flatMap(p=>[...project(p.x,p.y),0,0])
      gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(polygon),gl.STREAM_DRAW);gl.drawArrays(gl.TRIANGLE_FAN,0,item.clip.length)
      gl.colorMask(true,true,true,true);gl.stencilMask(0);gl.stencilFunc(gl.EQUAL,1,255);gl.stencilOp(gl.KEEP,gl.KEEP,gl.KEEP)
      const m=item.matrix,w=item.width/2,h=item.height/2
      const vertices=[[-w,-h,0,0],[w,-h,1,0],[-w,h,0,1],[-w,h,0,1],[w,-h,1,0],[w,h,1,1]].flatMap(([x,y,u,v])=>[...project(m[0]*x!+m[2]*y!+m[4],m[1]*x!+m[3]*y!+m[5]),u!,item.flipY?1-v!:v!])
      gl.uniform1f(gl.getUniformLocation(this.program,'opacity'),item.opacity)
      gl.enable(gl.BLEND);gl.blendEquation(gl.FUNC_ADD);gl.blendFuncSeparate(gl.SRC_ALPHA,item.additive?gl.ONE:gl.ONE_MINUS_SRC_ALPHA,gl.ZERO,gl.ONE)
      gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(vertices),gl.STREAM_DRAW);gl.drawArrays(gl.TRIANGLES,0,6)
    }
    gl.disable(gl.STENCIL_TEST);gl.stencilMask(255);gl.bindVertexArray(null)
  }
  clearSources(){for(const t of this.textures.values())this.gl.deleteTexture(t);this.textures.clear()}
  dispose(){this.clearSources();this.gl.deleteProgram(this.program);this.gl.deleteBuffer(this.buffer);this.gl.deleteVertexArray(this.vao)}
}
