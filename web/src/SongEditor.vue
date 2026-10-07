<script setup lang="ts">
import { tr } from './i18n'
import { computed } from 'vue'
import {
  NModal,
  NCard,
  NButton,
  NForm,
  NFormItem,
  NCheckbox,
  NSpace,
  NAlert
} from 'naive-ui'
import FieldInput from './FieldInput.vue'
import { fieldLabel } from './fieldLabels'
import type { Field } from './project'

const props = defineProps<{
  show: boolean
  fields: Field[]
  title: string
  busy: boolean
}>()

const emit = defineEmits(['update:show', 'save'])

const difficulties = [
  { name: 'Normal', level: 'Normal', designer: 'Normal' },
  { name: 'Hard', level: 'Hard', designer: 'Hard' },
  { name: 'Expert', level: 'Extreme', designer: 'Expert' },
  { name: 'Inferno', level: 'Inferno', designer: 'Inferno' }
]

function chartFields(d: typeof difficulties[number]) {
  return props.fields.filter(f =>
    [`Difficulty${d.level}Lv`, `NotesDesigner${d.designer}`, `ClearNormaRate${d.level}`].includes(f.name)
  )
}

const regions = computed(() => props.fields.filter(f => f.name.startsWith('bValidCulture_')))

const groups = computed(() => {
  const used = new Set([...regions.value, ...difficulties.flatMap(chartFields)].map(f => f.name))
  const remaining = props.fields.filter(
    f => !used.has(f.name) && !['Reserved', 'WorkBuffer', 'AssetFullPath'].includes(f.name)
  )
  return [
    {
      name: tr('ui.basicInformation'),
      fields: remaining.filter(f =>
        [
          'UniqueID',
          'MusicMessage',
          'ArtistMessage',
          'CopyrightMessage',
          'Rubi',
          'VersionNo',
          'Bpm',
          'ScoreGenre',
          'HashTag'
        ].includes(f.name)
      )
    },
    {
      name: tr('ui.resources'),
      fields: remaining.filter(f => !f.readOnly && /Asset/.test(f.name))
    },
    {
      name: tr('ui.previewSettings'),
      fields: remaining.filter(f => f.name.startsWith('Preview'))
    },
    {
      name: tr('ui.unlockTags'),
      fields: remaining.filter(f => f.name.startsWith('MusicTagForUnlock'))
    },
    {
      name: tr('ui.otherSettings'),
      fields: remaining.filter(
        f =>
          !f.readOnly &&
          ![
            'MusicMessage',
            'ArtistMessage',
            'CopyrightMessage',
            'Rubi',
            'VersionNo',
            'Bpm',
            'ScoreGenre',
            'HashTag'
          ].includes(f.name) &&
          !/Asset/.test(f.name) &&
          !f.name.startsWith('Preview') &&
          !f.name.startsWith('MusicTagForUnlock')
      )
    },
    {
      name: tr('ui.internalFields'),
      fields: props.fields.filter(f =>
        ['Reserved', 'WorkBuffer', 'AssetFullPath'].includes(f.name)
      )
    }
  ]
})
</script>

<template>
  <n-modal
    :show="show"
    :mask-closable="false"
    @update:show="emit('update:show', $event)"
  >
    <n-card
      :title="title"
      class="song-dialog-card"
      closable
      @close="emit('update:show', false)"
    >
      <div class="song-scroll-area">
        <n-alert type="info" style="margin-bottom: 16px">
          {{ tr('ui.applyingChangesUpdatesTheDraftOnlyUseWriteFilesToExportNewRowsDoNotCreateChartsAudioOrUnlockRelations') }}
        </n-alert>

        <n-form label-placement="top">
          <!-- 基础信息 -->
          <section v-for="group in groups.slice(0, 1)" :key="group.name">
            <h3>{{ group.name }}</h3>
            <div class="form-grid">
              <n-form-item
                v-for="field in group.fields"
                :key="field.name"
                :label="fieldLabel(field.name)"
                :title="field.name"
              >
                <field-input :field="field" />
              </n-form-item>
            </div>
          </section>

          <!-- 谱面难度信息 -->
          <section>
            <h3>{{ tr('ui.chartInformation') }}</h3>
            <div class="chart-grid">
              <div
                v-for="difficulty in difficulties"
                :key="difficulty.name"
                class="chart-column"
              >
                <h4>{{ difficulty.name }}</h4>
                <n-form-item
                  v-for="field in chartFields(difficulty)"
                  :key="field.name"
                  :label="fieldLabel(field.name)"
                  :title="field.name"
                >
                  <field-input :field="field" />
                </n-form-item>
              </div>
            </div>
          </section>

          <!-- 区域/有效文化标志 -->
          <section>
            <h3>{{ tr('ui.regionsUserTypes') }}</h3>
            <div class="regions-flex">
              <n-checkbox
                v-for="field in regions"
                :key="field.name"
                v-model:checked="field.value as boolean"
                :title="field.name"
              >
                {{ field.name.replace('bValidCulture_', '') }} / {{ field.name }}
              </n-checkbox>
            </div>
          </section>

          <!-- 资源、预览与其他设置 -->
          <section v-for="group in groups.slice(1)" :key="group.name">
            <h3>{{ group.name }}</h3>
            <div class="form-grid">
              <n-form-item
                v-for="field in group.fields"
                :key="field.name"
                :label="fieldLabel(field.name)"
                :title="field.name"
              >
                <field-input :field="field" />
              </n-form-item>
            </div>
          </section>
        </n-form>
      </div>

      <template #footer>
        <div class="dialog-actions">
          <n-button :disabled="busy" @click="emit('update:show', false)">
            {{ tr('ui.cancel') }}
          </n-button>
          <n-button
            type="primary"
            :loading="busy"
            @click="emit('save')"
          >
            {{ tr('ui.applyToDraft') }}
          </n-button>
        </div>
      </template>
    </n-card>
  </n-modal>
</template>

<style scoped>
:deep(.song-dialog-card) {
  width: min(1040px, 95vw);
  max-height: 92vh;
  display: flex;
  flex-direction: column;
}
:deep(.song-dialog-card > .n-card__content) {
  flex: 1;
  overflow: hidden;
  padding: 16px 20px;
}
.song-scroll-area {
  max-height: calc(92vh - 160px);
  overflow-y: auto;
  padding-right: 6px;
}
.form-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0 16px;
}
.chart-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}
.chart-column {
  background: #f7f8fa;
  border-radius: 8px;
  padding: 12px;
}
.regions-flex {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
section {
  margin-bottom: 20px;
}
h3 {
  border-bottom: 1px solid #eee;
  padding-bottom: 8px;
  margin-top: 0;
  font-size: 15px;
}
h4 {
  margin: 0 0 10px;
  font-size: 14px;
}
.dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}

@media (max-width: 900px) {
  .chart-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
  .form-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
@media (max-width: 600px) {
  :deep(.song-dialog-card) {
    width: 98vw;
    max-height: 95vh;
  }
  .song-scroll-area {
    max-height: calc(95vh - 140px);
  }
  .chart-grid {
    grid-template-columns: 1fr;
  }
  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>
