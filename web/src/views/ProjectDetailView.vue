<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { allocationsApi, projectsApi, resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Allocation, EffortUnit, ProjectDetail, Resource } from '@/types'
import {
  bookingStatusBadge, bookingStatusLabel, fmtDate, fmtMoney, milestoneBadge, milestoneLabel,
  projectStatus,
} from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'
import AllocationEditModal from '@/components/AllocationEditModal.vue'
import ProjectFormModal from '@/components/ProjectFormModal.vue'
import AppAvatar from '@/components/AppAvatar.vue'
import AvatarStack from '@/components/AvatarStack.vue'

const route = useRoute()
const router = useRouter()
const toast = useToastStore()

const project = ref<ProjectDetail | null>(null)
const loading = ref(true)

const resources = ref<Resource[]>([])
const showAdd = ref(false)
const showEdit = ref(false)
const saving = ref(false)
const editAlloc = ref<Allocation | null>(null)

const form = ref({
  resourceId: '', startDate: '', endDate: '',
  effort: 38, effortUnit: 'hoursPerWeek' as EffortUnit, roleOnProject: '',
})

/** Share of the budget already consumed, for the progress meter. */
const consumed = computed(() => {
  const p = project.value
  if (!p || p.budget == null || p.budget <= 0 || p.remaining == null) return null
  return Math.min(1, Math.max(0, (p.budget - p.remaining) / p.budget))
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
    // Over-allocation is allowed (FR-ALL-6) — the save succeeded either way.
    if (a.warnings?.length) toast.warning(`Allocated — ${a.warnings.join(' ')}`)
    else toast.success('Allocation added')
    showAdd.value = false
    form.value.resourceId = ''
    form.value.roleOnProject = ''
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not add allocation')
  } finally {
    saving.value = false
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

async function removeProject() {
  const p = project.value
  if (!p) return
  const has = p.allocations.length > 0
  if (!confirm(has
    ? `Delete project ${p.code} and its ${p.allocations.length} allocation(s)?`
    : `Delete project ${p.code}?`)) return
  try {
    await projectsApi.remove(p.id, has)
    toast.success('Project deleted')
    router.push('/projects')
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not delete project')
  }
}

onMounted(load)
</script>

<template>
  <div class="toolbar">
    <RouterLink class="icon-btn plain" to="/projects" aria-label="Back to projects">
      <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" /></svg>
    </RouterLink>
    <h1 class="ttl">{{ project ? `${project.name}` : 'Project' }}</h1>
    <span v-if="project" class="badge" :class="projectStatus(project.status).class">{{ projectStatus(project.status).label }}</span>
    <div class="spacer" />
    <RouterLink v-if="project" class="btn" :to="`/gantt?clientId=${project.clientId}`">Gantt</RouterLink>
    <button class="btn" :disabled="!project" @click="showEdit = true">Edit</button>
    <button class="btn btn-danger" :disabled="!project" @click="removeProject">Delete</button>
    <button class="btn btn-accent btn-pill" :disabled="!project" @click="openAdd">
      <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><path d="M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Zm1 8h4v2h-4v4h-2v-4H7v-2h4V7h2v4Z" fill="currentColor" /></svg>
      Allocate
    </button>
  </div>

  <div class="page page-narrow">
    <div v-if="loading" class="card card-pad"><span class="sr-only" role="status">Loading project…</span><div class="skeleton" style="height: 90px" /></div>

    <template v-else-if="project">
      <p class="crumb">
        <RouterLink :to="`/clients/${project.clientId}`">{{ project.clientName }}</RouterLink>
        <span class="muted"> · {{ project.code }}</span>
      </p>

      <div class="figures">
        <div>
          <span class="fig-label">Dates</span>
          <span class="fig-value small">{{ fmtDate(project.startDate) }} – {{ fmtDate(project.endDate) }}</span>
        </div>
        <div><span class="fig-label">Budget</span><span class="fig-value">{{ fmtMoney(project.budget) }}</span></div>
        <div>
          <span class="fig-label">Remaining</span>
          <span class="fig-value">{{ fmtMoney(project.remaining) }}</span>
          <div v-if="consumed !== null" class="ubar" :class="{ over: consumed >= 0.9 }" style="margin-top: 8px" aria-hidden="true">
            <span :style="{ width: `${consumed * 100}%` }" />
          </div>
          <span v-if="consumed !== null" class="muted" style="font-size: 11.5px">{{ Math.round(consumed * 100) }}% consumed</span>
        </div>
        <div><span class="fig-label">Billable</span><span class="fig-value">{{ project.billable ? 'Yes' : 'No' }}</span></div>
        <div>
          <span class="fig-label">Team</span>
          <div style="margin-top: 8px"><AvatarStack :people="project.team" :max="6" :size="30" /></div>
        </div>
      </div>

      <div class="card">
        <div class="card-pad" style="padding-bottom: 8px"><h2>Team &amp; allocations</h2></div>
        <div class="table-wrap">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Person</th><th scope="col">Role</th><th scope="col">Dates</th>
                <th scope="col" class="num">Effort</th><th scope="col">Billable</th>
                <th scope="col">Status</th>
                <th scope="col"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="a in project.allocations" :key="a.id" class="clickable" @click="router.push(`/people/${a.resourceId}`)">
                <td>
                  <div class="row row-nowrap" style="gap: 9px">
                    <AppAvatar :name="a.resourceName ?? '?'" :size="28" />
                    <RouterLink :to="`/people/${a.resourceId}`" class="row-link" @click.stop>{{ a.resourceName }}</RouterLink>
                  </div>
                </td>
                <td>{{ a.roleOnProject ?? '—' }}</td>
                <td>{{ fmtDate(a.startDate) }} – {{ fmtDate(a.endDate) }}</td>
                <td class="num">{{ a.effort }} {{ a.effortUnit === 'percent' ? '%' : 'h/wk' }}</td>
                <td><span class="badge" :class="a.billable ? 'green' : 'gray'">{{ a.billable ? 'Billable' : 'Non-billable' }}</span></td>
                <td>
                  <span class="badge" :class="bookingStatusBadge(a.bookingStatus)">
                    {{ bookingStatusLabel(a.bookingStatus) }}
                  </span>
                </td>
                <td class="num" @click.stop>
                  <button class="btn btn-sm" @click="editAlloc = a">Edit<span class="sr-only"> allocation of {{ a.resourceName }}</span></button>
                  <button class="btn btn-sm btn-danger" @click="removeAllocation(a)">Remove<span class="sr-only"> allocation of {{ a.resourceName }}</span></button>
                </td>
              </tr>
              <tr v-if="!project.allocations.length"><td colspan="7" class="empty">No one allocated yet.</td></tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Phases & milestones (Requirements §3.6 / §3.7) ------------------ -->
      <div class="grid grid-2">
        <div class="card">
          <div class="card-pad" style="padding-bottom: 8px"><h2>Phases</h2></div>
          <div class="card-pad" style="padding-top: 0">
            <ol v-if="project.phases.length" class="stages">
              <li v-for="ph in project.phases" :key="ph.id">
                <span class="stage-bar" :style="{ background: ph.colour || 'var(--brand-500)' }" aria-hidden="true" />
                <div>
                  <div class="stage-name">{{ ph.name }}</div>
                  <div class="muted">{{ fmtDate(ph.startDate) }} – {{ fmtDate(ph.endDate) }}</div>
                </div>
              </li>
            </ol>
            <p v-else class="muted" style="margin: 0">
              No phases. Add them from <em>Edit project → Phases</em>.
            </p>
          </div>
        </div>

        <div class="card">
          <div class="card-pad" style="padding-bottom: 8px"><h2>Milestones</h2></div>
          <div class="card-pad" style="padding-top: 0">
            <ol v-if="project.milestones.length" class="stages">
              <li v-for="m in project.milestones" :key="m.id">
                <span class="milestone-dot" :class="m.status" aria-hidden="true" />
                <div>
                  <div class="stage-name">{{ m.name }}</div>
                  <div class="muted">
                    {{ fmtDate(m.dueDate) }} ·
                    <span class="badge" :class="milestoneBadge(m.status)">{{ milestoneLabel(m.status) }}</span>
                  </div>
                </div>
              </li>
            </ol>
            <p v-else class="muted" style="margin: 0">
              No milestones. Add them from <em>Edit project → Milestones</em>.
            </p>
          </div>
        </div>
      </div>
    </template>
  </div>

  <ModalDialog v-if="showAdd" title="Assign People and Resources" @close="showAdd = false">
    <div class="field">
      <label for="al-resource">Person *</label>
      <select id="al-resource" class="select" v-model="form.resourceId">
        <option value="" disabled>Select a person…</option>
        <option v-for="r in resources" :key="r.id" :value="r.id">{{ r.name }} — {{ r.primaryJobTitle }}</option>
      </select>
    </div>
    <div class="form-row">
      <div class="field"><label for="al-start">Start date</label><input id="al-start" class="input" v-model="form.startDate" type="date" /></div>
      <div class="field"><label for="al-end">End date</label><input id="al-end" class="input" v-model="form.endDate" type="date" /></div>
    </div>
    <div class="form-row">
      <div class="field"><label for="al-effort">Effort</label><input id="al-effort" class="input" v-model.number="form.effort" type="number" min="0" /></div>
      <div class="field">
        <label for="al-unit">Unit</label>
        <select id="al-unit" class="select" v-model="form.effortUnit">
          <option value="hoursPerWeek">Hours / week</option>
          <option value="percent">Percent</option>
        </select>
      </div>
    </div>
    <div class="field"><label for="al-role">Role on project (optional)</label><input id="al-role" class="input" v-model="form.roleOnProject" /></div>
    <p class="muted" style="font-size: 12.5px">Dates must fall within the project window. Over-allocation is allowed but flagged.</p>
    <template #footer>
      <button class="btn" @click="showAdd = false">Cancel</button>
      <button class="btn btn-accent btn-pill" :disabled="saving || !form.resourceId || !form.startDate || !form.endDate" @click="addAllocation">Allocate</button>
    </template>
  </ModalDialog>

  <ProjectFormModal
    v-if="showEdit && project" :project="project"
    @close="showEdit = false" @saved="showEdit = false; load()"
  />

  <AllocationEditModal
    v-if="editAlloc" :allocation="editAlloc"
    @close="editAlloc = null" @saved="editAlloc = null; load()"
  />
</template>

<style scoped>
.stages { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 12px; }
.stages li { display: flex; align-items: flex-start; gap: 10px; }
.stage-bar { width: 4px; align-self: stretch; min-height: 34px; border-radius: 999px; flex: none; }
.stage-name { font-weight: 600; }
.milestone-dot { width: 10px; height: 10px; border-radius: 50%; margin-top: 5px; flex: none; background: var(--gray-400); }
.milestone-dot.met { background: var(--green-600); }
.milestone-dot.missed { background: var(--red-600); }
.ttl { font-size: 18px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.crumb { margin: 0 0 14px; font-size: 13.5px; }
.figures {
  display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius);
  margin-bottom: 16px; box-shadow: var(--shadow-sm);
}
.figures > div { padding: 16px 18px; border-left: 1px solid var(--border); }
.figures > div:first-child { border-left: 0; }
.fig-label { display: block; color: var(--text-muted); font-size: 12px; }
.fig-value { display: block; font-size: 22px; font-weight: 680; color: var(--brand-800); margin-top: 4px; }
.fig-value.small { font-size: 14px; font-weight: 600; padding: 5px 0; }
</style>
