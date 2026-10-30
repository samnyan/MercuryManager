<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NButton, NInput, NPopconfirm, NSpin, NTag, useMessage } from 'naive-ui'
import { api, useProject } from '../../project'
import { auditTabs, auditPageSize, useAssetRegistryAudit } from './useAssetRegistryAudit'

interface Tag { name: string; value: string }
interface Entry { index: number; objectPath: string; packagePath: string; assetClass: string; packageName: string; assetName: string; tags: Tag[]; chunkIds: number[]; packageFlags: number }
interface Document { revision: string; entries: Entry[] }
const project=useProject(), message=useMessage()
const { audit, auditing, auditKind, auditQuery, auditPage, auditRows, auditPageCount, visibleAuditRows, resetAudit, checkResources } =
 useAssetRegistryAudit(() => project.id, error => message.error(error))
const doc=ref<Document>(), selected=ref<number>(), query=ref(''), busy=ref(false), dirty=ref(false), page=ref(0), chunks=ref('')
const filtered=computed(()=>(doc.value?.entries??[]).filter(e=>`${e.objectPath} ${e.assetClass} ${e.tags.map(t=>t.name+' '+t.value).join(' ')}`.toLowerCase().includes(query.value.toLowerCase())))
const visible=computed(()=>filtered.value.slice(page.value*200,(page.value+1)*200))
const current=computed(()=>doc.value?.entries.find(e=>e.index===selected.value))
watch(query,()=>{page.value=0})
watch(current,e=>{chunks.value=e?.chunkIds.join(', ')??''})
function mark(){
 if(doc.value)for(const entry of doc.value.entries)entry.objectPath=`${entry.packageName}.${entry.assetName}`
 dirty.value=true; resetAudit()
}
async function load(){
 if(!project.id)return
 busy.value=true; resetAudit()
 try {doc.value=await api<Document>(`/projects/${project.id}/asset-registry`);selected.value=doc.value.entries[0]?.index;dirty.value=false;page.value=0}
 catch(e){message.error(String(e))}finally{busy.value=false}
}
watch(()=>project.id,load,{immediate:true})
function add(){
 if(!doc.value)return
 const index=Math.min(-1,...doc.value.entries.map(e=>e.index-1))
 doc.value.entries.push({index,objectPath:'/Game/NewAsset.NewAsset',packagePath:'/Game',assetClass:'Object',packageName:'/Game/NewAsset',assetName:'NewAsset',tags:[],chunkIds:[],packageFlags:0})
 selected.value=index;query.value='';page.value=Math.floor((doc.value.entries.length-1)/200);mark()
}
function remove(){
 if(!current.value||!doc.value)return
 const target=current.value
 doc.value.entries=doc.value.entries.filter(e=>e.index!==target.index);selected.value=doc.value.entries[0]?.index;page.value=Math.min(page.value,Math.max(0,Math.ceil(filtered.value.length/200)-1));mark()
}
function updateField(field:'objectPath'|'packagePath'|'assetClass'|'packageName'|'assetName', value:string){
 if(!current.value)return
 if(field==='objectPath')return
 current.value[field]=value
 if(field==='assetName'||field==='packageName')current.value.objectPath=`${current.value.packageName}.${current.value.assetName}`
 mark()
}
function changeChunks(v:string){
 chunks.value=v
 if(!current.value)return
 if(!v.trim()){current.value.chunkIds=[];mark();return}
 const parts=v.split(',').map(x=>x.trim())
 if(parts.some(x=>!/^[-]?\d+$/.test(x)||Number(x)<-2147483648||Number(x)>2147483647)){message.warning('Chunk IDs: comma-separated int32');return}
 current.value.chunkIds=parts.map(Number);mark()
}
async function save(){
 if(!doc.value)return
 if(current.value && chunks.value!==current.value.chunkIds.join(', ') && chunks.value.replace(/\s/g,'')!==current.value.chunkIds.join(',')){message.error('Invalid Chunk IDs');return}
 busy.value=true
 try{await api(`/projects/${project.id}/asset-registry`,'POST',{revision:doc.value.revision,entries:doc.value.entries});await project.status();await load();message.success('AssetRegistry.bin saved to project draft')}
 catch(e){message.error(String(e))}finally{busy.value=false}
}
</script>
<template>
 <div class="registry">
  <div class="toolbar">
   <strong>AssetRegistry.bin</strong><n-tag size="small">v5</n-tag><span>{{ filtered.length }} / {{ doc?.entries.length ?? 0 }}</span>
   <n-button size="tiny" :disabled="page===0" @click="page--">‹</n-button><span>{{ page+1 }} / {{ Math.max(1,Math.ceil(filtered.length/200)) }}</span><n-button size="tiny" :disabled="(page+1)*200>=filtered.length" @click="page++">›</n-button>
   <n-input v-model:value="query" size="small" placeholder="搜索路径、类型或标签 / Search" clearable />
   <n-button size="small" :disabled="!doc" @click="add">新增 / Add</n-button>
   <n-button size="small" :disabled="!dirty" :loading="busy" @click="save">保存草稿 / Save</n-button>
   <n-button size="small" :disabled="!dirty" @click="load">重置 / Reset</n-button>
   <n-button size="small" :disabled="!doc || dirty || busy" :loading="auditing" @click="checkResources">检查资源 / Check resources</n-button>
   <n-button size="small" tag="a" :href="`/api/projects/${project.id}/asset-registry-download`" target="_blank">下载 / Download</n-button>
  </div>
  <section v-if="audit" class="audit">
   <div class="audit-toolbar"><strong>资源检查</strong><span>登记 {{ audit.registeredPackages }} 包 / 可用 {{ audit.availablePackages }} 包 / 匹配 {{ audit.matchedPackages }} 包</span><n-button size="tiny" @click="resetAudit">关闭</n-button></div>
   <small>原游戏 + 项目 Imported/Resources 草稿；按 .uasset/.umap 包检查，不检查包内对象与 .uexp/.ubulk。未登记不一定是错误，不会自动增删记录。</small>
   <div class="audit-toolbar">
    <n-button v-for="tab in auditTabs" :key="tab[0]" size="tiny" :type="auditKind===tab[0]?'primary':'default'" @click="auditKind=tab[0]">{{ tab[1] }} ({{ audit[tab[0]].length }})</n-button>
    <n-input v-model:value="auditQuery" size="tiny" placeholder="筛选包路径" clearable />
    <n-button size="tiny" :disabled="auditPage===0" @click="auditPage--">‹</n-button><span>{{ auditPage+1 }} / {{ auditPageCount }}</span><n-button size="tiny" :disabled="(auditPage+1)*auditPageSize>=auditRows.length" @click="auditPage++">›</n-button>
   </div>
   <div class="audit-results"><div v-for="item in visibleAuditRows" :key="item.packageName"><code>{{ item.packageName }}</code><small>{{ item.file ?? item.objectPaths.join(', ') }}</small></div><span v-if="!auditRows.length">没有匹配的问题。</span></div>
  </section>
  <n-spin v-if="busy && !doc" />
  <div v-else class="body">
   <div class="list"><button v-for="e in visible" :key="e.index" :class="{active:current?.index===e.index}" @click="selected=e.index"><span>{{ e.assetName }}</span><small>{{ e.assetClass }} · {{ e.packagePath }}</small></button></div>
   <div v-if="current" class="details">
    <div class="detail-header"><strong>资产记录 / Asset entry</strong><n-popconfirm @positive-click="remove"><template #trigger><n-button size="tiny" type="error">删除 / Delete</n-button></template>仅删除 Registry 记录，不会删除资产文件或更新其他引用。</n-popconfirm></div>
    <div v-for="field in (['packagePath','assetClass','packageName','assetName'] as const)" :key="field" class="field"><label>{{ field }}</label><n-input v-model:value="current[field]" size="small" @update:value="mark" /></div>
    <div class="field"><label>Chunk IDs</label><n-input :value="chunks" size="small" @update:value="changeChunks" /></div>
    <div class="field"><label>Package flags (uint32)</label><input type="number" min="0" max="4294967295" v-model.number="current.packageFlags" @input="mark" /></div>
    <div class="detail-header"><strong>Tags</strong><n-button size="tiny" @click="current.tags.push({name:'NewTag',value:''});mark()">新增 / Add</n-button></div>
    <div v-for="(tag,i) in current.tags" :key="i" class="tag-row"><n-input v-model:value="tag.name" size="small" @update:value="mark" /><n-input v-model:value="tag.value" type="textarea" :autosize="{minRows:1,maxRows:6}" size="small" @update:value="mark" /><n-button size="tiny" @click="current.tags.splice(i,1);mark()">×</n-button></div>
   </div>
  </div>
 </div>
