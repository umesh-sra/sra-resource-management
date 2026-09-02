import type { MilestoneStatus, ProjectStatus, ResourceStatus, TimeOffType, Weekday } from '@/types'

export const fmtDate = (iso?: string): string =>
  iso ? new Date(iso).toLocaleDateString('en-AU', { day: '2-digit', month: 'short', year: 'numeric' }) : '—'

/** Compact form used in dense tables and timeline headers: "4 Jul 2026". */
export const fmtDateShort = (iso?: string): string =>
  iso ? new Date(iso).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' }) : '—'

export const fmtMoney = (n?: number): string =>
  n == null ? '—' : new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', maximumFractionDigits: 0 }).format(n)

export const fmtPercent = (fraction: number): string => `${Math.round(fraction * 100)}%`

/**
 * Hours rendered the way the reference reports do: "1,905h 47m".
 */
export const fmtHours = (hours: number): string => {
  const whole = Math.floor(Math.abs(hours))
  const mins = Math.round((Math.abs(hours) - whole) * 60)
  const sign = hours < 0 ? '-' : ''
  return mins ? `${sign}${whole.toLocaleString()}h ${mins}m` : `${sign}${whole.toLocaleString()}h`
}

const PROJECT_STATUS: Record<ProjectStatus, { label: string; class: string }> = {
  planned: { label: 'Planned', class: 'gray' },
  active: { label: 'Active', class: 'green' },
  onHold: { label: 'On hold', class: 'amber' },
  completed: { label: 'Completed', class: 'blue' },
  cancelled: { label: 'Cancelled', class: 'red' },
}
export const projectStatus = (s: ProjectStatus) => PROJECT_STATUS[s] ?? { label: s, class: 'gray' }

const RESOURCE_STATUS: Record<ResourceStatus, { label: string; class: string }> = {
  active: { label: 'Active', class: 'green' },
  inactive: { label: 'Inactive', class: 'gray' },
  onLeave: { label: 'On leave', class: 'amber' },
}
export const resourceStatus = (s: ResourceStatus) => RESOURCE_STATUS[s] ?? { label: s, class: 'gray' }

const TIME_OFF_LABELS: Record<TimeOffType, string> = {
  annualLeave: 'Annual leave',
  personal: 'Personal',
  sick: 'Sick',
  publicHoliday: 'Public holiday',
  other: 'Other',
}
export const timeOffLabel = (t: TimeOffType): string => TIME_OFF_LABELS[t] ?? t

const MILESTONE: Record<MilestoneStatus, { label: string; class: string }> = {
  pending: { label: 'Pending', class: 'gray' },
  met: { label: 'Met', class: 'green' },
  missed: { label: 'Missed', class: 'red' },
}
export const milestoneLabel = (m: MilestoneStatus): string => MILESTONE[m]?.label ?? m
export const milestoneBadge = (m: MilestoneStatus): string => MILESTONE[m]?.class ?? 'gray'

export const initials = (name: string): string =>
  name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('')

/**
 * Turns an API-relative path (e.g. the `/v1/resources/{id}/image` that the API
 * stores in `imageUrl`) into something the browser can fetch, by hanging it off
 * the origin of `VITE_API_BASE`.
 */
export function assetUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) return path
  const base = import.meta.env.VITE_API_BASE ?? 'http://localhost:5163/v1'
  try {
    return new URL(path, new URL(base).origin).toString()
  } catch {
    return path
  }
}

// ---------------------------------------------------------------------------
// Date helpers for the timeline views.
//
// Everything here works in local time on purpose: `toISOString()` converts to
// UTC, which shifts local midnight into the previous day in UTC+ zones such as
// Australia/Sydney and makes every date off by one.
// ---------------------------------------------------------------------------

/** Local-time `yyyy-MM-dd`. */
export const isoDate = (d: Date): string =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

/** Parses `yyyy-MM-dd` as local midnight (`new Date('...')` would parse as UTC). */
export function parseDate(iso: string): Date {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
  return new Date(y!, (m ?? 1) - 1, d ?? 1)
}

export function addDays(d: Date, n: number): Date {
  const out = new Date(d)
  out.setDate(out.getDate() + n)
  return out
}

/** Whole days from `a` to `b`, ignoring any time component. */
export function daysBetween(a: Date, b: Date): number {
  const ms = new Date(b.getFullYear(), b.getMonth(), b.getDate()).getTime()
    - new Date(a.getFullYear(), a.getMonth(), a.getDate()).getTime()
  return Math.round(ms / 86_400_000)
}

/** Monday of the week containing `d`. */
export function startOfWeek(d: Date): Date {
  const out = new Date(d)
  out.setDate(out.getDate() - ((out.getDay() + 6) % 7))
  out.setHours(0, 0, 0, 0)
  return out
}

/** ISO-8601 week number, matching the "W37" markers in the reference timeline. */
export function isoWeek(d: Date): number {
  const t = new Date(d.getFullYear(), d.getMonth(), d.getDate())
  t.setDate(t.getDate() + 3 - ((t.getDay() + 6) % 7))
  const week1 = new Date(t.getFullYear(), 0, 4)
  return 1 + Math.round(
    ((t.getTime() - week1.getTime()) / 86_400_000 - 3 + ((week1.getDay() + 6) % 7)) / 7,
  )
}

export const isWeekend = (d: Date): boolean => d.getDay() === 0 || d.getDay() === 6

export const isSameDay = (a: Date, b: Date): boolean =>
  a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()

/**
 * Effort rendered as the reference does on a timeline bar: "8h per day".
 *
 * `workingDaysPerWeek` is the person's own pattern, not a flat five: someone on
 * a three-day week doing 24h/week works 8h on each day they work, and showing
 * 4.8h would contradict what was entered in the booking dialog.
 */
export function effortPerDay(
  effort: number,
  unit: 'hoursPerWeek' | 'percent',
  availability = 38,
  workingDaysPerWeek = 5,
): string {
  const weekly = unit === 'percent' ? (effort / 100) * availability : effort
  const perDay = weekly / (workingDaysPerWeek || 5)
  if (perDay >= 1) return `${Math.round(perDay * 10) / 10}h per day`
  return `${Math.round(perDay * 60)}m per day`
}

/** `Date.getDay()` order, so a JS date maps straight onto a resource's `workingDays`. */
const WEEKDAY_BY_INDEX: Weekday[] =
  ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday']

const DEFAULT_WORKING_DAYS: Weekday[] = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday']

/**
 * Working days in the inclusive range `from`–`to` for a person's pattern, used
 * to price a booking or a block of leave in days as well as hours. Falls back
 * to Mon–Fri when the resource has no pattern recorded.
 */
export function countWorkingDays(from: string, to: string, workingDays?: Weekday[]): number {
  if (!from || !to || to < from) return 0
  const pattern = new Set(workingDays?.length ? workingDays : DEFAULT_WORKING_DAYS)
  const end = parseDate(to)
  let n = 0
  // Guard against a pathological range dragging the UI to a halt.
  for (let d = parseDate(from), i = 0; d <= end && i < 3660; d = addDays(d, 1), i++) {
    if (pattern.has(WEEKDAY_BY_INDEX[d.getDay()]!)) n++
  }
  return n
}
