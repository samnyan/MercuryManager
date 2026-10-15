// CircleText uses a zero-size widget as the circle origin, not a text box.
// Raster bounds are symmetric so the origin survives ancestor transforms.
export function circleText(visual: Record<string, any>, fontFamily:string, fontSize:number, fill:string): HTMLCanvasElement {
  const number=(v:any,f:number)=>Number.isFinite(Number(v))?Number(v):f
  const chars=Array.from(String(visual.Text ?? ''))
  const radius=number(visual.TextRadius,0), interval=number(visual.TextRadiusInterval,0)
  const start=number(visual.TextAngleStart,0),end=number(visual.TextAngleEnd,start)
  const extent=Math.max(Math.abs(radius),Math.abs(radius+interval*Math.max(0,chars.length-1)))+fontSize*2
  const canvas=document.createElement('canvas')
  canvas.width=canvas.height=Math.min(8192,Math.max(1,Math.ceil(extent*2)))
  const ctx=canvas.getContext('2d')!
  ctx.translate(canvas.width/2,canvas.height/2)
  ctx.font=`${fontSize}px ${fontFamily}`
  ctx.fillStyle=fill;ctx.textAlign='center';ctx.textBaseline='middle'
  chars.forEach((char,i)=>{
    const angle=start+(end-start)*(chars.length>1?i/(chars.length-1):0.5)
    const radians=angle*Math.PI/180,r=radius+interval*i
    ctx.save();ctx.translate(Math.cos(radians)*r,Math.sin(radians)*r)
    ctx.rotate((number(visual.CharacterAngle,0)+(visual.bCharacterAngleBasedHorizontal?0:angle))*Math.PI/180)
    ctx.fillText(char,0,0);ctx.restore()
  })
  return canvas
}
