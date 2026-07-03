<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { clientsApi, projectsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Client, Page, Project, ProjectStatus } from '@/types'
import { fmtDate, fmtMoney, projectStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'
import PagerBar from '@/components/PagerBar.vue'

const router = useRouter()
const toast = useToastStore()

const data = ref<Page<Project> | null>(null)
const loading = ref(true)
const q = ref('')
const statusFilter = ref<'' | ProjectStatus>('')
const page = ref(1)
let timer: ReturnType<typeof setTimeout>

const clients = ref<Client[]>([])
const showCreate = ref(false)
const saving = ref(false)
const form = ref({
  name: '', code: '', clientId: '', startDate: '', endDate: '',
  budget: undefined as number | undefined, billable: true, status: 'planned' as ProjectStatus,
})

async function load() {
  loading.value = true
  try {
    data.value = await projectsApi.list({
      q: q.value || undefined, status: statusFilter.value || undefined,
      page: page.value, pageSize: 25, sort: 'name',
    })
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load projects')
  } finally {
    loading.value = false
  }
}
function onFilter() { clearTimeout(timer); timer = setTimeout(() => { page.value = 1; load() }, 300) }
function goPage(p: number) { page.value = p; load() }

async function openCreate() {
  showCreate.value = true
  if (!clients.value.length) clients.value = (await clientsApi.list({ pageSize: 200, sort: 'name' })).items
}

async function create() {
  saving.value = true
  try {
    const f = form.value
    const p = await projectsApi.create({
      name: f.name.trim(), code: f.code.trim(), clientId: f.clientId,
      startDate: f.startDate, endDate: f.endDate,
      budget: f.budget != null ? Number(f.budget) : undefined,
      billable: f.billable, status: f.status,
    })
    toast.success('Project created')
    showCreate.value = false
    router.push(`/projects/${p.id}`)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not create project')
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Projects</h1>
        <div class="subtitle">Client engagements, timelines and budgets.</div>
      </div>
      <button class="btn btn-primary" @click="openCreate">+ New project</button>
    </div>

    <div class="card">
      <div class="card-pad row">
        <input class="input" style="max-width: 260px" v-model="q" @input="onFilter" placeholder="Search name or code…" />
        <select class="select" style="max-width: 170px" v-model="statusFilter" @change="onFilter">
          <option value="">All statuses</option>
          <option value="planned">Planned</option><option value="active">Active</option>
          <option value="onHold">On hold</option><option value="completed">Completed</option>
          <option value="cancelled">Cancelled</option>
        </select>
      </div>
      <div class="table-wrap">
        <table class="table">
          <thead><tr><th>Code</th><th>Name</th><th>Client</th><th>Status</th><th>Dates</th><th class="num">Budget</th><th>Billable</th></tr></thead>
          <tbody>
            <tr v-if="loading"><td colspan="7"><div class="skeleton" style="height: 20px; margin: 6px 0" /></td></tr>
            <template v-else>
              <tr v-for="p in data?.items" :key="p.id" class="clickable" @click="router.push(`/projects/${p.id}`)">
                <td><strong>{{ p.code }}</strong></td>
                <td>{{ p.name }}</td>
                <td>{{ p.clientName }}</td>
                <td><span class="badge" :class="projectStatus(p.status).class">{{ projectStatus(p.status).label }}</span></td>
                <td>{{ fmtDate(p.startDate) }} – {{ fmtDate(p.endDate) }}</td>
                <td class="num">{{ fmtMoney(p.budget) }}</td>
                <td><span class="badge" :class="p.billable ? 'green' : 'gray'">{{ p.billable ? 'Yes' : 'No' }}</span></td>
              </tr>
              <tr v-if="data && !data.items.length"><td colspan="7" class="empty">No projects found.</td></tr>
            </template>
          </tbody>
        </table>
      </div>
      <PagerBar v-if="data" :meta="data.meta" @change="goPage" />
    </div>

    <ModalDialog v-if="showCreate" title="New project" @close="showCreate = false">
      <div class="form-row">
        <div class="field"><label>Name</label><input class="input" v-model="form.name" /></div>
        <div class="field"><label>Code</label><input class="input" v-model="form.code" placeholder="ACME-001" /></div>
      </div>
      <div class="field"><label>Client</label>
        <select class="select" v-model="form.clientId">
          <option value="" disabled>Select a client…</option>
          <option v-for="c in clients" :key="c.id" :value="c.id">{{ c.name }}</option>
        </select>
      </div>
      <div class="form-row">
        <div class="field"><label>Start date</label><input class="input" v-model="form.startDate" type="date" /></div>
        <div class="field"><label>End date</label><input class="input" v-model="form.endDate" type="date" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label>Budget (AUD)</label><input class="input" v-model.number="form.budget" type="number" min="0" /></div>
        <div class="field"><label>Status</label>
          <select class="select" v-model="form.status">
            <option value="planned">Planned</option><option value="active">Active</option>
            <option value="onHold">On hold</option><option value="completed">Completed</option><option value="cancelled">Cancelled</option>
          </select>
        </div>
      </div>
      <div class="field"><label><input type="checkbox" v-model="form.billable" /> Billable</label></div>
      <template #footer>
        <button class="btn" @click="showCreate = false">Cancel</button>
        <button class="btn btn-primary" :disabled="saving || !form.name || !form.code || !form.clientId || !form.startDate || !form.endDate" @click="create">Create</button>
      </template>
    </ModalDialog>
  </div>
</template>
