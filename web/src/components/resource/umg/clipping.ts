import type { UMGNode, ComputedLayout } from './types'
import { polygon, type Matrix } from './transforms'
export type Vertex = { x: number; y: number }
export function intersect(subject: Vertex[], clip: Vertex[]): Vertex[] {
  const area = clip.reduce((s,p,i)=>{const q=clip[(i+1)%clip.length]!;return s+p.x*q.y-q.x*p.y},0)
  const side = (a:Vertex,b:Vertex,p:Vertex)=>(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x)
  let result=subject
  for(let i=0;i<clip.length;i++){
    const a=clip[i]!,b=clip[(i+1)%clip.length]!,input=result;result=[]
    if(!input.length)break
    let previous=input[input.length-1]!,pd=side(a,b,previous)*(area>=0?1:-1)
    for(const point of input){const d=side(a,b,point)*(area>=0?1:-1)
      if((d>=0)!==(pd>=0)){const t=pd/(pd-d);result.push({x:previous.x+t*(point.x-previous.x),y:previous.y+t*(point.y-previous.y)})}
      if(d>=0)result.push(point)
      previous=point;pd=d
    }
  }
  return result
}
export function widgetClip(node: UMGNode,nodes: Map<string,UMGNode>,layouts: Map<string,ComputedLayout>,transforms: Map<string,Matrix>,viewport:{width:number;height:number}): Vertex[] {
  let result=[{x:0,y:0},{x:viewport.width,y:0},{x:viewport.width,y:viewport.height},{x:0,y:viewport.height}]
  const chain: UMGNode[]=[];const seen=new Set<string>();let current:UMGNode|undefined=node
  while(current&&!seen.has(current.id)){seen.add(current.id);chain.unshift(current);current=current.parentId?nodes.get(current.parentId):undefined}
  for(const item of chain){
    const mode=String(item.visual.Clipping??'Inherit').split('::').pop()
    if(!['ClipToBounds','ClipToBoundsAlways'].includes(mode??''))continue
    const layout=layouts.get(item.id),matrix=transforms.get(item.id)
    if(!layout||!matrix)continue
    const points=polygon(matrix,layout.rect).split(' ').map(p=>{const [x,y]=p.split(',').map(Number);return {x:x!,y:y!}})
    result=intersect(result,points)
  }
  return result
}
