<script setup lang="ts">
import { ref, computed, h, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NTabs, NTab, NInput, NMenu } from 'naive-ui'
import { i18n, tr } from '../../i18n'
import { tableCatalog } from '../../tableCatalog'
import ResourceTree from '../resource/ResourceTree.vue'

const props = defineProps<{
  collapsed?: boolean
}>()

const emit = defineEmits<{
  navigate: []
}>()

const router = useRouter()
const mode = ref('Table')
const navSearch = ref('')

const groupKeys: Record<string, string> = {
  "歌曲与解锁": "songsAndUnlocks",
  "成长与收集": "progressionAndCollection",
  "系统与界面": "systemAndInterface",
  "选曲与视觉": "selectionAndVisuals",
  "音效与音量": "soundAndVolume",
  "活动与挑战": "eventsAndChallenges",
  "游玩与判定": "gameplayAndJudgments"
}

watch(
  () => router.currentRoute.value.path,
  path => {
    mode.value = path.startsWith('/resource')
      ? 'Resource'
      : path.startsWith('/message')
      ? 'Message'
      : 'Table'
  },
  { immediate: true }
)

const names = [
  "AttackSEMessage", "BingoMissionConditionMessage", "BingoMissionMessage", "BoostItemMessage",
  "Chara01Message", "Chara02Message", "Chara03Message", "Chara04Message", "Chara54Message",
  "Chara55Message", "Chara56Message", "Chara57Message", "Chara58Message", "Chara59Message",
  "Chara60Message", "Chara61Message", "CharaMessageCommon", "ErrorMessage", "EventMessage",
  "GradeMessage", "IconMessage", "MultiPlayMessage", "MusicLicenseMessage", "MusicSearchMessage",
  "MusicSelectOptionMenuMessage", "MyRoomMessage", "NavigateCharacterMessage", "NGWordMessage",
  "ResultMessage", "ShopMessage", "StageUpMessage", "SugorokuMessage", "SystemMessage",
  "TestModeMessage", "TicketMessage", "TouchEffectMessage", "TouchPanelSymbolColorMessage",
  "UserPlateBackgroundMessage", "WelcomeMessage"
]

function navLabel(primary: string, secondary: string) {
  return () =>
    h('div', { class: 'nav-label' }, [
      h('div', { class: 'nav-title' }, primary),
      h('small', { class: 'nav-sub' }, secondary)
    ])
}

const options = computed(() => {
  const query = navSearch.value.trim().toLowerCase()
  return mode.value === 'Table'
    ? [...new Set(tableCatalog.map(t => t.group))]
        .map(group => ({
          key: group,
          label: tr('ui.' + (groupKeys[group] ?? 'systemAndInterface')),
          children: tableCatalog
            .filter(t => {
              if (t.group !== group) return false
              if (!query) return true
              const localized = tr('tables.' + t.name).toLowerCase()
              return `${t.title} ${t.name} ${localized}`.toLowerCase().includes(query)
            })
            .map(({ name }) => ({
              label: navLabel(tr('tables.' + name), name),
              key: '/table/' + name
            }))
        }))
        .filter(g => g.children.length)
    : names
        .filter(name => {
          if (!query) return true
          const localized = tr('messages.' + name).toLowerCase()
          return `${name} ${localized}`.toLowerCase().includes(query)
        })
        .map(name => ({
          label: navLabel(tr('messages.' + name), name),
          key: '/message/' + encodeURIComponent(name)
        }))
})

function toggle(value: string) {
  mode.value = value
  const targetPath =
    value === 'Resource'
      ? '/resource'
      : value === 'Table'
      ? '/table/MusicParameterTable'
      : names.length
      ? '/message/' + encodeURIComponent(names[0]!)
      : '/message'
  router.push(targetPath)
  emit('navigate')
}

function onMenuSelect(key: string) {
  router.push(key)
  emit('navigate')
}
</script>

<template>
  <div class="app-sidebar">
    <div class="sidebar-top">
      <n-tabs :value="mode" type="line" size="small" justify-content="space-evenly" @update:value="toggle">
        <n-tab name="Table">Table</n-tab>
        <n-tab name="Message">Message</n-tab>
        <n-tab name="Resource">Resource</n-tab>
      </n-tabs>
      <n-input
        v-if="mode !== 'Resource'"
        v-model:value="navSearch"
        :placeholder="tr('ui.searchTables')"
        clearable
        size="small"
        class="search-input"
      />
    </div>

    <div class="sidebar-scroll">
      <resource-tree v-if="mode === 'Resource'" @select="emit('navigate')" />
      <n-menu
        v-else
        :options="options"
        :value="router.currentRoute.value.path"
        @update:value="onMenuSelect"
      />
    </div>
  </div>
</template>

<style scoped>
.app-sidebar {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background-color: #fff;
}
.sidebar-top {
  padding: 12px 14px 10px;
  border-bottom: 1px solid #f0f0f4;
}
.search-input {
  margin-top: 8px;
}
.sidebar-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
}
:deep(.nav-label) {
  line-height: 1.3;
  padding: 4px 0;
}
:deep(.nav-title) {
  font-weight: 500;
  font-size: 13px;
}
:deep(.nav-sub) {
  display: block;
  color: #888;
  font-size: 11px;
}
:deep(.n-menu .n-menu-item) {
  height: auto;
  min-height: 48px;
}
</style>
