<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { NRadioGroup, NRadioButton, NTag } from 'naive-ui'
import type { UMGDocument, UMGNode } from './types'
import { computeTreeLayout } from './layout'
import UMGHierarchy from './UMGHierarchy.vue'
import UMGStage from './UMGStage.vue'
import UMGInspector from './UMGInspector.vue'
import TexturePreviewModal from '../TexturePreviewModal.vue'

const props = defineProps<{
  document: UMGDocument
  path: string
}>()

const emit = defineEmits<{
  'navigate-path': [path: string]
}>()

const activeRootId = ref<string | null>(props.document.suggestedRootId || props.document.roots[0]?.id || null)
const selectedNodeId = ref<string | null>(null)
const viewMode = ref<'appearance' | 'structure'>('appearance')

// WACCA 街机标准竖屏分辨率 1080 × 1920
const viewport = ref({ width: 1080, height: 1920 })

const activeRoot = computed(() => {
  return props.document.roots.find(r => r.id === activeRootId.value) || props.document.roots[0]
})

watch(() => props.document, doc => {
  activeRootId.value = doc.suggestedRootId || doc.roots[0]?.id || null
  selectedNodeId.value = null
})

// 计算布局
const computedLayouts = computed(() => {
  if (!activeRoot.value || !activeRoot.value.rootNodeId) return new Map()
  return computeTreeLayout(props.document.nodes, activeRoot.value.rootNodeId, viewport.value)
})

const selectedNode = computed<UMGNode | null>(() => {
  if (!selectedNodeId.value) return null
  return props.document.nodes.find(n => n.id === selectedNodeId.value) || null
})

const selectedNodeLayout = computed(() => {
  if (!selectedNodeId.value) return null
  return computedLayouts.value.get(selectedNodeId.value) || null
})

// 贴图预览弹窗
const previewModalVisible = ref(false)
const previewTexPath = ref('')

function openTexturePreview(targetPath: string) {
  previewTexPath.value = targetPath
  previewModalVisible.value = true
}
</script>

<template>
  <div class="umg-designer">
    <div class="umg-designer-topbar">
      <div class="topbar-left">
        <span class="designer-title">UMG Designer</span>
        <n-radio-group v-model:value="viewMode" size="small">
          <n-radio-button value="appearance">外观模式</n-radio-button>
          <n-radio-button value="structure">结构线框</n-radio-button>
        </n-radio-group>
      </div>

      <div class="topbar-right">
        <n-tag size="small" :bordered="false" type="info">竖屏 1080 × 1920</n-tag>
      </div>
    </div>

    <div v-if="document.diagnostics.length" class="umg-diagnostics"><div v-for="(issue, index) in document.diagnostics" :key="index">{{ issue.code }}: {{ issue.message }}</div></div>
    <div class="umg-designer-body">
      <!-- 左栏：层级树 -->
      <u-m-g-hierarchy
        style="width: 280px; flex-shrink: 0"
        :document="document"
        :active-root-id="activeRootId"
        :selected-id="selectedNodeId"
        @select-root="id => { activeRootId = id; selectedNodeId = null }"
        @select-node="id => (selectedNodeId = id)"
      />

      <!-- 中栏：预览舞台 -->
      <u-m-g-stage
        :nodes="document.nodes"
        :layouts="computedLayouts"
        :selected-id="selectedNodeId"
        :viewport="viewport"
        :view-mode="viewMode"
        @select-node="id => (selectedNodeId = id)"
      />

      <!-- 右栏：类型化属性检查器 -->
      <u-m-g-inspector
        :node="selectedNode"
        :layout="selectedNodeLayout"
        @navigate-path="emit('navigate-path', $event)"
        @preview-texture="openTexturePreview"
      />
    </div>

    <!-- 贴图预览模态框 -->
    <texture-preview-modal
      v-model:show="previewModalVisible"
      :asset-path="previewTexPath"
      @navigate-path="emit('navigate-path', $event)"
    />
  </div>
</template>

<style scoped>
.umg-designer {
  display: flex;
  flex-direction: column;
  height: 100%;
  width: 100%;
  overflow: hidden;
  background: var(--n-color);
}
.umg-designer-topbar {
  padding: 8px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--n-border-color);
  background: rgba(128, 128, 128, 0.04);
}
.topbar-left {
  display: flex;
  align-items: center;
  gap: 16px;
}
.designer-title {
  font-weight: 700;
  font-size: 14px;
}
.umg-designer-body {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}
</style>
