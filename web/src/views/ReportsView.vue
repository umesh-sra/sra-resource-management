<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { allocationsApi, projectsApi, reportsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Allocation, Project, UtilisationReport } from '@/types'
import { addDays, fmtDate, fmtHours, fmtMoney, isoDate, parseDate } from '@/lib/format'
import { useToastStore } from '@/stores/toast'

/**
 * Reports (screens/reports.png): a standard-report rail on the left, and a
 * chart + headline figures + detail table on the right.
 *
 * Only `/v1/reports/utilisation` exists server-side; the other two entries are
 * derived client-side from the projects and allocations endpoints. Reports the
 * reference offers but SRA-RMS has no data for (timesheet actuals, time off)
 * are listed as unavailable rather than faked.
 */
const route = useRoute()
const router = useRouter()
const toast = useToastStore()

type ReportId = 'utilisation' | 'projects' | 'bookings'

const REPORTS: { id: ReportId; label: string; blurb: string }[] = [
  { id: 'utilisation', label: 'Utilisation & Capacity', blurb: 'Allocated hours against availability, per person.' },
  { id: 'projects', label: 'Projects Overview', blurb: 'Budget, spend and team size across the portfolio.' },
  { id: 'bookings', label: 'Bookings by Client', blurb: 'Weekly allocated hours grouped by client.' },
]

const UNAVAILABLE = [
  { label: 'Scheduled & Actuals', why: 'needs timesheet actuals' },
  { label: 'Individual Project', why: 'covered by the project page' },
  { label: 'Time Off', why: 'leave is not modelled' },
]

const report = computed<ReportId>(() => {
  const id = route.query.report as string | undefined
  return REPORTS.some((r) => r.id === id) ? (id as ReportId) : 'utilisation'
})
const current = computed(() => REPORTS.find((r) => r.id === report.value)!)

const today = new Date()
const from = ref(isoDate(new Date(today.getFullYear(), today.getMonth(), 1)))
const to = ref(isoDate(new Date(today.getFullYear(), today.getMonth() + 3, 0)))

const util = ref<UtilisationReport | null>(null)
const projects = ref<Project[]>([])
const allocations = ref<Allocation[]>([])
const loading = ref(false)

function select(id: ReportId) {
  router.push({ path: '/reports', query: { report: id } })
}

function shiftRange(direction: -1 | 1) {
  const span = Math.max(1, Math.round((parseDate(to.value).getTime() - parseDate(from.value).getTime()) / 86_400_000) + 1)
  from.value = isoDate(addDays(parseDate(from.value), direction * span))
  to.value = isoDate(addDays(parseDate(to.value), direction * span))
  run()
}

async function run() {
  if (to.value < from.value) { toast.error('"To" must be on or after "From".'); return }
  loading.value = true
  try {
    if (report.value === 'utilisation') {
      util.value = await reportsApi.utilisation({ from: from.value, to: to.value })
    } else if (report.value === 'projects') {
      projects.value = (await projectsApi.list({ pageSize: 200, sort: 'name' })).items
    } else {
      const [a, p] = await Promise.all([
        allocationsApi.list({ from: from.value, to: to.value, pageSize: 200 }),
        projectsApi.list({ pageSize: 200, sort: 'name' }),
      ])
      allocations.value = a.items
      projects.value = p.items
    }
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to generate the report')
  } finally {
    loading.value = false
  }
}

watch(report, run)

// ---- Utilisation ---------------------------------------------------------
const utilTotals = computed(() => {
  const rows = util.value?.rows ?? []
  const capacity = rows.reduce((s, r) => s + r.availableHours, 0)
  const scheduled = rows.reduce((s, r) => s + r.allocatedHours, 0)
  const remaining = rows.reduce((s, r) => s + Math.max(0, r.availableHours - r.allocatedHours), 0)
  const over = rows.filter((r) => r.utilisation > 1).length
  // Part of `scheduled`, never subtracted from it (FR-REP-7).
  const unconfirmed = rows.reduce((s, r) => s + (r.unconfirmedHours ?? 0), 0)
  return {
    capacity, scheduled, remaining, over, unconfirmed,
    utilisation: capacity ? scheduled / capacity : 0,
  }
})

/** Busiest people first — a full bar chart of 200 people reads as noise. */
const chartRows = computed(() =>
  [...(util.value?.rows ?? [])].sort((a, b) => b.utilisation - a.utilisation).slice(0, 24),
)

// ---- Projects overview ---------------------------------------------------
const projectTotals = computed(() => {
  const budget = projects.value.reduce((s, p) => s + (p.budget ?? 0), 0)
  const remaining = projects.value.reduce((s, p) => s + (p.remaining ?? 0), 0)
  const atRisk = projects.value.filter(
    (p) => p.budget != null && p.remaining != null && p.budget > 0 && p.remaining / p.budget <= 0.1,
  ).length
  return { budget, remaining, spent: budget - remaining, atRisk }
})

