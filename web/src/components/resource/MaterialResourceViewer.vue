<script setup lang="ts">
import { computed } from 'vue'
import { NCard, NTag, NSpace, NDivider, NButton, NIcon } from 'naive-ui'
import { Eye, ExternalLink, Image as ImageIcon, Sliders, Palette, Box } from '@lucide/vue'

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

function rgbaStyle(v: { R: number; G: number; B: number; A: number }) {
  const r = Math.round(Math.min(1, Math.max(0, v.R)) * 255)
  const g = Math.round(Math.min(1, Math.max(0, v.G)) * 255)
  const b = Math.round(Math.min(1, Math.max(0, v.B)) * 255)
  return `rgba(${r}, ${g}, ${b}, ${v.A})`
}

function hexColor(v: { R: number; G: number; B: number }) {
  const r = Math.round(Math.min(1, Math.max(0, v.R)) * 255).toString(16).padStart(2, '0')
  const g = Math.round(Math.min(1, Math.max(0, v.G)) * 255).toString(16).padStart(2, '0')
  const b = Math.round(Math.min(1, Math.max(0, v.B)) * 255).toString(16).padStart(2, '0')
  return `#${r}${g}${b}`.toUpperCase()
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
  if (b.includes('BLEND_Opaque')) return 'Opaque (不透明)'
  if (b.includes('BLEND_Masked')) return 'Masked (镂空遮罩)'
  if (b.includes('BLEND_Translucent')) return 'Translucent (半透明混合)'
  if (b.includes('BLEND_Additive')) return 'Additive (加法叠加)'
  if (b.includes('BLEND_Modulate')) return 'Modulate (正片叠底)'
  return b || 'Opaque'
})
</script>

