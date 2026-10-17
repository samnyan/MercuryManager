export interface AnimationKey {
  frame: number; value: number; interpolation: string; arriveTangent: number; leaveTangent: number
  tangentWeightMode?: string; arriveTangentWeight?: number; leaveTangentWeight?: number; tangentWeightTimeScale?: number
}
export interface AnimationChannel { defaultValue: number; hasDefault: boolean; keys: AnimationKey[] }
const bezier = (a: number, b: number, c: number, d: number, t: number) => {
  const s = 1 - t
  return s*s*s*a + 3*s*s*t*b + 3*s*t*t*c + t*t*t*d
}
// Split at stationary points so even handles extending outside the segment can be inverted.
function solveTime(x1: number, x2: number, x: number): number {
  const a = 3*x1 - 3*x2 + 1, b = 2*(x2 - 2*x1), c = x1
  const cuts = [0, 1]
  if (Math.abs(a) < 1e-12) {
    if (Math.abs(b) > 1e-12) cuts.push(-c/b)
  } else {
    const disc = b*b - 4*a*c
    if (disc >= 0) cuts.push((-b-Math.sqrt(disc))/(2*a), (-b+Math.sqrt(disc))/(2*a))
  }
  const points = cuts.filter(t => t >= 0 && t <= 1).sort((a,b) => a-b)
  const roots: number[] = []
  for (let i=1;i<points.length;i++) {
    let lo=points[i-1], hi=points[i]
    const increasing=bezier(0,x1,x2,1,hi)>=bezier(0,x1,x2,1,lo)
    if (x < Math.min(bezier(0,x1,x2,1,lo),bezier(0,x1,x2,1,hi))-1e-12 || x > Math.max(bezier(0,x1,x2,1,lo),bezier(0,x1,x2,1,hi))+1e-12) continue
    for(let n=0;n<48;n++) {
      const mid=(lo+hi)/2
      if ((bezier(0,x1,x2,1,mid)<x)===increasing) lo=mid; else hi=mid
    }
    roots.push((lo+hi)/2)
  }
  // UE selects the largest valid root when time has multiple solutions.
  return roots.length ? Math.max(...roots) : x
}
function handle(key: AnimationKey, side: 'arrive'|'leave', duration: number): [number,number] {
  const slope = side==='arrive'?key.arriveTangent:key.leaveTangent
  const mode=key.tangentWeightMode??''
  const weighted=mode.includes('WeightedBoth') || mode.includes(side==='arrive'?'WeightedArrive':'WeightedLeave')
  if (!weighted) return [duration/3,slope*duration/3]
  const scale=key.tangentWeightTimeScale??1
  const weight=(side==='arrive'?key.arriveTangentWeight:key.leaveTangentWeight)??0
  // Weight is a length in the source time/value plane, not in seconds/value.
  const angle=Math.atan(slope*scale)
  return [Math.cos(angle)*weight*scale,Math.sin(angle)*weight]
}
export function sampleChannel(channel: AnimationChannel, time: number): number|null {
  const keys=channel.keys
  if(!keys.length) return channel.hasDefault?channel.defaultValue:null
  // Lower bound preserves the first key at an exact timestamp, including duplicates.
  let lo=0, hi=keys.length
  while(lo<hi) {
    const mid=lo+Math.floor((hi-lo)/2)
    if(keys[mid].frame<time) lo=mid+1; else hi=mid
  }
  const right=lo
  if(right===keys.length) return keys[keys.length-1].value
  if(right===0 || keys[right].frame===time) return keys[right].value
  const left=keys[right-1],next=keys[right],duration=next.frame-left.frame
  if(duration<=0) return next.value
  if(left.interpolation.toLowerCase().includes('constant')) return left.value
  const t=(time-left.frame)/duration
  if(left.interpolation.toLowerCase().includes('cubic')) {
    const [lx,ly]=handle(left,'leave',duration),[rx,ry]=handle(next,'arrive',duration)
    const weighted=(left.tangentWeightMode??'').match(/Weighted(Both|Leave)/) || (next.tangentWeightMode??'').match(/Weighted(Both|Arrive)/)
    const u=weighted?solveTime(lx/duration,1-rx/duration,t):t
    return bezier(left.value,left.value+ly,next.value-ry,next.value,u)
  }
  return left.value+(next.value-left.value)*t
}
