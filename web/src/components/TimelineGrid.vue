<script setup lang="ts">
import { computed } from 'vue'
import type { GanttBar, GanttRow } from '@/types'
import { addDays, daysBetween, isSameDay, isWeekend, isoWeek, parseDate } from '@/lib/format'

/**
 * Horizontal day-grid timeline shared by the Schedule (people) and Gantt
 * (projects) screens — see screens/schedule.png. A sticky left column carries
 * the row label; the scrolling right pane is a fixed-width day grid with bars
 * positioned over it.
 */
const props = withDefaults(
  defineProps<{
    rows: GanttRow[]
    /** Window bounds, `yyyy-MM-dd`. */
    from: string
    to: string
    /** Column width in px — the zoom level. */
    dayWidth?: number
    labelWidth?: number
    loading?: boolean
    emptyText?: string
  }>(),
  { dayWidth: 34, labelWidth: 236, loading: false, emptyText: 'Nothing scheduled in this window.' },
)

const emit = defineEmits<{ barClick: [bar: GanttBar, row: GanttRow] }>()

const start = computed(() => parseDate(props.from))
const days = computed(() => {
  const n = daysBetween(start.value, parseDate(props.to)) + 1
  return Array.from({ length: Math.max(n, 1) }, (_, i) => addDays(start.value, i))
})
const gridWidth = computed(() => days.value.length * props.dayWidth)
const today = new Date()

/**
 * Month header segments — one cell per month spanned by the window. A segment
 * only carries its label if it is wide enough to show it; the window's first
 * and last months are usually partial, and a clipped "Sept 2026" reads as noise.
 */
const MONTH_LABEL_MIN_PX = 62
const months = computed(() => {
  const out: { key: string; label: string; width: number }[] = []
  for (const d of days.value) {
    const key = `${d.getFullYear()}-${d.getMonth()}`
    const last = out[out.length - 1]
    if (last?.key === key) last.width += props.dayWidth
    else out.push({ key, label: d.toLocaleDateString('en-AU', { month: 'short', year: 'numeric' }), width: props.dayWidth })
  }
  return out.map((m) => ({ ...m, label: m.width >= MONTH_LABEL_MIN_PX ? m.label : '' }))
})

/** Day columns are only labelled once they are wide enough to read. */
const showDayNumbers = computed(() => props.dayWidth >= 18)
const showWeekdays = computed(() => props.dayWidth >= 28)

const todayOffset = computed(() => {
  const i = daysBetween(start.value, today)
  return i >= 0 && i < days.value.length ? i * props.dayWidth : null
})

const BAR_H = 34
const BAR_GAP = 4

/**
 * Packs a row's bars into lanes so overlapping date ranges stack instead of
 * covering each other — the row's height then follows its busiest overlap.
 */
function laidOut(row: GanttRow) {
  const sorted = [...row.bars].sort((a, b) => a.start.localeCompare(b.start))
  const laneEnds: number[] = []
  return sorted.map((bar) => {
    const s = Math.max(0, daysBetween(start.value, parseDate(bar.start)))
    const e = Math.min(days.value.length - 1, daysBetween(start.value, parseDate(bar.end)))
    let lane = laneEnds.findIndex((end) => end < s)
    if (lane === -1) { lane = laneEnds.length; laneEnds.push(e) } else { laneEnds[lane] = e }
    return {
      bar,
      lane,
      left: s * props.dayWidth,
      width: Math.max((e - s + 1) * props.dayWidth - 2, 6),
      clipped: e < s,
    }
  }).filter((b) => !b.clipped)
}

const layouts = computed(() =>
  props.rows.map((row) => {
    const bars = laidOut(row)
    const lanes = bars.reduce((m, b) => Math.max(m, b.lane + 1), 0)
    return { row, bars, height: Math.max(lanes, 1) * (BAR_H + BAR_GAP) + BAR_GAP + 12 }
  }),
)
</script>

