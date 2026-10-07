import { defineStore } from 'pinia'
import { ref } from 'vue'
export interface Field { name: string; type: string; value: string | number | boolean | null; readOnly: boolean }
export interface Row { rowName: string; fields: Field[] }
export interface ApiResult<T> { code: number; message: string; data: T }
export class ApiError extends Error {
  constructor(message: string, public readonly code: number, public readonly status: number) { super(message); this.name = 'ApiError' }
}
export async function api<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  let response: Response
  try { response = await fetch('/api' + path, { method, headers: { 'Content-Type': 'application/json', 'X-Mercury-Local': '1' }, body: body === undefined ? undefined : JSON.stringify(body) }) }
  catch { throw new ApiError('无法连接服务器，请检查网络或服务是否已启动', -1, 0) }
  const text = await response.text()
  let result: ApiResult<T>
  try { result = JSON.parse(text) }
  catch { throw new ApiError(`服务器响应不是有效 JSON（HTTP ${response.status}），请检查服务日志`, -2, response.status) }
  if (!result || typeof result.code !== 'number' || typeof result.message !== 'string' || !('data' in result))
    throw new ApiError('服务器响应格式不符合 ApiResult，请刷新页面或检查服务版本', -2, response.status)
  if (!response.ok || result.code !== 0) throw new ApiError(result.message || `HTTP ${response.status}`, result.code, response.status)
  return result.data
}
export const useProject = defineStore('project', () => {
  const id = ref(''), name=ref(''), dirty=ref(false), pending=ref(false), saved=ref(false), revision=ref(0)
  async function status(){const p=await api<{name:string;contentRoot:string;dirty:boolean;saved:boolean}>(`/projects/${id.value}`);name.value=p.name;contentRoot.value=p.contentRoot??'';dirty.value=p.dirty;saved.value=p.saved}
  async function create(label:string){const p=await api<{id:string}>('/projects','POST',{name:label});id.value=p.id;rows.value=[];await status();revision.value++}
  async function importTables(path:string){await api(`/projects/${id.value}/import`,'POST',{serverPath:path});await status();await refresh();revision.value++}
  async function save(){await api(`/projects/${id.value}/save`,'POST');await status()}
  const rows = ref<Row[]>([])
  const contentRoot = ref('')
  function close() { pending.value=false; id.value = ''; rows.value = []; contentRoot.value = ''; localStorage.removeItem('mercury-workspace') }
  async function open(path: string) {
    const workspace = await api<{id:string;contentRoot:string}>('/workspaces', 'POST', {serverPath:path})
    const loadedRows = await api<Row[]>(`/workspaces/${workspace.id}/music`)
    id.value = workspace.id
    contentRoot.value = workspace.contentRoot
    rows.value = loadedRows
    localStorage.setItem('mercury-workspace', workspace.id)
  }
  async function resume(workspaceId: string) {
    const workspace = await api<{contentRoot?:string}>('/projects/' + encodeURIComponent(workspaceId))
    const loadedRows = workspace.contentRoot ? await api<Row[]>(`/workspaces/${encodeURIComponent(workspaceId)}/music`) : []
    id.value = workspaceId
    await status();revision.value++
    contentRoot.value = workspace.contentRoot ?? ''
    rows.value = loadedRows
    localStorage.setItem('mercury-workspace', workspaceId)
  }
  async function refresh() { rows.value = await api<Row[]>(`/workspaces/${id.value}/music`) }
  return { id, name, dirty, pending, saved, revision, rows, contentRoot, close, open, resume, refresh, status, create, importTables, save }
})
