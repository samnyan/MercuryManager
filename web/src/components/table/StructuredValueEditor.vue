<script setup lang="ts">
import { computed } from 'vue'
import { NCheckbox,NInput,NInputNumber,NSelect } from 'naive-ui'
import ArrayEditor from './ArrayEditor.vue'
import type { FieldValue,PropertyShape } from '../../project'
import enumsData from '../../enums.json'
const props=defineProps<{value:FieldValue;shape:PropertyShape;disabled?:boolean}>()
const emit=defineEmits<{'update:value':[value:FieldValue]}>()
const object=computed(()=>props.value!==null&&typeof props.value==='object'&&!Array.isArray(props.value)?props.value as Record<string,FieldValue>:{})
const isNumber=computed(()=>/^(Int|UInt|Float|Double|Byte)/.test(props.shape.type)&&!['Int64PropertyData','UInt64PropertyData'].includes(props.shape.type))
const enumOptions=computed(()=>{const name=String(props.value??'').split('::')[0]??'';const values=(enumsData as Record<string,{values:{full:string;name:string}[]}>)[name]?.values??[];return [...new Set([String(props.value??''),...values.map(v=>v.full)])].filter(Boolean).map(v=>({label:v,value:v}))})
function setField(key:string,value:FieldValue){emit('update:value',{...object.value,[key]:value})}
</script>
<template>
 <array-editor v-if="shape.type==='ArrayPropertyData'" :value="value" :shape="shape" :disabled="disabled" @update:value="emit('update:value',$event)" />
 <div v-else-if="shape.type==='StructPropertyData'" class="struct-editor">
  <label v-for="(child,key) in shape.fields" :key="key" class="struct-field"><span>{{key}}</span><structured-value-editor :value="object[key]??null" :shape="child" :disabled="disabled" @update:value="setField(String(key),$event)" /></label>
 </div>
 <n-checkbox v-else-if="shape.type==='BoolPropertyData'" :checked="value===true" :disabled="disabled" @update:checked="emit('update:value',$event)" />
 <n-input-number v-else-if="isNumber" :value="typeof value==='number'?value:0" :disabled="disabled" :precision="['FloatPropertyData','DoublePropertyData'].includes(shape.type)?undefined:0" :min="shape.type.startsWith('UInt')||shape.type==='BytePropertyData'?0:undefined" :max="shape.type==='BytePropertyData'?255:undefined" @update:value="emit('update:value',$event??0)" />
 <n-select v-else-if="shape.type==='EnumPropertyData'" :value="String(value??'')" :options="enumOptions" filterable tag :disabled="disabled" @update:value="emit('update:value',$event)" />
 <n-input v-else :value="value===null?'':String(value)" :disabled="disabled" @update:value="emit('update:value',$event)" />
</template>
<style scoped>
.struct-editor{display:flex;flex-direction:column;gap:8px;padding:8px;border:1px solid #8883;border-radius:4px}.struct-field{display:flex;flex-direction:column;gap:4px}.struct-field>span{font-size:12px;color:#888}
</style>
