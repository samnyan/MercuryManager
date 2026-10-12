// Shared UMG/material boundary. Binding provenance must be explicit, never inferred from file names.
export type Fidelity = 'approximate' | 'verified-translation'
export interface MaterialTextureSlot {
  samplerName: string
  assetPath: string
  slot: number
  provenance: 'shader-reflection' | 'material-parameter' | 'manual'
}
export interface MaterialFrameRequest {
  assetPath: string
  revision: string
  width: number
  height: number
  timeSeconds: number
  scalars: Record<string, number>
  vectors: Record<string, { r: number; g: number; b: number; a: number }>
  textures: MaterialTextureSlot[]
}
export interface MaterialFrameResult {
  source: HTMLCanvasElement | ImageBitmap
  fidelity: Fidelity
  diagnostics: string[]
}
export interface MaterialRenderer {
  render(request: MaterialFrameRequest, signal?: AbortSignal): Promise<MaterialFrameResult>
  dispose(): void
}
export function validateMaterialFrame(request: MaterialFrameRequest): string[] {
  const errors: string[] = []
  if (!request.assetPath || !request.revision) errors.push('Material identity and revision are required.')
  for (const dimension of [request.width, request.height]) {
    if (!Number.isInteger(dimension) || dimension < 1 || dimension > 4096) errors.push('Frame dimensions must be integers between 1 and 4096.')
  }
  if (!Number.isFinite(request.timeSeconds) || request.timeSeconds < 0) errors.push('Time must be finite and non-negative.')
  for (const [name, value] of Object.entries(request.scalars)) if (!Number.isFinite(value)) errors.push(`Non-finite scalar: ${name}`)
  for (const [name, value] of Object.entries(request.vectors)) if (![value.r,value.g,value.b,value.a].every(Number.isFinite)) errors.push(`Non-finite vector: ${name}`)
  const samplers = new Set<string>(), slots = new Set<number>()
  for (const binding of request.textures) {
    if (!binding.samplerName || samplers.has(binding.samplerName)) errors.push(`Missing or duplicate sampler: ${binding.samplerName}`)
    if (!Number.isInteger(binding.slot) || binding.slot < 0 || slots.has(binding.slot)) errors.push(`Invalid or duplicate texture slot: ${binding.slot}`)
    if (!binding.assetPath || binding.assetPath.startsWith('/') || binding.assetPath.includes('\\') || binding.assetPath.split('/').some(p=>p==='..'||p==='.'||!p)) errors.push(`Invalid Content-relative texture path: ${binding.assetPath}`)
    samplers.add(binding.samplerName); slots.add(binding.slot)
  }
  return errors
}
