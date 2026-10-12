<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { NCard, NButton, NSpace, NTag, NSpin, useMessage, NIcon } from 'naive-ui'
import { Save, RotateCcw, Download } from '@lucide/vue'
import { useRouter } from 'vue-router'
import { api, useProject } from '../../project'
import { tr } from '../../i18n'

const props = defineProps<{
  path: string
}>()

const project = useProject()
const router = useRouter()
const message = useMessage()

const loading = ref(false)
const saving = ref(false)
const content = ref('')
const originalContent = ref('')
const headers = ref<Record<string, string>>({})
const eventCount = ref(0)
const selectedSource = ref<'project' | 'game'>('game')
const hasProjectDraft = ref(false)
const hasGameSource = ref(false)

const isDirty = computed(() => content.value !== originalContent.value)
const lineCount = computed(() => (content.value ? content.value.split('\n').length : 0))

const bgmAssetPath = computed(() => {
  const bgm = headers.value['MUSIC_FILE_PATH']
  if (!bgm) return null
  return `Sound/Bgm/${bgm}.uasset`
})

async function loadText(source?: string) {
  if (!props.path || !project.id) return
  loading.value = true
  try {
    const params = new URLSearchParams({ path: props.path })
    if (source) params.set('source', source)
    const res = await api<{
      path: string
      content: string
      headers: Record<string, string>
      eventCount: number
      sources: { selected: 'project' | 'game'; project: boolean; game: boolean }
    }>(`/projects/${project.id}/resource-text?` + params.toString())

    content.value = res.content
    originalContent.value = res.content
    headers.value = res.headers || {}
    eventCount.value = res.eventCount || 0
    selectedSource.value = res.sources.selected
    hasProjectDraft.value = res.sources.project
    hasGameSource.value = res.sources.game
  } catch (e: any) {
    message.error(e?.message || String(e))
  } finally {
    loading.value = false
  }
}

watch(
  () => [props.path, project.id, project.revision],
  () => {
    loadText()
  },
  { immediate: true }
)

async function saveText() {
  if (!props.path || !project.id) return
  saving.value = true
  try {
    const res = await api<{
      path: string
      verified: boolean
      sources: { selected: 'project' | 'game'; project: boolean; game: boolean }
    }>(`/projects/${project.id}/resource-text?` + new URLSearchParams({ path: props.path }), 'POST', { content: content.value })
    originalContent.value = content.value
    selectedSource.value = res.sources.selected
    hasProjectDraft.value = res.sources.project
    hasGameSource.value = res.sources.game
    message.success(tr('ui.merSaved'))
  } catch (e: any) {
    message.error(e?.message || String(e))
  } finally {
    saving.value = false
  }
}

function resetChanges() {
  content.value = originalContent.value
}

function downloadMer() {
  const blob = new Blob([content.value], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = props.path.split('/').pop() || 'score.mer'
  a.click()
  URL.revokeObjectURL(url)
}

function openBgmAsset() {
  if (!bgmAssetPath.value) return
  router.push({
    path: '/resource',
    query: { path: bgmAssetPath.value, from: 'link' }
  })
}
</script>

<template>
  <div class="mer-viewer">
    <div v-if="loading" class="mer-loading">
      <n-spin size="large" />
    </div>
    <template v-else>
      <n-card size="small" class="mer-meta-card">
        <div class="mer-toolbar">
          <div class="mer-meta-tags">
            <n-tag type="success" size="small" :bordered="false">{{ tr('ui.merChart') }}</n-tag>
            <n-tag v-if="hasProjectDraft" type="warning" size="small">{{ tr('ui.draftModified') }}</n-tag>
            <span class="meta-stat">{{ tr('ui.merTotalLines') }}: <b>{{ lineCount }}</b></span>
            <span class="meta-stat">{{ tr('ui.merBodyEvents') }}: <b>{{ eventCount }}</b></span>
            <span v-if="headers['OFFSET']" class="meta-stat">OFFSET: <code>{{ headers['OFFSET'] }}</code></span>
            <span v-if="headers['MOVIEOFFSET']" class="meta-stat">MOVIEOFFSET: <code>{{ headers['MOVIEOFFSET'] }}</code></span>
            <span v-if="headers['MUSIC_FILE_PATH']" class="meta-stat">
              {{ tr('ui.merLinkedAudio') }}:
              <a class="ref-link" @click.prevent="openBgmAsset">
                🔗 {{ headers['MUSIC_FILE_PATH'] }} ({{ bgmAssetPath }})
              </a>
            </span>
          </div>

          <n-space size="small" align="center">
            <n-button
              v-if="hasProjectDraft && hasGameSource"
              size="small"
              secondary
              @click="loadText(selectedSource === 'project' ? 'game' : 'project')"
            >
              {{ selectedSource === 'project' ? tr('ui.viewGameOriginal') : tr('ui.viewProjectDraft') }}
            </n-button>
            <n-button size="small" secondary :disabled="!isDirty" @click="resetChanges">
              <template #icon><n-icon :component="RotateCcw" /></template>
              {{ tr('ui.resetChanges') }}
            </n-button>
            <n-button size="small" secondary @click="downloadMer">
              <template #icon><n-icon :component="Download" /></template>
              {{ tr('ui.exportMer') }}
            </n-button>
            <n-button size="small" type="primary" :loading="saving" :disabled="!isDirty" @click="saveText">
              <template #icon><n-icon :component="Save" /></template>
              {{ tr('ui.saveToWorkspace') }}
            </n-button>
          </n-space>
        </div>
      </n-card>

      <div class="mer-editor-wrapper">
        <textarea
          v-model="content"
          class="mer-textarea"
          spellcheck="false"
          :placeholder="tr('ui.merEmptyPlaceholder')"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.mer-viewer {
  display: flex;
  flex-direction: column;
  gap: 10px;
  height: 100%;
  min-height: 0;
  flex: 1;
}
.mer-loading {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 240px;
}
.mer-meta-card {
  flex-shrink: 0;
}
.mer-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
}
.mer-meta-tags {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  font-size: 13px;
}
.meta-stat {
  color: #555;
}
.meta-stat code {
  background: #f4f4f8;
  padding: 2px 6px;
  border-radius: 4px;
  font-family: monospace;
}
.ref-link {
  color: #18a058;
  text-decoration: none;
  border-bottom: 1px dashed #18a058;
  cursor: pointer;
  font-weight: 500;
}
.ref-link:hover {
  color: #36ad6a;
  border-bottom-style: solid;
}
.mer-editor-wrapper {
  flex: 1;
  min-height: 0;
  display: flex;
  border: 1px solid #e0e0e6;
  border-radius: 6px;
  overflow: hidden;
  background: #1e1e24;
}
.mer-textarea {
  width: 100%;
  height: 100%;
  border: none;
  outline: none;
  resize: none;
  padding: 14px 16px;
  font-family: 'JetBrains Mono', 'Fira Code', Consolas, Monaco, monospace;
  font-size: 13px;
  line-height: 1.55;
  color: #e6edf3;
  background: transparent;
  tab-size: 4;
  white-space: pre;
  overflow: auto;
}
</style>
