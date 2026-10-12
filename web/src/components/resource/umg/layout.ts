import type { UMGNode, ComputedLayout, Rect } from './types'

export function computeTreeLayout(nodes: UMGNode[], rootNodeId: string, viewport: { width: number; height: number }): Map<string, ComputedLayout> {
  const byId = new Map(nodes.map(n => [n.id, n]))
  const result = new Map<string, ComputedLayout>()
  const number = (v: unknown, fallback = 0) => typeof v === 'number' && Number.isFinite(v) ? v : fallback
  function desired(n: UMGNode, seen = new Set<string>()): { width: number; height: number; known: boolean } {
    if (seen.has(n.id)) return { width: 0, height: 0, known: false }
    seen = new Set(seen).add(n.id)
    if (String(n.visual.Visibility).split('::').pop() === 'Collapsed') return { width: 0, height: 0, known: true }
    const image = n.visual.Brush?.ImageSize
    if (image) return { width: number(image.X), height: number(image.Y), known: true }
    const children = n.children.map(id => byId.get(id)).filter((v): v is UMGNode => !!v).map(c => desired(c, seen))
    const width = n.className === 'HorizontalBox' ? children.reduce((s,c) => s+c.width,0) : Math.max(0,...children.map(c=>c.width))
    const height = n.className === 'VerticalBox' ? children.reduce((s,c) => s+c.height,0) : Math.max(0,...children.map(c=>c.height))
    return { width: n.visual.bOverride_WidthOverride ? number(n.visual.WidthOverride) : width, height: n.visual.bOverride_HeightOverride ? number(n.visual.HeightOverride) : height, known: children.length > 0 && children.every(c=>c.known) }
  }
  function visit(n: UMGNode, allocated: Rect, inheritedVisible: boolean, path: Set<string>, root = false) {
    if (path.has(n.id) || result.has(n.id)) return
    path = new Set(path).add(n.id)
    const visibility = String(n.visual.Visibility ?? 'Visible').split('::').pop()
    const collapsed = visibility === 'Collapsed'
    const visible = inheritedVisible && !collapsed && visibility !== 'Hidden'
    const issues: string[] = []
    let rect = { ...allocated }
    const slot = n.slot?.values ?? {}
    if (!root && n.slot?.className === 'CanvasPanelSlot') {
      const data = slot.LayoutData ?? {}, anchors = data.Anchors ?? {}, off = data.Offsets ?? {}, align = data.Alignment ?? {}
      const minX = number(anchors.Minimum?.X), minY = number(anchors.Minimum?.Y)
      const maxX = number(anchors.Maximum?.X), maxY = number(anchors.Maximum?.Y)
      const size = desired(n)
      if (slot.bAutoSize && !size.known) issues.push('Unknown desired size (AutoSize).')
      if (!slot.bAutoSize && (off.Right === undefined || off.Bottom === undefined)) issues.push('Missing serialized dimensions; zero used as a preview fallback.')
      const w = maxX !== minX ? allocated.width * (maxX-minX)-number(off.Left)-number(off.Right) : slot.bAutoSize ? size.width : number(off.Right)
      const h = maxY !== minY ? allocated.height * (maxY-minY)-number(off.Top)-number(off.Bottom) : slot.bAutoSize ? size.height : number(off.Bottom)
      rect = { x: allocated.x+allocated.width*minX+number(off.Left)-(maxX===minX ? w*number(align.X) : 0), y: allocated.y+allocated.height*minY+number(off.Top)-(maxY===minY ? h*number(align.Y) : 0), width: Math.max(0,w), height: Math.max(0,h) }
    } else if (!root && n.slot && ['OverlaySlot','BorderSlot','SizeBoxSlot','HorizontalBoxSlot','VerticalBoxSlot'].includes(n.slot.className)) {
      const p = slot.Padding ?? {}, d = desired(n)
      rect = { x: allocated.x+number(p.Left), y: allocated.y+number(p.Top), width: Math.max(0,allocated.width-number(p.Left)-number(p.Right)), height: Math.max(0,allocated.height-number(p.Top)-number(p.Bottom)) }
      for (const axis of ['H','V'] as const) {
        const alignment = String(slot[axis==='H'?'HorizontalAlignment':'VerticalAlignment'] ?? `${axis}Align_Fill`)
        if (alignment.endsWith('Fill')) continue
        if (!d.known) issues.push('Unknown desired size for aligned child.')
        const horizontal = axis==='H', available = horizontal?rect.width:rect.height, size = Math.min(available,horizontal?d.width:d.height)
        const shift = alignment.endsWith('Center') ? (available-size)/2 : alignment.endsWith('Right')||alignment.endsWith('Bottom') ? available-size : 0
        if(horizontal){rect.x+=shift;rect.width=size}else{rect.y+=shift;rect.height=size}
      }
    }
    if (n.preview.layout === 'unsupported') issues.push('Unsupported layout; outline is allocated space, not a reconstructed widget.')
    if (n.visual.RenderTransform?.Shear) issues.push('Shear is not supported; preview applies translation, scale and rotation only.')
    if (n.visual.RenderTransform && !n.visual.RenderTransformPivot) issues.push('Unknown pivot; preview uses the center.')
    result.set(n.id, { rect, visible, collapsed, issues })
    if (collapsed) return
    const supportedContainers = ['CanvasPanel','Overlay','Border','SizeBox','HorizontalBox','VerticalBox']
    if (n.children.length && !supportedContainers.includes(n.className)) { issues.push('Child arrangement is unsupported; children are omitted from the stage.'); return }
    if (n.className === 'SizeBox') {
      if (n.visual.bOverride_WidthOverride) rect = { ...rect, width: number(n.visual.WidthOverride) }
      if (n.visual.bOverride_HeightOverride) rect = { ...rect, height: number(n.visual.HeightOverride) }
    }
    let children = n.children.map(id => byId.get(id)).filter((v): v is UMGNode => !!v)
    if(n.className==='CanvasPanel') children = children.map((c,i)=>({c,i})).sort((a,b)=>number(a.c.slot?.values.ZOrder)-number(b.c.slot?.values.ZOrder)||a.i-b.i).map(v=>v.c)
    const box = n.className==='HorizontalBox'||n.className==='VerticalBox'
    const horizontal = n.className==='HorizontalBox'
    const active = children.filter(c=>String(c.visual.Visibility).split('::').pop()!=='Collapsed')
    const extent = horizontal?rect.width:rect.height
    const auto = active.reduce((sum,c)=>c.slot?.values.Size?.SizeRule==='Fill'?sum:sum+(horizontal?desired(c).width:desired(c).height),0)
    const weight = active.reduce((sum,c)=>sum+(c.slot?.values.Size?.SizeRule==='Fill'?number(c.slot?.values.Size?.Value,1):0),0)
    let cursor=0
    for(const child of children){
      let area={...rect}
      if(box){const d=desired(child);const fill=child.slot?.values.Size?.SizeRule==='Fill';const size=String(child.visual.Visibility).split('::').pop()==='Collapsed'?0:fill?Math.max(0,extent-auto)*number(child.slot?.values.Size?.Value,1)/(weight||1):horizontal?d.width:d.height
        if(horizontal){area.x+=cursor;area.width=size}else{area.y+=cursor;area.height=size}cursor+=size
      }
      visit(child,area,visible,path)
    }
  }
  const root=byId.get(rootNodeId)
  if(root)visit(root,{x:0,y:0,width:viewport.width,height:viewport.height},true,new Set(),true)
  return result
}
