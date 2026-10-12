<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
import { NTree, NInput, NSelect, NTag } from 'naive-ui'
import type { UMGDocument, UMGNode, UMGTreeRoot } from './types'

const props = defineProps<{
  document: UMGDocument
  selectedId: string | null
  activeRootId: string | null
}>()

const emit = defineEmits<{
  'select-node': [id: string | null]
  'select-root': [rootId: string]
}>()

const rootOptions = computed(() =>
  props.document.roots.map((r: UMGTreeRoot) => ({
    label: `${r.ownerName} (${r.kind})`,
    value: r.id
  }))
)

const activeRoot = computed(() =>
  props.document.roots.find(r => r.id === props.activeRootId) || props.document.roots[0]
)

type TreeItem = {
  key: string
  label: string
  node: UMGNode
  children: TreeItem[]
}

const treeData = computed<TreeItem[]>(() => {
  if (!activeRoot.value || !activeRoot.value.rootNodeId) return []
  const nodeMap = new Map<string, UMGNode>(props.document.nodes.map(n => [n.id, n]))

  const visited = new Set<string>()
  function buildBranch(nodeId: string): TreeItem | null {
    if (visited.has(nodeId)) return null
    visited.add(nodeId)
    const node = nodeMap.get(nodeId)
    if (!node) return null
    return {
      key: node.id,
      label: `${node.name} (${node.className})`,
      node,
      children: node.children.map(buildBranch).filter((c): c is TreeItem => c !== null)
    }
  }

  const rootItem = buildBranch(activeRoot.value.rootNodeId)
  return rootItem ? [rootItem] : []
})

const expandedKeys = ref<string[]>([])
const container = ref<HTMLElement | null>(null)
watch(() => [props.selectedId, props.activeRootId, props.document] as const, async () => {
  const chain = new Set<string>()
  let node = props.document.nodes.find(n => n.id === props.selectedId)
  while (node && !chain.has(node.id)) {
    chain.add(node.id)
    node = props.document.nodes.find(n => n.id === node!.parentId)
  }
  if (activeRoot.value?.rootNodeId) chain.add(activeRoot.value.rootNodeId)
  expandedKeys.value = [...new Set([...expandedKeys.value, ...chain])]
  await nextTick()
  container.value?.querySelector('.n-tree-node--selected')?.scrollIntoView({ block: 'nearest' })
}, { immediate: true })
const selectedKeys = computed(() => (props.selectedId ? [props.selectedId] : []))
</script>

<template>
  <div class="umg-hierarchy">
    <div class="hierarchy-header">
      <span class="header-title">Widget 层次树</span>
      <n-select
        :value="activeRootId"
        :options="rootOptions"
        size="tiny"
        style="width: 160px"
        @update:value="val => emit('select-root', val)"
      />
    </div>
    <div ref="container" class="tree-container">
      <n-tree
        block-line
        :expanded-keys="expandedKeys"
        @update:expanded-keys="keys => expandedKeys = keys as string[]"
        :data="treeData"
        :selected-keys="selectedKeys"
        @update:selected-keys="keys => emit('select-node', (keys[0] as string) || null)"
      />
    </div>
  </div>
</template>

<style scoped>
.umg-hierarchy {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--n-color);
  border-right: 1px solid var(--n-border-color);
}
.hierarchy-header {
  padding: 8px 12px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--n-border-color);
}
.header-title {
  font-weight: 600;
  font-size: 13px;
}
.tree-container {
  flex: 1;
  overflow: auto;
  padding: 8px;
}
</style>
