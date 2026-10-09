<script setup lang="ts">
import { ref } from 'vue'
import { NPopover, NSpin, NEmpty } from 'naive-ui'
import { tr } from '../../i18n'
import { api, useProject } from '../../project'

const props = defineProps<{
  cueName: string
}>()

export interface VoiceMatch {
  table: string
  rowKey: string
  japaneseMessage: string
}

const project = useProject()
const show = ref(false)
const loading = ref(false)
const loaded = ref(false)
const matches = ref<VoiceMatch[]>([])
const error = ref('')

async function handleShowChange(val: boolean) {
  show.value = val
  if (val && !loaded.value && props.cueName && project.id) {
    loading.value = true
    error.value = ''
    try {
      const res = await api<VoiceMatch[]>(
        `/workspaces/${project.id}/voice-lookup?` +
          new URLSearchParams({ voiceId: props.cueName })
      )
      matches.value = res || []
      loaded.value = true
    } catch (e) {
      error.value = String(e)
    } finally {
      loading.value = false
    }
  }
}
</script>

<template>
  <span class="voice-cell-wrapper" @click.stop>
    <n-popover
      trigger="click"
      placement="bottom-start"
      :show="show"
      @update:show="handleShowChange"
    >
      <template #trigger>
        <span
          class="cue-ref-link"
          :title="tr('ui.reverseVoiceLookup').replace('{name}', cueName)"
        >
          {{ cueName }}
        </span>
      </template>

      <div class="voice-popover-panel">
        <div class="popover-header">
          <span class="popover-title">{{ tr('ui.matchedVoiceLines').replace('{count}', String(matches.length)) }}</span>
          <span class="cue-badge">{{ cueName }}</span>
        </div>

        <div v-if="loading" class="popover-loading">
          <n-spin size="small" />
          <span style="font-size: 12px; margin-left: 8px">{{ tr('ui.searching') }}</span>
        </div>

        <div v-else-if="error" class="popover-error">
          {{ error }}
        </div>

        <div v-else-if="matches.length === 0" class="popover-empty">
          <n-empty size="small" :description="tr('ui.noVoiceLinesFound')" />
        </div>

        <div v-else class="popover-table-container">
          <table class="voice-result-table">
            <thead>
              <tr>
                <th style="width: 140px">Table</th>
                <th>JapaneseMessage</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(m, idx) in matches" :key="idx">
                <td class="table-name-cell">
                  <router-link
                    :to="`/message/${encodeURIComponent(m.table)}`"
                    class="nav-link"
                    @click="show = false"
                  >
                    {{ m.table }}
                  </router-link>
                  <div class="row-key-sub" :title="m.rowKey">{{ m.rowKey }}</div>
                </td>
                <td class="japanese-cell" :title="m.japaneseMessage">
                  {{ m.japaneseMessage || '—' }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </n-popover>
  </span>
</template>

<style scoped>
.voice-cell-wrapper {
  display: inline-flex;
  align-items: center;
  max-width: 100%;
}

.cue-ref-link {
  color: #18a058;
  text-decoration: underline;
  text-decoration-style: dashed;
  cursor: pointer;
  font-weight: 500;
  display: inline-block;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  transition: color 0.15s;
}

.cue-ref-link:hover {
  color: #36ad6a;
}

.voice-popover-panel {
  max-width: 520px;
  min-width: 360px;
  padding: 4px 2px;
}

.popover-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
  padding-bottom: 6px;
  border-bottom: 1px solid #f0f0f0;
}

.popover-title {
  font-weight: 600;
  font-size: 13px;
  color: #333;
}

.cue-badge {
  font-family: monospace;
  font-size: 11px;
  background: #f5f5f5;
  padding: 2px 6px;
  border-radius: 4px;
  color: #666;
}

.popover-loading,
.popover-empty,
.popover-error {
  padding: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.popover-error {
  color: #d03050;
  font-size: 12px;
}

.popover-table-container {
  max-height: 280px;
  overflow-y: auto;
}

.voice-result-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
  text-align: left;
}

.voice-result-table th {
  background: #fafafa;
  color: #666;
  font-weight: 600;
  padding: 6px 8px;
  border-bottom: 1px solid #eee;
  position: sticky;
  top: 0;
  z-index: 1;
}

.voice-result-table td {
  padding: 6px 8px;
  border-bottom: 1px solid #f5f5f5;
  vertical-align: top;
}

.table-name-cell {
  font-weight: 500;
}

.row-key-sub {
  font-family: monospace;
  font-size: 10.5px;
  color: #888;
  margin-top: 2px;
  max-width: 130px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.nav-link {
  color: #18a058;
  text-decoration: underline;
}

.nav-link:hover {
  color: #36ad6a;
}

.japanese-cell {
  color: #222;
  word-break: break-all;
  font-size: 12.5px;
}
</style>
