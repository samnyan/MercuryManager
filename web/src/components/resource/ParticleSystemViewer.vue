<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import {
  NTag,
  NButton,
  NButtonGroup,
  NIcon,
  NSelect,
  NCollapse,
  NCollapseItem,
  NTooltip
} from 'naive-ui'
import {
  Play,
  Pause,
  RotateCcw,
  Eye,
  EyeOff,
  ExternalLink,
  Sparkles
} from '@lucide/vue'
import ResourceCardList, { type ResourceItem } from './ResourceCardList.vue'
import ParticleCutoutCanvas from './ParticleCutoutCanvas.vue'
import { materialPrograms } from '../../utils/material-runtime/MaterialProgramCache'
import {
  hasNonSlateParticleDiagnostics,
  type NonSlateParticleDiagnostics,
  type ParticleProgramEvidence
} from '../../utils/material-runtime/ParticleProgramDiagnostics'
import { tr } from '../../i18n'
import { particleVertexFactory, selectEmitterProgram } from '../../utils/material-runtime/ParticleEmitterProgram'
import { loadParticleMesh, meshOriginRadius, type ParticleMeshGeometry } from '../../utils/particle-system/ParticleMeshGeometry'
import { fitParticleCamera } from '../../utils/particle-system/ParticleCamera'
import { useProject } from '../../project'
import {
  type ParticleModuleInfo,
  type ParticleSystemData,
  ParticleSimulationEngine,
  buildParticleMaterialSprites
} from '../../utils/particle-system/ParticleRuntime'

const props = defineProps<{
  info: ParticleSystemData
  path: string
}>()

const emit = defineEmits<{
  'navigate-path': [path: string]
  'preview-texture': [path: string, label: string]
}>()

const project = useProject()

// 选中的发射器或模块
const selectedEmitterIdx = ref<number>(0)
const selectedModule = ref<ParticleModuleInfo | null>(null)
const enabledEmitters = ref<Set<number>>(new Set())
const soloEmitterIdx = ref<number | null>(null)

// 视口与模拟状态
const showViewport = ref(true)
const isPlaying = ref(true)
const simSpeed = ref(1.0)
const simTime = ref(0)
const activeParticleCount = ref(0)
const viewportBg = ref<'dark' | 'grid'>('grid')
const enableMaterialSprites = ref(true)

const speedOptions = [
  { label: '0.25x', value: 0.25 },
  { label: '0.5x', value: 0.5 },
  { label: '1.0x', value: 1.0 },
  { label: '2.0x', value: 2.0 }
]

const totalModulesCount = computed(() => {
  let count = 0
  for (const em of props.info.Emitters || []) {
    const lod = em.LODLevels?.[0]
    if (!lod) continue
    if (lod.RequiredModule) count++
    if (lod.SpawnModule) count++
    if (lod.TypeDataModule) count++
    count += lod.Modules?.length || 0
  }
  return count
})

const totalPeakParticles = computed(() => {
  return (props.info.Emitters || []).reduce((acc, em) => acc + (em.PeakActiveParticles || 0), 0)
})

const selectedModuleResources = computed<ResourceItem[]>(() => {
  if (!selectedModule.value?.References) return []
  const list: ResourceItem[] = []
  for (const [rKey, refItem] of Object.entries(selectedModule.value.References)) {
    if (!refItem || !refItem.ContentFile) continue
    list.push({
      assetPath: refItem.ContentFile,
      role: rKey,
      className: refItem.Class || (rKey === 'Material' ? 'Material' : rKey === 'Mesh' ? 'StaticMesh' : undefined),
      objectName: refItem.ObjectName
    })
  }
  return list
})

// 材质 Shader 诊断缓存与状态
const materialDiagnosticsMap = ref<Record<string, NonSlateParticleDiagnostics | null>>({})
const materialLimitations = ref<Record<string, string[]>>({})
const activeMaterialPath = computed(() => {
  return selectedModuleResources.value.find(r => r.role === 'Material')?.assetPath || null
})
const emitterTypeData = computed(() => props.info.Emitters?.[selectedEmitterIdx.value]?.LODLevels?.[0]?.TypeDataModule?.Class)
const expectedFactory = computed(() => particleVertexFactory(emitterTypeData.value))
const emitterProgram = computed(() => selectEmitterProgram(currentMaterialDiagnostics.value?.particlePrograms ?? [], emitterTypeData.value))
const currentMaterialDiagnostics = computed(() => {
  const p = activeMaterialPath.value
  return p ? materialDiagnosticsMap.value[p] || null : null
})

function formatFogBinding(f: NonNullable<ParticleProgramEvidence['fogBindings']>[number]): string {
  const stage = f.stage === 'vertex' ? 'VS' : 'PS'
  const parts: string[] = []
  if (f.bindings.inscatteringCubemap?.numResources > 0) {
    parts.push(`cube t${f.bindings.inscatteringCubemap.baseIndex}`)
  }
  if (f.bindings.integratedLightScattering?.numResources > 0) {
    parts.push(`vol t${f.bindings.integratedLightScattering.baseIndex}`)
  }
  const cCount = f.bindings.constants?.length ?? 0
  if (cCount > 0) {
    parts.push(`${cCount} consts`)
  }
  return `${stage} (${parts.join(', ') || 'unbound'})`
}

