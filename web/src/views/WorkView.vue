<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { clientsApi, projectsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Client, Page, Project, ProjectStatus } from '@/types'
import { fmtDateShort, fmtMoney, projectStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import AvatarStack from '@/components/AvatarStack.vue'
import ModalDialog from '@/components/ModalDialog.vue'
import PagerBar from '@/components/PagerBar.vue'
import ProjectFormModal from '@/components/ProjectFormModal.vue'

/**
 * Projects & Clients (screens/projects and clients.png): one screen, two tabs,
 * each a dense sortable table. The tab is carried by the route so both halves
 * stay linkable.
 */
const route = useRoute()
const router = useRouter()
const toast = useToastStore()

const tab = computed<'projects' | 'clients'>(() => (route.path.startsWith('/clients') ? 'clients' : 'projects'))

const projects = ref<Page<Project> | null>(null)
const clients = ref<Page<Client> | null>(null)
const loading = ref(true)
const q = ref('')
const statusFilter = ref<'' | ProjectStatus>('')
const page = ref(1)
const sort = ref('name')
let timer: ReturnType<typeof setTimeout>

const showNewProject = ref(false)
const showNewClient = ref(false)
const newClientName = ref('')
const savingClient = ref(false)

/** Total counts for the tab labels — kept even while the other tab is showing. */
const counts = ref<{ projects: number | null; clients: number | null }>({ projects: null, clients: null })

async function load() {
  loading.value = true
  try {
    if (tab.value === 'projects') {
      const data = await projectsApi.list({
        q: q.value || undefined,
        status: statusFilter.value || undefined,
        page: page.value, pageSize: 25, sort: sort.value,
      })
      projects.value = data
      counts.value.projects = data.meta.totalItems
    } else {
      // The clients endpoint sorts on name and createdAt only.
      const clientSort = /^-?(name|createdAt)$/.test(sort.value) ? sort.value : 'name'
      const data = await clientsApi.list({
        q: q.value || undefined,
        page: page.value, pageSize: 25, sort: clientSort,
      })
      clients.value = data
      counts.value.clients = data.meta.totalItems
    }
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load')
  } finally {
    loading.value = false
  }
}

/** Prime whichever count the current tab did not fetch, so both tabs read right. */
async function primeCounts() {
  try {
    if (counts.value.clients === null) counts.value.clients = (await clientsApi.list({ pageSize: 1 })).meta.totalItems
    if (counts.value.projects === null) counts.value.projects = (await projectsApi.list({ pageSize: 1 })).meta.totalItems
  } catch {
    // Counts are decoration; the tables carry the real data.
  }
}

watch(tab, () => { page.value = 1; sort.value = 'name'; q.value = ''; statusFilter.value = ''; load() })

function onFilter() {
  clearTimeout(timer)
  timer = setTimeout(() => { page.value = 1; load() }, 300)
}
function goPage(p: number) { page.value = p; load() }

/** Header click cycles ascending → descending on that field. */
function sortBy(field: string) {
  sort.value = sort.value === field ? `-${field}` : field
  page.value = 1
  load()
}
const sortArrow = (field: string) =>
  sort.value === field ? '↑' : sort.value === `-${field}` ? '↓' : ''

async function createClient() {
  if (!newClientName.value.trim()) return
  savingClient.value = true
  try {
    const c = await clientsApi.create({ name: newClientName.value.trim() })
    toast.success('Client created')
    showNewClient.value = false
    newClientName.value = ''
    router.push(`/clients/${c.id}`)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not create client')
  } finally {
    savingClient.value = false
  }
}

onMounted(async () => { await load(); primeCounts() })
</script>

<template>
  <div class="toolbar">
    <h1 class="sr-only">Projects &amp; Clients</h1>
    <div class="tabs">
      <RouterLink to="/projects" class="tab" :class="{ active: tab === 'projects' }">
        Projects <span class="count">({{ counts.projects ?? '…' }})</span>
      </RouterLink>
      <RouterLink to="/clients" class="tab" :class="{ active: tab === 'clients' }">
        Clients <span class="count">({{ counts.clients ?? '…' }})</span>
      </RouterLink>
    </div>

    <label class="search">
      <span class="sr-only">{{ tab === 'projects' ? 'Search projects by name or code' : 'Search clients' }}</span>
      <input class="input" type="search" v-model="q" @input="onFilter" placeholder="Search" />
      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M10 4a6 6 0 1 1 0 12 6 6 0 0 1 0-12Zm10 16-5.2-5.2" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" /></svg>
    </label>

    <select v-if="tab === 'projects'" class="select" style="max-width: 160px" v-model="statusFilter" @change="onFilter" aria-label="Filter by status">
      <option value="">All statuses</option>
      <option value="planned">Planned</option>
      <option value="active">Active</option>
      <option value="onHold">On hold</option>
      <option value="completed">Completed</option>
      <option value="cancelled">Cancelled</option>
    </select>

    <div class="spacer" />

    <RouterLink v-if="tab === 'projects'" class="btn" to="/gantt">Gantt charts</RouterLink>

    <button class="btn btn-accent btn-pill" @click="tab === 'projects' ? (showNewProject = true) : (showNewClient = true)">
      <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><path d="M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Zm1 8h4v2h-4v4h-2v-4H7v-2h4V7h2v4Z" fill="currentColor" /></svg>
      New
    </button>
  </div>

  <div class="page">
    <!-- Projects ------------------------------------------------------- -->
    <div v-if="tab === 'projects'" class="card">
      <div class="table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th scope="col"><button class="th-sort" @click="sortBy('name')">Project Name <span class="arrow">{{ sortArrow('name') }}</span></button></th>
              <th scope="col">Client Name</th>
              <th scope="col">Gantt</th>
              <th scope="col"><button class="th-sort" @click="sortBy('startDate')">Start Date <span class="arrow">{{ sortArrow('startDate') }}</span></button></th>
              <th scope="col"><button class="th-sort" @click="sortBy('endDate')">End Date <span class="arrow">{{ sortArrow('endDate') }}</span></button></th>
              <th scope="col" class="num">Budget</th>
              <th scope="col" class="num">Remaining</th>
              <th scope="col"><button class="th-sort" @click="sortBy('code')">Project Code <span class="arrow">{{ sortArrow('code') }}</span></button></th>
              <th scope="col">Billable</th>
              <th scope="col"><button class="th-sort" @click="sortBy('status')">Status <span class="arrow">{{ sortArrow('status') }}</span></button></th>
              <th scope="col">Team</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td colspan="11"><span class="sr-only" role="status">Loading projects…</span><div class="skeleton" style="height: 20px; margin: 6px 0" /></td></tr>
            <template v-else>
              <tr v-for="p in projects?.items" :key="p.id" class="clickable" @click="router.push(`/projects/${p.id}`)">
                <td>
                  <RouterLink :to="`/projects/${p.id}`" class="row-link" @click.stop>
                    <strong class="truncate">{{ p.name }}</strong>
                  </RouterLink>
                </td>
                <td><RouterLink :to="`/clients/${p.clientId}`" class="row-link truncate" @click.stop>{{ p.clientName }}</RouterLink></td>
                <td @click.stop>
                  <RouterLink class="icon-btn plain" :to="`/gantt?clientId=${p.clientId}`" :aria-label="`Open ${p.name} on the Gantt chart`">
                    <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Zm1 8h4v2h-4v4h-2v-4H7v-2h4V7h2v4Z" fill="currentColor" /></svg>
                  </RouterLink>
                </td>
                <td>{{ fmtDateShort(p.startDate) }}</td>
                <td>{{ fmtDateShort(p.endDate) }}</td>
                <td class="num">{{ fmtMoney(p.budget) }}</td>
                <td class="num">{{ fmtMoney(p.remaining) }}</td>
                <td>{{ p.code }}</td>
                <td>{{ p.billable ? 'Yes' : 'No' }}</td>
                <td><span class="badge" :class="projectStatus(p.status).class">{{ projectStatus(p.status).label }}</span></td>
                <td @click.stop><AvatarStack :people="p.team" /></td>
              </tr>
              <tr v-if="projects && !projects.items.length"><td colspan="11" class="empty">No projects found.</td></tr>
            </template>
          </tbody>
        </table>
      </div>
      <PagerBar v-if="projects" :meta="projects.meta" @change="goPage" />
    </div>

    <!-- Clients -------------------------------------------------------- -->
    <div v-else class="card">
      <div class="table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th scope="col"><button class="th-sort" @click="sortBy('name')">Client Name <span class="arrow">{{ sortArrow('name') }}</span></button></th>
              <th scope="col" class="num">Projects</th>
              <th scope="col"><button class="th-sort" @click="sortBy('createdAt')">Created <span class="arrow">{{ sortArrow('createdAt') }}</span></button></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td colspan="3"><span class="sr-only" role="status">Loading clients…</span><div class="skeleton" style="height: 20px; margin: 6px 0" /></td></tr>
            <template v-else>
              <tr v-for="c in clients?.items" :key="c.id" class="clickable" @click="router.push(`/clients/${c.id}`)">
                <td><RouterLink :to="`/clients/${c.id}`" class="row-link" @click.stop><strong>{{ c.name }}</strong></RouterLink></td>
                <td class="num">{{ c.projectCount }}</td>
                <td>{{ fmtDateShort(c.createdAt) }}</td>
              </tr>
              <tr v-if="clients && !clients.items.length"><td colspan="3" class="empty">No clients found.</td></tr>
            </template>
          </tbody>
        </table>
      </div>
      <PagerBar v-if="clients" :meta="clients.meta" @change="goPage" />
    </div>
  </div>

  <ProjectFormModal
    v-if="showNewProject"
    @close="showNewProject = false"
    @saved="(p) => { showNewProject = false; router.push(`/projects/${p.id}`) }"
  />

  <ModalDialog v-if="showNewClient" title="New Client" @close="showNewClient = false">
    <div class="field">
      <label for="nc-name">Client name *</label>
      <input id="nc-name" class="input" v-model="newClientName" placeholder="e.g. Department of Education" @keyup.enter="createClient" />
    </div>
    <template #footer>
      <button class="btn" @click="showNewClient = false">Cancel</button>
      <button class="btn btn-accent btn-pill" :disabled="savingClient || !newClientName.trim()" @click="createClient">Add Client</button>
    </template>
  </ModalDialog>
</template>