// ---- Bookings by client --------------------------------------------------
const bookings = computed(() => {
  const byId = new Map(projects.value.map((p) => [p.id, p]))
  const groups = new Map<string, { client: string; hours: number; people: Set<string>; projects: Set<string> }>()
  for (const a of allocations.value) {
    const p = byId.get(a.projectId)
    const client = p?.clientName ?? 'Unassigned'
    const g = groups.get(client) ?? { client, hours: 0, people: new Set(), projects: new Set() }
    // Percent effort is a share of a 38-hour week, matching the API's default availability.
    g.hours += a.effortUnit === 'percent' ? (a.effort / 100) * 38 : a.effort
    g.people.add(a.resourceId)
    g.projects.add(a.projectId)
    groups.set(client, g)
  }
  return [...groups.values()].sort((a, b) => b.hours - a.hours)
})
const bookingsMax = computed(() => Math.max(1, ...bookings.value.map((b) => b.hours)))

const csvUrl = computed(() => reportsApi.utilisationCsvUrl({ from: from.value, to: to.value }))

onMounted(run)
</script>

<template>
  <div class="reports">
    <!-- Report rail ----------------------------------------------------- -->
    <nav class="rail" aria-label="Reports">
      <h1 class="rail-title">Reports</h1>

      <h2 class="rail-head">Standard Reports</h2>
      <ul class="rail-list">
        <li v-for="r in REPORTS" :key="r.id">
          <button class="rail-link" :class="{ active: report === r.id }" :aria-current="report === r.id ? 'page' : undefined" @click="select(r.id)">
            {{ r.label }}
          </button>
        </li>
      </ul>

      <h2 class="rail-head">Not available</h2>
      <ul class="rail-list">
        <li v-for="u in UNAVAILABLE" :key="u.label" class="rail-off">
          {{ u.label }}<span class="why">{{ u.why }}</span>
        </li>
      </ul>
    </nav>

    <!-- Report body ------------------------------------------------------ -->
    <section class="body">
      <div class="toolbar">
        <h2 class="rep-title">{{ current.label }}</h2>
        <a v-if="report === 'utilisation'" class="btn" :href="csvUrl" target="_blank" rel="noopener">
          Export CSV<span class="sr-only"> (opens in a new tab)</span>
        </a>
        <div class="spacer" />
        <button class="icon-btn plain" aria-label="Previous period" @click="shiftRange(-1)">
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" /></svg>
        </button>
        <label class="sr-only" for="rep-from">From</label>
        <input id="rep-from" class="input" style="max-width: 160px" type="date" v-model="from" @change="run" />
        <label class="sr-only" for="rep-to">To</label>
        <input id="rep-to" class="input" style="max-width: 160px" type="date" v-model="to" @change="run" />
        <button class="icon-btn plain" aria-label="Next period" @click="shiftRange(1)">
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M9 6l6 6-6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" /></svg>
        </button>
      </div>

      <div class="page">
        <p class="blurb">{{ current.blurb }} <span class="muted">{{ fmtDate(from) }} – {{ fmtDate(to) }}</span></p>

        <div v-if="loading" class="card card-pad"><span class="sr-only" role="status">Generating report…</span><div class="skeleton" style="height: 260px" /></div>

        <!-- Utilisation & Capacity ------------------------------------- -->
        <template v-else-if="report === 'utilisation'">
          <div class="card card-pad">
            <div class="legend">
              <span><i class="sw sched" />Scheduled</span>
              <span><i class="sw over" />Over capacity</span>
              <span><i class="sw cap" />Remaining capacity</span>
            </div>
            <div v-if="chartRows.length" class="chart" role="img" :aria-label="`Utilisation by person, ${chartRows.length} busiest people shown`">
              <div v-for="r in chartRows" :key="r.resourceId" class="bar-col" :title="`${r.resourceName}: ${Math.round(r.utilisation * 100)}%`">
                <div class="bar-track">
                  <div class="bar-fill" :class="{ over: r.utilisation > 1 }" :style="{ height: `${Math.min(r.utilisation, 1) * 100}%` }" />
                </div>
                <span class="bar-label">{{ r.resourceName.split(' ')[0] }}</span>
              </div>
            </div>
            <p v-else class="empty">No utilisation data in this range.</p>
          </div>

          <div class="figures">
            <div><span class="fig-label">Scheduled Utilisation</span><span class="fig-value">{{ Math.round(utilTotals.utilisation * 100) }}%</span></div>
            <div><span class="fig-label">Scheduled, Total</span><span class="fig-value">{{ fmtHours(utilTotals.scheduled) }}</span></div>
            <div><span class="fig-label">Effective Capacity</span><span class="fig-value">{{ fmtHours(utilTotals.capacity) }}</span></div>
            <div><span class="fig-label">Remaining Capacity</span><span class="fig-value">{{ fmtHours(utilTotals.remaining) }}</span></div>
            <div>
              <span class="fig-label">Not yet firm</span>
              <span class="fig-value" :class="{ warn: utilTotals.unconfirmed > 0 }">{{ fmtHours(utilTotals.unconfirmed) }}</span>
              <span class="fig-hint muted">of the scheduled total</span>
            </div>
            <div><span class="fig-label">Over-allocated</span><span class="fig-value" :class="{ danger: utilTotals.over > 0 }">{{ utilTotals.over }}</span></div>
          </div>

          <div class="card">
            <div class="table-wrap">
              <table class="table">
                <thead>
                  <tr>
                    <th scope="col">Person</th><th scope="col">Department</th>
                    <th scope="col" class="num">Capacity (h)</th><th scope="col" class="num">Scheduled (h)</th>
                    <th scope="col" class="num">Not yet firm (h)</th>
                    <th scope="col" style="width: 230px">Utilisation</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="r in util?.rows" :key="r.resourceId" class="clickable" @click="router.push(`/people/${r.resourceId}`)">
                    <td><RouterLink :to="`/people/${r.resourceId}`" class="row-link" @click.stop>{{ r.resourceName }}</RouterLink></td>
                    <td>{{ r.department ?? '—' }}</td>
                    <td class="num">{{ r.availableHours.toLocaleString() }}</td>
                    <td class="num">{{ r.allocatedHours.toLocaleString() }}</td>
                    <td class="num" :class="{ 'warn-text': (r.unconfirmedHours ?? 0) > 0 }">
                      {{ r.unconfirmedHours ? r.unconfirmedHours.toLocaleString() : '—' }}
                    </td>
                    <td>
                      <div class="row row-nowrap" style="gap: 10px">
                        <div class="ubar" :class="{ over: r.utilisation > 1 }" style="flex: 1" aria-hidden="true">
                          <span :style="{ width: `${Math.min(r.utilisation * 100, 100)}%` }" />
                        </div>
                        <span :class="r.utilisation > 1 ? 'warn-text' : 'muted'" style="min-width: 42px; text-align: right">
                          {{ Math.round(r.utilisation * 100) }}%<span v-if="r.utilisation > 1" class="sr-only"> — over-allocated</span>
                        </span>
                      </div>
                    </td>
                  </tr>
                  <tr v-if="util && !util.rows.length"><td colspan="6" class="empty">No data for this range.</td></tr>
                </tbody>
              </table>
            </div>
          </div>
        </template>

        <!-- Projects Overview ------------------------------------------- -->
        <template v-else-if="report === 'projects'">
          <div class="figures">
            <div><span class="fig-label">Projects</span><span class="fig-value">{{ projects.length }}</span></div>
            <div><span class="fig-label">Total Budget</span><span class="fig-value">{{ fmtMoney(projectTotals.budget) }}</span></div>
            <div><span class="fig-label">Consumed</span><span class="fig-value">{{ fmtMoney(projectTotals.spent) }}</span></div>
            <div><span class="fig-label">Remaining</span><span class="fig-value">{{ fmtMoney(projectTotals.remaining) }}</span></div>
            <div><span class="fig-label">≥90% consumed</span><span class="fig-value" :class="{ danger: projectTotals.atRisk > 0 }">{{ projectTotals.atRisk }}</span></div>
          </div>

          <div class="card">
            <div class="table-wrap">
              <table class="table">
                <thead>
                  <tr>
                    <th scope="col">Project</th><th scope="col">Client</th><th scope="col">Dates</th>
                    <th scope="col" class="num">Budget</th><th scope="col" class="num">Remaining</th>
                    <th scope="col" class="num">Team</th><th scope="col">Billable</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="p in projects" :key="p.id" class="clickable" @click="router.push(`/projects/${p.id}`)">
                    <td><RouterLink :to="`/projects/${p.id}`" class="row-link" @click.stop>{{ p.code }} — {{ p.name }}</RouterLink></td>
                    <td>{{ p.clientName }}</td>
                    <td>{{ fmtDate(p.startDate) }} – {{ fmtDate(p.endDate) }}</td>
                    <td class="num">{{ fmtMoney(p.budget) }}</td>
                    <td class="num">{{ fmtMoney(p.remaining) }}</td>
                    <td class="num">{{ p.team.length }}</td>
                    <td>{{ p.billable ? 'Yes' : 'No' }}</td>
                  </tr>
                  <tr v-if="!projects.length"><td colspan="7" class="empty">No projects.</td></tr>
                </tbody>
              </table>
            </div>
          </div>
        </template>

        <!-- Bookings by Client ------------------------------------------ -->
        <template v-else>
          <div class="card">
            <div class="table-wrap">
              <table class="table">
                <thead>
                  <tr>
                    <th scope="col">Client</th><th scope="col" class="num">Projects</th><th scope="col" class="num">People</th>
                    <th scope="col" class="num">Hours / week</th><th scope="col" style="width: 260px">Share</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="b in bookings" :key="b.client">
                    <td><strong>{{ b.client }}</strong></td>
                    <td class="num">{{ b.projects.size }}</td>
                    <td class="num">{{ b.people.size }}</td>
                    <td class="num">{{ Math.round(b.hours) }}</td>
                    <td>
                      <div class="ubar" aria-hidden="true"><span :style="{ width: `${(b.hours / bookingsMax) * 100}%` }" /></div>
                    </td>
                  </tr>
                  <tr v-if="!bookings.length"><td colspan="5" class="empty">No allocations in this range.</td></tr>
                </tbody>
              </table>
            </div>
          </div>
          <p class="muted foot">Percent-based effort is converted at 38 hours per week.</p>
        </template>
      </div>
    </section>
  </div>
