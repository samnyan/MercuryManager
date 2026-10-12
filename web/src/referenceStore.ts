import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { api, type Row } from './project'

export interface ReferenceResult {
  status: 'idle' | 'loading' | 'found' | 'not_found'
  text?: string
}

export type MessageLanguage =
  | 'JapaneseMessage'
  | 'EnglishMessageUSA'
  | 'EnglishMessageSG'
  | 'TraditionalChineseMessageTW'
  | 'TraditionalChineseMessageHK'
  | 'SimplifiedChineseMessage'
  | 'KoreanMessage'

export interface MessageLanguageOption {
  key: MessageLanguage
  label: string
  labelEn: string
}

export const MESSAGE_LANGUAGES: MessageLanguageOption[] = [
  { key: 'JapaneseMessage', label: '日语 (JA)', labelEn: 'Japanese (JA)' },
  { key: 'SimplifiedChineseMessage', label: '简体中文 (ZH-CN)', labelEn: 'Simplified Chinese' },
  { key: 'TraditionalChineseMessageTW', label: '繁体中文-台 (ZH-TW)', labelEn: 'Traditional Chinese (TW)' },
  { key: 'TraditionalChineseMessageHK', label: '繁体中文-港 (ZH-HK)', labelEn: 'Traditional Chinese (HK)' },
  { key: 'EnglishMessageUSA', label: '英语-美 (EN-US)', labelEn: 'English (USA)' },
  { key: 'EnglishMessageSG', label: '英语-新 (EN-SG)', labelEn: 'English (SG)' },
  { key: 'KoreanMessage', label: '韩语 (KO)', labelEn: 'Korean (KO)' }
]

function extractMessageText(row: Row, selectedLanguage: MessageLanguage): string {
  if (!row.fields || !row.fields.length) return ''
  const messageField = row.fields.find(f => f.name === selectedLanguage)
  if (messageField?.value != null && String(messageField.value).trim() !== '') return String(messageField.value)
  // 若所选语言为空，回退到日语 -> 通用文本字段
  const jaField = row.fields.find(f => f.name === 'JapaneseMessage')
  if (jaField?.value != null && String(jaField.value).trim() !== '') return String(jaField.value)
  const preferred = row.fields.find(f => /^(Message|Text|MessageText|Content|Name)$/i.test(f.name))
  if (preferred && preferred.value != null && String(preferred.value).trim() !== '') {
    return String(preferred.value)
  }
  const strField = row.fields.find(f => f.type === 'StrPropertyData' && f.value != null && String(f.value).trim() !== '')
  if (strField) return String(strField.value)
  return row.fields[0]?.value != null ? String(row.fields[0].value) : ''
}

export const useReferenceStore = defineStore('reference', () => {
  const resolveEnabled = ref(true)
  const savedLang = localStorage.getItem('mercury-reference-language') as MessageLanguage | null
  const language = ref<MessageLanguage>(
    savedLang && MESSAGE_LANGUAGES.some(l => l.key === savedLang) ? savedLang : 'JapaneseMessage'
  )
  const rawCache = ref<Record<string, Row[]>>({})
  const cache = ref<Record<string, Map<string, string>>>({})
  const loading = ref<Record<string, boolean>>({})
  const version = ref(0)

  function buildMap(rows: Row[], lang: MessageLanguage): Map<string, string> {
    const map = new Map<string, string>()
    for (const row of rows) {
      if (row.rowName) {
        map.set(row.rowName, extractMessageText(row, lang))
      }
    }
    return map
  }

  watch(language, value => {
    localStorage.setItem('mercury-reference-language', value)
    const nextCache: Record<string, Map<string, string>> = {}
    for (const [msgName, rows] of Object.entries(rawCache.value)) {
      nextCache[msgName] = buildMap(rows, value)
    }
    cache.value = nextCache
    version.value++
  })

  async function loadMessage(workspaceId: string, messageName: string): Promise<void> {
    if (!workspaceId || !messageName) return
    if (cache.value[messageName] || loading.value[messageName]) return

    loading.value[messageName] = true
    try {
      const rows = await api<Row[]>(`/workspaces/${workspaceId}/messages/${encodeURIComponent(messageName)}`)
      rawCache.value[messageName] = rows
      cache.value[messageName] = buildMap(rows, language.value)
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
    language,
    cache,
    loading,
    version,
    loadMessage,
    getText
  }
})
