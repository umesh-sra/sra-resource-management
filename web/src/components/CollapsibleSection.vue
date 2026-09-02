<script setup lang="ts">
import { ref, useId } from 'vue'

/**
 * Disclosure block used in the person panel and the create dialogs
 * (screens/person_overview.png, screens/new_person_part2.png). When collapsed
 * it shows `summary` — the italic list of fields hiding inside.
 */
const props = withDefaults(
  defineProps<{ title: string; summary?: string; open?: boolean }>(),
  { open: false },
)

const expanded = ref(props.open)
const bodyId = useId()
</script>

<template>
  <div class="section">
    <button
      class="section-head" type="button"
      :aria-expanded="expanded" :aria-controls="bodyId"
      @click="expanded = !expanded"
    >
      <h3>{{ title }}</h3>
      <svg class="chev" viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
        <path d="M6 9l6 6 6-6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
    </button>
    <p v-if="!expanded && summary" class="section-summary">{{ summary }}</p>
    <div v-show="expanded" :id="bodyId" class="section-body">
      <slot />
    </div>
  </div>
</template>