// 按需单次异步请求材质诊断（去重且避免模拟每 tick 轮询）
watch(
  [() => project.id, activeMaterialPath],
  async ([projId, matPath], _previous, onCleanup) => {
    let cancelled = false
    onCleanup(() => { cancelled = true })
    if (!projId || !matPath || materialDiagnosticsMap.value[matPath] !== undefined) return
    try {
      const data = await materialPrograms.load(projId, matPath)
      if (cancelled) return
      materialLimitations.value[matPath] = data.limitations ?? []
      if (hasNonSlateParticleDiagnostics(data)) {
        materialDiagnosticsMap.value[matPath] = data as NonSlateParticleDiagnostics
      } else {
        materialDiagnosticsMap.value[matPath] = null
      }
    } catch {
      if (!cancelled) materialDiagnosticsMap.value[matPath] = null
    }
  },
  { immediate: true }
)

// 材质贴图加载辅助
const rawImageCache = new Map<string, Promise<HTMLImageElement | null>>()
function loadRawImage(texPath: string): Promise<HTMLImageElement | null> {
  if (!project.id || !texPath) return Promise.resolve(null)
  const url = `/api/projects/${project.id}/resource-image?` + new URLSearchParams({ path: texPath, mip: '0' })
  const cached = rawImageCache.get(url)
  if (cached) return cached
  const p = new Promise<HTMLImageElement | null>(resolve => {
    const img = new Image()
    img.crossOrigin = 'anonymous'
    img.onload = () => resolve(img)
    img.onerror = () => resolve(null)
    img.src = url
  })
  rawImageCache.set(url, p)
  return p
}

// 模拟引擎实例
let simEngine: ParticleSimulationEngine | null = null
let meshAbort: AbortController | null = null
const emitterMeshes = new Map<number, ParticleMeshGeometry>()
const emitterMeshRadii = new Map<number, number>()
const meshLoadErrors = ref<Record<number, string>>({})

function initSelection() {
  const emitters = props.info.Emitters || []
  enabledEmitters.value = new Set(emitters.map((_, idx) => idx))
  soloEmitterIdx.value = null
  if (emitters.length > 0) {
    selectedEmitterIdx.value = 0
    const lod0 = emitters[0].LODLevels?.[0]
    selectedModule.value = lod0?.RequiredModule || lod0?.SpawnModule || lod0?.Modules?.[0] || null
  } else {
    selectedModule.value = null
  }

  meshAbort?.abort()
  meshAbort = new AbortController()
  const signal = meshAbort.signal
  emitterMeshes.clear()
  emitterMeshRadii.clear()
  meshLoadErrors.value = {}
  const requests = new Map<string, Promise<ParticleMeshGeometry>>()
  if (project.id) emitters.forEach((emitter, index) => {
    const type = emitter.LODLevels?.[0]?.TypeDataModule
    const path = type?.References?.Mesh?.ContentFile
    if (type?.Class !== 'ParticleModuleTypeDataMesh' || !path) return
    let request = requests.get(path)
    if (!request) {
      request = loadParticleMesh(project.id!, path, signal)
      requests.set(path, request)
    }
    request.then(mesh => {
      if (signal.aborted) return
      emitterMeshes.set(index, mesh)
      emitterMeshRadii.set(index, meshOriginRadius(mesh))
      needsCameraFit = true
    }).catch(error => {
      if (!signal.aborted) meshLoadErrors.value[index] = `${path}: ${error instanceof Error ? error.message : String(error)}`
    })
  })
  simEngine = new ParticleSimulationEngine(props.info)
  const engine = simEngine
  buildParticleMaterialSprites(props.info, loadRawImage, undefined, project.id).then(sprites => {
    if (!signal.aborted && simEngine === engine) {
      engine.setMaterialSprites(sprites)
    }
  })
}

function selectModule(emitterIdx: number, mod: ParticleModuleInfo) {
  selectedEmitterIdx.value = emitterIdx
  selectedModule.value = mod
}

function isEmitterActive(idx: number): boolean {
  if (soloEmitterIdx.value !== null) return soloEmitterIdx.value === idx
  return enabledEmitters.value.has(idx)
}

function toggleEmitterEnable(idx: number) {
  const next = new Set(enabledEmitters.value)
  if (next.has(idx)) next.delete(idx)
  else next.add(idx)
  enabledEmitters.value = next
}

function toggleSoloEmitter(idx: number) {
  if (soloEmitterIdx.value === idx) soloEmitterIdx.value = null
  else soloEmitterIdx.value = idx
}

function getModuleDisplayTitle(mod: ParticleModuleInfo): string {
  const cls = mod.Class || ''
  if (cls === 'ParticleModuleRequired') return 'Required'
  if (cls === 'ParticleModuleSpawn') return 'Spawn'
  if (cls.startsWith('ParticleModuleTypeData')) {
    return 'TypeData ' + cls.replace('ParticleModuleTypeData', '')
  }
  return cls.replace(/^ParticleModule/, '')
}

