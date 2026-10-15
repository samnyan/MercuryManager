import type { ParticleMeshGeometry } from './ParticleMeshGeometry'
import type { SimParticle } from './ParticleRuntime'
import { transformMeshVertex } from './ParticleMeshWireframe'

/** APPROXIMATION: real mesh triangles, particle color, fixed preview lighting.
 * Optional frozen material bake uses affine UV0 mapping (without particle RGB tint).
 * Not native material execution: no reflections, shadows or
 * depth buffer. Painter sorting and whole-triangle near rejection are approximate.
 * XYZ Euler transform is shared with the diagnostic renderer, not UE factory parity.
 */
export function drawParticleMeshPreview(
  ctx: CanvasRenderingContext2D, mesh: ParticleMeshGeometry, p: SimParticle,
  project: (x: number, y: number, z: number) => { sx: number; sy: number; depth: number } | null,
  texture?: HTMLCanvasElement | null
): number {
  const vertices: [number, number, number][] = []
  for (let i = 0; i < mesh.positions.length; i += 3)
    vertices.push(transformMeshVertex(mesh.positions[i]!, mesh.positions[i + 1]!, mesh.positions[i + 2]!, p))
  const points = vertices.map(v => project(...v))
  const triangles = []
  for (const section of mesh.sections) {
    for (let i = section.firstIndex; i < section.firstIndex + section.numTriangles * 3; i += 3) {
      const ia = mesh.indices[i]!, ib = mesh.indices[i + 1]!, ic = mesh.indices[i + 2]!
      const a = points[ia], b = points[ib], c = points[ic]
      if (!a || !b || !c) continue
      const va = vertices[ia]!, vb = vertices[ib]!, vc = vertices[ic]!
      const u = vb.map((v, j) => v - va[j]!), v = vc.map((v, j) => v - va[j]!)
      const nx = u[1]! * v[2]! - u[2]! * v[1]!
      const ny = u[2]! * v[0]! - u[0]! * v[2]!
      const nz = u[0]! * v[1]! - u[1]! * v[0]!
      const length = Math.hypot(nx, ny, nz)
      if (!length) continue
      // APPROXIMATION: two-sided neutral studio light, independent of game scene.
      const light = 0.3 + 0.7 * Math.abs((nx * 0.3 + ny * 0.4 + nz * 0.8660254) / length)
      triangles.push({ a, b, c, ia, ib, ic, depth: (a.depth + b.depth + c.depth) / 3, light })
    }
  }
  triangles.sort((a, b) => b.depth - a.depth)
  ctx.save()
  ctx.globalCompositeOperation = p.blendMode.includes('Additive') ? 'lighter' : 'source-over'
  ctx.globalAlpha = Math.max(0, Math.min(1, p.a))
  const uv = mesh.texCoords?.[0]
  const pattern = texture && texture.width && texture.height ? ctx.createPattern(texture, 'repeat') : null
  for (const { a, b, c, ia, ib, ic, light } of triangles) {
    // APPROXIMATION: affine UV0 mapping of the existing frozen material bake.
    // No perspective correction, per-section materials, shader animation or lighting.
    if (pattern && texture && uv) {
      const x0 = uv[ia * 2]! * texture.width, y0 = uv[ia * 2 + 1]! * texture.height
      const x1 = uv[ib * 2]! * texture.width - x0, y1 = uv[ib * 2 + 1]! * texture.height - y0
      const x2 = uv[ic * 2]! * texture.width - x0, y2 = uv[ic * 2 + 1]! * texture.height - y0
      const det = x1 * y2 - x2 * y1
      if (Number.isFinite(det) && Math.abs(det) > 1e-8) {
        const m0 = ((b.sx - a.sx) * y2 - (c.sx - a.sx) * y1) / det
        const m1 = ((b.sy - a.sy) * y2 - (c.sy - a.sy) * y1) / det
        const m2 = ((c.sx - a.sx) * x1 - (b.sx - a.sx) * x2) / det
        const m3 = ((c.sy - a.sy) * x1 - (b.sy - a.sy) * x2) / det
        pattern.setTransform(new DOMMatrix([m0, m1, m2, m3, a.sx - m0 * x0 - m2 * y0, a.sy - m1 * x0 - m3 * y0]))
        ctx.fillStyle = pattern
        ctx.beginPath()
        ctx.moveTo(a.sx, a.sy); ctx.lineTo(b.sx, b.sy); ctx.lineTo(c.sx, c.sy); ctx.closePath()
        ctx.fill()
        continue
      }
    }
    const channel = (value: number) => Math.round(Math.max(0, Math.min(1, value * light)) * 255)
    ctx.fillStyle = `rgb(${channel(p.r)},${channel(p.g)},${channel(p.b)})`
    ctx.beginPath()
    ctx.moveTo(a.sx, a.sy); ctx.lineTo(b.sx, b.sy); ctx.lineTo(c.sx, c.sy); ctx.closePath()
    ctx.fill()
  }
  ctx.restore()
  return triangles.length
}
