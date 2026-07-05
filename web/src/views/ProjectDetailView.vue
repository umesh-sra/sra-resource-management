<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { allocationsApi, projectsApi, resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Allocation, EffortUnit, ProjectDetail, ProjectStatus, Resource } from '@/types'
import { fmtDate, fmtMoney, projectStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'
import AllocationEditModal from '@/components/AllocationEditModal.vue'

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

const editAlloc = ref<Allocation | null>(null)
const showEdit = ref(false)
const savingEdit = ref(false)
const editForm = ref({
  name: '', code: '', startDate: '', endDate: '',
  budget: null as number | null, remaining: null as number | null,
  billable: true, status: 'active' as ProjectStatus,
})

const num = (v: unknown) => (v === null || v === undefined || (v as unknown) === '' ? undefined : Number(v))

function openEdit() {
  const pr = project.value
  if (!pr) return
  editForm.value = {
    name: pr.name, code: pr.code, startDate: pr.startDate, endDate: pr.endDate,
    budget: pr.budget ?? null, remaining: pr.remaining ?? null,
    billable: pr.billable, status: pr.status,
  }
  showEdit.value = true
}

async function saveEdit() {
  if (!project.value) return
  savingEdit.value = true
  try {
    const f = editForm.value
    await projectsApi.update(project.value.id, {
      clientId: project.value.clientId,
      name: f.name, code: f.code, startDate: f.startDate, endDate: f.endDate,
      budget: num(f.budget), remaining: num(f.remaining),
      billable: f.billable, status: f.status,
    })
    toast.success('Project updated')
    showEdit.value = false
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not update project')
  } finally {
    savingEdit.value = false
  }
}

async function removeAllocation(a: Allocation) {
  if (!confirm(`Remove ${a.resourceName} from this project?`)) return
  try {
    await allocationsApi.remove(a.id)
    toast.success('Allocation removed')
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not remove allocation')
  }
}

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
    <div v-if="loading" class="card card-pad"><span class="sr-only" role="status">Loading project…</span><div class="skeleton" style="height: 60px" /></div>

    <template v-else-if="project">
      <div class="page-header">
        <div>
          <RouterLink to="/projects" class="muted"><span aria-hidden="true">← </span>Back to projects</RouterLink>
          <h1 style="margin-top: 4px">{{ project.code }} — {{ project.name }}</h1>
          <div class="subtitle">
            <RouterLink :to="`/clients/${project.clientId}`">{{ project.clientName }}</RouterLink>
            · <span class="badge" :class="projectStatus(project.status).class">{{ projectStatus(project.status).label }}</span>
          </div>
        </div>
        <div class="row">
          <button class="btn btn-primary" @click="openAdd">+ Allocate resource</button>
          <button class="btn" @click="openEdit">Edit</button>
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
            <thead><tr><th scope="col">Resource</th><th scope="col">Role</th><th scope="col">Dates</th><th scope="col" class="num">Effort</th><th scope="col">Billable</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
            <tbody>
              <tr v-for="a in project.allocations" :key="a.id" class="clickable" @click="router.push(`/resources/${a.resourceId}`)">
                <td><RouterLink :to="`/resources/${a.resourceId}`" class="row-link" @click.stop>{{ a.resourceName }}</RouterLink></td>
                <td>{{ a.roleOnProject ?? '—' }}</td>
                <td>{{ fmtDate(a.startDate) }} – {{ fmtDate(a.endDate) }}</td>
                <td class="num">{{ a.effort }} {{ a.effortUnit === 'percent' ? '%' : 'h/wk' }}</td>
                <td><span class="badge" :class="a.billable ? 'green' : 'gray'">{{ a.billable ? 'Billable' : 'Non-billable' }}</span></td>
                <td class="num" @click.stop>
                  <button class="btn btn-sm" @click="editAlloc = a">Edit<span class="sr-only"> allocation of {{ a.resourceName }}</span></button>
                  <button class="btn btn-sm btn-danger" @click="removeAllocation(a)">Remove<span class="sr-only"> allocation of {{ a.resourceName }}</span></button>
                </td>
              </tr>
              <tr v-if="!project.allocations.length"><td colspan="6" class="empty">No one allocated yet.</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </template>

    <ModalDialog v-if="showAdd" title="Allocate a resource" @close="showAdd = false">
      <div class="field"><label for="al-resource">Resource</label>
        <select id="al-resource" class="select" v-model="form.resourceId">
          <option value="" disabled>Select a resource…</option>
          <option v-for="r in resources" :key="r.id" :value="r.id">{{ r.name }} — {{ r.primaryJobTitle }}</option>
        </select>
      </div>
      <div class="form-row">
        <div class="field"><label for="al-start">Start date</label><input id="al-start" class="input" v-model="form.startDate" type="date" /></div>
        <div class="field"><label for="al-end">End date</label><input id="al-end" class="input" v-model="form.endDate" type="date" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label for="al-effort">Effort</label><input id="al-effort" class="input" v-model.number="form.effort" type="number" min="0" /></div>
        <div class="field"><label for="al-unit">Unit</label>
          <select id="al-unit" class="select" v-model="form.effortUnit">
            <option value="hoursPerWeek">Hours / week</option><option value="percent">Percent</option>
          </select>
        </div>
      </div>
      <div class="field"><label for="al-role">Role on project (optional)</label><input id="al-role" class="input" v-model="form.roleOnProject" /></div>
      <p class="muted" style="font-size: 12.5px">Dates must fall within the project window. Over-allocation is allowed but flagged.</p>
      <template #footer>
        <button class="btn" @click="showAdd = false">Cancel</button>
        <button class="btn btn-primary" :disabled="saving || !form.resourceId || !form.startDate || !form.endDate" @click="addAllocation">Allocate</button>
      </template>
    </ModalDialog>

    <ModalDialog v-if="showEdit" title="Edit project" @close="showEdit = false">
      <div class="form-row">
        <div class="field"><label for="ep-name">Name</label><input id="ep-name" class="input" v-model="editForm.name" /></div>
        <div class="field"><label for="ep-code">Code</label><input id="ep-code" class="input" v-model="editForm.code" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label for="ep-start">Start date</label><input id="ep-start" class="input" v-model="editForm.startDate" type="date" /></div>
        <div class="field"><label for="ep-end">End date</label><input id="ep-end" class="input" v-model="editForm.endDate" type="date" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label for="ep-budget">Budget</label><input id="ep-budget" class="input" v-model.number="editForm.budget" type="number" min="0" /></div>
        <div class="field"><label for="ep-remaining">Remaining</label><input id="ep-remaining" class="input" v-model.number="editForm.remaining" type="number" min="0" /></div>
      </div>
      <div class="form-row">
        <div class="field"><label for="ep-status">Status</label>
          <select id="ep-status" class="select" v-model="editForm.status">
            <option value="planned">Planned</option><option value="active">Active</option>
            <option value="onHold">On hold</option><option value="completed">Completed</option>
            <option value="cancelled">Cancelled</option>
          </select>
        </div>
        <div class="field">
          <label style="display:flex; align-items:center; gap:8px; margin-top: 26px">
            <input type="checkbox" v-model="editForm.billable" /> Billable
          </label>
        </div>
      </div>
      <p class="muted" style="font-size: 12.5px">Narrowing the dates is rejected if existing allocations would fall outside the new window.</p>
      <template #footer>
        <button class="btn" @click="showEdit = false">Cancel</button>
        <button class="btn btn-primary" :disabled="savingEdit || !editForm.name.trim() || !editForm.code.trim() || !editForm.startDate || !editForm.endDate" @click="saveEdit">Save</button>
      </template>
    </ModalDialog>

    <AllocationEditModal
      v-if="editAlloc" :allocation="editAlloc"
      @close="editAlloc = null" @saved="editAlloc = null; load()"
    />
  </div>
</template>
