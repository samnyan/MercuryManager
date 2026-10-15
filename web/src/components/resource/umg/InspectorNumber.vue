<script setup lang="ts">
import { ref, watch } from 'vue'
import { tr } from '../../../i18n'
const props = defineProps<{ value:number; label:string; step?:number; dirty?:boolean }>()
const emit = defineEmits<{ 'update:value':[value:number]; reset:[] }>()
const text=ref(String(props.value))
watch(()=>props.value,v=>text.value=String(v))
function commit(){const v=Number(text.value);if(text.value.trim() && Number.isFinite(v))emit('update:value',v);else text.value=String(props.value)}
let startX=0,startValue=0
function start(e:PointerEvent){if(e.button!==0)return;startX=e.clientX;startValue=props.value;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)}
function move(e:PointerEvent){if(!(e.currentTarget as HTMLElement).hasPointerCapture(e.pointerId))return;const factor=e.shiftKey?0.1:e.altKey?10:1;emit('update:value',Number((startValue+(e.clientX-startX)*(props.step??1)*factor).toFixed(6)))}
function end(e:PointerEvent){const t=e.currentTarget as HTMLElement;if(t.hasPointerCapture(e.pointerId))t.releasePointerCapture(e.pointerId)}
</script>
<template>
  <div class="number" :class="{dirty}">
    <span class="drag" :title="`${label}: ${tr('ui.umgNumberDragHint')}`" @pointerdown="start" @pointermove="move" @pointerup="end" @pointercancel="end">{{label}}{{dirty?' *':''}}</span>
    <input v-model="text" :aria-label="label" inputmode="decimal" @change="commit" @keydown.enter="commit" @keydown.esc="text=String(value)" />
    <button v-if="dirty" type="button" :aria-label="`${tr('ui.umgNumberResetAria')} ${label}`" @click="emit('reset')">↶</button>
  </div>
</template>
<style scoped>
.number{display:flex;align-items:center;gap:4px;min-width:0;height:24px;font-size:11px}.drag{cursor:ew-resize;touch-action:none;user-select:none;flex:1;overflow-wrap:anywhere}.number input{width:80px;min-width:0;box-sizing:border-box;border:1px solid #8884;border-radius:3px;background:transparent;color:inherit;padding:2px 5px;font:inherit}.dirty{font-weight:700}.dirty input{font-weight:700}.number button{border:0;background:transparent;color:inherit;cursor:pointer;padding:0 3px}
</style>
