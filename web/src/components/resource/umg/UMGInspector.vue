<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import {
  NTag,
  NButton,
  NCollapse,
  NCollapseItem,
  NImage,
  NSelect,
  NSlider,
  NInputNumber,
  NIcon,
  useMessage
} from 'naive-ui'
import { Save, ExternalLink, RotateCcw } from '@lucide/vue'
import { tr } from '../../../i18n'
import type { UMGNode, ComputedLayout } from './types'
import { api, useProject } from '../../../project'
import {
  normalizeMaterialEntry,
  renderUMGMaterialToCanvas
} from '../../../utils/material-shader/UMGMaterialBridge'

const props = defineProps<{
  node: UMGNode | null
  layout: ComputedLayout | null
  materials?: Record<string, any>
  assetPath?: string
}>()

const emit = defineEmits<{
  'navigate-path': [path: string]
  'preview-texture': [path: string]
  'node-updated': []
}>()

const project = useProject()
const message = useMessage()
const materialThumbnails = ref<Record<string, string>>({})

// 可编辑的控件属性状态 (Visibility & RenderOpacity)
const editVisibility = ref<string>('Visible')
const origVisibility = ref<string>('Visible')
const editOpacity = ref<number>(1)
const origOpacity = ref<number>(1)
const savingProps = ref(false)

const visibilityOptions = computed(() => [
  { label: tr('ui.umgVisibilityVisible'), value: 'Visible' },
  { label: tr('ui.umgVisibilityCollapsed'), value: 'Collapsed' },
  { label: tr('ui.umgVisibilityHidden'), value: 'Hidden' },
  { label: tr('ui.umgVisibilityHitTestInvisible'), value: 'HitTestInvisible' },
  { label: tr('ui.umgVisibilitySelfHitTestInvisible'), value: 'SelfHitTestInvisible' }
])

watch(
  () => props.node,
  n => {
    if (!n) return
    const v = n.visual?.Visibility || 'Visible'
    const op = typeof n.visual?.RenderOpacity === 'number' ? n.visual.RenderOpacity : 1
    editVisibility.value = v
    origVisibility.value = v
    editOpacity.value = Number(op.toFixed(3))
    origOpacity.value = Number(op.toFixed(3))
  },
  { immediate: true }
)

// 当用户实时调整编辑框时，同步回 node.visual 让画布实时预览
watch(editVisibility, val => {
  if (props.node && props.node.visual) {
    props.node.visual.Visibility = val
  }
})

watch(editOpacity, val => {
  if (props.node && props.node.visual && typeof val === 'number') {
    props.node.visual.RenderOpacity = val
  }
})

const isPropsDirty = computed(() => {
  return (
    editVisibility.value !== origVisibility.value ||
    Math.abs(editOpacity.value - origOpacity.value) > 0.001
  )
})

function resetNodeProps() {
  editVisibility.value = origVisibility.value
  editOpacity.value = origOpacity.value
}

// 判断当前节点是否属于主资产（非嵌套跨包展开节点），支持直接写回草稿
const targetPackagePath = computed(() => {
  if (!props.node) return ''
  if (props.node.id.includes('/')) return ''
  return props.assetPath || ''
})

async function saveNodeProps() {
  if (!project.id || !props.node || !targetPackagePath.value) return
  savingProps.value = true
  try {
    await api(
      `/projects/${project.id}/resource-umg-widget?` +
        new URLSearchParams({ path: targetPackagePath.value }),
      'POST',
      {
        exportIndex: props.node.exportIndex,
        visibility: editVisibility.value,
        renderOpacity: editOpacity.value
      }
    )
    origVisibility.value = editVisibility.value
    origOpacity.value = editOpacity.value
    if (props.node.sources) {
      props.node.sources['visual.Visibility'] = 'serialized'
      props.node.sources['visual.RenderOpacity'] = 'serialized'
    }
    await project.status()
    project.revision++
    message.success(tr('ui.umgPropsSaved'))
    emit('node-updated')
  } catch (e: any) {
    message.error(e?.message || String(e))
  } finally {
    savingProps.value = false
  }
}

