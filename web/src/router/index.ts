import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/dashboard' },
  { path: '/dashboard', name: 'dashboard', component: () => import('@/views/DashboardView.vue'), meta: { title: 'Dashboard' } },
  { path: '/clients', name: 'clients', component: () => import('@/views/ClientsView.vue'), meta: { title: 'Clients' } },
  { path: '/clients/:id', name: 'client', component: () => import('@/views/ClientDetailView.vue'), meta: { title: 'Client' } },
  { path: '/projects', name: 'projects', component: () => import('@/views/ProjectsView.vue'), meta: { title: 'Projects' } },
  { path: '/projects/:id', name: 'project', component: () => import('@/views/ProjectDetailView.vue'), meta: { title: 'Project' } },
  { path: '/resources', name: 'resources', component: () => import('@/views/ResourcesView.vue'), meta: { title: 'Resources' } },
  { path: '/resources/:id', name: 'resource', component: () => import('@/views/ResourceDetailView.vue'), meta: { title: 'Resource' } },
  { path: '/allocations', name: 'allocations', component: () => import('@/views/AllocationsView.vue'), meta: { title: 'Allocations' } },
  { path: '/reports', name: 'reports', component: () => import('@/views/ReportsView.vue'), meta: { title: 'Reports' } },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · SRA-RMS` : 'SRA-RMS'
})
