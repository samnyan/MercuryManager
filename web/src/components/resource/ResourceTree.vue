<script setup lang="ts">
import { ref, watch, nextTick } from 'vue'
import { NTree, NSpin, useMessage, type TreeOption } from 'naive-ui'
import { useRouter, useRoute } from 'vue-router'
import { api, useProject } from '../../project'
import { tr } from '../../i18n'

const emit = defineEmits<{
  select: []
}>()

const project = useProject()
const router = useRouter()
const route = useRoute()
const message = useMessage()
const nodes = ref<TreeOption[]>([])
const expandedKeys = ref<string[]>([])
const selectedKeys = ref<string[]>([])
const loading = ref(false)
const containerRef = ref<HTMLElement | null>(null)

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

function findNode(list: TreeOption[], key: string): TreeOption | undefined {
  for (const item of list) {
    if (item.key === key) return item
    if (item.children) {
      const found = findNode(item.children, key)
      if (found) return found
    }
  }
  return undefined
}

async function revealPath(targetPath: string) {
  if (!targetPath || !nodes.value.length) return
  const parts = targetPath.split('/').filter(Boolean)
  if (!parts.length) return

  selectedKeys.value = [targetPath]
  const newExpanded = new Set(expandedKeys.value)
  let currentList = nodes.value
  let currentPrefix = ''

  // 逐级展开父目录
  for (let i = 0; i < parts.length - 1; i++) {
    currentPrefix = currentPrefix ? `${currentPrefix}/${parts[i]}` : parts[i]!
    const folderNode = currentList.find(n => n.key === currentPrefix)
    if (!folderNode) break
    if (!folderNode.children) {
      try {
        folderNode.children = await children(currentPrefix)
      } catch {
        break
      }
    }
    newExpanded.add(currentPrefix)
    currentList = folderNode.children || []
  }

  expandedKeys.value = Array.from(newExpanded)

  await nextTick()
  setTimeout(() => {
    const selectedEl = containerRef.value?.querySelector('.n-tree-node--selected')
    selectedEl?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }, 60)
}

watch(
  () => [project.id, project.contentRoot, project.revision],
  async () => {
    nodes.value = []
    expandedKeys.value = []
    selectedKeys.value = []
    if (project.id && project.contentRoot) {
      loading.value = true
      try {
        nodes.value = await children('')
        if (route.query.path) {
          await revealPath(String(route.query.path))
        }
      } catch (e) {
        message.error(String(e))
      } finally {
        loading.value = false
      }
    }
  },
  { immediate: true }
)

watch(
  () => route.query.path,
  async newPath => {
    if (newPath && nodes.value.length > 0) {
      await revealPath(String(newPath))
    } else if (!newPath) {
      selectedKeys.value = []
    }
  }
)

function select(keys: (string | number)[], options: (TreeOption | null)[]) {
  if (keys[0] && options[0]?.isLeaf) {
    selectedKeys.value = [String(keys[0])]
    router.push('/resource?path=' + encodeURIComponent(String(keys[0])))
    emit('select')
  }
}

function onUpdateExpandedKeys(keys: string[]) {
  expandedKeys.value = keys
}
</script>

<template>
  <div ref="containerRef" class="resource-tree-container">
    <div v-if="loading" class="resource-tree-loading">
      <n-spin size="small" :description="tr('ui.loadingResources')" />
    </div>
    <n-tree
      v-else
      :data="nodes"
      :on-load="load"
      :selected-keys="selectedKeys"
      :expanded-keys="expandedKeys"
      expand-on-click
      block-line
      @update:selected-keys="select"
      @update:expanded-keys="onUpdateExpandedKeys"
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
:deep(.n-tree-node-content__text) {
  word-break: break-all;
  line-height: 1.35;
}
</style>
