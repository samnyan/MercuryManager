<script setup lang="ts">
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { NSelect } from 'naive-ui'
import { tr } from '../../../i18n'
import type { UMGAnimation, UMGNode, UMGTreeRoot } from './types'

const props = defineProps<{ animations: UMGAnimation[]; activeAnimationId: string | null; time: number; playing: boolean; roots: UMGTreeRoot[]; nodes: UMGNode[] }>()
const emit = defineEmits<{ 'update:activeAnimationId': [id: string | null]; 'update:time': [time: number]; 'update:playing': [playing: boolean] }>()
const scrollRef = ref<HTMLElement | null>(null)
const editorRef = ref<HTMLElement | null>(null)
const rulerRef = ref<HTMLElement | null>(null)
const trackListRef = ref<HTMLElement | null>(null)
const inspectorRef = ref<HTMLElement | null>(null)
const inspectorHeight = ref(0)
const hoveredKey = ref<{ track: string; channel: string; frame: number; value: number; interpolation: string } | null>(null)
const animation = computed(() => props.animations.find(item => item.id === props.activeAnimationId) || null)
const previousAnimationId = ref<string | null>(null)
const span = computed(() => Math.max(0.001, (animation.value?.end ?? 0) - (animation.value?.start ?? 0)))
const timelineWidth = computed(() => `${Math.max(800, span.value * 600)}px`)
const tick = computed(() => 1 / (animation.value?.tickRate && animation.value.tickRate > 0 ? animation.value.tickRate : 60))
const contentWidth = computed(() => Math.max(800, span.value * 600))
const keyPercent = (time: number) => Math.max(0, Math.min(contentWidth.value, ((time - (animation.value?.start ?? 0)) / span.value) * contentWidth.value))
function bindingLabel(guid:string) {
  const binding = animation.value?.bindings.find(item => item.guid === guid)
  if (!binding) return tr('ui.umgAnimationUnbound')
  const roots = props.roots.filter(root => props.nodes.some(node => node.treeId === root.treeId && node.name === binding.widgetName))
  const root = roots.find(item => item.kind === 'nested') || roots[0]
  return root ? `${root.ownerName} / ${binding.widgetName}` : binding.widgetName
}
const tickMarks = computed(() => {
  const seconds = span.value
  const raw = seconds / 8
  const magnitude = Math.pow(10, Math.floor(Math.log10(raw || 1)))
  const step = [1, 2, 5, 10].map(v => v * magnitude).find(v => v >= raw) || 10 * magnitude
  const first = Math.ceil((animation.value?.start ?? 0) / step) * step
  const marks: number[] = []
  for (let value = first; value <= (animation.value?.end ?? 0) + step * 0.001; value += step) marks.push(value)
  return marks
})
const tracks = computed(() => {
  if (!animation.value) return []
  return animation.value.tracks.flatMap(track => {
    const rows = track.channels.map(channel => ({ id: `${track.targetGuid}:${track.name}:${channel.property}`, track, label: channel.property, keys: channel.keys }))
    for (const parameter of track.materialParameters || []) rows.push({ id: `${track.targetGuid}:${track.name}:${parameter.name}`, track, label: `${tr('ui.umgAnimationMaterial')}: ${parameter.name}`, keys: parameter.channel.keys })
    if (!rows.length) rows.push({ id: `${track.targetGuid}:${track.name}:empty`, track, label: track.property || track.type, keys: [] })
    return rows
  })
})
const channelTitle = (trackName: string, label: string) => `${bindingLabel(tracks.value.find(row => row.track.name === trackName)?.track.targetGuid || '')} · ${trackName} · ${label}`
let resizeObserver: ResizeObserver | undefined
function syncScroll() {
  const body = scrollRef.value
  if (!body || !animation.value) return
  const x = ((props.time - animation.value.start) / span.value) * contentWidth.value
  const left = Math.min(Math.max(0, contentWidth.value - body.clientWidth), Math.max(0, x - body.clientWidth * 0.45))
  body.scrollLeft = left
  if (rulerRef.value) rulerRef.value.scrollLeft = left
  if (trackListRef.value) trackListRef.value.scrollTop = body.scrollTop
}
function seekFromPointer(event: PointerEvent) {
  if (!scrollRef.value || !animation.value) return
  const lane = (event.target as HTMLElement).closest('.track-lane, .ruler') as HTMLElement | null
  if (!lane) return
  const rect = lane.getBoundingClientRect()
  const localX = event.clientX - rect.left
  const x = scrollRef.value.scrollLeft + localX
  emit('update:time', animation.value.start + Math.max(0, Math.min(1, x / contentWidth.value)) * span.value)
}
function togglePlay() { emit('update:playing', !props.playing) }
function clearAnimation() { emit('update:activeAnimationId', null); emit('update:time', 0); emit('update:playing', false); hoveredKey.value = null }
function showKey(trackName: string, channel: string, frame: number, value: number, interpolation: string, event: PointerEvent) {
  hoveredKey.value = { track: trackName, channel, frame, value, interpolation }
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
  const root = editorRef.value
  const inspector = inspectorRef.value
  if (root && inspector) inspectorHeight.value = Math.max(0, Math.min(root.clientHeight - inspector.offsetHeight, rect.top - root.getBoundingClientRect().top))
}
function hideKey() { hoveredKey.value = null }
function syncInspectorPosition() {
  if (!hoveredKey.value) return
  const dot = editorRef.value?.querySelector<HTMLElement>('.key-dot:hover')
  const root = editorRef.value
  const inspector = inspectorRef.value
  if (!dot || !root || !inspector) return
  inspectorHeight.value = Math.max(0, Math.min(root.clientHeight - inspector.offsetHeight, dot.getBoundingClientRect().top - root.getBoundingClientRect().top))
}
watch(() => [props.time, animation.value?.id], () => {
  if (previousAnimationId.value !== animation.value?.id) {
    previousAnimationId.value = animation.value?.id || null
    requestAnimationFrame(() => { syncScroll(); if (trackListRef.value) trackListRef.value.scrollTop = 0 })
  } else syncScroll()
}, { flush: 'post' })
watch(() => animation.value?.id, clearHover => { void clearHover; hoveredKey.value = null })
watch(() => props.activeAnimationId, () => { emit('update:playing', false); hoveredKey.value = null })
onBeforeUnmount(() => resizeObserver?.disconnect())
</script>

