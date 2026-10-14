import type { FabricObject } from 'fabric'

type ClipPoint = { x: number; y: number }
const clips = new WeakMap<FabricObject, ClipPoint[]>()

/** Clip in scene coordinates, without Fabric's temporary mask canvases. */
export function setRetainedClip(object: FabricObject, points: ClipPoint[]) {
  if (!clips.has(object)) {
    const render = object.render
    object.render = function (ctx: CanvasRenderingContext2D) {
      const polygon = clips.get(this)
      if (!polygon?.length) return
      ctx.save()
      try {
        ctx.beginPath()
        polygon.forEach((p, i) => i ? ctx.lineTo(p.x, p.y) : ctx.moveTo(p.x, p.y))
        ctx.closePath()
        ctx.clip()
        render.call(this, ctx)
      } finally { ctx.restore() }
    }
  }
  clips.set(object, points)
  object.clipPath = undefined
}