function getCategoryColor(cat: string): string {
  switch (cat) {
    case 'Required':
      return '#f59e0b'
    case 'Spawn':
      return '#ef4444'
    case 'TypeData':
      return '#8b5cf6'
    case 'Lifetime':
      return '#10b981'
    case 'Size':
      return '#06b6d4'
    case 'Velocity':
      return '#3b82f6'
    case 'Color':
      return '#ec4899'
    case 'Location':
      return '#14b8a6'
    case 'Rotation':
      return '#a855f7'
    case 'Acceleration':
      return '#6366f1'
    default:
      return '#64748b'
  }
}

function getModuleColorSwatch(mod: ParticleModuleInfo): string | null {
  if (mod.Category !== 'Color') return null
  const sc = mod.Distributions?.StartColor || mod.Distributions?.ColorOverLife
  if (!sc) return null
  const vec = sc.MaxVector || sc.MinVector
  if (!Array.isArray(vec) || vec.length < 3) return null
  const r = Math.round(Math.min(1, Math.max(0, vec[0])) * 255)
  const g = Math.round(Math.min(1, Math.max(0, vec[1])) * 255)
  const b = Math.round(Math.min(1, Math.max(0, vec[2])) * 255)
  const sa = mod.Distributions?.StartAlpha || mod.Distributions?.AlphaOverLife
  const a = sa?.MaxValue ?? sa?.MinValue ?? 1
  return `rgba(${r}, ${g}, ${b}, ${Math.min(1, Math.max(0.15, a))})`
}

function resetSimulation() {
  if (simEngine) {
    simEngine.reset(isEmitterActive)
    needsCameraFit = true
    simTime.value = 0
    activeParticleCount.value = simEngine.particles.length
  }
}

// ==================== 3D 粒子渲染视口 ====================
const viewportPaneRef = ref<HTMLDivElement | null>(null)
const canvasRef = ref<HTMLCanvasElement | null>(null)
let animFrameId: number | null = null
let lastTimestamp = 0
let resizeObserver: ResizeObserver | null = null

function updateCanvasSize() {
  if (!viewportPaneRef.value || !canvasRef.value) return
  const rect = viewportPaneRef.value.getBoundingClientRect()
  const w = Math.max(100, Math.floor(rect.width))
  const h = Math.max(100, Math.floor(rect.height))
  if (canvasRef.value.width !== w || canvasRef.value.height !== h) {
    canvasRef.value.width = w
    canvasRef.value.height = h
  }
}

const camYaw = ref(0.6)
const camPitch = ref(0.35)
const camDistance = ref(1800)
const camTarget = ref<[number, number, number]>([0, 0, 0])
let needsCameraFit = true
function fitViewport() {
  const fit = fitParticleCamera(simEngine?.particles.filter(p => isEmitterActive(p.emitterIdx)) ?? [], p => emitterMeshRadii.get(p.emitterIdx))
  if (!fit) return
  camTarget.value = fit.target
  camDistance.value = fit.distance
  needsCameraFit = false
}
const isDraggingCam = ref(false)
let dragStartX = 0
let dragStartY = 0

function tick(ts: number) {
  if (!lastTimestamp) lastTimestamp = ts
  const rawDt = Math.min(0.05, (ts - lastTimestamp) / 1000)
  lastTimestamp = ts

  if (isPlaying.value && showViewport.value && simEngine) {
    simEngine.step(rawDt * simSpeed.value, isEmitterActive)
    simTime.value = simEngine.simTime
    activeParticleCount.value = simEngine.particles.length
  }

  if (needsCameraFit && simEngine?.particles.length) fitViewport()
  if (showViewport.value && canvasRef.value && simEngine) {
    const ctx = canvasRef.value.getContext('2d')
    if (ctx) {
      simEngine.renderToCanvas(ctx, canvasRef.value.width, canvasRef.value.height, {
        camYaw: camYaw.value,
        camPitch: camPitch.value,
        camDistance: camDistance.value,
        camTarget: camTarget.value,
        meshGeometry: emitterMeshes,
        useMaterialSprites: enableMaterialSprites.value,
        selectedEmitterIdx: selectedEmitterIdx.value,
        showGrid: viewportBg.value === 'grid'
      })
    }
  }
  animFrameId = requestAnimationFrame(tick)
}

function onCanvasMouseDown(e: MouseEvent) {
  isDraggingCam.value = true
  dragStartX = e.clientX
  dragStartY = e.clientY
}

function onCanvasMouseMove(e: MouseEvent) {
  if (!isDraggingCam.value) return
  const dx = e.clientX - dragStartX
  const dy = e.clientY - dragStartY
  dragStartX = e.clientX
  dragStartY = e.clientY
  camYaw.value += dx * 0.008
  camPitch.value = Math.max(-1.2, Math.min(1.2, camPitch.value + dy * 0.008))
}

function onCanvasMouseUp() {
  isDraggingCam.value = false
}

function onCanvasWheel(e: WheelEvent) {
  e.preventDefault()
  camDistance.value = Math.max(100, Math.min(1e9, camDistance.value * Math.exp(Math.max(-1, Math.min(1, e.deltaY * 0.001)))))
}

watch(
  [() => project.id, () => props.info, () => props.path],
  () => {
    materialDiagnosticsMap.value = {}
    materialLimitations.value = {}
    rawImageCache.clear()
    initSelection()
    needsCameraFit = true
  },
  { immediate: true }
)