<template>
  <section class="umg-timeline">
    <header class="timeline-header">
      <n-select v-if="animations.length" :value="activeAnimationId" :options="animations.map(a => ({ label: a.name, value: a.id }))" :placeholder="tr('ui.umgAnimationSelect')" size="tiny" style="width:190px" @update:value="emit('update:activeAnimationId',$event)" />
      <span v-else class="empty-hint">{{ tr('ui.umgAnimationNoneSelected') }}</span>
      <n-button size="tiny" quaternary :disabled="!activeAnimationId" @click="clearAnimation">{{ tr('ui.umgAnimationClear') }}</n-button>
      <template v-if="animation">
        <n-button size="tiny" quaternary :title="tr('ui.umgTimelineStart')" @click="emit('update:time', animation!.start)">⏮</n-button>
        <n-button size="tiny" quaternary :title="playing ? tr('ui.umgTimelinePause') : tr('ui.umgTimelinePlay')" @click="togglePlay">{{ playing ? '⏸' : '▶' }}</n-button>
        <span class="time-readout">{{ time.toFixed(3) }}s / {{ animation.end.toFixed(3) }}s</span>
        <span class="track-count">{{ tracks.length }} {{ tr('ui.umgAnimationChannels') }}</span>
      </template>
      <span v-if="!animation" class="empty-hint">{{ tr('ui.umgAnimationNoneSelected') }}</span>
    </header>
    <div class="timeline-editor" ref="editorRef">
      <div class="track-list" ref="trackListRef" @scroll="event => { if (scrollRef) scrollRef.scrollTop = (event.target as HTMLElement).scrollTop }">
        <div class="track-list-spacer"></div>
        <div v-for="row in tracks" :key="row.id" class="track-row-label">
          <span class="binding-scope">{{ tr('ui.umgAnimationBindingScope') }}</span><b>{{ bindingLabel(row.track.targetGuid) }}</b>
          <small :title="channelTitle(row.track.name, row.label)">{{ row.track.property || row.track.name }}</small>
          <small class="channel-label" :title="row.label">{{ row.label }}</small>
        </div>
      </div>
      <div class="timeline-scroll" ref="scrollRef" @scroll="event => { if (trackListRef) trackListRef.scrollTop = (event.target as HTMLElement).scrollTop; if (rulerRef) rulerRef.scrollLeft = (event.target as HTMLElement).scrollLeft; syncInspectorPosition() }" @pointerdown="seekFromPointer">
        <div class="timeline-content" :style="{ width: timelineWidth }">
          <div ref="rulerRef" class="ruler" :style="{ width: `max(100%, ${timelineWidth})` }">
            <span v-for="mark in tickMarks" :key="mark" :style="{ left: `${((mark - animation!.start) / span) * contentWidth}px` }">{{ mark.toFixed(2) }}</span>
          </div>
          <div class="track-rows">
            <div v-for="row in tracks" :key="row.id" class="track-lane">
              <span v-for="(key,index) in row.keys" :key="`${row.id}-${index}`" class="key-dot" :title="`${row.track.name} · ${row.label}`" @pointerenter="showKey(row.track.name, row.label, key.frame, key.value, key.interpolation, $event)" @pointerleave="hideKey" :style="{ left: `${((key.frame - animation!.start) / span) * contentWidth}px` }" />
            </div>
          </div>
          <div class="playhead" :style="{ left: `${Math.max(0, Math.min(contentWidth, ((time - animation!.start) / span) * contentWidth))}px` }"><span class="playhead-head"></span></div>
        </div>
      </div>
    </div>
    <div v-if="hoveredKey && animation" ref="inspectorRef" class="key-inspector" :style="{ top: `${inspectorHeight}px` }">
      <b>{{ bindingLabel(tracks.find(row => row.track.name === hoveredKey!.track)?.track.targetGuid || '') }} · {{ hoveredKey.track }}</b>
      <span>{{ hoveredKey.channel }}</span><span>{{ hoveredKey.frame.toFixed(4) }}s</span>
      <strong>{{ hoveredKey.value }}</strong><small>{{ hoveredKey.interpolation }}</small>
    </div>
  </section>
