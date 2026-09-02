<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { clientsApi, projectsApi, resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type {
  Client, EffortUnit, MilestoneStatus, Project, ProjectBudgetType, ProjectDetail,
  ProjectStatus, Resource,
} from '@/types'
import { useToastStore } from '@/stores/toast'
import ModalDialog from './ModalDialog.vue'
import AppAvatar from './AppAvatar.vue'

/**
 * Create / edit a project through the reference's tabbed dialog
 * (screens/new_project*.png): Overview, Budget, Team, Phases, Milestones.
 *
 * Phases and Milestones are backed by the V002 model (Requirements §3.6/§3.7).
 * Both are child records of a saved project, so on a *new* project they are
 * staged here and POSTed after the project exists — the same pattern the Team
 * tab already uses for allocations.
 */
const props = defineProps<{ project?: ProjectDetail | null }>()
const emit = defineEmits<{ close: []; saved: [project: Project] }>()

const toast = useToastStore()
const editing = computed(() => !!props.project)

type Tab = 'overview' | 'budget' | 'team' | 'phases' | 'milestones'
const tab = ref<Tab>('overview')

const p = props.project
const form = ref({
  name: p?.name ?? '',
  code: p?.code ?? '',
  clientId: p?.clientId ?? '',
  startDate: p?.startDate ?? '',
  endDate: p?.endDate ?? '',
  billable: p?.billable ?? true,
  status: (p?.status ?? 'planned') as ProjectStatus,
  // budgetType comes from the server on edit; fall back to inferring it from
  // whichever amount is present so V001-era rows open on the right mode.
  budgetMode: (p?.budgetType ?? (p?.budget != null ? 'fee' : 'none')) as ProjectBudgetType,
  budget: p?.budget ?? null as number | null,
  remaining: p?.remaining ?? null as number | null,
  budgetHours: p?.budgetHours ?? null as number | null,
  remainingHours: p?.remainingHours ?? null as number | null,
  details: p?.details ?? '',
})

/** Phase / milestone rows staged on the dialog (FR-PHASE-1, FR-MILE-1). */
interface PhaseRow { id?: string; name: string; startDate: string; endDate: string; sortOrder: number }
interface MilestoneRow { id?: string; name: string; dueDate: string; status: MilestoneStatus }
const phases = ref<PhaseRow[]>(
  (p?.phases ?? []).map((x) => ({
    id: x.id, name: x.name, startDate: x.startDate, endDate: x.endDate, sortOrder: x.sortOrder,
  })),
)
const milestones = ref<MilestoneRow[]>(
  (p?.milestones ?? []).map((x) => ({ id: x.id, name: x.name, dueDate: x.dueDate, status: x.status })),
)

function addPhase() {
  phases.value.push({
    name: '', startDate: form.value.startDate, endDate: form.value.endDate,
    sortOrder: phases.value.length + 1,
  })
}
function removePhase(i: number) { phases.value.splice(i, 1) }
function addMilestone() {
  milestones.value.push({ name: '', dueDate: form.value.startDate, status: 'pending' })
}
function removeMilestone(i: number) { milestones.value.splice(i, 1) }

/** A phase/milestone must sit inside the project window (FR-PHASE-4, FR-MILE-4). */
const outOfWindow = computed(() => {
  const { startDate: s, endDate: e } = form.value
  if (!s || !e) return { phases: [] as number[], milestones: [] as number[] }
  return {
    phases: phases.value
      .map((x, i) => (x.startDate && x.endDate && (x.startDate < s || x.endDate > e) ? i : -1))
      .filter((i) => i >= 0),
    milestones: milestones.value
      .map((x, i) => (x.dueDate && (x.dueDate < s || x.dueDate > e) ? i : -1))
      .filter((i) => i >= 0),
  }
})

const clients = ref<Client[]>([])
const resources = ref<Resource[]>([])
const saving = ref(false)

/** Team rows staged on the dialog; they become allocations once the project exists. */
interface TeamRow {
  resourceId: string
  roleOnProject: string
  effort: number
  effortUnit: EffortUnit
}
const team = ref<TeamRow[]>([])

const resourceById = computed(() => new Map(resources.value.map((r) => [r.id, r])))
const chosen = computed(() => new Set(team.value.map((t) => t.resourceId)))

onMounted(async () => {
  try {
    const [c, r] = await Promise.all([
      clientsApi.list({ pageSize: 200, sort: 'name' }),
      resourcesApi.list({ pageSize: 200, sort: 'name' }),
    ])
    clients.value = c.items
    resources.value = r.items
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not load clients and people')
  }
})

function addTeamRow() {
  team.value.push({ resourceId: '', roleOnProject: '', effort: 38, effortUnit: 'hoursPerWeek' })
}
function removeTeamRow(i: number) { team.value.splice(i, 1) }

const num = (v: unknown) => (v === null || v === undefined || v === '' ? undefined : Number(v))

const valid = computed(() =>
  !!form.value.name.trim() && !!form.value.code.trim() && !!form.value.clientId
  && !!form.value.startDate && !!form.value.endDate
  && form.value.endDate >= form.value.startDate,
)

/** Tabs carry a dot when they hold something the user should notice. */
const tabState = computed(() => ({
  overview: !valid.value,
  budget: (form.value.budgetMode === 'fee' && form.value.budget == null)
       || (form.value.budgetMode === 'hours' && form.value.budgetHours == null),
  team: team.value.some((t) => !t.resourceId),
  phases: phases.value.some((x) => !x.name.trim()) || outOfWindow.value.phases.length > 0,
  milestones: milestones.value.some((x) => !x.name.trim()) || outOfWindow.value.milestones.length > 0,
}))

async function save() {
  if (!valid.value) { tab.value = 'overview'; return }
  saving.value = true
  try {
    const f = form.value
    const body = {
      name: f.name.trim(),
      code: f.code.trim(),
      clientId: f.clientId,
      startDate: f.startDate,
      endDate: f.endDate,
      budgetType: f.budgetMode,
      budget: f.budgetMode === 'fee' ? num(f.budget) : undefined,
      remaining: f.budgetMode === 'fee' ? num(f.remaining) : undefined,
      budgetHours: f.budgetMode === 'hours' ? num(f.budgetHours) : undefined,
      remainingHours: f.budgetMode === 'hours' ? num(f.remainingHours) : undefined,
      details: f.details.trim() || undefined,
      billable: f.billable,
      status: f.status,
    }
    const saved = props.project
      ? await projectsApi.update(props.project.id, body)
      : await projectsApi.create(body)

    // Staged team rows become allocations spanning the whole project window.
    const rows = team.value.filter((t) => t.resourceId)
    let warned = false
    for (const row of rows) {
      try {
        const alloc = await projectsApi.createAllocation(saved.id, {
          resourceId: row.resourceId,
          startDate: saved.startDate,
          endDate: saved.endDate,
          effort: Number(row.effort),
          effortUnit: row.effortUnit,
          roleOnProject: row.roleOnProject || undefined,
        })
        if (alloc.warnings?.length) warned = true
      } catch (e) {
        const who = resourceById.value.get(row.resourceId)?.name ?? 'a team member'
        toast.error(e instanceof ApiError ? `${who}: ${e.message}` : `Could not allocate ${who}`)
      }
    }

    // Phases and milestones: create the new ones, update the ones already saved.
    for (const row of phases.value.filter((x) => x.name.trim())) {
      const body = {
        name: row.name.trim(), startDate: row.startDate, endDate: row.endDate, sortOrder: row.sortOrder,
      }
      try {
        if (row.id) await projectsApi.updatePhase(saved.id, row.id, body)
        else await projectsApi.createPhase(saved.id, body)
      } catch (e) {
        toast.error(e instanceof ApiError ? `Phase "${row.name}": ${e.message}` : `Could not save phase "${row.name}"`)
      }
    }
    for (const row of milestones.value.filter((x) => x.name.trim())) {
      const body = { name: row.name.trim(), dueDate: row.dueDate, status: row.status }
      try {
        if (row.id) await projectsApi.updateMilestone(saved.id, row.id, body)
        else await projectsApi.createMilestone(saved.id, body)
      } catch (e) {
        toast.error(e instanceof ApiError ? `Milestone "${row.name}": ${e.message}` : `Could not save milestone "${row.name}"`)
      }
    }

    if (warned) toast.warning('Project saved — some allocations exceed weekly availability.')
    else toast.success(editing.value ? 'Project updated' : 'Project created')
    emit('saved', saved)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not save this project')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <ModalDialog :title="editing ? 'Edit Project' : 'New Project'" :max-width="600" @close="emit('close')">
    <template #head-icon>
      <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M9 3h6v2h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h3V3Zm2 2v1h2V5h-2Z" fill="currentColor" /></svg>
    </template>

    <template #subhead>
      <div class="dialog-tabs">
        <div class="tabs" role="tablist" aria-label="Project sections">
          <button class="tab" role="tab" :aria-selected="tab === 'overview'" :class="{ active: tab === 'overview' }" @click="tab = 'overview'">
            Overview<span v-if="tabState.overview" class="dot" aria-hidden="true" />
          </button>
          <button class="tab" role="tab" :aria-selected="tab === 'budget'" :class="{ active: tab === 'budget' }" @click="tab = 'budget'">
            Budget<span v-if="tabState.budget" class="dot" aria-hidden="true" />
          </button>
          <button class="tab" role="tab" :aria-selected="tab === 'team'" :class="{ active: tab === 'team' }" @click="tab = 'team'">
            Team<span v-if="tabState.team" class="dot" aria-hidden="true" />
          </button>
          <button class="tab" role="tab" :aria-selected="tab === 'phases'" :class="{ active: tab === 'phases' }" @click="tab = 'phases'">
            Phases<span v-if="tabState.phases" class="dot" aria-hidden="true" />
          </button>
          <button class="tab" role="tab" :aria-selected="tab === 'milestones'" :class="{ active: tab === 'milestones' }" @click="tab = 'milestones'">
            Milestones<span v-if="tabState.milestones" class="dot" aria-hidden="true" />
          </button>
        </div>
      </div>
    </template>

    <!-- Overview --------------------------------------------------------- -->
    <div v-show="tab === 'overview'" role="tabpanel">
      <div v-if="!editing" class="hint-banner">
        <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><path d="M12 2a7 7 0 0 0-4 12.7V17a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1v-2.3A7 7 0 0 0 12 2Zm-2 18h4v1a1 1 0 0 1-1 1h-2a1 1 0 0 1-1-1v-1Z" fill="currentColor" /></svg>
        <span>Allocations must fall inside the project window, so set the dates before assigning the team.</span>
      </div>

      <div class="field"><label for="pj-name">Name *</label><input id="pj-name" class="input" v-model="form.name" /></div>

      <div class="form-row">
        <div class="field"><label for="pj-code">Project code *</label><input id="pj-code" class="input" v-model="form.code" placeholder="e.g. DECYP006412" /></div>
        <div class="field">
          <label for="pj-status">Status</label>
          <select id="pj-status" class="select" v-model="form.status">
            <option value="planned">Planned</option>
            <option value="active">Active</option>
            <option value="onHold">On hold</option>
            <option value="completed">Completed</option>
            <option value="cancelled">Cancelled</option>
          </select>
        </div>
      </div>

      <div class="field">
        <label for="pj-client">Client *</label>
        <select id="pj-client" class="select" v-model="form.clientId">
          <option value="" disabled>Select a client…</option>
          <option v-for="c in clients" :key="c.id" :value="c.id">{{ c.name }}</option>
        </select>
      </div>

      <div class="form-row">
        <div class="field"><label for="pj-start">Start date *</label><input id="pj-start" class="input" type="date" v-model="form.startDate" /></div>
        <div class="field"><label for="pj-end">End date *</label><input id="pj-end" class="input" type="date" v-model="form.endDate" /></div>
      </div>
      <p v-if="form.startDate && form.endDate && form.endDate < form.startDate" class="err">End date must be on or after the start date.</p>

      <label class="switch">
        <input type="checkbox" v-model="form.billable" /><span class="track" />
        <span>Billable</span>
      </label>
      <p class="muted hint">New allocations inherit this unless overridden.</p>
    </div>

    <!-- Budget ----------------------------------------------------------- -->
    <div v-show="tab === 'budget'" role="tabpanel">
      <div class="segmented" role="group" aria-label="Budget type" style="margin-bottom: 18px">
        <button :aria-pressed="form.budgetMode === 'none'" @click="form.budgetMode = 'none'">No budget</button>
        <button :aria-pressed="form.budgetMode === 'fee'" @click="form.budgetMode = 'fee'">Budget by fee</button>
        <button :aria-pressed="form.budgetMode === 'hours'" @click="form.budgetMode = 'hours'">Budget by hours</button>
      </div>

      <template v-if="form.budgetMode === 'fee'">
        <div class="form-row">
          <div class="field"><label for="pj-budget">Budget (AUD)</label><input id="pj-budget" class="input" type="number" min="0" v-model.number="form.budget" /></div>
          <div class="field"><label for="pj-remaining">Remaining (AUD)</label><input id="pj-remaining" class="input" type="number" min="0" v-model.number="form.remaining" /></div>
        </div>
        <p class="muted hint">
          Leave <em>Remaining</em> blank on a new project and it starts equal to the budget.
          The dashboard flags a project as at risk once ≥90% is consumed.
        </p>
      </template>
      <template v-else-if="form.budgetMode === 'hours'">
        <div class="form-row">
          <div class="field"><label for="pj-bhours">Budget (hours)</label><input id="pj-bhours" class="input" type="number" min="0" v-model.number="form.budgetHours" /></div>
          <div class="field"><label for="pj-rhours">Remaining (hours)</label><input id="pj-rhours" class="input" type="number" min="0" v-model.number="form.remainingHours" /></div>
        </div>
        <p class="muted hint">
          Leave <em>Remaining</em> blank on a new project and it starts equal to the hour budget.
        </p>
      </template>
      <p v-else class="muted">No budget is tracked for this project.</p>

      <div class="note-box">
        Per-person charge-out rates are recorded on each allocation (Team tab). How “remaining”
        is drawn down is still an open question in <code>docs/Requirements.md</code> §8 — it is
        maintained by hand until timesheet actuals exist.
      </div>
    </div>

    <!-- Team ------------------------------------------------------------- -->
    <div v-show="tab === 'team'" role="tabpanel">
      <button class="btn btn-outline btn-pill" @click="addTeamRow">
        <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><path d="M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Zm1 8h4v2h-4v4h-2v-4H7v-2h4V7h2v4Z" fill="currentColor" /></svg>
        Assign People and Resources
      </button>

      <div v-if="editing" class="note-box" style="margin-top: 14px">
        People added here are allocated for the full project window. Existing allocations are edited
        on the project page, where dates and effort can be tuned per person.
      </div>

      <ul class="team-list">
        <li v-for="(row, i) in team" :key="i" class="team-row">
          <AppAvatar
            :name="resourceById.get(row.resourceId)?.name ?? '?'"
            :image-url="resourceById.get(row.resourceId)?.imageUrl" :size="34"
          />
          <div class="team-fields">
            <select class="select" v-model="row.resourceId" :aria-label="`Person for team row ${i + 1}`">
              <option value="" disabled>Select a person…</option>
              <option v-for="r in resources" :key="r.id" :value="r.id" :disabled="chosen.has(r.id) && r.id !== row.resourceId">
                {{ r.name }} — {{ r.primaryJobTitle }}
              </option>
            </select>
            <input class="input" v-model="row.roleOnProject" placeholder="Role (optional)" :aria-label="`Role for team row ${i + 1}`" />
            <input class="input effort" type="number" min="0" v-model.number="row.effort" :aria-label="`Effort for team row ${i + 1}`" />
            <select class="select unit" v-model="row.effortUnit" :aria-label="`Effort unit for team row ${i + 1}`">
              <option value="hoursPerWeek">h/wk</option>
              <option value="percent">%</option>
            </select>
          </div>
          <button class="icon-btn plain" :aria-label="`Remove team row ${i + 1}`" @click="removeTeamRow(i)">
            <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2" stroke-linecap="round" /></svg>
          </button>
        </li>
      </ul>

      <p v-if="!team.length" class="muted" style="margin-top: 16px">No one assigned yet.</p>
    </div>

    <!-- Phases ----------------------------------------------------------- -->
    <div v-show="tab === 'phases'" role="tabpanel">
      <button class="btn btn-outline" @click="addPhase">
        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M12 5v14M5 12h14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" /></svg>
        New Phase
      </button>

      <p v-if="!phases.length" class="muted" style="margin-top: 16px">
        No phases yet. A phase is a named, dated stage of the project — Discovery, Build, UAT.
      </p>

      <div v-for="(row, i) in phases" :key="i" class="sub-row">
        <div class="field" style="flex: 2 1 180px">
          <label :for="`ph-name-${i}`">Name</label>
          <input :id="`ph-name-${i}`" class="input" v-model="row.name" placeholder="Discovery" />
        </div>
        <div class="field" style="flex: 1 1 130px">
          <label :for="`ph-start-${i}`">Start</label>
          <input :id="`ph-start-${i}`" class="input" type="date" v-model="row.startDate" />
        </div>
        <div class="field" style="flex: 1 1 130px">
          <label :for="`ph-end-${i}`">End</label>
          <input :id="`ph-end-${i}`" class="input" type="date" v-model="row.endDate" />
        </div>
        <button class="btn btn-ghost btn-sm" :aria-label="`Remove phase ${i + 1}`" @click="removePhase(i)">✕</button>
        <p v-if="outOfWindow.phases.includes(i)" class="row-error">
          Outside the project window ({{ form.startDate || '—' }} – {{ form.endDate || '—' }}).
        </p>
      </div>
    </div>

    <!-- Milestones --------------------------------------------------------- -->
    <div v-show="tab === 'milestones'" role="tabpanel">
      <button class="btn btn-outline" @click="addMilestone">
        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M12 5v14M5 12h14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" /></svg>
        New Milestone
      </button>

      <p v-if="!milestones.length" class="muted" style="margin-top: 16px">
        No milestones yet. A milestone is a dated checkpoint — Design sign-off, Go live.
      </p>

      <div v-for="(row, i) in milestones" :key="i" class="sub-row">
        <div class="field" style="flex: 2 1 180px">
          <label :for="`ms-name-${i}`">Name</label>
          <input :id="`ms-name-${i}`" class="input" v-model="row.name" placeholder="Go live" />
        </div>
        <div class="field" style="flex: 1 1 130px">
          <label :for="`ms-due-${i}`">Due</label>
          <input :id="`ms-due-${i}`" class="input" type="date" v-model="row.dueDate" />
        </div>
        <div class="field" style="flex: 1 1 120px">
          <label :for="`ms-status-${i}`">Status</label>
          <select :id="`ms-status-${i}`" class="select" v-model="row.status">
            <option value="pending">Pending</option>
            <option value="met">Met</option>
            <option value="missed">Missed</option>
          </select>
        </div>
        <button class="btn btn-ghost btn-sm" :aria-label="`Remove milestone ${i + 1}`" @click="removeMilestone(i)">✕</button>
        <p v-if="outOfWindow.milestones.includes(i)" class="row-error">
          Outside the project window ({{ form.startDate || '—' }} – {{ form.endDate || '—' }}).
        </p>
      </div>
    </div>

    <template #footer>
      <span class="note">Over-allocation is flagged, not blocked (FR-ALL-6).</span>
      <button class="btn" @click="emit('close')">Cancel</button>
      <button class="btn btn-accent btn-pill" :disabled="saving || !valid" @click="save">
        {{ editing ? 'Save changes' : 'Add Project' }}
      </button>
    </template>
  </ModalDialog>
</template>

<style scoped>
.sub-row {
  display: flex; flex-wrap: wrap; gap: 0 12px; align-items: flex-end;
  padding: 12px 0; border-bottom: 1px solid var(--gray-100);
}
.sub-row .field { margin-bottom: 0; }
.sub-row > .btn-ghost { margin-bottom: 4px; }
.row-error { flex: 1 0 100%; margin: 6px 0 0; color: var(--red-700); font-size: 12.5px; }
.dialog-tabs { padding: 0 20px; border-bottom: 1px solid var(--border); }
.dot { display: inline-block; width: 6px; height: 6px; border-radius: 50%; background: var(--accent); margin-left: 6px; vertical-align: middle; }
.hint { font-size: 12px; margin: 6px 0 0; }
.err { color: var(--red-700); font-size: 12.5px; margin: -6px 0 12px; }
.note-box { background: var(--gray-50); border: 1px solid var(--border); border-radius: var(--radius-sm); padding: 12px 14px; color: var(--text-muted); font-size: 12.5px; margin-top: 20px; }
.team-list { list-style: none; margin: 16px 0 0; padding: 0; display: grid; gap: 10px; }
.team-row { display: flex; align-items: center; gap: 10px; }
.team-fields { flex: 1; display: grid; grid-template-columns: 1.4fr 1fr 66px 78px; gap: 8px; min-width: 0; }
.team-fields .effort { text-align: right; }
</style>
