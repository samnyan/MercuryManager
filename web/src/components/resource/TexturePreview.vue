<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import TextureCreateDialog from './TextureCreateDialog.vue'
import { NImage, NButton } from 'naive-ui'
import { useProject } from '../../project'
import { tr } from '../../i18n'

const props = defineProps<{
  table?: string
  field: string
  value: unknown
}>()

const project = useProject()
const src = ref('')
const state = ref('')
const error = ref('')
const create = ref(false)
const refresh = ref(0)

let timer: ReturnType<typeof setTimeout> | undefined
let controller: AbortController | undefined

function cleanup() {
  clearTimeout(timer)
  controller?.abort()
  if (src.value) URL.revokeObjectURL(src.value)
  src.value = ''
}

watch(
  () => [props.value, props.field, props.table, project.id, project.revision, refresh.value],
  () => {
    cleanup()
    error.value = ''
    const value = String(props.value ?? '').trim()
    if (!value || !project.id) {
      state.value = ''
      return
    }
    state.value = 'loading'
    timer = setTimeout(async () => {
      const current = new AbortController()
      controller = current
      try {
        const query = new URLSearchParams({
          table: props.table ?? '',
          field: props.field,
          value
        })
        const response = await fetch(`/api/projects/${project.id}/texture?${query}`, {
          signal: current.signal
        })
        if (response.status === 404) {
          state.value = 'missing'
          return
        }
        if (!response.ok) {
          const result = await response.json()
          throw new Error(result.message)
        }
        const blob = await response.blob()
        if (current.signal.aborted) return
        src.value = URL.createObjectURL(blob)
        state.value = 'found'
      } catch (e) {
        if (current.signal.aborted) return
        state.value = 'error'
        error.value = String(e)
      }
    }, 300)
  },
  { immediate: true }
)

onBeforeUnmount(cleanup)
</script>

<template>
  <div v-if="state" class="texture-preview-box">
    <span v-if="state === 'loading'" class="status-text">{{ tr('ui.textureLoading') }}</span>
    <n-image
      v-else-if="src"
      :src="src"
      width="128"
      style="max-height: 160px"
      object-fit="contain"
    />
    <span v-else class="status-text error-text" :title="error">
      {{ tr('ui.textureUnavailable') }}
    </span>

    <n-button
      v-if="state === 'missing' && table === 'MusicParameterTable' && field === 'JacketAssetName'"
      size="small"
      type="primary"
      ghost
      @click="create = true"
    >
      {{ tr('ui.createTexture') }}
    </n-button>

    <texture-create-dialog
      v-model:show="create"
      :target="'UI/Textures/JACKET/' + String(value) + '.uasset'"
      @created="refresh++"
    />
  </div>
</template>

<style scoped>
.texture-preview-box {
  margin-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.status-text {
  font-size: 12px;
  color: #888;
}
.error-text {
  color: #d03050;
}
</style>
