import { onBeforeUnmount, onMounted } from 'vue'

/**
 * Stops the page behind an open overlay from scrolling.
 *
 * Without this, a wheel gesture over a dialog whose body does not itself need
 * to scroll bleeds through and moves the page underneath, so closing the
 * overlay leaves the user somewhere else entirely.
 *
 * Overlays nest (the person drawer opens the edit dialog), so locks are counted
 * and only the last one to release restores the original overflow. The
 * scrollbar's width is replaced with padding so the page does not jump sideways
 * as it disappears.
 */
let depth = 0
let previousOverflow = ''
let previousPaddingRight = ''

export function useScrollLock(): void {
  onMounted(() => {
    if (depth++ === 0) {
      const { style } = document.body
      previousOverflow = style.overflow
      previousPaddingRight = style.paddingRight
      const gap = window.innerWidth - document.documentElement.clientWidth
      style.overflow = 'hidden'
      if (gap > 0) style.paddingRight = `${gap}px`
    }
  })

  onBeforeUnmount(() => {
    if (--depth === 0) {
      document.body.style.overflow = previousOverflow
      document.body.style.paddingRight = previousPaddingRight
    }
    // Guard against an unbalanced release leaving the count negative.
    if (depth < 0) depth = 0
  })
}
