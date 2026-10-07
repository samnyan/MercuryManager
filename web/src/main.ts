import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import { AllCommunityModule, ModuleRegistry } from 'ag-grid-community'
import App from './App.vue'
import {i18n} from './i18n'
import MessageView from './MessageView.vue'
import MusicView from './MusicView.vue'
import ResourceView from './ResourceView.vue'
ModuleRegistry.registerModules([AllCommunityModule])
const router = createRouter({ history: createWebHistory(), routes: [{path:'/resource',component:ResourceView}, { path: '/', redirect: '/table/MusicParameterTable' }, { path: '/music', redirect: '/table/MusicParameterTable' }, { path: '/table/MusicParameterTable', component: MusicView }, {path:'/message/:name?',component:MessageView},{path:'/table/:name',component:MessageView}] })
createApp(App).use(i18n).use(createPinia()).use(router).mount('#app')
