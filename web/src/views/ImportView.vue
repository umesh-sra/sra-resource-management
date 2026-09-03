<script setup lang="ts">
import { computed, ref } from 'vue'
import { importApi } from '@/api'
import { ApiError } from '@/api/http'
import type { ImportReport } from '@/types'
import { useToastStore } from '@/stores/toast'

/**
 * Data Import (FR-IMP-*): load the incumbent Resource Guru report export.
 *
 * Two steps, deliberately. "Analyse" posts the file with `dryRun=true`, which
 * runs the whole import inside a transaction the API then rolls back — so the
 * numbers and warnings shown here are the ones the real import will produce,
 * not an estimate. Only then does "Import" become available. The same File is
 * posted a second time to commit; nothing is staged server-side, so a preview
 * that is never confirmed leaves no trace.
 */
const toast = useToastStore()

const file = ref<File | null>(null)
const dragging = ref(false)
const busy = ref<'analysing' | 'importing' | null>(null)
const progress = ref(0)
const preview = ref<ImportReport | null>(null)
const result = ref<ImportReport | null>(null)
const error = ref<string | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

/** The report on screen: the committed one once it exists, otherwise the preview. */
const report = computed(() => result.value ?? preview.value)

const totalCreated = computed(() =>
  (report.value?.entities ?? []).reduce((sum, e) => sum + e.created, 0),
)
const totalSkipped = computed(() =>
  (report.value?.entities ?? []).reduce((sum, e) => sum + e.skipped, 0),
)
const totalRows = computed(() =>
  (report.value?.sourceRows ?? []).reduce((sum, s) => sum + s.rows, 0),
)

/** Entity keys are camelCase API tokens; the screen wants readable labels. */
const LABELS: Record<string, string> = {
  clients: 'Clients',
  projects: 'Projects',
  resources: 'People',
  allocations: 'Bookings',
  timeOff: 'Time off',
  bookings: 'Bookings',
  downtime: 'Downtime',
  availability: 'Availability',
  scheduledVsActuals: 'Scheduled vs actuals',
  departments: 'Departments',
  locations: 'Locations',
  jobTitles: 'Job titles',
  skills: 'Skills',
  activityTypes: 'Activity types',
}
function label(key: string): string {
  return LABELS[key] ?? key.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase())
}

const CORE_ENTITIES = ['clients', 'projects', 'resources', 'allocations', 'timeOff']
const coreCounts = computed(() =>
  (report.value?.entities ?? []).filter((e) => CORE_ENTITIES.includes(e.entity)),
)
const referenceCounts = computed(() =>
  (report.value?.entities ?? []).filter((e) => !CORE_ENTITIES.includes(e.entity)),
)

function chooseFile(next: File | null) {
  // A new file invalidates the report on screen — never let an old preview
  // stand next to a file it was not produced from.
  file.value = next
  preview.value = null
  result.value = null
  error.value = null
}

function onDrop(event: DragEvent) {
  dragging.value = false
  const dropped = event.dataTransfer?.files?.[0]
  if (dropped) chooseFile(dropped)
}

function onPick(event: Event) {
  chooseFile((event.target as HTMLInputElement).files?.[0] ?? null)
}

function clear() {
  chooseFile(null)
  if (fileInput.value) fileInput.value.value = ''
}

async function run(dryRun: boolean) {
  if (!file.value) return
  busy.value = dryRun ? 'analysing' : 'importing'
  progress.value = 0
  error.value = null
  try {
    const report = await importApi.resourceGuru(file.value, dryRun, (p) => (progress.value = p))
    if (dryRun) {
      preview.value = report
      result.value = null
      toast.success('Analysed. Nothing has been written yet.')
    } else {
      result.value = report
      toast.success(`Imported ${totalCreated.value.toLocaleString()} records.`)
    }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'The import failed.'
    toast.error(error.value)
  } finally {
    busy.value = null
  }
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}
</script>

