<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import {
  NCard,
  NTag,
  NSpace,
  NButton,
  NIcon,
  NInputNumber,
  NColorPicker,
  useMessage
} from 'naive-ui'
import { Save, RotateCcw, ExternalLink, Image as ImageIcon, Layers } from '@lucide/vue'
import { api, useProject } from '../../project'
import { tr } from '../../i18n'
import MaterialShaderCanvas from './MaterialShaderCanvas.vue'

export interface MaterialInstanceData {
  Name: string
  Parent?: string
  ParentPackage?: string
  Scalars?: Record<string, number>
  Textures?: Record<string, string>
  ScalarOverrides?: Array<{ ParameterName: string; Value: number }>
  VectorOverrides?: Array<{ ParameterName: string; R: number; G: number; B: number; A: number }>
  TextureOverrides?: Array<{ ParameterName: string; ContentFile: string }>
  BasePropertyOverrides?: Record<string, any>
  ParentMaterialInfo?: {
    Name: string
    MaterialDomain?: string
    BlendMode?: string
    ShadingModel?: string
    TwoSided?: boolean
    ScalarParameters?: Array<{ ParameterName: string; DefaultValue: number; SliderMin?: number; SliderMax?: number }>
    VectorParameters?: Array<{ ParameterName: string; R: number; G: number; B: number; A: number }>
    TextureNodes?: Array<{ ParameterName?: string; TextureName?: string; ContentFile?: string }>
    FunctionCalls?: Array<{ FunctionName?: string; ContentFile?: string }>
  }
}

const props = defineProps<{
  info: MaterialInstanceData
  path: string
}>()

const emit = defineEmits<{
  navigatePath: [targetPath: string]
  previewTexture: [path: string, label: string]
}>()

const project = useProject()
const message = useMessage()

const saving = ref(false)

// 可编辑标量参数字典: { [paramName]: number }
const editScalars = ref<Record<string, number>>({})
const origScalars = ref<Record<string, number>>({})

// 可编辑矢量参数字典: { [paramName]: { r: number, g: number, b: number, a: number, hex: string } }
interface ColorParam {
  r: number
  g: number
  b: number
  a: number
  hex: string
}
const editVectors = ref<Record<string, ColorParam>>({})
const origVectors = ref<Record<string, ColorParam>>({})

function toHex(r: number, g: number, b: number): string {
  const cr = Math.round(Math.min(1, Math.max(0, r)) * 255).toString(16).padStart(2, '0')
  const cg = Math.round(Math.min(1, Math.max(0, g)) * 255).toString(16).padStart(2, '0')
  const cb = Math.round(Math.min(1, Math.max(0, b)) * 255).toString(16).padStart(2, '0')
  return `#${cr}${cg}${cb}`.toUpperCase()
}

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const clean = hex.replace('#', '')
  if (clean.length === 6) {
    const r = parseInt(clean.substring(0, 2), 16) / 255
    const g = parseInt(clean.substring(2, 4), 16) / 255
    const b = parseInt(clean.substring(4, 6), 16) / 255
    return { r, g, b }
  }
  return { r: 1, g: 1, b: 1 }
}

function initEditState() {
  const sMap: Record<string, number> = {}
  if (props.info.ScalarOverrides) {
    for (const item of props.info.ScalarOverrides) {
      sMap[item.ParameterName] = item.Value
    }
  } else if (props.info.Scalars) {
    Object.assign(sMap, props.info.Scalars)
  }
  editScalars.value = { ...sMap }
  origScalars.value = { ...sMap }

  const vMap: Record<string, ColorParam> = {}
  if (props.info.VectorOverrides) {
    for (const item of props.info.VectorOverrides) {
      vMap[item.ParameterName] = {
        r: item.R,
        g: item.G,
        b: item.B,
        a: item.A,
        hex: toHex(item.R, item.G, item.B)
      }
    }
  }
  editVectors.value = JSON.parse(JSON.stringify(vMap))
  origVectors.value = JSON.parse(JSON.stringify(vMap))
}

watch(() => props.info, initEditState, { immediate: true })

const isDirty = computed(() => {
  for (const k of Object.keys(editScalars.value)) {
    if (editScalars.value[k] !== origScalars.value[k]) return true
  }
  for (const k of Object.keys(editVectors.value)) {
    const cur = editVectors.value[k]
    const orig = origVectors.value[k]
    if (!orig) return true
    if (cur.r !== orig.r || cur.g !== orig.g || cur.b !== orig.b || cur.a !== orig.a) return true
  }
  return false
})