watch(
  () => [props.node?.id, props.materials],
  async () => {
    materialThumbnails.value = {}
    if (!props.node || !props.materials) return
    const matResources = props.node.resources?.filter(
      r =>
        r.className === 'Material' ||
        r.className === 'MaterialInstanceConstant' ||
        r.className === 'MaterialInstance'
    )
    if (!matResources?.length) return

    for (const r of matResources) {
      const rawMat = props.materials[r.assetPath]
      if (!rawMat) continue
      const spec = normalizeMaterialEntry(rawMat)
      if (!spec) continue
      try {
        const canvas = await renderUMGMaterialToCanvas(
          spec,
          async (texPath: string) => {
            if (!project.id) return null
            const url =
              `/api/projects/${project.id}/resource-image?` +
              new URLSearchParams({ path: texPath, mip: '0' })
            return new Promise<HTMLImageElement | null>(resolve => {
              const img = new Image()
              img.crossOrigin = 'anonymous'
              img.onload = () => resolve(img)
              img.onerror = () => resolve(null)
              img.src = url
            })
          },
          128,
          128
        )
        if (canvas) {
          materialThumbnails.value[r.assetPath] = canvas.toDataURL()
        }
      } catch {}
    }
  },
  { immediate: true }
)

function getTextureThumbnailUrl(assetPath: string): string {
  if (!project.id) return ''
  return (
    `/api/projects/${project.id}/resource-image?` +
    new URLSearchParams({ path: assetPath, mip: '0' })
  )
}

// 解析 RGBA 颜色对象为 CSS 颜色与 Hex
function parseRgba(c: any): { css: string; label: string } | null {
  if (!c || typeof c !== 'object') return null
  const col = c.SpecifiedColor ?? c
  if (typeof col.R !== 'number' && typeof col.G !== 'number' && typeof col.B !== 'number') {
    return null
  }
  const r = Math.round(Math.min(1, Math.max(0, col.R ?? 0)) * 255)
  const g = Math.round(Math.min(1, Math.max(0, col.G ?? 0)) * 255)
  const b = Math.round(Math.min(1, Math.max(0, col.B ?? 0)) * 255)
  const a = Number((col.A ?? 1).toFixed(2))
  return {
    css: `rgba(${r}, ${g}, ${b}, ${a})`,
    label: `RGBA(${(col.R ?? 0).toFixed(2)}, ${(col.G ?? 0).toFixed(2)}, ${(col.B ?? 0).toFixed(2)}, ${a})`
  }
}

// 提取结构化 Slot 信息
const parsedSlotInfo = computed(() => {
  const s = props.node?.slot
  if (!s || !s.values) return null
  const v = s.values
  const ld = v.LayoutData || {}
  const off = ld.Offsets || v.Offset || v.Padding
  const anc = ld.Anchors || v.Anchors
  const align = ld.Alignment || v.Alignment

  return {
    className: s.className,
    exportIndex: s.exportIndex,
    offsets: off,
    anchors: anc,
    alignment: align,
    autoSize: v.bAutoSize,
    zOrder: v.ZOrder,
    hAlign: v.HorizontalAlignment,
    vAlign: v.VerticalAlignment,
    size: v.Size
  }
})

// 提取 RenderTransform 详情
const parsedTransform = computed(() => {
  const v = props.node?.visual
  if (!v) return null
  const rt = v.RenderTransform
  const pivot = v.RenderTransformPivot || { X: 0.5, Y: 0.5 }
  return {
    translation: rt?.Translation || { X: 0, Y: 0 },
    scale: rt?.Scale || { X: 1, Y: 1 },
    shear: rt?.Shear || { X: 0, Y: 0 },
    angle: rt?.Angle ?? 0,
    pivot
  }
})

// 提取颜色属性列表
const parsedColors = computed(() => {
  const v = props.node?.visual
  if (!v) return []
  const list: Array<{ key: string; css: string; label: string }> = []
  for (const k of ['ColorAndOpacity', 'BrushColor', 'ContentColorAndOpacity']) {
    const parsed = parseRgba(v[k])
    if (parsed) list.push({ key: k, ...parsed })
  }
  if (v.Brush?.TintColor) {
    const parsed = parseRgba(v.Brush.TintColor)
    if (parsed) list.push({ key: 'Brush.TintColor', ...parsed })
  }
  return list
})

