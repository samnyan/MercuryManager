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

  function desired(n: UMGNode, seen = new Set<string>()): { width: number; height: number; known: boolean } {
    if (seen.has(n.id)) return { width: 0, height: 0, known: false }
    seen = new Set(seen).add(n.id)

    const vis = String(n.visual.Visibility ?? 'Visible').split('::').pop()
    if (vis === 'Collapsed') return { width: 0, height: 0, known: true }

    // 显式宽高覆盖
    if (n.visual.bOverride_WidthOverride || n.visual.bOverride_HeightOverride) {
      return {
        width: n.visual.bOverride_WidthOverride ? number(n.visual.WidthOverride) : 0,
        height: n.visual.bOverride_HeightOverride ? number(n.visual.HeightOverride) : 0,
        known: true
      }
    }

    const childNodes = n.children.map(id => byId.get(id)).filter((v): v is UMGNode => !!v)

    // 笔刷固有图片尺寸 (非容器控件如 Image，或无子元素的控件；对于 Border/SizeBox 等容器，有子元素时优先度低于子元素布局测量)
    const isContainer = ['Border', 'SizeBox', 'CanvasPanel', 'Overlay', 'HorizontalBox', 'VerticalBox'].includes(n.className)
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
        const lines = n.visual.Text.split(/\r?\n/)
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
        const cw = (minX === maxX && !slot.bAutoSize && number(off.Right) > 0) ? number(off.Right) : cd.width
        const ch = (minY === maxY && !slot.bAutoSize && number(off.Bottom) > 0) ? number(off.Bottom) : cd.height
        if (cw > 0 || ch > 0) anyKnown = true
        maxW = Math.max(maxW, cw)
        maxH = Math.max(maxH, ch)
      }
      return { width: maxW, height: maxH, known: anyKnown }
    }

    const children = childNodes.map(c => desired(c, seen))
    const width = n.className === 'HorizontalBox'
      ? children.reduce((s, c) => s + c.width, 0)
      : Math.max(0, ...children.map(c => c.width))
    const height = n.className === 'VerticalBox'
      ? children.reduce((s, c) => s + c.height, 0)
      : Math.max(0, ...children.map(c => c.height))

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

      let w = 0, h = 0
      const hasExplicitRight = number(off.Right) > 0
      const hasExplicitBottom = number(off.Bottom) > 0

      if (maxX !== minX) {
        // 拉伸
        w = allocated.width * (maxX - minX) - number(off.Left) - number(off.Right)
      } else {
        // 点锚点
        if (slot.bAutoSize || (!hasExplicitRight && !hasExplicitBottom)) {
          w = size.width > 0 ? size.width : number(off.Right)
        } else if (!hasExplicitRight && hasExplicitBottom && size.height > 0 && size.width > 0) {
          // 若只明确指定了高度，且元素具有固有比例尺寸，按比例适配宽度
          w = (size.width / size.height) * number(off.Bottom)
        } else {
          w = hasExplicitRight ? number(off.Right) : (size.width > 0 ? size.width : 0)
        }
      }

      if (maxY !== minY) {
        // 拉伸
        h = allocated.height * (maxY - minY) - number(off.Top) - number(off.Bottom)
      } else {
        // 点锚点
        if (slot.bAutoSize || (!hasExplicitRight && !hasExplicitBottom)) {
          h = size.height > 0 ? size.height : number(off.Bottom)
        } else if (!hasExplicitBottom && hasExplicitRight && size.width > 0 && size.height > 0) {
          // 若只明确指定了宽度，且元素具有固有比例尺寸，按比例适配高度
          h = (size.height / size.width) * number(off.Right)
        } else {
          h = hasExplicitBottom ? number(off.Bottom) : (size.height > 0 ? size.height : 0)
        }
      }

      const alignX = maxX === minX ? number(align.X) : 0
      const alignY = maxY === minY ? number(align.Y) : 0

      rect = {
        x: allocated.x + allocated.width * minX + number(off.Left) - w * alignX,
        y: allocated.y + allocated.height * minY + number(off.Top) - h * alignY,
        width: Math.max(0, w),
        height: Math.max(0, h)
      }
    } else if (!root && n.slot && ['OverlaySlot', 'BorderSlot', 'SizeBoxSlot', 'HorizontalBoxSlot', 'VerticalBoxSlot'].includes(n.slot.className)) {
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
        // 若子元素无固有尺寸（如未限定大小的容器），默认占满可用空间，防止坍缩为 0
        const size = d.known && (horizontal ? d.width : d.height) > 0
          ? Math.min(available, horizontal ? d.width : d.height)
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

    if (n.visual.Text != null) issues.push('Text metrics use a browser fallback font, not the game font.')
    if (n.preview.layout === 'unsupported') issues.push('Unsupported layout; outline is allocated space, not a reconstructed widget.')

    result.set(n.id, { rect, visible, collapsed, issues })
    if (collapsed) return

    const supportedContainers = ['CanvasPanel', 'Overlay', 'Border', 'SizeBox', 'HorizontalBox', 'VerticalBox']
    if (n.children.length && !supportedContainers.includes(n.className) && !n.childWidget?.localTreeId) {
      issues.push('Child arrangement is unsupported; children are omitted from the stage.')
      return
    }

    if (n.className === 'SizeBox') {
      if (n.visual.bOverride_WidthOverride) rect = { ...rect, width: number(n.visual.WidthOverride) }
      if (n.visual.bOverride_HeightOverride) rect = { ...rect, height: number(n.visual.HeightOverride) }
    }

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
    const auto = active.reduce(
      (sum, c) => c.slot?.values.Size?.SizeRule === 'Fill' ? sum : sum + (horizontal ? desired(c).width : desired(c).height),
      0
    )
    const weight = active.reduce(
      (sum, c) => sum + (c.slot?.values.Size?.SizeRule === 'Fill' ? number(c.slot?.values.Size?.Value, 1) : 0),
      0
    )

    let cursor = 0
    for (const child of children) {
      let area = { ...rect }
      if (box) {
        const d = desired(child)
        const fill = child.slot?.values.Size?.SizeRule === 'Fill'
        const size = String(child.visual.Visibility).split('::').pop() === 'Collapsed'
          ? 0
          : fill
            ? Math.max(0, extent - auto) * number(child.slot?.values.Size?.Value, 1) / (weight || 1)
            : horizontal ? d.width : d.height

        if (horizontal) {
          area.x += cursor
          area.width = size
        } else {
          area.y += cursor
          area.height = size
        }
        cursor += size
      }
      visit(child, area, visible, path)
    }
  }

  const root = byId.get(rootNodeId)
  if (root) {
    visit(root, { x: 0, y: 0, width: viewport.width, height: viewport.height }, true, new Set(), true)
  }
  return result
}
