<script setup lang="ts">
import { tr } from "../../../i18n"
import { computed, ref } from 'vue'
import { NButton, NSelect } from 'naive-ui'
import type { UMGNode, ComputedLayout } from './types'
import { useProject } from '../../../project'

const props = defineProps<{
  nodes: UMGNode[]
  layouts: Map<string, ComputedLayout>
  selectedId: string | null
  viewport: { width: number; height: number }
  viewMode: 'appearance' | 'structure'
}>()

const emit = defineEmits<{
  'select-node': [id: string]
}>()

const project = useProject()
const stageViewport = ref<HTMLElement | null>(null)
function fit() {
  const element = stageViewport.value
  if (element) scale.value = Math.max(0.05, Math.min((element.clientWidth - 24) / props.viewport.width, (element.clientHeight - 24) / props.viewport.height))
  panX.value = 0; panY.value = 0
}
function pan(event: PointerEvent) {
  if (event.button !== 1 && !event.shiftKey) return
  event.preventDefault()
  const element = event.currentTarget as HTMLElement
  element.setPointerCapture(event.pointerId)
  const move = (e: PointerEvent) => { panX.value += e.movementX; panY.value += e.movementY }
  const end = () => { element.removeEventListener('pointermove', move); element.removeEventListener('pointerup', end); element.removeEventListener('pointercancel', end) }
  element.addEventListener('pointermove', move); element.addEventListener('pointerup', end); element.addEventListener('pointercancel', end)
}
const scale = ref(0.5)
const panX = ref(0)
const panY = ref(0)

const renderedNodes = computed(() => {
  const nodes = new Map(props.nodes.map(n => [n.id, n]))
  return [...props.layouts.keys()].map(id => nodes.get(id)!).filter(n => props.viewMode === 'structure' || props.layouts.get(n.id)!.visible)
    .map(n => ({
      node: n,
      layout: props.layouts.get(n.id)!
    }))
})

const selectedRect = computed(() => {
  if (!props.selectedId) return null
  return props.layouts.get(props.selectedId)?.rect || null
})

function effectiveOpacity(node: UMGNode) {
  const seen = new Set<string>()
  let opacity = 1, current: UMGNode | undefined = node
  while (current && !seen.has(current.id)) {
    seen.add(current.id)
    opacity *= Number(current.visual.RenderOpacity ?? 1)
    current = props.nodes.find(n => n.id === current!.parentId)
  }
  return opacity
}

function getImageUrl(node: UMGNode) {
  const brushPath = node.visual.Brush?.ResourceObject?.assetPath
  const texRef = node.resources?.find(r => r.className === 'Texture2D' && r.assetPath === brushPath)
  if (!texRef || !project.id) return null
  return `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: texRef.assetPath, mip: '0' })
}
</script>

<template>
  <div class="umg-stage-container">
    <div class="stage-toolbar">
      <span>{{ tr('ui.umgCanvas') }}: {{ viewport.width }} × {{ viewport.height }}</span>
      <div class="zoom-controls">
        <n-button size="tiny" @click="scale = Math.max(0.1, scale - 0.1)">-</n-button>
        <span class="zoom-label">{{ Math.round(scale * 100) }}%</span>
        <n-button size="tiny" @click="scale = Math.min(2, scale + 0.1)">+</n-button>
        <n-button size="tiny" @click="fit">{{ tr('ui.umgResetZoom') }}</n-button>
      </div>
    </div>

    <div ref="stageViewport" class="stage-viewport" @pointerdown="pan">
      <div
        class="stage-canvas"
        :style="{
          width: `${viewport.width}px`,
          height: `${viewport.height}px`,
          transform: `translate(${panX}px, ${panY}px) scale(${scale})`
        }"
      >
        <div
          v-for="{ node, layout } in renderedNodes"
          :key="node.id"
          class="umg-widget-box"
          :class="{
            'is-structure': viewMode === 'structure',
            'is-selected': selectedId === node.id,
            'is-hidden': !layout.visible
          }"
          :style="{
            left: `${layout.rect.x}px`,
            top: `${layout.rect.y}px`,
            width: `${layout.rect.width}px`,
            height: `${layout.rect.height}px`,
            opacity: viewMode === 'structure' ? 1 : effectiveOpacity(node),
            pointerEvents: viewMode === 'appearance' && node.children.length > 0 ? 'none' : 'auto'
          }"
          @click.stop="emit('select-node', node.id)"
        >
          <img
            v-if="viewMode === 'appearance' && getImageUrl(node)"
            :src="getImageUrl(node)!"
            class="widget-img"
            alt=""
          />
          <span v-else-if="node.visual?.Text" class="widget-text">
            {{ node.visual.Text }}
          </span>
          <span v-if="viewMode === 'structure' || node.preview.appearance === 'unsupported'" class="structure-label">
            {{ node.name }}
          </span>
        </div>

        <!-- SVG 选中框 -->
        <svg
          class="selection-overlay"
          :width="viewport.width"
          :height="viewport.height"
        >
          <rect
            v-if="selectedRect"
            :x="selectedRect.x"
            :y="selectedRect.y"
            :width="selectedRect.width"
            :height="selectedRect.height"
            fill="none"
            stroke="#2080f0"
            stroke-width="3"
          />
        </svg>
      </div>
    </div>
  </div>
</template>

<style scoped>
.umg-stage-container {
  display: flex;
  flex-direction: column;
  flex: 1;
  height: 100%;
  background: #18181c;
  overflow: hidden;
}
.stage-toolbar {
  padding: 6px 12px;
  background: rgba(255, 255, 255, 0.04);
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 12px;
  color: #aaa;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}
.zoom-controls {
  display: flex;
  align-items: center;
  gap: 8px;
}
.zoom-label {
  min-width: 44px;
  text-align: center;
  font-family: monospace;
}
.stage-viewport {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: auto;
  position: relative;
}
.stage-canvas {
  position: relative;
  background-color: #26262a;
  background-image:
    linear-gradient(45deg, #2f2f35 25%, transparent 25%),
    linear-gradient(-45deg, #2f2f35 25%, transparent 25%),
    linear-gradient(45deg, transparent 75%, #2f2f35 75%),
    linear-gradient(-45deg, transparent 75%, #2f2f35 75%);
  background-size: 24px 24px;
  background-position: 0 0, 0 12px, 12px -12px, -12px 0;
  transform-origin: center center;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.45);
}
.umg-widget-box {
  position: absolute;
  box-sizing: border-box;
  cursor: pointer;
}
.umg-widget-box.is-structure {
  border: 1px dashed rgba(255, 255, 255, 0.35);
}
.umg-widget-box.is-hidden {
  border: 1px dotted rgba(240, 160, 32, 0.5);
}
.widget-img {
  width: 100%;
  height: 100%;
  object-fit: fill;
  pointer-events: none;
}
.widget-text {
  color: #fff;
  font-size: 18px;
  pointer-events: none;
}
.structure-label {
  position: absolute;
  top: 2px;
  left: 4px;
  font-size: 11px;
  color: rgba(255, 255, 255, 0.65);
  background: rgba(0, 0, 0, 0.5);
  padding: 0 4px;
  border-radius: 2px;
  pointer-events: none;
}
.selection-overlay {
  position: absolute;
  top: 0;
  left: 0;
  pointer-events: none;
}
</style>
