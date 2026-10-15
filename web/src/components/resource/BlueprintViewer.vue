<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NTree, NInput, NSpin, NButton, NSelect } from 'naive-ui'
import { tr } from '../../i18n'
import GenericResourceViewer from './GenericResourceViewer.vue'
import TexturePreviewModal from './TexturePreviewModal.vue'
import { api, useProject } from '../../project'
type Node={index:number;name:string;type:string;group:string;outerIndex:number;parentIndex:number|null;slotIndex:number|null}
type Reference={ContentFile:string;Class?:string;ObjectName?:string}
type Summary={nodes:Node[];references:Reference[]}
type Detail={name:string;type:string;properties:Record<string,unknown>;references:Reference[];internalReferences?:{index:number;name:string;type:string;property:string}[];animation?:{duration?:number;bindings?:{name:string;guid:string;tracks:{trackName:string;trackType:string;property?:string;propertyPath?:string}[]}[];widgetBindings?:{widgetName:string;slotWidgetName:string;bIsRootWidget:boolean;guid?:string}[]};opaqueBytes:number}
type Tree={key:number;label:string;children:Tree[]}
const props=defineProps<{path:string;summary:Summary}>()
const emit=defineEmits<{(e:'navigate-path',path:string):void}>()
const project=useProject(),query=ref(''),group=ref('widgets'),selected=ref<number[]>([]),detail=ref<Detail>(),error=ref(''),busy=ref(false)
let generation=0
const options=computed(()=>[
  {label:tr('ui.bpGroupWidgets'),value:'widgets'},
  {label:tr('ui.bpGroupAnimations'),value:'animations'},
  {label:tr('ui.bpGroupSlots'),value:'slots'},
  {label:tr('ui.bpGroupAll'),value:'all'}
])
const tree=computed(()=>{
 const nodes=props.summary.nodes.filter(n=>group.value==='all'||n.group===group.value)
 const byIndex=new Map(nodes.map(n=>[n.index,n]))
 const map=new Map<number,Tree>(nodes.map(n=>[n.index,{key:n.index,label:`${n.name} · ${n.type}`,children:[]}]))
 if(group.value==='widgets')for(const t of props.summary.nodes.filter(n=>n.type==='WidgetTree')){
   const owner=props.summary.nodes.find(n=>n.index===t.outerIndex)
   map.set(t.index,{key:t.index,label:`${owner?.name||t.name} · ${t.name}`,children:[]})
 }
 const roots:Tree[]=group.value==='widgets'?props.summary.nodes.filter(n=>n.type==='WidgetTree').map(n=>map.get(n.index)!):[]
 for(const n of nodes){const item=map.get(n.index)!;const parent=group.value==='widgets'?n.parentIndex:n.outerIndex
  // Refuse cyclic/malformed relationships rather than recursively rendering forever.
  let cursor=parent,cycle=false;const seen=new Set([n.index]);while(cursor!=null&&map.has(cursor)){if(seen.has(cursor)){cycle=true;break}seen.add(cursor);const p=byIndex.get(cursor);cursor=p?(group.value==='widgets'?p.parentIndex:p.outerIndex):null}
  if(!cycle&&parent!=null&&map.has(parent))map.get(parent)!.children.push(item);else if(group.value==='widgets'&&map.has(n.outerIndex))map.get(n.outerIndex)!.children.push(item);else roots.push(item)
 }
 return roots
})
const active=computed(()=>props.summary.nodes.find(n=>n.index===selected.value[0]))
const resourceQuery=ref(''),resourceType=ref('all'),referenceScope=ref('node'),previewPath=ref('')
const category=(r:Reference)=>r.Class==='Texture2D'?'texture':r.Class?.includes('Material')?'material':r.Class?.includes('Font')?'font':r.Class?.includes('Blueprint')?'blueprint':'other'
const resourceOptions=computed(()=>[
  {label:tr('ui.bpResAll'),value:'all'},
  {label:tr('ui.bpResTexture'),value:'texture'},
  {label:tr('ui.bpResMaterial'),value:'material'},
  {label:tr('ui.bpResFont'),value:'font'},
  {label:tr('ui.bpResBlueprint'),value:'blueprint'},
  {label:tr('ui.bpResOther'),value:'other'}
])
const refs=computed(()=>{
 const source=referenceScope.value==='asset'||!selected.value.length?props.summary.references:detail.value?.references||[]
 const unique=new Map<string,Reference>();for(const r of source){const prior=unique.get(r.ContentFile);if(!prior||prior.Class==='Package')unique.set(r.ContentFile,r)}
 return [...unique.values()].filter(r=>(resourceType.value==='all'||category(r)===resourceType.value)&&`${r.ContentFile} ${r.Class||''}`.toLowerCase().includes(resourceQuery.value.toLowerCase()))
})
const previewUrl=computed(()=>`/api/projects/${project.id}/resource-image?`+new URLSearchParams({path:previewPath.value}))
const rawUrl=computed(()=>`/api/projects/${project.id}/resource-raw?`+new URLSearchParams({path:props.path}))
async function select(keys:number[]){selected.value=keys;detail.value=undefined;error.value='';const id=++generation;if(keys[0]==null)return;busy.value=true
 try{const d=await api<Detail>(`/projects/${project.id}/resource-blueprint-node?`+new URLSearchParams({path:props.path,index:String(keys[0])}));if(id===generation)detail.value=d}catch(e){if(id===generation)error.value=String(e)}finally{if(id===generation)busy.value=false}
}
watch(()=>props.path,()=>{generation++;selected.value=[];detail.value=undefined;busy.value=false;previewPath.value=''})
</script>
<template>
 <div class="blueprint-viewer">
  <aside><n-select v-model:value="group" :options="options"/><n-input v-model:value="query" :placeholder="tr('ui.bpSearchPlaceholder')" clearable/><n-tree block-line virtual-scroll :data="tree" :pattern="query" :selected-keys="selected" :on-update:selected-keys="keys=>select(keys as number[])" style="height:58vh" /></aside>
  <main><p class="note">{{ tr('ui.bpReadonlyNote') }}</p>
   <h3>{{active ? `${active.name} · ${active.type}` : tr('ui.bpSelectHint')}}</h3>
   <n-button v-if="active?.slotIndex!=null" size="small" @click="select([active.slotIndex])">{{ tr('ui.bpViewSlot') }}</n-button>
   <div class="resource-filters">
    <n-select v-model:value="referenceScope" :options="[{label:tr('ui.bpScopeNode'),value:'node'},{label:tr('ui.bpScopeAsset'),value:'asset'}]"/>
    <n-select v-model:value="resourceType" :options="resourceOptions"/>
    <n-input v-model:value="resourceQuery" :placeholder="tr('ui.bpFilterRes')" clearable/>
   </div>
   <div class="references"><div v-for="r in refs" :key="r.ContentFile" class="reference-row"><n-button text type="primary" @click="emit('navigate-path',r.ContentFile)">{{r.Class}} · {{r.ContentFile}}</n-button><n-button v-if="r.Class==='Texture2D'" size="tiny" @click="previewPath=r.ContentFile">{{ tr('ui.bpPreviewTexture') }}</n-button></div><span v-if="!refs.length && !busy" class="note">{{ tr('ui.bpNoRefs') }}</span></div>
   <div v-if="detail?.animation" class="animation-summary">
    <div class="anim-header"><span class="anim-title">{{ tr('ui.bpAnimTitle') }}</span><span v-if="detail.animation.duration!=null" class="anim-duration">{{ tr('ui.bpDuration') }}: {{detail.animation.duration.toFixed(2)}}s</span></div>
    <div v-if="detail.animation.widgetBindings?.length" class="anim-section"><span class="section-label">{{ tr('ui.bpBoundWidgets') }}:</span><div class="widget-tags"><n-button v-for="wb in detail.animation.widgetBindings" :key="wb.widgetName+wb.slotWidgetName" size="tiny" tertiary @click="query=wb.widgetName;group='widgets'">{{wb.widgetName}}{{wb.slotWidgetName&&wb.slotWidgetName!=='None'?` (${wb.slotWidgetName})`:''}}{{wb.bIsRootWidget?' [Root]':''}}</n-button></div></div>
    <div v-if="detail.animation.bindings?.length" class="anim-section"><span class="section-label">{{ tr('ui.bpTracksList') }}:</span><div class="tracks-list"><div v-for="b in detail.animation.bindings" :key="b.guid" class="track-group"><span class="track-binding-name">{{b.name}}:</span><span v-for="t in b.tracks" :key="t.trackName" class="track-pill">{{t.property || t.trackType}}</span><span v-if="!b.tracks.length" class="note">{{ tr('ui.bpNoTracks') }}</span></div></div></div>
   </div>
   <div v-if="detail?.internalReferences?.length" class="internal-links"><span class="note">{{ tr('ui.bpInternalRefs') }}</span><n-button v-for="r in detail.internalReferences" :key="r.index" size="small" @click="group='all';select([r.index])">{{r.property}} → {{r.name}} · {{r.type}}</n-button></div>
   <texture-preview-modal :show="!!previewPath" :title="previewPath" :src="previewUrl" :asset-path="previewPath" @update:show="show=>{if(!show)previewPath=''}" @navigate-path="emit('navigate-path',$event)"/>
   <n-spin v-if="busy"/><p v-if="error" class="error">{{error}}</p>
   <generic-resource-viewer v-if="detail" :key="selected[0]" :data="{Properties:detail.properties,OpaqueBytes:detail.opaqueBytes}" :path="path" :preview-src="''" :preview-error="''" :raw-url="rawUrl" @navigate-path="emit('navigate-path',$event)"/>
  </main>
 </div>
