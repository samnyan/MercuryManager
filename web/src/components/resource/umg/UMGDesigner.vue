<script setup lang="ts">
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { NTag, NButton, NSpin, NAlert, NModal, NSplit } from 'naive-ui'
import { useProject } from '../../../project'
import { tr } from '../../../i18n'
import type { UMGAnimation, UMGDocument, UMGNode } from './types'
import { computeTreeLayout } from './layout'
import { resolveAnimation, reachableNodes, entranceSelection, combineAnimations, isolationHidden } from './animationPreview'
import UMGHierarchy from './UMGHierarchy.vue'
import UMGStage from './UMGStage.vue'
import UMGInspector from './UMGInspector.vue'
import UMGAnimationTimeline from './UMGAnimationTimeline.vue'

const props = defineProps<{
  path: string
  revision: string
}>()

const emit = defineEmits<{
  'navigate-path': [path: string]
  'fallback-generic': []
}>()
let loadController: AbortController | null = null


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
const timelineHeight = ref(220)
const timelineCollapsed = ref(false)
watch(timelineCollapsed, collapsed => {
  if (!collapsed && timelineHeight.value < 100) timelineHeight.value = 220
})
const timelinePane = ref<HTMLElement | null>(null)
let draggingTimeline = false
function startTimelineResize(event: MouseEvent) {
  if (timelineCollapsed.value || event.button !== 0) return
  event.preventDefault()
  draggingTimeline = true
  const move = (e: MouseEvent) => {
    if (!draggingTimeline) return
    const bounds = timelinePane.value?.parentElement?.getBoundingClientRect()
    if (!bounds) return
    timelineHeight.value = Math.max(100, Math.min(bounds.height * 0.6, bounds.bottom - e.clientY))
  }
  const stop = () => {
    draggingTimeline = false
    window.removeEventListener('mousemove', move)
    window.removeEventListener('mouseup', stop)
  }
  window.addEventListener('mousemove', move)
  window.addEventListener('mouseup', stop)
}
const toggleTimeline = () => {
  timelineCollapsed.value = !timelineCollapsed.value
  if (!timelineCollapsed.value && timelineHeight.value < 100) timelineHeight.value = 220
}
const activeRoot = computed(() => {
  if (!doc.value) return null
  return doc.value.roots.find(r => r.id === activeRootId.value) || doc.value.roots[0] || null
})

