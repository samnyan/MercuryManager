<script setup lang="ts">
import { computed } from 'vue'
import { NInput, NSwitch, NSelect } from 'naive-ui'
import InspectorNumber from './InspectorNumber.vue'
const props = defineProps<{ value: Record<string, any>; baseline: Record<string, any>; path?: string }>()
const enums: Record<string, string[]> = {
  Visibility: ['Visible','Collapsed','Hidden','HitTestInvisible','SelfHitTestInvisible'],
  DrawAs: ['NoDrawType','Image','Box','Border'],
  Tiling: ['NoTile','Horizontal','Vertical','Both'],
  Mirroring: ['NoMirror','Horizontal','Vertical','Both'],
  HorizontalAlignment: ['HAlign_Fill','HAlign_Left','HAlign_Center','HAlign_Right'],
  VerticalAlignment: ['VAlign_Fill','VAlign_Top','VAlign_Center','VAlign_Bottom']
}
const entries = computed(() => Object.entries(props.value).filter(([key,value]) =>
  value != null && !['ResourceObject','ObjectName','ObjectPath','PackageIndex','Visibility','RenderOpacity'].includes(key)))
function options(key: string, current: string) {
  const prefix = current.includes('::') ? current.slice(0,current.lastIndexOf('::')+2) : ''
  return enums[key].map(v => ({label:v,value:prefix+v}))
}
function setNumber(key:string, value:number|null) {
  if (value != null && Number.isFinite(value)) props.value[key] = value
}
</script>
<template>
  <div class="property-fields" :class="{'vector-fields': entries.length === 2 && entries.every(([k,v]) => ['X','Y'].includes(k) && typeof v === 'number')}">
    <template v-for="[key, item] in entries" :key="key">
      <fieldset v-if="typeof item === 'object' && !Array.isArray(item)">
        <legend>{{ key }}</legend>
        <UMGPropertyFields :value="item" :baseline="baseline[key] ?? {}" :path="`${path || ''}.${key}`" />
      </fieldset>
      <InspectorNumber v-else-if="typeof item === 'number'" :label="key" :value="item"
        :step="['R','G','B','A','X','Y'].includes(key) ? 0.01 : 0.1"
        :dirty="item !== baseline[key]" @update:value="setNumber(key,$event)"
        @reset="value[key]=baseline[key]" />
      <label v-else-if="typeof item === 'boolean'" class="property-row">
        <span>{{ key }}</span><n-switch :value="item" size="small" @update:value="value[key]=$event" />
      </label>
      <label v-else-if="typeof item === 'string' && enums[key]" class="property-row">
        <span>{{ key }}</span><n-select :value="item" :options="options(key,item)" size="tiny" @update:value="value[key]=$event" />
      </label>
      <label v-else-if="typeof item === 'string' && ['Text','TypefaceFontName'].includes(key)" class="property-row">
        <span>{{ key }}</span><n-input :value="item" size="tiny" @update:value="value[key]=$event" />
      </label>
      <div v-else class="property-row readonly"><span>{{ key }}</span><code>{{ typeof item === 'object' ? JSON.stringify(item) : item }}</code></div>
    </template>
  </div>
</template>
<style scoped>
.property-fields { display:flex;flex-direction:column;gap:6px;min-width:0 }
fieldset { min-width:0;margin:0;padding:8px;border:1px solid rgba(128,128,128,.18);border-radius:5px;background:rgba(128,128,128,.07) }
legend { color:inherit;opacity:.8;font-size:11px;font-weight:600;padding:0 4px }
.vector-fields { display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:8px }
.vector-fields :deep(.number input) { width:100%;flex:1 }
.vector-fields :deep(.number .drag) { flex:0 0 auto;min-width:12px }
.property-row { display:grid;grid-template-columns:minmax(70px,1fr) minmax(90px,1.3fr);align-items:center;gap:6px;font-size:11px }
.property-row > span { overflow-wrap:anywhere }
.readonly code { overflow-wrap:anywhere;color:#999 }
</style>
