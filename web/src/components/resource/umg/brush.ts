// Rasterize texture brushes once per scene for both stage backends.
export function boxBrush(source: HTMLImageElement | HTMLCanvasElement, brush: any, width: number, height: number): HTMLImageElement | HTMLCanvasElement {
  const mode = String(brush?.DrawAs ?? 'Image').split('::').pop()
  const tiling = String(brush?.Tiling ?? 'NoTile').split('::').pop()
  const mirror = String(brush?.Mirroring ?? 'NoMirror').split('::').pop()
  if (mode === 'Image' && tiling === 'NoTile' && mirror === 'NoMirror') return source
  const sw = (source as HTMLImageElement).naturalWidth || source.width
  const sh = (source as HTMLImageElement).naturalHeight || source.height
  const nw = brush?.ImageSize?.X || sw, nh = brush?.ImageSize?.Y || sh
  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, Math.ceil(width)); canvas.height = Math.max(1, Math.ceil(height))
  const ctx = canvas.getContext('2d')!
  if (mode === 'NoDrawType') return canvas
  if (mirror === 'Horizontal' || mirror === 'Both') { ctx.translate(canvas.width, 0); ctx.scale(-1, 1) }
  if (mirror === 'Vertical' || mirror === 'Both') { ctx.translate(0, canvas.height); ctx.scale(1, -1) }
  function draw(sx:number,sy:number,w:number,h:number,dx:number,dy:number,dw:number,dh:number,tx=false,ty=false) {
    if (dw <= 0 || dh <= 0) return
    sx = Math.min(sw-1,sx); sy = Math.min(sh-1,sy); w = Math.max(1,w); h = Math.max(1,h)
    const tileW = tx ? Math.max(1,w/sw*nw) : dw, tileH = ty ? Math.max(1,h/sh*nh) : dh
    if (!tx && !ty) {
      ctx.drawImage(source,sx,sy,w,h,dx,dy,dw,dh)
      return
    }
    // Native repetition keeps draw-call count independent of destination size.
    // Crop once in source resolution, then scale the pattern to the Slate tile
    // size; partial final tiles are clipped by fillRect, not stretched.
    const tile = document.createElement('canvas')
    tile.width = Math.max(1,Math.ceil(w)); tile.height = Math.max(1,Math.ceil(h))
    tile.getContext('2d')!.drawImage(source,sx,sy,w,h,0,0,tile.width,tile.height)
    const pattern = ctx.createPattern(tile,tx && ty ? 'repeat' : tx ? 'repeat-x' : 'repeat-y')
    if (!pattern) return
    pattern.setTransform(new DOMMatrix().translate(dx,dy).scale(tileW/tile.width,tileH/tile.height))
    ctx.fillStyle = pattern
    ctx.fillRect(dx,dy,dw,dh)
  }
  if (mode !== 'Box' && mode !== 'Border') {
    draw(0,0,sw,sh,0,0,canvas.width,canvas.height,tiling==='Horizontal'||tiling==='Both',tiling==='Vertical'||tiling==='Both')
    return canvas
  }
  const margin = brush.Margin ?? {}
  const axis = (size:number,target:number,first:number,last:number,natural:number) => {
    first=Math.max(0,Math.min(1,first)); last=Math.max(0,Math.min(1-first,last))
    const a=natural*first,b=natural*last,shrink=Math.min(1,target/(a+b||1))
    return {src:[0,size*first,size*(1-last),size],dst:[0,a*shrink,target-b*shrink,target]}
  }
  const x=axis(sw,canvas.width,margin.Left??0,margin.Right??0,nw)
  const y=axis(sh,canvas.height,margin.Top??0,margin.Bottom??0,nh)
  for(let row=0;row<3;row++) for(let col=0;col<3;col++) {
    if(mode==='Border'&&row===1&&col===1)continue
    draw(x.src[col]!,y.src[row]!,x.src[col+1]!-x.src[col]!,y.src[row+1]!-y.src[row]!,x.dst[col]!,y.dst[row]!,x.dst[col+1]!-x.dst[col]!,y.dst[row+1]!-y.dst[row]!,mode==='Border'&&col===1,mode==='Border'&&row===1)
  }
  return canvas
}