onMounted(() => {
  animFrameId = requestAnimationFrame(tick)
  if (viewportPaneRef.value) {
    resizeObserver = new ResizeObserver(() => {
      updateCanvasSize()
    })
    resizeObserver.observe(viewportPaneRef.value)
    updateCanvasSize()
  }
})

onBeforeUnmount(() => {
  meshAbort?.abort()
  emitterMeshes.clear()
  emitterMeshRadii.clear()
  if (animFrameId !== null) cancelAnimationFrame(animFrameId)
  if (resizeObserver) {
    resizeObserver.disconnect()
    resizeObserver = null
  }
})
</script>

<template>
  <div class="cascade-editor">
    <!-- 顶部 Cascade 工具栏 -->
    <div class="cascade-toolbar">
      <div class="toolbar-left">
        <n-tag type="warning" size="small" :bordered="false">
          <template #icon><n-icon :component="Sparkles" /></template>
          UE Cascade Particle System
        </n-tag>
        <strong class="system-title">{{ info.Name }}</strong>
        <span class="meta-stats">
          发射器: <b>{{ info.Emitters?.length || 0 }}</b> · 模块: <b>{{ totalModulesCount }}</b> ·
          峰值粒子: <b>{{ totalPeakParticles }}</b>
        </span>
      </div>

      <div class="toolbar-right">
        <n-button-group size="small">
          <n-button :type="isPlaying ? 'primary' : 'default'" @click="isPlaying = !isPlaying">
            <template #icon><n-icon :component="isPlaying ? Pause : Play" /></template>
            {{ isPlaying ? '暂停' : '播放' }}
          </n-button>
          <n-button @click="resetSimulation">
            <template #icon><n-icon :component="RotateCcw" /></template>
            重置
          </n-button>
        </n-button-group>

        <n-select
          v-model:value="simSpeed"
          size="small"
          :options="speedOptions"
          style="width: 82px"
        />

        <n-button
          size="small"
          :type="enableMaterialSprites ? 'primary' : 'default'"
          secondary
          @click="enableMaterialSprites = !enableMaterialSprites"
        >
          {{ enableMaterialSprites ? '材质着色: 开启' : '材质着色: 线框模式' }}
        </n-button>

        <n-button
          size="small"
          :type="showViewport ? 'info' : 'default'"
          secondary
          @click="showViewport = !showViewport"
        >
          {{ showViewport ? '隐藏模拟视口' : '显示模拟视口' }}
        </n-button>
      </div>
    </div>

    <!-- 主工作区：左侧 3D 视口 + 中间 Cascade 发射器列堆栈 + 右侧 Details 面板 -->
    <div class="cascade-workspace">
      <!-- 1. 实时粒子模拟视口 -->
      <div v-if="showViewport" ref="viewportPaneRef" class="cascade-viewport-pane">
        <canvas
          ref="canvasRef"
          class="particle-canvas"
          @mousedown="onCanvasMouseDown"
          @mousemove="onCanvasMouseMove"
          @mouseup="onCanvasMouseUp"
          @mouseleave="onCanvasMouseUp"
          @wheel="onCanvasWheel"
        />
        <div class="viewport-hud">
          <div>Time: {{ simTime.toFixed(2) }}s</div>
          <div>Particles: {{ activeParticleCount }}</div>
          <div v-for="(error, index) in meshLoadErrors" :key="index" role="alert" style="color: #f87171; overflow-wrap: anywhere">Mesh {{ index }}: {{ error }}</div>
          <div v-if="info.Emitters.some(em => em.LODLevels?.[0]?.TypeDataModule?.Class === 'ParticleModuleTypeDataMesh')">Mesh: 几何线框预览 · 非原生材质</div>
          <div class="hud-hint">拖拽旋转 3D 视角 · 滚轮缩放</div>
        </div>
      </div>

      <!-- 2. UE Cascade 发射器横向堆栈区 (Emitters Panel) -->
      <div class="cascade-emitters-scroll">
        <div class="emitters-columns">
          <div
            v-for="(em, emIdx) in info.Emitters"
            :key="em.ExportIndex"
            class="emitter-column"
            :class="{
              'is-selected-emitter': selectedEmitterIdx === emIdx,
              'is-muted-emitter': !isEmitterActive(emIdx)
            }"
          >
            <!-- 发射器顶部块 (Emitter Header) -->
            <div
              class="emitter-header"
              :class="`type-${em.EmitterType.toLowerCase()}`"
              @click="
                selectedEmitterIdx = emIdx;
                selectedModule = em.LODLevels?.[0]?.RequiredModule || null
              "
            >
              <div class="em-header-top">
                <span class="em-title" :title="em.EmitterName">{{ em.EmitterName }}</span>
                <div class="em-controls" @click.stop>
                  <n-tooltip trigger="hover">
                    <template #trigger>
                      <button
                        class="mini-icon-btn"
                        :class="{ 'is-active': soloEmitterIdx === emIdx }"
                        @click="toggleSoloEmitter(emIdx)"
                      >
                        S
                      </button>
                    </template>
                    独奏 (Solo) 此发射器
                  </n-tooltip>
                  <n-tooltip trigger="hover">
                    <template #trigger>
                      <button class="mini-icon-btn" @click="toggleEmitterEnable(emIdx)">
                        <n-icon :component="isEmitterActive(emIdx) ? Eye : EyeOff" :size="13" />
                      </button>
                    </template>
                    启用 / 隐藏发射器
                  </n-tooltip>
                </div>
              </div>

              <div class="em-header-sub">
                <n-tag size="tiny" :bordered="false" type="info">{{ em.EmitterType }}</n-tag>
                <span class="em-sub-meta">Peak: {{ em.PeakActiveParticles }}</span>
                <span class="em-sub-meta">#{{ em.ExportIndex }}</span>
              </div>
            </div>

            <!-- 模块纵向堆栈 (Module Stack) -->
            <div v-if="em.LODLevels?.[0]" class="module-stack">
              <!-- Required Module -->
              <div
                v-if="em.LODLevels[0].RequiredModule"
                class="module-card mod-required"
                :class="{
                  'is-selected-mod':
                    selectedModule?.ExportIndex === em.LODLevels[0].RequiredModule.ExportIndex
                }"
                :style="{ borderLeftColor: getCategoryColor('Required') }"
                @click="selectModule(emIdx, em.LODLevels[0].RequiredModule!)"
              >
                <div class="mod-row">
                  <span class="mod-name">Required</span>
                  <span class="mod-idx">#{{ em.LODLevels[0].RequiredModule.ExportIndex }}</span>
                </div>
                <div v-if="em.LODLevels[0].RequiredModule.Summary" class="mod-summary">
                  Mat: {{ em.LODLevels[0].RequiredModule.Summary }}
                </div>
              </div>

              <!-- Spawn Module -->
              <div
                v-if="em.LODLevels[0].SpawnModule"
                class="module-card mod-spawn"
                :class="{
                  'is-selected-mod':
                    selectedModule?.ExportIndex === em.LODLevels[0].SpawnModule.ExportIndex
                }"
                :style="{ borderLeftColor: getCategoryColor('Spawn') }"
                @click="selectModule(emIdx, em.LODLevels[0].SpawnModule!)"
              >
                <div class="mod-row">
                  <span class="mod-name">Spawn</span>
                  <span class="mod-idx">#{{ em.LODLevels[0].SpawnModule.ExportIndex }}</span>
                </div>
                <div v-if="em.LODLevels[0].SpawnModule.Summary" class="mod-summary">
                  {{ em.LODLevels[0].SpawnModule.Summary }}
                </div>
              </div>

              <!-- TypeData Module (e.g. Mesh / GPU) -->
              <div
                v-if="em.LODLevels[0].TypeDataModule"
                class="module-card mod-typedata"
                :class="{
                  'is-selected-mod':
                    selectedModule?.ExportIndex === em.LODLevels[0].TypeDataModule.ExportIndex
                }"
                :style="{ borderLeftColor: getCategoryColor('TypeData') }"
                @click="selectModule(emIdx, em.LODLevels[0].TypeDataModule!)"
              >
                <div class="mod-row">
                  <span class="mod-name">
                    {{ getModuleDisplayTitle(em.LODLevels[0].TypeDataModule) }}
                  </span>
                  <span class="mod-idx">#{{ em.LODLevels[0].TypeDataModule.ExportIndex }}</span>
                </div>
                <div v-if="em.LODLevels[0].TypeDataModule.Summary" class="mod-summary">
                  Mesh: {{ em.LODLevels[0].TypeDataModule.Summary }}
                </div>
              </div>

              <!-- Standard Modules List -->
              <div
                v-for="mod in em.LODLevels[0].Modules"
                :key="mod.ExportIndex"
                class="module-card"
                :class="{ 'is-selected-mod': selectedModule?.ExportIndex === mod.ExportIndex }"
                :style="{ borderLeftColor: getCategoryColor(mod.Category) }"
                @click="selectModule(emIdx, mod)"
              >
                <div class="mod-row">
                  <span class="mod-name">{{ getModuleDisplayTitle(mod) }}</span>
                  <div class="mod-right-badges">
                    <span
                      v-if="getModuleColorSwatch(mod)"
                      class="mod-color-dot"
                      :style="{ backgroundColor: getModuleColorSwatch(mod)! }"
                    />
                    <span class="mod-idx">#{{ mod.ExportIndex }}</span>
                  </div>
                </div>
                <div v-if="mod.Summary" class="mod-summary">{{ mod.Summary }}</div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 3. 右侧 UE Cascade Details 属性检查器 -->
      <div class="cascade-details-pane">
        <div v-if="!selectedModule" class="empty-details">
          点击任意发射器模块查看详细参数与分布曲线
        </div>

        <div v-else class="details-content">
          <div class="details-header">
            <div>
              <h4 class="details-title">{{ selectedModule.Name }}</h4>
              <span class="details-sub">{{ selectedModule.Class }}</span>
            </div>
            <n-tag size="small" type="info" :bordered="false">
              Export #{{ selectedModule.ExportIndex }}
            </n-tag>
          </div>

          <!-- 关联的外部引用 (如 Material / StaticMesh) -->
          <div
            v-if="selectedModuleResources.length > 0"
            class="details-section"
          >
            <ResourceCardList
              title="关联引用资产"
              :resources="selectedModuleResources"
              :materials="info.Materials"
              @navigate-path="emit('navigate-path', $event)"
              @preview-texture="(p, l) => emit('preview-texture', p, l || 'Texture')"
            />
          </div>

          <!-- Cutout 几何轮廓诊断 (仅在 RequiredModule 且具备 CutoutConfiguration 时展示) -->
          <div
            v-if="selectedModule.Class === 'ParticleModuleRequired' && selectedModule.CutoutConfiguration"
            class="details-section"
          >
            <ParticleCutoutCanvas :cutout="selectedModule.CutoutConfiguration" />
          </div>

          <div v-if="activeMaterialPath && materialLimitations[activeMaterialPath]?.length" class="details-section" role="status">
            <div class="sec-title">{{ tr('ui.particleUnverified') }}</div>
            <div v-for="reason in materialLimitations[activeMaterialPath]" :key="reason" style="overflow-wrap: anywhere">{{ reason }}</div>
          </div>

          <!-- 材质 Shader 顶点工厂与底层诊断 (Low-Level Evidence) -->
          <div
            v-if="currentMaterialDiagnostics?.particlePrograms?.length"
            class="details-section"
          >
            <div class="sec-title">{{ tr('ui.particleDiagnosticsTitle') }}</div>
            <div class="diag-container">
              <div class="diag-desc">{{ tr('ui.particleDiagnosticsDesc') }}</div>
              <div class="diag-factory-name">{{ expectedFactory || emitterTypeData || 'unsupported' }}</div>
              <div v-if="!emitterProgram" class="diag-desc">{{ tr('ui.particleUnverified') }}</div>
              <div
                v-for="(prog, pIdx) in currentMaterialDiagnostics.particlePrograms"
                :key="pIdx"
                class="diag-card"
              >
                <div class="diag-row-top">
                  <span class="diag-factory-name">{{ prog.vertexFactory }}</span>
                  <n-tag
                    size="tiny"
                    :type="prog.identityVerified ? 'success' : 'warning'"
                    :bordered="false"
                  >
                    {{ prog.identityVerified ? tr('ui.particleVerified') : tr('ui.particleUnverified') }}
                  </n-tag>
                </div>

                <div class="diag-meta-grid">
                  <span class="meta-k">{{ tr('ui.particleOffsets') }}:</span>
                  <span class="meta-v">VS @ {{ prog.vertexOffset ?? 'N/A' }} / PS @ {{ prog.fragmentOffset ?? 'N/A' }}</span>

                  <template v-if="prog.spriteBindings">
                    <span class="meta-k">{{ tr('ui.particleSpriteBindings') }}:</span>
                    <span class="meta-v">
                      cb{{ prog.spriteBindings.numCutoutVerticesPerFrame.bufferIndex }}[{{ prog.spriteBindings.numCutoutVerticesPerFrame.baseIndex }}],
                      res: {{ prog.spriteBindings.cutoutGeometry.numResources }} @ t{{ prog.spriteBindings.cutoutGeometry.baseIndex }}
                    </span>
                  </template>

                  <template v-if="prog.fogBindings?.length">
                    <span class="meta-k">{{ tr('ui.particleFogBindings') }}:</span>
                    <span class="meta-v">
                      {{ prog.fogBindings.map(formatFogBinding).join(' | ') }}
                    </span>
                  </template>
                </div>

                <div v-if="prog.limitations?.length" class="diag-limits">
                  <div class="limit-title">{{ tr('ui.particleLimitations') }}:</div>
                  <ul class="limit-list">
                    <li v-for="(lim, lIdx) in prog.limitations" :key="lIdx">{{ lim }}</li>
                  </ul>
                </div>
              </div>
            </div>
          </div>

          <!-- 结构化属性与分布 (Properties & Distributions) -->
          <div class="details-section">
            <div class="sec-title">
              模块参数 (Module Properties · {{ selectedModule.Properties?.length || 0 }})
            </div>

            <div v-if="selectedModule.Properties?.length" class="prop-list">
              <div
                v-for="prop in selectedModule.Properties"
                :key="prop.Name"
                class="prop-item"
              >
                <div class="prop-header-row">
                  <span class="prop-name">{{ prop.Name }}</span>
                  <n-tag size="tiny" :bordered="false">{{ prop.Kind }}</n-tag>
                </div>

                <!-- 若是对象引用 -->
                <div v-if="prop.Kind === 'Object'" class="prop-val-box">
                  <code class="prop-code">{{ prop.Value }}</code>
                  <a
                    v-if="prop.ContentFile"
                    class="prop-path-link"
                    href="javascript:void(0)"
                    @click="emit('navigate-path', prop.ContentFile)"
                  >
                    {{ prop.ContentFile }}
                  </a>
                </div>

                <!-- 若是 BurstList -->
                <div v-else-if="prop.Kind === 'BurstList' && prop.Bursts?.length" class="burst-table">
                  <div v-for="(b, bIdx) in prop.Bursts" :key="bIdx" class="burst-row">
                    <span class="burst-tag">Burst #{{ bIdx }}</span>
                    <span class="burst-meta">
                      Count: <b>{{ b.Count }}</b>{{ b.CountLow >= 0 ? ` ~ ${b.CountLow}` : '' }} @ <b>{{ b.Time }}s</b>
                    </span>
                  </div>
                </div>

                <!-- 若是分布曲线/范围 (Distribution) -->
                <div v-else-if="prop.Distribution" class="dist-box">
                  <div class="dist-row">
                    <span class="dist-label">取值范围:</span>
                    <code class="dist-val">{{ prop.Distribution.Summary }}</code>
                  </div>
                  <div
                    v-if="
                      prop.Distribution.TableValues &&
                      prop.Distribution.TableValues.length > 1
                    "
                    class="dist-table-box"
                  >
                    <span class="dist-label">
                      LookupTable ({{ prop.Distribution.TableValues.length }}):
                    </span>
                    <code class="dist-table-code">
                      [{{
                        prop.Distribution.TableValues.slice(0, 8)
                          .map((v: number) => Number(v.toFixed(3)))
                          .join(', ')
                      }}{{ prop.Distribution.TableValues.length > 8 ? ', ...' : '' }}]
                    </code>
                  </div>
                </div>

                <!-- 普通标量/枚举/布尔/向量 -->
                <div v-else class="prop-val-box">
                  <code class="prop-code">{{ prop.Value }}</code>
                </div>
              </div>
            </div>
            <div v-else class="muted-text">此模块使用引擎默认参数</div>
          </div>

          <!-- 底层序列化属性折叠面板 -->
          <n-collapse>
            <n-collapse-item title="底层 UAsset 序列化结构 (Raw Properties)" name="raw">
              <pre class="raw-json-pre">{{
                JSON.stringify(selectedModule.RawProperties || {}, null, 2)
              }}</pre>
            </n-collapse-item>
          </n-collapse>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.cascade-editor {
  display: flex;
  flex-direction: column;
  height: 100%;
  width: 100%;
  overflow: hidden;
  background: var(--n-color);
}

