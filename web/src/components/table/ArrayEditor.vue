<script setup lang="ts">
import { computed } from 'vue'
import { NButton } from 'naive-ui'
import StructuredValueEditor from './StructuredValueEditor.vue'
import type { FieldValue, PropertyShape } from '../../project'
import { tr } from '../../i18n'
const props=defineProps<{value:FieldValue;shape?:PropertyShape|null;itemType?:'string'|'number'|'boolean'|'object';disabled?:boolean}>()
const emit=defineEmits<{'update:value':[value:FieldValue[]]}>()
const items=computed<FieldValue[]>(()=>Array.isArray(props.value)?props.value as FieldValue[]:[])
const element=computed<PropertyShape>(()=>props.shape?.element??{type:props.itemType==='number'?'IntPropertyData':props.itemType==='boolean'?'BoolPropertyData':'StrPropertyData'})
function initial(shape:PropertyShape):FieldValue {
 if(shape.type==='ArrayPropertyData')return []
 if(shape.type==='StructPropertyData')return Object.fromEntries(Object.entries(shape.fields??{}).map(([k,s])=>[k,initial(s)]))
 if(shape.type==='BoolPropertyData')return false
 if(['Int64PropertyData','UInt64PropertyData'].includes(shape.type))return '0'
 if(/^(Int|UInt|Float|Double|Byte)/.test(shape.type))return 0
 return ''
}
function update(index:number,value:FieldValue){emit('update:value',items.value.map((item,i)=>i===index?value:item))}
function add(){emit('update:value',[...items.value,initial(element.value)])}
</script>
<template>
 <div class="array-editor">
  <div class="array-header"><code>{{element.type.replace('PropertyData','')}}[] ({{items.length}})</code></div>
  <div v-if="!items.length" class="array-empty">{{tr('ui.noItemsClickToAdd')}}</div>
  <div v-for="(item,index) in items" :key="index" class="array-row">
   <span class="array-index">#{{index}}</span>
   <structured-value-editor :value="item" :shape="element" :disabled="disabled" @update:value="update(index,$event)" />
   <n-button size="tiny" type="error" quaternary :disabled="disabled" :aria-label="tr('ui.delete')+' '+index" @click="emit('update:value',items.filter((_,i)=>i!==index))">✕</n-button>
  </div>
  <n-button size="small" dashed block :disabled="disabled || (element.type==='StructPropertyData' && !element.fields)" @click="add">{{tr('ui.addItem')}}</n-button>
 </div>
</template>
<style scoped>
.array-editor{width:100%;border:1px solid #8884;border-radius:4px;padding:8px;box-sizing:border-box}.array-header{margin-bottom:8px;font-size:12px;color:#888}.array-row{display:flex;align-items:flex-start;gap:8px;padding:6px 0}.array-row>:nth-child(2){flex:1;min-width:0}.array-index{font-size:12px;color:#888;padding-top:6px}.array-empty{padding:8px;color:#888;font-size:12px}
</style>