</template>
<style scoped>
.registry{height:100%;min-height:0;display:flex;flex-direction:column;gap:8px}.toolbar{display:flex;align-items:center;gap:8px}.toolbar .n-input{max-width:360px}.body{display:flex;flex:1;min-height:0;gap:12px}.list{width:35%;min-width:220px;overflow:auto;border:1px solid var(--n-border-color)}.list button{display:flex;flex-direction:column;width:100%;text-align:left;padding:6px 8px;border:0;background:transparent;color:inherit;cursor:pointer}.list button:hover,.list button.active{background:var(--n-color-hover,rgba(128,128,128,.16))}.list small{opacity:.65;overflow-wrap:anywhere}.details{flex:1;overflow:auto;min-width:0}.detail-header{display:flex;align-items:center;justify-content:space-between;margin:8px 0}.field{display:grid;grid-template-columns:minmax(140px,25%) 1fr;gap:8px;margin:8px 0;align-items:start}.field label{overflow-wrap:anywhere}.tag-row{display:grid;grid-template-columns:minmax(140px,25%) 1fr auto;gap:8px;margin:8px 0}
.audit{border:1px solid #5555;padding:8px;max-height:40%;overflow:auto}.audit-toolbar{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:6px}.audit-toolbar .n-input{max-width:260px}.audit-results{max-height:180px;overflow:auto}.audit-results>div{padding:4px 0;display:flex;flex-direction:column;overflow-wrap:anywhere}.audit small{opacity:.7}.toolbar{flex-wrap:wrap}
</style>