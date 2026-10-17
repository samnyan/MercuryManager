import type { UMGNode, UMGAnimation } from './types'
import { sampleChannel } from './animationSampling'
import { supportsFloatProperty, scalarTrackChannel } from './animationCapabilities'

export function applyAnimation(nodes: UMGNode[], boundTracks: { track: UMGAnimation['tracks'][number], node: UMGNode, slotBinding?: boolean }[], time: number): UMGNode[] {
  // Preserve every nested tree and the original layout/draw order. Visibility is a separate mask.
  const clones = new Map<string, UMGNode>()
  for (const { node } of boundTracks) {
    if (clones.has(node.id)) continue
    clones.set(node.id, {
    ...node,
    slot: node.slot ? { ...node.slot, values: { ...node.slot.values, LayoutData: { ...node.slot.values.LayoutData, Offsets: { ...node.slot.values.LayoutData?.Offsets } } } } : node.slot,
    visual: {
      ...node.visual,
      RenderTransform: node.visual.RenderTransform ? {
        ...node.visual.RenderTransform,
        Translation: { ...node.visual.RenderTransform.Translation },
        Scale: { ...node.visual.RenderTransform.Scale },
        Shear: { ...node.visual.RenderTransform.Shear }
      } : undefined
    }
    })
  }
  for(const {track, node: source, slotBinding} of boundTracks){
    const node=clones.get(source.id)
    if(!node) continue
    const values=new Map(track.channels.map(c=>[c.property,sampleChannel(c,time)]))
    if (slotBinding && !(track.type.includes('Margin') && ['Offsets', 'Padding'].includes(track.property ?? '') && node.slot)) continue
    if(track.type === 'MovieScene2DTransformTrack' && track.property === 'RenderTransform'){
      const axis=(name:string,index:number)=>values.get(`${name}.${index}`)??values.get(`${name}${index===0?'X':'Y'}`)??null
      const translation={X:axis('Translation',0)??node.visual.RenderTransform?.Translation?.X??0,Y:axis('Translation',1)??node.visual.RenderTransform?.Translation?.Y??0}
      const scale={X:axis('Scale',0)??node.visual.RenderTransform?.Scale?.X??1,Y:axis('Scale',1)??node.visual.RenderTransform?.Scale?.Y??1}
      const shear={X:axis('Shear',0)??node.visual.RenderTransform?.Shear?.X??0,Y:axis('Shear',1)??node.visual.RenderTransform?.Shear?.Y??0}
      node.visual.RenderTransform={...(node.visual.RenderTransform||{}),Translation:translation,Scale:scale,Shear:shear,Angle:values.get('Rotation.0')??values.get('Rotation')??node.visual.RenderTransform?.Angle??0}
    } else if(scalarTrackChannel(track.type) && supportsFloatProperty(track.property, node.className)) {
      const value = values.get(scalarTrackChannel(track.type)!)
      if (value != null && track.property) node.visual[track.property] = value
    }
    else if(track.type.includes('Margin') && track.property==='Offsets' && node.slot) {
      const data = node.slot.values.LayoutData ??= {}
      const offsets = data.Offsets ??= {}
      for (const side of ['Left','Top','Right','Bottom']) {
        const value = values.get(`${side}Curve`)
        if (value != null) offsets[side] = value
      }
    } else if(track.type.includes('Margin') && track.property==='Padding' && slotBinding && node.slot) {
      const padding = { ...(node.slot.values.Padding ?? {}) }
      for (const side of ['Left','Top','Right','Bottom']) {
        const value = values.get(`${side}Curve`)
        if (value != null) padding[side] = value
      }
      node.slot.values.Padding = padding
    } else if(track.type.includes('Color')) {
      const property = track.property
      if (!property || !['ColorAndOpacity','ContentColorAndOpacity','BrushColor','Color'].includes(property)) continue
      const original = node.visual[property] ?? {}
      const color = { ...(original.SpecifiedColor ?? original) }
      for (const [channel, component] of [['RedCurve','R'],['GreenCurve','G'],['BlueCurve','B'],['AlphaCurve','A']]) {
        const value = values.get(channel)
        if (value != null) color[component] = value
      }
      node.visual[property] = original.SpecifiedColor ? { ...original, SpecifiedColor: color } : color
    }
  }
  return nodes.map(node => clones.get(node.id) ?? node)
}