<template>
  <div class="toolbar">
    <h1 class="title">Data Import</h1>
    <div class="spacer" />
    <button v-if="file" class="btn btn-ghost" :disabled="!!busy" @click="clear">Start over</button>
    <button
      class="btn"
      :disabled="!file || !!busy"
      @click="run(true)"
    >
      {{ busy === 'analysing' ? 'Analysing…' : 'Analyse' }}
    </button>
    <button
      class="btn btn-primary"
      :disabled="!preview || !!busy || !!result"
      :title="!preview ? 'Analyse the file first' : undefined"
      @click="run(false)"
    >
      {{ busy === 'importing' ? 'Importing…' : 'Import' }}
    </button>
  </div>

  <div class="page page-narrow">
    <div class="hint-banner">
      <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
        <path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm1 15h-2v-6h2v6Zm0-8h-2V7h2v2Z" fill="currentColor" />
      </svg>
      <div>
        Upload the <strong>Resource Guru report export</strong> — the <code>.zip</code> as
        downloaded, or a single exported <code>.csv</code>. Analysing is always safe: the API runs
        the import in a transaction and rolls it back, so the counts below are exactly what
        importing will do. Records that already exist are matched on client name, project code,
        person email and a booking's window, and left alone — so re-importing tops up rather than
        duplicating.
      </div>
    </div>

    <!-- Upload ---------------------------------------------------------- -->
    <div
      class="drop card"
      :class="{ dragging, filled: !!file }"
      @dragover.prevent="dragging = true"
      @dragleave.prevent="dragging = false"
      @drop.prevent="onDrop"
    >
      <input
        id="import-file" ref="fileInput" class="sr-only" type="file"
        accept=".zip,.csv,application/zip,text/csv" :disabled="!!busy" @change="onPick"
      />

      <template v-if="file">
        <svg viewBox="0 0 24 24" width="28" height="28" aria-hidden="true">
          <path d="M6 2h8l6 6v14H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2Zm7 1.5V9h5.5L13 3.5ZM8 13h8v2H8v-2Zm0 4h8v2H8v-2Z" fill="currentColor" />
        </svg>
        <div class="drop-text">
          <strong>{{ file.name }}</strong>
          <span class="muted">{{ formatBytes(file.size) }}</span>
        </div>
        <label class="btn btn-outline" for="import-file">Choose a different file</label>
      </template>

      <template v-else>
        <svg viewBox="0 0 24 24" width="28" height="28" aria-hidden="true">
          <path d="M12 3l5 5h-3v6h-4V8H7l5-5ZM5 18h14v2H5v-2Z" fill="currentColor" />
        </svg>
        <div class="drop-text">
          <strong>Drop the export here</strong>
          <span class="muted">or choose a file — up to 100 MB</span>
        </div>
        <label class="btn btn-primary" for="import-file">Choose file…</label>
      </template>
    </div>

    <div v-if="busy" class="card card-pad" role="status">
      <div class="row row-nowrap">
        <span>{{ busy === 'analysing' ? 'Analysing the export…' : 'Importing…' }}</span>
        <span class="muted" v-if="progress > 0 && progress < 100">uploaded {{ progress }}%</span>
      </div>
      <div class="skeleton" style="height: 8px; margin-top: 12px" />
    </div>

    <div v-if="error" class="card card-pad err" role="alert">{{ error }}</div>

    <!-- Report ---------------------------------------------------------- -->
    <template v-if="report && !busy">
      <div class="row" style="margin: 22px 0 10px">
        <h2 class="sec-title">{{ result ? 'Imported' : 'Preview' }}</h2>
        <span v-if="result" class="badge green">Committed</span>
        <span v-else class="badge amber">Nothing written yet</span>
        <div class="spacer" />
        <span class="muted">{{ (report.durationMs / 1000).toFixed(1) }}s</span>
      </div>

      <div class="grid grid-stats">
        <div class="card stat">
          <div class="label">Rows read</div>
          <div class="value">{{ totalRows.toLocaleString() }}</div>
          <div class="hint">across {{ report.sheetsRead.length }} sheet(s)</div>
        </div>
        <div class="card stat">
          <div class="label">{{ result ? 'Created' : 'To create' }}</div>
          <div class="value">{{ totalCreated.toLocaleString() }}</div>
          <div class="hint">records</div>
        </div>
        <div class="card stat">
          <div class="label">Already present</div>
          <div class="value">{{ totalSkipped.toLocaleString() }}</div>
          <div class="hint">left untouched</div>
        </div>
        <div class="card stat">
          <div class="label">Warnings</div>
          <div class="value" :class="{ warn: report.warnings.length > 0 }">{{ report.warnings.length }}</div>
          <div class="hint">non-blocking</div>
        </div>
      </div>

      <div class="card">
        <div class="table-wrap">
          <table class="table">
            <caption class="sr-only">Records by entity</caption>
            <thead>
              <tr>
                <th scope="col">Entity</th>
                <th scope="col" class="num">{{ result ? 'Created' : 'To create' }}</th>
                <th scope="col" class="num">Updated</th>
                <th scope="col" class="num">Already present</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="e in coreCounts" :key="e.entity">
                <td>{{ label(e.entity) }}</td>
                <td class="num strong">{{ e.created.toLocaleString() }}</td>
                <td class="num">{{ e.updated ? e.updated.toLocaleString() : '—' }}</td>
                <td class="num muted">{{ e.skipped ? e.skipped.toLocaleString() : '—' }}</td>
              </tr>
              <tr v-for="e in referenceCounts" :key="e.entity" class="ref-row">
                <td>{{ label(e.entity) }} <span class="badge gray">pick-list</span></td>
                <td class="num strong">{{ e.created.toLocaleString() }}</td>
                <td class="num">{{ e.updated ? e.updated.toLocaleString() : '—' }}</td>
                <td class="num muted">{{ e.skipped ? e.skipped.toLocaleString() : '—' }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Warnings ------------------------------------------------------ -->
      <template v-if="report.warnings.length">
        <h3 class="sub-title">Warnings</h3>
        <p class="muted note">
          None of these stopped the import. Each is grouped by kind, with the number of records
          affected.
        </p>
        <div class="card">
          <ul class="issues">
            <li v-for="w in report.warnings" :key="w.code">
              <div class="row row-nowrap issue-head">
                <span class="badge amber">{{ w.count.toLocaleString() }}</span>
                <code class="issue-code">{{ w.code }}</code>
              </div>
              <p class="issue-msg">{{ w.message }}</p>
              <p v-if="w.examples.length" class="issue-eg muted">
                e.g. {{ w.examples.join(', ') }}
              </p>
            </li>
          </ul>
        </div>
      </template>

      <!-- Not imported -------------------------------------------------- -->
      <template v-if="report.unmappedFields.length">
        <h3 class="sub-title">Not imported</h3>
        <p class="muted note">
          Source columns SRA-RMS has nowhere to put. Listed so the loss is explicit rather than
          silent.
        </p>
        <div class="card">
          <div class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">Sheet</th>
                  <th scope="col">Column</th>
                  <th scope="col">Why</th>
                  <th scope="col" class="num">Rows</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="f in report.unmappedFields" :key="`${f.sheet}.${f.field}`">
                  <td class="muted">{{ f.sheet }}</td>
                  <td>{{ f.field }}</td>
                  <td class="muted">{{ f.reason }}</td>
                  <td class="num">{{ f.nonEmptyRows.toLocaleString() }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- Source -------------------------------------------------------- -->
      <h3 class="sub-title">Source</h3>
      <p class="muted note">
        Rows read, per sheet. Files in the archive with no row count are derived reports
        SRA-RMS does not store — utilisation percentages and timesheet actuals.
      </p>
      <div class="card card-pad">
        <ul class="files">
          <li v-for="s in report.sourceRows" :key="s.sheet">
            <span>{{ label(s.sheet) }}</span>
            <span class="muted">{{ s.rows.toLocaleString() }} rows</span>
          </li>
        </ul>
        <ul class="files mono-list">
          <li v-for="name in report.sourceFiles" :key="name">
            <span class="mono">{{ name }}</span>
          </li>
        </ul>
      </div>

      <div v-if="result" class="row done">
        <RouterLink class="btn btn-primary" to="/people">Open People &amp; Resources</RouterLink>
        <RouterLink class="btn" to="/projects">Open Projects &amp; Clients</RouterLink>
        <RouterLink class="btn" to="/schedule">Open Schedule</RouterLink>
      </div>
    </template>
  </div>
</template>

<style scoped>
.title { font-size: 17px; font-weight: 660; margin: 0; }
.sec-title { font-size: 15px; font-weight: 640; margin: 0; }
.sub-title { font-size: 14px; font-weight: 640; margin: 24px 0 4px; }
.note { font-size: 12.5px; margin: 0 0 10px; }

/* Drop zone: the card outline becomes the target, and thickens on drag-over. */
.drop {
  display: flex; align-items: center; gap: 16px; padding: 22px 24px;
  border: 2px dashed var(--gray-300); background: var(--surface);
}
.drop.dragging { border-color: var(--brand-500); background: var(--brand-50); }
.drop.filled { border-style: solid; border-color: var(--border); }
.drop svg { color: var(--brand-500); flex-shrink: 0; }
.drop-text { display: flex; flex-direction: column; gap: 2px; min-width: 0; flex: 1; }
.drop-text strong { font-weight: 620; overflow-wrap: anywhere; }
.drop-text .muted { font-size: 12.5px; }
/* The file input is visually hidden, so its label carries the focus ring. */
.drop input:focus-visible + * { outline: 2px solid var(--focus-ring); outline-offset: 2px; }

.err { border-left: 3px solid var(--red-600); color: var(--red-700); }

.value.warn { color: var(--amber-700); }
.num { text-align: right; }
.strong { font-weight: 640; }
.ref-row td:first-child { color: var(--text-muted); }

.issues { list-style: none; margin: 0; padding: 0; }
.issues > li { padding: 14px 18px; border-top: 1px solid var(--border); }
.issues > li:first-child { border-top: 0; }
.issue-head { gap: 9px; }
.issue-code { font-size: 11.5px; color: var(--text-muted); font-family: ui-monospace, Menlo, Consolas, monospace; }
.issue-msg { margin: 6px 0 0; }
.issue-eg { margin: 4px 0 0; font-size: 12.5px; overflow-wrap: anywhere; }

.files { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 5px; }
.mono-list { margin-top: 14px; padding-top: 12px; border-top: 1px solid var(--border); }
.files li { display: flex; gap: 12px; justify-content: space-between; font-size: 13px; }
.mono { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 12.5px; overflow-wrap: anywhere; }

.done { margin-top: 22px; }
</style>
