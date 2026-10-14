<script setup lang="ts">
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { NTag, NButton, NSpin, NAlert, NModal, NSplit } from 'naive-ui'
import { useProject } from '../../../project'
import { tr } from '../../../i18n'
import type { UMGAnimation, UMGDocument, UMGNode } from './types'
import { computeTreeLayout } from './layout'
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
// Scrubbing pauses playback; timer updates must not pause themselves.
watch(animationFrame, () => { if (!advancingAnimation) setAnimationPlaying(false) }, { flush: 'sync' })
let advancingAnimation = false
let animationTimer: number | undefined
const animationsForRoot = computed(() => doc.value?.animations || [])
const activeAnimation = computed(() => doc.value?.animations?.find(a => a.id === activeAnimationId.value) || null)
watch(activeAnimationId, () => {
  animationFrame.value = activeAnimation.value?.start || 0
  setAnimationPlaying(false)
})
watch(() => [activeAnimation.value?.id, activeRoot.value?.treeId, hiddenIds.value], () => {
  const animation = activeAnimation.value
  animationSoloHiddenIds.value = new Set()
  if (!animation || !doc.value || !activeRoot.value) return
  const treeNodes = doc.value.nodes.filter(node => node.treeId === activeRoot.value?.treeId)
  const validBindings = animation.bindings.filter(binding => treeNodes.some(node => node.name === binding.widgetName))
  const targetNames = new Set(validBindings.map(binding => binding.widgetName))
  const targets = new Set(treeNodes.filter(node => targetNames.has(node.name)).map(node => node.id))
  if (!targets.size) return
  const keepVisible = new Set<string>()
  for (const id of targets) keepVisible.add(id)
  for (const node of treeNodes) if (!hiddenIds.value.has(node.id)) keepVisible.add(node.id)
  for (const id of [...keepVisible]) {
    let current = treeNodes.find(node => node.id === id)
    while (current?.parentId) {
      keepVisible.add(current.parentId)
      current = treeNodes.find(node => node.id === current!.parentId)
    }
  }
  animationSoloHiddenIds.value = new Set(treeNodes.filter(node => !keepVisible.has(node.id)).map(node => node.id))
}, { immediate: true })
function sampleChannel(channel: {defaultValue:number;hasDefault:boolean;keys:{frame:number;value:number;interpolation:string;arriveTangent:number;leaveTangent:number}[]}, time:number): number | null {
  const keys=channel.keys
  if(!keys.length) return channel.hasDefault?channel.defaultValue:null
  const right=keys.findIndex(key=>key.frame>=time)
  if(right<0) return keys[keys.length-1].value
  if(right===0) return keys[0].value
  const left=keys[right-1],next=keys[right],duration=next.frame-left.frame
  if(duration<=0) return next.value
  const t=(time-left.frame)/duration
  if(left.interpolation.toLowerCase().includes('cubic')){const t2=t*t,t3=t2*t;return (2*t3-3*t2+1)*left.value+(t3-2*t2+t)*(left.leaveTangent||0)*duration+(-2*t3+3*t2)*next.value+(t3-t2)*(next.arriveTangent||0)*duration}
  return left.value+(next.value-left.value)*t
}
const animationMaterialParameters = computed(() => {
  const result: Record<string, Record<string, number>> = {}
  const a=activeAnimation.value
  if(!a) return result
  for(const track of a.tracks){
    if (!track.materialParameters?.length) continue
    const binding=a.bindings.find(item=>item.guid===track.targetGuid)
    const node=binding&&doc.value?.nodes.find(item=>item.name===binding.widgetName&&item.treeId===activeRoot.value?.treeId)
    if(!node) continue
    const parameters=result[node.id]||(result[node.id]={})
    for(const parameter of track.materialParameters){const value=sampleChannel(parameter.channel,animationFrame.value);if(value!==null)parameters[parameter.name]=value}
  }
  return result
})
const previewNodes = computed(() => {
  if (!doc.value) return []
  const combinedHidden = new Set([...hiddenIds.value, ...animationSoloHiddenIds.value])
  if (!activeAnimation.value) return doc.value.nodes.filter(node => !animationSoloHiddenIds.value.has(node.id))
  const names = new Set(activeAnimation.value.bindings.map(binding => binding.widgetName).filter(Boolean))
  const treeNodes = doc.value.nodes.filter(node => node.treeId === activeRoot.value?.treeId)
  const targets = new Set(treeNodes.filter(node => names.has(node.name)).map(node => node.id))
  const visible = new Set(treeNodes.filter(node => !combinedHidden.has(node.id)).map(node => node.id))
  if (!targets.size) return doc.value.nodes.filter(node => !combinedHidden.has(node.id))
  for (const id of targets) visible.add(id)
  for (const id of [...visible]) {
    let current = treeNodes.find(node => node.id === id)
    while (current?.parentId) {
      visible.add(current.parentId)
      current = treeNodes.find(node => node.id === current!.parentId)
    }
  }
  const clones = new Map((doc.value.nodes.filter(node => visible.has(node.id))).map(node => [node.id, {
    ...node,
    children: node.children.filter(id => visible.has(id)),
    visual: {
      ...node.visual,
      RenderTransform: node.visual.RenderTransform ? {
        ...node.visual.RenderTransform,
        Translation: { ...node.visual.RenderTransform.Translation },
        Scale: { ...node.visual.RenderTransform.Scale },
        Shear: { ...node.visual.RenderTransform.Shear }
      } : undefined
    }
  }]))
  for(const track of activeAnimation.value.tracks){
    const binding=activeAnimation.value.bindings.find(b=>b.guid===track.targetGuid)
    if(!binding) continue
    const matchingNodes=[...clones.values()].filter(n=>n.name===binding.widgetName&&n.treeId===activeRoot.value?.treeId)
    const node=matchingNodes.find(n=>n.id===selectedNodeId.value)||matchingNodes[0]
    if(!node) continue
    const values=new Map(track.channels.map(c=>[c.property,sampleChannel(c,animationFrame.value)]))
    if(track.type.includes('Transform')){
      const axis=(name:string,index:number)=>values.get(`${name}.${index}`)??values.get(`${name}${index===0?'X':'Y'}`)??null
      const translation={X:axis('Translation',0)??node.visual.RenderTransform?.Translation?.X??0,Y:axis('Translation',1)??node.visual.RenderTransform?.Translation?.Y??0}
      const scale={X:axis('Scale',0)??node.visual.RenderTransform?.Scale?.X??1,Y:axis('Scale',1)??node.visual.RenderTransform?.Scale?.Y??1}
      const shear={X:axis('Shear',0)??node.visual.RenderTransform?.Shear?.X??0,Y:axis('Shear',1)??node.visual.RenderTransform?.Shear?.Y??0}
      node.visual.RenderTransform={...(node.visual.RenderTransform||{}),Translation:translation,Scale:scale,Shear:shear,Angle:values.get('Rotation.0')??values.get('Rotation')??node.visual.RenderTransform?.Angle??0}
    } else if(track.type.includes('Float')&&track.property==='RenderOpacity') node.visual.RenderOpacity=values.get('FloatCurve')??node.visual.RenderOpacity
  }
  return [...clones.values()]
})
function setAnimationPlaying(playing: boolean) {
  animationPlaying.value = playing
  if (animationTimer) window.cancelAnimationFrame(animationTimer)
  animationTimer = undefined
  if (!playing) return
  let last = performance.now()
  const tick = (now: number) => {
    const delta = Math.min(0.1, Math.max(0, (now - last) / 1000))
    last = now
    const a = activeAnimation.value
    if (!a || a.end <= a.start) { setAnimationPlaying(false); return }
    advancingAnimation = true
    try {
      const next = animationFrame.value + delta
      animationFrame.value = next >= a.end ? a.start + (next - a.start) % (a.end - a.start) : next
    } finally { advancingAnimation = false }
    if (animationPlaying.value) animationTimer = window.requestAnimationFrame(tick)
  }
  animationTimer = window.requestAnimationFrame(tick)
}
function toggleAnimation() { setAnimationPlaying(!animationPlaying.value) }
watch(activeAnimationId,()=>{animationFrame.value=activeAnimation.value?.start||0;animationPlaying.value=false;if(animationTimer)window.cancelAnimationFrame(animationTimer)})
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
  if (activeAnimation.value) {
    const current = activeAnimationId.value
    activeAnimationId.value = null
    requestAnimationFrame(() => { activeAnimationId.value = current })
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
            :material-time="animationPlaying ? animationFrame : undefined"
            :selected-id="selectedNodeId"
            :hidden-ids="new Set([...hiddenIds, ...animationSoloHiddenIds])"
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
          :time="animationFrame"
          :playing="animationPlaying"
          :roots="doc.roots"
          :nodes="doc.nodes"
          @update:active-animation-id="activeAnimationId = $event"
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
