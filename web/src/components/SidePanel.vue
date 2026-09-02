<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useScrollLock } from '@/lib/scrollLock'

/**
 * Right-hand drawer used for record detail (screens/person_overview.png).
 * Same dialog semantics as ModalDialog: labelled, modal, focus-trapped,
 * Escape-dismissable, and it restores focus to whatever opened it.
 */
defineProps<{ labelledBy?: string; label?: string }>()
const emit = defineEmits<{ close: [] }>()

useScrollLock()

const panelEl = ref<HTMLElement | null>(null)
let previouslyFocused: HTMLElement | null = null

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

function focusables(): HTMLElement[] {
  return Array.from(panelEl.value?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? [])
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    e.stopPropagation()
    emit('close')
    return
  }
  if (e.key !== 'Tab') return
  const els = focusables()
  if (!els.length) return
  const first = els[0]!
  const last = els[els.length - 1]!
  const active = document.activeElement
  if (e.shiftKey && (active === first || active === panelEl.value)) {
    e.preventDefault()
    last.focus()
  } else if (!e.shiftKey && active === last) {
    e.preventDefault()
    first.focus()
  }
}

onMounted(() => {
  previouslyFocused = document.activeElement as HTMLElement | null
  panelEl.value?.focus()
})
onBeforeUnmount(() => { previouslyFocused?.focus() })
</script>

<template>
  <div class="panel-backdrop" @click.self="emit('close')" @keydown="onKeydown">
    <div
      class="panel" role="dialog" aria-modal="true"
      :aria-labelledby="labelledBy" :aria-label="label" ref="panelEl" tabindex="-1"
    >
      <button class="panel-close icon-btn plain" aria-label="Close panel" @click="emit('close')">
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2" stroke-linecap="round" /></svg>
      </button>
      <slot />
    </div>
  </div>
</template>

<style scoped>
.panel-backdrop {
  position: fixed; inset: 0; background: rgba(15, 23, 42, .45);
  display: flex; justify-content: flex-end; z-index: 900;
}
.panel {
  position: relative; background: var(--surface); width: 100%; max-width: 880px;
  height: 100%; box-shadow: var(--shadow-lg); display: flex; overflow: hidden;
}
.panel:focus { outline: none; }
.panel-close { position: absolute; top: 12px; right: 12px; z-index: 5; }
@media (max-width: 900px) { .panel { max-width: 100%; } }
</style>