<template>
  <div class="material-viewer">
    <!-- 头部着色器基础属性卡片 -->
    <n-card size="small" class="mat-section-card">
      <template #header>
        <div class="mat-card-header">
          <span>母版材质属性 (Master Material)</span>
          <n-tag type="info" size="small">{{ info.Name }}</n-tag>
        </div>
      </template>

      <div class="mat-prop-grid">
        <div class="mat-prop-item">
          <span class="label">材质域 (Domain):</span>
          <span class="value font-medium">{{ domainLabel }}</span>
        </div>
        <div class="mat-prop-item">
          <span class="label">混合模式 (Blend Mode):</span>
          <span class="value font-medium">{{ blendModeLabel }}</span>
        </div>
        <div class="mat-prop-item">
          <span class="label">着色模型 (Shading Model):</span>
          <span class="value">{{ info.ShadingModel || 'MSM_DefaultLit' }}</span>
        </div>
        <div class="mat-prop-item">
          <span class="label">双面渲染 (Two Sided):</span>
          <span class="value">{{ info.TwoSided ? '是' : '否' }}</span>
        </div>
        <div v-if="info.CanMaskedBeAssumedOpaque" class="mat-prop-item">
          <span class="label">遮罩假设不透明:</span>
          <span class="value">True</span>
        </div>
        <div v-if="info.StateId" class="mat-prop-item full-row">
          <span class="label">StateId (编译指纹):</span>
          <span class="value code-val">{{ info.StateId }}</span>
        </div>
      </div>
    </n-card>

    <!-- 关联贴图采样器 -->
    <n-card size="small" class="mat-section-card">
      <template #header>
        <div class="mat-card-header">
          <span>引用贴图采样 (Texture Samplers / Parameters)</span>
          <n-tag size="small">{{ info.TextureNodes?.length || 0 }} 个采样节点</n-tag>
        </div>
      </template>

      <div v-if="!info.TextureNodes?.length" class="empty-hint">无显式纹理采样节点</div>
      <div v-else class="tex-grid">
        <div v-for="node in info.TextureNodes" :key="node.ExportIndex" class="tex-card">
          <div class="tex-card-header">
            <span class="tex-node-name">[{{ node.ExportIndex }}] {{ node.NodeName }}</span>
            <n-tag v-if="node.ParameterName" type="warning" size="small">{{ node.ParameterName }}</n-tag>
          </div>
          <div class="tex-card-body">
            <div class="tex-field">
              <span class="f-label">贴图名称:</span>
              <span class="f-val font-medium">{{ node.TextureName || '—' }}</span>
            </div>
            <div v-if="node.ContentFile" class="tex-field">
              <span class="f-label">资产路径:</span>
              <a class="ref-link" @click.prevent="emit('navigatePath', node.ContentFile)">
                🔗 {{ node.ContentFile }}
              </a>
            </div>
            <div v-if="node.SamplerType" class="tex-field">
              <span class="f-label">采样类型:</span>
              <span class="f-val">{{ node.SamplerType }}</span>
            </div>
          </div>
          <div class="tex-card-footer">
            <n-button
              v-if="node.ContentFile"
              size="tiny"
              type="primary"
              @click="emit('previewTexture', node.ContentFile, node.TextureName || 'Texture')"
            >
              <template #icon><n-icon :component="ImageIcon" /></template>
              预览贴图
            </n-button>
            <n-button
              v-if="node.ContentFile"
              size="tiny"
              secondary
              @click="emit('navigatePath', node.ContentFile)"
            >
              <template #icon><n-icon :component="ExternalLink" /></template>
              打开资产
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
            <span>标量参数 (Scalar Parameters)</span>
            <n-tag size="small">{{ info.ScalarParameters?.length || 0 }} 个</n-tag>
          </div>
        </template>
        <div v-if="!info.ScalarParameters?.length" class="empty-hint">无标量参数</div>
        <div v-else class="param-list">
          <div v-for="p in info.ScalarParameters" :key="p.ExportIndex" class="param-row">
            <div class="param-title">
              <span class="p-name">{{ p.ParameterName }}</span>
              <span class="p-node">({{ p.NodeName }})</span>
            </div>
            <div class="param-value">
              <code>{{ p.DefaultValue }}</code>
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
            <span>矢量 / 颜色参数 (Vector Parameters)</span>
            <n-tag size="small">{{ info.VectorParameters?.length || 0 }} 个</n-tag>
          </div>
        </template>
        <div v-if="!info.VectorParameters?.length" class="empty-hint">无矢量/颜色参数</div>
        <div v-else class="param-list">
          <div v-for="p in info.VectorParameters" :key="p.ExportIndex" class="param-row">
            <div class="param-title">
              <div class="color-swatch-box" :style="{ backgroundColor: rgbaStyle(p) }" />
              <span class="p-name">{{ p.ParameterName }}</span>
              <span class="p-node">({{ p.NodeName }})</span>
            </div>
            <div class="param-value">
              <span class="hex-tag">{{ hexColor(p) }}</span>
              <code class="rgba-detail">RGBA({{ p.R.toFixed(2) }}, {{ p.G.toFixed(2) }}, {{ p.B.toFixed(2) }}, {{ p.A.toFixed(2) }})</code>
            </div>
          </div>
        </div>
      </n-card>
    </div>

    <!-- 材质函数引用 & 表达式节点总览 -->
    <n-card size="small" class="mat-section-card">
      <template #header>
        <div class="mat-card-header">
          <span>材质表达式节点清单 (Expressions)</span>
          <n-tag size="small">{{ info.Expressions?.length || 0 }} 个节点</n-tag>
        </div>
      </template>

      <div v-if="info.FunctionCalls?.length" class="func-calls-box">
        <div class="func-calls-title">使用的材质函数 (Material Functions):</div>
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
.color-swatch-box {
  width: 18px;
  height: 18px;
  border-radius: 3px;
  border: 1px solid rgba(0, 0, 0, 0.2);
  box-shadow: 0 1px 2px rgba(0,0,0,0.1);
}
.hex-tag {
  font-family: monospace;
  font-weight: 600;
  font-size: 11px;
  background: #eee;
  padding: 1px 4px;
  border-radius: 3px;
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
