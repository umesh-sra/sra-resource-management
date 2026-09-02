<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { allocationsApi, dashboardApi, projectsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Allocation, DashboardSummary, Project } from '@/types'
import { effortPerDay, fmtDate, fmtMoney, fmtPercent, isSameDay, isoDate, parseDate } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import AppAvatar from '@/components/AppAvatar.vue'

/**
 * Dashboard (screens/dashboard.png): a dated agenda down the middle with a
 * summary rail on the right. The reference's rail carries time-off, which
 * SRA-RMS does not model — it carries portfolio health here instead.
 */
const toast = useToastStore()

const month = ref(new Date(new Date().getFullYear(), new Date().getMonth(), 1))
const summary = ref<DashboardSummary | null>(null)
const allocations = ref<Allocation[]>([])
const projects = ref<Project[]>([])
const loading = ref(true)

const monthStart = computed(() => month.value)
const monthEnd = computed(() => new Date(month.value.getFullYear(), month.value.getMonth() + 1, 0))
const monthLabel = computed(() => month.value.toLocaleDateString('en-AU', { month: 'long', year: 'numeric' }))

/**
 * "Today" comes from the server once the summary loads, so the agenda's date
 * marker matches the business date the API computed its figures for. The
 * browser's local date is only the pre-fetch fallback: the two can differ, which
 * is precisely the bug the API-side BusinessClock fixes.
 */
const today = computed(() => (summary.value ? parseDate(summary.value.today) : new Date()))
const greeting = computed(() => `Have a great ${today.value.toLocaleDateString('en-AU', { weekday: 'long' })}`)

interface AgendaEvent {
  kind: 'project-start' | 'project-end' | 'joins' | 'rolls-off'
  title: string
  detail: string
  to: string
  who?: { name: string; imageUrl?: string }
}

/**
 * Everything that begins or ends inside the shown month, bucketed by day —
 * the portfolio's diary rather than one person's calendar.
 */
const agenda = computed(() => {
  const buckets = new Map<string, AgendaEvent[]>()
  const push = (iso: string, ev: AgendaEvent) => {
    if (iso < isoDate(monthStart.value) || iso > isoDate(monthEnd.value)) return
    const list = buckets.get(iso) ?? []
    list.push(ev)
    buckets.set(iso, list)
  }

  for (const p of projects.value) {
    push(p.startDate, {
      kind: 'project-start', title: `${p.name} starts`,
      detail: `${p.code} · ${p.clientName ?? ''}`, to: `/projects/${p.id}`,
    })
    push(p.endDate, {
      kind: 'project-end', title: `${p.name} ends`,
      detail: `${p.code} · ${p.clientName ?? ''}`, to: `/projects/${p.id}`,
    })
  }

  for (const a of allocations.value) {
    const effort = effortPerDay(a.effort, a.effortUnit)
    push(a.startDate, {
      kind: 'joins', title: `${a.resourceName} joins ${a.projectName}`,
      detail: `${effort}${a.roleOnProject ? ` · ${a.roleOnProject}` : ''}`,
      to: `/people/${a.resourceId}`, who: { name: a.resourceName ?? '' },
    })
    push(a.endDate, {
      kind: 'rolls-off', title: `${a.resourceName} rolls off ${a.projectName}`,
      detail: a.roleOnProject ?? '', to: `/people/${a.resourceId}`,
      who: { name: a.resourceName ?? '' },
    })
  }

  return [...buckets.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([iso, events]) => ({ iso, date: parseDate(iso), events }))
})

async function load() {
  loading.value = true
  try {
    const from = isoDate(monthStart.value)
    const to = isoDate(monthEnd.value)
    const [s, a, p] = await Promise.all([
      dashboardApi.summary(),
      allocationsApi.list({ from, to, pageSize: 200 }),
      projectsApi.list({ pageSize: 200, sort: 'startDate' }),
    ])
    summary.value = s
    allocations.value = a.items
    projects.value = p.items
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load the dashboard')
  } finally {
    loading.value = false
  }
}

function shiftMonth(n: number) {
  month.value = new Date(month.value.getFullYear(), month.value.getMonth() + n, 1)
  load()
}
function goToday() {
  month.value = new Date(today.value.getFullYear(), today.value.getMonth(), 1)
  load()
}

