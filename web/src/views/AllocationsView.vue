<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { allocationsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Allocation, Page } from '@/types'
import { fmtDate } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import PagerBar from '@/components/PagerBar.vue'
import AllocationEditModal from '@/components/AllocationEditModal.vue'

const toast = useToastStore()

const data = ref<Page<Allocation> | null>(null)
const loading = ref(true)
const from = ref('')
const to = ref('')
const page = ref(1)
const editAlloc = ref<Allocation | null>(null)

async function load() {
  loading.value = true
  try {
    data.value = await allocationsApi.list({
      from: from.value || undefined, to: to.value || undefined,
      page: page.value, pageSize: 25,
    })
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load allocations')
  } finally {
    loading.value = false
  }
}
function goPage(p: number) { page.value = p; load() }

async function remove(a: Allocation) {
  if (!confirm(`Remove ${a.resourceName} from ${a.projectName}?`)) return
  try {
    await allocationsApi.remove(a.id)
    toast.success('Allocation removed')
    load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not remove allocation')
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Allocations</h1>
        <div class="subtitle">Every resource-to-project assignment. Add allocations from a project page.</div>
      </div>
    </div>

    <div class="card">
      <div class="card-pad row">
        <label class="muted" for="alloc-from">From</label>
        <input id="alloc-from" class="input" style="max-width: 170px" type="date" v-model="from" @change="page = 1; load()" />
        <label class="muted" for="alloc-to">To</label>
        <input id="alloc-to" class="input" style="max-width: 170px" type="date" v-model="to" @change="page = 1; load()" />
        <button v-if="from || to" class="btn btn-sm" @click="from = ''; to = ''; page = 1; load()">Clear<span class="sr-only"> date filters</span></button>
      </div>
      <div class="table-wrap">
        <table class="table">
          <thead><tr><th scope="col">Resource</th><th scope="col">Project</th><th scope="col">Role</th><th scope="col">Dates</th><th scope="col" class="num">Effort</th><th scope="col">Billable</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>
            <tr v-if="loading"><td colspan="7"><span class="sr-only" role="status">Loading allocations…</span><div class="skeleton" style="height: 20px; margin: 6px 0" /></td></tr>
            <template v-else>
              <tr v-for="a in data?.items" :key="a.id">
                <td><RouterLink :to="`/resources/${a.resourceId}`">{{ a.resourceName }}</RouterLink></td>
                <td><RouterLink :to="`/projects/${a.projectId}`">{{ a.projectName }}</RouterLink></td>
                <td>{{ a.roleOnProject ?? '—' }}</td>
                <td>{{ fmtDate(a.startDate) }} – {{ fmtDate(a.endDate) }}</td>
                <td class="num">{{ a.effort }} {{ a.effortUnit === 'percent' ? '%' : 'h/wk' }}</td>
                <td><span class="badge" :class="a.billable ? 'green' : 'gray'">{{ a.billable ? 'Billable' : 'Non-billable' }}</span></td>
                <td class="num">
                  <button class="btn btn-sm" @click="editAlloc = a">Edit<span class="sr-only"> allocation of {{ a.resourceName }} on {{ a.projectName }}</span></button>
                  <button class="btn btn-sm btn-danger" @click="remove(a)">Remove<span class="sr-only"> allocation of {{ a.resourceName }} on {{ a.projectName }}</span></button>
                </td>
              </tr>
              <tr v-if="data && !data.items.length"><td colspan="7" class="empty">No allocations found.</td></tr>
            </template>
          </tbody>
        </table>
      </div>
      <PagerBar v-if="data" :meta="data.meta" @change="goPage" />
    </div>

    <AllocationEditModal
      v-if="editAlloc" :allocation="editAlloc"
      @close="editAlloc = null" @saved="editAlloc = null; load()"
    />
  </div>
</template>