// 提取其他未归类的原始 Visual 属性
const extraVisualEntries = computed(() => {
  const v = props.node?.visual
  if (!v) return []
  const handled = new Set([
    'Visibility',
    'RenderOpacity',
    'RenderTransform',
    'RenderTransformPivot',
    'ColorAndOpacity',
    'BrushColor',
    'ContentColorAndOpacity',
    'Brush',
    'Text',
    'Font'
  ])
  return Object.entries(v).filter(([k, val]) => !handled.has(k) && val !== undefined && val !== null)
})
</script>

<template>
  <div class="umg-inspector">
    <div v-if="!node" class="empty-inspector">
      <span>在左侧树或画面中点击组件查看与编辑详情</span>
    </div>

    <div v-else class="inspector-content">
      <!-- 顶部标题栏 -->
      <div class="node-title-bar">
        <div class="title-left">
          <h4>{{ node.name }}</h4>
          <span class="export-badge">#{{ node.exportIndex }}</span>
        </div>
        <n-tag size="small" type="info" :bordered="false">{{ node.className }}</n-tag>
      </div>

      <!-- 若是嵌套子 Widget (ChildWidget)，提供一键打开该子蓝图按钮 -->
      <div v-if="node.childWidget?.assetPath" class="child-widget-banner">
        <div class="cw-info">
          <span class="cw-label">子蓝图控件 (Child Widget)</span>
          <span class="cw-path">{{ node.childWidget.assetPath }}</span>
        </div>
        <n-button
          size="tiny"
          type="primary"
          secondary
          @click="emit('navigate-path', node.childWidget.assetPath!)"
        >
          <template #icon><n-icon :component="ExternalLink" /></template>
          打开子蓝图
        </n-button>
      </div>

      <!-- 1. 可见性与不透明度（支持实时预览与保存写回工程草稿） -->
      <div class="inspector-section highlight-edit-section">
        <div class="section-header-row">
          <span class="section-title">显示状态与透明度 (可编辑)</span>
          <n-tag
            v-if="node.sources?.['visual.Visibility'] === 'serialized'"
            size="tiny"
            type="success"
            :bordered="false"
          >
            Serialized
          </n-tag>
          <n-tag v-else size="tiny" :bordered="false">Default</n-tag>
        </div>

        <div class="edit-row">
          <span class="edit-label">Visibility</span>
          <n-select
            v-model:value="editVisibility"
            :options="visibilityOptions"
            size="tiny"
            style="flex: 1"
          />
        </div>

        <div class="edit-row">
          <span class="edit-label">Opacity</span>
          <n-slider
            v-model:value="editOpacity"
            :min="0"
            :max="1"
            :step="0.01"
            style="flex: 1"
          />
          <n-input-number
            v-model:value="editOpacity"
            :min="0"
            :max="1"
            :step="0.05"
            size="tiny"
            :show-button="false"
            style="width: 58px"
          />
        </div>

        <div v-if="isPropsDirty && targetPackagePath" class="save-actions-row">
          <n-button size="tiny" quaternary @click="resetNodeProps">
            <template #icon><n-icon :component="RotateCcw" /></template>
            重置
          </n-button>
          <n-button
            size="tiny"
            type="primary"
            :loading="savingProps"
            @click="saveNodeProps"
          >
            <template #icon><n-icon :component="Save" /></template>
            {{ tr('ui.umgSaveProps') }}
          </n-button>
        </div>
      </div>

      <!-- 2. 计算布局与 Slot 详情 -->
      <div class="inspector-section">
        <div class="section-title">
          {{ tr('ui.umgPropSlot') }} ({{ node.slot?.className || 'Root' }})
        </div>
        <div v-if="layout" class="kv-grid">
          <span class="k">屏幕位置 (X, Y)</span>
          <span class="v">{{ Math.round(layout.rect.x) }}, {{ Math.round(layout.rect.y) }}</span>
          <span class="k">屏幕尺寸 (W × H)</span>
          <span class="v">{{ Math.round(layout.rect.width) }} × {{ Math.round(layout.rect.height) }}</span>
        </div>

        <div v-if="parsedSlotInfo" class="structured-box">
          <template v-if="parsedSlotInfo.anchors">
            <div class="struct-row">
              <span class="sk">Anchors Min/Max:</span>
              <code class="sv">
                ({{ parsedSlotInfo.anchors.Minimum?.X ?? 0 }}, {{ parsedSlotInfo.anchors.Minimum?.Y ?? 0 }})
                →
                ({{ parsedSlotInfo.anchors.Maximum?.X ?? 0 }}, {{ parsedSlotInfo.anchors.Maximum?.Y ?? 0 }})
              </code>
            </div>
          </template>
          <template v-if="parsedSlotInfo.offsets">
            <div class="struct-row">
              <span class="sk">Offsets (L/T/R/B):</span>
              <code class="sv">
                {{ parsedSlotInfo.offsets.Left ?? 0 }},
                {{ parsedSlotInfo.offsets.Top ?? 0 }},
                {{ parsedSlotInfo.offsets.Right ?? 0 }},
                {{ parsedSlotInfo.offsets.Bottom ?? 0 }}
              </code>
            </div>
          </template>
          <template v-if="parsedSlotInfo.alignment">
            <div class="struct-row">
              <span class="sk">Alignment (Pivot):</span>
              <code class="sv">
                X: {{ parsedSlotInfo.alignment.X ?? 0 }}, Y: {{ parsedSlotInfo.alignment.Y ?? 0 }}
              </code>
            </div>
          </template>
          <template v-if="parsedSlotInfo.zOrder !== undefined">
            <div class="struct-row">
              <span class="sk">ZOrder:</span>
              <code class="sv">{{ parsedSlotInfo.zOrder }}</code>
            </div>
          </template>
          <template v-if="parsedSlotInfo.autoSize !== undefined">
            <div class="struct-row">
              <span class="sk">AutoSize:</span>
              <code class="sv">{{ parsedSlotInfo.autoSize }}</code>
            </div>
          </template>
          <template v-if="parsedSlotInfo.hAlign || parsedSlotInfo.vAlign">
            <div class="struct-row">
              <span class="sk">Align (H / V):</span>
              <code class="sv">{{ parsedSlotInfo.hAlign || '-' }} / {{ parsedSlotInfo.vAlign || '-' }}</code>
            </div>
          </template>
        </div>
      </div>

      <!-- 3. 变换与枢轴 (RenderTransform) -->
      <div v-if="parsedTransform" class="inspector-section">
        <div class="section-title">{{ tr('ui.umgPropTransform') }}</div>
        <div class="kv-grid">
          <span class="k">Translation</span>
          <span class="v">
            X: {{ parsedTransform.translation.X ?? 0 }}, Y: {{ parsedTransform.translation.Y ?? 0 }}
          </span>
          <span class="k">Scale</span>
          <span class="v">
            X: {{ parsedTransform.scale.X ?? 1 }}, Y: {{ parsedTransform.scale.Y ?? 1 }}
          </span>
          <span class="k">Shear / Angle</span>
          <span class="v">
            ({{ parsedTransform.shear.X ?? 0 }}, {{ parsedTransform.shear.Y ?? 0 }}) | {{ parsedTransform.angle }}°
          </span>
          <span class="k">Pivot</span>
          <span class="v">
            X: {{ parsedTransform.pivot.X ?? 0.5 }}, Y: {{ parsedTransform.pivot.Y ?? 0.5 }}
          </span>
        </div>
      </div>

      <!-- 4. 颜色、笔刷与文本样式 -->
      <div
        v-if="parsedColors.length || node.visual?.Brush || node.visual?.Text || node.visual?.Font"
        class="inspector-section"
      >
        <div class="section-title">{{ tr('ui.umgPropBrush') }}</div>

        <!-- 颜色色块展示 -->
        <div v-for="col in parsedColors" :key="col.key" class="color-swatch-row">
          <span class="k">{{ col.key }}</span>
          <div class="swatch-val">
            <span class="color-box" :style="{ backgroundColor: col.css }" />
            <code class="sv">{{ col.label }}</code>
          </div>
        </div>

        <!-- Brush 详情 -->
        <div v-if="node.visual?.Brush" class="structured-box">
          <div v-if="node.visual.Brush.ImageSize" class="struct-row">
            <span class="sk">Brush ImageSize:</span>
            <code class="sv">
              {{ node.visual.Brush.ImageSize.X ?? 32 }} × {{ node.visual.Brush.ImageSize.Y ?? 32 }}
            </code>
          </div>
          <div v-if="node.visual.Brush.DrawAs" class="struct-row">
            <span class="sk">DrawAs / Tiling:</span>
            <code class="sv">
              {{ node.visual.Brush.DrawAs }} / {{ node.visual.Brush.Tiling || 'NoTile' }}
            </code>
          </div>
        </div>

        <!-- Text 与 Font 详情 -->
        <div v-if="node.visual?.Text !== undefined || node.visual?.Font" class="structured-box">
          <div v-if="node.visual?.Text !== undefined" class="struct-row">
            <span class="sk">Text Content:</span>
            <code class="sv">{{ node.visual.Text || '(Empty)' }}</code>
          </div>
          <div v-if="node.visual?.Font" class="struct-row">
            <span class="sk">Font Info:</span>
            <code class="sv">
              Size: {{ node.visual.Font.Size ?? '-' }}
              {{ (node.visual.Font as any).TypefaceFontName ? `| ${(node.visual.Font as any).TypefaceFontName}` : '' }}
            </code>
          </div>
        </div>
      </div>

      <!-- 5. 关联外部资源（点击弹窗打开详情） -->
      <div class="inspector-section">
        <div class="section-title">关联外部资源 ({{ node.resources?.length || 0 }})</div>
        <div v-if="node.resources?.length" class="resource-card-list">
          <div
            v-for="r in node.resources"
            :key="r.assetPath"
            class="resource-card clickable-card"
            @click="emit('navigate-path', r.assetPath)"
          >
            <div v-if="r.className === 'Texture2D'" class="card-thumb" @click.stop="emit('preview-texture', r.assetPath)">
              <n-image
                :src="getTextureThumbnailUrl(r.assetPath)"
                width="48"
                height="48"
                object-fit="cover"
                preview-disabled
              />
            </div>
            <div v-else-if="materialThumbnails[r.assetPath]" class="card-thumb">
              <n-image
                :src="materialThumbnails[r.assetPath]"
                width="48"
                height="48"
                object-fit="contain"
                preview-disabled
              />
            </div>
            <div v-else class="card-type-icon">
              <span class="type-badge">{{ (r.className || r.role || 'RES').slice(0, 4) }}</span>
            </div>

            <div class="card-info">
              <div class="card-top">
                <n-tag size="tiny" :bordered="false" type="info">{{ r.className || r.role }}</n-tag>
                <span class="role-hint">{{ r.role }}</span>
              </div>
              <span class="card-path" :title="r.assetPath">{{ r.assetPath }}</span>
            </div>
          </div>
        </div>
        <span v-else class="muted-note">无直接绑定的外部资源</span>
      </div>

      <!-- 6. 其他附加属性与高级诊断折叠面板 -->
      <n-collapse>
        <n-collapse-item
          v-if="extraVisualEntries.length"
          :title="`更多序列化属性 (${extraVisualEntries.length})`"
          name="extra"
        >
          <div class="raw-slot-box">
            <div v-for="[k, val] in extraVisualEntries" :key="k" class="kv-row">
              <span class="k">{{ k }}:</span>
              <code class="v">{{ JSON.stringify(val) }}</code>
            </div>
          </div>
        </n-collapse-item>

        <n-collapse-item title="预览能力与底层诊断" name="raw">
          <div class="cap-tags" style="margin-bottom: 8px">
            <n-tag size="small" :type="node.preview.layout === 'supported' ? 'success' : 'warning'">
              布局: {{ node.preview.layout }}
            </n-tag>
            <n-tag size="small" :type="node.preview.appearance === 'supported' ? 'success' : 'warning'">
              外观: {{ node.preview.appearance }}
            </n-tag>
          </div>
          <ul v-if="layout?.issues.length" class="issue-list">
            <li v-for="issue in layout.issues" :key="issue">{{ issue }}</li>
          </ul>
          <ul v-if="node.preview.issues?.length" class="issue-list">
            <li v-for="(iss, idx) in node.preview.issues" :key="idx">{{ iss }}</li>
          </ul>
          <div class="kv-grid" style="margin-top: 8px">
            <span class="k">Export Index</span>
            <span class="v">#{{ node.exportIndex }}</span>
            <span class="k">Node ID</span>
            <span class="v">{{ node.id }}</span>
            <span class="k">Tree ID</span>
            <span class="v">{{ node.treeId }}</span>
          </div>
        </n-collapse-item>
      </n-collapse>
    </div>
  </div>
