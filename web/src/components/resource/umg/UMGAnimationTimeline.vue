<script setup lang="ts">
import { computed } from 'vue'
import { NSelect, NButton, NCheckbox, NConfigProvider, darkTheme } from 'naive-ui'
import { tr } from '../../../i18n'
import type { UMGAnimation, UMGNode, UMGTreeRoot } from './types'
import { animationTrackIssue } from './animationCapabilities'
import { matchesAnimationPhase } from './animationPreview'
import UMGTimelineCanvas from './UMGTimelineCanvas.vue'
const props = defineProps<{ animations: UMGAnimation[]; activeAnimationId: string | null; previewAnimation: UMGAnimation | null; selectedAnimationIds: string[]; isolate: boolean; repeat: boolean; time: number; playing: boolean; roots: UMGTreeRoot[]; nodes: UMGNode[] }>()
const emit = defineEmits<{ 'update:selectedAnimationIds': [ids: string[]]; 'play-entrances': []; 'update:isolate': [value: boolean]; 'update:repeat': [value: boolean]; 'update:activeAnimationId': [id: string | null]; 'update:time': [time: number]; 'update:playing': [playing: boolean] }>()
const animation = computed(() => props.previewAnimation)
const animationOptions = computed(() => {
  const groups = new Map<string, { type: 'group'; label: string; key: string; children: {label: string; value: string}[] }>()
  for (const a of props.animations) {
    const key = a.ownerId ?? a.id
    let group = groups.get(key)
    if (!group) {
      group = { type: 'group', key, label: `${a.ownerPath || '?'} · ${a.scope || '?'}`, children: [] }
      groups.set(key, group)
    }
    group.children.push({ value: a.id, label: `${a.ownerPath || '?'} / ${a.name} · ${(a.end-a.start).toFixed(2)}s` })
  }
  return [...groups.values()]
})
const unresolvedBindings = computed(() => animation.value?.bindings.filter(b => !b.nodeId) ?? [])
const unresolvedCount = computed(() => unresolvedBindings.value.length)
const unresolvedTitle = computed(() => unresolvedBindings.value.map(b => `${b.label || b.widgetName}: ${b.resolutionIssue ?? 'Unresolved'}`).join('\n'))
const trackLimitations = computed(() => {
  const a = animation.value
  if (!a) return []
  const bindings = new Map(a.bindings.map(b => [b.guid, b]))
  return a.tracks.flatMap(t => {
    const binding = bindings.get(t.targetGuid)
    if (!binding?.nodeId) return [] // Reported separately as unresolved bindings.
    const issue = animationTrackIssue(t, binding, props.nodes.find(n => n.id === binding.nodeId))
    return issue ? [`${binding.label || binding.widgetName} / ${t.property || t.type}: ${issue}`] : []
  })
})
const channelCount = computed(() => animation.value?.tracks.reduce((n,t) => n+t.channels.length+(t.materialParameters?.length??0),0) ?? 0)
function togglePlay() { emit('update:playing', !props.playing) }
function selectPhase(phase: string) { emit('update:playing', false); emit('update:time', 0); emit('update:selectedAnimationIds', props.animations.filter(a => matchesAnimationPhase(a.name, phase)).map(a => a.id)) }
function clearAnimation() { emit('update:selectedAnimationIds', []); emit('update:activeAnimationId', null); emit('update:time', 0); emit('update:playing', false) }
</script>
<template>
  <n-config-provider :theme="darkTheme" class="umg-timeline">
    <header class="timeline-header">
      <n-button size="tiny" :disabled="!animation" :title="tr('ui.umgTimelineStart')" @click="emit('update:time',animation?.start ?? 0)">⏮</n-button>
      <n-button size="tiny" :disabled="!animation" :title="playing ? tr('ui.umgTimelinePause') : tr('ui.umgTimelinePlay')" @click="togglePlay">{{ playing ? '⏸' : '▶' }}</n-button>
      <span class="time-readout">{{ time.toFixed(3) }}s / {{ (animation?.end ?? 0).toFixed(3) }}s</span>
      <n-select class="animation-select" multiple filterable :value="selectedAnimationIds" :options="animationOptions" :max-tag-count="1" :placeholder="tr('ui.umgAnimationSelect')" size="tiny" @update:value="emit('update:selectedAnimationIds',$event)" />
      <div class="phase-buttons">
        <n-button v-for="phase in ['in','loop','out']" :key="phase" size="tiny" @click="selectPhase(phase)">{{ phase.toUpperCase() }}</n-button>
        <n-button size="tiny" @click="clearAnimation">Clear</n-button>
      </div>
      <n-checkbox size="small" :checked="isolate" @update:checked="emit('update:isolate',$event)">Isolate</n-checkbox>
      <n-checkbox size="small" :checked="repeat" @update:checked="emit('update:repeat',$event)">Repeat</n-checkbox>
      <span class="track-count">{{ channelCount }} channels</span>
      <span v-if="trackLimitations.length" class="preview-note" :title="trackLimitations.join('\n')">Preview limits: {{ trackLimitations.length }} tracks</span>
      <span v-if="unresolvedCount" class="preview-note" :title="unresolvedTitle">Unresolved: {{ unresolvedCount }}</span>
    </header>
    <UMGTimelineCanvas :animation="animation" :time="time" :playing="playing" @seek="emit('update:time',$event)" />
  </n-config-provider>
</template>
<style scoped>
.umg-timeline{height:100%;width:100%;flex:1;min-height:0;display:flex;flex-direction:column;border-top:1px solid #455064;background:#181c24;color:#d6deeb;overflow:hidden}
.timeline-header{flex:0 0 auto;display:flex;align-items:center;gap:5px;padding:4px 8px;flex-wrap:wrap;background:#202632}
.animation-select{flex:1;min-width:180px;max-width:380px}
.phase-buttons{display:flex;gap:2px}
.time-readout,.track-count{font:11px monospace;color:#c4cfdf;white-space:nowrap}
.preview-note{color:#e5b66b;font-size:11px;white-space:nowrap}
.timeline-header :deep(.n-checkbox){align-items:center}
.timeline-header :deep(.n-checkbox__label){font-size:11px;padding-left:4px;line-height:16px}
.timeline-header :deep(.n-checkbox-box-wrapper){height:16px;display:flex;align-items:center}
.timeline-header :deep(.n-button){font-size:11px;padding:0 6px;height:22px}
.timeline-header :deep(.n-base-selection){font-size:11px}
</style>
