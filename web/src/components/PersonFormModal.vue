<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type { BookableStatus, Resource, ResourceStatus, Weekday } from '@/types'
import { assetUrl } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from './ModalDialog.vue'
import CollapsibleSection from './CollapsibleSection.vue'

/**
 * Create / edit a person, laid out as in screens/new_person_part1.png and
 * new_person_part2.png: the essentials up front, the rest folded into
 * Extra Details / Scheduling / Financial disclosures.
 *
 * The reference's invitation email, permissions role, colour swatch and manager
 * have no counterpart in the SRA-RMS data model (roles come from AD groups per
 * SRS §5), so they are not offered here.
 */
const props = defineProps<{ resource?: Resource | null }>()
const emit = defineEmits<{ close: []; saved: [resource: Resource] }>()

const toast = useToastStore()
const editing = computed(() => !!props.resource)

const WEEKDAYS: Weekday[] = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday']

/** The reference splits the name in two; the API stores one `name` field. */
function splitName(full: string): [string, string] {
  const parts = full.trim().split(/\s+/)
  return parts.length < 2 ? [full.trim(), ''] : [parts.slice(0, -1).join(' '), parts[parts.length - 1]!]
}

const r = props.resource
const [initialFirst, initialLast] = splitName(r?.name ?? '')

const form = ref({
  firstName: initialFirst,
  lastName: initialLast,
  email: r?.email ?? '',
  primaryJobTitle: r?.primaryJobTitle ?? '',
  secondaryJobTitle: r?.secondaryJobTitle ?? '',
  department: r?.department ?? '',
  location: r?.location ?? '',
  skills: (r?.skills ?? []).join(', '),
  notes: r?.notes ?? '',
  availabilityHoursPerWeek: Number(r?.availabilityHoursPerWeek ?? 38),
  workingDays: [...(r?.workingDays ?? (['monday', 'tuesday', 'wednesday', 'thursday', 'friday'] as Weekday[]))],
  status: (r?.status ?? 'active') as ResourceStatus,
  // V002 profile (Requirements §3.3)
  jobRole: r?.jobRole ?? '',
  managerId: r?.managerId ?? '',
  phone: r?.phone ?? '',
  secondarySkills: (r?.secondarySkills ?? []).join(', '),
  securityClearances: (r?.securityClearances ?? []).join(', '),
  securityNpcObtainedOn: r?.securityNpcObtainedOn ?? '',
  certifications: (r?.certifications ?? []).join(', '),
  timeZone: r?.timeZone ?? '',
  bookableStatus: (r?.bookableStatus ?? 'bookable') as BookableStatus,
  publicHolidayCalendar: r?.publicHolidayCalendar ?? '',
  defaultRateHourly: r?.defaultRateHourly ?? null as number | null,
})

/** Manager pick-list; excludes the person being edited (FR-RES-9). */
const managers = ref<Resource[]>([])
onMounted(async () => {
  try {
    const page = await resourcesApi.list({ pageSize: 200, sort: 'name' })
    managers.value = page.items.filter((m) => m.id !== r?.id)
  } catch {
    // A missing manager list must not block the dialog; the field just stays empty.
  }
})

const csv = (v: string) => v.split(',').map((x) => x.trim()).filter(Boolean)

const saving = ref(false)
const imageFile = ref<File | null>(null)
const localPreview = ref<string | null>(null)
const preview = computed(() => localPreview.value ?? (r?.imageUrl ? assetUrl(r.imageUrl) : null))

function pickImage(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (!file) return
  if (!['image/png', 'image/jpeg'].includes(file.type)) {
    toast.error('Profile photos must be PNG or JPEG.')
    return
  }
  if (file.size > 5 * 1024 * 1024) {
    toast.error('Profile photos must be 5 MB or smaller.')
    return
  }
  imageFile.value = file
  if (localPreview.value) URL.revokeObjectURL(localPreview.value)
  localPreview.value = URL.createObjectURL(file)
}
onBeforeUnmount(() => { if (localPreview.value) URL.revokeObjectURL(localPreview.value) })

function toggleDay(d: Weekday) {
  const i = form.value.workingDays.indexOf(d)
  if (i === -1) form.value.workingDays.push(d)
  else form.value.workingDays.splice(i, 1)
}

const fullName = computed(() => `${form.value.firstName} ${form.value.lastName}`.trim())
const valid = computed(() =>
  !!form.value.firstName.trim() && !!form.value.email.trim() && !!form.value.primaryJobTitle.trim(),
)

