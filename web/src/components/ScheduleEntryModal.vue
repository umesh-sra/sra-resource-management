<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { allocationsApi, projectsApi, timeOffApi } from '@/api'
import { ApiError } from '@/api/http'
import type { EffortUnit, Project, Resource, TimeOffType } from '@/types'
import { countWorkingDays, fmtHours, isoDate, timeOffLabel } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from './ModalDialog.vue'
import AppAvatar from './AppAvatar.vue'

/**
 * The reference application's day-cell dialog (screens/shedule_booking.png,
 * screens/shedule_timeoff.png): picking a day on the Schedule opens a two-tab
 * sheet that either books work (an allocation, FR-ALL-1) or records leave
 * (time off, FR-TIMEOFF-1) for that person on that day.
 */
const props = defineProps<{
  /** People to choose from — the Schedule already has them loaded. */
  resources: Resource[]
  /** Row the day cell belonged to. */
  resourceId?: string
  /** Day that was clicked, `yyyy-MM-dd`; seeds both ends of the range. */
  date?: string
  initialTab?: Tab
}>()
const emit = defineEmits<{ close: []; saved: [] }>()

type Tab = 'booking' | 'timeOff'
const toast = useToastStore()

const tab = ref<Tab>(props.initialTab ?? 'booking')
const saving = ref(false)
const today = isoDate(new Date())
const day = props.date ?? today

const projects = ref<Project[]>([])
const projectById = computed(() => new Map(projects.value.map((p) => [p.id, p])))

onMounted(async () => {
  try {
    projects.value = (await projectsApi.list({ pageSize: 200, sort: 'name' })).items
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not load projects')
  }
})

/** "Project / Client" in the reference — one optgroup per client. */
const projectGroups = computed(() => {
  const groups = new Map<string, Project[]>()
  for (const p of projects.value) {
    const key = p.clientName ?? 'Other'
    const list = groups.get(key) ?? []
    list.push(p)
    groups.set(key, list)
  }
  return [...groups.entries()].sort((a, b) => a[0].localeCompare(b[0]))
})

// ---------------------------------------------------------------------------
// Booking
// ---------------------------------------------------------------------------

/**
 * The reference books in hours per day. The API stores effort as hours per week
 * or a percentage of availability (EffortUnit), so hours-per-day is a data-entry
 * mode that converts on save using the person's own working pattern.
 */
type EffortMode = 'hoursPerDay' | 'hoursPerWeek' | 'percent'

const booking = ref({
  resourceId: props.resourceId ?? '',
  projectId: '',
  mode: 'hoursPerDay' as EffortMode,
  hours: 8,
  mins: 0,
  hoursPerWeek: 38,
  percent: 100,
  startDate: day,
  endDate: day,
  roleOnProject: '',
  billable: true,
  details: '',
  bookerId: '',
})

const bookingResource = computed(() => props.resources.find((r) => r.id === booking.value.resourceId))
/**
 * The Booker defaults to blank rather than the signed-in user: nothing maps the
 * authenticated AD identity onto a Resource row yet, so there is no reliable
 * "me" to preselect. See TODO.md.
 */
const bookingBooker = computed(() => props.resources.find((r) => r.id === booking.value.bookerId))
const bookingProject = computed(() => projectById.value.get(booking.value.projectId))

/** Days a week this person actually works — 5 unless their pattern says otherwise. */
const workingDaysPerWeek = computed(() => bookingResource.value?.workingDays?.length || 5)
const availability = computed(() => Number(bookingResource.value?.availabilityHoursPerWeek ?? 38))

/** The booking expressed the way the API takes it. */
const effort = computed<{ value: number; unit: EffortUnit }>(() => {
  const b = booking.value
  if (b.mode === 'percent') return { value: Number(b.percent) || 0, unit: 'percent' }
  if (b.mode === 'hoursPerWeek') return { value: Number(b.hoursPerWeek) || 0, unit: 'hoursPerWeek' }
  const perDay = (Number(b.hours) || 0) + (Number(b.mins) || 0) / 60
  return { value: Math.round(perDay * workingDaysPerWeek.value * 100) / 100, unit: 'hoursPerWeek' }
})

/** Weekly hours the booking works out to, whichever way it was entered. */
const weeklyHours = computed(() =>
  effort.value.unit === 'percent' ? (effort.value.value / 100) * availability.value : effort.value.value,
)

