<script setup lang="ts">
import { useToastStore } from '@/stores/toast'
const toasts = useToastStore()
</script>

<template>
  <!-- aria-live region: new toasts are announced by screen readers -->
  <div class="toasts" aria-live="polite" aria-atomic="false">
    <div
      v-for="t in toasts.toasts"
      :key="t.id"
      class="toast"
      :class="{ error: t.kind === 'error', success: t.kind === 'success', warning: t.kind === 'warning' }"
      :role="t.kind === 'error' ? 'alert' : 'status'"
    >
      <span>{{ t.message }}</span>
      <button class="toast-dismiss" aria-label="Dismiss notification" @click="toasts.dismiss(t.id)">✕</button>
    </div>
  </div>
</template>