const activeAnimationId = ref<string | null>(null)
const animationFrame = ref(0)
const animationPlaying = ref(false)
const timelineVisible = ref(true)
const animationSoloHiddenIds = ref<Set<string>>(new Set())
// Keep identity stable across playback ticks; a fresh Set triggers stage rebuilds.
const stageHiddenIds = computed(() => new Set([...hiddenIds.value, ...animationSoloHiddenIds.value]))
// Scrubbing pauses playback; timer updates must not pause themselves.
watch(animationFrame, () => { if (!advancingAnimation) setAnimationPlaying(false) }, { flush: 'sync' })
let advancingAnimation = false
let animationTimer: number | undefined
const selectedAnimationIds = ref<string[]>([])
const isolateAnimation = ref(false)
const repeatAnimation = ref(false)
const resolvedAnimations = computed(() => doc.value ? (doc.value.animations || []).map(a => resolveAnimation(a, doc.value!)) : [])
const animationsForRoot = resolvedAnimations
const activeAnimation = computed(() => {
  const selected = resolvedAnimations.value.filter(a => selectedAnimationIds.value.includes(a.id))
  return combineAnimations(selected, tr('ui.umgCombinedPreview'))
})
watch(selectedAnimationIds, () => {
  animationFrame.value = 0
  setAnimationPlaying(false)
  activeAnimationId.value = selectedAnimationIds.value.length ? '__combined' : null
}, { flush: 'sync' })
watch(() => [activeAnimation.value, activeRoot.value?.rootNodeId, isolateAnimation.value], () => {
  animationSoloHiddenIds.value = isolateAnimation.value && activeAnimation.value && doc.value
    ? isolationHidden(doc.value.nodes, activeAnimation.value.bindings.flatMap(b => b.nodeId ? [b.nodeId] : []), activeRoot.value?.rootNodeId)
    : new Set()
}, { immediate: true })
function playEntrances() {
  if (!doc.value) return
  // Respect serialized visibility and manual hiding; do not animate every
  // mutually exclusive mode merely because its tree is structurally reachable.
  const visible = reachableNodes(doc.value.nodes, activeRoot.value?.rootNodeId)
  const byId = new Map(doc.value.nodes.map(n => [n.id, n]))
  for (const id of visible) {
    let node = byId.get(id)
    const seen = new Set<string>()
    while (node && !seen.has(node.id)) {
      seen.add(node.id)
      const visibility = String(node.visual.Visibility ?? 'Visible').split('::').pop()
      if (hiddenIds.value.has(node.id) || visibility === 'Hidden' || visibility === 'Collapsed') { visible.delete(id); break }
      node = node.parentId ? byId.get(node.parentId) : undefined
    }
  }
  selectedAnimationIds.value = entranceSelection(resolvedAnimations.value, visible)
  animationFrame.value = 0
  setAnimationPlaying(selectedAnimationIds.value.length > 0)
}
function selectAnimations(ids: string[]) {
  // One variant per owner: explicitly choosing another variant replaces the old one.
  const added = ids.filter(id => !selectedAnimationIds.value.includes(id))
  const owners = new Set(resolvedAnimations.value.filter(a => added.includes(a.id)).map(a => a.ownerId))
  selectedAnimationIds.value = ids.filter(id => added.includes(id) || !owners.has(resolvedAnimations.value.find(a => a.id === id)?.ownerId))
}
function sampleChannel(channel: {defaultValue:number;hasDefault:boolean;keys:{frame:number;value:number;interpolation:string;arriveTangent:number;leaveTangent:number}[]}, time:number): number | null {
  const keys=channel.keys
  if(!keys.length) return channel.hasDefault?channel.defaultValue:null
  const right=keys.findIndex(key=>key.frame>=time)
  if(right<0) return keys[keys.length-1].value
  if(right===0 || keys[right].frame===time) return keys[right].value
  const left=keys[right-1],next=keys[right],duration=next.frame-left.frame
  if(duration<=0) return next.value
  if(left.interpolation.toLowerCase().includes('constant')) return left.value
  const t=(time-left.frame)/duration
  if(left.interpolation.toLowerCase().includes('cubic')){const t2=t*t,t3=t2*t;return (2*t3-3*t2+1)*left.value+(t3-2*t2+t)*(left.leaveTangent||0)*duration+(-2*t3+3*t2)*next.value+(t3-t2)*(next.arriveTangent||0)*duration}
  return left.value+(next.value-left.value)*t
}
// Binding resolution is invariant during playback; do not search all nodes per track.
const sourceNodesById = computed(() => new Map((doc.value?.nodes ?? []).map(n => [n.id, n])))
const boundTracks = computed(() => {
  const animation = activeAnimation.value
  const bindings = new Map(animation?.bindings.map(b => [b.guid, b.nodeId]) ?? [])
  return (animation?.tracks ?? []).flatMap(track => {
    const id = bindings.get(track.targetGuid)
    const node = id ? sourceNodesById.value.get(id) : undefined
    return node ? [{ track, node }] : []
  })
})
const animationMaterialParameters = computed(() => {
  const result: Record<string, Record<string, number>> = {}
  const a=activeAnimation.value
  if(!a) return result
  for(const {track, node} of boundTracks.value){
    if (!track.materialParameters?.length) continue
    const parameters=result[node.id]||(result[node.id]={})
    for(const parameter of track.materialParameters){const value=sampleChannel(parameter.channel,animationFrame.value);if(value!==null)parameters[parameter.name]=value}
  }
  return result
})
const previewNodes = computed(() => {
  if (!doc.value) return []
  if (!activeAnimation.value) return doc.value.nodes
  // Preserve every nested tree and the original layout/draw order. Visibility is a separate mask.
  const clones = new Map<string, UMGNode>()
  for (const { node } of boundTracks.value) {
    if (clones.has(node.id)) continue
    clones.set(node.id, {
    ...node,
    slot: node.slot ? { ...node.slot, values: { ...node.slot.values, LayoutData: { ...node.slot.values.LayoutData, Offsets: { ...node.slot.values.LayoutData?.Offsets } } } } : node.slot,
    visual: {
      ...node.visual,
      RenderTransform: node.visual.RenderTransform ? {
        ...node.visual.RenderTransform,
        Translation: { ...node.visual.RenderTransform.Translation },
        Scale: { ...node.visual.RenderTransform.Scale },
        Shear: { ...node.visual.RenderTransform.Shear }
      } : undefined
    }
    })
  }
  for(const {track, node: source} of boundTracks.value){
    const node=clones.get(source.id)
    if(!node) continue
    const values=new Map(track.channels.map(c=>[c.property,sampleChannel(c,animationFrame.value)]))
    if(track.type.includes('Transform')){
      const axis=(name:string,index:number)=>values.get(`${name}.${index}`)??values.get(`${name}${index===0?'X':'Y'}`)??null
      const translation={X:axis('Translation',0)??node.visual.RenderTransform?.Translation?.X??0,Y:axis('Translation',1)??node.visual.RenderTransform?.Translation?.Y??0}
      const scale={X:axis('Scale',0)??node.visual.RenderTransform?.Scale?.X??1,Y:axis('Scale',1)??node.visual.RenderTransform?.Scale?.Y??1}
      const shear={X:axis('Shear',0)??node.visual.RenderTransform?.Shear?.X??0,Y:axis('Shear',1)??node.visual.RenderTransform?.Shear?.Y??0}
      node.visual.RenderTransform={...(node.visual.RenderTransform||{}),Translation:translation,Scale:scale,Shear:shear,Angle:values.get('Rotation.0')??values.get('Rotation')??node.visual.RenderTransform?.Angle??0}
    } else if(track.type.includes('Float')&&track.property==='RenderOpacity') node.visual.RenderOpacity=values.get('FloatCurve')??node.visual.RenderOpacity
    else if(track.type.includes('Margin') && track.property==='Offsets' && node.slot) {
      const data = node.slot.values.LayoutData ??= {}
      const offsets = data.Offsets ??= {}
      for (const side of ['Left','Top','Right','Bottom']) {
        const value = values.get(`${side}Curve`)
        if (value != null) offsets[side] = value
      }
    } else if(track.type.includes('Color')) {
      const property = track.property
      if (!property || !['ColorAndOpacity','ContentColorAndOpacity','BrushColor','Color'].includes(property)) continue
      const original = node.visual[property] ?? {}
      const color = { ...(original.SpecifiedColor ?? original) }
      for (const [channel, component] of [['RedCurve','R'],['GreenCurve','G'],['BlueCurve','B'],['AlphaCurve','A']]) {
        const value = values.get(channel)
        if (value != null) color[component] = value
      }
      node.visual[property] = original.SpecifiedColor ? { ...original, SpecifiedColor: color } : color
    }
  }
  return doc.value.nodes.map(node => clones.get(node.id) ?? node)
})
function setAnimationPlaying(playing: boolean) {
  animationPlaying.value = playing
  if (animationTimer) window.cancelAnimationFrame(animationTimer)
  animationTimer = undefined
  if (!playing || !activeAnimation.value) return
  if (animationFrame.value >= activeAnimation.value.end) animationFrame.value = activeAnimation.value.start
  animationPlaying.value = true
  let last = performance.now()
  const tick = (now: number) => {
    const delta = Math.min(0.1, Math.max(0, (now - last) / 1000))
    last = now
    const a = activeAnimation.value
    if (!a || a.end <= a.start) { setAnimationPlaying(false); return }
    advancingAnimation = true
    try {
      const next = animationFrame.value + delta
      animationFrame.value = next >= a.end ? repeatAnimation.value ? a.start + (next - a.start) % (a.end - a.start) : a.end : next
      if (next >= a.end && !repeatAnimation.value) animationPlaying.value = false
    } finally { advancingAnimation = false }
    if (animationPlaying.value) animationTimer = window.requestAnimationFrame(tick)
  }
  animationTimer = window.requestAnimationFrame(tick)
}
function toggleAnimation() { setAnimationPlaying(!animationPlaying.value) }