async function save() {
  if (!valid.value) return
  saving.value = true
  try {
    const f = form.value
    const body = {
      name: fullName.value,
      email: f.email.trim(),
      primaryJobTitle: f.primaryJobTitle.trim(),
      secondaryJobTitle: f.secondaryJobTitle.trim() || undefined,
      department: f.department.trim() || undefined,
      location: f.location.trim() || undefined,
      notes: f.notes.trim() || undefined,
      skills: f.skills.split(',').map((s) => s.trim()).filter(Boolean),
      availabilityHoursPerWeek: Number(f.availabilityHoursPerWeek),
      workingDays: f.workingDays,
      status: f.status,
      // V002 profile (Requirements §3.3). Empty strings are sent as undefined so
      // clearing a field nulls it rather than storing "".
      jobRole: f.jobRole.trim() || undefined,
      managerId: f.managerId || undefined,
      phone: f.phone.trim() || undefined,
      secondarySkills: csv(f.secondarySkills),
      securityClearances: csv(f.securityClearances),
      securityNpcObtainedOn: f.securityNpcObtainedOn || undefined,
      certifications: csv(f.certifications),
      timeZone: f.timeZone.trim() || undefined,
      bookableStatus: f.bookableStatus,
      publicHolidayCalendar: f.publicHolidayCalendar.trim() || undefined,
      defaultRateHourly: f.defaultRateHourly == null || (f.defaultRateHourly as unknown) === ''
        ? undefined
        : Number(f.defaultRateHourly),
    }
    const saved = props.resource
      ? await resourcesApi.update(props.resource.id, body)
      : await resourcesApi.create(body)

    if (imageFile.value) {
      // A failed photo must not lose the record that just saved successfully.
      try {
        await resourcesApi.uploadImage(saved.id, imageFile.value)
      } catch (e) {
        toast.error(e instanceof ApiError ? `Saved, but the photo failed: ${e.message}` : 'Saved, but the photo failed to upload')
      }
    }

    toast.success(editing.value ? 'Person updated' : 'Person added')
    emit('saved', saved)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not save this person')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <ModalDialog :title="editing ? 'Edit Person' : 'New Person'" @close="emit('close')">
    <template #head-icon>
      <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M12 12a4 4 0 1 0-4-4 4 4 0 0 0 4 4Zm0 2c-3.3 0-8 1.7-8 5v1h16v-1c0-3.3-4.7-5-8-5Z" fill="currentColor" /></svg>
    </template>

    <div v-if="!editing" class="hint-banner">
      <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><path d="M12 2a7 7 0 0 0-4 12.7V17a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1v-2.3A7 7 0 0 0 12 2Zm-2 18h4v1a1 1 0 0 1-1 1h-2a1 1 0 0 1-1-1v-1Z" fill="currentColor" /></svg>
      <span>People added here become allocatable resources straight away. Availability drives the over-allocation warnings on the Schedule.</span>
    </div>

    <div class="form-row">
      <div class="field"><label for="pf-first">First name *</label><input id="pf-first" class="input" v-model="form.firstName" /></div>
      <div class="field"><label for="pf-last">Last name</label><input id="pf-last" class="input" v-model="form.lastName" /></div>
    </div>

    <div class="field">
      <label for="pf-email">Email *</label>
      <input id="pf-email" class="input" type="email" v-model="form.email" />
    </div>

    <div class="field">
      <span class="field-label">Photo</span>
      <div class="photo-row">
        <div class="photo-box">
          <img v-if="preview" :src="preview" alt="" />
          <svg v-else viewBox="0 0 24 24" width="30" height="30" aria-hidden="true"><path d="M4 7h3l1.5-2h7L17 7h3a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V8a1 1 0 0 1 1-1Zm8 3.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Z" fill="currentColor" /></svg>
        </div>
        <label class="btn btn-outline">
          {{ preview ? 'Change image' : 'Add image' }}
          <input type="file" accept="image/png,image/jpeg" class="sr-only" @change="pickImage" />
        </label>
      </div>
      <p class="muted hint">PNG or JPEG, up to 5 MB. Served only through the authorised image endpoint (NFR-SEC-5).</p>
    </div>

    <div class="form-row">
      <div class="field"><label for="pf-title">Job title *</label><input id="pf-title" class="input" v-model="form.primaryJobTitle" placeholder="e.g. Senior Consultant" /></div>
      <div class="field"><label for="pf-role">Job role</label><input id="pf-role" class="input" v-model="form.secondaryJobTitle" placeholder="e.g. Tech Lead" /></div>
    </div>

    <div class="form-row">
      <div class="field"><label for="pf-dept">Department</label><input id="pf-dept" class="input" v-model="form.department" /></div>
      <div class="field"><label for="pf-loc">Location</label><input id="pf-loc" class="input" v-model="form.location" /></div>
    </div>

    <div class="form-row">
      <div class="field">
        <label for="pf-jobrole">Job role</label>
        <input id="pf-jobrole" class="input" v-model="form.jobRole" placeholder="Tech Lead" />
      </div>
      <div class="field">
        <label for="pf-manager">Manager</label>
        <select id="pf-manager" class="select" v-model="form.managerId">
          <option value="">— None —</option>
          <option v-for="m in managers" :key="m.id" :value="m.id">{{ m.name }}</option>
        </select>
      </div>
    </div>

    <div class="sections">
      <CollapsibleSection
        title="Extra Details"
        summary="Phone, primary &amp; secondary skills, security clearances, certifications, notes"
      >
        <div class="field">
          <label for="pf-phone">Phone</label>
          <input id="pf-phone" class="input" type="tel" v-model="form.phone" placeholder="+61 400 000 000" />
        </div>
        <div class="field">
          <label for="pf-skills">Primary skills (comma-separated)</label>
          <input id="pf-skills" class="input" v-model="form.skills" placeholder="C#, Vue.js, PostgreSQL" />
        </div>
        <div class="field">
          <label for="pf-skills2">Secondary skills (comma-separated)</label>
          <input id="pf-skills2" class="input" v-model="form.secondarySkills" placeholder="Azure, Terraform" />
        </div>
        <div class="form-row">
          <div class="field">
            <label for="pf-clear">Security clearances (comma-separated)</label>
            <input id="pf-clear" class="input" v-model="form.securityClearances" placeholder="Baseline, NV1" />
          </div>
          <div class="field">
            <label for="pf-npc">Security NPC obtained</label>
            <input id="pf-npc" class="input" type="date" v-model="form.securityNpcObtainedOn" />
          </div>
        </div>
        <div class="field">
          <label for="pf-certs">Staff certifications (comma-separated)</label>
          <input id="pf-certs" class="input" v-model="form.certifications" placeholder="AZ-204, CSM" />
        </div>
        <div class="field">
          <label for="pf-notes">Notes</label>
          <textarea id="pf-notes" class="input" rows="3" v-model="form.notes" />
        </div>
      </CollapsibleSection>

      <CollapsibleSection title="Scheduling" summary="Availability, working days, bookable status">
        <div class="form-row">
          <div class="field">
            <label for="pf-avail">Availability (hours per week)</label>
            <input id="pf-avail" class="input" type="number" min="0" max="168" v-model.number="form.availabilityHoursPerWeek" />
          </div>
          <div class="field">
            <label for="pf-status">Status</label>
            <select id="pf-status" class="select" v-model="form.status">
              <option value="active">Active</option>
              <option value="inactive">Inactive</option>
              <option value="onLeave">On leave</option>
            </select>
          </div>
        </div>
        <div class="form-row">
          <div class="field">
            <label for="pf-tz">Time zone</label>
            <input id="pf-tz" class="input" v-model="form.timeZone" placeholder="Australia/Adelaide" />
          </div>
          <div class="field">
            <label for="pf-bookable">Bookable status</label>
            <select id="pf-bookable" class="select" v-model="form.bookableStatus">
              <option value="bookable">Bookable</option>
              <option value="nonBookable">Non-bookable</option>
            </select>
          </div>
        </div>
        <div class="field">
          <label for="pf-holcal">Public holiday calendar</label>
          <input id="pf-holcal" class="input" v-model="form.publicHolidayCalendar" placeholder="AU-SA" />
        </div>
        <fieldset class="days">
          <legend>Working days</legend>
          <label v-for="d in WEEKDAYS" :key="d" class="day">
            <input type="checkbox" :checked="form.workingDays.includes(d)" @change="toggleDay(d)" />
            <span>{{ d.slice(0, 3) }}</span>
          </label>
        </fieldset>
      </CollapsibleSection>

      <CollapsibleSection title="Financial" summary="Default charge-out rate">
        <div class="field">
          <label for="pf-rate">Default rate (AUD per hour)</label>
          <input id="pf-rate" class="input" type="number" min="0" step="0.01" v-model.number="form.defaultRateHourly" />
        </div>
        <p class="muted" style="margin: 0">
          Used as the default when this person is allocated to a project; each allocation can
          override it on the project's Team tab.
        </p>
      </CollapsibleSection>
    </div>

    <template #footer>
      <button class="btn" @click="emit('close')">Cancel</button>
      <button class="btn btn-accent btn-pill" :disabled="saving || !valid" @click="save">
        {{ editing ? 'Save changes' : 'Add Person' }}
      </button>
    </template>
  </ModalDialog>
</template>

<style scoped>
.field-label { font-size: 12.5px; font-weight: 600; color: var(--gray-700); }
.photo-row { display: flex; align-items: center; gap: 16px; }
.photo-box {
  width: 96px; height: 96px; border: 1px dashed var(--gray-300); border-radius: var(--radius-sm);
  display: grid; place-items: center; color: var(--gray-400); overflow: hidden; background: var(--gray-50);
}
.photo-box img { width: 100%; height: 100%; object-fit: cover; }
.hint { font-size: 12px; margin: 6px 0 0; }
.sections { margin-top: 8px; }
.days { border: 0; padding: 0; margin: 0; }
.days legend { font-size: 12.5px; font-weight: 600; color: var(--gray-700); padding: 0; margin-bottom: 6px; }
.day { display: inline-flex; align-items: center; gap: 5px; margin: 0 12px 6px 0; text-transform: capitalize; font-size: 13px; }
</style>
