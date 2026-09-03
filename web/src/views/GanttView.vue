<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { clientsApi, dashboardApi, projectsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Client, GanttResponse, GanttRow, GanttView, Project } from '@/types'
import {
  addDays, bookingStatusLabel, isUnconfirmed, isoDate, parseDate, projectStatus, startOfWeek,
} from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import TimelineGrid from '@/components/TimelineGrid.vue'

/**
 * Project Gantt (FR-GANTT-*), backed by `GET /v1/dashboard/gantt`. The same
 * screen switches between the projects and resources views; Schedule is the
 * day-level people view of the latter.
 */
const route = useRoute()
const toast = useToastStore()

type Zoom = 'weeks' | 'months' | 'quarters'
const ZOOM: Record<Zoom, { dayWidth: number; span: number; label: string }> = {
  weeks: { dayWidth: 18, span: 70, label: 'Weeks' },
  months: { dayWidth: 8, span: 182, label: 'Months' },
  quarters: { dayWidth: 3, span: 366, label: 'Year' },
}

const view = ref<GanttView>('projects')
const zoom = ref<Zoom>('months')
const anchor = ref(startOfWeek(addDays(new Date(), -14)))
const clientFilter = ref((route.query.clientId as string) ?? '')
const q = ref('')

const data = ref<GanttResponse | null>(null)
const clients = ref<Client[]>([])
const projects = ref<Project[]>([])
const loading = ref(true)

const from = computed(() => isoDate(anchor.value))
const to = computed(() => isoDate(addDays(anchor.value, ZOOM[zoom.value].span - 1)))

const rangeLabel = computed(() => {
  const fmt = (iso: string) =>
    parseDate(iso).toLocaleDateString('en-AU', { month: 'short', year: 'numeric' })
  return `${fmt(from.value)} – ${fmt(to.value)}`
})

const projectById = computed(() => new Map(projects.value.map((p) => [p.id, p])))

const rows = computed<GanttRow[]>(() => {
  const rowsIn = data.value?.rows ?? []
  const term = q.value.trim().toLowerCase()
  if (!term) return rowsIn
  return rowsIn.filter(
    (r) => r.label.toLowerCase().includes(term) || r.bars.some((b) => (b.label ?? '').toLowerCase().includes(term)),
  )
})

async function load() {
  loading.value = true
  try {
    data.value = await dashboardApi.gantt({
      view: view.value, from: from.value, to: to.value,
      clientId: view.value === 'projects' && clientFilter.value ? clientFilter.value : undefined,
    })
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load the Gantt chart')
  } finally {
    loading.value = false
  }
}

async function loadReference() {
  try {
    const [c, p] = await Promise.all([
      clientsApi.list({ pageSize: 200, sort: 'name' }),
      projectsApi.list({ pageSize: 200, sort: 'name' }),
    ])
    clients.value = c.items
    projects.value = p.items
  } catch {
    // Reference data only enriches the row labels — the chart still renders.
  }
}

function shift(direction: -1 | 1) {
  anchor.value = addDays(anchor.value, direction * Math.round(ZOOM[zoom.value].span / 3))
  load()
}
function goToday() { anchor.value = startOfWeek(addDays(new Date(), -14)); load() }

watch([view, zoom, clientFilter], load)

onMounted(() => { loadReference(); load() })
</script>

<template>
  <div class="toolbar">
    <h1 class="sr-only">Gantt charts</h1>
    <div class="segmented" role="group" aria-label="Gantt view">
      <button :aria-pressed="view === 'projects'" @click="view = 'projects'">Projects</button>
      <button :aria-pressed="view === 'resources'" @click="view = 'resources'">People</button>
    </div>

    <button class="btn" @click="goToday">Today</button>
    <div class="row row-nowrap" style="gap: 4px">
      <button class="icon-btn plain" aria-label="Previous period" @click="shift(-1)">
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" /></svg>
      </button>
      <button class="icon-btn plain" aria-label="Next period" @click="shift(1)">
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M9 6l6 6-6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" /></svg>
      </button>
    </div>
    <strong class="range">{{ rangeLabel }}</strong>

    <div class="segmented" role="group" aria-label="Zoom">
      <button v-for="(cfg, key) in ZOOM" :key="key" :aria-pressed="zoom === key" @click="zoom = key as Zoom">{{ cfg.label }}</button>
    </div>

    <div class="spacer" />

    <label class="search">
      <span class="sr-only">Search the chart</span>
      <input class="input" type="search" v-model="q" placeholder="Search…" />
      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M10 4a6 6 0 1 1 0 12 6 6 0 0 1 0-12Zm10 16-5.2-5.2" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" /></svg>
    </label>

    <select v-if="view === 'projects'" class="select" style="max-width: 220px" v-model="clientFilter" aria-label="Filter by client">
      <option value="">All clients</option>
      <option v-for="c in clients" :key="c.id" :value="c.id">{{ c.name }}</option>
    </select>
  </div>

  <div class="page">
    <TimelineGrid
      :rows="rows" :from="from" :to="to" :day-width="ZOOM[zoom].dayWidth" :label-width="280"
      :loading="loading"
      :empty-text="view === 'projects' ? 'No projects run in this window.' : 'No one is allocated in this window.'"
    >
      <template #label-head>
        <span class="muted" style="font-size: 12px; text-transform: uppercase; letter-spacing: .03em; font-weight: 600">
          {{ rows.length }} {{ view === 'projects' ? 'projects' : 'people' }}
        </span>
      </template>

      <template #row-label="{ row }">
        <RouterLink v-if="view === 'projects'" :to="`/projects/${row.id}`" class="lbl">
          <span class="lbl-name">{{ row.label }}</span>
          <span class="lbl-sub">
            {{ projectById.get(row.id)?.clientName ?? '' }}
            <span v-if="projectById.get(row.id)" class="badge" :class="projectStatus(projectById.get(row.id)!.status).class">
              {{ projectStatus(projectById.get(row.id)!.status).label }}
            </span>
          </span>
        </RouterLink>
        <RouterLink v-else :to="`/people/${row.id}`" class="lbl">
          <span class="lbl-name">{{ row.label }}</span>
        </RouterLink>
      </template>

      <template #bar="{ bar }">
        {{ bar.label }}<template v-if="isUnconfirmed(bar.bookingStatus)">
          ({{ bookingStatusLabel(bar.bookingStatus!).toLowerCase() }})</template>
      </template>
    </TimelineGrid>

    <p class="muted legend">
      Dashed bars are bookings that are not yet firm (FR-ALL-9). They still count toward capacity,
      so a provisional booking that would over-allocate someone is still shown in red.
    </p>
  </div>
</template>

<style scoped>
.range { font-size: 15px; color: var(--gray-900); min-width: 180px; }
.lbl { color: inherit; display: block; min-width: 0; }
.lbl:hover { text-decoration: none; }
.lbl:hover .lbl-name { text-decoration: underline; }
.lbl-name { display: block; font-weight: 600; color: var(--brand-700); font-size: 13px; }
.lbl-sub { display: flex; align-items: center; gap: 6px; color: var(--text-muted); font-size: 12px; margin-top: 3px; }
.legend { font-size: 12.5px; margin: 12px 0 0; }
</style>
