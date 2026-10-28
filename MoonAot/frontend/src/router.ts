import { createRouter, createWebHashHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'

const routes: RouteRecordRaw[] = [
  { path: '/', name: 'shelf', component: () => import('./views/ShelfView.vue'), meta: { title: '书架' } },
  { path: '/recent', name: 'recent', component: () => import('./views/RecentView.vue'), meta: { title: '最近阅读' } },
  { path: '/notes', name: 'notes', component: () => import('./views/NotesView.vue'), meta: { title: '笔记' } },
  { path: '/files', name: 'files', component: () => import('./views/FilesView.vue'), meta: { title: '媒体库' } },
  { path: '/macro', name: 'macro', component: () => import('./views/MacroView.vue'), meta: { title: '按键精灵' } },
  { path: '/browser', name: 'browser', component: () => import('./views/BrowserLauncher.vue'), meta: { title: '浏览器' } },
  { path: '/wiki', name: 'wiki', component: () => import('./views/WikiView.vue'), meta: { title: '维基百科' } },
  { path: '/search', name: 'search', component: () => import('./views/SearchView.vue'), meta: { title: '全盘搜索' } },
  { path: '/toolbox', name: 'toolbox', component: () => import('./views/ToolboxView.vue'), meta: { title: '工具箱' } },
  { path: '/sitetools', name: 'sitetools', component: () => import('./views/SiteToolsView.vue'), meta: { title: '网站工具' } },
  { path: '/browserbar', name: 'browserbar', component: () => import('./views/BrowserBar.vue'), meta: { title: '浏览器', viewer: true } },
  { path: '/stats', name: 'stats', component: () => import('./views/StatsView.vue'), meta: { title: '统计' } },
  { path: '/settings', name: 'settings', component: () => import('./views/SettingsView.vue'), meta: { title: '设置' } },
  { path: '/view/:kind', name: 'viewer', component: () => import('./views/ViewerHost.vue'), meta: { title: '播放器', viewer: true } },
  { path: '/:pathMatch(.*)*', redirect: '/' },
]

const router = createRouter({
  history: createWebHashHistory(),
  routes,
})

router.afterEach((to) => {
  if (!to.meta.viewer) document.title = `静读天下 · ${to.meta.title || '书架'}`
})

export default router
