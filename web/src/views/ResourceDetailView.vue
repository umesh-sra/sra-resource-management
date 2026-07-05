<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type { ResourceDetail, ResourceStatus } from '@/types'
import { fmtDate, initials, resourceStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'

const route = useRoute()
const router = useRouter()
const toast = useToastStore()

const resource = ref<ResourceDetail | null>(null)
const loading = ref(true)

const showEdit = ref(false)
const savingEdit = ref(false)
const editForm = ref({
  name: '', email: '', primaryJobTitle: '', secondaryJobTitle: '',
  availabilityHoursPerWeek: 38, status: 'active' as ResourceStatus,
  department: '', location: '', skills: '', notes: '',
})

function openEdit() {
  const r = resource.value
  if (!r) return
  editForm.value = {
    name: r.name, email: r.email, primaryJobTitle: r.primaryJobTitle,
    secondaryJobTitle: r.secondaryJobTitle ?? '',
    availabilityHoursPerWeek: r.availabilityHoursPerWeek, status: r.status,
    department: r.department ?? '', location: r.location ?? '',
    skills: r.skills.join(', '), notes: r.notes ?? '',
  }
  showEdit.value = true
}

async function saveEdit() {
  const r = resource.value
  if (!r) return
  savingEdit.value = true
  try {
    const f = editForm.value
    await resourcesApi.update(r.id, {
      name: f.name, email: f.email, primaryJobTitle: f.primaryJobTitle,
      secondaryJobTitle: f.secondaryJobTitle || undefined,
      availabilityHoursPerWeek: Number(f.availabilityHoursPerWeek),
      status: f.status,
      department: f.department || undefined, location: f.location || undefined,
      skills: f.skills.split(',').map((s) => s.trim()).filter(Boolean),
      notes: f.notes || undefined,
      workingDays: r.workingDays,
    })
    toast.success('Resource updated')
    showEdit.value = false
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not update resource')
  } finally {
    savingEdit.value = false
  }
}

const utilisation = computed(() => {
  if (!resource.value || resource.value.availabilityHoursPerWeek <= 0) return 0
  return resource.value.allocatedHoursPerWeek / resource.value.availabilityHoursPerWeek
})
const over = computed(() => utilisation.value > 1)

async function load() {
  loading.value = true
  try {
    resource.value = await resourcesApi.get(route.params.id as string)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load resource')
  } finally {
    loading.value = false
  }
}

async function remove() {
  if (!resource.value) return
  const hasAllocs = resource.value.allocations.length > 0
  if (!confirm(hasAllocs
    ? `Delete ${resource.value.name} and their ${resource.value.allocations.length} allocation(s)?`
    : `Delete ${resource.value.name}?`)) return
  try {
    await resourcesApi.remove(resource.value.id, hasAllocs)
    toast.success('Resource deleted')
    router.push('/resources')
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not delete resource')
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div v-if="loading" class="card card-pad"><span class="sr-only" role="status">Loading resource…</span><div class="skeleton" style="height: 60px" /></div>

    <template v-else-if="resource">
      <div class="page-header">
        <div>
          <RouterLink to="/resources" class="muted"><span aria-hidden="true">← </span>Back to resources</RouterLink>
          <div class="row" style="gap: 12px; margin-top: 6px">
            <span class="big-avatar" aria-hidden="true">{{ initials(resource.name) }}</span>
            <div>
              <h1>{{ resource.name }}</h1>
              <div class="subtitle">
                {{ resource.primaryJobTitle }}<span v-if="resource.secondaryJobTitle"> · {{ resource.secondaryJobTitle }}</span>
              </div>
            </div>
          </div>
        </div>
        <div class="row">
          <button class="btn" @click="openEdit">Edit</button>
          <button class="btn btn-danger" @click="remove">Delete</button>
        </div>
      </div>

      <div class="grid grid-2">
        <div class="card card-pad">
          <h2>Profile</h2>
          <dl class="defs">
            <div><dt>Email</dt><dd>{{ resource.email }}</dd></div>
            <div><dt>Status</dt><dd><span class="badge" :class="resourceStatus(resource.status).class">{{ resourceStatus(resource.status).label }}</span></dd></div>
            <div><dt>Department</dt><dd>{{ resource.department ?? '—' }}</dd></div>
            <div><dt>Location</dt><dd>{{ resource.location ?? '—' }}</dd></div>
            <div><dt>Working days</dt><dd>{{ resource.workingDays.length ? resource.workingDays.join(', ') : '—' }}</dd></div>
            <div><dt>Skills</dt><dd><span v-for="s in resource.skills" :key="s" class="chip">{{ s }}</span><span v-if="!resource.skills.length">—</span></dd></div>
          </dl>
        </div>

        <div class="card card-pad">
          <h2>Capacity</h2>
          <div class="cap-num" :style="{ color: over ? 'var(--red-600)' : 'var(--brand-800)' }">
            {{ resource.allocatedHoursPerWeek }} / {{ resource.availabilityHoursPerWeek }} h&nbsp;<span class="muted" style="font-size:14px">per week</span>
          </div>
          <div class="ubar" :class="{ over }" style="margin: 12px 0 8px" aria-hidden="true">
            <span :style="{ width: Math.min(utilisation * 100, 100) + '%' }" />
          </div>
          <div :class="over ? 'warn-text' : 'muted'">
            {{ Math.round(utilisation * 100) }}% utilised<span v-if="over"> — over-allocated</span>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="card-pad" style="padding-bottom: 8px"><h2>Allocations</h2></div>
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th scope="col">Project</th><th scope="col">Role</th><th scope="col">Dates</th><th scope="col" class="num">Effort</th><th scope="col">Billable</th></tr></thead>
            <tbody>
              <tr v-for="a in resource.allocations" :key="a.id" class="clickable" @click="router.push(`/projects/${a.projectId}`)">
                <td><RouterLink :to="`/projects/${a.projectId}`" class="row-link" @click.stop>{{ a.projectName }}</RouterLink></td>
                <td>{{ a.roleOnProject ?? '—' }}</td>
                <td>{{ fmtDate(a.startDate) }} – {{ fmtDate(a.endDate) }}</td>
                <td class="num">{{ a.effort }} {{ a.effortUnit === 'percent' ? '%' : 'h/wk' }}</td>
                <td><span class="badge" :class="a.billable ? 'green' : 'gray'">{{ a.billable ? 'Billable' : 'Non-billable' }}</span></td>
              </tr>
              <tr v-if="!resource.allocations.length"><td colspan="5" class="empty">No allocations.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </template>

    <ModalDialog v-if="showEdit" title="Edit resource" @close="showEdit = false">
      <div class="form-row">
        <div class="field"><label for="er-name">Name</label><input id="er-name" class="input" v-model="editForm.name" /></div>
        <div class="field"><label for="er-email">Email</label><input id="er-email" class="input" v-model="editForm.email" type="email" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label for="er-title1">Primary job title</label><input id="er-title1" class="input" v-model="editForm.primaryJobTitle" /></div>
        <div class="field"><label for="er-title2">Secondary job title</label><input id="er-title2" class="input" v-model="editForm.secondaryJobTitle" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label for="er-avail">Availability (h/week)</label><input id="er-avail" class="input" v-model.number="editForm.availabilityHoursPerWeek" type="number" min="0" max="168" /></div>
        <div class="field"><label for="er-status">Status</label>
          <select id="er-status" class="select" v-model="editForm.status">
            <option value="active">Active</option><option value="inactive">Inactive</option>
            <option value="onLeave">On leave</option>
          </select>
        </div>
      </div>
      <div class="form-row">
        <div class="field"><label for="er-dept">Department</label><input id="er-dept" class="input" v-model="editForm.department" /></div>
        <div class="field"><label for="er-loc">Location</label><input id="er-loc" class="input" v-model="editForm.location" /></div>
      </div>
      <div class="field"><label for="er-skills">Skills (comma-separated)</label><input id="er-skills" class="input" v-model="editForm.skills" /></div>
      <div class="field"><label for="er-notes">Notes</label><input id="er-notes" class="input" v-model="editForm.notes" /></div>
      <template #footer>
        <button class="btn" @click="showEdit = false">Cancel</button>
        <button class="btn btn-primary" :disabled="savingEdit || !editForm.name.trim() || !editForm.email.trim() || !editForm.primaryJobTitle.trim()" @click="saveEdit">Save</button>
      </template>
    </ModalDialog>
  </div>
</template>

<style scoped>
.big-avatar { width: 52px; height: 52px; border-radius: 50%; background: var(--brand-100); color: var(--brand-700); display: grid; place-items: center; font-weight: 700; font-size: 18px; }
.defs { display: grid; gap: 10px; margin: 12px 0 0; }
.defs > div { display: grid; grid-template-columns: 130px 1fr; align-items: start; }
.defs dt { color: var(--text-muted); font-size: 13px; }
.defs dd { margin: 0; }
.cap-num { font-size: 26px; font-weight: 680; margin-top: 10px; }
</style>
