// Rasterize Slate Box brushes once per scene, shared by Canvas and GPU composition.
export function boxBrush(source: HTMLImageElement | HTMLCanvasElement, brush: any, width: number, height: number): HTMLImageElement | HTMLCanvasElement {
  if (String(brush?.DrawAs).split('::').pop() !== 'Box') return source
  const sw = (source as HTMLImageElement).naturalWidth || source.width
  const sh = (source as HTMLImageElement).naturalHeight || source.height
  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, Math.ceil(width)); canvas.height = Math.max(1, Math.ceil(height))
  const ctx = canvas.getContext('2d')!
  const margin = brush.Margin ?? {}
  const axis = (size: number, target: number, first: number, last: number, natural: number) => {
    first = Math.max(0, Math.min(1, first)); last = Math.max(0, Math.min(1 - first, last))
    const a = natural * first, b = natural * last, shrink = Math.min(1, target / (a + b || 1))
    return { src: [0, size * first, size * (1 - last), size], dst: [0, a * shrink, target - b * shrink, target] }
  }
  const x = axis(sw, canvas.width, margin.Left ?? 0, margin.Right ?? 0, brush.ImageSize?.X || sw)
  const y = axis(sh, canvas.height, margin.Top ?? 0, margin.Bottom ?? 0, brush.ImageSize?.Y || sh)
  for (let row = 0; row < 3; row++) for (let col = 0; col < 3; col++) {
    const dw = x.dst[col + 1]! - x.dst[col]!, dh = y.dst[row + 1]! - y.dst[row]!
    if (dw <= 0 || dh <= 0) continue
    // Degenerate UV strips interpolate the boundary texel instead of leaving a hole.
    const sx = Math.min(sw - 1, x.src[col]!), sy = Math.min(sh - 1, y.src[row]!)
    ctx.drawImage(source, sx, sy, Math.max(1, x.src[col + 1]! - x.src[col]!), Math.max(1, y.src[row + 1]! - y.src[row]!), x.dst[col]!, y.dst[row]!, dw, dh)
  }
  return canvas
}