.cascade-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 14px;
  border-bottom: 1px solid rgba(128, 128, 128, 0.2);
  background: rgba(128, 128, 128, 0.04);
  flex-shrink: 0;
  gap: 12px;
  flex-wrap: wrap;
}

.toolbar-left,
.toolbar-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

.system-title {
  font-size: 14px;
}

.meta-stats {
  font-size: 12px;
  color: #888;
}

.cascade-workspace {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

/* 1. 左侧 3D 模拟视口 */
.cascade-viewport-pane {
  position: relative;
  width: 440px;
  min-width: 280px;
  max-width: 50vw;
  flex-shrink: 0;
  border-right: 1px solid rgba(128, 128, 128, 0.2);
  background: #0b0e14;
  display: flex;
  align-items: stretch;
  justify-content: stretch;
  overflow: hidden;
}

.particle-canvas {
  width: 100%;
  height: 100%;
  cursor: grab;
  display: block;
}

.particle-canvas:active {
  cursor: grabbing;
}

.viewport-hud {
  position: absolute;
  top: 10px;
  left: 12px;
  font-family: monospace;
  font-size: 11px;
  color: rgba(255, 255, 255, 0.78);
  background: rgba(0, 0, 0, 0.55);
  padding: 6px 10px;
  border-radius: 6px;
  pointer-events: none;
  line-height: 1.5;
}

.hud-hint {
  color: rgba(255, 255, 255, 0.45);
  font-size: 10px;
  margin-top: 2px;
}

/* 2. 中间 Cascade 发射器列横向滚动区 */
.cascade-emitters-scroll {
  flex: 1;
  min-width: 0;
  overflow: auto;
  padding: 12px;
  background: rgba(0, 0, 0, 0.03);
}

.emitters-columns {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  min-height: 100%;
}

.emitter-column {
  width: 230px;
  flex-shrink: 0;
  border-radius: 8px;
  background: rgba(128, 128, 128, 0.06);
  border: 1px solid rgba(128, 128, 128, 0.18);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  transition: all 0.15s ease;
}

.emitter-column.is-selected-emitter {
  border-color: rgba(32, 128, 240, 0.65);
  box-shadow: 0 0 0 1px rgba(32, 128, 240, 0.25);
}

.emitter-column.is-muted-emitter {
  opacity: 0.5;
}

.emitter-header {
  padding: 10px;
  background: linear-gradient(135deg, rgba(59, 130, 246, 0.18), rgba(30, 64, 175, 0.08));
  border-bottom: 1px solid rgba(128, 128, 128, 0.18);
  cursor: pointer;
}

.emitter-header.type-mesh {
  background: linear-gradient(135deg, rgba(139, 92, 246, 0.22), rgba(109, 40, 217, 0.08));
}

.em-header-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
}

