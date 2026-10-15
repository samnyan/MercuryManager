import type { ParticleMeshGeometry } from './ParticleMeshGeometry'
import type { SimParticle } from './ParticleRuntime'

/** Diagnostic XYZ Euler transform only; not a verified UE mesh-factory transform. */
export function transformMeshVertex(x: number, y: number, z: number, p: SimParticle): [number, number, number] {
  // APPROXIMATION: apply TypeData mesh-local orientation before particle scale/rotation.
  // Keep matrix composition separate: Euler angles cannot simply be added.
  if (p.meshPreRotation) {
    const [rx, ry, rz] = p.meshPreRotation
    const y1 = y * Math.cos(rx) - z * Math.sin(rx), z1 = y * Math.sin(rx) + z * Math.cos(rx)
    const x2 = x * Math.cos(ry) + z1 * Math.sin(ry), z2 = -x * Math.sin(ry) + z1 * Math.cos(ry)
    x = x2 * Math.cos(rz) - y1 * Math.sin(rz)
    y = x2 * Math.sin(rz) + y1 * Math.cos(rz)
    z = z2
  }
  x *= p.sizeX; y *= p.sizeY; z *= p.sizeZ
  const cx = Math.cos(p.rotX), sx = Math.sin(p.rotX)
  const cy = Math.cos(p.rotY), sy = Math.sin(p.rotY)
  const cz = Math.cos(p.rotZ), sz = Math.sin(p.rotZ)
  const y1 = y * cx - z * sx, z1 = y * sx + z * cx
  const x2 = x * cy + z1 * sy, z2 = -x * sy + z1 * cy
  return [x2 * cz - y1 * sz + p.x, x2 * sz + y1 * cz + p.y, z2 + p.z]
}

export function drawParticleMeshWireframe(
  ctx: CanvasRenderingContext2D, mesh: ParticleMeshGeometry, p: SimParticle,
  project: (x: number, y: number, z: number) => { sx: number; sy: number } | null
): number {
  const points = []
  for (let i = 0; i < mesh.positions.length; i += 3) {
    points.push(project(...transformMeshVertex(mesh.positions[i]!, mesh.positions[i + 1]!, mesh.positions[i + 2]!, p)))
  }
  let triangles = 0
  ctx.save()
  ctx.strokeStyle = `rgba(${Math.round(p.r * 255)},${Math.round(p.g * 255)},${Math.round(p.b * 255)},${Math.max(0, Math.min(1, p.a))})`
  ctx.lineWidth = 1
  ctx.beginPath()
  for (const section of mesh.sections) {
    for (let i = section.firstIndex; i < section.firstIndex + section.numTriangles * 3; i += 3) {
      const a = points[mesh.indices[i]!], b = points[mesh.indices[i + 1]!], c = points[mesh.indices[i + 2]!]
      // Conservative near-plane rejection; no claim of hardware clipping parity.
      if (!a || !b || !c) continue
      ctx.moveTo(a.sx, a.sy); ctx.lineTo(b.sx, b.sy); ctx.lineTo(c.sx, c.sy); ctx.closePath()
      triangles++
    }
  }
  ctx.stroke()
  ctx.restore()
  return triangles
}
