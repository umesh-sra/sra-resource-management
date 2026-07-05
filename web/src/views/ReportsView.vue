<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { reportsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { UtilisationReport } from '@/types'
import { useToastStore } from '@/stores/toast'

const toast = useToastStore()
const report = ref<UtilisationReport | null>(null)
const loading = ref(false)

// Format in local time. toISOString() converts to UTC, which shifts local
// midnight to the previous day in UTC+ timezones (e.g. Australia/Sydney),
// making the default range off by one day.
const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
const today = new Date()
const from = ref(iso(new Date(today.getFullYear(), today.getMonth(), 1)))
const to = ref(iso(new Date(today.getFullYear(), today.getMonth() + 6, 0)))

async function run() {
  if (to.value < from.value) { toast.error('"To" must be on or after "From".'); return }
  loading.value = true
  try {
    report.value = await reportsApi.utilisation({ from: from.value, to: to.value })
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to generate report')
  } finally {
    loading.value = false
  }
}

const csvUrl = () => reportsApi.utilisationCsvUrl({ from: from.value, to: to.value })

onMounted(run)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Reports</h1>
        <div class="subtitle">Resource utilisation over a date range.</div>
      </div>
    </div>

    <div class="card card-pad row">
      <label class="muted" for="report-from">From</label>
      <input id="report-from" class="input" style="max-width: 170px" type="date" v-model="from" />
      <label class="muted" for="report-to">To</label>
      <input id="report-to" class="input" style="max-width: 170px" type="date" v-model="to" />
      <button class="btn btn-primary" :disabled="loading" @click="run">Generate<span class="sr-only"> utilisation report</span></button>
      <div class="spacer" />
      <a class="btn" :href="csvUrl()" target="_blank" rel="noopener"><span aria-hidden="true">⬇</span> Export CSV<span class="sr-only"> (opens in a new tab)</span></a>
    </div>

    <div class="card" style="margin-top: 16px">
      <div class="card-pad" style="padding-bottom: 8px">
        <h2>Utilisation</h2>
        <div v-if="report" class="muted">{{ report.from }} → {{ report.to }}</div>
      </div>
      <div class="table-wrap">
        <table class="table">
          <thead><tr><th scope="col">Resource</th><th scope="col">Department</th><th scope="col" class="num">Available (h)</th><th scope="col" class="num">Allocated (h)</th><th scope="col" style="width: 220px">Utilisation</th></tr></thead>
          <tbody>
            <tr v-if="loading"><td colspan="5"><span class="sr-only" role="status">Generating report…</span><div class="skeleton" style="height: 20px; margin: 6px 0" /></td></tr>
            <template v-else-if="report">
              <tr v-for="r in report.rows" :key="r.resourceId">
                <td>{{ r.resourceName }}</td>
                <td>{{ r.department ?? '—' }}</td>
                <td class="num">{{ r.availableHours.toLocaleString() }}</td>
                <td class="num">{{ r.allocatedHours.toLocaleString() }}</td>
                <td>
                  <div class="row" style="gap: 10px">
                    <div class="ubar" :class="{ over: r.utilisation > 1 }" style="flex: 1" aria-hidden="true">
                      <span :style="{ width: Math.min(r.utilisation * 100, 100) + '%' }" />
                    </div>
                    <span :class="r.utilisation > 1 ? 'warn-text' : 'muted'" style="min-width: 42px; text-align: right">
                      {{ Math.round(r.utilisation * 100) }}%<span v-if="r.utilisation > 1" class="sr-only"> — over-allocated</span>
                    </span>
                  </div>
                </td>
              </tr>
              <tr v-if="!report.rows.length"><td colspan="5" class="empty">No data for this range.</td></tr>
            </template>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>
