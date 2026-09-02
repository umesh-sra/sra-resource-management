<script setup lang="ts">
import { computed, onMounted, ref, useId, watch } from 'vue'
import { projectsApi, resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Project, ResourceDetail } from '@/types'
import { assetUrl, fmtDate, initials, resourceStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import SidePanel from './SidePanel.vue'
import CollapsibleSection from './CollapsibleSection.vue'
import PersonFormModal from './PersonFormModal.vue'

/**
 * Person record drawer — the two-tab layout from screens/person_overview.png
 * and screens/person_projects.png, with the dark identity rail on the left.
 */
const props = defineProps<{ resourceId: string }>()
const emit = defineEmits<{ close: []; changed: [] }>()

const toast = useToastStore()
const titleId = useId()

const person = ref<ResourceDetail | null>(null)
const projects = ref<Project[]>([])
const loading = ref(true)
const tab = ref<'overview' | 'assigned'>('overview')
const showEdit = ref(false)
const imageFailed = ref(false)

const photo = computed(() =>
  person.value?.imageUrl && !imageFailed.value ? assetUrl(person.value.imageUrl) : null,
)

async function load() {
  loading.value = true
  imageFailed.value = false
  try {
    person.value = await resourcesApi.get(props.resourceId)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load this person')
    emit('close')
  } finally {
    loading.value = false
  }
}
watch(() => props.resourceId, load)

onMounted(async () => {
  await load()
  try {
    // Allocations carry a project name but not its client — join client-side.
    projects.value = (await projectsApi.list({ pageSize: 200, sort: 'name' })).items
  } catch {
    // Client names are a nicety; the assigned list still renders without them.
  }
})

const projectById = computed(() => new Map(projects.value.map((p) => [p.id, p])))

/** Distinct projects this person is allocated to, with their client. */
const assignedProjects = computed(() => {
  const seen = new Map<string, { id: string; name: string; code?: string; clientName?: string }>()
  for (const a of person.value?.allocations ?? []) {
    if (seen.has(a.projectId)) continue
    const p = projectById.value.get(a.projectId)
    seen.set(a.projectId, {
      id: a.projectId,
      name: a.projectName ?? p?.name ?? 'Project',
      code: p?.code,
      clientName: p?.clientName,
    })
  }
  return [...seen.values()].sort((a, b) => a.name.localeCompare(b.name))
})

const assignedClients = computed(() => {
  const seen = new Map<string, string>()
  for (const a of person.value?.allocations ?? []) {
    const p = projectById.value.get(a.projectId)
    if (p?.clientName) seen.set(p.clientId, p.clientName)
  }
  return [...seen.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name))
})

const utilisation = computed(() => {
  const r = person.value
  if (!r || r.availabilityHoursPerWeek <= 0) return 0
  return r.allocatedHoursPerWeek / r.availabilityHoursPerWeek
})

async function remove() {
  const r = person.value
  if (!r) return
  const hasAllocs = r.allocations.length > 0
  if (!confirm(hasAllocs
    ? `Delete ${r.name} and their ${r.allocations.length} allocation(s)?`
    : `Delete ${r.name}?`)) return
  try {
    await resourcesApi.remove(r.id, hasAllocs)
    toast.success('Person deleted')
    emit('changed')
    emit('close')
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not delete this person')
  }
}
</script>