.em-title {
  font-weight: 700;
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.em-controls {
  display: flex;
  gap: 4px;
}

.mini-icon-btn {
  border: 1px solid rgba(128, 128, 128, 0.3);
  background: rgba(255, 255, 255, 0.06);
  color: inherit;
  border-radius: 4px;
  width: 22px;
  height: 22px;
  font-size: 11px;
  font-weight: 700;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  padding: 0;
}

.mini-icon-btn.is-active {
  background: #f59e0b;
  color: #fff;
  border-color: #f59e0b;
}

.em-header-sub {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 6px;
  font-size: 11px;
  color: #888;
}

.module-stack {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px;
}

.module-card {
  padding: 7px 9px;
  border-radius: 5px;
  background: var(--n-card-color, rgba(255, 255, 255, 0.04));
  border: 1px solid rgba(128, 128, 128, 0.16);
  border-left: 4px solid #64748b;
  cursor: pointer;
  transition: all 0.12s ease;
}

.module-card:hover {
  background: rgba(32, 128, 240, 0.08);
}

.module-card.is-selected-mod {
  background: rgba(32, 128, 240, 0.14);
  border-color: rgba(32, 128, 240, 0.6);
}

.mod-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
}

.mod-name {
  font-size: 12px;
  font-weight: 600;
}