</template>

<style scoped>
.umg-inspector {
  width: 340px;
  flex-shrink: 0;
  height: 100%;
  overflow-y: auto;
  border-left: 1px solid var(--n-border-color);
  padding: 12px;
  box-sizing: border-box;
}
.empty-inspector {
  color: #888;
  font-size: 12px;
  text-align: center;
  margin-top: 40px;
}
.inspector-content {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.node-title-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.title-left {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
.node-title-bar h4 {
  margin: 0;
  font-size: 14px;
  word-break: break-all;
}
.export-badge {
  font-size: 11px;
  font-family: monospace;
  color: #888;
  background: rgba(128, 128, 128, 0.12);
  padding: 1px 5px;
  border-radius: 4px;
}
.child-widget-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 10px;
  background: rgba(32, 128, 240, 0.08);
  border: 1px solid rgba(32, 128, 240, 0.25);
  border-radius: 6px;
}
.cw-info {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.cw-label {
  font-size: 11px;
  font-weight: 600;
  color: #2080f0;
}
.cw-path {
  font-size: 11px;
  color: #666;
  word-break: break-all;
}
.inspector-section {
  border-top: 1px solid rgba(128, 128, 128, 0.15);
  padding-top: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.highlight-edit-section {
  background: rgba(24, 160, 88, 0.04);
  border: 1px solid rgba(24, 160, 88, 0.2);
  border-radius: 6px;
  padding: 10px;
}
.section-header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.section-title {
  font-size: 12px;
  font-weight: 600;
  color: #666;
}
.edit-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
}
.edit-label {
  width: 68px;
  color: #666;
  font-size: 12px;
  flex-shrink: 0;
}
.save-actions-row {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 4px;
}
.kv-grid {
  display: grid;
  grid-template-columns: 115px 1fr;
  gap: 4px 8px;
  font-size: 12px;
}
.k {
  color: #888;
}
.v {
  font-family: monospace;
  word-break: break-all;
}
.structured-box,
.raw-slot-box {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 11px;
  background: rgba(128, 128, 128, 0.06);
  padding: 6px 8px;
  border-radius: 4px;
}
.struct-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}
.sk {
  color: #777;
}
.sv {
  font-family: monospace;
  color: #333;
}
.color-swatch-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 12px;
}
.swatch-val {
  display: flex;
  align-items: center;
  gap: 6px;
}
.color-box {
  width: 14px;
  height: 14px;
  border-radius: 3px;
  border: 1px solid rgba(0, 0, 0, 0.25);
  display: inline-block;
}
.kv-row {
  display: flex;
  flex-direction: column;
}
.resource-card-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.resource-card {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px;
  border-radius: 6px;
  background: rgba(128, 128, 128, 0.08);
  border: 1px solid rgba(128, 128, 128, 0.15);
  transition: all 0.2s ease;
  cursor: pointer;
}
.resource-card:hover {
  background: rgba(128, 128, 128, 0.16);
  border-color: rgba(64, 158, 255, 0.5);
}
.card-thumb {
  width: 48px;
  height: 48px;
  border-radius: 4px;
  overflow: hidden;
  background: #111;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  border: 1px solid rgba(255, 255, 255, 0.1);
}
.card-type-icon {
  width: 48px;
  height: 48px;
  border-radius: 4px;
  background: rgba(128, 128, 128, 0.1);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.type-badge {
  font-size: 11px;
  font-weight: 700;
  color: #888;
  text-transform: uppercase;
}
.card-info {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  flex: 1;
}
.card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.role-hint {
  font-size: 10px;
  color: #999;
}
.card-path {
  font-size: 11px;
  color: #2080f0;
  word-break: break-all;
  line-height: 1.3;
}
.cap-tags {
  display: flex;
  gap: 8px;
}
.issue-list {
  margin: 4px 0 0;
  padding-left: 18px;
  font-size: 12px;
  color: #f0a020;
}
.muted-note {
  font-size: 12px;
  color: #888;
}
</style>
