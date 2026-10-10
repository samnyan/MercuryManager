import { defineStore } from 'pinia'
import { ref } from 'vue'
import { tr } from './i18n'
export type FieldValue = string | number | boolean | null | unknown[] | Record<string, unknown>
export interface PropertyShape { type: string; element?: PropertyShape | null; fields?: Record<string, PropertyShape> | null }
export interface Field { name: string; type: string; value: FieldValue; readOnly: boolean; shape?: PropertyShape | null }
export interface Row { rowName: string; fields: Field[] }
export interface ApiResult<T> { code: number; message: string; data: T }
export class ApiError extends Error {
  constructor(message: string, public readonly code: number, public readonly status: number) { super(message); this.name = 'ApiError' }
}
export async function api<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  let response: Response
  try { response = await fetch('/api' + path, { method, headers: { 'Content-Type': 'application/json', 'X-Mercury-Local': '1' }, body: body === undefined ? undefined : JSON.stringify(body) }) }
  catch { throw new ApiError(tr('ui.networkError') || 'Network error', -1, 0) }
  const text = await response.text()
  let result: ApiResult<T>
  try { result = JSON.parse(text) }
  catch { throw new ApiError((tr('ui.invalidJsonResponse') || 'Invalid JSON response').replace('{status}', String(response.status)), -2, response.status) }
  if (!result || typeof result.code !== 'number' || typeof result.message !== 'string' || !('data' in result))
    throw new ApiError(tr('ui.invalidApiResult') || 'Invalid ApiResult format', -2, response.status)
  if (!response.ok || result.code !== 0) throw new ApiError(result.message || `HTTP ${response.status}`, result.code, response.status)
  return result.data
}
export const useProject = defineStore('project', () => {
  const id = ref(''), name=ref(''), dirty=ref(false), pending=ref(false), saved=ref(false), revision=ref(0), loading=ref(false)
  async function status(){const p=await api<{name:string;contentRoot:string;dirty:boolean;saved:boolean}>(`/projects/${id.value}`);name.value=p.name;contentRoot.value=p.contentRoot??'';dirty.value=p.dirty;saved.value=p.saved}
  async function create(label:string){loading.value=true; try{const p=await api<{id:string}>('/projects','POST',{name:label});id.value=p.id;rows.value=[];await status();revision.value++} finally {loading.value=false}}
  async function importTables(path:string){loading.value=true; try{await api(`/projects/${id.value}/import`,'POST',{serverPath:path});await status();await refresh();revision.value++} finally {loading.value=false}}
  async function save(){loading.value=true; try{await api(`/projects/${id.value}/save`,'POST');await status()} finally {loading.value=false}}
  const rows = ref<Row[]>([])
  const contentRoot = ref('')
  function close() { pending.value=false; id.value = ''; rows.value = []; contentRoot.value = ''; localStorage.removeItem('mercury-workspace') }
  async function open(path: string) {
    loading.value = true
    try {
      const workspace = await api<{id:string;contentRoot:string}>('/workspaces', 'POST', {serverPath:path})
      id.value = workspace.id
      contentRoot.value = workspace.contentRoot
      rows.value = []
      localStorage.setItem('mercury-workspace', workspace.id)
    } finally {
      loading.value = false
    }
  }
  async function resume(workspaceId: string) {
    loading.value = true
    try {
      id.value = workspaceId
      localStorage.setItem('mercury-workspace', workspaceId)
      await status()
      revision.value++
    } finally {
      loading.value = false
    }
  }
  async function refresh() { rows.value = await api<Row[]>(`/workspaces/${id.value}/music`) }
  return { id, name, dirty, pending, saved, revision, loading, rows, contentRoot, close, open, resume, refresh, status, create, importTables, save }
})
