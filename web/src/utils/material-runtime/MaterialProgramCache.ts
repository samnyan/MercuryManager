import type { MaterialShaderContract } from '../material-shader/MaterialWebGLRenderer'

/** Bounded, project-scoped immutable contracts. Invalidate on asset writes/reimport. */
class MaterialProgramCache {
  private entries = new Map<string, Promise<MaterialShaderContract>>()
  load(project: string, path: string): Promise<MaterialShaderContract> {
    const key = JSON.stringify([project, path])
    const existing = this.entries.get(key)
    if (existing) return existing
    const task = (async () => {
      const response = await fetch(`/api/projects/${project}/resource-material-shader?` + new URLSearchParams({ path }))
      const result = await response.json()
      if (!response.ok || result.code !== 0 || !result.data) throw new Error(result.message || `HTTP ${response.status}`)
      return result.data as MaterialShaderContract
    })()
    if (this.entries.size >= 64) this.entries.delete(this.entries.keys().next().value!)
    this.entries.set(key, task)
    task.catch(() => { if (this.entries.get(key) === task) this.entries.delete(key) })
    return task
  }
  invalidate(project?: string) {
    for (const key of this.entries.keys()) if (!project || JSON.parse(key)[0] === project) this.entries.delete(key)
  }
}
export const materialPrograms = new MaterialProgramCache()