</template>

<style scoped>
.umg-timeline { width:100%; flex:1; min-height:0; display:flex; flex-direction:column; position:relative; border-top:1px solid #d9d9d9; background:#fff; color:#222; overflow:hidden; }
.timeline-header { flex:0 0 34px; height:34px; display:flex; align-items:center; gap:8px; padding:0 10px; overflow:hidden; }
.time-readout,.empty-hint,.track-count { font:12px monospace; color:#555; white-space:nowrap; }
.binding-scope { font:9px sans-serif; color:#6b7280; flex:0 0 62px; }
.timeline-editor { display:grid; grid-template-columns:210px minmax(0,1fr); flex:1; min-height:0; position:relative; overflow:hidden; }
.track-list { min-height:0; overflow:hidden auto; background:#f8f9fa; border-right:1px solid #d9d9d9; z-index:3; }
.track-list-spacer { height:24px; position:sticky; top:0; background:#f3f4f6; border-bottom:1px solid #d9d9d9; z-index:1; }
.track-row-label { height:29px; display:flex; align-items:center; gap:6px; padding:0 6px; overflow:hidden; border-bottom:1px solid #e5e7eb; white-space:nowrap; }
.track-row-label b { flex:0 0 66px; overflow:hidden; text-overflow:ellipsis; font-size:11px; color:#222; }
.track-row-label small { overflow:hidden; text-overflow:ellipsis; font-size:10px; color:#555; }
.track-row-label .channel-label { flex:1; }
.timeline-scroll { min-width:0; min-height:0; overflow:auto; cursor:crosshair; background:#fff; }
.timeline-content { min-height:100%; position:relative; }
.ruler { height:24px; position:sticky; top:0; background:#f3f4f6; border-bottom:1px solid #d9d9d9; z-index:2; overflow:hidden; }
.ruler span { position:absolute; top:4px; transform:translateX(-50%); font:10px monospace; color:#444; white-space:nowrap; }
.track-rows { position:relative; }
.track-lane { height:29px; position:relative; border-bottom:1px solid #e5e7eb; }
.key-dot { position:absolute; top:10px; width:8px; height:8px; background:#d49c00; transform:translateX(-50%) rotate(45deg); z-index:2; cursor:pointer; }
.key-dot:hover { width:11px; height:11px; top:8px; background:#f0b400; }
.playhead { position:absolute; top:24px; bottom:0; width:1px; background:#e53935; pointer-events:none; z-index:4; }
.playhead-head { position:absolute; top:0; left:-5px; width:11px; height:8px; background:#e53935; clip-path:polygon(0 0,100% 0,50% 100%); }
.key-inspector { position:absolute; left:218px; z-index:8; display:flex; align-items:center; gap:9px; max-width:calc(100% - 226px); padding:6px 9px; border:1px solid #c7cbd1; border-radius:4px; background:rgba(255,255,255,.97); box-shadow:0 2px 8px #0002; font-size:11px; pointer-events:none; }
.key-inspector b,.key-inspector span,.key-inspector small { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.key-inspector strong { color:#975e00; font:600 12px monospace; }

</style>
