<script setup lang="ts">
import {tr} from './i18n'
import { ref, onMounted } from 'vue'
import { NButton, NAutoComplete, NSpace, NModal, NCard, NRadioGroup, NRadio, NCheckbox, NInput, useMessage } from 'naive-ui'
import { api } from './project'
import { useProject } from './project'
const project = useProject()
const path = ref('')
const busy = ref(false)
const message = useMessage()
const history = ref<string[]>([])
onMounted(() => {
  try {
    const stored: unknown = JSON.parse(localStorage.getItem('mercury-content-history') ?? '[]')
    if (Array.isArray(stored)) history.value = [...new Set(stored.filter((value): value is string => typeof value === 'string' && value.length > 0))].slice(0, 10)
    path.value = history.value[0] ?? ''
  } catch { history.value = [] }
})
async function open() {
  busy.value = true
  try {
    await project.open(path.value)
    path.value = project.contentRoot
    history.value = [path.value, ...history.value.filter(value => value !== path.value)].slice(0, 10)
    try { localStorage.setItem('mercury-content-history', JSON.stringify(history.value)) }
    catch { message.warning(tr('ui.projectOpenedButDirectoryHistoryCouldNotBeSaved')) }
    message.success(tr('ui.contentProjectOpened'))
  } catch(e) { message.error(e instanceof Error ? e.message : String(e)) }
  finally { busy.value = false }
}
const writeDialog = ref(false)
const mode = ref('overwrite')
const backup = ref(true)
const outputDirectory = ref('')
const changedFiles = ref<string[]>([])
async function saveProject() {
  try { const result = await api<{projectPath:string}>(`/workspaces/${project.id}/save`, 'POST'); message.success(tr('ui.projectSaved') + result.projectPath) }
  catch(e) { message.error(String(e)) }
}
async function showWrite() {
  try { changedFiles.value = await api<string[]>(`/workspaces/${project.id}/changes`); writeDialog.value = true }
  catch(e) { message.error(String(e)) }
}
async function write() {
  if (mode.value === 'overwrite' && !confirm(tr('ui.overwriteSourceAssetsCloseTheGameFirst') + (backup.value ? tr('ui.bakBackupsWillBeCreated') : tr('ui.backupsAreDisabled')))) return
  busy.value = true
  try {
    const result = await api<{writtenFiles:string[]}>(`/workspaces/${project.id}/write`, 'POST', {mode:mode.value,backup:backup.value,outputDirectory:outputDirectory.value})
    await project.refresh()
    writeDialog.value = false
    message.success(tr('ui.filesWritten') + result.writtenFiles.length + tr('ui.files'))
  } catch(e) { message.error(String(e)) }
  finally { busy.value = false }
}
function close() { if (confirm(tr('ui.closeProjectSavedDraftsRemainUnappliedChangesAreDiscarded'))) project.close() }
</script>
<template><div style="padding:10px 140px 10px 16px"><n-space align="center"><strong style="font-size:22px">MercuryManager</strong><n-auto-complete v-model:value="path" :options="history" :placeholder="tr('ui.gameContentDirectoryServerPath')" style="width:min(420px,40vw)"/><n-button :loading="busy" @click="open">{{tr('ui.openProject')}}</n-button><n-button :disabled="!project.id" @click="close">{{tr('ui.closeProject')}}</n-button><n-button :disabled="!project.id" @click="saveProject">{{tr('ui.saveProject')}}</n-button><n-button :disabled="!project.id" @click="showWrite">{{tr('ui.writeFiles')}}</n-button></n-space></div><n-modal v-model:show="writeDialog"><n-card :title="tr('ui.writeChangedFiles')" style="width:620px"><n-space vertical><div v-for="file in changedFiles" :key="file">{{ file }}</div><div v-if="!changedFiles.length">{{tr('ui.noChangedFiles')}}</div><n-radio-group v-model:value="mode"><n-radio value="overwrite">{{tr('ui.overwriteSourceFiles')}}</n-radio><n-radio value="directory">{{tr('ui.exportToDirectory')}}</n-radio></n-radio-group><n-checkbox v-if="mode === 'overwrite'" v-model:checked="backup">{{tr('ui.createBakBackupsStopIfBackupExists')}}</n-checkbox><n-input v-else v-model:value="outputDirectory" :placeholder="tr('ui.serverOutputDirectoryRetainRelativeTablePaths')"/><div>{{tr('ui.onlyAppliedDraftChangesAreWrittenApplyChangesFirst')}}</div><n-button :loading="busy" :disabled="!changedFiles.length" type="primary" @click="write">{{tr('ui.confirmWrite')}}</n-button></n-space></n-card></n-modal></template>
