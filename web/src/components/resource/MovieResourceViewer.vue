<script setup lang="ts">
import { computed } from 'vue'
import { NCard, NTag, NIcon } from 'naive-ui'
import { Film } from '@lucide/vue'
import { tr } from '../../i18n'

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
  return `${mins}:${secs.toString().padStart(2, '0')} (${(props.info.DurationSec || 0).toFixed(2)}s)`
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
            <span>{{ tr('ui.movieTitle') }}</span>
          </div>
          <n-tag type="info" size="small">{{ info.Name }}</n-tag>
        </div>
      </template>

      <div class="movie-grid">
        <div class="movie-prop">
          <span class="label">{{ tr('ui.movieFilePath') }}:</span>
          <code class="val-code">{{ info.FilePath || '—' }}</code>
          <n-tag v-if="info.UsmExists" type="success" size="tiny">
            {{ tr('ui.movieFileExists') }} ({{ sizeStr }})
          </n-tag>
          <n-tag v-else type="warning" size="tiny">
            {{ tr('ui.movieFileMissing') }}
          </n-tag>
        </div>

        <div class="movie-prop">
          <span class="label">{{ tr('ui.movieResolution') }}:</span>
          <span class="val font-medium">{{ info.Width }} × {{ info.Height }}</span>
        </div>

        <div class="movie-prop">
          <span class="label">{{ tr('ui.movieFrameRate') }}:</span>
          <span class="val">{{ (info.FrameRate || 0).toFixed(2) }} FPS</span>
        </div>

        <div class="movie-prop">
          <span class="label">{{ tr('ui.movieTotalFrames') }}:</span>
          <span class="val">{{ info.TotalFrames }}</span>
        </div>

        <div class="movie-prop">
          <span class="label">{{ tr('ui.movieDuration') }}:</span>
          <span class="val font-medium">{{ durationStr }}</span>
        </div>

        <div class="movie-prop">
          <span class="label">{{ tr('ui.movieCodec') }}:</span>
          <span class="val">{{ info.MovieType?.replace('EManaMovieType::', '') || 'SofdecPrime' }}</span>
        </div>

        <div class="movie-prop">
          <span class="label">{{ tr('ui.movieAlpha') }}:</span>
          <span class="val">{{ info.IsAlpha ? tr('ui.yes') : tr('ui.no') }}</span>
        </div>
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
</style>