const bookingTotal = computed(() => {
  const b = booking.value
  const days = countWorkingDays(b.startDate, b.endDate, bookingResource.value?.workingDays)
  const perDay = weeklyHours.value / workingDaysPerWeek.value
  return { days, perDay, total: perDay * days }
})

/** Non-blocking: over-allocation is flagged, not refused (FR-ALL-6). */
const overAllocated = computed(() =>
  !!bookingResource.value && weeklyHours.value > availability.value + 0.001,
)

/** Allocation dates must sit inside the project window — the API rejects otherwise. */
const outsideProjectWindow = computed(() => {
  const p = bookingProject.value
  const b = booking.value
  return !!p && !!b.startDate && !!b.endDate && (b.startDate < p.startDate || b.endDate > p.endDate)
})

// A project carries its own billable default; adopt it when one is picked.
watch(() => booking.value.projectId, (id) => {
  const p = projectById.value.get(id)
  if (p) booking.value.billable = p.billable
})

const bookingValid = computed(() => {
  const b = booking.value
  return !!b.resourceId && !!b.projectId && !!b.startDate && !!b.endDate
    && b.endDate >= b.startDate && effort.value.value > 0
})

async function addBooking() {
  saving.value = true
  try {
    const b = booking.value
    const created = await allocationsApi.create({
      projectId: b.projectId,
      resourceId: b.resourceId,
      startDate: b.startDate,
      endDate: b.endDate,
      effort: effort.value.value,
      effortUnit: effort.value.unit,
      roleOnProject: b.roleOnProject.trim() || undefined,
      billable: b.billable,
      details: b.details.trim() || undefined,
      bookerId: b.bookerId || undefined,
    })
    if (created.warnings?.length) toast.warning(`Booking added — ${created.warnings.join(' ')}`)
    else toast.success('Booking added')
    emit('saved')
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not add the booking')
  } finally {
    saving.value = false
  }
}

// ---------------------------------------------------------------------------
// Time off
// ---------------------------------------------------------------------------

const TIME_OFF_TYPES: TimeOffType[] = ['annualLeave', 'personal', 'sick', 'publicHoliday', 'other']

const leave = ref({
  resourceId: props.resourceId ?? '',
  startDate: day,
  endDate: day,
  type: 'annualLeave' as TimeOffType,
  /** Empty means a whole working day, which is what the API assumes. */
  hoursPerDay: '' as number | '',
  note: '',
  bookerId: '',
})

const leaveResource = computed(() => props.resources.find((r) => r.id === leave.value.resourceId))
const leaveBooker = computed(() => props.resources.find((r) => r.id === leave.value.bookerId))

const leaveTotal = computed(() => {
  const l = leave.value
  const days = countWorkingDays(l.startDate, l.endDate, leaveResource.value?.workingDays)
  const fullDay = Number(leaveResource.value?.availabilityHoursPerWeek ?? 38)
    / (leaveResource.value?.workingDays?.length || 5)
  const perDay = l.hoursPerDay === '' ? fullDay : Number(l.hoursPerDay)
  return { days, hours: perDay * days }
})

const leaveValid = computed(() => {
  const l = leave.value
  return !!l.resourceId && !!l.startDate && !!l.endDate && l.endDate >= l.startDate
})

