<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Page, Resource, ResourceStatus } from '@/types'
import { initials, resourceStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'
import PagerBar from '@/components/PagerBar.vue'

const router = useRouter()
const toast = useToastStore()

const data = ref<Page<Resource> | null>(null)
const loading = ref(true)
const q = ref('')
const skillFilter = ref('')
const statusFilter = ref<'' | ResourceStatus>('')
const page = ref(1)
let timer: ReturnType<typeof setTimeout>

const showCreate = ref(false)
const saving = ref(false)
const form = ref({
  name: '', email: '', primaryJobTitle: '', department: '', location: '',
  availabilityHoursPerWeek: 38, status: 'active' as ResourceStatus, skills: '',
})

async function load() {
  loading.value = true
  try {
    const skills = skillFilter.value.split(',').map((s) => s.trim()).filter(Boolean)
    data.value = await resourcesApi.list({
      q: q.value || undefined,
      skill: skills.length ? skills : undefined,
      status: statusFilter.value || undefined,
      page: page.value, pageSize: 25, sort: 'name',
    })
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load resources')
  } finally {
    loading.value = false
  }
}

function onFilter() {
  clearTimeout(timer)
  timer = setTimeout(() => { page.value = 1; load() }, 300)
}
function goPage(p: number) { page.value = p; load() }

async function create() {
  saving.value = true
  try {
    const f = form.value
    const r = await resourcesApi.create({
      name: f.name.trim(), email: f.email.trim(), primaryJobTitle: f.primaryJobTitle.trim(),
      department: f.department || undefined, location: f.location || undefined,
      availabilityHoursPerWeek: Number(f.availabilityHoursPerWeek), status: f.status,
      skills: f.skills.split(',').map((s) => s.trim()).filter(Boolean),
    })
    toast.success('Resource created')
    showCreate.value = false
    router.push(`/resources/${r.id}`)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not create resource')
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
        <h1>Resources</h1>
        <div class="subtitle">People, their skills and weekly availability.</div>
      </div>
      <button class="btn btn-primary" @click="showCreate = true">+ New resource</button>
    </div>

    <div class="card">
      <div class="card-pad row">
        <input class="input" style="max-width: 240px" v-model="q" @input="onFilter" placeholder="Search name or email…" aria-label="Search resources by name or email" type="search" />
        <input class="input" style="max-width: 220px" v-model="skillFilter" @input="onFilter" placeholder="Skills (comma, AND)…" aria-label="Filter by skills, comma-separated" />
        <select class="select" style="max-width: 160px" v-model="statusFilter" @change="onFilter" aria-label="Filter by status">
          <option value="">All statuses</option>
          <option value="active">Active</option>
          <option value="inactive">Inactive</option>
          <option value="onLeave">On leave</option>
        </select>
      </div>
      <div class="table-wrap">
        <table class="table">
          <thead><tr><th scope="col">Name</th><th scope="col">Job title</th><th scope="col">Department</th><th scope="col">Skills</th><th scope="col" class="num">Avail (h/wk)</th><th scope="col">Status</th></tr></thead>
          <tbody>
            <tr v-if="loading"><td colspan="6"><span class="sr-only" role="status">Loading resources…</span><div class="skeleton" style="height: 20px; margin: 6px 0" /></td></tr>
            <template v-else>
              <tr v-for="r in data?.items" :key="r.id" class="clickable" @click="router.push(`/resources/${r.id}`)">
                <td>
                  <div class="row" style="gap: 9px">
                    <span class="avatar" aria-hidden="true">{{ initials(r.name) }}</span>
                    <RouterLink :to="`/resources/${r.id}`" class="row-link" @click.stop><strong>{{ r.name }}</strong></RouterLink>
                  </div>
                </td>
                <td>{{ r.primaryJobTitle }}</td>
                <td>{{ r.department ?? '—' }}</td>
                <td><span v-for="s in r.skills.slice(0, 3)" :key="s" class="chip">{{ s }}</span><span v-if="r.skills.length > 3" class="muted">+{{ r.skills.length - 3 }}</span></td>
                <td class="num">{{ r.availabilityHoursPerWeek }}</td>
                <td><span class="badge" :class="resourceStatus(r.status).class">{{ resourceStatus(r.status).label }}</span></td>
              </tr>
              <tr v-if="data && !data.items.length"><td colspan="6" class="empty">No resources found.</td></tr>
            </template>
          </tbody>
        </table>
      </div>
      <PagerBar v-if="data" :meta="data.meta" @change="goPage" />
    </div>

    <ModalDialog v-if="showCreate" title="New resource" @close="showCreate = false">
      <div class="form-row">
        <div class="field"><label for="nr-name">Name</label><input id="nr-name" class="input" v-model="form.name" /></div>
        <div class="field"><label for="nr-email">Email</label><input id="nr-email" class="input" v-model="form.email" type="email" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label for="nr-title">Primary job title</label><input id="nr-title" class="input" v-model="form.primaryJobTitle" /></div>
        <div class="field"><label for="nr-avail">Availability (h/week)</label><input id="nr-avail" class="input" v-model.number="form.availabilityHoursPerWeek" type="number" min="0" max="168" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label for="nr-dept">Department</label><input id="nr-dept" class="input" v-model="form.department" /></div>
        <div class="field"><label for="nr-loc">Location</label><input id="nr-loc" class="input" v-model="form.location" /></div>
      </div>
      <div class="field"><label for="nr-skills">Skills (comma-separated)</label><input id="nr-skills" class="input" v-model="form.skills" placeholder="C#, Vue.js, PostgreSQL" /></div>
      <div class="field"><label for="nr-status">Status</label>
        <select id="nr-status" class="select" v-model="form.status">
          <option value="active">Active</option><option value="inactive">Inactive</option><option value="onLeave">On leave</option>
        </select>
      </div>
      <template #footer>
        <button class="btn" @click="showCreate = false">Cancel</button>
        <button class="btn btn-primary" :disabled="saving || !form.name || !form.email || !form.primaryJobTitle" @click="create">Create</button>
      </template>
    </ModalDialog>
  </div>
</template>

<style scoped>
.avatar { width: 30px; height: 30px; border-radius: 50%; background: var(--brand-100); color: var(--brand-700); display: grid; place-items: center; font-weight: 700; font-size: 11.5px; }
</style>
