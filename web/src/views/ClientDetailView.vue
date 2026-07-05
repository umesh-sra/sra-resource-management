<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { clientsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { ClientDetail } from '@/types'
import { fmtDate, fmtMoney, initials, projectStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'

const route = useRoute()
const router = useRouter()
const toast = useToastStore()

const client = ref<ClientDetail | null>(null)
const loading = ref(true)

const showEdit = ref(false)
const editName = ref('')
const savingEdit = ref(false)

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
  if (!client.value) return
  const hasProjects = client.value.projects.length > 0
  const msg = hasProjects
    ? `Delete "${client.value.name}" and its ${client.value.projects.length} project(s) and allocations?`
    : `Delete "${client.value.name}"?`
  if (!confirm(msg)) return
  try {
    await clientsApi.remove(client.value.id, hasProjects)
    toast.success('Client deleted')
    router.push('/clients')
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not delete client')
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div v-if="loading" class="card card-pad"><span class="sr-only" role="status">Loading client…</span><div class="skeleton" style="height: 60px" /></div>

    <template v-else-if="client">
      <div class="page-header">
        <div>
          <RouterLink to="/clients" class="muted"><span aria-hidden="true">← </span>Back to clients</RouterLink>
          <h1 style="margin-top: 4px">{{ client.name }}</h1>
          <div class="subtitle">{{ client.projects.length }} project(s) · {{ client.team.length }} people on the team</div>
        </div>
        <div class="row">
          <button class="btn" @click="openEdit">Edit</button>
          <button class="btn btn-danger" @click="remove">Delete</button>
        </div>
      </div>

      <div class="card">
        <div class="card-pad" style="padding-bottom: 8px"><h2>Projects</h2></div>
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th scope="col">Code</th><th scope="col">Name</th><th scope="col">Status</th><th scope="col">Dates</th><th scope="col" class="num">Budget</th></tr></thead>
            <tbody>
              <tr v-for="p in client.projects" :key="p.id" class="clickable" @click="router.push(`/projects/${p.id}`)">
                <td><RouterLink :to="`/projects/${p.id}`" class="row-link" @click.stop>{{ p.code }}</RouterLink></td>
                <td>{{ p.name }}</td>
                <td><span class="badge" :class="projectStatus(p.status).class">{{ projectStatus(p.status).label }}</span></td>
                <td>{{ fmtDate(p.startDate) }} – {{ fmtDate(p.endDate) }}</td>
                <td class="num">{{ fmtMoney(p.budget) }}</td>
              </tr>
              <tr v-if="!client.projects.length"><td colspan="5" class="empty">No projects yet.</td></tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="card">
        <div class="card-pad"><h2>Team</h2></div>
        <div class="card-pad" style="padding-top: 0">
          <div v-if="!client.team.length" class="muted">No resources allocated across this client's projects.</div>
          <div v-else class="row">
            <RouterLink
              v-for="m in client.team" :key="m.id" :to="`/resources/${m.id}`"
              class="member"
            >
              <span class="avatar" aria-hidden="true">{{ initials(m.name) }}</span>
              <span>
                <span class="member-name">{{ m.name }}</span>
                <span class="muted" style="display:block; font-size:12px">{{ m.primaryJobTitle }}</span>
              </span>
            </RouterLink>
          </div>
        </div>
      </div>
    </template>

    <ModalDialog v-if="showEdit" title="Edit client" @close="showEdit = false">
      <div class="field">
        <label for="edit-client-name">Client name</label>
        <input id="edit-client-name" class="input" v-model="editName" @keyup.enter="saveEdit" />
      </div>
      <template #footer>
        <button class="btn" @click="showEdit = false">Cancel</button>
        <button class="btn btn-primary" :disabled="savingEdit || !editName.trim()" @click="saveEdit">Save</button>
      </template>
    </ModalDialog>
  </div>
</template>

<style scoped>
.member { display: flex; align-items: center; gap: 10px; padding: 8px 12px; border: 1px solid var(--border); border-radius: var(--radius-sm); background: #fff; }
.member:hover { background: var(--gray-50); text-decoration: none; }
.member .avatar { width: 34px; height: 34px; border-radius: 50%; background: var(--brand-100); color: var(--brand-700); display: grid; place-items: center; font-weight: 700; font-size: 12px; }
.member-name { font-weight: 600; color: var(--text); }
</style>