<template>
  <SidePanel :labelled-by="titleId" @close="emit('close')">
    <!-- Identity rail -------------------------------------------------- -->
    <div class="rail">
      <div class="photo">
        <img v-if="photo" :src="photo" alt="" @error="imageFailed = true" />
        <span v-else-if="person" class="photo-initials" aria-hidden="true">{{ initials(person.name) }}</span>
      </div>
      <div class="rail-body">
        <h2 :id="titleId" class="rail-name">{{ person?.name ?? 'Loading…' }}</h2>

        <template v-if="person">
          <div class="rail-field">
            <span class="rail-label">Email</span>
            <a class="rail-email" :href="`mailto:${person.email}`">{{ person.email }}</a>
          </div>
          <div class="rail-field">
            <span class="rail-label">Job Title</span>
            <strong>{{ person.primaryJobTitle }}</strong>
          </div>
          <div class="rail-field">
            <span class="rail-label">Availability</span>
            <strong>{{ person.availabilityHoursPerWeek }} h / week</strong>
          </div>
          <div class="rail-field">
            <span class="rail-label">Status</span>
            <span class="status-chip">{{ resourceStatus(person.status).label }}</span>
          </div>
          <p class="rail-foot">Last updated {{ fmtDate(person.updatedAt) }}</p>

          <div class="rail-actions">
            <button class="btn btn-sm" @click="showEdit = true">Edit</button>
            <button class="btn btn-sm btn-danger" @click="remove">Delete</button>
          </div>
        </template>
      </div>
    </div>

    <!-- Tabbed detail --------------------------------------------------- -->
    <div class="detail">
      <div class="detail-tabs">
        <div class="tabs" role="tablist" aria-label="Person detail">
          <button class="tab" :class="{ active: tab === 'overview' }" role="tab" :aria-selected="tab === 'overview'" @click="tab = 'overview'">Overview</button>
          <button class="tab" :class="{ active: tab === 'assigned' }" role="tab" :aria-selected="tab === 'assigned'" @click="tab = 'assigned'">Assigned Projects &amp; Clients</button>
        </div>
      </div>

      <div v-if="loading" class="detail-body"><div class="skeleton" style="height: 120px" /></div>

      <div v-else-if="person && tab === 'overview'" class="detail-body" role="tabpanel">
        <dl class="defs">
          <div><dt>Department</dt><dd><span class="pill">{{ person.department || '—' }}</span></dd></div>
          <div><dt>Job Role</dt><dd><span class="pill">{{ person.secondaryJobTitle || person.primaryJobTitle }}</span></dd></div>
          <div><dt>Location</dt><dd><span class="pill">{{ person.location || '—' }}</span></dd></div>
          <div><dt>Notes</dt><dd>{{ person.notes || '—' }}</dd></div>
        </dl>

        <div class="sections">
          <CollapsibleSection
            title="Extra Details"
            summary="Primary skills, secondary job title, notes"
          >
            <dl class="defs">
              <div>
                <dt>Skills</dt>
                <dd>
                  <span v-for="s in person.skills" :key="s" class="chip">{{ s }}</span>
                  <span v-if="!person.skills.length">—</span>
                </dd>
              </div>
              <div><dt>Secondary job title</dt><dd>{{ person.secondaryJobTitle || '—' }}</dd></div>
              <div><dt>Notes</dt><dd>{{ person.notes || '—' }}</dd></div>
            </dl>
          </CollapsibleSection>

          <CollapsibleSection title="Scheduling" summary="Availability, working days, status">
            <dl class="defs">
              <div><dt>Availability</dt><dd>{{ person.availabilityHoursPerWeek }} hours per week</dd></div>
              <div>
                <dt>Working days</dt>
                <dd style="text-transform: capitalize">{{ person.workingDays.length ? person.workingDays.join(', ') : '—' }}</dd>
              </div>
              <div>
                <dt>Status</dt>
                <dd><span class="badge" :class="resourceStatus(person.status).class">{{ resourceStatus(person.status).label }}</span></dd>
              </div>
              <div>
                <dt>Current load</dt>
                <dd>
                  <div class="row" style="gap: 10px">
                    <div class="ubar" :class="{ over: utilisation > 1 }" style="flex: 1; max-width: 200px" aria-hidden="true">
                      <span :style="{ width: `${Math.min(utilisation * 100, 100)}%` }" />
                    </div>
                    <span :class="utilisation > 1 ? 'warn-text' : 'muted'">
                      {{ person.allocatedHoursPerWeek }} / {{ person.availabilityHoursPerWeek }} h
                      ({{ Math.round(utilisation * 100) }}%)<span v-if="utilisation > 1" class="sr-only"> — over-allocated</span>
                    </span>
                  </div>
                </dd>
              </div>
            </dl>
          </CollapsibleSection>

          <CollapsibleSection title="Financial" summary="Charge-out rates">
            <p class="muted" style="margin: 0">
              Per-person rates are not part of the SRA-RMS data model. Project-level budget and
              remaining spend are on the project record.
            </p>
          </CollapsibleSection>
        </div>
      </div>

      <div v-else-if="person" class="detail-body" role="tabpanel">
        <h3 class="list-head">Assigned Projects ({{ assignedProjects.length }})</h3>
        <ul class="rec-list">
          <li v-for="p in assignedProjects" :key="p.id">
            <RouterLink :to="`/projects/${p.id}`" class="rec">
              <span class="rec-title">{{ p.name }}<span v-if="p.code" class="muted"> ({{ p.code }})</span></span>
              <span class="rec-sub">{{ p.clientName ?? '—' }}</span>
            </RouterLink>
          </li>
          <li v-if="!assignedProjects.length" class="muted">Not allocated to any project.</li>
        </ul>

        <h3 class="list-head">Assigned Clients ({{ assignedClients.length }})</h3>
        <ul class="rec-list">
          <li v-for="c in assignedClients" :key="c.id">
            <RouterLink :to="`/clients/${c.id}`" class="rec"><span class="rec-title">{{ c.name }}</span></RouterLink>
          </li>
          <li v-if="!assignedClients.length" class="muted">No clients yet.</li>
        </ul>
      </div>
    </div>
  </SidePanel>

  <PersonFormModal
    v-if="showEdit && person" :resource="person"
    @close="showEdit = false"
    @saved="showEdit = false; load(); emit('changed')"
  />
