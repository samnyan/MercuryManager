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
import { ExternalLink, Image as ImageIcon, Save, RotateCcw, FunctionSquare } from '@lucide/vue'
import { api, useProject } from '../../project'
import { tr } from '../../i18n'
import MaterialShaderCanvas, { type SimTextureItem } from './MaterialShaderCanvas.vue'

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

// 内联加载引用的材质函数详情与参数
interface InlineFunctionEntry {
  nodeName: string
  functionName: string
  contentFile?: string
  info?: any
  editScalars: Record<string, number>
  origScalars: Record<string, number>
  saving: boolean
}

const inlineFunctions = ref<InlineFunctionEntry[]>([])

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

async function loadInlineFunctions() {
  const calls = props.info.FunctionCalls || []
  const list: InlineFunctionEntry[] = []
  const seenFiles = new Set<string>()

  for (const fn of calls) {
    const key = fn.ContentFile || fn.FunctionName || fn.NodeName
    if (seenFiles.has(key)) continue
    seenFiles.add(key)

    const entry: InlineFunctionEntry = {
      nodeName: fn.NodeName,
      functionName: fn.FunctionName || fn.NodeName,
      contentFile: fn.ContentFile,
      editScalars: {},
      origScalars: {},
      saving: false
    }

    if (fn.ContentFile && project.id && fn.ContentFile.endsWith('.uasset')) {
      try {
        const res = await api<{ data: any }>(
          `/projects/${project.id}/resource-data?` + new URLSearchParams({ path: fn.ContentFile })
        )
        const fnInfo = res.data?.MaterialFunctionInfo
        if (fnInfo) {
          entry.info = fnInfo
          const sMap: Record<string, number> = {}
          for (const sp of fnInfo.ScalarParameters || []) {
            sMap[sp.ParameterName] = sp.DefaultValue
          }
          entry.editScalars = { ...sMap }
          entry.origScalars = { ...sMap }
        }
      } catch {
        // ignore
      }
    }

    list.push(entry)
  }

  inlineFunctions.value = list
}

function isFnDirty(fn: InlineFunctionEntry): boolean {
  for (const k of Object.keys(fn.editScalars)) {
    if (fn.editScalars[k] !== fn.origScalars[k]) return true
  }
  return false
}