onBeforeUnmount(()=>{if(animationTimer)window.cancelAnimationFrame(animationTimer)})

// Texture preview modal
const previewPath=ref('')
const previewUrl=computed(()=>`/api/projects/${project.id}/resource-image?`+new URLSearchParams({path:previewPath.value}))
async function loadDocument() {
  loadController?.abort()
  const controller = new AbortController()
  loadController = controller

  loading.value = true
  error.value = null
  setAnimationPlaying(false)
  selectedAnimationIds.value = []
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


watch(activeRoot, r => {
  if (r && r.rootNodeId) selectedNodeId.value = r.rootNodeId
})
watch(() => activeRoot.value?.treeId, () => {
  selectedAnimationIds.value = []
  setAnimationPlaying(false)
})

const layouts = computed(() => {
  if (!doc.value || !activeRoot.value?.rootNodeId) return new Map()
  return computeTreeLayout(previewNodes.value, activeRoot.value.rootNodeId, viewport.value)
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
    <div v-else-if="doc" class="umg-body">
      <section class="umg-workspace">
        <div class="umg-left-pane">
          <UMGHierarchy
            :roots="doc.roots"
            :active-root-id="activeRootId"
            :nodes="doc.nodes"
            :selected-id="selectedNodeId"
            :hidden-ids="hiddenIds"
            :animation-solo-hidden-ids="animationSoloHiddenIds"
            @update:active-root-id="id => (activeRootId = id)"
            @select-node="id => (selectedNodeId = id)"
            @update:hidden-ids="ids => (hiddenIds = ids)"
          />
        </div>
        <div class="umg-stage-pane">
          <UMGStage
            :nodes="previewNodes"
            :layouts="layouts"
            :materials="doc.materials"
            :particles="doc.particles"
            :animation-material-parameters="animationMaterialParameters"
            :material-time="activeAnimation ? animationFrame : undefined"
            :selected-id="selectedNodeId"
            :hidden-ids="stageHiddenIds"
            :viewport="viewport"
            :view-mode="viewMode"
            @select-node="id => (selectedNodeId = id)"
          />
        </div>
        <div class="umg-right-pane">
          <UMGInspector
            :node="selectedNode"
            :layout="selectedLayout"
            :materials="doc.materials"
            :asset-path="path"
            @navigate-path="p => emit('navigate-path', p)"
            @preview-texture="p => (previewPath = p)"
            @node-updated="loadDocument"
          />
        </div>
      </section>
      <div class="timeline-resize-handle" :class="{ collapsed: timelineCollapsed }" role="separator" @mousedown="startTimelineResize">
        <button class="timeline-collapse" type="button" :title="timelineCollapsed ? tr('ui.umgTimelineExpand') : tr('ui.umgTimelineCollapse')" @mousedown.stop @click.stop="toggleTimeline">{{ timelineCollapsed ? '▴' : '▾' }}</button>
      </div>
      <div ref="timelinePane" class="timeline-pane" :style="{ height: timelineCollapsed ? '12px' : `${timelineHeight}px` }">
        <UMGAnimationTimeline
          :animations="animationsForRoot"
          :active-animation-id="activeAnimationId"
          :preview-animation="activeAnimation"
          :selected-animation-ids="selectedAnimationIds"
          :isolate="isolateAnimation"
          :repeat="repeatAnimation"
          :time="animationFrame"
          :playing="animationPlaying"
          :roots="doc.roots"
          :nodes="doc.nodes"
          @update:active-animation-id="selectedAnimationIds = []"
          @update:selected-animation-ids="selectAnimations"
          @play-entrances="playEntrances"
          @update:isolate="isolateAnimation = $event"
          @update:repeat="repeatAnimation = $event"
          @update:time="animationFrame = $event"
          @update:playing="setAnimationPlaying($event)"
        />
      </div>
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
.umg-body { display: flex; flex-direction: column; flex: 1; min-height: 0; min-width: 0; width: 100%; overflow: hidden; }
.umg-workspace { display: grid; grid-template-columns: clamp(180px, 18vw, 320px) minmax(0, 1fr) clamp(260px, 23vw, 420px); flex: 1 1 auto; min-height: 0; min-width: 0; width: 100%; overflow: hidden; }
.umg-left-pane, .umg-stage-pane, .umg-right-pane { min-width: 0; min-height: 0; overflow: hidden; }
.timeline-pane { flex: 0 0 auto; min-height: 0; min-width: 0; width: 100%; overflow: hidden; transition: height 120ms ease; }
.timeline-resize-handle { flex: 0 0 12px; width: 100%; display: flex; align-items: center; justify-content: center; cursor: row-resize; background: #eceef1; border-top: 1px solid #d4d7dc; border-bottom: 1px solid #d4d7dc; }
.timeline-resize-handle.collapsed { cursor: default; }
.timeline-collapse { border: 0; border-radius: 4px; background: #fff; color: #333; cursor: pointer; line-height: 14px; padding: 0 12px; font-size: 10px; }
.timeline-collapse:hover { background: #e5e7eb; }
.timeline-empty { height: 100%; display: flex; align-items: flex-start; gap: 12px; padding: 6px 10px; background: #fff; color: #555; font-size: 12px; }
</style>
