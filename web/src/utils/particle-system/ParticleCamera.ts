export interface ParticleBoundsItem { x: number; y: number; z: number; sizeX: number; sizeY: number; sizeZ: number }
/** Frame simulation bounds without modifying authored positions or scale. Mesh bounds remain approximate. */
export function fitParticleCamera<T extends ParticleBoundsItem>(items: readonly T[], meshRadius?: (item: T) => number | undefined) {
  const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity]
  let count = 0
  for (const p of items) {
    const position = [p.x,p.y,p.z], size = [p.sizeX,p.sizeY,p.sizeZ]
    const radius = meshRadius?.(p)
    if (radius !== undefined && Number.isFinite(radius) && radius >= 0) {
      const extent = radius * Math.max(...size.map(Math.abs))
      size.fill(extent)
    }
    if (![...position,...size].every(Number.isFinite)) continue
    count++
    for (let i=0;i<3;i++) { min[i]=Math.min(min[i]!,position[i]!-Math.abs(size[i]!)); max[i]=Math.max(max[i]!,position[i]!+Math.abs(size[i]!)) }
  }
  if (!count) return null
  const target = min.map((v,i)=>v+(max[i]!-v)/2) as [number,number,number]
  const radius = Math.hypot(...max.map((v,i)=>(v-min[i]!)/2))
  return { target, distance: Math.max(100, radius*4) }
}