</template>

<style scoped>
.reports { display: grid; grid-template-columns: 244px minmax(0, 1fr); align-items: start; }
.rail { border-right: 1px solid var(--border); background: var(--surface); min-height: 100vh; padding: 20px 16px 40px; position: sticky; top: 0; }
.rail-title { font-size: 18px; margin-bottom: 20px; }
.rail-head { font-size: 12px; text-transform: uppercase; letter-spacing: .04em; color: var(--text-muted); margin: 20px 0 8px; }
.rail-list { list-style: none; margin: 0; padding: 0; }
.rail-link {
  width: 100%; text-align: left; background: none; border: 0; font: inherit; font-size: 13.5px;
  padding: 9px 11px; border-radius: var(--radius-sm); cursor: pointer; color: var(--text);
}
.rail-link:hover { background: var(--gray-50); }
.rail-link.active { background: var(--brand-100); color: var(--brand-700); font-weight: 600; }
.rail-off { padding: 8px 11px; color: var(--gray-400); font-size: 13px; }
.rail-off .why { display: block; font-size: 11.5px; font-style: italic; }

.body { min-width: 0; }
.rep-title { font-size: 17px; }
.blurb { margin: 0 0 16px; font-size: 13px; }

.legend { display: flex; gap: 18px; font-size: 12.5px; color: var(--text-muted); margin-bottom: 16px; }
.sw { display: inline-block; width: 11px; height: 11px; border-radius: 3px; margin-right: 6px; vertical-align: -1px; }
.sw.sched { background: var(--brand-500); }
.sw.over { background: var(--red-600); }
.sw.cap { background: var(--gray-200); }