</template>

<style scoped>
.rail {
  width: 340px; flex-shrink: 0; background: var(--brand-800); color: #dbe7f2;
  display: flex; flex-direction: column; overflow-y: auto;
}
.photo { aspect-ratio: 1; background: var(--brand-700); display: grid; place-items: center; overflow: hidden; }
.photo img { width: 100%; height: 100%; object-fit: cover; }
.photo-initials { font-size: 64px; font-weight: 700; color: #fff; }
.rail-body { padding: 20px 22px 24px; }
.rail-name { color: #fff; font-size: 22px; margin-bottom: 18px; }
.rail-field { margin-bottom: 14px; }
.rail-label { display: block; color: #8fb2c6; font-size: 11.5px; margin-bottom: 2px; }
.rail-email { color: var(--silver); word-break: break-all; }
.rail-field strong { color: #fff; font-weight: 600; }
.status-chip { display: inline-block; background: #fff; color: var(--brand-800); border-radius: 6px; padding: 3px 12px; font-weight: 600; font-size: 12.5px; }
.rail-foot { color: #8fb2c6; font-size: 12px; margin: 20px 0 0; }
.rail-actions { display: flex; gap: 8px; margin-top: 16px; }

.detail { flex: 1; min-width: 0; display: flex; flex-direction: column; }
.detail-tabs { border-bottom: 1px solid var(--border); padding: 0 56px 9px 24px; }
.detail-body { padding: 22px 24px 32px; overflow-y: auto; }
.sections { margin-top: 22px; }
.list-head { font-size: 14px; margin: 22px 0 10px; }
.list-head:first-child { margin-top: 0; }
.rec-list { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
.rec { display: block; border-left: 3px solid var(--brand-400); padding: 2px 0 2px 12px; color: inherit; }
.rec:hover { text-decoration: none; }
.rec:hover .rec-title { text-decoration: underline; }
.rec-title { display: block; color: var(--brand-700); font-weight: 550; }
.rec-sub { display: block; color: var(--text-muted); font-size: 12.5px; }

@media (max-width: 900px) {
  .rail { display: none; }
}
</style>
