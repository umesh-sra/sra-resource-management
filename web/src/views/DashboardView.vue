<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { dashboardApi } from '@/api'
import { ApiError } from '@/api/http'
import type { DashboardSummary } from '@/types'
import { fmtDate, fmtMoney, fmtPercent, projectStatus } from '@/lib/format'

const router = useRouter()
const data = ref<DashboardSummary | null>(null)
const loading = ref(true)
const error = ref('')

onMounted(async () => {
  try {
    data.value = await dashboardApi.summary()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Failed to load dashboard'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Dashboard</h1>
        <div class="subtitle">Portfolio utilisation and project health at a glance.</div>
      </div>
    </div>

    <div v-if="loading" class="grid grid-stats">
      <span class="sr-only" role="status">Loading dashboard…</span>
      <div v-for="n in 6" :key="n" class="card stat"><div class="skeleton" style="height: 56px" /></div>
    </div>

    <div v-else-if="error" class="card card-pad empty">{{ error }}</div>

    <template v-else-if="data">
      <div class="grid grid-stats">
        <div class="card stat">
          <div class="label">Active projects</div>
          <div class="value">{{ data.activeProjects }}</div>
        </div>
        <div class="card stat">
          <div class="label">Resources</div>
          <div class="value">{{ data.totalResources }}</div>
        </div>
        <div class="card stat">
          <div class="label">Avg utilisation</div>
          <div class="value">{{ fmtPercent(data.averageUtilisation) }}</div>
          <div class="hint">across resources with availability</div>
        </div>
        <div class="card stat">
          <div class="label">Over-allocated</div>
          <div class="value" :style="{ color: data.overAllocatedResources ? 'var(--red-600)' : undefined }">
            {{ data.overAllocatedResources }}
          </div>
          <div class="hint">resources above capacity</div>
        </div>
        <div class="card stat">
          <div class="label">Under-allocated</div>
          <div class="value">{{ data.underAllocatedResources }}</div>
          <div class="hint">below 50% utilisation</div>
        </div>
        <div class="card stat">
          <div class="label">Budget at risk</div>
          <div class="value">{{ fmtMoney(data.budgetAtRisk) }}</div>
          <div class="hint">active projects ≥90% consumed</div>
        </div>
      </div>

      <div class="grid grid-2" style="margin-top: 16px;">
        <div class="card">
          <div class="card-pad" style="padding-bottom: 8px"><h2>Upcoming project starts</h2></div>
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th scope="col">Project</th><th scope="col">Client</th><th scope="col">Status</th><th scope="col">Starts</th></tr></thead>
              <tbody>
                <tr v-for="p in data.upcomingProjectStarts" :key="p.id" class="clickable" @click="router.push(`/projects/${p.id}`)">
                  <td><RouterLink :to="`/projects/${p.id}`" class="row-link" @click.stop>{{ p.code }} — {{ p.name }}</RouterLink></td>
                  <td>{{ p.clientName }}</td>
                  <td><span class="badge" :class="projectStatus(p.status).class">{{ projectStatus(p.status).label }}</span></td>
                  <td>{{ fmtDate(p.startDate) }}</td>
                </tr>
                <tr v-if="!data.upcomingProjectStarts.length"><td colspan="4" class="empty">Nothing on the horizon.</td></tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card">
          <div class="card-pad" style="padding-bottom: 8px"><h2>Upcoming roll-offs</h2></div>
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th scope="col">Resource</th><th scope="col">Project</th><th scope="col">Ends</th></tr></thead>
              <tbody>
                <tr v-for="a in data.upcomingRollOffs" :key="a.id" class="clickable" @click="router.push(`/resources/${a.resourceId}`)">
                  <td><RouterLink :to="`/resources/${a.resourceId}`" class="row-link" @click.stop>{{ a.resourceName }}</RouterLink></td>
                  <td>{{ a.projectName }}</td>
                  <td>{{ fmtDate(a.endDate) }}</td>
                </tr>
                <tr v-if="!data.upcomingRollOffs.length"><td colspan="3" class="empty">No resources rolling off soon.</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
