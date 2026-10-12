<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { NModal, NButton, NButtonGroup, NSpin, NTag, NIcon, NSpace } from 'naive-ui'
import { ExternalLink, Grid, Moon, Sun } from '@lucide/vue'
import { useProject } from '../../project'
import { tr } from '../../i18n'

const props = withDefaults(
  defineProps<{
    show: boolean
    title?: string
    src?: string
    assetPath?: string
    mip?: number
    error?: string
  }>(),
  {
    title: '',
    src: '',
    assetPath: '',
    mip: 0,
    error: ''
  }
)

const emit = defineEmits<{
  'update:show': [value: boolean]
  navigatePath: [path: string]
}>()

const project = useProject()

type BgMode = 'checkerboard' | 'dark' | 'light'
const bgMode = ref<BgMode>('checkerboard')

const loading = ref(false)
const loadError = ref('')
const naturalWidth = ref(0)
const naturalHeight = ref(0)

const effectiveSrc = computed(() => {
  if (props.src) return props.src
  if (props.assetPath && project.id) {
    return (
      `/api/projects/${project.id}/resource-image?` +
      new URLSearchParams({
        path: props.assetPath,
        mip: String(props.mip ?? 0)
      }).toString()
    )
  }
  return ''
})

const modalTitle = computed(() => {
  if (props.title) return props.title
  if (props.assetPath) return props.assetPath
  return tr('ui.rawPreview')
})

watch(
  () => [props.show, effectiveSrc.value],
  ([isShow, url]) => {
    if (isShow && url) {
      loading.value = true
      loadError.value = ''
      naturalWidth.value = 0
      naturalHeight.value = 0
    } else if (!isShow) {
      loading.value = false
    }
  },
  { immediate: true }
)

function onImgLoad(e: Event) {
  loading.value = false
  loadError.value = ''
  const img = e.target as HTMLImageElement | null
  if (img) {
    naturalWidth.value = img.naturalWidth
    naturalHeight.value = img.naturalHeight
  }
}

function onImgError() {
  loading.value = false
  loadError.value = props.error || tr('ui.texPreviewFailed')
}

function handleNavigate() {
  if (!props.assetPath) return
  emit('update:show', false)
  emit('navigatePath', props.assetPath)
}
</script>

<template>
  <n-modal
    :show="show"
    preset="card"
    :title="modalTitle"
    style="width: min(820px, 92vw)"
    @update:show="val => emit('update:show', val)"
  >
    <div class="tex-modal-toolbar">
      <div class="tex-meta-info">
        <n-tag v-if="naturalWidth && naturalHeight" size="small" type="info" :bordered="false">
          {{ naturalWidth }} × {{ naturalHeight }} px
        </n-tag>
        <span v-if="assetPath" class="tex-path-text">{{ assetPath }}</span>
      </div>

      <n-space size="small" align="center">
        <span class="bg-label">{{ tr('ui.texBgLabel') }}:</span>
        <n-button-group size="tiny">
          <n-button
            :type="bgMode === 'checkerboard' ? 'primary' : 'default'"
            :secondary="bgMode !== 'checkerboard'"
            @click="bgMode = 'checkerboard'"
          >
            <template #icon><n-icon :component="Grid" /></template>
            {{ tr('ui.texBgChecker') }}
          </n-button>
          <n-button
            :type="bgMode === 'dark' ? 'primary' : 'default'"
            :secondary="bgMode !== 'dark'"
            @click="bgMode = 'dark'"
          >
            <template #icon><n-icon :component="Moon" /></template>
            {{ tr('ui.texBgDark') }}
          </n-button>
          <n-button
            :type="bgMode === 'light' ? 'primary' : 'default'"
            :secondary="bgMode !== 'light'"
            @click="bgMode = 'light'"
          >
            <template #icon><n-icon :component="Sun" /></template>
            {{ tr('ui.texBgLight') }}
          </n-button>
        </n-button-group>

        <n-button v-if="assetPath" size="tiny" type="primary" secondary @click="handleNavigate">
          <template #icon><n-icon :component="ExternalLink" /></template>
          {{ tr('ui.openAsset') }}
        </n-button>
      </n-space>
    </div>

    <div class="tex-stage" :class="`bg-${bgMode}`">
      <n-spin v-if="loading" size="large" class="tex-spinner" />
      <div v-if="loadError || error" class="tex-error">
        {{ loadError || error }}
      </div>
      <img
        v-if="effectiveSrc && !loadError"
        :src="effectiveSrc"
        :alt="modalTitle"
        class="tex-preview-img"
        :style="{ opacity: loading ? 0 : 1 }"
        @load="onImgLoad"
        @error="onImgError"
      />
    </div>
  </n-modal>
</template>

<style scoped>
.tex-modal-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 12px;
}
.tex-meta-info {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.tex-path-text {
  font-family: monospace;
  font-size: 12px;
  color: #666;
  word-break: break-all;
}
.bg-label {
  font-size: 12px;
  color: #666;
}
.tex-stage {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 280px;
  max-height: 68vh;
  padding: 20px;
  border-radius: 6px;
  border: 1px solid #dcdfe6;
  overflow: auto;
  transition: background-color 0.15s ease;
}
/* PS 经典透明棋盘格 */
.tex-stage.bg-checkerboard {
  background-color: #ffffff;
  background-image:
    linear-gradient(45deg, #cccccc 25%, transparent 25%),
    linear-gradient(-45deg, #cccccc 25%, transparent 25%),
    linear-gradient(45deg, transparent 75%, #cccccc 75%),
    linear-gradient(-45deg, transparent 75%, #cccccc 75%);
  background-size: 16px 16px;
  background-position: 0 0, 0 8px, 8px -8px, -8px 0;
}
/* 纯黑背景 */
.tex-stage.bg-dark {
  background-color: #141418;
  background-image: none;
}
/* 纯白背景 */
.tex-stage.bg-light {
  background-color: #ffffff;
  background-image: none;
}
.tex-spinner {
  position: absolute;
}
.tex-error {
  color: #d03050;
  font-size: 13px;
  padding: 16px;
  background: rgba(255, 255, 255, 0.9);
  border-radius: 4px;
}
.tex-preview-img {
  display: block;
  max-width: 100%;
  max-height: 62vh;
  width: auto;
  height: auto;
  object-fit: contain;
  flex-shrink: 0;
  image-rendering: pixelated;
  transition: opacity 0.15s ease;
}
</style>
