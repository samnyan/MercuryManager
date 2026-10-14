<script setup lang="ts">
import { computed } from 'vue'
import { NSelect, NButton } from 'naive-ui'
import { tr } from '../../../i18n'
import type { UMGAnimation, UMGNode, UMGTreeRoot } from './types'
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
const unresolvedTitle = computed(() => unresolvedBindings.value.map(b => b.label || b.widgetName).join('\n'))
const channelCount = computed(() => animation.value?.tracks.reduce((n,t) => n+t.channels.length+(t.materialParameters?.length??0),0) ?? 0)
function togglePlay() { emit('update:playing', !props.playing) }
function clearAnimation() { emit('update:activeAnimationId', null); emit('update:time', 0); emit('update:playing', false) }
</script>
<template>
  <section class="umg-timeline">
    <header class="timeline-header">
      <n-select multiple filterable :value="selectedAnimationIds" :options="animationOptions" :max-tag-count="1" :placeholder="tr('ui.umgAnimationSelect')" size="tiny" style="width:380px;flex-shrink:0" @update:value="emit('update:selectedAnimationIds',$event)" />
      <n-button size="tiny" :disabled="!animations.length" @click="emit('play-entrances')">{{ tr('ui.umgPlayEntrances') }}</n-button>
      <n-button size="tiny" :type="isolate ? 'primary' : 'default'" :title="tr('ui.umgIsolateHint')" @click="emit('update:isolate',!isolate)">{{ tr('ui.umgIsolateAnimation') }}</n-button>
      <n-button size="tiny" :type="repeat ? 'primary' : 'default'" @click="emit('update:repeat',!repeat)">{{ tr('ui.umgRepeatPreview') }}</n-button>
      <n-button size="tiny" quaternary :disabled="!activeAnimationId" @click="clearAnimation">{{ tr('ui.umgAnimationClear') }}</n-button>
      <template v-if="animation">
        <n-button size="tiny" quaternary :title="tr('ui.umgTimelineStart')" @click="emit('update:time', (animation?.start ?? 0))">⏮</n-button>
        <n-button size="tiny" quaternary :title="playing ? tr('ui.umgTimelinePause') : tr('ui.umgTimelinePlay')" @click="togglePlay">{{ playing ? '⏸' : '▶' }}</n-button>
        <span class="time-readout">{{ time.toFixed(3) }}s / {{ animation.end.toFixed(3) }}s</span>
        <span class="track-count">{{ channelCount }} {{ tr('ui.umgAnimationChannels') }}</span>
      </template>
      <span v-if="!animation" class="empty-hint">{{ tr('ui.umgAnimationNoneSelected') }}</span>
    </header>
    <div v-if="animation && unresolvedCount" class="preview-note" :title="unresolvedTitle">{{ tr('ui.umgUnresolvedBindings') }}: {{ unresolvedCount }}</div>
    <UMGTimelineCanvas :animation="animation" :time="time" :playing="playing" @seek="emit('update:time',$event)" />
  </section>
</template>
<style scoped>
.umg-timeline{height:100%;width:100%;flex:1;min-height:0;display:flex;flex-direction:column;border-top:1px solid #455064;background:#fff;color:#222;overflow:hidden}
.timeline-header{flex:0 0 auto;min-height:34px;flex-wrap:wrap;display:flex;align-items:center;gap:8px;padding:0 10px}
.time-readout,.empty-hint,.track-count{font:12px monospace;color:#555;white-space:nowrap}
.preview-note{padding:3px 10px;color:#777;font-size:11px}
</style>