.chart { display: flex; align-items: flex-end; justify-content: flex-start; gap: 6px; height: 240px; overflow-x: auto; padding-bottom: 4px; }
/* Cap the width so a handful of people render as bars rather than as slabs
   filling the whole plot; the row stays left-aligned as it fills up. */
.bar-col { flex: 1 0 42px; max-width: 76px; display: flex; flex-direction: column; align-items: center; height: 100%; }
.bar-track { width: 100%; flex: 1; background: var(--gray-200); border-radius: 4px; display: flex; align-items: flex-end; overflow: hidden; }
.bar-fill { width: 100%; background: var(--brand-500); border-radius: 4px 4px 0 0; min-height: 2px; }
.bar-fill.over { background: var(--red-600); }
.bar-label { font-size: 10.5px; color: var(--text-muted); margin-top: 6px; max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.figures {
  display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius);
  margin: 16px 0; box-shadow: var(--shadow-sm);
}
.figures > div { padding: 16px 18px; border-left: 1px solid var(--border); }
.figures > div:first-child { border-left: 0; }
.fig-label { display: block; color: var(--text-muted); font-size: 12px; }
.fig-value { display: block; font-size: 24px; font-weight: 680; color: var(--brand-800); margin-top: 4px; }
.fig-value.danger { color: var(--red-600); }
.fig-value.warn { color: var(--amber-700); }
.fig-hint { display: block; font-size: 11.5px; margin-top: 3px; }
.foot { font-size: 12.5px; margin-top: 12px; }

@media (max-width: 900px) {
  .reports { grid-template-columns: 1fr; }
  .rail { min-height: 0; position: static; border-right: 0; border-bottom: 1px solid var(--border); }
}
</style>
