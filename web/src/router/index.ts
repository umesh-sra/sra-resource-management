import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

/**
 * Routes follow the reference application's information architecture:
 * Clients live inside "Projects & Clients", and a person's record is a drawer
 * over the People grid (so /people/:id is deep-linkable). The pre-existing
 * /resources and /allocations paths redirect to their new homes.
 */
const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/dashboard' },
  { path: '/dashboard', name: 'dashboard', component: () => import('@/views/DashboardView.vue'), meta: { title: 'Dashboard' } },
  { path: '/schedule', name: 'schedule', component: () => import('@/views/ScheduleView.vue'), meta: { title: 'Schedule' } },
  { path: '/gantt', name: 'gantt', component: () => import('@/views/GanttView.vue'), meta: { title: 'Gantt Charts' } },

  { path: '/people', name: 'people', component: () => import('@/views/PeopleView.vue'), meta: { title: 'People & Resources' } },
  { path: '/people/:id', name: 'person', component: () => import('@/views/PeopleView.vue'), meta: { title: 'Person' } },

  { path: '/projects', name: 'projects', component: () => import('@/views/WorkView.vue'), meta: { title: 'Projects & Clients' } },
  { path: '/projects/:id', name: 'project', component: () => import('@/views/ProjectDetailView.vue'), meta: { title: 'Project' } },
  { path: '/clients', name: 'clients', component: () => import('@/views/WorkView.vue'), meta: { title: 'Projects & Clients' } },
  { path: '/clients/:id', name: 'client', component: () => import('@/views/ClientDetailView.vue'), meta: { title: 'Client' } },

  { path: '/reports', name: 'reports', component: () => import('@/views/ReportsView.vue'), meta: { title: 'Reports' } },
  { path: '/import', name: 'import', component: () => import('@/views/ImportView.vue'), meta: { title: 'Data Import' } },

  // Legacy paths from the previous navigation.
  { path: '/resources', redirect: '/people' },
  { path: '/resources/:id', redirect: (to) => `/people/${to.params.id}` },
  { path: '/allocations', redirect: '/schedule' },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  // Opening the person drawer must not yank the grid back to the top.
  scrollBehavior: (to, from) => (to.path.startsWith('/people') && from.path.startsWith('/people') ? false : { top: 0 }),
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · SRA-RMS` : 'SRA-RMS'
})
