import { normalizeMaterialEntry } from '../material-shader/UMGMaterialBridge'

/** APPROXIMATION: bounded material families, not native shader permutations.
 * Texture color-space and scene-dependent parameters remain preview approximations.
 * Resolve by actual parent identity, never by instance suffix or parameter guessing.
 */
export interface ParticleMaterialCompatibility {
  mode: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10
  rank?: number[]
  miss?: number[]
  hold?: number[]
  tap?: number[]
  secondary?: HTMLCanvasElement
  tiling: number[]
  strength: number
  colors: number[][]
  threshold: number
}
const contracts = new WeakMap<HTMLCanvasElement, ParticleMaterialCompatibility>()
export function particleMaterialCompatibility(surface?: HTMLCanvasElement) { return surface ? contracts.get(surface) : undefined }

export async function createParticleMaterialCompatibility(raw: any, path: string,
  loadImage: (path: string) => Promise<HTMLImageElement | null>): Promise<HTMLCanvasElement | null> {
  const parent = raw?.Type === 'Material' ? raw.MaterialInfo : raw?.MaterialInstanceInfo?.ParentMaterialInfo
  const modes: Record<string, ParticleMaterialCompatibility['mode']> = { vM_1_Base_004: 1, vM_1_Base_000: 2, vM_1_Base_005: 3, vM_2_Scroll_003: 4, vM_1_Base_003: 5, vM_2_Dslv_003: 6, vMX_2_Tap_000: 7, vMX_2_Hold_000: 8, vMX_1_Miss_000: 9, vMX_3_RankUp_000: 10 }
  const mode = modes[parent?.Name]
  if (!mode) return null
  const spec = normalizeMaterialEntry(raw, path)
  // Resolve slots independently: two parameters may intentionally reference one asset.
  const texturePath = (name: string): string | undefined => {
    const overrides = raw?.MaterialInstanceInfo?.TextureOverrides
    return overrides?.find((t: any) => t.ParameterName === name)?.ContentFile
      ?? parent?.TextureNodes?.find((t: any) => t.ParameterName === name)?.ContentFile
  }
  const primarySlots: Record<ParticleMaterialCompatibility['mode'], string> = {
    1: 'Color_texture', 2: 'texture', 3: 'Color_texture', 4: 'Color_texture',
    5: 'Color_texture', 6: 'Color', 7: 'ColorTexture', 8: '', 9: 'Color_texture', 10: 'Color_Texture',
  }
  const slot = texturePath(primarySlots[mode])
  if (!spec || (!slot && mode !== 8)) return null
  // APPROXIMATION: NoteHeight collection fixed at 0.5; use its authored length knot.
  const tap = mode === 7 ? ['NoteLength_0.5','ColorTex_pos','ColorTexture_ONOFF','btm_Mask_Contrast','Btmmask_OnOFF','GlowSub_Gamma','GlowCore_Gamma','LinePos','LineLength_Change_ONOFF'].map(name => spec.scalars[name]!) : undefined
  if (tap && !tap.every(Number.isFinite)) return null
  // APPROXIMATION: procedural Hold glow uses preview NoteHeight=0.5, not game collections.
  const holdNames = ['CoreGlowPos_NoteMin','CoreGlowPos_NoteMax','CoreGlow_Min','CoreGlow_Max',
    'CoreGlowMskPosV_NoteMin','CoreGlowMskPosV_NoteMax','CoreGlowRange_Min','CoreGlowRange_Max',
    'CoreGlow_Strength','ColorBalance','LineGlow_Pos','LineGlow_Min','LineGlow_Max','LineGow_Strength',
    'U_pos','Rotate','FlickCycle_Speed','FlickStrength_Min','FlickStrength_Max']
  const hold = mode === 8 ? holdNames.map(name => spec.scalars[name]!) : undefined
  if (hold) {
    for (const name of ['CoreGlow_Color','SideGlow_Color']) {
      const c = spec.vectors[name]
      if (!c) return null
      hold.push(c.r,c.g,c.b)
    }
    if (!hold.every(Number.isFinite) || hold[2] === hold[3] || hold[6] === hold[7] || hold[11] === hold[12]) return null
  }
  // APPROXIMATION: BG_Mask collection fixed to zero.
  const rank = mode === 10 ? ['BG_Mask_0','ColorTex_ONOFF','Rotate','1_UV_Tiling','WaveInside_Density','SmoothStep_Min','SmoothStep_Max','Shape_Min','Shape_Max'].map(name => spec.scalars[name]!) : undefined
  if (rank && (!rank.every(Number.isFinite) || rank[3]! <= 0 || rank[5] === rank[6])) return null
  const miss = mode === 9 ? ['outside_scale','outside_Density','inside_Density'].map(name => spec.scalars[name]!) : undefined
  if (miss) {
    for (const name of ['RandomColorA','RandomColorB','Line_Color','Base_Color']) {
      const c = spec.vectors[name]
      if (!c) return null
      miss.push(c.r,c.g,c.b)
    }
    if (!miss.every(Number.isFinite) || miss[0]! <= 0) return null
  }
  const colors = ['Color1', 'Color2', 'Color3'].map(name => {
    const c = spec.vectors[name]; return c ? [c.r, c.g, c.b] : []
  })
  if (mode === 3 && (colors.some(c => c.length !== 3) || !Number.isFinite(spec.scalars.Ramdom))) return null
  const tiling = [1,1,1,1]
  let strength = 1
  if (mode === 5) {
    const color = spec.vectors.Color_Tiling, mask = spec.vectors.Mask_Tiling
    if (!color || !mask || !Number.isFinite(spec.scalars.BG_Mask_0)) return null
    tiling.splice(0,4,color.r,color.g,mask.r,mask.g)
    // APPROXIMATION: collection BG_Mask fixed to zero, selecting authored BG_Mask_0.
    strength = spec.scalars.BG_Mask_0!
  }
  if (mode === 6) {
    if (!Number.isFinite(spec.scalars.ColorPower)) return null
    strength = spec.scalars.ColorPower!
  }
  const load = async (path: string) => {
    const image = await loadImage(path)
    if (!image?.naturalWidth || !image.naturalHeight) return null
    const c = document.createElement('canvas'); c.width = image.naturalWidth; c.height = image.naturalHeight
    c.getContext('2d')!.drawImage(image,0,0)
    return c
  }
  let secondary: HTMLCanvasElement | undefined
  if (mode === 5 || mode === 6 || mode === 9 || mode === 10) {
    const maskPath = texturePath(mode === 10 ? 'Texture' : mode === 9 ? 'TextureObject' : mode === 6 ? 'DslvTex' : 'Mask_texture')
    if (!maskPath) return null
    secondary = await load(maskPath) ?? undefined
    if (!secondary) return null
  }
  const canvas = mode === 8 ? document.createElement('canvas') : await load(slot!)
  if (!canvas) return null
  // Procedural material has no texture; 1x1 storage only keeps shared samplers complete.
  if (mode === 8) { canvas.width = 1; canvas.height = 1 }
  canvas.dataset.previewProfile = 'approximate-particle-family'
  contracts.set(canvas, { mode, tap, hold, miss, rank, colors, secondary, tiling, strength, threshold: spec.scalars.Ramdom ?? 0.5 })
  return canvas
}
