<script setup lang="ts">
import { ref } from 'vue'
import {
  NConfigProvider,
  NMessageProvider,
  NLayout,
  NLayoutHeader,
  NLayoutSider,
  NLayoutContent,
  NDrawer,
  NDrawerContent,
  zhCN,
  enUS
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { i18n } from './i18n'
import { useProject } from './project'
import { useResponsive } from './composables/useResponsive'
import AppHeader from './components/layout/AppHeader.vue'
import AppSidebar from './components/layout/AppSidebar.vue'

const router = useRouter()
const project = useProject()
const { isMobile } = useResponsive()

const drawerVisible = ref(false)
</script>

<template>
  <n-config-provider :locale="i18n.global.locale.value === 'zh' ? zhCN : enUS">
    <n-message-provider>
      <div class="app-container">
        <!-- 顶部自适应导航栏 -->
        <app-header
          :is-mobile="isMobile"
          @toggle-drawer="drawerVisible = !drawerVisible"
        />

        <!-- 主内容区域 -->
        <n-layout has-sider class="app-body">
          <!-- 桌面端侧边栏 -->
          <n-layout-sider
            v-if="!isMobile"
            bordered
            :width="250"
            class="desktop-sider"
          >
            <app-sidebar />
          </n-layout-sider>

          <!-- 主视图区 -->
          <n-layout-content class="app-content">
            <router-view
              :key="project.id + project.revision + router.currentRoute.value.fullPath"
            />
          </n-layout-content>
        </n-layout>

        <!-- 移动端侧边抽屉 -->
        <n-drawer
          v-model:show="drawerVisible"
          placement="left"
          :width="280"
          :trap-focus="false"
          :block-scroll="false"
        >
          <n-drawer-content body-content-style="padding: 0; height: 100%; display: flex; flex-direction: column;">
            <app-sidebar @navigate="drawerVisible = false" />
          </n-drawer-content>
        </n-drawer>
      </div>
    </n-message-provider>
  </n-config-provider>
</template>

<style>
body {
  margin: 0;
  font-family: system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
  color: #333;
}
* {
  box-sizing: border-box;
}
.app-container {
  height: 100vh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.app-body {
  flex: 1;
  height: calc(100vh - 52px);
}
.desktop-sider {
  height: 100%;
}
.app-content {
  padding: 12px 16px;
  overflow-y: auto;
}
@media (max-width: 768px) {
  .app-content {
    padding: 8px 10px;
  }
}
.page-title {
  font-size: 17px;
  line-height: 24px;
  margin: 0 0 8px;
  font-weight: 600;
}
</style>
