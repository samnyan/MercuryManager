<script setup lang="ts">
import { computed } from 'vue'
import { NCard, NTag, NSpace, NDivider, NIcon } from 'naive-ui'
import { Film, Clock, Layers, FileVideo } from '@lucide/vue'

export interface MovieInfo {
  Name: string
  FilePath?: string
  Width: number
  Height: number
  FrameRate: number
  TotalFrames: number
  DurationSec: number
  IsAlpha: boolean
  MovieType?: string
  UsmExists: boolean
  UsmBytes: number
}

const props = defineProps<{
  info: MovieInfo
  path: string
}>()

const durationStr = computed(() => {
  const total = Math.round(props.info.DurationSec || 0)
  const mins = Math.floor(total / 60)
  const secs = total % 60
  return `${mins}:${secs.toString().padStart(2, '0')} (${(props.info.DurationSec || 0).toFixed(2)} 秒)`
})

const sizeStr = computed(() => {
  const bytes = props.info.UsmBytes || 0
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`
})
</script>

<template>
  <div class="movie-viewer">
    <n-card size="small" class="movie-card">
      <template #header>
        <div class="movie-card-header">
          <div class="header-left">
            <n-icon :component="Film" size="18" />
            <span>CRI Sofdec 视频资产 (FileManaMovie)</span>
          </div>
          <n-tag type="info" size="small">{{ info.Name }}</n-tag>
        </div>
      </template>

      <div class="movie-grid">
        <div class="movie-prop">
          <span class="label">关联媒体文件 (.usm):</span>
          <code class="val-code">{{ info.FilePath || '—' }}</code>
          <n-tag v-if="info.UsmExists" type="success" size="tiny">文件存在 ({{ sizeStr }})</n-tag>
          <n-tag v-else type="warning" size="tiny">未在磁盘上找到 .usm</n-tag>
        </div>

        <div class="movie-prop">
          <span class="label">分辨率尺寸:</span>
          <span class="val font-medium">{{ info.Width }} × {{ info.Height }}</span>
        </div>

        <div class="movie-prop">
          <span class="label">播放帧率:</span>
          <span class="val">{{ (info.FrameRate || 0).toFixed(2) }} FPS</span>
        </div>

        <div class="movie-prop">
          <span class="label">总帧数:</span>
          <span class="val">{{ info.TotalFrames }} 帧</span>
        </div>

        <div class="movie-prop">
          <span class="label">视频时长:</span>
          <span class="val font-medium">{{ durationStr }}</span>
        </div>

        <div class="movie-prop">
          <span class="label">编码类型:</span>
          <span class="val">{{ info.MovieType?.replace('EManaMovieType::', '') || 'SofdecPrime' }}</span>
        </div>

        <div class="movie-prop">
          <span class="label">Alpha 透明通道:</span>
          <span class="val">{{ info.IsAlpha ? '包含 Alpha 遮罩' : '无透明通道' }}</span>
        </div>
      </div>
    </n-card>

    <n-card size="small" title="CRI Sofdec 媒体说明" class="movie-card">
      <div class="movie-hint-text">
        <p>• 该资产是 UE4 中 <code>FileManaMovie</code> 的轻量烘焙索引元数据，真实视频流存储在游戏 Content 目录或 PAK 的 <code>{{ info.FilePath }}</code> 中。</p>
        <p>• WACCA 中的选歌背景 MV、结算动画及段位结算均采用 CRI Sofdec 视频（通常为 1080×1080 正方形 60FPS H.264/Sofdec 编码格式）。</p>
      </div>
    </n-card>
  </div>
</template>

<style scoped>
.movie-viewer {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  overflow-y: auto;
  padding-right: 4px;
}
.movie-card {
  border-radius: 6px;
}
.movie-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-weight: 600;
  font-size: 14px;
}
.header-left {
  display: flex;
  align-items: center;
  gap: 8px;
}
.movie-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 12px 24px;
  font-size: 13px;
}
.movie-prop {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.movie-prop .label {
  color: #666;
}
.movie-prop .val {
  color: #222;
}
.font-medium {
  font-weight: 500;
}
.val-code {
  font-family: monospace;
  background: #f4f4f8;
  padding: 2px 6px;
  border-radius: 4px;
}
.movie-hint-text {
  font-size: 13px;
  color: #555;
  line-height: 1.6;
}
.movie-hint-text p {
  margin: 4px 0;
}
</style>
