<script setup lang="ts">
import { ref, computed, h, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NTabs, NTab, NInput, NMenu } from 'naive-ui'
import { tr } from '../../i18n'
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

const messageLabels: Record<string, string> = {
  "AttackSEMessage": "打击音效", "BingoMissionConditionMessage": "宾果任务条件", "BingoMissionMessage": "宾果任务",
  "BoostItemMessage": "增益道具", "Chara01Message": "角色 01", "Chara02Message": "角色 02",
  "Chara03Message": "角色 03", "Chara04Message": "角色 04", "Chara54Message": "角色 54",
  "Chara55Message": "角色 55", "Chara56Message": "角色 56", "Chara57Message": "角色 57",
  "Chara58Message": "角色 58", "Chara59Message": "角色 59", "Chara60Message": "角色 60",
  "Chara61Message": "角色 61", "CharaMessageCommon": "角色通用", "ErrorMessage": "错误信息",
  "EventMessage": "活动", "GradeMessage": "等级", "IconMessage": "图标", "MultiPlayMessage": "多人游玩",
  "MusicLicenseMessage": "歌曲版权", "MusicSearchMessage": "歌曲搜索", "MusicSelectOptionMenuMessage": "选曲设置菜单",
  "MyRoomMessage": "个人房间", "NavigateCharacterMessage": "导航角色", "NGWordMessage": "禁用词",
  "ResultMessage": "成绩结果", "ShopMessage": "商店", "StageUpMessage": "段位挑战", "SugorokuMessage": "GATE",
  "SystemMessage": "系统", "TestModeMessage": "测试模式", "TicketMessage": "票券",
  "TouchEffectMessage": "触摸特效", "TouchPanelSymbolColorMessage": "触摸符号颜色",
  "UserPlateBackgroundMessage": "玩家名牌背景", "WelcomeMessage": "欢迎信息"
}

function navLabel(chinese: string, english: string) {
  return () =>
    h('div', { class: 'nav-label' }, [
      h('div', { class: 'nav-title' }, chinese),
      h('small', { class: 'nav-sub' }, english)
    ])
}

const options = computed(() =>
  mode.value === 'Table'
    ? [...new Set(tableCatalog.map(t => t.group))]
        .map(group => ({
          key: group,
          label: tr('ui.' + (groupKeys[group] ?? 'systemAndInterface')),
          children: tableCatalog
            .filter(
              t =>
                t.group === group &&
                `${t.title} ${t.name}`.toLowerCase().includes(navSearch.value.toLowerCase())
            )
            .map(({ name, title }) => ({
              label: navLabel(tr('tables.' + name), name),
              key: '/table/' + name
            }))
        }))
        .filter(g => g.children.length)
    : names
        .filter(name =>
          `${messageLabels[name]} ${name}`.toLowerCase().includes(navSearch.value.toLowerCase())
        )
        .map(name => ({
          label: navLabel(tr('messages.' + name), name),
          key: '/message/' + encodeURIComponent(name)
        }))
)

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
