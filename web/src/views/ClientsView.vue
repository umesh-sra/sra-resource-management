<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { clientsApi } from '@/api'
import { ApiError } from '@/api/http'
import type { Client, Page } from '@/types'
import { fmtDate } from '@/lib/format'
import { useToastStore } from '@/stores/toast'
import ModalDialog from '@/components/ModalDialog.vue'
import PagerBar from '@/components/PagerBar.vue'

const router = useRouter()
const toast = useToastStore()

const data = ref<Page<Client> | null>(null)
const loading = ref(true)
const q = ref('')
const page = ref(1)

const showCreate = ref(false)
const newName = ref('')
const saving = ref(false)

let searchTimer: ReturnType<typeof setTimeout>

async function load() {
  loading.value = true
  try {
    data.value = await clientsApi.list({ q: q.value || undefined, page: page.value, pageSize: 25, sort: 'name' })
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Failed to load clients')
  } finally {
    loading.value = false
  }
}

function onSearch() {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => { page.value = 1; load() }, 300)
}

function goPage(p: number) { page.value = p; load() }

async function create() {
  if (!newName.value.trim()) return
  saving.value = true
  try {
    const c = await clientsApi.create({ name: newName.value.trim() })
    toast.success('Client created')
    showCreate.value = false
    newName.value = ''
    router.push(`/clients/${c.id}`)
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'Could not create client')
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Clients</h1>
        <div class="subtitle">Organisations and the projects delivered for them.</div>
      </div>
      <button class="btn btn-primary" @click="showCreate = true">+ New client</button>
    </div>

    <div class="card">
      <div class="card-pad row">
        <input class="input" style="max-width: 320px" v-model="q" @input="onSearch" placeholder="Search clients…" aria-label="Search clients" type="search" />
      </div>
      <div class="table-wrap">
        <table class="table">
          <thead><tr><th scope="col">Name</th><th scope="col" class="num">Projects</th><th scope="col">Created</th></tr></thead>
          <tbody>
            <tr v-if="loading"><td colspan="3"><span class="sr-only" role="status">Loading clients…</span><div class="skeleton" style="height: 20px; margin: 6px 0" /></td></tr>
            <template v-else>
              <tr v-for="c in data?.items" :key="c.id" class="clickable" @click="router.push(`/clients/${c.id}`)">
                <td><RouterLink :to="`/clients/${c.id}`" class="row-link" @click.stop><strong>{{ c.name }}</strong></RouterLink></td>
                <td class="num">{{ c.projectCount }}</td>
                <td>{{ fmtDate(c.createdAt) }}</td>
              </tr>
              <tr v-if="data && !data.items.length"><td colspan="3" class="empty">No clients found.</td></tr>
            </template>
          </tbody>
        </table>
      </div>
      <PagerBar v-if="data" :meta="data.meta" @change="goPage" />
    </div>

    <ModalDialog v-if="showCreate" title="New client" @close="showCreate = false">
      <div class="field">
        <label for="client-name">Client name</label>
        <input id="client-name" class="input" v-model="newName" placeholder="e.g. Acme Corporation" @keyup.enter="create" />
      </div>
      <template #footer>
        <button class="btn" @click="showCreate = false">Cancel</button>
        <button class="btn btn-primary" :disabled="saving || !newName.trim()" @click="create">Create</button>
      </template>
    </ModalDialog>
  </div>
</template>
