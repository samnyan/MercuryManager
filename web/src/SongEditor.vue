<script setup lang="ts">
import {tr} from './i18n'
import { computed } from 'vue'
import { NModal,NCard,NButton,NForm,NFormItem,NCheckbox,NSpace,NAlert } from 'naive-ui'
import FieldInput from './FieldInput.vue'
import { fieldLabel } from './fieldLabels'
import type { Field } from './project'
const props=defineProps<{show:boolean;fields:Field[];title:string;busy:boolean}>()
const emit=defineEmits(['update:show','save'])
const difficulties=[{name:'Normal',level:'Normal',designer:'Normal'},{name:'Hard',level:'Hard',designer:'Hard'},{name:'Expert',level:'Extreme',designer:'Expert'},{name:'Inferno',level:'Inferno',designer:'Inferno'}]
function chartFields(d:typeof difficulties[number]) { return props.fields.filter(f=>[`Difficulty${d.level}Lv`,`NotesDesigner${d.designer}`,`ClearNormaRate${d.level}`].includes(f.name)) }
const regions=computed(()=>props.fields.filter(f=>f.name.startsWith('bValidCulture_')))
const groups=computed(()=>{
 const used=new Set([...regions.value,...difficulties.flatMap(chartFields)].map(f=>f.name))
 const remaining=props.fields.filter(f=>!used.has(f.name) && !['Reserved','WorkBuffer','AssetFullPath'].includes(f.name))
 return [
 {name:tr('ui.basicInformation'),fields:remaining.filter(f=>['UniqueID','MusicMessage','ArtistMessage','CopyrightMessage','Rubi','VersionNo','Bpm','ScoreGenre','HashTag'].includes(f.name))},
 {name:tr('ui.resources'),fields:remaining.filter(f=>!f.readOnly && /Asset/.test(f.name))},
 {name:tr('ui.previewSettings'),fields:remaining.filter(f=>f.name.startsWith('Preview'))},
 {name:tr('ui.unlockTags'),fields:remaining.filter(f=>f.name.startsWith('MusicTagForUnlock'))},
 {name:tr('ui.otherSettings'),fields:remaining.filter(f=>!f.readOnly && !['MusicMessage','ArtistMessage','CopyrightMessage','Rubi','VersionNo','Bpm','ScoreGenre','HashTag'].includes(f.name) && !/Asset/.test(f.name) && !f.name.startsWith('Preview') && !f.name.startsWith('MusicTagForUnlock'))},
 {name:tr('ui.internalFields'),fields:props.fields.filter(f=>['Reserved','WorkBuffer','AssetFullPath'].includes(f.name))}
 ]
})
</script>
<template><n-modal :show="show" :mask-closable="false" @update:show="emit('update:show',$event)"><n-card :title="title" class="song-dialog" closable @close="emit('update:show',false)"><div class="song-scroll"><n-alert type="info" style="margin-bottom:18px">{{tr('ui.applyingChangesUpdatesTheDraftOnlyUseWriteFilesToExportNewRowsDoNotCreateChartsAudioOrUnlockRelations')}}</n-alert><n-form label-placement="top"><section v-for="group in groups.slice(0,1)" :key="group.name"><h3>{{group.name}}</h3><div class="form-grid"><n-form-item v-for="field in group.fields" :key="field.name" :label="fieldLabel(field.name)" :title="field.name"><field-input :field="field"/></n-form-item></div></section><section><h3>{{tr('ui.chartInformation')}}</h3><div class="chart-grid"><div v-for="difficulty in difficulties" :key="difficulty.name" class="chart-column"><h4>{{difficulty.name}}</h4><n-form-item v-for="field in chartFields(difficulty)" :key="field.name" :label="fieldLabel(field.name)" :title="field.name"><field-input :field="field"/></n-form-item></div></div></section><section><h3>{{tr('ui.regionsUserTypes')}}</h3><n-space><n-checkbox v-for="field in regions" :key="field.name" v-model:checked="field.value as boolean" :title="field.name">{{field.name.replace('bValidCulture_','')}} / {{field.name}}</n-checkbox></n-space></section><section v-for="group in groups.slice(1)" :key="group.name"><h3>{{group.name}}</h3><div class="form-grid"><n-form-item v-for="field in group.fields" :key="field.name" :label="fieldLabel(field.name)" :title="field.name"><field-input :field="field"/></n-form-item></div></section></n-form></div><template #footer><n-space justify="end"><n-button @click="emit('update:show',false)">{{tr('ui.cancel')}}</n-button><n-button type="primary" :loading="busy" @click="emit('save')">{{tr('ui.applyToDraft')}}</n-button></n-space></template></n-card></n-modal></template>
<style scoped>.song-dialog{width:calc(100vw - 32px);height:calc(100vh - 32px);max-width:none}.song-scroll{height:calc(100vh - 190px);overflow:auto;padding:0 16px}.form-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:0 22px}.chart-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:18px}.chart-column{background:#f7f8fa;border-radius:8px;padding:14px}section{margin-bottom:24px}h3{border-bottom:1px solid #eee;padding-bottom:10px}h4{margin:0 0 14px}@media(max-width:850px){.form-grid,.chart-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}</style>
