<script setup lang="ts">
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
    catch { message.warning('工程已打开，但浏览器无法保存目录历史') }
    message.success('Content 工程已打开')
  } catch(e) { message.error(e instanceof Error ? e.message : String(e)) }
  finally { busy.value = false }
}
const writeDialog = ref(false)
const mode = ref('overwrite')
const backup = ref(true)
const outputDirectory = ref('')
const changedFiles = ref<string[]>([])
async function saveProject() {
  try { const result = await api<{projectPath:string}>(`/workspaces/${project.id}/save`, 'POST'); message.success('工程已保存：' + result.projectPath) }
  catch(e) { message.error(String(e)) }
}
async function showWrite() {
  try { changedFiles.value = await api<string[]>(`/workspaces/${project.id}/changes`); writeDialog.value = true }
  catch(e) { message.error(String(e)) }
}
async function write() {
  if (mode.value === 'overwrite' && !confirm('确认覆盖原工程资产？请先关闭游戏。' + (backup.value ? '将创建 _bak 备份。' : '未开启备份。'))) return
  busy.value = true
  try {
    const result = await api<{writtenFiles:string[]}>(`/workspaces/${project.id}/write`, 'POST', {mode:mode.value,backup:backup.value,outputDirectory:outputDirectory.value})
    await project.refresh()
    writeDialog.value = false
    message.success('已写入 ' + result.writtenFiles.length + ' 个文件')
  } catch(e) { message.error(String(e)) }
  finally { busy.value = false }
}
function close() { if (confirm('关闭当前工程？已保存的草稿会保留，未保存的表单修改将丢弃。')) project.close() }
</script>
<template><div style="padding:10px 16px"><n-space align="center"><strong style="font-size:22px">MercuryManager</strong><n-auto-complete v-model:value="path" :options="history" placeholder="游戏 Content 目录（服务器路径）" style="width:540px"/><n-button :loading="busy" @click="open">打开工程</n-button><n-button :disabled="!project.id" @click="close">关闭工程</n-button><n-button :disabled="!project.id" @click="saveProject">保存工程</n-button><n-button :disabled="!project.id" @click="showWrite">写入文件</n-button></n-space></div><n-modal v-model:show="writeDialog"><n-card title="写入变动文件" style="width:620px"><n-space vertical><div v-for="file in changedFiles" :key="file">{{ file }}</div><div v-if="!changedFiles.length">没有变动文件</div><n-radio-group v-model:value="mode"><n-radio value="overwrite">覆盖原文件</n-radio><n-radio value="directory">输出到目录</n-radio></n-radio-group><n-checkbox v-if="mode === 'overwrite'" v-model:checked="backup">创建 _bak 备份（已有备份时停止，避免覆盖）</n-checkbox><n-input v-else v-model:value="outputDirectory" placeholder="服务器输出目录，保留 Table 相对路径"/><div>只写已应用到工程草稿的修改；请先在编辑器应用修改。</div><n-button :loading="busy" :disabled="!changedFiles.length" type="primary" @click="write">确认写入</n-button></n-space></n-card></n-modal></template>
