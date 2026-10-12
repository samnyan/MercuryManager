<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NTag, NButton, NSpin, NAlert, NModal } from 'naive-ui'
import { useProject } from '../../../project'
import { tr } from '../../../i18n'
import type { UMGDocument, UMGNode } from './types'
import { computeTreeLayout } from './layout'
import UMGHierarchy from './UMGHierarchy.vue'
import UMGStage from './UMGStage.vue'
import UMGInspector from './UMGInspector.vue'

const props = defineProps<{
  path: string
  revision: string
}>()

const emit = defineEmits<{
  'navigate-path': [path: string]
  'fallback-generic': []
}>()

const project = useProject()
const loading = ref(false)
const error = ref<string | null>(null)
const doc = ref<UMGDocument | null>(null)

const activeRootId = ref<string | null>(null)
const selectedNodeId = ref<string | null>(null)
const hiddenIds = ref<Set<string>>(new Set())
// WACCA 机台固定 1080 × 1920 竖屏
const viewport = ref({ width: 1080, height: 1920 })
const viewMode = ref<'appearance' | 'structure'>('appearance')

// Texture preview modal
const previewPath = ref('')
const previewUrl = computed(
  () => `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: previewPath.value })
)

let loadController: AbortController | null = null

async function loadDocument() {
  loadController?.abort()
  const controller = new AbortController()
  loadController = controller

  loading.value = true
  error.value = null
  doc.value = null
  selectedNodeId.value = null
  hiddenIds.value = new Set()

  try {
    const res = await fetch(
      `/api/projects/${project.id}/resource-umg?` + new URLSearchParams({ path: props.path }),
      { signal: controller.signal }
    )
    const json = await res.json().catch(() => null)
    if (controller.signal.aborted) return
    if (!res.ok) {
      throw new Error(json?.message || `${res.status} ${res.statusText}`)
    }
    const data = (json?.data ?? json) as UMGDocument
    doc.value = data
    activeRootId.value = data.suggestedRootId || data.roots?.[0]?.id || null
    const rootObj = data.roots?.find(r => r.id === activeRootId.value) || data.roots?.[0]
    selectedNodeId.value = rootObj?.rootNodeId || null
  } catch (e: any) {
    if (controller.signal.aborted) return
    error.value = e?.message || String(e)
  } finally {
    if (loadController === controller) {
      loading.value = false
    }
  }
}

watch(
  () => [project.id, props.path, props.revision],
  () => {
    if (project.id && props.path) loadDocument()
  },
  { immediate: true }
)

const activeRoot = computed(() => {
  if (!doc.value) return null
  return doc.value.roots.find(r => r.id === activeRootId.value) || doc.value.roots[0] || null
})

watch(activeRoot, r => {
  if (r && r.rootNodeId) {
    selectedNodeId.value = r.rootNodeId
  }
})

const layouts = computed(() => {
  if (!doc.value || !activeRoot.value?.rootNodeId) return new Map()
  return computeTreeLayout(doc.value.nodes, activeRoot.value.rootNodeId, viewport.value)
})

const selectedNode = computed<UMGNode | null>(() => {
  if (!doc.value || !selectedNodeId.value) return null
  return doc.value.nodes.find(n => n.id === selectedNodeId.value) || null
})

const selectedLayout = computed(() => {
  if (!selectedNodeId.value) return null
  return layouts.value.get(selectedNodeId.value) || null
})
</script>

<template>
  <div class="umg-designer">
    <!-- Top Banner & Mode Controls -->
    <div class="umg-topbar">
      <div class="topbar-left">
        <n-tag type="warning" size="small" :bordered="false">
          近似静态预览（非 UE 游戏实时渲染复现）
        </n-tag>
        <span v-if="doc" class="meta-text">
          Nodes: {{ doc.nodes.length }} | Diagnostics: {{ doc.diagnostics?.length || 0 }}
        </span>
      </div>

      <div class="topbar-right">
        <div class="mode-group">
          <n-button
            size="small"
            :type="viewMode === 'appearance' ? 'primary' : 'default'"
            @click="viewMode = 'appearance'"
          >
            外观预览
          </n-button>
          <n-button
            size="small"
            :type="viewMode === 'structure' ? 'primary' : 'default'"
            @click="viewMode = 'structure'"
          >
            结构线框
          </n-button>
        </div>

        <n-tag size="small" :bordered="false">1080 × 1920 (竖屏)</n-tag>

        <n-button size="small" secondary @click="emit('fallback-generic')">
          切换到旧版树视图
        </n-button>
      </div>
    </div>

    <!-- Loading / Error State -->
    <div v-if="loading" class="center-state">
      <n-spin size="large" />
    </div>

    <div v-else-if="error" class="center-state">
      <n-alert type="error" title="UMG 视图加载失败" style="max-width: 520px">
        <p>{{ error }}</p>
        <n-button size="small" @click="emit('fallback-generic')">使用旧版结构视图打开</n-button>
      </n-alert>
    </div>

    <!-- Three-Pane Workspace -->
    <div v-else-if="doc" class="umg-workspace">
      <UMGHierarchy
        :roots="doc.roots"
        :active-root-id="activeRootId"
        :nodes="doc.nodes"
        :selected-id="selectedNodeId"
        :hidden-ids="hiddenIds"
        @update:active-root-id="id => (activeRootId = id)"
        @select-node="id => (selectedNodeId = id)"
        @update:hidden-ids="ids => (hiddenIds = ids)"
      />

      <UMGStage
        :nodes="doc.nodes"
        :layouts="layouts"
        :selected-id="selectedNodeId"
        :hidden-ids="hiddenIds"
        :viewport="viewport"
        :view-mode="viewMode"
        @select-node="id => (selectedNodeId = id)"
      />

      <UMGInspector
        :node="selectedNode"
        :layout="selectedLayout"
        @navigate-path="p => emit('navigate-path', p)"
        @preview-texture="p => (previewPath = p)"
      />
    </div>

    <n-modal
      :show="!!previewPath"
      preset="card"
      :title="previewPath"
      style="width: min(720px, 92vw)"
      @update:show="v => !v && (previewPath = '')"
    >
      <img
        v-if="previewPath"
        :src="previewUrl"
        :alt="previewPath"
        style="max-width: 100%; max-height: 72vh; display: block; margin: 0 auto; object-fit: contain; background: #18181c"
      />
    </n-modal>
  </div>
</template>

<style scoped>
.umg-designer {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  overflow: hidden;
  border: 1px solid var(--n-border-color);
  border-radius: 6px;
}
.umg-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 8px 12px;
  border-bottom: 1px solid var(--n-border-color);
  background: rgba(128, 128, 128, 0.04);
  flex-wrap: wrap;
}
.topbar-left,
.topbar-right {
  display: flex;
  align-items: center;
  gap: 10px;
}
.mode-group {
  display: flex;
  gap: 4px;
}
.meta-text {
  font-size: 12px;
  color: #888;
}
.center-state {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
}
.umg-workspace {
  flex: 1;
  display: flex;
  min-height: 0;
  overflow: hidden;
}
</style>