async function saveInlineFunction(fn: InlineFunctionEntry) {
  if (!project.id || !fn.contentFile) return
  fn.saving = true
  try {
    await api(
      `/projects/${project.id}/resource-material-params?` +
        new URLSearchParams({ path: fn.contentFile }),
      'POST',
      {
        scalars: fn.editScalars
      }
    )
    fn.origScalars = { ...fn.editScalars }
    project.revision++
    message.success(tr('ui.matParamsSaved'))
  } catch (e: any) {
    message.error(e?.message || String(e))
  } finally {
    fn.saving = false
  }
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

  loadInlineFunctions()
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

// 传给 ShaderCanvas 的聚合数据
const simScalars = computed(() => {
  const merged = { ...editScalars.value }
  for (const fn of inlineFunctions.value) {
    for (const [k, v] of Object.entries(fn.editScalars)) {
      if (merged[k] === undefined) merged[k] = v
    }
  }
  return merged
})

const simVectors = computed(() => {
  const res: Record<string, { r: number; g: number; b: number; a: number }> = {}
  for (const [k, v] of Object.entries(editVectors.value)) {
    res[k] = { r: v.r, g: v.g, b: v.b, a: v.a }
  }
  return res
})

const simTextures = computed(() => {
  const res: Array<{ name: string; path: string }> = []
  const counts = new Map<string, number>()
  for (const node of props.info.TextureNodes || []) {
    if (node.ContentFile) counts.set(node.ContentFile, (counts.get(node.ContentFile) || 0) + 1)
  }
  const sortedNodes = [...(props.info.TextureNodes || [])].sort((a, b) => {
    const aSample = a.Class === 'MaterialExpressionTextureSample' ? 1 : 0
    const bSample = b.Class === 'MaterialExpressionTextureSample' ? 1 : 0
    if (aSample !== bSample) return bSample - aSample
    const ca = counts.get(a.ContentFile || '') || 0
    const cb = counts.get(b.ContentFile || '') || 0
    return cb - ca
  })
  for (const node of sortedNodes) {
    if (node.ContentFile) {
      res.push({
        name: node.ParameterName || node.TextureName || node.NodeName,
        path: node.ContentFile
      })
    }
  }
  for (const fn of inlineFunctions.value) {
    for (const tn of fn.info?.TextureNodes || []) {
      if (tn.ContentFile && tn.ContentFile.endsWith('.uasset')) {
        res.push({
          name: `[${fn.functionName}] ${tn.ParameterName || tn.TextureName || tn.NodeName}`,
          path: tn.ContentFile
        })
      }
    }
  }
  return res
})

const simFunctionNames = computed(() => {
  return (props.info.FunctionCalls || []).map(f => f.FunctionName || '').filter(Boolean)
})

function getTextureImageUrl(targetPath: string): string {
  return `/api/projects/${project.id}/resource-image?path=${encodeURIComponent(targetPath)}&mip=0`
}
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

    <!-- 关联贴图采样器 (上方直接显示缩略图，点击弹窗放大) -->
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
          <div
            v-if="node.ContentFile"
            class="tex-thumb-stage"
            @click="emit('previewTexture', node.ContentFile, node.ParameterName || node.TextureName || 'Texture')"
          >
            <img
              :src="getTextureImageUrl(node.ContentFile)"
              class="tex-thumb-img"
              :alt="node.TextureName"
            />
            <div class="thumb-zoom-overlay">
              <n-icon :component="ImageIcon" />
            </div>
          </div>

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
        </div>
      </div>
    </n-card>

    <div class="two-col-row">
      <div class="col-left-params">
        <!-- 标量参数列表 (Scalars) -->
        <n-card size="small" class="mat-section-card">
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
        <n-card size="small" class="mat-section-card">
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

      <!-- 右侧：UMG 实时材质动画模拟预览器 -->
      <div class="col-right-sim">
        <material-shader-canvas
          :material-path="props.path"
          :blend-mode="info.BlendMode"
          :scalars="simScalars"
          :vectors="simVectors"
          :textures="simTextures"
          :function-names="simFunctionNames"
        />
      </div>
    </div>

    <!-- 材质函数引用卡片 (可内联编辑函数参数 + 跳转详情) -->
    <n-card v-if="inlineFunctions.length" size="small" class="mat-section-card">
      <template #header>
        <div class="mat-card-header">
          <span>{{ tr('ui.materialFunctionsUsed') }}</span>
          <n-tag size="small">{{ inlineFunctions.length }}</n-tag>
        </div>
      </template>

      <div class="fn-cards-grid">
        <div v-for="fn in inlineFunctions" :key="fn.nodeName" class="fn-detail-card">
          <div class="fn-card-header">
            <div class="fn-header-left">
              <n-icon :component="FunctionSquare" color="#166534" />
              <span class="fn-title-text">{{ fn.functionName }}</span>
              <span class="fn-node-sub">({{ fn.nodeName }})</span>
            </div>
            <n-space size="small">
              <n-button
                v-if="fn.contentFile && isFnDirty(fn)"
                size="tiny"
                type="primary"
                :loading="fn.saving"
                @click="saveInlineFunction(fn)"
              >
                <template #icon><n-icon :component="Save" /></template>
                {{ tr('ui.saveToWorkspace') }}
              </n-button>
              <n-button
                v-if="fn.contentFile"
                size="tiny"
                secondary
                @click="emit('navigatePath', fn.contentFile)"
              >
                <template #icon><n-icon :component="ExternalLink" /></template>
                {{ tr('ui.openAsset') }}
              </n-button>
            </n-space>
          </div>

          <div v-if="fn.info?.Description" class="fn-desc">
            {{ fn.info.Description }}
          </div>

          <!-- 函数内包含的标量参数可直接在当前面板内修改 -->
          <div v-if="fn.info?.ScalarParameters?.length" class="fn-params-section">
            <div v-for="sp in fn.info.ScalarParameters" :key="sp.ParameterName" class="param-row">
              <div class="param-title">
                <span class="p-name">{{ sp.ParameterName }}</span>
                <span class="p-node">(Function Default)</span>
              </div>
              <div class="param-value">
                <n-input-number
                  v-model:value="fn.editScalars[sp.ParameterName]"
                  size="small"
                  :step="0.01"
                  style="width: 120px"
                />
              </div>
            </div>
          </div>

          <!-- 函数内包含的采样贴图缩略图 -->
          <div v-if="fn.info?.TextureNodes?.length" class="fn-tex-row">
            <div
              v-for="tn in fn.info.TextureNodes.filter((x: any) => x.ContentFile)"
              :key="tn.ExportIndex"
              class="fn-mini-tex"
              @click="emit('previewTexture', tn.ContentFile, tn.TextureName || 'Texture')"
            >
              <img
                :src="getTextureImageUrl(tn.ContentFile)"
                class="fn-mini-img"
                :alt="tn.TextureName"
              />
              <span class="fn-mini-name">{{ tn.TextureName || tn.NodeName }}</span>
            </div>
          </div>
        </div>
      </div>
    </n-card>

    <!-- 表达式节点总览 -->
    <n-card size="small" class="mat-section-card">
      <template #header>
        <div class="mat-card-header">
          <span>{{ tr('ui.expressionsListTitle') }}</span>
          <n-tag size="small">{{ info.Expressions?.length || 0 }}</n-tag>
        </div>
      </template>

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
  grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
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
.fn-cards-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 12px;
}
.fn-detail-card {
  border: 1px solid #bbf7d0;
  background: #f8fef9;
  border-radius: 6px;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.fn-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.fn-header-left {
  display: flex;
  align-items: center;
  gap: 6px;
}
.fn-title-text {
  font-weight: 600;
  font-size: 13px;
  color: #166534;
}
.fn-node-sub {
  font-size: 11px;
  color: #777;
}
.fn-desc {
  font-size: 12px;
  color: #555;
  background: #eefdf2;
  padding: 4px 8px;
  border-radius: 4px;
}
.fn-params-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.fn-tex-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.fn-mini-tex {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 8px;
  background: #fff;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  cursor: pointer;
}
.fn-mini-img {
  width: 32px;
  height: 32px;
  object-fit: contain;
  background: #f0f0f4;
  border-radius: 3px;
}
.fn-mini-name {
  font-size: 12px;
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
