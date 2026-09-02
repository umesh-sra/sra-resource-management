<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { allocationsApi, dashboardApi, resourcesApi, timeOffApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Allocation, GanttBar, GanttResponse, GanttRow, Resource } from '@/types'
import { addDays, effortPerDay, isoDate, parseDate, startOfWeek, timeOffLabel } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import TimelineGrid from '@/components/TimelineGrid.vue'
import AppAvatar from '@/components/AppAvatar.vue'
import AllocationEditModal from '@/components/AllocationEditModal.vue'
import ScheduleEntryModal from '@/components/ScheduleEntryModal.vue'

/**
 * People-by-day schedule (screens/schedule.png): one row per resource, one bar
 * per allocation, backed by `GET /v1/dashboard/gantt?view=resources`.
 */
const toast = useToastStore()

type Zoom = 'day' | 'week' | 'month'
const ZOOM: Record<Zoom, { dayWidth: number; span: number; label: string }> = {
  day: { dayWidth: 40, span: 21, label: 'Days' },
  week: { dayWidth: 22, span: 56, label: 'Weeks' },
  month: { dayWidth: 9, span: 182, label: 'Months' },
}

const zoom = ref<Zoom>('week')
const anchor = ref(startOfWeek(new Date()))
const q = ref('')
const departmentFilter = ref('')

const data = ref<GanttResponse | null>(null)
const resources = ref<Resource[]>([])
const loading = ref(true)
const editAlloc = ref<Allocation | null>(null)

/** Day cell picked on the grid — opens the Booking / Time Off sheet for that person and day. */
const entry = ref<{ resourceId?: string; date: string } | null>(null)

const from = computed(() => isoDate(anchor.value))
const to = computed(() => isoDate(addDays(anchor.value, ZOOM[zoom.value].span - 1)))

const rangeLabel = computed(() => {
  const a = parseDate(from.value)
  const b = parseDate(to.value)
  const fmt = (d: Date, withYear: boolean) =>
    d.toLocaleDateString('en-AU', { day: 'numeric', month: 'short', ...(withYear ? { year: 'numeric' } : {}) })
  return `${fmt(a, a.getFullYear() !== b.getFullYear())} – ${fmt(b, true)}`
})

/** Resource metadata (photo, job title, location) is not on the Gantt payload. */
const byId = computed(() => new Map(resources.value.map((r) => [r.id, r])))

const departments = computed(() =>
  [...new Set(resources.value.map((r) => r.department).filter(Boolean) as string[])].sort(),
)

const rows = computed<GanttRow[]>(() => {
  const rowsIn = data.value?.rows ?? []
  const term = q.value.trim().toLowerCase()
  if (!term) return rowsIn
  return rowsIn.filter((r) => {
    const meta = byId.value.get(r.id)
    return r.label.toLowerCase().includes(term)
      || (meta?.primaryJobTitle ?? '').toLowerCase().includes(term)
      || r.bars.some((b) => (b.label ?? '').toLowerCase().includes(term))
  })
})

async function load() {
  loading.value = true
  try {
    const [gantt, people, leave] = await Promise.all([
      dashboardApi.gantt({
        view: 'resources', from: from.value, to: to.value,
        department: departmentFilter.value || undefined,
      }),
      resources.value.length
        ? Promise.resolve({ items: resources.value })
        : resourcesApi.list({ pageSize: 200, sort: 'name' }),
      timeOffApi.list({ from: from.value, to: to.value, pageSize: 200 }),
    ])

    // The gantt endpoint returns allocations only; merge leave in as extra bars
    // so a person's row shows work and time off together (FR-TIMEOFF-5).
    const leaveByResource = new Map<string, GanttBar[]>()
    for (const t of leave.items) {
      const bar: GanttBar = {
        refId: t.id,
        label: t.hoursPerDay ? `${timeOffLabel(t.type)} (${t.hoursPerDay}h/day)` : timeOffLabel(t.type),
        start: t.startDate,
        end: t.endDate,
        kind: 'timeOff',
      }
      const list = leaveByResource.get(t.resourceId) ?? []
      list.push(bar)
      leaveByResource.set(t.resourceId, list)
    }

    data.value = {
      ...gantt,
      rows: gantt.rows.map((r) => {
        const extra = leaveByResource.get(r.id)
        return extra ? { ...r, bars: [...r.bars, ...extra] } : r
      }),
    }
    resources.value = people.items
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load the schedule')
  } finally {
    loading.value = false
  }
}

function shift(direction: -1 | 1) {
  anchor.value = addDays(anchor.value, direction * Math.round(ZOOM[zoom.value].span / 2))
  load()
}
function goToday() { anchor.value = startOfWeek(new Date()); load() }

watch(zoom, load)
watch(departmentFilter, load)

