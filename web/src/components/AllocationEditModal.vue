<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { allocationsApi, resourcesApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Allocation, EffortUnit, Resource } from '@/types'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'
import AppAvatar from '@/components/AppAvatar.vue'

const props = defineProps<{ allocation: Allocation }>()
const emit = defineEmits<{ close: []; saved: [] }>()
const toast = useToastStore()

const saving = ref(false)
const form = ref({
  startDate: props.allocation.startDate,
  endDate: props.allocation.endDate,
  effort: props.allocation.effort,
  effortUnit: props.allocation.effortUnit as EffortUnit,
  roleOnProject: props.allocation.roleOnProject ?? '',
  billable: props.allocation.billable,
  hourlyRate: props.allocation.hourlyRate ?? ('' as number | ''),
  details: props.allocation.details ?? '',
  bookerId: props.allocation.bookerId ?? '',
})

/**
 * PUT /allocations/{id} replaces the record, so every field the dialog can hold
 * has to round-trip — omitting Details or Booker here would silently clear them
 * on the next save.
 */
const resources = ref<Resource[]>([])
const booker = computed(() => resources.value.find((r) => r.id === form.value.bookerId))

onMounted(async () => {
  try {
    resources.value = (await resourcesApi.list({ pageSize: 200, sort: 'name' })).items
  } catch {
    // A missing list only costs the picker; the rest of the dialog still works.
  }
})

async function save() {
  saving.value = true
  try {
    const f = form.value
    const a = await allocationsApi.update(props.allocation.id, {
      startDate: f.startDate,
      endDate: f.endDate,
      effort: Number(f.effort),
      effortUnit: f.effortUnit,
      roleOnProject: f.roleOnProject || undefined,
      billable: f.billable,
      hourlyRate: f.hourlyRate === '' ? undefined : Number(f.hourlyRate),
      details: f.details.trim() || undefined,
      bookerId: f.bookerId || undefined,
    })
    // Over-allocation is allowed (FR-ALL-6): the save succeeded, so surface
    // warnings as warnings, not errors.
    if (a.warnings?.length) toast.warning(`Allocation updated — ${a.warnings.join(' ')}`)
    else toast.success('Allocation updated')
    emit('saved')
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not update allocation')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <ModalDialog :title="`Edit allocation — ${allocation.resourceName ?? 'resource'}`" @close="emit('close')">
    <div class="form-row">
      <div class="field"><label for="ea-start">Start date</label><input id="ea-start" class="input" v-model="form.startDate" type="date" /></div>
      <div class="field"><label for="ea-end">End date</label><input id="ea-end" class="input" v-model="form.endDate" type="date" /></div>
    </div>
    <div class="form-row">
      <div class="field"><label for="ea-effort">Effort</label><input id="ea-effort" class="input" v-model.number="form.effort" type="number" min="0" /></div>
      <div class="field"><label for="ea-unit">Unit</label>
        <select id="ea-unit" class="select" v-model="form.effortUnit">
          <option value="hoursPerWeek">Hours / week</option><option value="percent">Percent</option>
        </select>
      </div>
    </div>
    <div class="form-row">
      <div class="field"><label for="ea-role">Role on project (optional)</label><input id="ea-role" class="input" v-model="form.roleOnProject" /></div>
      <div class="field"><label for="ea-rate">Hourly rate (optional)</label><input id="ea-rate" class="input" type="number" min="0" step="0.01" v-model="form.hourlyRate" /></div>
    </div>
    <div class="field">
      <label style="display:flex; align-items:center; gap:8px">
        <input type="checkbox" v-model="form.billable" /> Billable
      </label>
    </div>
    <div class="field">
      <label for="ea-details">Details (optional)</label>
      <textarea id="ea-details" class="input" rows="3" v-model="form.details" />
    </div>
    <div class="field">
      <label for="ea-booker">Booker (optional)</label>
      <div class="picker">
        <AppAvatar v-if="booker" :name="booker.name" :image-url="booker.imageUrl" :size="30" />
        <select id="ea-booker" class="select" v-model="form.bookerId">
          <option value="">No booker recorded</option>
          <option v-for="r in resources" :key="r.id" :value="r.id">{{ r.name }}</option>
        </select>
      </div>
    </div>
    <p class="muted" style="font-size: 12.5px">Dates must fall within the project window. Over-allocation is allowed but flagged.</p>
    <template #footer>
      <button class="btn" @click="emit('close')">Cancel</button>
      <button class="btn btn-primary" :disabled="saving || !form.startDate || !form.endDate" @click="save">Save</button>
    </template>
  </ModalDialog>
</template>

<style scoped>
.picker { display: flex; align-items: center; gap: 10px; }
.picker .select { flex: 1; min-width: 0; }
</style>