function onColorPickerChange(paramName: string, hex: string) {
  const rgb = hexToRgb(hex)
  const current = editVectors.value[paramName]
  if (current) {
    current.hex = hex
    current.r = rgb.r
    current.g = rgb.g
    current.b = rgb.b
  }
}

function resetChanges() {
  initEditState()
}

async function saveChanges() {
  if (!project.id || !props.path) return
  saving.value = true
  try {
    const reqVectors: Record<string, { r: number; g: number; b: number; a: number }> = {}
    for (const [k, v] of Object.entries(editVectors.value)) {
      reqVectors[k] = { r: v.r, g: v.g, b: v.b, a: v.a }
    }
    await api(
      `/projects/${project.id}/resource-material-params?` +
        new URLSearchParams({ path: props.path }),
      'POST',
      {
        scalars: editScalars.value,
        vectors: reqVectors
      }
    )
    origScalars.value = { ...editScalars.value }
    origVectors.value = JSON.parse(JSON.stringify(editVectors.value))
    project.revision++
    message.success(tr('ui.matParamsSaved'))
  } catch (e: any) {
    message.error(e?.message || String(e))
  } finally {
    saving.value = false
  }
}

// 汇总展示父级和当前实例的所有标量参数
const allScalarParams = computed(() => {
  const list: Array<{
    name: string
    isOverridden: boolean
    value: number
    defaultValue?: number
    min?: number
    max?: number
  }> = []

  const seen = new Set<string>()

  // 1. 已覆写的项
  for (const [name, val] of Object.entries(editScalars.value)) {
    seen.add(name)
    const pInfo = props.info.ParentMaterialInfo?.ScalarParameters?.find(p => p.ParameterName === name)
    list.push({
      name,
      isOverridden: true,
      value: val,
      defaultValue: pInfo?.DefaultValue,
      min: pInfo?.SliderMin,
      max: pInfo?.SliderMax
    })
  }

  // 2. 来自 Parent 未被覆写的项
  if (props.info.ParentMaterialInfo?.ScalarParameters) {
    for (const p of props.info.ParentMaterialInfo.ScalarParameters) {
      if (!seen.has(p.ParameterName)) {
        list.push({
          name: p.ParameterName,
          isOverridden: false,
          value: p.DefaultValue,
          defaultValue: p.DefaultValue,
          min: p.SliderMin,
          max: p.SliderMax
        })
      }
    }
  }

  return list
})

// 汇总展示父级和当前实例的所有矢量参数
const allVectorParams = computed(() => {
  const list: Array<{
    name: string
    isOverridden: boolean
    r: number
    g: number
    b: number
    a: number
    hex: string
    defaultHex?: string
  }> = []

  const seen = new Set<string>()

  // 1. 已覆写
  for (const [name, val] of Object.entries(editVectors.value)) {
    seen.add(name)
    const pInfo = props.info.ParentMaterialInfo?.VectorParameters?.find(p => p.ParameterName === name)
    list.push({
      name,
      isOverridden: true,
      r: val.r,
      g: val.g,
      b: val.b,
      a: val.a,
      hex: val.hex,
      defaultHex: pInfo ? toHex(pInfo.R, pInfo.G, pInfo.B) : undefined
    })
  }

  // 2. 未覆写
  if (props.info.ParentMaterialInfo?.VectorParameters) {
    for (const p of props.info.ParentMaterialInfo.VectorParameters) {
      if (!seen.has(p.ParameterName)) {
        list.push({
          name: p.ParameterName,
          isOverridden: false,
          r: p.R,
          g: p.G,
          b: p.B,
          a: p.A,
          hex: toHex(p.R, p.G, p.B),
          defaultHex: toHex(p.R, p.G, p.B)
        })
      }
    }
  }

  return list
})

// 贴图参数清单
const textureParams = computed(() => {
  const map: Record<string, { isOverridden: boolean; path: string }> = {}
  if (props.info.Textures) {
    for (const [k, v] of Object.entries(props.info.Textures)) {
      map[k] = { isOverridden: true, path: v }
    }
  }
  if (props.info.ParentMaterialInfo?.TextureNodes) {
    for (const t of props.info.ParentMaterialInfo.TextureNodes) {
      if (t.ParameterName && !map[t.ParameterName] && t.ContentFile) {
        map[t.ParameterName] = { isOverridden: false, path: t.ContentFile }
      }
    }
  }
  return Object.entries(map).map(([name, item]) => ({
    name,
    isOverridden: item.isOverridden,
    path: item.path
  }))
})

