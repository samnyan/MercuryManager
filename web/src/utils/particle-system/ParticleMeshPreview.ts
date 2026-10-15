import type { ParticleMeshGeometry } from './ParticleMeshGeometry'
import type { SimParticle } from './ParticleRuntime'
import { transformMeshVertex } from './ParticleMeshWireframe'

/** APPROXIMATION: real mesh triangles, particle color, fixed preview lighting.
 * Not native material execution: no material textures, reflections, shadows or
 * depth buffer. Painter sorting and whole-triangle near rejection are approximate.
 * XYZ Euler transform is shared with the diagnostic renderer, not UE factory parity.
 */
export function drawParticleMeshPreview(
  ctx: CanvasRenderingContext2D, mesh: ParticleMeshGeometry, p: SimParticle,
  project: (x: number, y: number, z: number) => { sx: number; sy: number; depth: number } | null
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
      triangles.push({ a, b, c, depth: (a.depth + b.depth + c.depth) / 3, light })
    }
  }
  triangles.sort((a, b) => b.depth - a.depth)
  ctx.save()
  ctx.globalCompositeOperation = p.blendMode.includes('Additive') ? 'lighter' : 'source-over'
  ctx.globalAlpha = Math.max(0, Math.min(1, p.a))
  for (const { a, b, c, light } of triangles) {
    const channel = (value: number) => Math.round(Math.max(0, Math.min(1, value * light)) * 255)
    ctx.fillStyle = `rgb(${channel(p.r)},${channel(p.g)},${channel(p.b)})`
    ctx.beginPath()
    ctx.moveTo(a.sx, a.sy); ctx.lineTo(b.sx, b.sy); ctx.lineTo(c.sx, c.sy); ctx.closePath()
    ctx.fill()
  }
  ctx.restore()
  return triangles.length
}
