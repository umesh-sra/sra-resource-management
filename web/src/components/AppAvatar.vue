<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { assetUrl, initials } from '@/lib/format'

const props = withDefaults(
  defineProps<{
    name: string
    imageUrl?: string | null
    /** Rendered box in px; the font scales with it. */
    size?: number
    /** Square (rounded-corner) tile instead of a circle — used by the people grid. */
    square?: boolean
    /**
     * Tinted background with dark text rather than a saturated fill. Large
     * placeholder tiles read as colour blocks at full saturation; the reference
     * keeps its photo-less tiles quiet (screens/people and resources.png).
     */
    soft?: boolean
  }>(),
  { size: 32, square: false, soft: false },
)

// A failed image must fall back to initials rather than leaving a broken tile.
const failed = ref(false)
watch(() => props.imageUrl, () => { failed.value = false })

const src = computed(() => (props.imageUrl && !failed.value ? assetUrl(props.imageUrl) : null))

/**
 * Deterministic tint per person so the same face keeps the same colour across
 * screens. Hues are drawn from the SRA ramp rather than a rainbow palette.
 */
const TINTS = ['#0b3b73', '#1657a0', '#2f6f57', '#7a4b8f', '#a0522d', '#3f7cc4', '#8a5700', '#155e75']
const SOFT_TINTS = ['#e1ebf5', '#e6eef8', '#e4f0ea', '#efe7f3', '#f4eae2', '#e9f1fa', '#f6efe0', '#e3eff2']
const index = computed(() => {
  let h = 0
  for (const ch of props.name) h = (h * 31 + ch.charCodeAt(0)) >>> 0
  return h % TINTS.length
})
const background = computed(() => (props.soft ? SOFT_TINTS[index.value] : TINTS[index.value]))
const foreground = computed(() => (props.soft ? TINTS[index.value] : '#fff'))
</script>

<template>
  <img
    v-if="src" class="av" :src="src" :alt="''" aria-hidden="true" loading="lazy"
    :style="{ width: `${size}px`, height: `${size}px`, borderRadius: square ? '8px' : '50%' }"
    @error="failed = true"
  />
  <span
    v-else class="av" aria-hidden="true"
    :style="{
      width: `${size}px`, height: `${size}px`, background, color: foreground,
      fontSize: `${Math.max(9, Math.round(size * 0.38))}px`,
      borderRadius: square ? '8px' : '50%',
    }"
  >{{ initials(name) }}</span>
</template>