// 聚合给 Shader 预览器的有效参数
const simScalars = computed(() => {
  const res: Record<string, number> = {}
  for (const p of allScalarParams.value) {
    res[p.name] = p.isOverridden ? (editScalars.value[p.name] ?? p.value) : p.value
  }
  return res
})

const simVectors = computed(() => {
  const res: Record<string, { r: number; g: number; b: number; a: number }> = {}
  for (const p of allVectorParams.value) {
    if (p.isOverridden && editVectors.value[p.name]) {
      const v = editVectors.value[p.name]
      res[p.name] = { r: v.r, g: v.g, b: v.b, a: v.a }
    } else {
      res[p.name] = { r: p.r, g: p.g, b: p.b, a: p.a }
    }
  }
  return res
})

const simTextures = computed(() => {
  return textureParams.value.map(t => ({ name: t.name, path: t.path }))
})

const simFunctionNames = computed(() => {
  return (props.info.ParentMaterialInfo?.FunctionCalls || [])
    .map(f => f.FunctionName || '')
    .filter(Boolean)
})

function getTextureImageUrl(targetPath: string): string {
  return `/api/projects/${project.id}/resource-image?path=${encodeURIComponent(targetPath)}&mip=0`
}
</script>

<template>
  <div class="mi-editor">
    <!-- 顶部操作条与继承卡片 (General) -->
    <n-card size="small" class="mi-section-card">
      <div class="mi-toolbar">
        <div class="mi-title-left">
          <n-icon :component="Layers" size="18" />
          <span class="mi-title">{{ tr('ui.matInstanceEditor') }}</span>
          <n-tag type="info" size="small">{{ info.Name }}</n-tag>
          <n-tag v-if="isDirty" type="warning" size="small">{{ tr('ui.draftModified') }}</n-tag>
        </div>

        <n-space size="small">
          <n-button size="small" secondary :disabled="!isDirty" @click="resetChanges">
            <template #icon><n-icon :component="RotateCcw" /></template>
            {{ tr('ui.resetChanges') }}
          </n-button>
          <n-button
            size="small"
            type="primary"
            :loading="saving"
            :disabled="!isDirty"
            @click="saveChanges"
          >
            <template #icon><n-icon :component="Save" /></template>
            {{ tr('ui.saveToWorkspace') }}
          </n-button>
        </n-space>
      </div>

      <div class="mi-general-grid">
        <div class="mi-general-item">
          <span class="label">{{ tr('ui.parentMaterial') }}:</span>
          <a
            v-if="info.ParentPackage"
            class="ref-link"
            @click.prevent="emit('navigatePath', info.ParentPackage)"
          >
            🔗 {{ info.Parent || info.ParentPackage }}
          </a>
          <span v-else class="value">{{ info.Parent || '—' }}</span>
        </div>

        <div v-if="info.BasePropertyOverrides?.BlendMode" class="mi-general-item">
          <span class="label">{{ tr('ui.blendMode') }}:</span>
          <n-tag size="tiny" type="info">{{ info.BasePropertyOverrides.BlendMode }}</n-tag>
        </div>

        <div v-if="info.BasePropertyOverrides?.ShadingModel" class="mi-general-item">
          <span class="label">{{ tr('ui.shadingModel') }}:</span>
          <n-tag size="tiny">{{ info.BasePropertyOverrides.ShadingModel }}</n-tag>
        </div>

        <div v-if="info.BasePropertyOverrides?.TwoSided !== undefined" class="mi-general-item">
          <span class="label">{{ tr('ui.twoSided') }}:</span>
          <span class="value">{{ info.BasePropertyOverrides.TwoSided ? tr('ui.yes') : tr('ui.no') }}</span>
        </div>
      </div>
    </n-card>

    <!-- 贴图参数组 (上方直接显示缩略图，点击弹窗放大) -->
    <n-card v-if="textureParams.length" size="small" class="mi-section-card">
      <template #header>
        <div class="group-header">
          <span>{{ tr('ui.texParamsGroup') }}</span>
          <n-tag size="small">{{ textureParams.length }}</n-tag>
        </div>
      </template>

      <div class="tex-param-grid">
        <div v-for="t in textureParams" :key="t.name" class="tex-param-card">
          <div
            class="tex-thumb-stage"
            @click="emit('previewTexture', t.path, t.name)"
          >
            <img
              :src="getTextureImageUrl(t.path)"
              class="tex-thumb-img"
              :alt="t.name"
            />
            <div class="thumb-zoom-overlay">
              <n-icon :component="ImageIcon" />
            </div>
          </div>

          <div class="tex-param-header">
            <span class="p-name">{{ t.name }}</span>
            <n-tag size="tiny" :type="t.isOverridden ? 'warning' : 'default'">
              {{ t.isOverridden ? tr('ui.overridden') : tr('ui.inherited') }}
            </n-tag>
          </div>
          <div class="tex-param-path">
            <a class="ref-link" @click.prevent="emit('navigatePath', t.path)">
              🔗 {{ t.path }}
            </a>
          </div>
        </div>
      </div>
    </n-card>

    <div class="two-col-layout">
      <div class="col-left-params">
        <!-- 矢量与颜色参数组 (Vector Parameter Values) -->
        <n-card size="small" class="mi-section-card">
          <template #header>
            <div class="group-header">
              <span>{{ tr('ui.vectorParamsGroup') }}</span>
              <n-tag size="small">{{ allVectorParams.length }}</n-tag>
            </div>
          </template>

          <div v-if="!allVectorParams.length" class="empty-hint">{{ tr('ui.noVectorParams') }}</div>
          <div v-else class="param-rows-container">
            <div v-for="p in allVectorParams" :key="p.name" class="vector-param-row">
              <div class="param-info-header">
                <span class="p-name">{{ p.name }}</span>
                <n-tag size="tiny" :type="p.isOverridden ? 'warning' : 'default'">
                  {{ p.isOverridden ? tr('ui.overridden') : tr('ui.inherited') }}
                </n-tag>
              </div>

              <div class="color-control-box">
                <n-color-picker
                  v-if="p.isOverridden && editVectors[p.name]"
                  v-model:value="editVectors[p.name].hex"
                  :show-alpha="false"
                  size="small"
                  style="width: 130px"
                  @update:value="hex => onColorPickerChange(p.name, hex)"
                />
                <div v-else class="static-color-box" :style="{ backgroundColor: p.hex }" />

                <div class="color-values">
                  <span class="hex-text">{{ p.isOverridden && editVectors[p.name] ? editVectors[p.name].hex : p.hex }}</span>
                  <span class="rgba-detail">
                    RGBA({{ (p.isOverridden && editVectors[p.name] ? editVectors[p.name].r : p.r).toFixed(2) }},
                    {{ (p.isOverridden && editVectors[p.name] ? editVectors[p.name].g : p.g).toFixed(2) }},
                    {{ (p.isOverridden && editVectors[p.name] ? editVectors[p.name].b : p.b).toFixed(2) }},
                    {{ p.a.toFixed(1) }})
                  </span>
                </div>
              </div>
            </div>
          </div>
        </n-card>

        <!-- 标量参数组 (Scalar Parameter Values) -->
        <n-card size="small" class="mi-section-card">
          <template #header>
            <div class="group-header">
              <span>{{ tr('ui.scalarParamsGroup') }}</span>
              <n-tag size="small">{{ allScalarParams.length }}</n-tag>
            </div>
          </template>

          <div v-if="!allScalarParams.length" class="empty-hint">{{ tr('ui.noScalarParams') }}</div>
          <div v-else class="param-rows-container">
            <div v-for="p in allScalarParams" :key="p.name" class="scalar-param-row">
              <div class="param-info-header">
                <span class="p-name">{{ p.name }}</span>
                <n-tag size="tiny" :type="p.isOverridden ? 'warning' : 'default'">
                  {{ p.isOverridden ? tr('ui.overridden') : tr('ui.inherited') }}
                </n-tag>
              </div>

              <div class="scalar-control-box">
                <n-input-number
                  v-if="p.isOverridden"
                  v-model:value="editScalars[p.name]"
                  size="small"
                  :step="0.01"
                  style="width: 140px"
                />
                <code v-else class="scalar-static-val">{{ p.value }}</code>
                <span v-if="p.min !== undefined || p.max !== undefined" class="range-hint">
                  [{{ p.min ?? 0 }} ~ {{ p.max ?? 1 }}]
                </span>
              </div>
            </div>
          </div>
        </n-card>
      </div>

      <!-- 右侧：UMG 实时材质动画模拟预览器 -->
      <div class="col-right-sim">
        <material-shader-canvas
          :material-path="props.path"
          :blend-mode="info.BasePropertyOverrides?.BlendMode || info.ParentMaterialInfo?.BlendMode"
          :scalars="simScalars"
          :vectors="simVectors"
          :textures="simTextures"
          :function-names="simFunctionNames"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.mi-editor {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  overflow-y: auto;
  padding-right: 4px;
}
.mi-section-card {
  border-radius: 6px;
}
.mi-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 12px;
  border-bottom: 1px solid #f0f0f4;
  padding-bottom: 10px;
}
.mi-title-left {
  display: flex;
  align-items: center;
  gap: 8px;
}
.mi-title {
  font-weight: 600;
  font-size: 14px;
}
.mi-general-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 10px 20px;
  font-size: 13px;
}
.mi-general-item {
  display: flex;
  align-items: center;
  gap: 8px;
}
.mi-general-item .label {
  color: #666;
}
.mi-general-item .value {
  color: #222;
  font-weight: 500;
}
.ref-link {
  color: #18a058;
  text-decoration: none;
  border-bottom: 1px dashed #18a058;
  cursor: pointer;
  font-weight: 500;
  font-size: 12px;
  word-break: break-all;
}
.ref-link:hover {
  color: #36ad6a;
  border-bottom-style: solid;
}
.group-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-weight: 600;
  font-size: 13px;
}
.tex-param-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 10px;
}
.tex-param-card {
  border: 1px solid #e5e5ec;
  border-radius: 6px;
  padding: 10px 12px;
  background: #fafafc;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.tex-thumb-stage {
  position: relative;
  width: 100%;
  height: 128px;
  border-radius: 4px;
  background-color: #ffffff;
  background-image:
    linear-gradient(45deg, #e0e0e0 25%, transparent 25%),
    linear-gradient(-45deg, #e0e0e0 25%, transparent 25%),
    linear-gradient(45deg, transparent 75%, #e0e0e0 75%),
    linear-gradient(-45deg, transparent 75%, #e0e0e0 75%);
  background-size: 12px 12px;
  border: 1px solid #e5e5eb;
  overflow: hidden;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
}
.tex-thumb-img {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
  image-rendering: pixelated;
  transition: transform 0.2s;
}
.thumb-zoom-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-size: 20px;
  opacity: 0;
  transition: opacity 0.2s;
}
.tex-thumb-stage:hover .thumb-zoom-overlay {
  opacity: 1;
}
.tex-thumb-stage:hover .tex-thumb-img {
  transform: scale(1.05);
}
.tex-param-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.two-col-layout {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  align-items: flex-start;
}
.col-left-params {
  flex: 1.4;
  min-width: 340px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.col-right-sim {
  flex: 1;
  min-width: 300px;
}
.empty-hint {
  color: #999;
  font-size: 13px;
  padding: 12px 0;
  text-align: center;
}
.param-rows-container {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.vector-param-row,
.scalar-param-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  background: #f8f9fa;
  border-radius: 6px;
  gap: 12px;
}
.param-info-header {
  display: flex;
  align-items: center;
  gap: 8px;
}
.p-name {
  font-weight: 600;
  font-size: 13px;
  color: #222;
}
.color-control-box {
  display: flex;
  align-items: center;
  gap: 10px;
}
.static-color-box {
  width: 28px;
  height: 28px;
  border-radius: 4px;
  border: 1px solid rgba(0, 0, 0, 0.2);
}
.color-values {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}
.hex-text {
  font-family: monospace;
  font-weight: 600;
  font-size: 12px;
}
.rgba-detail {
  font-size: 10px;
  color: #888;
}
.scalar-control-box {
  display: flex;
  align-items: center;
  gap: 8px;
}
.scalar-static-val {
  font-family: monospace;
  font-weight: 600;
  background: #eef2f6;
  padding: 3px 8px;
  border-radius: 4px;
}
.range-hint {
  font-size: 11px;
  color: #888;
}
</style>
