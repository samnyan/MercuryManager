import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import { AllCommunityModule, ModuleRegistry } from 'ag-grid-community'
import App from './App.vue'
import MessageView from './MessageView.vue'
import MusicView from './MusicView.vue'
ModuleRegistry.registerModules([AllCommunityModule])
const router = createRouter({ history: createWebHistory(), routes: [{ path: '/', redirect: '/table/MusicParameterTable' }, { path: '/music', redirect: '/table/MusicParameterTable' }, { path: '/table/MusicParameterTable', component: MusicView }, {path:'/message/:name?',component:MessageView},{path:'/table/:name',component:MessageView}] })
createApp(App).use(createPinia()).use(router).mount('#app')
