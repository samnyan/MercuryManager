<script setup lang="ts">
import { ref, watch } from 'vue'
import { NModal, NButton, NIcon, NSpace } from 'naive-ui'
import { ExternalLink, Layers } from '@lucide/vue'
import { tr } from '../../i18n'
import ResourceView from '../../ResourceView.vue'

const props = withDefaults(
  defineProps<{
    show: boolean
    assetPath?: string
    title?: string
    depth?: number
  }>(),
  {
    assetPath: '',
    title: '',
    depth: 1
  }
)

const emit = defineEmits<{
  'update:show': [value: boolean]
}>()

// 维护当前弹窗内部的路径与嵌套子弹窗状态
const currentPath = ref('')
const childDialogShow = ref(false)
const childDialogPath = ref('')

watch(
  () => [props.show, props.assetPath] as const,
  ([isShow, newPath]) => {
    if (isShow && newPath) {
      currentPath.value = newPath
    }
  },
  { immediate: true }
)

// 当弹窗内的组件触发 @navigate-path 时，打开新的一层嵌套弹窗展示
function handleInnerNavigate(targetPath: string) {
  if (!targetPath) return
  childDialogPath.value = targetPath
  childDialogShow.value = true
}

function openInStandalonePage() {
  if (!currentPath.value) return
  window.open(`/resource?path=${encodeURIComponent(currentPath.value)}`, '_blank')
}
</script>

<template>
  <n-modal
    :show="show"
    preset="card"
    :title="title || currentPath || tr('ui.resourceDialogTitle')"
    :style="{
      width: `min(${Math.max(880, 1220 - (depth - 1) * 40)}px, ${Math.max(82, 94 - (depth - 1) * 3)}vw)`,
      height: `min(${Math.max(660, 860 - (depth - 1) * 30)}px, ${Math.max(80, 90 - (depth - 1) * 3)}vh)`
    }"
    content-style="display: flex; flex-direction: column; min-height: 0; overflow: hidden; padding: 12px 16px;"
    @update:show="val => emit('update:show', val)"
  >
    <template #header-extra>
      <n-space size="small" align="center" style="margin-right: 12px">
        <n-button
          v-if="currentPath"
          size="tiny"
          secondary
          type="primary"
          @click="openInStandalonePage"
        >
          <template #icon><n-icon :component="ExternalLink" /></template>
          {{ tr('ui.dialogOpenNewTab') }}
        </n-button>
      </n-space>
    </template>

    <div class="dialog-resource-body">
      <ResourceView
        v-if="show && currentPath"
        :initial-path="currentPath"
        :in-dialog="true"
        @open-dialog="handleInnerNavigate"
      />
    </div>

    <!-- 递归支持无限层级嵌套弹窗 (Material -> Parent Material / Function / Texture) -->
    <ResourceViewerModal
      v-if="childDialogShow"
      v-model:show="childDialogShow"
      :asset-path="childDialogPath"
      :depth="depth + 1"
    />
  </n-modal>
</template>

<style scoped>
.dialog-resource-body {
  flex: 1;
  min-height: 0;
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
</style>