<template>
  <div class="tl">
    <div class="tl-scroll">
      <!-- Header ------------------------------------------------------- -->
      <div class="tl-head" :style="{ width: `${labelWidth + gridWidth}px` }">
        <div class="tl-head-label" :style="{ width: `${labelWidth}px` }">
          <slot name="label-head" />
        </div>
        <div class="tl-head-grid" :style="{ width: `${gridWidth}px` }">
          <div class="tl-months">
            <div v-for="m in months" :key="m.key" class="tl-month" :style="{ width: `${m.width}px` }">{{ m.label }}</div>
          </div>
          <div class="tl-days">
            <div
              v-for="(d, i) in days" :key="i" class="tl-day"
              :class="{ weekend: isWeekend(d), today: isSameDay(d, today), 'week-start': d.getDay() === 1 }"
              :style="{ width: `${dayWidth}px` }"
            >
              <span v-if="d.getDay() === 1 && dayWidth >= 24" class="tl-week">W{{ isoWeek(d) }}</span>
              <span v-if="showWeekdays" class="tl-dow">{{ d.toLocaleDateString('en-AU', { weekday: 'short' }).slice(0, 2) }}</span>
              <span v-if="showDayNumbers" class="tl-dom">{{ d.getDate() }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Body --------------------------------------------------------- -->
      <div v-if="loading" class="tl-state"><span role="status">Loading timeline…</span></div>
      <div v-else-if="!rows.length" class="tl-state">{{ emptyText }}</div>
      <div v-else class="tl-body" :style="{ width: `${labelWidth + gridWidth}px` }">
        <div v-for="l in layouts" :key="l.row.id" class="tl-row" :style="{ height: `${l.height}px` }">
          <div class="tl-row-label" :style="{ width: `${labelWidth}px` }">
            <slot name="row-label" :row="l.row">
              <strong>{{ l.row.label }}</strong>
            </slot>
          </div>
          <div class="tl-row-grid" :style="{ width: `${gridWidth}px` }">
            <div class="tl-cols" aria-hidden="true">
              <div
                v-for="(d, i) in days" :key="i" class="tl-col"
                :class="{ weekend: isWeekend(d), today: isSameDay(d, today), 'week-start': d.getDay() === 1 }"
                :style="{ width: `${dayWidth}px` }"
              />
            </div>
            <button
              v-for="(b, i) in l.bars" :key="i" type="button" class="tl-bar"
              :class="{ over: b.bar.overAllocated }"
              :style="{ left: `${b.left}px`, width: `${b.width}px`, top: `${BAR_GAP + b.lane * (BAR_H + BAR_GAP)}px`, height: `${BAR_H}px` }"
              @click="emit('barClick', b.bar, l.row)"
            >
              <span class="tl-bar-text">
                <slot name="bar" :bar="b.bar" :row="l.row">{{ b.bar.label }}</slot>
              </span>
            </button>
          </div>
        </div>
        <div v-if="todayOffset !== null" class="tl-today-line" :style="{ left: `${labelWidth + todayOffset}px` }" aria-hidden="true" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.tl { border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface); overflow: hidden; }
.tl-scroll { overflow: auto; max-height: calc(100vh - 150px); position: relative; }

.tl-head { display: flex; position: sticky; top: 0; z-index: 6; background: var(--surface); border-bottom: 1px solid var(--border); }
.tl-head-label { flex-shrink: 0; position: sticky; left: 0; z-index: 7; background: var(--surface); border-right: 1px solid var(--border); display: flex; align-items: flex-end; padding: 6px 14px 8px; }
.tl-head-grid { flex-shrink: 0; }
.tl-months { display: flex; border-bottom: 1px solid var(--gray-100); }
.tl-month { padding: 6px 10px; font-weight: 650; color: var(--gray-900); font-size: 13px; white-space: nowrap; overflow: hidden; border-left: 1px solid var(--gray-100); }
.tl-days { display: flex; }
.tl-day {
  flex-shrink: 0; text-align: center; padding: 4px 0 6px; font-size: 11px; color: var(--text-muted);
  position: relative; line-height: 1.25;
}
.tl-day.weekend { background: var(--gray-50); }
.tl-day.week-start { border-left: 1px solid var(--gray-200); }
.tl-day.today { background: var(--accent-50); }
.tl-day.today .tl-dom { background: var(--accent); color: #fff; border-radius: 50%; display: inline-block; width: 20px; height: 20px; line-height: 20px; }
.tl-dow { display: block; }
.tl-dom { display: block; font-weight: 650; color: var(--gray-800); font-size: 12px; }
.tl-week { position: absolute; top: -14px; left: 3px; font-size: 9.5px; color: var(--gray-400); white-space: nowrap; }

.tl-body { position: relative; }
.tl-row { display: flex; border-bottom: 1px solid var(--gray-100); }
.tl-row-label {
  flex-shrink: 0; position: sticky; left: 0; z-index: 4; background: var(--surface);
  border-right: 1px solid var(--border); padding: 12px 14px; display: flex; align-items: flex-start;
}
.tl-row:hover .tl-row-label { background: var(--gray-50); }
.tl-row-grid { flex-shrink: 0; position: relative; }
.tl-cols { position: absolute; inset: 0; display: flex; }
.tl-col { flex-shrink: 0; }
.tl-col.weekend { background: var(--gray-50); }
.tl-col.week-start { border-left: 1px solid var(--gray-200); }
.tl-col.today { background: var(--accent-50); }

.tl-bar {
  position: absolute; border: 1px solid var(--brand-200); background: var(--brand-50); color: var(--brand-800);
  border-radius: 6px; padding: 4px 8px; text-align: left; font: inherit; font-size: 12px; cursor: pointer;
  overflow: hidden; box-shadow: var(--shadow-sm);
}
.tl-bar:hover { background: var(--brand-100); border-color: var(--brand-400); }
.tl-bar.over { background: var(--red-50); border-color: #f0b4b4; color: var(--red-700); }
.tl-bar.over:hover { background: #fbe0e0; }
.tl-bar-text { display: block; overflow: hidden; }

.tl-today-line { position: absolute; top: 0; bottom: 0; width: 2px; background: var(--accent); opacity: .5; pointer-events: none; z-index: 3; }
.tl-state { padding: 48px 20px; text-align: center; color: var(--text-muted); position: sticky; left: 0; }
</style>