const KIND_CLASS: Record<AgendaEvent['kind'], string> = {
  'project-start': 'start',
  'project-end': 'end',
  joins: 'join',
  'rolls-off': 'off',
}

onMounted(load)
</script>

<template>
  <div class="toolbar">
    <h1 class="month">{{ monthLabel }}</h1>
    <div class="row row-nowrap" style="gap: 4px">
      <button class="icon-btn plain" aria-label="Previous month" @click="shiftMonth(-1)">
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" /></svg>
      </button>
      <button class="icon-btn plain" aria-label="Next month" @click="shiftMonth(1)">
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M9 6l6 6-6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" /></svg>
      </button>
    </div>
    <button class="btn" @click="goToday">Today</button>
    <div class="spacer" />
    <RouterLink class="btn" to="/schedule">Open Schedule</RouterLink>
  </div>

  <div class="dash">
    <!-- Agenda ---------------------------------------------------------- -->
    <section class="agenda" aria-label="Month agenda">
      <div v-if="loading" class="card card-pad"><span class="sr-only" role="status">Loading dashboard…</span><div class="skeleton" style="height: 200px" /></div>

      <template v-else-if="agenda.length">
        <div v-for="day in agenda" :key="day.iso" class="day">
          <div class="day-date" :class="{ today: isSameDay(day.date, today) }">
            <span class="dow">{{ day.date.toLocaleDateString('en-AU', { weekday: 'short' }) }}</span>
            <span class="dom">{{ day.date.getDate() }}</span>
          </div>
          <ul class="day-events">
            <li v-for="(ev, i) in day.events" :key="i">
              <RouterLink :to="ev.to" class="event" :class="KIND_CLASS[ev.kind]">
                <AppAvatar v-if="ev.who" :name="ev.who.name" :image-url="ev.who.imageUrl" :size="28" />
                <span class="event-text">
                  <span class="event-title">{{ ev.title }}</span>
                  <span v-if="ev.detail" class="event-detail">{{ ev.detail }}</span>
                </span>
              </RouterLink>
            </li>
          </ul>
        </div>
      </template>

      <div v-else class="card card-pad empty">Nothing starts or finishes in {{ monthLabel }}.</div>
    </section>

    <!-- Summary rail ----------------------------------------------------- -->
    <aside class="rail" aria-label="Portfolio summary">
      <div class="hello">
        <AppAvatar name="Dev User" :size="56" />
        <h2>{{ greeting }}</h2>
        <p class="muted">{{ fmtDate(isoDate(today)) }}</p>
      </div>

      <template v-if="summary">
        <div class="kpis">
          <RouterLink class="kpi" to="/projects">
            <span class="kpi-value">{{ summary.activeProjects }}</span>
            <span class="kpi-label">Active projects</span>
          </RouterLink>
          <RouterLink class="kpi" to="/people">
            <span class="kpi-value">{{ summary.totalResources }}</span>
            <span class="kpi-label">People</span>
          </RouterLink>
          <RouterLink class="kpi" to="/reports">
            <span class="kpi-value">{{ fmtPercent(summary.averageUtilisation) }}</span>
            <span class="kpi-label">Avg utilisation</span>
          </RouterLink>
          <RouterLink class="kpi" to="/schedule">
            <span class="kpi-value" :class="{ danger: summary.overAllocatedResources > 0 }">{{ summary.overAllocatedResources }}</span>
            <span class="kpi-label">Over-allocated</span>
          </RouterLink>
          <RouterLink class="kpi" to="/schedule">
            <span class="kpi-value">{{ summary.underAllocatedResources }}</span>
            <span class="kpi-label">Under 50%</span>
          </RouterLink>
          <RouterLink class="kpi" to="/projects">
            <span class="kpi-value small">{{ fmtMoney(summary.budgetAtRisk) }}</span>
            <span class="kpi-label">Budget at risk</span>
          </RouterLink>
        </div>

        <h2 class="rail-head">Upcoming project starts</h2>
        <ul class="rail-list">
          <li v-for="p in summary.upcomingProjectStarts" :key="p.id">
            <RouterLink :to="`/projects/${p.id}`" class="rail-item">
              <span class="rail-title">{{ p.name }}</span>
              <span class="rail-sub">{{ p.clientName }} · {{ fmtDate(p.startDate) }}</span>
            </RouterLink>
          </li>
          <li v-if="!summary.upcomingProjectStarts.length" class="muted">Nothing on the horizon.</li>
        </ul>

        <h2 class="rail-head">Upcoming roll-offs</h2>
        <ul class="rail-list">
          <li v-for="a in summary.upcomingRollOffs" :key="a.id">
            <RouterLink :to="`/people/${a.resourceId}`" class="rail-item">
              <span class="rail-title">{{ a.resourceName }}</span>
              <span class="rail-sub">{{ a.projectName }} · ends {{ fmtDate(a.endDate) }}</span>
            </RouterLink>
          </li>
          <li v-if="!summary.upcomingRollOffs.length" class="muted">No one rolling off soon.</li>
        </ul>
      </template>
    </aside>
  </div>
