<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import {
  NButton,
  NSpace,
  NModal,
  NCard,
  NInput,
  NSelect,
  NRadioGroup,
  NRadio,
  NCheckbox,
  NDropdown,
  NTag,
  useMessage,
  type DropdownOption
} from 'naive-ui'
import { tr, i18n, setLanguage } from '../../i18n'
import { api, useProject } from '../../project'

const props = defineProps<{
  isMobile: boolean
}>()

const emit = defineEmits<{
  toggleDrawer: []
}>()

const project = useProject()
const message = useMessage()
const busy = ref(false)
const dialog = ref('')
const path = ref('')
const label = ref('')
const selected = ref('')
const projects = ref<{ id: string; name: string; saved: boolean }[]>([])

const mode = ref('overwrite')
const backup = ref(true)
const outputDirectory = ref('')
const changedFiles = ref<string[]>([])

async function run(action: () => Promise<void>) {
  busy.value = true
  try {
    await action()
  } catch (e) {
    message.error(String(e))
  } finally {
    busy.value = false
  }
}

async function showOpen() {
  await run(async () => {
    const list = await api<Array<{ id: string; name: string; saved: boolean }>>('/projects')
    projects.value = list
    selected.value = list[0]?.id ?? ''
    dialog.value = 'open'
  })
}

onMounted(async () => {
  const savedWs = localStorage.getItem('mercury-workspace')
  if (savedWs && !project.id) {
    try {
      await project.resume(savedWs)
    } catch {
      localStorage.removeItem('mercury-workspace')
    }
  }
})

async function accept() {
  await run(async () => {
    if (dialog.value === 'new') await project.create(label.value)
    else if (dialog.value === 'open') await project.resume(selected.value)
    else {
      if (project.contentRoot && !confirm(tr('ui.reimportConfirm'))) return
      await project.importTables(path.value)
    }
    dialog.value = ''
  })
}

async function save() {
  if (project.pending) {
    message.warning(tr('ui.applyBeforeSave'))
    return
  }
  await run(async () => {
    await project.save()
    message.success(tr('ui.projectSaved') + project.name)
  })
}

async function refreshExportFiles() {
  changedFiles.value = await api<string[]>(
    mode.value === 'directory'
      ? `/projects/${project.id}/export-files`
      : `/workspaces/${project.id}/changes`
  )
}

watch(mode, value => {
  if (dialog.value === 'export') {
    if (value === 'overwrite') outputDirectory.value = project.contentRoot
    run(refreshExportFiles)
  }
})

watch(outputDirectory, value => {
  if (dialog.value === 'export') {
    mode.value = value.trim() === project.contentRoot ? 'overwrite' : 'directory'
  }
})

async function showExport() {
  await run(async () => {
    await project.status()
    if (project.dirty || project.pending || !project.saved) {
      message.warning(tr('ui.saveBeforeExport'))
      return
    }
    outputDirectory.value = project.contentRoot
    mode.value = 'overwrite'
    await refreshExportFiles()
    dialog.value = 'export'
  })
}

async function write() {
  if (!outputDirectory.value.trim()) {
    message.warning(tr('ui.serverOutputDirectoryRetainRelativeTablePaths'))
    return
  }
  if (outputDirectory.value.trim() === project.contentRoot) mode.value = 'overwrite'
  if (mode.value === 'overwrite' && !confirm(tr('ui.overwriteSourceAssetsCloseTheGameFirst'))) return
  await run(async () => {
    const result = await api<{ writtenFiles: string[] }>(
      `/workspaces/${project.id}/write`,
      'POST',
      {
        mode: mode.value,
        backup: backup.value,
        outputDirectory: outputDirectory.value
      }
    )
    await project.status()
    await project.refresh()
    dialog.value = ''
    message.success(tr('ui.filesWritten') + result.writtenFiles.length)
  })
}

function close() {
  if ((project.dirty || project.pending) && !confirm(tr('ui.unsavedClose'))) return
  project.close()
}

// 移动端下拉折叠菜单项
const mobileMenuOptions = computed<DropdownOption[]>(() => {
  const list: DropdownOption[] = []

  if (!project.id) {
    list.push(
      { label: tr('ui.newProject'), key: 'new' },
      { label: tr('ui.openProject'), key: 'open' }
    )
  } else {
    list.push(
      { label: tr('ui.saveProject') + (project.dirty || project.pending ? ' *' : ''), key: 'save', disabled: busy.value },
      { label: tr('ui.exportGameTables'), key: 'export', disabled: busy.value || !project.contentRoot },
      { label: tr('ui.importGameTables'), key: 'import', disabled: busy.value },
      { label: tr('ui.closeProject'), key: 'close', disabled: busy.value }
    )
  }

  list.push(
    { type: 'divider', key: 'd1' },
    {
      label: i18n.global.locale.value === 'zh' ? 'Switch to English' : '切换为中文',
      key: 'toggle-lang'
    }
  )

  return list
})

function onMobileMenuSelect(key: string) {
  switch (key) {
    case 'new':
      dialog.value = 'new'
      break
    case 'open':
      showOpen()
      break
    case 'save':
      save()
      break
    case 'export':
      showExport()
      break
    case 'import':
      path.value = project.contentRoot
      dialog.value = 'import'
      break
    case 'close':
      close()
      break
    case 'toggle-lang':
      setLanguage(i18n.global.locale.value === 'zh' ? 'en' : 'zh')
      break
  }
}
</script>

