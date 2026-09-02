<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { clientsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { ClientDetail } from '@/types'
import { fmtDate, fmtMoney, projectStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'
import AppAvatar from '@/components/AppAvatar.vue'

const route = useRoute()
const router = useRouter()
const toast = useToastStore()

const client = ref<ClientDetail | null>(null)
const loading = ref(true)

const showEdit = ref(false)
const editName = ref('')
const savingEdit = ref(false)

const totals = computed(() => {
  const ps = client.value?.projects ?? []
  return {
    budget: ps.reduce((s, p) => s + (p.budget ?? 0), 0),
    remaining: ps.reduce((s, p) => s + (p.remaining ?? 0), 0),
    active: ps.filter((p) => p.status === 'active').length,
  }
})

function openEdit() {
  if (!client.value) return
  editName.value = client.value.name
  showEdit.value = true
}

async function saveEdit() {
  if (!client.value || !editName.value.trim()) return
  savingEdit.value = true
  try {
    await clientsApi.update(client.value.id, { name: editName.value.trim() })
    toast.success('Client updated')
    showEdit.value = false
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not update client')
  } finally {
    savingEdit.value = false
  }
}

async function load() {
  loading.value = true
  try {
    client.value = await clientsApi.get(route.params.id as string)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load client')
  } finally {
    loading.value = false
  }
}

async function remove() {
  const c = client.value
  if (!c) return
  const hasProjects = c.projects.length > 0
  if (!confirm(hasProjects
    ? `Delete "${c.name}" and its ${c.projects.length} project(s) and allocations?`
    : `Delete "${c.name}"?`)) return
  try {
    await clientsApi.remove(c.id, hasProjects)
    toast.success('Client deleted')
    router.push('/clients')
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not delete client')
  }
}

onMounted(load)
</script>

<template>
  <div class="toolbar">
    <RouterLink class="icon-btn plain" to="/clients" aria-label="Back to clients">
      <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" /></svg>
    </RouterLink>
    <h1 class="ttl">{{ client?.name ?? 'Client' }}</h1>
    <div class="spacer" />
    <RouterLink v-if="client" class="btn" :to="`/gantt?clientId=${client.id}`">Gantt</RouterLink>
    <button class="btn" :disabled="!client" @click="openEdit">Edit</button>
    <button class="btn btn-danger" :disabled="!client" @click="remove">Delete</button>
  </div>

  <div class="page page-narrow">
    <div v-if="loading" class="card card-pad"><span class="sr-only" role="status">Loading client…</span><div class="skeleton" style="height: 90px" /></div>

    <template v-else-if="client">
      <div class="figures">
        <div><span class="fig-label">Projects</span><span class="fig-value">{{ client.projects.length }}</span></div>
        <div><span class="fig-label">Active</span><span class="fig-value">{{ totals.active }}</span></div>
        <div><span class="fig-label">People</span><span class="fig-value">{{ client.team.length }}</span></div>
        <div><span class="fig-label">Total budget</span><span class="fig-value">{{ fmtMoney(totals.budget) }}</span></div>
        <div><span class="fig-label">Remaining</span><span class="fig-value">{{ fmtMoney(totals.remaining) }}</span></div>
      </div>

      <div class="card">
        <div class="card-pad" style="padding-bottom: 8px"><h2>Projects</h2></div>
        <div class="table-wrap">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Project Name</th><th scope="col">Project Code</th><th scope="col">Status</th>
                <th scope="col">Start Date</th><th scope="col">End Date</th>
                <th scope="col" class="num">Budget</th><th scope="col" class="num">Remaining</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="p in client.projects" :key="p.id" class="clickable" @click="router.push(`/projects/${p.id}`)">
                <td><RouterLink :to="`/projects/${p.id}`" class="row-link" @click.stop><strong>{{ p.name }}</strong></RouterLink></td>
                <td>{{ p.code }}</td>
                <td><span class="badge" :class="projectStatus(p.status).class">{{ projectStatus(p.status).label }}</span></td>
                <td>{{ fmtDate(p.startDate) }}</td>
                <td>{{ fmtDate(p.endDate) }}</td>
                <td class="num">{{ fmtMoney(p.budget) }}</td>
                <td class="num">{{ fmtMoney(p.remaining) }}</td>
              </tr>
              <tr v-if="!client.projects.length"><td colspan="7" class="empty">No projects yet.</td></tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="card">
        <div class="card-pad"><h2>Team</h2></div>
        <div class="card-pad" style="padding-top: 0">
          <p v-if="!client.team.length" class="muted">No one is allocated across this client's projects.</p>
          <div v-else class="row">
            <RouterLink v-for="m in client.team" :key="m.id" :to="`/people/${m.id}`" class="member">
              <AppAvatar :name="m.name" :image-url="m.imageUrl" :size="34" />
              <span>
                <span class="member-name">{{ m.name }}</span>
                <span class="muted" style="display: block; font-size: 12px">{{ m.primaryJobTitle }}</span>
              </span>
            </RouterLink>
          </div>
        </div>
      </div>
    </template>
  </div>

  <ModalDialog v-if="showEdit" title="Edit Client" @close="showEdit = false">
    <div class="field">
      <label for="edit-client-name">Client name *</label>
      <input id="edit-client-name" class="input" v-model="editName" @keyup.enter="saveEdit" />
    </div>
    <template #footer>
      <button class="btn" @click="showEdit = false">Cancel</button>
      <button class="btn btn-accent btn-pill" :disabled="savingEdit || !editName.trim()" @click="saveEdit">Save changes</button>
    </template>
  </ModalDialog>
</template>

<style scoped>
.ttl { font-size: 18px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.figures {
  display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius);
  margin-bottom: 16px; box-shadow: var(--shadow-sm);
}
.figures > div { padding: 16px 18px; border-left: 1px solid var(--border); }
.figures > div:first-child { border-left: 0; }
.fig-label { display: block; color: var(--text-muted); font-size: 12px; }
.fig-value { display: block; font-size: 22px; font-weight: 680; color: var(--brand-800); margin-top: 4px; }
.member { display: flex; align-items: center; gap: 10px; padding: 8px 12px; border: 1px solid var(--border); border-radius: var(--radius-sm); background: #fff; color: inherit; }
.member:hover { background: var(--gray-50); text-decoration: none; }
.member-name { font-weight: 600; color: var(--text); }
</style>
