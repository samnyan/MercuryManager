<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NButton, NSelect } from 'naive-ui'
import { tr } from '../../../i18n'
import type { UMGAnimation } from './types'

const props = defineProps<{ animations: UMGAnimation[]; activeAnimationId: string | null; time: number; playing: boolean }>()
const emit = defineEmits<{ 'update:activeAnimationId': [id: string | null]; 'update:time': [time: number]; 'update:playing': [playing: boolean] }>()
const expanded = ref(true)
const scrollRef = ref<HTMLElement | null>(null)
const animation = computed(() => props.animations.find(item => item.id === props.activeAnimationId) || null)
const span = computed(() => Math.max(0.001, (animation.value?.end ?? 0) - (animation.value?.start ?? 0)))
const timelineWidth = computed(() => `${Math.max(800,span.value * 300)}px`)
const tick = computed(() => 1 / (animation.value?.tickRate && animation.value.tickRate > 0 ? animation.value.tickRate : 60))
const percent = (time: number) => `${Math.max(0, Math.min(100, (time - (animation.value?.start ?? 0)) / span.value * 100))}%`
const percentCss = (time:number) => `${Math.max(0,(time-(animation.value?.start ?? 0))/span.value*100)}%`
const bindingLabel = (guid:string) => animation.value?.bindings.find(binding=>binding.guid===guid)?.widgetName || tr('ui.umgAnimationUnbound')
function togglePlay() { emit('update:playing', !props.playing) }
// Playback has one clock, owned by UMGDesigner; this component only emits user input.
watch(() => [props.time, animation.value?.id], () => {
  const el = scrollRef.value
  if (!el || !animation.value) return
  const x = (props.time - animation.value.start) / span.value * el.scrollWidth
  el.scrollLeft = Math.max(0, x - el.clientWidth * 0.45)
})
</script>

<template>
  <section class="umg-timeline" :class="{ collapsed: !expanded }">
    <header class="timeline-header">
      <n-button size="tiny" quaternary @click="expanded = !expanded">{{ expanded ? '▾' : '▴' }} {{ tr('ui.umgTimeline') }}</n-button>
      <n-select v-if="animations.length" :value="activeAnimationId" :options="animations.map(a => ({ label: a.name, value: a.id }))" :placeholder="tr('ui.umgAnimationSelect')" size="tiny" style="width:190px" @update:value="emit('update:activeAnimationId',$event)" />
      <template v-if="animation">
        <n-button size="tiny" quaternary :title="tr('ui.umgTimelineStart')" @click="emit('update:time', animation.start)">⏮</n-button>
        <n-button size="tiny" quaternary :title="playing ? tr('ui.umgTimelinePause') : tr('ui.umgTimelinePlay')" @click="togglePlay">{{ playing ? '⏸' : '▶' }}</n-button>
        <span class="time-readout">{{ time.toFixed(3) }}s / {{ animation.end.toFixed(3) }}s</span>
      </template>
      <span v-else class="empty-hint">{{ tr('ui.umgAnimationNone') }}</span>
    </header>
    <div v-if="expanded && animation" ref="scrollRef" class="timeline-body" :style="{ '--timeline-width': timelineWidth }" @click="event => { const selected=animation!;const rect=(event.currentTarget as HTMLElement).getBoundingClientRect(); emit('update:time', selected.start + Math.max(0,Math.min(1,(event.clientX-rect.left)/(event.currentTarget as HTMLElement).scrollWidth))*span) }">
      <div class="timeline-content" :style="{ width: timelineWidth }">
      <div class="ruler" :style="{ width: timelineWidth }"><span v-for="i in Math.max(2,Math.ceil(span / 5)+1)" :key="i" :style="{ left: `${(i-1)/(Math.max(2,Math.ceil(span / 5)))*100}%` }">{{ (animation.start + span*(i-1)/Math.max(2,Math.ceil(span / 5))).toFixed(2) }}</span></div>
      <div v-for="track in animation.tracks" :key="track.targetGuid + track.name" class="track-row">
        <div class="track-label"><b>{{ bindingLabel(track.targetGuid) }}</b><small>{{ track.name }} · {{ track.property || track.type }}</small></div>
        <div class="track-lane">
          <template v-for="channel in track.channels" :key="channel.property">
            <span v-for="(key,index) in channel.keys" :key="`${channel.property}-${index}`" class="key-dot" :title="`${channel.property} · ${key.frame.toFixed(3)}s · ${key.value}`" :style="{ left: percent(key.frame) }" />
          </template>
          <template v-for="parameter in track.materialParameters || []" :key="parameter.name">
            <span v-for="(key,index) in parameter.channel.keys" :key="`${parameter.name}-${index}`" class="key-dot material-key" :title="`${parameter.name} · ${key.frame.toFixed(3)}s · ${key.value}`" :style="{ left: percent(key.frame) }" />
          </template>
        </div>
      </div>
      <div class="playhead" :style="{ left: percentCss(time) }" />
      </div>
    </div>
  </section>
</template>

<style scoped>
.umg-timeline{flex:none;border-top:1px solid #d9d9d9;background:#fff;color:#222}.timeline-header{height:34px;display:flex;align-items:center;gap:8px;padding:0 10px}.time-readout,.empty-hint{font:12px monospace;color:#555}.timeline-body{height:220px;overflow:auto;position:relative;padding:0;cursor:crosshair;background:#fff}.timeline-content{position:relative;min-height:100%;padding-left:210px}.ruler{height:24px;position:sticky;top:0;background:#f3f4f6;border-bottom:1px solid #d9d9d9;z-index:2}.ruler span{position:absolute;transform:translateX(-50%);font:10px monospace;color:#444}.track-row{height:29px;position:relative;border-bottom:1px solid #e5e7eb}.track-label{position:sticky;left:0;width:202px;height:28px;display:flex;align-items:center;gap:7px;z-index:1;background:#f8f9fa;overflow:hidden;margin-left:-210px}.track-label b{font-size:11px;white-space:nowrap;color:#222}.track-label small{font-size:10px;color:#555;overflow:hidden;text-overflow:ellipsis}.track-lane{position:absolute;inset:0}.key-dot{position:absolute;top:10px;width:8px;height:8px;background:#d49c00;transform:translateX(-50%) rotate(45deg);z-index:3}.material-key{background:#16858a}.playhead{position:absolute;top:24px;bottom:0;width:1px;background:#e53935;pointer-events:none;z-index:4}.collapsed .timeline-header{height:30px}
</style>
