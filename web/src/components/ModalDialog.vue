<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useId } from 'vue'

defineProps<{ title: string }>()
const emit = defineEmits<{ close: [] }>()

const titleId = useId()
const modalEl = ref<HTMLElement | null>(null)
let previouslyFocused: HTMLElement | null = null

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

function focusables(): HTMLElement[] {
  return Array.from(modalEl.value?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? [])
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    e.stopPropagation()
    emit('close')
    return
  }
  // Keep Tab cycling inside the dialog (WCAG 2.4.3 / dialog pattern)
  if (e.key === 'Tab') {
    const els = focusables()
    if (!els.length) return
    const first = els[0]!
    const last = els[els.length - 1]!
    const active = document.activeElement
    if (e.shiftKey && (active === first || active === modalEl.value)) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && active === last) {
      e.preventDefault()
      first.focus()
    }
  }
}

onMounted(() => {
  previouslyFocused = document.activeElement as HTMLElement | null
  // Focus the first field if there is one, otherwise the dialog itself
  const els = focusables()
  const firstInput = els.find((el) => !el.classList.contains('modal-close'))
  ;(firstInput ?? modalEl.value)?.focus()
})

onBeforeUnmount(() => {
  previouslyFocused?.focus()
})
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('close')" @keydown="onKeydown">
    <div class="modal" role="dialog" aria-modal="true" :aria-labelledby="titleId" ref="modalEl" tabindex="-1">
      <div class="modal-head">
        <h2 :id="titleId">{{ title }}</h2>
        <button class="btn btn-ghost btn-sm modal-close" aria-label="Close dialog" @click="emit('close')">✕</button>
      </div>
      <div class="modal-body">
        <slot />
      </div>
      <div class="modal-foot">
        <slot name="footer" />
      </div>
    </div>
  </div>
</template>
