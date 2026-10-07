import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type Row } from './project'

export interface ReferenceResult {
  status: 'idle' | 'loading' | 'found' | 'not_found'
  text?: string
}

function extractMessageText(row: Row): string {
  if (!row.fields || !row.fields.length) return ''
  const preferred = row.fields.find(f => /^(Message|Text|MessageText|Japanese|Content|Name)$/i.test(f.name))
  if (preferred && preferred.value != null && String(preferred.value).trim() !== '') {
    return String(preferred.value)
  }
  const strField = row.fields.find(f => f.type === 'StrPropertyData' && f.value != null && String(f.value).trim() !== '')
  if (strField) return String(strField.value)
  return row.fields[0]?.value != null ? String(row.fields[0].value) : ''
}

export const useReferenceStore = defineStore('reference', () => {
  const resolveEnabled = ref(true)
  const cache = ref<Record<string, Map<string, string>>>({})
  const loading = ref<Record<string, boolean>>({})
  const version = ref(0)

  async function loadMessage(workspaceId: string, messageName: string): Promise<void> {
    if (!workspaceId || !messageName) return
    if (cache.value[messageName] || loading.value[messageName]) return

    loading.value[messageName] = true
    try {
      const rows = await api<Row[]>(`/workspaces/${workspaceId}/messages/${encodeURIComponent(messageName)}`)
      const map = new Map<string, string>()
      for (const row of rows) {
        if (row.rowName) {
          map.set(row.rowName, extractMessageText(row))
        }
      }
      cache.value[messageName] = map
      version.value++
    } catch (e) {
      console.warn(`[ReferenceStore] Failed to load message table "${messageName}":`, e)
      // 记录空 Map 避免无限重试
      cache.value[messageName] = new Map()
      version.value++
    } finally {
      loading.value[messageName] = false
    }
  }

  function getText(messageName: string, rowKey: string): ReferenceResult {
    if (!messageName || !rowKey) return { status: 'idle' }
    const map = cache.value[messageName]
    if (map) {
      if (map.has(rowKey)) {
        return { status: 'found', text: map.get(rowKey) }
      }
      return { status: 'not_found' }
    }
    if (loading.value[messageName]) {
      return { status: 'loading' }
    }
    return { status: 'idle' }
  }

  return {
    resolveEnabled,
    cache,
    loading,
    version,
    loadMessage,
    getText
  }
})
