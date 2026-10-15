<script setup lang="ts">
import { computed, ref, watch, onMounted, onBeforeUnmount } from 'vue'
import { NButton } from 'naive-ui'
import { tr } from '../../../i18n'
import type { UMGAnimation } from './types'

type Channel = UMGAnimation['tracks'][number]['channels'][number]
type Key = Channel['keys'][number]
const props = defineProps<{ animation: UMGAnimation | null; time: number; playing: boolean }>()
const emit = defineEmits<{ seek: [time: number] }>()
const host = ref<HTMLElement>()
const canvas = ref<HTMLCanvasElement>()
const zoom = ref(100)
const follow = ref(false)
const selected = ref<{ row: number; index: number } | null>(null)
const rows = computed(() => {
  const bindings = new Map(props.animation?.bindings.map(b => [b.guid, b]) ?? [])
  return props.animation?.tracks.flatMap((t, ti) => {
    const b = bindings.get(t.targetGuid)
    const owner = b?.label || b?.widgetName || '?'
    return [...t.channels.map(c => ({ label: `${t.property || t.type} / ${c.property}`, channel: c })),
      ...(t.materialParameters ?? []).map(p => ({ label: `Material / ${p.name}`, channel: p.channel }))]
      .map((c, ci) => ({ id: `${ti}:${ci}`, owner, label: c.label, keys: [...c.channel.keys].sort((a,b) => a.frame-b.frame) }))
  }) ?? []
})
const detail = computed(() => {
  const s = selected.value, row = s && rows.value[s.row], key = row && row.keys[s!.index]
  return row && key ? { row, key, previous: row.keys[s!.index-1] } : null
})
const start = computed(() => props.animation?.start ?? 0)
const duration = computed(() => Math.max(.001, (props.animation?.end ?? 0)-start.value))
let width=0, height=0, left=0, top=0, frame=0, observer: ResizeObserver | undefined
let drag: { id:number; mode:'pan'|'seek'; x:number; y:number; left:number; top:number } | null = null
const labelWidth = () => Math.min(330, width * .43)
const rulerHeight=26, rowHeight=34
const fmt = (n:number) => Number.isFinite(n) ? Number(n.toPrecision(7)).toString() : String(n)
function lower(keys: Key[], time:number) { let lo=0,hi=keys.length; while(lo<hi){const m=(lo+hi)>>>1;if(keys[m].frame<time)lo=m+1;else hi=m}return lo }
function clampScroll() {
  left=Math.max(0,Math.min(left,Math.max(0,duration.value*zoom.value-(width-labelWidth())+40)))
  top=Math.max(0,Math.min(top,Math.max(0,rows.value.length*rowHeight-(height-rulerHeight))))
}
function schedule() { if(!frame) frame=requestAnimationFrame(draw) }
function draw() {
  frame=0
  const drawStarted=performance.now()
  const el=canvas.value, ctx=el?.getContext('2d'); if(!el||!ctx)return
  const dpr=window.devicePixelRatio||1
  if(el.width!==Math.round(width*dpr)||el.height!==Math.round(height*dpr)){el.width=Math.round(width*dpr);el.height=Math.round(height*dpr)}
  ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,width,height)
  const label=labelWidth(), lane=width-label
  ctx.fillStyle='#181c24';ctx.fillRect(0,0,width,height)
  ctx.font='11px monospace';ctx.textBaseline='middle'
  const first=Math.max(0,Math.floor(top/rowHeight)), last=Math.min(rows.value.length,Math.ceil((top+height-rulerHeight)/rowHeight))
  let painted=0
  for(let i=first;i<last;i++) {
    const row=rows.value[i], y=rulerHeight+i*rowHeight-top
    ctx.fillStyle=selected.value?.row===i?'#293a50':i%2?'#202630':'#1b212b';ctx.fillRect(0,y,width,rowHeight-1)
    ctx.save();ctx.beginPath();ctx.rect(8,y,label-16,rowHeight);ctx.clip()
    ctx.fillStyle='#d4dcea';ctx.fillText(row.owner,8,y+10);ctx.fillStyle='#92a7bf';ctx.fillText(row.label,8,y+24);ctx.restore()
    ctx.save();ctx.beginPath();ctx.rect(label,y,lane,rowHeight);ctx.clip()
    const a=lower(row.keys,start.value+(left-8)/zoom.value), end=start.value+(left+lane+8)/zoom.value
    let lastPixel=-Infinity
    for(let k=a;k<row.keys.length&&row.keys[k].frame<=end;k++) {
      const key=row.keys[k],x=label+(key.frame-start.value)*zoom.value-left
      const chosen=selected.value?.row===i&&selected.value.index===k
      // Dense keys share a pixel: preserve selection, bound drawing by viewport width.
      if(!chosen&&Math.floor(x)===lastPixel)continue
      lastPixel=Math.floor(x);painted++
      ctx.fillStyle=chosen?'#fff3a3':'#e9b950';ctx.beginPath();ctx.moveTo(x,y+11);ctx.lineTo(x+5,y+17);ctx.lineTo(x,y+23);ctx.lineTo(x-5,y+17);ctx.closePath();ctx.fill()
      const next=row.keys[k+1];if(!next||(next.frame-key.frame)*zoom.value>72){ctx.fillStyle='#d1d9e5';ctx.fillText(fmt(key.value),x+9,y+17)}
    }
    ctx.restore()
  }
  ctx.save();ctx.beginPath();ctx.rect(label,0,lane,height);ctx.clip()
  const raw=75/zoom.value, magnitude=10**Math.floor(Math.log10(raw)),step=([1,2,5,10].find(n=>n*magnitude>=raw)??10)*magnitude
  const min=start.value+left/zoom.value,max=min+lane/zoom.value
  for(let t=Math.ceil(min/step)*step;t<=max;t+=step){const x=label+(t-start.value)*zoom.value-left;ctx.strokeStyle='#344052';ctx.beginPath();ctx.moveTo(x,rulerHeight);ctx.lineTo(x,height);ctx.stroke()}
  ctx.fillStyle='#2c3441';ctx.fillRect(label,0,lane,rulerHeight)
  ctx.fillStyle='#c6d1df';for(let t=Math.ceil(min/step)*step;t<=max;t+=step)ctx.fillText(`${Number(t.toFixed(4))}s`,label+(t-start.value)*zoom.value-left+4,13)
  const x=label+(props.time-start.value)*zoom.value-left
  ctx.strokeStyle='#ff646d';ctx.beginPath();ctx.moveTo(x,0);ctx.lineTo(x,height);ctx.stroke();ctx.fillStyle='#ff646d';ctx.fillRect(x-4,0,8,7);ctx.restore()
  ctx.fillStyle='#2c3441';ctx.fillRect(0,0,label,rulerHeight);ctx.fillStyle='#c6d1df';ctx.fillText(`${tr('ui.umgTimelineControlsParams')} · ${rows.value.length} ${tr('ui.umgTimelineTracks')}`,8,13)
  el.dataset.drawMs=(performance.now()-drawStarted).toFixed(3)
  el.dataset.visibleRows=String(last-first);el.dataset.paintedKeys=String(painted);el.dataset.offset=String(left);el.dataset.verticalOffset=String(top)
}
function position(e:PointerEvent) { const r=canvas.value!.getBoundingClientRect();return {x:e.clientX-r.left,y:e.clientY-r.top} }
function seek(x:number) { if(props.animation)emit('seek',Math.max(start.value,Math.min(props.animation.end,start.value+(x-labelWidth()+left)/zoom.value))) }
function down(e:PointerEvent) {
  if(e.button!==0&&e.button!==1)return
  const p=position(e);e.preventDefault();canvas.value?.focus();canvas.value?.setPointerCapture(e.pointerId)
  if(e.button===1||e.altKey||p.x<labelWidth()) {drag={id:e.pointerId,mode:'pan',...p,left,top};return}
  const rowIndex=Math.floor((p.y-rulerHeight+top)/rowHeight),row=rows.value[rowIndex]
  if(p.y>=rulerHeight&&row){const t=start.value+(p.x-labelWidth()+left)/zoom.value,idx=lower(row.keys,t);const candidates=[idx-1,idx].filter(i=>i>=0&&i<row.keys.length);const nearest=candidates.sort((a,b)=>Math.abs(row.keys[a].frame-t)-Math.abs(row.keys[b].frame-t))[0];if(nearest!==undefined&&Math.abs(row.keys[nearest].frame-t)*zoom.value<=8){selected.value={row:rowIndex,index:nearest};emit('seek',row.keys[nearest].frame);schedule();return}}
  drag={id:e.pointerId,mode:'seek',...p,left,top};seek(p.x)
}
function move(e:PointerEvent) {if(!drag)return;const p=position(e);if(drag.mode==='pan'){left=drag.left+drag.x-p.x;top=drag.top+drag.y-p.y;clampScroll();schedule()}else seek(p.x)}
function up(e:PointerEvent) {drag=null;if(canvas.value?.hasPointerCapture(e.pointerId))canvas.value.releasePointerCapture(e.pointerId)}
function setZoom(value:number,anchor=(width-labelWidth())/2) {const t=(left+anchor)/zoom.value;zoom.value=Math.max(5,Math.min(4000,value));left=t*zoom.value-anchor;clampScroll();schedule()}
function wheel(e:WheelEvent) {e.preventDefault();if(e.ctrlKey||e.metaKey){const r=canvas.value!.getBoundingClientRect();setZoom(zoom.value*Math.exp(-e.deltaY*.005),Math.max(0,e.clientX-r.left-labelWidth()))}else {left+=e.shiftKey?e.deltaY:e.deltaX;top+=e.shiftKey?0:e.deltaY;clampScroll();schedule()}}
function fit() {zoom.value=Math.max(5,Math.min(4000,(width-labelWidth()-30)/duration.value));left=0;clampScroll();schedule()}
function keydown(e:KeyboardEvent) {if(e.code==='Space'){e.preventDefault();follow.value=!follow.value}else if(e.key==='ArrowLeft'||e.key==='ArrowRight'){e.preventDefault();emit('seek',Math.max(start.value,Math.min(props.animation?.end??0,props.time+(e.key==='ArrowLeft'?-1:1)/(props.animation?.displayRate||60))))}}
watch(()=>props.time,()=>{if(follow.value&&props.playing&&!drag){const x=(props.time-start.value)*zoom.value-left;if(x>width-labelWidth()-20||x<0){left=(props.time-start.value)*zoom.value;clampScroll()}}schedule()})
watch(()=>props.animation,()=>{selected.value=null;top=0;fit()})
onMounted(()=>{observer=new ResizeObserver(()=>{const r=host.value!.getBoundingClientRect();width=r.width;height=r.height;clampScroll();schedule()});observer.observe(host.value!);fit()})
onBeforeUnmount(()=>{observer?.disconnect();cancelAnimationFrame(frame)})
</script>
<template>
  <div class="canvas-timeline">
    <div ref="host" class="viewport"><canvas ref="canvas" tabindex="0" :aria-label="tr('ui.umgTimelineAria')" @pointerdown="down" @pointermove="move" @pointerup="up" @pointercancel="up" @lostpointercapture="drag=null" @wheel="wheel" @keydown="keydown" /></div>
    <div class="key-detail" v-if="detail"><strong :title="detail.row.owner">{{ detail.row.owner }}</strong><span>{{ detail.row.label }}</span><span>{{ fmt(detail.key.frame) }}s</span><b>{{ tr('ui.umgTimelineVal') }} {{ fmt(detail.key.value) }}</b><span v-if="detail.previous">{{ tr('ui.umgTimelinePrevKey') }} {{ fmt(detail.previous.value) }} → {{ fmt(detail.key.value) }}（Δ {{ fmt(detail.key.value-detail.previous.value) }}）</span><span>{{ detail.key.interpolation }}</span><span :title="`Arrive: ${detail.key.arriveTangent}; Leave: ${detail.key.leaveTangent}`">Tangent {{ fmt(detail.key.arriveTangent) }} / {{ fmt(detail.key.leaveTangent) }}</span></div>
    <div class="tools"><n-button class="zoom-step" size="tiny" aria-label="Zoom out" @click="setZoom(zoom/1.4)">−</n-button><n-button size="tiny" @click="fit">{{ tr('ui.umgTimelineFit') }}</n-button><n-button class="zoom-step" size="tiny" aria-label="Zoom in" @click="setZoom(zoom*1.4)">+</n-button><span>{{ zoom.toFixed(0) }} px/s</span><label><input v-model="follow" type="checkbox">{{ tr('ui.umgTimelineFollow') }}</label><span class="hint">{{ tr('ui.umgTimelineHint') }}</span></div>
  </div>
</template>
<style scoped>
.canvas-timeline{display:flex;flex-direction:column;flex:1;min-height:0;background:#181c24;color:#d6deeb}.tools{display:flex;align-items:center;gap:8px;padding:3px 8px;font-size:11px;flex-wrap:wrap}.tools :deep(.n-button){height:22px;font-size:11px;padding:0 6px}.tools :deep(.zoom-step){width:22px;min-width:22px;padding:0}.tools label{display:flex;align-items:center;gap:4px}.hint{color:#9baac0;font-size:11px}.viewport{flex:1;min-height:30px;position:relative;overflow:hidden}.viewport canvas{position:absolute;width:100%;height:100%;touch-action:none;outline:none;cursor:crosshair}.viewport canvas:focus{outline:1px solid #6293ce;outline-offset:-1px}.key-detail{flex:0 0 auto;min-height:25px;padding:4px 8px;display:flex;gap:12px;flex-wrap:wrap;align-items:center;border-top:1px solid #354154;font-size:11px}.key-detail strong{max-width:260px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.key-detail b{color:#ffe19a}
</style>
