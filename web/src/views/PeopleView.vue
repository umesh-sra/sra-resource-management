<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Page, Resource, ResourceStatus } from '@/types'
import { resourceStatus } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import AppAvatar from '@/components/AppAvatar.vue'
import PagerBar from '@/components/PagerBar.vue'
import PersonPanel from '@/components/PersonPanel.vue'
import PersonFormModal from '@/components/PersonFormModal.vue'

/**
 * People & Resources (screens/people and resources.png): a photo-card grid with
 * a search box, the record count, and a "New" pill. Selecting a card opens the
 * person drawer at /people/:id, so a person is deep-linkable.
 */
const route = useRoute()
const router = useRouter()
const toast = useToastStore()

const data = ref<Page<Resource> | null>(null)
const loading = ref(true)
const q = ref('')
const skillFilter = ref('')
const statusFilter = ref<'' | ResourceStatus>('')
const layout = ref<'grid' | 'list'>((localStorage.getItem('sra-rms.people-layout') as 'grid' | 'list') ?? 'grid')
const page = ref(1)
const showCreate = ref(false)
let timer: ReturnType<typeof setTimeout>

const selectedId = computed(() => (route.params.id as string | undefined) ?? null)

watch(layout, (v) => localStorage.setItem('sra-rms.people-layout', v))

async function load() {
  loading.value = true
  try {
    const skills = skillFilter.value.split(',').map((s) => s.trim()).filter(Boolean)
    data.value = await resourcesApi.list({
      q: q.value || undefined,
      skill: skills.length ? skills : undefined,
      status: statusFilter.value || undefined,
      page: page.value,
      pageSize: layout.value === 'grid' ? 48 : 25,
      sort: 'name',
    })
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load people')
  } finally {
    loading.value = false
  }
}

function onFilter() {
  clearTimeout(timer)
  timer = setTimeout(() => { page.value = 1; load() }, 300)
}
function goPage(p: number) { page.value = p; load() }

function open(r: Resource) { router.push(`/people/${r.id}`) }
function closePanel() { router.push({ path: '/people', query: route.query }) }

/**
 * Job titles get a stable tint so the grid reads like the reference's, where
 * seniority is distinguishable at a glance.
 */
const TITLE_TINTS = ['#8a5700', '#15703f', '#1657a0', '#7a4b8f', '#a82828', '#155e75']
function titleColour(title: string): string {
  let h = 0
  for (const ch of title) h = (h * 31 + ch.charCodeAt(0)) >>> 0
  return TITLE_TINTS[h % TITLE_TINTS.length]!
}

onMounted(load)
</script>

