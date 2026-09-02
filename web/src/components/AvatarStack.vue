<script setup lang="ts">
import { computed } from 'vue'
import type { ResourceSummary } from '@/types'
import AppAvatar from './AppAvatar.vue'

const props = withDefaults(
  defineProps<{ people: ResourceSummary[]; max?: number; size?: number }>(),
  { max: 5, size: 26 },
)

const shown = computed(() => props.people.slice(0, props.max))
const overflow = computed(() => Math.max(0, props.people.length - props.max))
/** Names go on the group, not each tile, so screen readers read the team once. */
const label = computed(() => props.people.map((p) => p.name).join(', '))
</script>

<template>
  <span v-if="people.length" class="av-stack" :title="label">
    <AppAvatar v-for="p in shown" :key="p.id" :name="p.name" :image-url="p.imageUrl" :size="size" />
    <span v-if="overflow" class="av-more">+{{ overflow }}</span>
    <span class="sr-only">Team: {{ label }}</span>
  </span>
  <span v-else class="muted">—</span>
</template>
