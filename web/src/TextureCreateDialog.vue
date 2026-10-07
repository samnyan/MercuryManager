<script setup lang="ts">
import TextureCreateDialogImpl from './components/resource/TextureCreateDialog.vue'

defineProps<{ show: boolean; base?: string; target?: string }>()
const emit = defineEmits(['update:show', 'created'])
</script>

<template>
  <TextureCreateDialogImpl
    :show="show"
    :base="base"
    :target="target"
    @update:show="emit('update:show', $event)"
    @created="emit('created')"
  />
</template>
