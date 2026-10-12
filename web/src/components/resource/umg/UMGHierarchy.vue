<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NInput, NTree, NSelect, NTag, NButton, type TreeOption } from 'naive-ui'
import type { UMGNode, UMGTreeRoot } from './types'

const props = defineProps<{
  roots: UMGTreeRoot[]
  activeRootId: string | null
  nodes: UMGNode[]
  selectedId: string | null
  hiddenIds: Set<string>
}>()

const emit = defineEmits<{
  'update:activeRootId': [id: string]
  'select-node': [id: string]
  'update:hiddenIds': [ids: Set<string>]
}>()

const search = ref('')
const expandedKeys = ref<string[]>([])

const rootOptions = computed(() =>
  props.roots.map(r => ({
    label: `${r.ownerName} (${r.kind})`,
    value: r.id
  }))
)

const activeRoot = computed(() =>
  props.roots.find(r => r.id === props.activeRootId) || props.roots[0] || null
)

const nodeMap = computed(() => {
  const m = new Map<string, UMGNode>()
  for (const n of props.nodes) m.set(n.id, n)
  return m
})

// 收集当前子树的所有节点 ID
const allTreeNodeIds = computed(() => {
  const r = activeRoot.value
  if (!r || !r.rootNodeId) return []
  const ids: string[] = []
  const visited = new Set<string>()
  const walk = (id: string) => {
    if (visited.has(id)) return
    visited.add(id)
    const n = nodeMap.value.get(id)
    if (!n) return
    ids.push(n.id)
    for (const cid of n.children) walk(cid)
  }
  walk(r.rootNodeId)
  return ids
})

// 勾选的节点 = 未隐藏的节点
const checkedKeys = computed(() =>
  allTreeNodeIds.value.filter(id => !props.hiddenIds.has(id))
)

function handleUpdateCheckedKeys(keys: Array<string | number>) {
  const checkedSet = new Set(keys.map(String))
  const nextHidden = new Set(props.hiddenIds)
  for (const id of allTreeNodeIds.value) {
    if (checkedSet.has(id)) {
      nextHidden.delete(id)
    } else {
      nextHidden.add(id)
    }
  }
  emit('update:hiddenIds', nextHidden)
}

function showAll() {
  const nextHidden = new Set(props.hiddenIds)
  for (const id of allTreeNodeIds.value) nextHidden.delete(id)
  emit('update:hiddenIds', nextHidden)
}

function buildOption(nodeId: string, visited = new Set<string>()): TreeOption | null {
  if (visited.has(nodeId)) return null
  const n = nodeMap.value.get(nodeId)
  if (!n) return null
  const nextVisited = new Set(visited)
  nextVisited.add(nodeId)

  const children: TreeOption[] = []
  for (const cid of n.children) {
    const childOpt = buildOption(cid, nextVisited)
    if (childOpt) children.push(childOpt)
  }

  return {
    key: n.id,
    label: `${n.name} (${n.className})`,
    isLeaf: children.length === 0,
    children: children.length > 0 ? children : undefined,
    rawNode: n
  }
}

const treeData = computed<TreeOption[]>(() => {
  const r = activeRoot.value
  if (!r || !r.rootNodeId) return []
  const rootOpt = buildOption(r.rootNodeId)
  return rootOpt ? [rootOpt] : []
})

// 默认展开前两层
watch(
  () => activeRoot.value?.rootNodeId,
  rootNodeId => {
    if (!rootNodeId) {
      expandedKeys.value = []
      return
    }
    const keys: string[] = [rootNodeId]
    const rNode = nodeMap.value.get(rootNodeId)
    if (rNode) {
      keys.push(...rNode.children)
    }
    expandedKeys.value = keys
  },
  { immediate: true }
)

// 搜索时自动展开匹配的祖先节点
watch(search, q => {
  const query = q.trim().toLowerCase()
  if (!query) return
  const toExpand = new Set<string>(expandedKeys.value)
  for (const n of props.nodes) {
    if (n.name.toLowerCase().includes(query) || n.className.toLowerCase().includes(query)) {
      let curr: UMGNode | undefined = n
      while (curr?.parentId) {
        toExpand.add(curr.parentId)
        curr = nodeMap.value.get(curr.parentId)
      }
    }
  }
  expandedKeys.value = Array.from(toExpand)
})

function handleSelectKeys(keys: Array<string | number>) {
  if (keys.length > 0) {
    emit('select-node', String(keys[0]))
  }
}
</script>

<template>
  <div class="umg-hierarchy">
    <div class="hierarchy-header">
      <div class="section-row">
        <span class="section-title">Widget 层次树</span>
        <n-button
          v-if="hiddenIds.size > 0"
          size="tiny"
          quaternary
          type="primary"
          @click="showAll"
        >
          全部显示 ({{ hiddenIds.size }} 已隐藏)
        </n-button>
      </div>
      <n-select
        v-if="rootOptions.length > 1"
        size="small"
        :value="activeRoot?.id"
        :options="rootOptions"
        @update:value="val => emit('update:activeRootId', val)"
      />
      <n-input
        v-model:value="search"
        size="small"
        clearable
        placeholder="搜索控件名称或类型..."
      />
    </div>

    <div class="hierarchy-body">
      <n-tree
        block-line
        checkable
        cascade
        show-line
        :data="treeData"
        :pattern="search"
        :filter="(pattern, option) => String(option.label ?? '').toLowerCase().includes(pattern.toLowerCase())"
        :selected-keys="selectedId ? [selectedId] : []"
        :checked-keys="checkedKeys"
        v-model:expanded-keys="expandedKeys"
        @update:selected-keys="handleSelectKeys"
        @update:checked-keys="handleUpdateCheckedKeys"
      />
    </div>

    <div class="hierarchy-footer">
      <n-tag size="small" :bordered="false">共 {{ allTreeNodeIds.length }} 个节点</n-tag>
    </div>
  </div>
</template>

<style scoped>
.umg-hierarchy {
  display: flex;
  flex-direction: column;
  width: 280px;
  flex-shrink: 0;
  border-right: 1px solid var(--n-border-color);
  height: 100%;
  overflow: hidden;
}
.hierarchy-header {
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  border-bottom: 1px solid rgba(128, 128, 128, 0.15);
}
.section-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.section-title {
  font-size: 12px;
  font-weight: 600;
  color: #888;
}
.hierarchy-body {
  flex: 1;
  overflow-y: auto;
  padding: 6px;
}
/* 叶子节点隐藏展开图标，但保留 switcher 的宽度占位，维持原有的缩进层级 */
.hierarchy-body :deep(.n-tree-node-switcher--hide) {
  visibility: hidden;
  display: inline-block;
}
.hierarchy-footer {
  padding: 6px 10px;
  border-top: 1px solid rgba(128, 128, 128, 0.15);
  font-size: 12px;
}
</style>
