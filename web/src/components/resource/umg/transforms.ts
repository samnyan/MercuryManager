import type { UMGNode, ComputedLayout } from './types'

// CSS/SVG column-vector affine matrix: x'=a*x+c*y+e, y'=b*x+d*y+f.
export type Matrix = [number, number, number, number, number, number]
const identity = (): Matrix => [1, 0, 0, 1, 0, 0]
export function multiply(l: Matrix, r: Matrix): Matrix {
  return [l[0]*r[0]+l[2]*r[1], l[1]*r[0]+l[3]*r[1], l[0]*r[2]+l[2]*r[3], l[1]*r[2]+l[3]*r[3], l[0]*r[4]+l[2]*r[5]+l[4], l[1]*r[4]+l[3]*r[5]+l[5]]
}
const translate = (x: number, y: number): Matrix => [1,0,0,1,x,y]
const finite = (value: unknown, fallback: number) => typeof value === 'number' && Number.isFinite(value) ? value : fallback
export function computeRenderTransforms(nodes: UMGNode[], layouts: Map<string, ComputedLayout>, rootId: string): Map<string, Matrix> {
  const byId = new Map(nodes.map(n => [n.id,n]))
  const result = new Map<string, Matrix>()
  function visit(id: string, parent: Matrix) {
    const node = byId.get(id), layout = layouts.get(id)
    if (!node || !layout || result.has(id)) return
    const t = node.visual.RenderTransform, pivot = node.visual.RenderTransformPivot
    const px = layout.rect.x+layout.rect.width*finite(pivot?.X,0.5)
    const py = layout.rect.y+layout.rect.height*finite(pivot?.Y,0.5)
    const angle = finite(t?.Angle,0)*Math.PI/180
    const rotation: Matrix = [Math.cos(angle), Math.sin(angle), -Math.sin(angle), Math.cos(angle),0,0]
    const scale: Matrix = [finite(t?.Scale?.X,1),0,0,finite(t?.Scale?.Y,1),0,0]
    // FWidgetTransform: scale -> FShear2D -> rotation -> translation.
    const slope = (v: unknown) => Math.tan(Math.max(-89, Math.min(89, finite(v, 0))) * Math.PI / 180)
    const shear: Matrix = [1, slope(t?.Shear?.Y), slope(t?.Shear?.X), 1, 0, 0]
    const local = multiply(translate(px+finite(t?.Translation?.X,0),py+finite(t?.Translation?.Y,0)),multiply(rotation,multiply(shear,multiply(scale,translate(-px,-py)))))
    const world = multiply(parent,local)
    result.set(id,world)
    for (const child of node.children) visit(child,world)
  }
  visit(rootId,identity())
  return result
}
export function polygon(matrix: Matrix, rect: ComputedLayout['rect']): string {
  return [[rect.x,rect.y],[rect.x+rect.width,rect.y],[rect.x+rect.width,rect.y+rect.height],[rect.x,rect.y+rect.height]]
    .map(([x,y]) => `${matrix[0]*x!+matrix[2]*y!+matrix[4]},${matrix[1]*x!+matrix[3]*y!+matrix[5]}`).join(' ')
}
// Element local coordinates already include its layout translation.
export function elementMatrix(world: Matrix, x: number, y: number): string {
  return `matrix(${multiply(world,translate(x,y)).join(',')})`
}