</template>

<style scoped>
.month { font-size: 22px; }
.dash { display: grid; grid-template-columns: minmax(0, 1fr) 330px; gap: 0; align-items: start; }
.agenda { padding: 20px 26px; min-width: 0; }

.day { display: flex; gap: 16px; margin-bottom: 14px; }
.day-date { width: 44px; flex-shrink: 0; text-align: center; padding-top: 6px; }
.dow { display: block; font-size: 11.5px; color: var(--text-muted); }
.dom { display: block; font-size: 17px; font-weight: 650; color: var(--gray-900); line-height: 1.5; }
.day-date.today .dom { background: var(--accent); color: #fff; border-radius: 50%; width: 30px; height: 30px; line-height: 30px; margin: 0 auto; }

.day-events { list-style: none; margin: 0; padding: 0; flex: 1; display: grid; gap: 8px; min-width: 0; }
.event {
  display: flex; align-items: center; gap: 10px; background: var(--surface); border: 1px solid var(--border);
  border-left-width: 4px; border-radius: var(--radius-sm); padding: 10px 14px; color: inherit; box-shadow: var(--shadow-sm);
}
.event:hover { text-decoration: none; background: var(--gray-50); }
.event.start { border-left-color: var(--green-600); }
.event.end { border-left-color: var(--gray-400); }
.event.join { border-left-color: var(--brand-500); }
.event.off { border-left-color: var(--accent); }
.event-text { min-width: 0; }
.event-title { display: block; font-weight: 600; color: var(--gray-900); font-size: 13.5px; }
.event-detail { display: block; color: var(--text-muted); font-size: 12.5px; }

.rail { border-left: 1px solid var(--border); background: var(--surface); padding: 22px 20px 40px; min-height: calc(100vh - var(--toolbar-h)); position: sticky; top: var(--toolbar-h); }
.hello { text-align: center; padding-bottom: 20px; border-bottom: 1px solid var(--border); }
.hello h2 { margin-top: 12px; font-size: 17px; }
.hello p { margin: 4px 0 0; font-size: 12.5px; }

.kpis { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin: 18px 0 8px; }
.kpi { border: 1px solid var(--border); border-radius: var(--radius-sm); padding: 12px; color: inherit; display: block; }
.kpi:hover { text-decoration: none; background: var(--gray-50); }
.kpi-value { display: block; font-size: 22px; font-weight: 680; color: var(--brand-800); line-height: 1.15; }
.kpi-value.small { font-size: 15px; padding: 4px 0; }
.kpi-value.danger { color: var(--red-600); }
.kpi-label { display: block; font-size: 11.5px; color: var(--text-muted); margin-top: 2px; }

.rail-head { font-size: 13px; text-transform: uppercase; letter-spacing: .04em; color: var(--text-muted); margin: 24px 0 10px; }
.rail-list { list-style: none; margin: 0; padding: 0; display: grid; gap: 9px; }
.rail-item { display: block; color: inherit; border-left: 3px solid var(--brand-400); padding-left: 10px; }
.rail-item:hover { text-decoration: none; }
.rail-item:hover .rail-title { text-decoration: underline; }
.rail-title { display: block; font-weight: 600; color: var(--brand-700); font-size: 13px; }
.rail-sub { display: block; color: var(--text-muted); font-size: 12px; }

@media (max-width: 1080px) {
  .dash { grid-template-columns: 1fr; }
  .rail { border-left: 0; border-top: 1px solid var(--border); min-height: 0; position: static; }
}
</style>
