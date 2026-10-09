<script setup lang="ts">
import {NModal,NDialog} from 'naive-ui'
import {tr} from '../../i18n'
defineProps<{show:boolean;data:unknown}>()
const emit=defineEmits<{ 'update:show':[value:boolean] }>()
</script>
<template>
<n-modal :show="show" @update:show="emit('update:show',$event)"><n-dialog :title="tr('ui.audioDetails')" :show-icon="false" :positive-text="tr('ui.audioDetailsClose')" style="width:min(820px,95vw)" @close="emit('update:show',false)" @positive-click="emit('update:show',false)">
<div class="audio-details-body"><pre class="audio-details-json">{{JSON.stringify(data,null,2)}}</pre></div>
</n-dialog></n-modal>
</template>
<style>.audio-details-body{max-height:calc(85dvh - 140px);overflow:auto}.audio-details-json{margin:0;white-space:pre-wrap;overflow-wrap:anywhere;font-size:13px;line-height:1.6;user-select:text}</style>
