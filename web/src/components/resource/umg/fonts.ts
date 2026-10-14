const fontSet = document.fonts as FontFaceSet & { add(face: FontFace): void; delete(face: FontFace): boolean }
const families = new Map<string, Promise<string>>()
let serial = 0
/** Load actual cooked font files, including composite character-range overrides. */
export function gameFont(project: string, font: any): Promise<string> {
  const path = font?.FontObject?.assetPath
  if (!path) return Promise.resolve('sans-serif')
  const key = `${project}|${path}|${font.TypefaceFontName ?? 'Default'}`
  let task = families.get(key)
  if (task) return task
  task = (async () => {
    const family = `UMGFont${++serial}`
    const response = await fetch(`/api/projects/${project}/resource-font?` + new URLSearchParams({path}))
    if (!response.ok) throw new Error(`Font metadata: ${response.status}`)
    const json = await response.json(), composite = (json.data ?? json).properties.CompositeFont
    const added: FontFace[] = []
    const load = async (typeface: any, unicodeRange?: string) => {
      const entries = typeface?.Fonts ?? []
      const entry = entries.find((v:any) => v.Name === font.TypefaceFontName) ?? entries[0]
      if (!entry?.Font?.assetPath) return
      if (entry.Font.subFaceIndex) throw new Error('Nonzero font collection face is not supported')
      const url = `/api/projects/${project}/resource-font-face?` + new URLSearchParams({path: entry.Font.assetPath})
      const face = new FontFace(family, `url("${url}")`, unicodeRange ? {unicodeRange} : {})
      await face.load(); fontSet.add(face); added.push(face)
    }
    try {
      await load(composite.FallbackTypeface?.Typeface)
      await load(composite.DefaultTypeface)
      for (const sub of composite.SubTypefaces ?? []) {
        if (sub.Cultures || (sub.ScalingFactor != null && sub.ScalingFactor !== 1)) continue
        const ranges = (sub.CharacterRanges ?? []).map((r:any) => {
          const lo = r.LowerBound.Type === 'Open' ? 0 : r.LowerBound.Value + (r.LowerBound.Type === 'Exclusive' ? 1 : 0)
          const hi = r.UpperBound.Type === 'Open' ? 0x10ffff : r.UpperBound.Value - (r.UpperBound.Type === 'Exclusive' ? 1 : 0)
          return `U+${lo.toString(16)}-${hi.toString(16)}`
        }).join(',')
        if (ranges) await load(sub.Typeface, ranges)
      }
      if (!added.length) throw new Error('No usable font face')
      return `${family}, sans-serif`
    } catch (error) { for (const face of added) fontSet.delete(face); throw error }
  })().catch(error => { console.warn('UMG game font unavailable; browser fallback:', path, error); return 'sans-serif' })
  families.set(key, task)
  return task
}
