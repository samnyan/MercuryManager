/** Slate draw colors are bounded vertex colors, not HDR material parameters. */
export type SlateColor = [number, number, number, number]
type SerializedColor = { R?: number; G?: number; B?: number; A?: number; SpecifiedColor?: SerializedColor } | null | undefined

function component(value: number | undefined): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 1
}

export function clampSlateColor(color: readonly number[]): SlateColor {
  return [0, 1, 2, 3].map(i => Math.max(0, Math.min(1, component(color[i])))) as SlateColor
}

/** Multiply before clamping: HDR widget values can be attenuated by brush tint. */
export function multiplySlateColors(...colors: SerializedColor[]): SlateColor {
  const result: SlateColor = [1, 1, 1, 1]
  for (const raw of colors) {
    const c = raw?.SpecifiedColor ?? raw
    for (const [i, key] of (['R', 'G', 'B', 'A'] as const).entries()) result[i]! *= component(c?.[key])
  }
  return clampSlateColor(result)
}

export function linearToSrgbByte(value: number): number {
  const v = Math.max(0, Math.min(1, component(value)))
  return Math.round(255 * (v <= 0.0031308 ? 12.92 * v : 1.055 * Math.pow(v, 1 / 2.4) - 0.055))
}
