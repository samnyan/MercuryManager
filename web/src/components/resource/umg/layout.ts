import { previewText } from './textPreview'
import type { UMGNode, ComputedLayout, Rect } from './types'

export function computeTreeLayout(
  nodes: UMGNode[],
  rootNodeId: string,
  viewport: { width: number; height: number }
): Map<string, ComputedLayout> {
  const byId = new Map(nodes.map(n => [n.id, n]))
  const result = new Map<string, ComputedLayout>()

  const number = (v: unknown, fallback = 0) =>
    typeof v === 'number' && Number.isFinite(v) ? v : fallback

  const isFill = (n: UMGNode) => String(n.slot?.values.Size?.SizeRule ?? '').split('::').pop() === 'Fill'

  function desired(n: UMGNode, seen = new Set<string>()): { width: number; height: number; known: boolean } {
    if (seen.has(n.id)) return { width: 0, height: 0, known: false }
    seen = new Set(seen).add(n.id)

    const vis = String(n.visual.Visibility ?? 'Visible').split('::').pop()
    if (vis === 'Collapsed') return { width: 0, height: 0, known: true }

    if (n.className === 'Spacer') {
      return { width: Math.max(0, number(n.visual.Size?.X)), height: Math.max(0, number(n.visual.Size?.Y)), known: true }
    }


    const childNodes = n.children.map(id => byId.get(id)).filter((v): v is UMGNode => !!v)

    // 笔刷固有图片尺寸 (非容器控件如 Image，或无子元素的控件；对于 Border/SizeBox 等容器，有子元素时优先度低于子元素布局测量)
    const isContainer = ['RetainerBox', 'ScaleBox', 'WidgetSwitcher', 'Border', 'SizeBox', 'CanvasPanel', 'Overlay', 'HorizontalBox', 'VerticalBox'].includes(n.className)
    if (!isContainer || childNodes.length === 0) {
      const image = n.visual.Brush?.ImageSize || (!isContainer ? n.visual.Background?.ImageSize : undefined)
      if (image && (number(image.X) > 0 || number(image.Y) > 0)) {
        return { width: number(image.X), height: number(image.Y), known: true }
      }
    }

    // 文本测量 (包括 TextBlock 和 CircleText)
    if (typeof n.visual.Text === 'string' && typeof document !== 'undefined') {
      const context = document.createElement('canvas').getContext('2d')
      if (context) {
        const size = number(n.visual.Font?.Size, 12) * (96 / 72)
        context.font = `${size}px sans-serif`
        const lines = previewText(n.className, n.visual.Text).split(/\r?\n/)
        const textWidth = Math.max(
          number(n.visual.MinDesiredWidth),
          ...lines.map(line => context.measureText(line).width)
        )
        return { width: textWidth, height: lines.length * size * 1.2, known: true }
      }
    }

    // CanvasPanel 汇总子元素槽位尺寸
    if (n.className === 'CanvasPanel') {
      let maxW = 0, maxH = 0, anyKnown = false
      for (const c of childNodes) {
        const cvis = String(c.visual.Visibility ?? 'Visible').split('::').pop()
        if (cvis === 'Collapsed') continue
        const cd = desired(c, seen)
        const slot = c.slot?.values ?? {}
        const data = slot.LayoutData ?? {}, anchors = data.Anchors ?? {}, off = data.Offsets ?? {}
        const minX = number(anchors.Minimum?.X), maxX = number(anchors.Maximum?.X)
        const minY = number(anchors.Minimum?.Y), maxY = number(anchors.Maximum?.Y)
        // 当点锚点且非 bAutoSize 时，槽位 Right/Bottom 就是尺寸
        const cw = minX === maxX && !slot.bAutoSize ? number(off.Right) : cd.width
        const ch = minY === maxY && !slot.bAutoSize ? number(off.Bottom) : cd.height
        if (cw > 0 || ch > 0) anyKnown = true
        maxW = Math.max(maxW, cw)
        maxH = Math.max(maxH, ch)
      }
      return { width: maxW, height: maxH, known: anyKnown }
    }

    const measuredChildren = n.className === 'WidgetSwitcher' ? childNodes.filter((_,i)=>i===number(n.visual.ActiveWidgetIndex)) : childNodes
    const children = measuredChildren.map(c => {
      const d = desired(c, seen)
      if (String(c.visual.Visibility ?? '').split('::').pop() === 'Collapsed') return d
      const p = c.slot?.values.Padding ?? {}
      return { ...d, width: d.width + number(p.Left) + number(p.Right), height: d.height + number(p.Top) + number(p.Bottom) }
    })
    const width = n.className === 'HorizontalBox'
      ? children.reduce((s, c) => s + c.width, 0)
      : Math.max(0, ...children.map(c => c.width))
    const height = n.className === 'VerticalBox'
      ? children.reduce((s, c) => s + c.height, 0)
      : Math.max(0, ...children.map(c => c.height))

    if (n.className === 'SizeBox') {
      const overrideWidth = !!n.visual.bOverride_WidthOverride
      const overrideHeight = !!n.visual.bOverride_HeightOverride
      const minWidth = !!n.visual.bOverride_MinDesiredWidth
      const minHeight = !!n.visual.bOverride_MinDesiredHeight
      const maxWidth = !!n.visual.bOverride_MaxDesiredWidth
      const maxHeight = !!n.visual.bOverride_MaxDesiredHeight
      const constrained = (value: number, min: boolean, minValue: unknown, max: boolean, maxValue: unknown) => {
        // SBox measures minimum first, then maximum; explicit overrides bypass both.
        if (min) value = Math.max(value, number(minValue))
        if (max) value = Math.min(value, number(maxValue))
        return value
      }
      return {
        width: overrideWidth ? number(n.visual.WidthOverride) : constrained(width, minWidth, n.visual.MinDesiredWidth, maxWidth, n.visual.MaxDesiredWidth),
        height: overrideHeight ? number(n.visual.HeightOverride) : constrained(height, minHeight, n.visual.MinDesiredHeight, maxHeight, n.visual.MaxDesiredHeight),
        known: overrideWidth || overrideHeight || minWidth || minHeight || maxWidth || maxHeight || children.some(c => c.known)
      }
    }
    return {
      width,
      height,
      known: (width > 0 || height > 0) && children.some(c => c.known)
    }
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

      // Canvas offsets are literal dimensions unless AutoSize is explicitly enabled.
      // Zero is meaningful (e.g. FACE's zero-sized positioning container).
      const w = maxX !== minX
        ? allocated.width * (maxX - minX) - number(off.Left) - number(off.Right)
        : slot.bAutoSize ? size.width : number(off.Right)
      const h = maxY !== minY
        ? allocated.height * (maxY - minY) - number(off.Top) - number(off.Bottom)
        : slot.bAutoSize ? size.height : number(off.Bottom)

      const alignX = maxX === minX ? number(align.X) : 0
      const alignY = maxY === minY ? number(align.Y) : 0

      rect = {
        x: allocated.x + allocated.width * minX + number(off.Left) - w * alignX,
        y: allocated.y + allocated.height * minY + number(off.Top) - h * alignY,
        width: Math.max(0, w),
        height: Math.max(0, h)
      }
    } else if (!root && n.slot && ['RetainerBoxSlot', 'WidgetSwitcherSlot', 'OverlaySlot', 'BorderSlot', 'SizeBoxSlot', 'HorizontalBoxSlot', 'VerticalBoxSlot'].includes(n.slot.className)) {
      const p = slot.Padding ?? {}, d = desired(n)
      rect = {
        x: allocated.x + number(p.Left),
        y: allocated.y + number(p.Top),
        width: Math.max(0, allocated.width - number(p.Left) - number(p.Right)),
        height: Math.max(0, allocated.height - number(p.Top) - number(p.Bottom))
      }

      for (const axis of ['H', 'V'] as const) {
        const alignment = String(slot[axis === 'H' ? 'HorizontalAlignment' : 'VerticalAlignment'] ?? `${axis}Align_Fill`)
        if (alignment.endsWith('Fill')) continue

        const horizontal = axis === 'H'
        const available = horizontal ? rect.width : rect.height
        // A measured zero is meaningful (Spacer/SizeBox); only unknown sizes fall back to allocation.
        const size = d.known
          ? Math.max(0, Math.min(available, horizontal ? d.width : d.height))
          : available

        const shift = alignment.endsWith('Center')
          ? (available - size) / 2
          : alignment.endsWith('Right') || alignment.endsWith('Bottom')
            ? available - size
            : 0

        if (horizontal) {
          rect.x += shift
          rect.width = size
        } else {
          rect.y += shift
          rect.height = size
        }
      }
    }

    if (n.className === 'RetainerBox') issues.push('RetainerBox displays children directly; effect material, offscreen compositing and render phases are not simulated.')
    if (n.visual.Text != null) issues.push('Text metrics use a browser fallback font, not the game font.')
    if (n.preview.layout === 'unsupported') issues.push('Unsupported layout; outline is allocated space, not a reconstructed widget.')

    result.set(n.id, { rect, visible, collapsed, issues })
    if (collapsed) return

    const supportedContainers = ['RetainerBox', 'ScaleBox', 'WidgetSwitcher', 'CanvasPanel', 'Overlay', 'Border', 'SizeBox', 'HorizontalBox', 'VerticalBox']
    if (n.children.length && !supportedContainers.includes(n.className) && !n.childWidget?.localTreeId) {
      issues.push('Child arrangement is unsupported; children are omitted from the stage.')
      return
    }

    // SizeBox overrides participate in desired-size measurement above.
    // Children arrange inside the geometry actually allotted by the parent.

    let children = n.children.map(id => byId.get(id)).filter((v): v is UMGNode => !!v)
    if (n.className === 'CanvasPanel') {
      children = children
        .map((c, i) => ({ c, i }))
        .sort((a, b) => number(a.c.slot?.values.ZOrder) - number(b.c.slot?.values.ZOrder) || a.i - b.i)
        .map(v => v.c)
    }

    const box = n.className === 'HorizontalBox' || n.className === 'VerticalBox'
    const horizontal = n.className === 'HorizontalBox'
    const active = children.filter(c => String(c.visual.Visibility).split('::').pop() !== 'Collapsed')
    const extent = horizontal ? rect.width : rect.height
    const paddingExtent = (c: UMGNode) => {
      const p = c.slot?.values.Padding ?? {}
      return horizontal ? number(p.Left) + number(p.Right) : number(p.Top) + number(p.Bottom)
    }
    const reservedPadding = active.reduce((sum,c) => sum + paddingExtent(c), 0)
    const auto = active.reduce(
      (sum, c) => isFill(c) ? sum : sum + (horizontal ? desired(c).width : desired(c).height),
      0
    )
    const weight = active.reduce(
      (sum, c) => sum + (isFill(c) ? number(c.slot?.values.Size?.Value, 1) : 0),
      0
    )

    if (n.className === 'ScaleBox') {
      const child = children[0]
      if (!child) return
      const d = desired(child), mode = String(n.visual.Stretch ?? 'ScaleToFit').split('::').pop()
      if (!(d.width > 0 && d.height > 0)) {
        issues.push('ScaleBox desired size is unknown; using allocated space without scaling.')
        visit(child, rect, visible, path)
        return
      }
      let sx = 1, sy = 1
      const x = rect.width / d.width, y = rect.height / d.height
      switch (mode) {
        case 'Fill': sx=x; sy=y; break
        case 'ScaleToFit': sx=sy=Math.min(x,y); break
        case 'ScaleToFitX': sx=sy=x; break
        case 'ScaleToFitY': sx=sy=y; break
        case 'ScaleToFill': sx=sy=Math.max(x,y); break
        case 'UserSpecified': sx=sy=Math.max(0,number(n.visual.UserSpecifiedScale,1)); break
        case 'None': break
        default: issues.push(`Unsupported ScaleBox stretch mode: ${mode}`)
      }
      const direction=String(n.visual.StretchDirection ?? 'Both').split('::').pop()
      if (direction==='DownOnly') { sx=Math.min(1,sx); sy=Math.min(1,sy) }
      if (direction==='UpOnly') { sx=Math.max(1,sx); sy=Math.max(1,sy) }
      const align = (axis: 'HorizontalAlignment'|'VerticalAlignment', remaining: number) => {
        const value=String(child.slot?.values[axis] ?? 'Center')
        return value.endsWith('Left')||value.endsWith('Top') ? 0 : value.endsWith('Right')||value.endsWith('Bottom') ? remaining : remaining/2
      }
      const ox=rect.x+align('HorizontalAlignment',rect.width-d.width*sx)
      const oy=rect.y+align('VerticalAlignment',rect.height-d.height*sy)
      // Arrange at natural size first, then transform every newly arranged descendant.
      const before=new Set(result.keys())
      visit(child,{x:0,y:0,width:d.width,height:d.height},visible,path)
      for (const [id,layout] of result) if (!before.has(id)) {
        const r=layout.rect
        layout.rect={x:ox+r.x*sx,y:oy+r.y*sy,width:r.width*sx,height:r.height*sy}
      }
      issues.push('ScaleBox geometry is reconstructed; font metrics, render transforms and inherited-scale cancellation remain approximate.')
      return
    }

    let cursor = 0
    for (const child of children) {
      let area = { ...rect }
      if (box) {
        const d = desired(child)
        const fill = isFill(child)
        const size = String(child.visual.Visibility).split('::').pop() === 'Collapsed'
          ? 0
          : fill
            ? Math.max(0, extent - auto - reservedPadding) * number(child.slot?.values.Size?.Value, 1) / (weight || 1) + paddingExtent(child)
            : (horizontal ? d.width : d.height) + paddingExtent(child)

        if (horizontal) {
          area.x += cursor
          area.width = size
        } else {
          area.y += cursor
          area.height = size
        }
        cursor += size
      }
      visit(child, area, visible && (n.className !== 'WidgetSwitcher' || children.indexOf(child) === number(n.visual.ActiveWidgetIndex)), path)
    }
  }

  const root = byId.get(rootNodeId)
  if (root) {
    visit(root, { x: 0, y: 0, width: viewport.width, height: viewport.height }, true, new Set(), true)
  }
  return result
}