<template>
  <div class="toolbar">
    <h1 class="sr-only">People &amp; Resources</h1>

    <label class="search">
      <span class="sr-only">Search people by name or email</span>
      <input class="input" type="search" v-model="q" @input="onFilter" placeholder="Search" />
      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M10 4a6 6 0 1 1 0 12 6 6 0 0 1 0-12Zm10 16-5.2-5.2" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" /></svg>
    </label>

    <input class="input" style="max-width: 210px" v-model="skillFilter" @input="onFilter" placeholder="Skills (comma, AND)…" aria-label="Filter by skills, comma-separated" />

    <select class="select" style="max-width: 150px" v-model="statusFilter" @change="onFilter" aria-label="Filter by status">
      <option value="">All statuses</option>
      <option value="active">Active</option>
      <option value="inactive">Inactive</option>
      <option value="onLeave">On leave</option>
    </select>

    <div class="spacer" />

    <div class="segmented" role="group" aria-label="Layout">
      <button :aria-pressed="layout === 'grid'" @click="layout = 'grid'; page = 1; load()">Cards</button>
      <button :aria-pressed="layout === 'list'" @click="layout = 'list'; page = 1; load()">List</button>
    </div>

    <button class="btn btn-accent btn-pill" @click="showCreate = true">
      <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><path d="M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Zm1 8h4v2h-4v4h-2v-4H7v-2h4V7h2v4Z" fill="currentColor" /></svg>
      New
    </button>
  </div>

  <div class="page">
    <p class="count" role="status">
      <template v-if="loading">Loading people…</template>
      <template v-else>{{ data?.meta.totalItems ?? 0 }} {{ (data?.meta.totalItems ?? 0) === 1 ? 'person' : 'people' }}</template>
    </p>

    <!-- Card grid ------------------------------------------------------ -->
    <div v-if="layout === 'grid'">
      <div v-if="loading" class="people-grid">
        <div v-for="n in 12" :key="n" class="card"><div class="skeleton" style="aspect-ratio: 1" /><div style="padding: 12px"><div class="skeleton" style="height: 30px" /></div></div>
      </div>
      <div v-else-if="data?.items.length" class="people-grid">
        <button v-for="r in data.items" :key="r.id" class="person-card" type="button" @click="open(r)">
          <span class="person-photo">
            <AppAvatar :name="r.name" :image-url="r.imageUrl" :size="200" square soft />
          </span>
          <span class="person-foot">
            <span class="person-name">{{ r.name }}</span>
            <span class="person-title" :style="{ color: titleColour(r.primaryJobTitle) }">{{ r.primaryJobTitle }}</span>
            <span v-if="r.status !== 'active'" class="badge" :class="resourceStatus(r.status).class">{{ resourceStatus(r.status).label }}</span>
          </span>
        </button>
      </div>
      <div v-else class="card card-pad empty">No people match these filters.</div>
    </div>

    <!-- List ----------------------------------------------------------- -->
    <div v-else class="card">
      <div class="table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Name</th><th scope="col">Job title</th><th scope="col">Department</th>
              <th scope="col">Location</th><th scope="col">Skills</th>
              <th scope="col" class="num">Avail (h/wk)</th><th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td colspan="7"><div class="skeleton" style="height: 20px; margin: 6px 0" /></td></tr>
            <template v-else>
              <tr v-for="r in data?.items" :key="r.id" class="clickable" @click="open(r)">
                <td>
                  <div class="row" style="gap: 9px; flex-wrap: nowrap">
                    <AppAvatar :name="r.name" :image-url="r.imageUrl" :size="30" />
                    <RouterLink :to="`/people/${r.id}`" class="row-link" @click.stop><strong>{{ r.name }}</strong></RouterLink>
                  </div>
                </td>
                <td>{{ r.primaryJobTitle }}</td>
                <td>{{ r.department ?? '—' }}</td>
                <td>{{ r.location ?? '—' }}</td>
                <td>
                  <span v-for="s in r.skills.slice(0, 3)" :key="s" class="chip">{{ s }}</span>
                  <span v-if="r.skills.length > 3" class="muted">+{{ r.skills.length - 3 }}</span>
                </td>
                <td class="num">{{ r.availabilityHoursPerWeek }}</td>
                <td><span class="badge" :class="resourceStatus(r.status).class">{{ resourceStatus(r.status).label }}</span></td>
              </tr>
              <tr v-if="data && !data.items.length"><td colspan="7" class="empty">No people match these filters.</td></tr>
            </template>
          </tbody>
        </table>
      </div>
    </div>

    <PagerBar v-if="data && data.meta.totalPages > 1" :meta="data.meta" @change="goPage" />
  </div>

  <PersonPanel v-if="selectedId" :resource-id="selectedId" @close="closePanel" @changed="load" />

  <PersonFormModal
    v-if="showCreate"
    @close="showCreate = false"
    @saved="(r) => { showCreate = false; load(); router.push(`/people/${r.id}`) }"
  />
</template>

<style scoped>
.count { color: var(--text-muted); font-size: 13px; margin: 0 0 14px; }

.people-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(178px, 1fr)); gap: 14px; }

/* Column layout so every tile in a row keeps an identical square photo and the
   footer absorbs the leftover height — otherwise a status badge on one card
   squashes that card's photo and the row reads as ragged. */
.person-card {
  display: flex; flex-direction: column; text-align: left; padding: 0; cursor: pointer; overflow: hidden;
  background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius);
  box-shadow: var(--shadow-sm); transition: box-shadow .12s, transform .12s;
}
.person-card:hover { box-shadow: var(--shadow); transform: translateY(-1px); }
.person-photo { display: block; aspect-ratio: 1; flex: none; background: var(--gray-100); }
/* The avatar sizes itself from the `size` prop; stretch it to fill the tile. */
.person-photo :deep(.av) { width: 100% !important; height: 100% !important; border-radius: 0 !important; }
.person-foot { display: block; flex: 1; padding: 11px 13px 13px; border-top: 1px solid var(--border); }
.person-name { display: block; font-weight: 650; color: var(--gray-900); font-size: 13.5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.person-title { display: block; font-size: 12.5px; margin-top: 2px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.person-foot .badge { margin-top: 6px; }
</style>
