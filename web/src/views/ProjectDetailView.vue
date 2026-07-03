<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { projectsApi, resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type { EffortUnit, ProjectDetail, Resource } from '@/types'
import { fmtDate, fmtMoney, projectStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'

const route = useRoute()
const router = useRouter()
const toast = useToastStore()

const project = ref<ProjectDetail | null>(null)
const loading = ref(true)

const resources = ref<Resource[]>([])
const showAdd = ref(false)
const saving = ref(false)
const form = ref({
  resourceId: '', startDate: '', endDate: '',
  effort: 38, effortUnit: 'hoursPerWeek' as EffortUnit, roleOnProject: '',
})

async function load() {
  loading.value = true
  try {
    project.value = await projectsApi.get(route.params.id as string)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load project')
  } finally {
    loading.value = false
  }
}

async function openAdd() {
  if (project.value) {
    form.value.startDate = project.value.startDate
    form.value.endDate = project.value.endDate
  }
  showAdd.value = true
  if (!resources.value.length) resources.value = (await resourcesApi.list({ pageSize: 200, sort: 'name' })).items
}

async function addAllocation() {
  if (!project.value) return
  saving.value = true
  try {
    const f = form.value
    const a = await projectsApi.createAllocation(project.value.id, {
      resourceId: f.resourceId, startDate: f.startDate, endDate: f.endDate,
      effort: Number(f.effort), effortUnit: f.effortUnit, roleOnProject: f.roleOnProject || undefined,
    })
    if (a.warnings?.length) toast.error(a.warnings[0]!)
    else toast.success('Allocation added')
    showAdd.value = false
    form.value.resourceId = ''; form.value.roleOnProject = ''
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not add allocation')
  } finally {
    saving.value = false
  }
}

async function removeProject() {
  if (!project.value) return
  const has = project.value.allocations.length > 0
  if (!confirm(has
    ? `Delete project ${project.value.code} and its ${project.value.allocations.length} allocation(s)?`
    : `Delete project ${project.value.code}?`)) return
  try {
    await projectsApi.remove(project.value.id, has)
    toast.success('Project deleted')
    router.push('/projects')
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not delete project')
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div v-if="loading" class="card card-pad"><div class="skeleton" style="height: 60px" /></div>

    <template v-else-if="project">
      <div class="page-header">
        <div>
          <RouterLink to="/projects" class="muted">← Projects</RouterLink>
          <h1 style="margin-top: 4px">{{ project.code }} — {{ project.name }}</h1>
          <div class="subtitle">
            <RouterLink :to="`/clients/${project.clientId}`">{{ project.clientName }}</RouterLink>
            · <span class="badge" :class="projectStatus(project.status).class">{{ projectStatus(project.status).label }}</span>
          </div>
        </div>
        <div class="row">
          <button class="btn btn-primary" @click="openAdd">+ Allocate resource</button>
          <button class="btn btn-danger" @click="removeProject">Delete</button>
        </div>
      </div>

      <div class="grid grid-stats">
        <div class="card stat"><div class="label">Dates</div><div class="value" style="font-size:16px; margin-top:10px">{{ fmtDate(project.startDate) }} – {{ fmtDate(project.endDate) }}</div></div>
        <div class="card stat"><div class="label">Budget</div><div class="value">{{ fmtMoney(project.budget) }}</div></div>
        <div class="card stat"><div class="label">Remaining</div><div class="value">{{ fmtMoney(project.remaining) }}</div></div>
        <div class="card stat"><div class="label">Billable</div><div class="value">{{ project.billable ? 'Yes' : 'No' }}</div></div>
      </div>

      <div class="card" style="margin-top: 16px">
        <div class="card-pad" style="padding-bottom: 8px"><h2>Team & allocations</h2></div>
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th>Resource</th><th>Role</th><th>Dates</th><th class="num">Effort</th><th>Billable</th></tr></thead>
            <tbody>
              <tr v-for="a in project.allocations" :key="a.id" class="clickable" @click="router.push(`/resources/${a.resourceId}`)">
                <td>{{ a.resourceName }}</td>
                <td>{{ a.roleOnProject ?? '—' }}</td>
                <td>{{ fmtDate(a.startDate) }} – {{ fmtDate(a.endDate) }}</td>
                <td class="num">{{ a.effort }} {{ a.effortUnit === 'percent' ? '%' : 'h/wk' }}</td>
                <td><span class="badge" :class="a.billable ? 'green' : 'gray'">{{ a.billable ? 'Billable' : 'Non-billable' }}</span></td>
              </tr>
              <tr v-if="!project.allocations.length"><td colspan="5" class="empty">No one allocated yet.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </template>

    <ModalDialog v-if="showAdd" title="Allocate a resource" @close="showAdd = false">
      <div class="field"><label>Resource</label>
        <select class="select" v-model="form.resourceId">
          <option value="" disabled>Select a resource…</option>
          <option v-for="r in resources" :key="r.id" :value="r.id">{{ r.name }} — {{ r.primaryJobTitle }}</option>
        </select>
      </div>
      <div class="form-row">
        <div class="field"><label>Start date</label><input class="input" v-model="form.startDate" type="date" /></div>
        <div class="field"><label>End date</label><input class="input" v-model="form.endDate" type="date" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label>Effort</label><input class="input" v-model.number="form.effort" type="number" min="0" /></div>
        <div class="field"><label>Unit</label>
          <select class="select" v-model="form.effortUnit">
            <option value="hoursPerWeek">Hours / week</option><option value="percent">Percent</option>
          </select>
        </div>
      </div>
      <div class="field"><label>Role on project (optional)</label><input class="input" v-model="form.roleOnProject" /></div>
      <p class="muted" style="font-size: 12.5px">Dates must fall within the project window. Over-allocation is allowed but flagged.</p>
      <template #footer>
        <button class="btn" @click="showAdd = false">Cancel</button>
        <button class="btn btn-primary" :disabled="saving || !form.resourceId || !form.startDate || !form.endDate" @click="addAllocation">Allocate</button>
      </template>
    </ModalDialog>
  </div>
</template>
