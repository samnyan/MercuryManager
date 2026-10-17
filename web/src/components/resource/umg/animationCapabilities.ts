import type { UMGAnimation, UMGNode } from './types'

export function supportsFloatProperty(property: string | null | undefined, className?: string): boolean {
  if (property === 'RenderOpacity') return true
  if (className === 'ScaleBox') return property === 'UserSpecifiedScale'
  return className === 'SizeBox' && ['WidthOverride', 'HeightOverride', 'MinDesiredWidth', 'MinDesiredHeight', 'MaxDesiredWidth', 'MaxDesiredHeight'].includes(property ?? '')
}

export function scalarTrackChannel(type: string): string | null {
  if (type === 'MovieSceneFloatTrack') return 'FloatCurve'
  if (type === 'MovieSceneDoubleTrack') return 'DoubleCurve'
  return null
}

type Track = UMGAnimation['tracks'][number]
type Binding = UMGAnimation['bindings'][number]

// Keep aligned with animationApply.ts and the scalar material preview path.
// This reports preview capability, not whether the package was deserialized.
export function animationTrackIssue(track: Track, binding?: Binding, node?: UMGNode): string | null {
  if (!binding?.nodeId) return 'Target is unresolved'
  const slot = !!binding.slotWidgetName && binding.slotWidgetName !== 'None'
  if (slot) {
    if (track.type.includes('Margin') && ['Offsets', 'Padding'].includes(track.property ?? '')) return null
    return 'Panel-slot property is not supported by the preview'
  }
  if (track.materialParameters?.length) {
    return 'Material parameter preview is scalar-only; vector/color and parameter association parity is not established'
  }
  if (track.type === 'MovieScene2DTransformTrack' && track.property === 'RenderTransform') return null
  if (scalarTrackChannel(track.type) && supportsFloatProperty(track.property, node?.className)) return null
  if (track.type.includes('Color') && ['ColorAndOpacity', 'ContentColorAndOpacity', 'BrushColor', 'Color'].includes(track.property ?? '')) return null
  return 'Property track is parsed but not applied by the preview'
}
