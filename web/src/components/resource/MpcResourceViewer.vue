<script setup lang="ts">
import { NCard, NTag, NSpace } from 'naive-ui'

export interface MpcInfo {
  Name: string
  ScalarParameters: Array<{
    ParameterName: string
    DefaultValue: number
    Id?: string
  }>
  VectorParameters: Array<{
    ParameterName: string
    R: number
    G: number
    B: number
    A: number
    Id?: string
  }>
}

const props = defineProps<{
  info: MpcInfo
  path: string
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
</script>

<template>
  <div class="mpc-viewer">
    <n-card size="small" class="mpc-card">
      <template #header>
        <div class="mpc-card-header">
          <span>材质参数集 (Material Parameter Collection)</span>
          <n-tag type="info" size="small">{{ info.Name }}</n-tag>
        </div>
      </template>
      <div class="mpc-summary">
        <span>标量参数: <b>{{ info.ScalarParameters?.length || 0 }}</b> 个</span>
        <span>矢量/颜色参数: <b>{{ info.VectorParameters?.length || 0 }}</b> 个</span>
      </div>
    </n-card>

    <div class="mpc-content-row">
      <!-- 矢量颜色参数列表 -->
      <n-card size="small" class="mpc-card col-half">
        <template #header>
          <div class="mpc-card-header">
            <span>矢量 / 颜色参数 (Vector Parameters)</span>
            <n-tag size="small">{{ info.VectorParameters?.length || 0 }} 个</n-tag>
          </div>
        </template>
        <div v-if="!info.VectorParameters?.length" class="empty-hint">无矢量/颜色参数</div>
        <div v-else class="param-list">
          <div v-for="p in info.VectorParameters" :key="p.ParameterName" class="param-row">
            <div class="param-title">
              <div class="color-swatch-box" :style="{ backgroundColor: rgbaStyle(p) }" />
              <span class="p-name">{{ p.ParameterName }}</span>
            </div>
            <div class="param-value">
              <span class="hex-tag">{{ hexColor(p) }}</span>
              <code class="rgba-detail">RGBA({{ p.R.toFixed(2) }}, {{ p.G.toFixed(2) }}, {{ p.B.toFixed(2) }}, {{ p.A.toFixed(2) }})</code>
            </div>
          </div>
        </div>
      </n-card>

      <!-- 标量参数列表 -->
      <n-card size="small" class="mpc-card col-half">
        <template #header>
          <div class="mpc-card-header">
            <span>标量参数 (Scalar Parameters)</span>
            <n-tag size="small">{{ info.ScalarParameters?.length || 0 }} 个</n-tag>
          </div>
        </template>
        <div v-if="!info.ScalarParameters?.length" class="empty-hint">无标量参数</div>
        <div v-else class="param-list">
          <div v-for="p in info.ScalarParameters" :key="p.ParameterName" class="param-row">
            <span class="p-name">{{ p.ParameterName }}</span>
            <code class="scalar-val">{{ p.DefaultValue }}</code>
          </div>
        </div>
      </n-card>
    </div>
  </div>
</template>

<style scoped>
.mpc-viewer {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  overflow-y: auto;
  padding-right: 4px;
}
.mpc-card {
  border-radius: 6px;
}
.mpc-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-weight: 600;
  font-size: 14px;
}
.mpc-summary {
  display: flex;
  gap: 20px;
  font-size: 13px;
  color: #555;
}
.mpc-content-row {
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
  padding: 8px 12px;
  background: #f8f9fa;
  border-radius: 4px;
  font-size: 13px;
  gap: 12px;
}
.param-title {
  display: flex;
  align-items: center;
  gap: 10px;
}
.p-name {
  font-weight: 600;
  color: #222;
}
.color-swatch-box {
  width: 22px;
  height: 22px;
  border-radius: 4px;
  border: 1px solid rgba(0, 0, 0, 0.2);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.1);
}
.param-value {
  display: flex;
  align-items: center;
  gap: 8px;
}
.hex-tag {
  font-family: monospace;
  font-weight: 600;
  font-size: 12px;
  background: #eee;
  padding: 2px 6px;
  border-radius: 3px;
}
.rgba-detail {
  font-size: 11px;
  color: #666;
}
.scalar-val {
  font-family: monospace;
  background: #eef2f6;
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 600;
  color: #1e293b;
}
</style>