.mod-right-badges {
  display: flex;
  align-items: center;
  gap: 5px;
}

.mod-color-dot {
  width: 11px;
  height: 11px;
  border-radius: 50%;
  border: 1px solid rgba(255, 255, 255, 0.4);
  display: inline-block;
}

.mod-idx {
  font-size: 10px;
  font-family: monospace;
  color: #888;
}

.mod-summary {
  font-size: 11px;
  color: #888;
  margin-top: 3px;
  font-family: monospace;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* 3. 右侧 Details 面板 */
.cascade-details-pane {
  width: 380px;
  min-width: 300px;
  flex-shrink: 0;
  border-left: 1px solid rgba(128, 128, 128, 0.2);
  padding: 12px;
  overflow-y: auto;
}

.empty-details {
  color: #888;
  font-size: 12px;
  text-align: center;
  margin-top: 40px;
}

.details-content {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.details-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
}

.details-title {
  margin: 0;
  font-size: 14px;
  word-break: break-all;
}

.details-sub {
  font-size: 11px;
  color: #888;
  font-family: monospace;
}

.details-section {
  border-top: 1px solid rgba(128, 128, 128, 0.16);
  padding-top: 10px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.sec-title {
  font-size: 12px;
  font-weight: 600;
  color: #888;
}

.diag-container {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.diag-desc {
  font-size: 11px;
  color: #71717a;
  line-height: 1.4;
}

.diag-card {
  background: rgba(128, 128, 128, 0.05);
  border: 1px solid rgba(128, 128, 128, 0.16);
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.diag-row-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.diag-factory-name {
  font-family: monospace;
  font-size: 11px;
  font-weight: 600;
  color: #38bdf8;
  word-break: break-all;
}

.diag-meta-grid {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 4px 8px;
  font-size: 11px;
  line-height: 1.4;
}

.meta-k {
  color: #71717a;
}

.meta-v {
  font-family: monospace;
  color: #10b981;
  word-break: break-all;
}

.diag-limits {
  border-top: 1px dashed rgba(128, 128, 128, 0.15);
  padding-top: 4px;
  font-size: 10px;
  color: #eab308;
}

.limit-title {
  font-weight: 600;
  margin-bottom: 2px;
}

.limit-list {
  margin: 0;
  padding-left: 14px;
  list-style-type: disc;
}

.limit-list li {
  line-height: 1.3;
}

.prop-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.prop-item {
  padding: 8px 10px;
  border-radius: 6px;
  background: rgba(128, 128, 128, 0.06);
  border: 1px solid rgba(128, 128, 128, 0.12);
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.prop-header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.prop-name {
  font-size: 12px;
  font-weight: 600;
  word-break: break-all;
}

.prop-val-box {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.prop-code {
  font-family: monospace;
  font-size: 11px;
  word-break: break-word;
  white-space: pre-wrap;
  background: rgba(0, 0, 0, 0.2);
  padding: 3px 6px;
  border-radius: 4px;
  color: #10b981;
}

.prop-path-link {
  font-size: 11px;
  color: #2080f0;
  word-break: break-all;
  line-height: 1.4;
  text-decoration: none;
}

.prop-path-link:hover {
  text-decoration: underline;
}

.dist-box {
  display: flex;
  flex-direction: column;
  gap: 5px;
  font-size: 11px;
}

.dist-row {
  display: flex;
  align-items: baseline;
  gap: 6px;
}

.dist-label {
  color: #888;
  flex-shrink: 0;
}

.dist-val {
  font-family: monospace;
  font-weight: 600;
  color: #10b981;
  word-break: break-all;
}

.dist-table-box {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.dist-table-code {
  font-family: monospace;
  font-size: 10px;
  word-break: break-all;
  background: rgba(0, 0, 0, 0.2);
  padding: 4px 6px;
  border-radius: 4px;
  color: #94a3b8;
}

.burst-table {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 11px;
}

.burst-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
  background: rgba(0, 0, 0, 0.15);
  padding: 3px 6px;
  border-radius: 4px;
}

.burst-tag {
  color: #888;
  font-weight: 500;
}

.burst-meta {
  font-family: monospace;
  color: #10b981;
}

.burst-row {
  display: flex;
  justify-content: space-between;
}

.raw-json-pre {
  margin: 0;
  font-size: 11px;
  max-height: 280px;
  overflow: auto;
  background: rgba(0, 0, 0, 0.08);
  padding: 8px;
  border-radius: 4px;
}

.muted-text {
  font-size: 12px;
  color: #888;
}
</style>
