<script setup lang="ts">
import type { PageMeta } from '@/types'
const props = defineProps<{ meta: PageMeta }>()
const emit = defineEmits<{ change: [page: number] }>()

const from = () => (props.meta.totalItems === 0 ? 0 : (props.meta.page - 1) * props.meta.pageSize + 1)
const to = () => Math.min(props.meta.page * props.meta.pageSize, props.meta.totalItems)
</script>

<template>
  <nav class="row" aria-label="Pagination" style="padding: 12px 14px; border-top: 1px solid var(--border);">
    <span class="muted">{{ from() }}–{{ to() }} of {{ meta.totalItems }}</span>
    <div class="spacer" />
    <button class="btn btn-sm" :disabled="meta.page <= 1" @click="emit('change', meta.page - 1)">Previous<span class="sr-only"> page</span></button>
    <span class="muted">Page {{ meta.page }} / {{ Math.max(meta.totalPages, 1) }}</span>
    <button class="btn btn-sm" :disabled="meta.page >= meta.totalPages" @click="emit('change', meta.page + 1)">Next<span class="sr-only"> page</span></button>
  </nav>
</template>
