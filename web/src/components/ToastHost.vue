<script setup lang="ts">
import { useToastStore } from '@/stores/toast'
const toasts = useToastStore()
</script>

<template>
  <div class="toasts">
    <div
      v-for="t in toasts.toasts"
      :key="t.id"
      class="toast"
      :class="{ error: t.kind === 'error', success: t.kind === 'success', warning: t.kind === 'warning' }"
      @click="toasts.dismiss(t.id)"
    >
      {{ t.message }}
    </div>
  </div>
</template>
