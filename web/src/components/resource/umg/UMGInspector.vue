<script setup lang="ts">
import { tr } from "../../../i18n"
import { NTag, NButton, NCollapse, NCollapseItem } from 'naive-ui'
import type { UMGNode, ComputedLayout } from './types'

const props = defineProps<{
  node: UMGNode | null
  layout: ComputedLayout | null
}>()

const emit = defineEmits<{
  'navigate-path': [path: string]
  'preview-texture': [path: string]
}>()
</script>

<template>
  <div class="umg-inspector">
    <div v-if="!node" class="empty-inspector">
      <span>在左侧树或画面中点击组件查看详情</span>
    </div>

    <div v-else class="inspector-content">
      <div class="node-title-bar">
        <h4>{{ node.name }}</h4>
        <n-tag size="small" type="info" :bordered="false">{{ node.className }}</n-tag>
      </div>

      <!-- 1. 计算布局与 Slot -->
      <div class="inspector-section">
        <div class="section-title">布局与 Slot ({{ node.slot?.className || 'Root' }})</div>
        <div v-if="layout" class="kv-grid">
          <span class="k">计算位置 (X, Y)</span>
          <span class="v">{{ Math.round(layout.rect.x) }}, {{ Math.round(layout.rect.y) }}</span>
          <span class="k">计算尺寸 (W × H)</span>
          <span class="v">{{ Math.round(layout.rect.width) }} × {{ Math.round(layout.rect.height) }}</span>
        </div>
        <div v-if="node.slot?.values" class="raw-slot-box">
          <div v-for="(val, key) in node.slot.values" :key="key" class="kv-row">
            <span class="k">{{ key }}:</span>
            <code class="v">{{ JSON.stringify(val) }}</code>
          </div>
        </div>
      </div>

      <!-- 2. 外观与内容 -->
      <div class="inspector-section">
        <div class="section-title">外观与可见性</div>
        <div class="kv-grid">
          <span class="k">Visibility</span>
          <span class="v">{{ node.visual?.Visibility || tr('ui.umgUnknownVisible') }}</span>
          <span class="k">RenderOpacity</span>
          <span class="v">{{ node.visual?.RenderOpacity ?? tr('ui.umgUnknownOpacity') }}</span>
          <template v-if="node.visual?.Text">
            <span class="k">Text</span>
            <span class="v">{{ node.visual.Text }}</span>
          </template>
        </div>
      </div>

      <!-- 3. 关联外部资源 -->
      <div class="inspector-section">
        <div class="section-title">关联资源 ({{ node.resources?.length || 0 }})</div>
        <div v-if="node.resources?.length" class="resource-list">
          <div v-for="r in node.resources" :key="r.assetPath" class="resource-item">
            <div class="res-meta">
              <n-tag size="tiny" :bordered="false">{{ r.className || r.role }}</n-tag>
              <span class="res-path" @click="emit('navigate-path', r.assetPath)">{{ r.assetPath }}</span>
            </div>
            <n-button
              v-if="r.className === 'Texture2D'"
              size="tiny"
              secondary
              @click="emit('preview-texture', r.assetPath)"
            >
              预览
            </n-button>
          </div>
        </div>
        <span v-else class="muted-note">无直接绑定的外部资源</span>
      </div>

      <!-- 4. 预览支持诊断 -->
      <div class="inspector-section">
        <div class="section-title">预览能力诊断</div>
        <div class="cap-tags">
          <n-tag size="small" :type="node.preview.layout === 'supported' ? 'success' : 'warning'">
            布局: {{ node.preview.layout }}
          </n-tag>
          <n-tag size="small" :type="node.preview.appearance === 'supported' ? 'success' : 'warning'">
            外观: {{ node.preview.appearance }}
          </n-tag>
        </div>
        <ul v-if="layout?.issues.length" class="issue-list"><li v-for="issue in layout.issues" :key="issue">{{ issue }}</li></ul>
        <ul v-if="node.preview.issues?.length" class="issue-list">
          <li v-for="(iss, idx) in node.preview.issues" :key="idx">{{ iss }}</li>
        </ul>
      </div>

      <n-collapse>
        <n-collapse-item title="高级诊断信息" name="raw">
          <div class="kv-grid">
            <span class="k">Export Index</span>
            <span class="v">#{{ node.exportIndex }}</span>
            <span class="k">Node ID</span>
            <span class="v">{{ node.id }}</span>
          </div>
        </n-collapse-item>
      </n-collapse>
    </div>
  </div>
</template>

<style scoped>
.umg-inspector {
  width: 320px;
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
  gap: 14px;
}
.node-title-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.node-title-bar h4 {
  margin: 0;
  font-size: 14px;
  word-break: break-all;
}
.inspector-section {
  border-top: 1px solid rgba(128, 128, 128, 0.15);
  padding-top: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.section-title {
  font-size: 12px;
  font-weight: 600;
  color: #888;
}
.kv-grid {
  display: grid;
  grid-template-columns: 110px 1fr;
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
.raw-slot-box {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 11px;
  background: rgba(128, 128, 128, 0.06);
  padding: 6px;
  border-radius: 4px;
}
.kv-row {
  display: flex;
  flex-direction: column;
}
.resource-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.resource-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
  font-size: 12px;
}
.res-meta {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
.res-path {
  color: #2080f0;
  cursor: pointer;
  text-decoration: underline;
  word-break: break-all;
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
