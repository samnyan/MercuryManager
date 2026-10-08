<script setup lang="ts">
import { ref, watch, onBeforeUnmount, computed } from 'vue'
import { useRoute } from 'vue-router'
import { NImage, NButton, NSpace, NTabs, NTab, NSpin } from 'naive-ui'
import { api, useProject } from './project'
import { tr } from './i18n'
import TextureCreateDialog from './components/resource/TextureCreateDialog.vue'
import GenericResourceViewer from './components/resource/GenericResourceViewer.vue'

const route = useRoute()
const project = useProject()

const src = ref('')
const error = ref('')
const textureError = ref('')
const supported = ref(false)
const show = ref(false)
const path = ref('')
const rawUrl = computed(()=>'/api/projects/'+project.id+'/resource-raw?'+new URLSearchParams({path:path.value}))
const mode = ref('generic')
const data = ref<unknown>()
const isTexture = ref(false)
const busy = ref(false)

let controller: AbortController | undefined
let generation = 0

function cleanup() {
  controller?.abort()
  if (src.value) URL.revokeObjectURL(src.value)
  src.value = ''
}

watch(
  () => [route.query.path, project.id],
  async () => {
    cleanup()
    const request = ++generation
    error.value = ''
    textureError.value = ''
    supported.value = false
    data.value = undefined
    isTexture.value = false
    mode.value = 'generic'
    path.value = String(route.query.path ?? '')
    if (!path.value || !project.id) return

    busy.value = true
    const current = new AbortController()
    controller = current

    try {
      const result = await api<{ isTexture: boolean; data: unknown }>(
        `/projects/${project.id}/resource-data?` + new URLSearchParams({ path: path.value })
      )
      if (request !== generation) return
      data.value = result.data
      isTexture.value = result.isTexture
      if (!isTexture.value) return
      mode.value = 'texture'

      try {
        const response = await fetch(
          `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: path.value }),
          { signal: current.signal }
        )
        if (!response.ok) throw new Error((await response.json()).message)
        const blob = await response.blob()
        if (request !== generation) return
        src.value = URL.createObjectURL(blob)
        const info = await api<{ supported: boolean }>(
          `/projects/${project.id}/resource-info?` + new URLSearchParams({ path: path.value })
        )
        if (request === generation) supported.value = info.supported
      } catch (e) {
        if (request === generation) textureError.value = String(e)
      }
    } catch (e) {
      if (request === generation) error.value = String(e)
    } finally {
      if (request === generation) busy.value = false
    }
  },
  { immediate: true }
)

onBeforeUnmount(() => {
  generation++
  cleanup()
})
</script>

<template>
  <div class="resource-view-container">
    <h2 class="page-title resource-path-title">
      {{ path || tr('ui.selectResource') }}
    </h2>

    <n-tabs v-if="data" v-model:value="mode" type="line" class="resource-tabs">
      <n-tab name="texture" :disabled="!isTexture">
        {{ tr('ui.textureMode') }}
      </n-tab>
      <n-tab name="generic">
        {{ tr('ui.genericMode') }}
      </n-tab>
    </n-tabs>

    <n-spin :show="busy">
      <div v-if="error" class="error-banner">{{ error }}</div>

      <generic-resource-viewer
        v-if="data && mode === 'generic'"
        :data="data"
        :preview-src="src"
        :preview-error="textureError"
        :raw-url="rawUrl"
      />

      <div v-if="mode === 'texture'" class="texture-pane">
        <n-image
          v-if="src"
          :src="src"
          width="512"
          class="texture-img"
        />
        <div v-if="textureError" class="error-banner">{{ textureError }}</div>
        <n-button
          v-if="src"
          type="primary"
          :disabled="!supported"
          @click="show = true"
        >
          {{ tr('ui.useTextureBase') }}
        </n-button>
        <div v-if="src && !supported" class="unsupported-hint">
          {{ tr('ui.unsupportedTexture') }}
        </div>
      </div>
    </n-spin>

    <texture-create-dialog
      v-model:show="show"
      :base="path"
      @created="project.revision++"
    />
  </div>
</template>

<style scoped>
.resource-view-container {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.resource-path-title {
  word-break: break-all;
}
.resource-tabs {
  margin-bottom: 4px;
}
.error-banner {
  color: #d03050;
  margin-bottom: 8px;
}
.texture-pane {
  display: flex;
  flex-direction: column;
  gap: 12px;
  align-items: flex-start;
}
.texture-img {
  max-width: 100%;
  border-radius: 4px;
  border: 1px solid #eee;
}
.unsupported-hint {
  font-size: 12px;
  color: #888;
}
</style>