</template>
<style scoped>
.animation-summary{background:rgba(255,255,255,0.03);border:1px solid rgba(255,255,255,0.08);border-radius:6px;padding:12px;margin:12px 0;display:flex;flex-direction:column;gap:8px}
.anim-header{display:flex;justify-content:space-between;align-items:center}
.anim-title{font-weight:600;font-size:13px;color:#2080f0}
.anim-duration{font-size:12px;color:#f0a020;font-family:monospace}
.anim-section{display:flex;flex-direction:column;gap:6px;font-size:12px}
.section-label{color:#888;font-weight:500}
.widget-tags{display:flex;flex-wrap:wrap;gap:6px}
.tracks-list{display:flex;flex-direction:column;gap:4px}
.track-group{display:flex;align-items:center;flex-wrap:wrap;gap:6px;font-size:12px}
.track-binding-name{color:#aaa;font-family:monospace}
.track-pill{background:rgba(32,128,240,0.12);color:#2080f0;padding:1px 6px;border-radius:4px;font-size:11px}
.resource-filters{display:grid;grid-template-columns:180px 140px minmax(160px,1fr);gap:8px;margin-top:12px}.reference-row{display:flex;align-items:center;gap:12px;max-width:100%}.internal-links{display:flex;flex-wrap:wrap;gap:8px;margin:12px 0}.internal-links .note{width:100%}@media(max-width:800px){.resource-filters{grid-template-columns:1fr}}
.blueprint-viewer{display:flex;gap:16px;height:100%;min-height:520px}aside{width:340px;flex-shrink:0;display:flex;flex-direction:column;gap:10px;overflow:auto}main{flex:1;min-width:0;overflow:auto}.note{color:#888;font-size:12px}.references{display:flex;flex-direction:column;align-items:flex-start;max-height:180px;overflow:auto;margin:10px 0}.references :deep(button){white-space:normal;text-align:left;height:auto;margin:3px 0}.error{color:#d03050}@media(max-width:800px){.blueprint-viewer{flex-direction:column}aside{width:100%}}
</style>
