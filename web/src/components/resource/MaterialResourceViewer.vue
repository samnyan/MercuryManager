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
import { ExternalLink, Image as ImageIcon, Save, RotateCcw } from '@lucide/vue'
import { api, useProject } from '../../project'
import { tr } from '../../i18n'

export interface MaterialInfo {
  Name: string
  MaterialDomain: string
  BlendMode: string
  ShadingModel: string
  TwoSided: boolean
  CanMaskedBeAssumedOpaque: boolean
  StateId: string
  ScalarParameters: Array<{
    ExportIndex: number
    NodeName: string
    ParameterName: string
    DefaultValue: number
    SliderMin?: number
    SliderMax?: number
  }>
  VectorParameters: Array<{
    ExportIndex: number
    NodeName: string
    ParameterName: string
    R: number
    G: number
    B: number
    A: number
  }>
  TextureNodes: Array<{
    ExportIndex: number
    NodeName: string
    Class: string
    ParameterName?: string
    TextureName?: string
    ContentFile?: string
    SamplerType?: string
  }>
  FunctionCalls: Array<{
    ExportIndex: number
    NodeName: string
    FunctionName?: string
    ContentFile?: string
  }>
  Expressions: Array<{
    ExportIndex: number
    NodeName: string
    Class: string
    ParameterName?: string
  }>
  ImportedTextures: Array<{
    ObjectName: string
    ContentFile?: string
    Package?: string
  }>
}

const props = defineProps<{
  info: MaterialInfo
  path: string
}>()

const emit = defineEmits<{
  navigatePath: [targetPath: string]
  previewTexture: [path: string, label: string]
}>()

const project = useProject()
const message = useMessage()
const saving = ref(false)

interface ColorParam {
  r: number
  g: number
  b: number
  a: number
  hex: string
}

const editScalars = ref<Record<string, number>>({})
const origScalars = ref<Record<string, number>>({})
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
    return {
      r: parseInt(clean.substring(0, 2), 16) / 255,
      g: parseInt(clean.substring(2, 4), 16) / 255,
      b: parseInt(clean.substring(4, 6), 16) / 255
    }
  }
  return { r: 1, g: 1, b: 1 }
}

