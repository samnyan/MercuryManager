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
import { Save, RotateCcw } from '@lucide/vue'
import { api, useProject } from '../../project'
import { tr } from '../../i18n'

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
</script>

<template>
  <div class="mpc-viewer">
    <n-card size="small" class="mpc-card">
      <div class="mpc-toolbar">
        <div class="mpc-title-left">
          <span class="mpc-title">{{ tr('ui.mpcTitle') }}</span>
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

      <div class="mpc-summary">
        <span>{{ tr('ui.scalarParamsGroup') }}: <b>{{ info.ScalarParameters?.length || 0 }}</b></span>
        <span>{{ tr('ui.vectorParamsGroup') }}: <b>{{ info.VectorParameters?.length || 0 }}</b></span>
      </div>
    </n-card>

    <div class="mpc-content-row">
      <!-- 矢量颜色参数列表 -->
      <n-card size="small" class="mpc-card col-half">
        <template #header>
          <div class="mpc-card-header">
            <span>{{ tr('ui.vectorParamsGroup') }}</span>
            <n-tag size="small">{{ info.VectorParameters?.length || 0 }}</n-tag>
          </div>
        </template>
        <div v-if="!info.VectorParameters?.length" class="empty-hint">{{ tr('ui.noVectorParams') }}</div>
        <div v-else class="param-list">
          <div v-for="p in info.VectorParameters" :key="p.ParameterName" class="param-row">
            <div class="param-title">
              <span class="p-name">{{ p.ParameterName }}</span>
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

      <!-- 标量参数列表 -->
      <n-card size="small" class="mpc-card col-half">
        <template #header>
          <div class="mpc-card-header">
            <span>{{ tr('ui.scalarParamsGroup') }}</span>
            <n-tag size="small">{{ info.ScalarParameters?.length || 0 }}</n-tag>
          </div>
        </template>
        <div v-if="!info.ScalarParameters?.length" class="empty-hint">{{ tr('ui.noScalarParams') }}</div>
        <div v-else class="param-list">
          <div v-for="p in info.ScalarParameters" :key="p.ParameterName" class="param-row">
            <span class="p-name">{{ p.ParameterName }}</span>
            <n-input-number
              v-model:value="editScalars[p.ParameterName]"
              size="small"
              :step="0.01"
              style="width: 140px"
            />
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
.mpc-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 12px;
  border-bottom: 1px solid #f0f0f4;
  padding-bottom: 10px;
}
.mpc-title-left {
  display: flex;
  align-items: center;
  gap: 8px;
}
.mpc-title {
  font-weight: 600;
  font-size: 14px;
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
.param-value {
  display: flex;
  align-items: center;
  gap: 8px;
}
.rgba-detail {
  font-size: 11px;
  color: #666;
}
</style>