<template>
  <header class="app-header">
    <div class="header-left">
      <!-- 移动端汉堡折叠按钮 -->
      <n-button
        v-if="isMobile"
        quaternary
        size="small"
        class="menu-btn"
        @click="emit('toggleDrawer')"
      >
        <span class="menu-icon">☰</span>
      </n-button>

      <span class="app-title">MercuryManager</span>

      <n-tag
        v-if="project.id"
        size="small"
        :type="project.dirty || project.pending ? 'warning' : 'default'"
        class="project-badge"
      >
        {{ project.name }}{{ project.dirty || project.pending ? ' *' : '' }}
      </n-tag>

      <div v-if="project.loading" class="header-loading-tag">
        <n-spin size="small" />
        <span class="loading-text">{{ tr('ui.loading') }}</span>
      </div>
    </div>

    <!-- 桌面端操作区域 -->
    <div v-if="!isMobile" class="header-right-desktop">
      <n-space align="center" :size="8">
        <template v-if="!project.id">
          <n-button size="small" @click="dialog = 'new'">{{ tr('ui.newProject') }}</n-button>
          <n-button size="small" @click="showOpen">{{ tr('ui.openProject') }}</n-button>
        </template>
        <template v-else>
          <n-button size="small" type="primary" :loading="busy" @click="save">
            {{ tr('ui.saveProject') }}
          </n-button>
          <n-button size="small" :disabled="busy || !project.contentRoot" @click="showExport">
            {{ tr('ui.exportGameTables') }}
          </n-button>
          <n-button size="small" :disabled="busy" @click="path = project.contentRoot; dialog = 'import'">
            {{ tr('ui.importGameTables') }}
          </n-button>
          <n-button size="small" quaternary :disabled="busy" @click="close">
            {{ tr('ui.closeProject') }}
          </n-button>
        </template>

        <n-select
          :value="i18n.global.locale.value"
          :options="[{ label: '中文', value: 'zh' }, { label: 'English', value: 'en' }]"
          size="small"
          style="width: 95px; margin-left: 8px"
          @update:value="setLanguage"
        />
      </n-space>
    </div>

    <!-- 移动端操作区域：聚合为折叠下拉按钮 -->
    <div v-else class="header-right-mobile">
      <n-dropdown
        trigger="click"
        :options="mobileMenuOptions"
        @select="onMobileMenuSelect"
      >
        <n-button size="small" secondary>
          <span>{{ tr('ui.actions') }} ▾</span>
        </n-button>
      </n-dropdown>
    </div>

    <!-- 项目相关模态框 -->
    <n-modal
      :show="!!dialog"
      :mask-closable="!busy"
      @update:show="v => { if (!v && !busy) dialog = '' }"
    >
      <n-card
        class="project-modal-card"
        :title="tr('ui.' + ({ new: 'newProject', open: 'openProject', import: 'importGameTables', export: 'exportGameTables' }[dialog] ?? 'openProject'))"
        :closable="!busy"
        @close="dialog = ''"
      >
        <n-space vertical :size="14">
          <n-input
            v-if="dialog === 'new'"
            v-model:value="label"
            :placeholder="tr('ui.projectName')"
            :disabled="busy"
          />

          <n-select
            v-if="dialog === 'open'"
            v-model:value="selected"
            :options="projects.map(p => ({ label: p.name, value: p.id }))"
          />

          <template v-if="dialog === 'import'">
            <n-input
              v-model:value="path"
              :placeholder="tr('ui.gameContentDirectoryServerPath')"
              :disabled="busy"
            />
            <div class="modal-tip">{{ tr('ui.importHint') }}</div>
          </template>

          <template v-if="dialog === 'export'">
            <n-input
              v-model:value="outputDirectory"
              :disabled="busy"
              :placeholder="tr('ui.serverOutputDirectoryRetainRelativeTablePaths')"
            />
            <n-input
              type="textarea"
              :value="changedFiles.length ? changedFiles.join('\n') : tr('ui.noChangedFiles')"
              readonly
              :rows="8"
              class="code-textarea"
            />
            <n-radio-group v-model:value="mode">
              <n-space>
                <n-radio value="overwrite">{{ tr('ui.overwriteSourceFiles') }}</n-radio>
                <n-radio value="directory">{{ tr('ui.exportToDirectory') }}</n-radio>
              </n-space>
            </n-radio-group>
            <n-checkbox v-if="mode === 'overwrite'" v-model:checked="backup">
              {{ tr('ui.createBakBackupsStopIfBackupExists') }}
            </n-checkbox>
          </template>

          <n-button
            type="primary"
            block
            :loading="busy"
            :disabled="busy"
            @click="dialog === 'export' ? write() : accept()"
          >
            {{ tr(dialog === 'export' ? 'ui.confirmWrite' : 'ui.confirm') }}
          </n-button>
        </n-space>
      </n-card>
    </n-modal>
  </header>
</template>

<style scoped>
.app-header {
  height: 52px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 14px;
  background-color: #fff;
  border-bottom: 1px solid #efeff5;
  box-sizing: border-box;
}
.header-left {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}
.menu-btn {
  padding: 0 6px;
  font-size: 16px;
}
.menu-icon {
  font-size: 18px;
  line-height: 1;
}
.app-title {
  font-weight: 700;
  font-size: 15px;
  color: #1f2225;
  white-space: nowrap;
}
.project-badge {
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.header-loading-tag {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: #2080f0;
  background: #f0f7ff;
  border: 1px solid #bae0ff;
  padding: 2px 8px;
  border-radius: 12px;
  line-height: 1;
}
.loading-text {
  font-weight: 500;
}
.header-right-desktop {
  display: flex;
  align-items: center;
}
.header-right-mobile {
  display: flex;
  align-items: center;
}
.project-modal-card {
  width: min(640px, 94vw);
}
.modal-tip {
  font-size: 12px;
  color: #888;
}
.code-textarea {
  font-family: monospace;
  font-size: 12px;
}
</style>