async function addTimeOff() {
  saving.value = true
  try {
    const l = leave.value
    await timeOffApi.create({
      resourceId: l.resourceId,
      startDate: l.startDate,
      endDate: l.endDate,
      type: l.type,
      hoursPerDay: l.hoursPerDay === '' ? undefined : Number(l.hoursPerDay),
      note: l.note.trim() || undefined,
      bookerId: l.bookerId || undefined,
    })
    toast.success('Time off added')
    emit('saved')
  } catch (e) {
    // Overlapping leave for the same person is a 409 — a data error, not a warning.
    toast.error(e instanceof ApiError ? e.message : 'Could not add the time off')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <!-- Deliberately not titled with the clicked day: the day only seeds the date
       fields, and the user is free to change them before saving. -->
  <ModalDialog title="Add to the schedule" :max-width="580" @close="emit('close')">
    <template #head-icon>
      <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M7 2v2H5a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2h-2V2h-2v2H9V2H7Zm12 8v9H5v-9h14Z" fill="currentColor" /></svg>
    </template>

    <template #subhead>
      <div class="dialog-tabs">
        <div class="tabs" role="tablist" aria-label="What to schedule">
          <button class="tab" role="tab" :aria-selected="tab === 'booking'" :class="{ active: tab === 'booking' }" @click="tab = 'booking'">Booking</button>
          <button class="tab" role="tab" :aria-selected="tab === 'timeOff'" :class="{ active: tab === 'timeOff' }" @click="tab = 'timeOff'">Time Off</button>
        </div>
      </div>
    </template>

    <!-- Booking ----------------------------------------------------------- -->
    <div v-show="tab === 'booking'" role="tabpanel" aria-label="Booking">
      <div class="hint-banner">
        <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><path d="M12 2a7 7 0 0 0-4 12.7V17a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1v-2.3A7 7 0 0 0 12 2Zm-2 18h4v1a1 1 0 0 1-1 1h-2a1 1 0 0 1-1-1v-1Z" fill="currentColor" /></svg>
        <span>Book the hours you need across a date range. Going over someone's weekly availability is allowed — it is flagged, not blocked.</span>
      </div>

      <div class="field">
        <label for="se-person">People or resources *</label>
        <div class="picker">
          <AppAvatar v-if="bookingResource" :name="bookingResource.name" :image-url="bookingResource.imageUrl" :size="30" />
          <select id="se-person" class="select" v-model="booking.resourceId">
            <option value="" disabled>Select a person…</option>
            <option v-for="r in resources" :key="r.id" :value="r.id">{{ r.name }}</option>
          </select>
        </div>
      </div>

      <div class="field">
        <label id="se-mode-label">Effort</label>
        <div class="segmented" role="group" aria-labelledby="se-mode-label">
          <button type="button" :aria-pressed="booking.mode === 'hoursPerDay'" @click="booking.mode = 'hoursPerDay'">Hours per day</button>
          <button type="button" :aria-pressed="booking.mode === 'hoursPerWeek'" @click="booking.mode = 'hoursPerWeek'">Hours per week</button>
          <button type="button" :aria-pressed="booking.mode === 'percent'" @click="booking.mode = 'percent'">Percent</button>
        </div>
      </div>

      <div v-if="booking.mode === 'hoursPerDay'" class="form-row">
        <div class="field"><label for="se-hours">Hours</label><input id="se-hours" class="input" type="number" min="0" max="24" v-model.number="booking.hours" /></div>
        <div class="field"><label for="se-mins">Mins</label><input id="se-mins" class="input" type="number" min="0" max="59" step="5" v-model.number="booking.mins" /></div>
      </div>
      <div v-else-if="booking.mode === 'hoursPerWeek'" class="field">
        <label for="se-hpw">Hours per week</label>
        <input id="se-hpw" class="input" type="number" min="0" step="0.5" v-model.number="booking.hoursPerWeek" />
      </div>
      <div v-else class="field">
        <label for="se-pct">Percent of availability</label>
        <input id="se-pct" class="input" type="number" min="0" max="200" v-model.number="booking.percent" />
      </div>

      <div class="form-row">
        <div class="field">
          <label for="se-from">From *</label>
          <input id="se-from" class="input" type="date" v-model="booking.startDate" :min="bookingProject?.startDate" :max="bookingProject?.endDate" />
        </div>
        <div class="field">
          <label for="se-to">To *</label>
          <input id="se-to" class="input" type="date" v-model="booking.endDate" :min="booking.startDate || bookingProject?.startDate" :max="bookingProject?.endDate" />
        </div>
      </div>

      <p class="total">
        Total: {{ fmtHours(bookingTotal.total) }}
        <span class="muted">
          ({{ bookingTotal.days }} working {{ bookingTotal.days === 1 ? 'day' : 'days' }} ·
          {{ fmtHours(bookingTotal.perDay) }} per day · {{ fmtHours(weeklyHours) }} per week)
        </span>
      </p>
      <p v-if="overAllocated" class="warn-text small">
        That is more than {{ bookingResource?.name }}'s {{ availability }}h weekly availability — the booking is saved with an over-allocation warning.
      </p>

      <div class="field">
        <label for="se-project">Project / client *</label>
        <select id="se-project" class="select" v-model="booking.projectId">
          <option value="" disabled>Select a project…</option>
          <optgroup v-for="[client, list] in projectGroups" :key="client" :label="client">
            <option v-for="p in list" :key="p.id" :value="p.id">{{ p.name }} ({{ p.code }})</option>
          </optgroup>
        </select>
      </div>
      <p v-if="outsideProjectWindow" class="err small">
        Those dates fall outside the project window ({{ bookingProject?.startDate }} – {{ bookingProject?.endDate }}).
      </p>

      <div class="field">
        <label for="se-role">Role on project (optional)</label>
        <input id="se-role" class="input" v-model="booking.roleOnProject" :placeholder="bookingProject ? 'e.g. Developer' : 'Select a project first'" :disabled="!booking.projectId" />
      </div>

      <label class="switch">
        <input type="checkbox" v-model="booking.billable" /><span class="track" />
        <span>Billable</span>
      </label>

      <div class="field" style="margin-top: 14px">
        <label for="se-details">Details (optional)</label>
        <textarea id="se-details" class="input" rows="3" v-model="booking.details" />
      </div>

      <div class="field">
        <label for="se-booker">Booker (optional)</label>
        <div class="picker">
          <AppAvatar v-if="bookingBooker" :name="bookingBooker.name" :image-url="bookingBooker.imageUrl" :size="30" />
          <select id="se-booker" class="select" v-model="booking.bookerId">
            <option value="">No booker recorded</option>
            <option v-for="r in resources" :key="r.id" :value="r.id">{{ r.name }}</option>
          </select>
        </div>
      </div>
    </div>

    <!-- Time off ---------------------------------------------------------- -->
    <div v-show="tab === 'timeOff'" role="tabpanel" aria-label="Time off">
      <div class="field">
        <label for="se-to-person">People or resources *</label>
        <div class="picker">
          <AppAvatar v-if="leaveResource" :name="leaveResource.name" :image-url="leaveResource.imageUrl" :size="30" />
          <select id="se-to-person" class="select" v-model="leave.resourceId">
            <option value="" disabled>Select a person…</option>
            <option v-for="r in resources" :key="r.id" :value="r.id">{{ r.name }}</option>
          </select>
        </div>
      </div>

      <div class="form-row">
        <div class="field"><label for="se-lfrom">From *</label><input id="se-lfrom" class="input" type="date" v-model="leave.startDate" /></div>
        <div class="field"><label for="se-lto">To *</label><input id="se-lto" class="input" type="date" v-model="leave.endDate" :min="leave.startDate" /></div>
      </div>

      <p class="total">
        Total: {{ leaveTotal.days }}{{ leaveTotal.days === 1 ? ' day' : ' days' }}
        <span class="muted">({{ fmtHours(leaveTotal.hours) }})</span>
      </p>

      <div class="form-row">
        <div class="field">
          <label for="se-ltype">Type</label>
          <select id="se-ltype" class="select" v-model="leave.type">
            <option v-for="t in TIME_OFF_TYPES" :key="t" :value="t">{{ timeOffLabel(t) }}</option>
          </select>
        </div>
        <div class="field">
          <label for="se-lhpd">Hours per day</label>
          <input id="se-lhpd" class="input" type="number" min="0.5" max="24" step="0.5" v-model="leave.hoursPerDay" placeholder="Full day" />
        </div>
      </div>

      <div class="field">
        <label for="se-lnote">Details (optional)</label>
        <textarea id="se-lnote" class="input" rows="3" v-model="leave.note" />
      </div>

      <div class="field">
        <label for="se-lbooker">Booker (optional)</label>
        <div class="picker">
          <AppAvatar v-if="leaveBooker" :name="leaveBooker.name" :image-url="leaveBooker.imageUrl" :size="30" />
          <select id="se-lbooker" class="select" v-model="leave.bookerId">
            <option value="">No booker recorded</option>
            <option v-for="r in resources" :key="r.id" :value="r.id">{{ r.name }}</option>
          </select>
        </div>
      </div>

      <p class="muted small">
        Leave does not block bookings, but it reduces effective capacity in the utilisation report.
        Overlapping leave for the same person is rejected.
      </p>
    </div>

    <template #footer>
      <button class="btn" @click="emit('close')">Cancel</button>
      <button
        v-if="tab === 'booking'" class="btn btn-primary"
        :disabled="saving || !bookingValid" @click="addBooking"
      >Add booking</button>
      <button
        v-else class="btn btn-primary"
        :disabled="saving || !leaveValid" @click="addTimeOff"
      >Add time off</button>
    </template>
  </ModalDialog>
</template>

<style scoped>
.dialog-tabs { padding: 0 20px; border-bottom: 1px solid var(--border); }
.picker { display: flex; align-items: center; gap: 10px; }
.picker .select { flex: 1; min-width: 0; }
.total { margin: 2px 0 12px; font-weight: 650; color: var(--gray-900); font-size: 13.5px; }
.total .muted { font-weight: 400; }
.small { font-size: 12.5px; }
.err { color: var(--red-700); margin: -6px 0 12px; }
.warn-text.small { margin: -6px 0 12px; }
</style>
