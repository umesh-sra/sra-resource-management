import type { ProjectStatus, ResourceStatus } from '@/types'

export const fmtDate = (iso?: string): string =>
  iso ? new Date(iso).toLocaleDateString('en-AU', { day: '2-digit', month: 'short', year: 'numeric' }) : '—'

export const fmtMoney = (n?: number): string =>
  n == null ? '—' : new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', maximumFractionDigits: 0 }).format(n)

export const fmtPercent = (fraction: number): string => `${Math.round(fraction * 100)}%`

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

export const initials = (name: string): string =>
  name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('')
