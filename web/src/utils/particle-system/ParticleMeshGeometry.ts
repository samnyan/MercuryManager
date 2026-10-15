export interface ParticleMeshGeometry {
  positions: number[]
  texCoords: number[][]
  indices: number[]
  sections: { materialIndex: number; firstIndex: number; numTriangles: number }[]
  tangentX: number[]
  tangentZ: number[]
}

/** Origin-centred sphere remains conservative under arbitrary particle rotation. */
export function meshOriginRadius(mesh: ParticleMeshGeometry): number {
  const positions = mesh.positions
  if (!Array.isArray(positions) || !positions.length || positions.length % 3 || !positions.every(Number.isFinite)) {
    throw new Error('Invalid mesh positions')
  }
  let radius = 0
  for (let i = 0; i < positions.length; i += 3) {
    radius = Math.max(radius, Math.hypot(positions[i]!, positions[i + 1]!, positions[i + 2]!))
  }
  return radius
}

export async function loadParticleMesh(projectId: string, path: string, signal: AbortSignal): Promise<ParticleMeshGeometry> {
  const response = await fetch(`/api/projects/${encodeURIComponent(projectId)}/resource-mesh-geometry?${new URLSearchParams({ path })}`, { signal })
  const result = await response.json() as { code: number; message: string; data: ParticleMeshGeometry }
  if (!result || typeof result.code !== 'number' || typeof result.message !== 'string' || !('data' in result)) {
    throw new Error(`Invalid mesh API response: ${path}`)
  }
  if (!response.ok || result.code !== 0) throw new Error(`${result.message || `HTTP ${response.status}`}: ${path}`)
  const mesh = result.data
  if (!mesh) throw new Error(`Missing mesh geometry: ${path}`)
  meshOriginRadius(mesh)
  return mesh
}