/** Bars carry the allocation id, so clicking one opens that allocation. */
async function openBar(bar: GanttBar) {
  if (!bar.refId) return
  // Leave bars carry a time-off id, not an allocation id — they are not editable here.
  if (bar.kind === 'timeOff') return
  try {
    const page = await allocationsApi.list({ pageSize: 200 })
    const found = page.items.find((a) => a.id === bar.refId)
    if (found) editAlloc.value = found
    else toast.error('That allocation is no longer available.')
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not open the allocation')
  }
}

/** Clicking a day opens the reference's Booking / Time Off sheet (screens/shedule_*.png). */
function openCell(row: GanttRow, date: string) {
  entry.value = { resourceId: row.id, date }
}

/** Keyboard equivalent of picking a cell: the row's own "add" action. */
function openRow(row: GanttRow) {
  const clamped = from.value <= isoDate(new Date()) && isoDate(new Date()) <= to.value
    ? isoDate(new Date())
    : from.value
  entry.value = { resourceId: row.id, date: clamped }
}

function barText(bar: GanttBar, row: GanttRow): string {
  const meta = byId.value.get(row.id)
  const effort = bar.effort != null
    ? effortPerDay(
        bar.effort, bar.effortUnit ?? 'hoursPerWeek',
        Number(meta?.availabilityHoursPerWeek ?? 38), meta?.workingDays?.length || 5,
      )
    : ''
  return effort ? `${effort} · ${bar.label ?? ''}` : (bar.label ?? '')
}

onMounted(load)
</script>

<template>
  <div class="toolbar">
    <h1 class="sr-only">Schedule</h1>
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
      <span class="sr-only">Search people or projects</span>
      <input class="input" type="search" v-model="q" placeholder="Search…" />
      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M10 4a6 6 0 1 1 0 12 6 6 0 0 1 0-12Zm10 16-5.2-5.2" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" /></svg>
    </label>

    <select class="select" style="max-width: 190px" v-model="departmentFilter" aria-label="Filter by department">
      <option value="">All departments</option>
      <option v-for="d in departments" :key="d" :value="d">{{ d }}</option>
    </select>
  </div>

  <div class="page">
    <TimelineGrid
      :rows="rows" :from="from" :to="to" :day-width="ZOOM[zoom].dayWidth"
      :loading="loading" empty-text="No one is scheduled in this window." cell-clickable
      @bar-click="openBar" @cell-click="openCell"
    >
      <template #label-head>
        <span class="muted" style="font-size: 12px; text-transform: uppercase; letter-spacing: .03em; font-weight: 600">
          {{ rows.length }} {{ rows.length === 1 ? 'person' : 'people' }}
        </span>
      </template>

      <template #row-label="{ row }">
        <div class="person-row">
          <RouterLink :to="`/people/${row.id}`" class="person">
            <AppAvatar :name="row.label" :image-url="byId.get(row.id)?.imageUrl" :size="38" />
            <span class="person-meta">
              <span class="person-name">{{ row.label }}</span>
              <span class="person-role">{{ byId.get(row.id)?.primaryJobTitle ?? '—' }}</span>
              <span class="person-loc">{{ byId.get(row.id)?.location ?? '' }}</span>
            </span>
          </RouterLink>
          <button
            class="icon-btn plain add" :aria-label="`Add a booking or time off for ${row.label}`"
            @click="openRow(row)"
          >
            <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M12 5v14M5 12h14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" /></svg>
          </button>
        </div>
      </template>

      <template #bar="{ bar, row }">{{ barText(bar, row) }}</template>
    </TimelineGrid>

    <p class="muted legend">
      Bars in red exceed the person's weekly availability — over-allocation is flagged, not blocked (FR-ALL-6). Select a bar to edit that allocation, or a free day to add a booking or time off. Hatched blocks are time off (FR-TIMEOFF-5).</p>
  </div>

  <AllocationEditModal
    v-if="editAlloc" :allocation="editAlloc"
    @close="editAlloc = null" @saved="editAlloc = null; load()"
  />

  <ScheduleEntryModal
    v-if="entry" :resources="resources" :resource-id="entry.resourceId" :date="entry.date"
    @close="entry = null" @saved="entry = null; load()"
  />
</template>

<style scoped>
.range { font-size: 15px; color: var(--gray-900); min-width: 190px; }
.person-row { display: flex; align-items: flex-start; gap: 6px; width: 100%; }
.person { display: flex; gap: 10px; align-items: flex-start; color: inherit; min-width: 0; flex: 1; }
/* Keyboard/AT route to the day-cell dialog, which is pointer-only on the grid. */
.person-row .add { margin-left: auto; opacity: 0; flex-shrink: 0; }
.tl-row:hover .person-row .add, .person-row .add:focus-visible { opacity: 1; }
.person:hover { text-decoration: none; }
.person:hover .person-name { text-decoration: underline; }
.person-meta { min-width: 0; }
.person-name { display: block; font-weight: 650; color: var(--brand-700); font-size: 13.5px; }
.person-role { display: block; color: var(--amber-700); font-size: 12px; }
.person-loc { display: block; color: var(--text-muted); font-size: 11.5px; }
.legend { margin-top: 12px; font-size: 12.5px; }
</style>
