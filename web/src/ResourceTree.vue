<script setup lang="ts">
import {ref,watch} from 'vue'
import {NTree,useMessage,type TreeOption} from 'naive-ui'
import {useRouter} from 'vue-router'
import {api,useProject} from './project'
const project=useProject(),router=useRouter(),message=useMessage(),nodes=ref<TreeOption[]>([])
async function children(directory:string){const rows=await api<{path:string;name:string;directory:boolean}[]>(`/projects/${project.id}/resources?`+new URLSearchParams({directory}));return rows.map(r=>({key:r.path,label:r.name,isLeaf:!r.directory}))}
async function load(node:TreeOption){node.children=await children(String(node.key))}
watch(()=>[project.id,project.contentRoot,project.revision],async()=>{nodes.value=[];if(project.id&&project.contentRoot)try{nodes.value=await children('')}catch(e){message.error(String(e))}},{immediate:true})
function select(keys:(string|number)[],options:(TreeOption|null)[]){if(keys[0]&&options[0]?.isLeaf)router.push('/resource?path='+encodeURIComponent(String(keys[0])))}
</script>
<template><n-tree :data="nodes" :on-load="load" block-line @update:selected-keys="select"/></template>
