<script setup lang="ts">
import { ref, watch } from 'vue'
import { NTree, NSpin, useMessage, type TreeOption } from 'naive-ui'
import { useRouter } from 'vue-router'
import { api, useProject } from '../../project'
import { tr } from '../../i18n'

const emit = defineEmits<{
  select: []
}>()

const project = useProject()
const router = useRouter()
const message = useMessage()
const nodes = ref<TreeOption[]>([])
const loading = ref(false)

async function children(directory: string) {
  const rows = await api<{ path: string; name: string; directory: boolean }[]>(
    `/projects/${project.id}/resources?` + new URLSearchParams({ directory })
  )
  return rows.map(r => ({
    key: r.path,
    label: r.name,
    isLeaf: !r.directory
  }))
}

async function load(node: TreeOption) {
  node.children = await children(String(node.key))
}

watch(
  () => [project.id, project.contentRoot, project.revision],
  async () => {
    nodes.value = []
    if (project.id && project.contentRoot) {
      loading.value = true
      try {
        nodes.value = await children('')
      } catch (e) {
        message.error(String(e))
      } finally {
        loading.value = false
      }
    }
  },
  { immediate: true }
)

function select(keys: (string | number)[], options: (TreeOption | null)[]) {
  if (keys[0] && options[0]?.isLeaf) {
    router.push('/resource?path=' + encodeURIComponent(String(keys[0])))
    emit('select')
  }
}
</script>

<template>
  <div class="resource-tree-container">
    <div v-if="loading" class="resource-tree-loading">
      <n-spin size="small" :description="tr('ui.loadingResources')" />
    </div>
    <n-tree
      v-else
      :data="nodes"
      :on-load="load"
      expand-on-click
      block-line
      @update:selected-keys="select"
    />
  </div>
</template>

<style scoped>
.resource-tree-container {
  min-height: 120px;
  position: relative;
}
.resource-tree-loading {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 30px 0;
}
</style>
