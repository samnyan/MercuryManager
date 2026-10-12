<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NTree, NInput, NSpin, NButton, NSelect } from 'naive-ui'
import GenericResourceViewer from './GenericResourceViewer.vue'
import { api, useProject } from '../../project'
type Node={index:number;name:string;type:string;group:string;outerIndex:number;parentIndex:number|null;slotIndex:number|null}
type Reference={ContentFile:string;Class?:string;ObjectName?:string}
type Summary={nodes:Node[];references:Reference[]}
type Detail={name:string;type:string;properties:Record<string,unknown>;references:Reference[];opaqueBytes:number}
type Tree={key:number;label:string;children:Tree[]}
const props=defineProps<{path:string;summary:Summary}>()
const emit=defineEmits<{(e:'navigate-path',path:string):void}>()
const project=useProject(),query=ref(''),group=ref('widgets'),selected=ref<number[]>([]),detail=ref<Detail>(),error=ref(''),busy=ref(false)
let generation=0
const options=[{label:'Widget Tree',value:'widgets'},{label:'动画',value:'animations'},{label:'布局 Slots',value:'slots'},{label:'全部对象',value:'all'}]
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
const refs=computed(()=>Array.from(new Map((detail.value?.references||props.summary.references).map(r=>[r.ContentFile,r])).values()))
const rawUrl=computed(()=>`/api/projects/${project.id}/resource-raw?`+new URLSearchParams({path:props.path}))
async function select(keys:number[]){selected.value=keys;detail.value=undefined;error.value='';const id=++generation;if(keys[0]==null)return;busy.value=true
 try{const d=await api<Detail>(`/projects/${project.id}/resource-blueprint-node?`+new URLSearchParams({path:props.path,index:String(keys[0])}));if(id===generation)detail.value=d}catch(e){if(id===generation)error.value=String(e)}finally{if(id===generation)busy.value=false}
}
watch(()=>props.path,()=>{generation++;selected.value=[];detail.value=undefined;busy.value=false})
</script>
<template>
 <div class="blueprint-viewer">
  <aside><n-select v-model:value="group" :options="options"/><n-input v-model:value="query" placeholder="搜索组件 / 类型" clearable/><n-tree block-line virtual-scroll :data="tree" :pattern="query" :selected-keys="selected" :on-update:selected-keys="keys=>select(keys as number[])" style="height:58vh" /></aside>
  <main><p class="note">只读结构与默认属性；不执行运行时绑定、材质或动画。</p>
   <h3>{{active ? `${active.name} · ${active.type}` : '选择组件查看属性；下方列出资产依赖'}}</h3>
   <n-button v-if="active?.slotIndex!=null" size="small" @click="select([active.slotIndex])">查看布局 Slot</n-button>
   <div class="references"><n-button v-for="r in refs" :key="r.ContentFile" text type="primary" @click="emit('navigate-path',r.ContentFile)">{{r.Class}} · {{r.ContentFile}}</n-button></div>
   <n-spin v-if="busy"/><p v-if="error" class="error">{{error}}</p>
   <generic-resource-viewer v-if="detail" :key="selected[0]" :data="{Properties:detail.properties,OpaqueBytes:detail.opaqueBytes}" :path="path" :preview-src="''" :preview-error="''" :raw-url="rawUrl" @navigate-path="emit('navigate-path',$event)"/>
  </main>
 </div>
</template>
<style scoped>
.blueprint-viewer{display:flex;gap:16px;height:100%;min-height:520px}aside{width:340px;flex-shrink:0;display:flex;flex-direction:column;gap:10px;overflow:auto}main{flex:1;min-width:0;overflow:auto}.note{color:#888;font-size:12px}.references{display:flex;flex-direction:column;align-items:flex-start;max-height:180px;overflow:auto;margin:10px 0}.references :deep(button){white-space:normal;text-align:left;height:auto;margin:3px 0}.error{color:#d03050}@media(max-width:800px){.blueprint-viewer{flex-direction:column}aside{width:100%}}
</style>