function initEditState() {
  const sMap: Record<string, number> = {}
  for (const p of props.info.ScalarParameters || []) {
    sMap[p.ParameterName] = p.DefaultValue
  }
  editScalars.value = { ...sMap }
  origScalars.value = { ...sMap }

  const vMap: Record<string, ColorParam> = {}
  for (const p of props.info.VectorParameters || []) {
    vMap[p.ParameterName] = {
      r: p.R,
      g: p.G,
      b: p.B,
      a: p.A,
      hex: toHex(p.R, p.G, p.B)
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

const domainLabel = computed(() => {
  const d = props.info.MaterialDomain || ''
  if (d.includes('MD_UI')) return 'UI (UserInterface)'
  if (d.includes('MD_Surface')) return 'Surface'
  if (d.includes('MD_PostProcess')) return 'Post Process'
  return d || 'Surface'
})

const blendModeLabel = computed(() => {
  const b = props.info.BlendMode || ''
  if (b.includes('BLEND_Opaque')) return 'Opaque'
  if (b.includes('BLEND_Masked')) return 'Masked'
  if (b.includes('BLEND_Translucent')) return 'Translucent'
  if (b.includes('BLEND_Additive')) return 'Additive'
  if (b.includes('BLEND_Modulate')) return 'Modulate'
  return b || 'Opaque'
})
</script>

<template>
  <div class="material-viewer">
    <!-- 头部着色器基础属性卡片 -->
    <n-card size="small" class="mat-section-card">
      <div class="mat-toolbar">
        <div class="mat-title-left">
          <span class="mat-title">{{ tr('ui.masterMaterialTitle') }}</span>
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

      <div class="mat-prop-grid">
        <div class="mat-prop-item">
          <span class="label">{{ tr('ui.materialDomain') }}:</span>
          <span class="value font-medium">{{ domainLabel }}</span>
        </div>
        <div class="mat-prop-item">
          <span class="label">{{ tr('ui.blendMode') }}:</span>
          <span class="value font-medium">{{ blendModeLabel }}</span>
        </div>
        <div class="mat-prop-item">
          <span class="label">{{ tr('ui.shadingModel') }}:</span>
          <span class="value">{{ info.ShadingModel || 'MSM_DefaultLit' }}</span>
        </div>
        <div class="mat-prop-item">
          <span class="label">{{ tr('ui.twoSided') }}:</span>
          <span class="value">{{ info.TwoSided ? tr('ui.yes') : tr('ui.no') }}</span>
        </div>
        <div v-if="info.CanMaskedBeAssumedOpaque" class="mat-prop-item">
          <span class="label">{{ tr('ui.maskedAssumedOpaque') }}:</span>
          <span class="value">{{ tr('ui.yes') }}</span>
        </div>
        <div v-if="info.StateId" class="mat-prop-item full-row">
          <span class="label">StateId:</span>
          <span class="value code-val">{{ info.StateId }}</span>
        </div>
      </div>
    </n-card>

    <!-- 关联贴图采样器 -->
    <n-card size="small" class="mat-section-card">
      <template #header>
        <div class="mat-card-header">
          <span>{{ tr('ui.texSamplersTitle') }}</span>
          <n-tag size="small">{{ info.TextureNodes?.length || 0 }}</n-tag>
        </div>
      </template>

      <div v-if="!info.TextureNodes?.length" class="empty-hint">{{ tr('ui.noTextureSamplers') }}</div>
      <div v-else class="tex-grid">
        <div v-for="node in info.TextureNodes" :key="node.ExportIndex" class="tex-card">
          <div class="tex-card-header">
            <span class="tex-node-name">[{{ node.ExportIndex }}] {{ node.NodeName }}</span>
            <n-tag v-if="node.ParameterName" type="warning" size="small">{{ node.ParameterName }}</n-tag>
          </div>
          <div class="tex-card-body">
            <div class="tex-field">
              <span class="f-label">{{ tr('ui.textureName') }}:</span>
              <span class="f-val font-medium">{{ node.TextureName || '—' }}</span>
            </div>
            <div v-if="node.ContentFile" class="tex-field">
              <span class="f-label">{{ tr('ui.assetPath') }}:</span>
              <a class="ref-link" @click.prevent="emit('navigatePath', node.ContentFile)">
                🔗 {{ node.ContentFile }}
              </a>
            </div>
            <div v-if="node.SamplerType" class="tex-field">
              <span class="f-label">{{ tr('ui.samplerType') }}:</span>
              <span class="f-val">{{ node.SamplerType }}</span>
            </div>
          </div>
          <div class="tex-card-footer">
            <n-button
              v-if="node.ContentFile"
              size="tiny"
              type="primary"
              secondary
              @click="emit('previewTexture', node.ContentFile, node.TextureName || 'Texture')"
            >
              <template #icon><n-icon :component="ImageIcon" /></template>
              {{ tr('ui.rawPreview') }}
            </n-button>
            <n-button
              v-if="node.ContentFile"
              size="tiny"
              secondary
              @click="emit('navigatePath', node.ContentFile)"
            >
              <template #icon><n-icon :component="ExternalLink" /></template>
              {{ tr('ui.openAsset') }}
            </n-button>
          </div>
        </div>
      </div>
    </n-card>

    <div class="two-col-row">
      <!-- 标量参数列表 (Scalars) -->
      <n-card size="small" class="mat-section-card col-half">
        <template #header>
          <div class="mat-card-header">
            <span>{{ tr('ui.scalarParamsGroup') }}</span>
            <n-tag size="small">{{ info.ScalarParameters?.length || 0 }}</n-tag>
          </div>
        </template>
        <div v-if="!info.ScalarParameters?.length" class="empty-hint">{{ tr('ui.noScalarParams') }}</div>
        <div v-else class="param-list">
          <div v-for="p in info.ScalarParameters" :key="p.ExportIndex" class="param-row">
            <div class="param-title">
              <span class="p-name">{{ p.ParameterName }}</span>
              <span class="p-node">({{ p.NodeName }})</span>
            </div>
            <div class="param-value">
              <n-input-number
                v-model:value="editScalars[p.ParameterName]"
                size="small"
                :step="0.01"
                style="width: 130px"
              />
              <span v-if="p.SliderMin !== undefined || p.SliderMax !== undefined" class="p-range">
                [{{ p.SliderMin ?? 0 }} ~ {{ p.SliderMax ?? 1 }}]
              </span>
            </div>
          </div>
        </div>
      </n-card>

      <!-- 矢量 / 颜色参数列表 (Vectors) -->
      <n-card size="small" class="mat-section-card col-half">
        <template #header>
          <div class="mat-card-header">
            <span>{{ tr('ui.vectorParamsGroup') }}</span>
            <n-tag size="small">{{ info.VectorParameters?.length || 0 }}</n-tag>
          </div>
        </template>
        <div v-if="!info.VectorParameters?.length" class="empty-hint">{{ tr('ui.noVectorParams') }}</div>
        <div v-else class="param-list">
          <div v-for="p in info.VectorParameters" :key="p.ExportIndex" class="param-row">
            <div class="param-title">
              <span class="p-name">{{ p.ParameterName }}</span>
              <span class="p-node">({{ p.NodeName }})</span>
            </div>
            <div v-if="editVectors[p.ParameterName]" class="param-value">
              <n-color-picker
                v-model:value="editVectors[p.ParameterName].hex"
                :show-alpha="false"
                size="small"
                style="width: 120px"
                @update:value="hex => onColorPickerChange(p.ParameterName, hex)"
              />
              <code class="rgba-detail">
                RGBA({{ editVectors[p.ParameterName].r.toFixed(2) }},
                {{ editVectors[p.ParameterName].g.toFixed(2) }},
                {{ editVectors[p.ParameterName].b.toFixed(2) }},
                {{ editVectors[p.ParameterName].a.toFixed(1) }})
              </code>
            </div>
          </div>
        </div>
      </n-card>
    </div>

    <!-- 材质函数引用 & 表达式节点总览 -->
    <n-card size="small" class="mat-section-card">
      <template #header>
        <div class="mat-card-header">
          <span>{{ tr('ui.expressionsListTitle') }}</span>
          <n-tag size="small">{{ info.Expressions?.length || 0 }}</n-tag>
        </div>
      </template>

      <div v-if="info.FunctionCalls?.length" class="func-calls-box">
        <div class="func-calls-title">{{ tr('ui.materialFunctionsUsed') }}:</div>
        <div class="func-tags">
          <div v-for="fn in info.FunctionCalls" :key="fn.ExportIndex" class="func-pill">
            <span class="fn-name">⚡ {{ fn.FunctionName || fn.NodeName }}</span>
            <a v-if="fn.ContentFile" class="ref-link" @click.prevent="emit('navigatePath', fn.ContentFile)">
              🔗 {{ fn.ContentFile }}
            </a>
          </div>
        </div>
      </div>

      <div class="expr-chips">
        <div
          v-for="e in info.Expressions"
          :key="e.ExportIndex"
          class="expr-chip"
          :title="e.Class"
        >
          <span class="chip-idx">#{{ e.ExportIndex }}</span>
          <span class="chip-class">{{ e.Class.replace('MaterialExpression', '') }}</span>
          <span v-if="e.ParameterName" class="chip-param">{{ e.ParameterName }}</span>
        </div>
      </div>
    </n-card>
  </div>
</template>

<style scoped>
.material-viewer {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  overflow-y: auto;
  padding-right: 4px;
}
.mat-section-card {
  border-radius: 6px;
}
.mat-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 12px;
  border-bottom: 1px solid #f0f0f4;
  padding-bottom: 10px;
}
.mat-title-left {
  display: flex;
  align-items: center;
  gap: 8px;
}
.mat-title {
  font-weight: 600;
  font-size: 14px;
}
.mat-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-weight: 600;
  font-size: 14px;
}
.mat-prop-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 10px 20px;
  font-size: 13px;
}
.mat-prop-item {
  display: flex;
  align-items: center;
  gap: 8px;
}
.mat-prop-item.full-row {
  grid-column: 1 / -1;
}
.mat-prop-item .label {
  color: #666;
  white-space: nowrap;
}
.mat-prop-item .value {
  color: #222;
}
.font-medium {
  font-weight: 500;
}
.code-val {
  font-family: monospace;
  background: #f4f4f8;
  padding: 2px 6px;
  border-radius: 4px;
}
.tex-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 12px;
}
.tex-card {
  border: 1px solid #e0e0e6;
  border-radius: 6px;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  background: #fafafc;
}
.tex-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid #eef0f4;
  padding-bottom: 6px;
}
.tex-node-name {
  font-weight: 600;
  font-size: 13px;
  color: #333;
}
.tex-card-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 12px;
}
.tex-field {
  display: flex;
  align-items: center;
  gap: 6px;
}
.tex-field .f-label {
  color: #777;
  white-space: nowrap;
}
.tex-field .f-val {
  color: #222;
}
.tex-card-footer {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 4px;
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
.two-col-row {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}
.col-half {
  flex: 1;
  min-width: 320px;
}
.empty-hint {
  color: #999;
  font-size: 13px;
  padding: 12px 0;
  text-align: center;
}
.param-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.param-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 10px;
  background: #f8f9fa;
  border-radius: 4px;
  font-size: 13px;
  gap: 12px;
}
.param-title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.p-name {
  font-weight: 600;
  color: #222;
}
.p-node {
  color: #888;
  font-size: 11px;
}
.param-value {
  display: flex;
  align-items: center;
  gap: 8px;
}
.p-range {
  color: #888;
  font-size: 11px;
}
.rgba-detail {
  font-size: 11px;
  color: #666;
}
.func-calls-box {
  margin-bottom: 12px;
  padding-bottom: 12px;
  border-bottom: 1px dashed #e0e0e6;
}
.func-calls-title {
  font-size: 12px;
  font-weight: 600;
  color: #555;
  margin-bottom: 6px;
}
.func-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.func-pill {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: 4px;
  padding: 3px 8px;
  font-size: 12px;
}
.fn-name {
  color: #166534;
  font-weight: 500;
}
.expr-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.expr-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 7px;
  border-radius: 3px;
  background: #f4f4f7;
  border: 1px solid #e5e5ea;
  font-size: 11px;
}
.chip-idx {
  color: #888;
}
.chip-class {
  color: #333;
  font-weight: 500;
}
.chip-param {
  color: #d97706;
  font-weight: 600;
  background: #fef3c7;
  padding: 0 3px;
  border-radius: 2px;
}
</style>
