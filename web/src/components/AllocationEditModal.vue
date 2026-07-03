<script setup lang="ts">
import { ref } from 'vue'
import { allocationsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Allocation, EffortUnit } from '@/types'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'

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
      <div class="field"><label>Start date</label><input class="input" v-model="form.startDate" type="date" /></div>
      <div class="field"><label>End date</label><input class="input" v-model="form.endDate" type="date" /></div>
    </div>
    <div class="form-row">
      <div class="field"><label>Effort</label><input class="input" v-model.number="form.effort" type="number" min="0" /></div>
      <div class="field"><label>Unit</label>
        <select class="select" v-model="form.effortUnit">
          <option value="hoursPerWeek">Hours / week</option><option value="percent">Percent</option>
        </select>
      </div>
    </div>
    <div class="field"><label>Role on project (optional)</label><input class="input" v-model="form.roleOnProject" /></div>
    <div class="field">
      <label style="display:flex; align-items:center; gap:8px">
        <input type="checkbox" v-model="form.billable" /> Billable
      </label>
    </div>
    <p class="muted" style="font-size: 12.5px">Dates must fall within the project window. Over-allocation is allowed but flagged.</p>
    <template #footer>
      <button class="btn" @click="emit('close')">Cancel</button>
      <button class="btn btn-primary" :disabled="saving || !form.startDate || !form.endDate" @click="save">Save</button>
    </template>
  </ModalDialog>
</template>
