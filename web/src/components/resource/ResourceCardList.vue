<script setup lang="ts">
import { ref, watch } from 'vue'
import { NTag, NImage } from 'naive-ui'
import { useProject } from '../../project'
import { normalizeMaterialEntry, renderUMGMaterialToCanvas } from '../../utils/material-shader/UMGMaterialBridge'

export interface ResourceItem {
  assetPath: string
  className?: string | null
  role?: string | null
  objectName?: string | null
}

const props = withDefaults(
  defineProps<{
    resources: ResourceItem[]
    title?: string
    materials?: Record<string, any>
  }>(),
  {
    title: '关联外部资源',
    materials: undefined
  }
)

const emit = defineEmits<{
  'navigate-path': [path: string]
  'preview-texture': [path: string, label?: string]
}>()

const project = useProject()
const materialThumbnails = ref<Record<string, string>>({})

watch(
  () => [props.resources, props.materials],
  async () => {
    materialThumbnails.value = {}
    if (!props.resources?.length || !props.materials) return
    const matResources = props.resources.filter(
      r =>
        r.className === 'Material' ||
        r.className === 'MaterialInstanceConstant' ||
        r.className === 'MaterialInstance' ||
        r.role?.toLowerCase().includes('material') ||
        r.assetPath.toLowerCase().includes('/material/')
    )
    if (!matResources.length) return

    for (const r of matResources) {
      const rawMat = props.materials[r.assetPath]
      if (!rawMat) continue
      const spec = normalizeMaterialEntry(rawMat, r.assetPath)
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

function isTexture(r: ResourceItem): boolean {
  return r.className === 'Texture2D' || (!r.className && r.assetPath.endsWith('.uasset') && r.assetPath.includes('/Texture/'))
}
</script>

<template>
  <div class="resource-card-section">
    <div v-if="title" class="section-title">
      {{ title }} ({{ resources?.length || 0 }})
    </div>
    <div v-if="resources?.length" class="resource-card-list">
      <div
        v-for="r in resources"
        :key="r.assetPath + (r.role || '')"
        class="resource-card clickable-card"
        @click="emit('navigate-path', r.assetPath)"
      >
        <!-- 贴图缩略图 -->
        <div
          v-if="isTexture(r)"
          class="card-thumb"
          @click.stop="emit('preview-texture', r.assetPath, r.role || r.className || 'Texture')"
        >
          <n-image
            :src="getTextureThumbnailUrl(r.assetPath)"
            width="48"
            height="48"
            object-fit="cover"
            preview-disabled
          />
        </div>

        <!-- 材质解析缩略图 -->
        <div v-else-if="materialThumbnails[r.assetPath]" class="card-thumb">
          <n-image
            :src="materialThumbnails[r.assetPath]"
            width="48"
            height="48"
            object-fit="contain"
            preview-disabled
          />
        </div>

        <!-- 默认类型 Badge -->
        <div v-else class="card-type-icon">
          <span class="type-badge">{{ (r.className || r.role || 'RES').slice(0, 4) }}</span>
        </div>

        <!-- 资源卡片信息 -->
        <div class="card-info">
          <div class="card-top">
            <n-tag size="tiny" :bordered="false" type="info">
              {{ r.className || r.role || 'Resource' }}
            </n-tag>
            <span v-if="r.role && r.role !== r.className" class="role-hint">{{ r.role }}</span>
            <span v-else-if="r.objectName" class="role-hint">{{ r.objectName }}</span>
          </div>
          <span class="card-path" :title="r.assetPath">{{ r.assetPath }}</span>
        </div>
      </div>
    </div>
    <span v-else class="muted-note">无直接绑定的外部资源</span>
  </div>
</template>

<style scoped>
.resource-card-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.section-title {
  font-size: 12px;
  font-weight: 600;
  color: #888;
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
  gap: 6px;
}

.role-hint {
  font-size: 10px;
  color: #999;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-path {
  font-size: 11px;
  color: #2080f0;
  word-break: break-all;
  line-height: 1.3;
}

.muted-note {
  font-size: 12px;
  color: #888;
}
</style>
