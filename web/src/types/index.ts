// TypeScript mirrors of the OpenAPI/API DTOs (docs/openapi.yaml).

export type ProjectStatus = 'planned' | 'active' | 'onHold' | 'completed' | 'cancelled'
export type ResourceStatus = 'active' | 'inactive' | 'onLeave'
export type EffortUnit = 'hoursPerWeek' | 'percent'
export type BookableStatus = 'bookable' | 'nonBookable'
export type TimeOffType = 'annualLeave' | 'personal' | 'sick' | 'publicHoliday' | 'other'
export type MilestoneStatus = 'pending' | 'met' | 'missed'
export type ProjectBudgetType = 'none' | 'fee' | 'hours'
export type Weekday =
  | 'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday'

export interface PageMeta {
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export interface Page<T> {
  items: T[]
  meta: PageMeta
}

export interface Client {
  id: string
  name: string
  projectCount: number
  createdAt: string
  updatedAt: string
}

export interface ClientDetail extends Client {
  projects: Project[]
  team: ResourceSummary[]
}

export interface Project {
  id: string
  clientId: string
  clientName?: string
  name: string
  code: string
  startDate: string
  endDate: string
  budget?: number
  remaining?: number
  billable: boolean
  status: ProjectStatus
  /** V002 — which budget applies; 'fee' uses `budget`, 'hours' uses `budgetHours`. */
  budgetType: ProjectBudgetType
  budgetHours?: number
  remainingHours?: number
  activityTypes: string[]
  details?: string
  colour?: string
  /** Distinct people allocated to the project (list + detail responses). */
  team: ResourceSummary[]
  createdAt: string
  updatedAt: string
}

export interface ProjectDetail extends Project {
  allocations: Allocation[]
  phases: ProjectPhase[]
  milestones: ProjectMilestone[]
}

export interface ProjectPhase {
  id: string
  projectId: string
  name: string
  startDate: string
  endDate: string
  colour?: string
  sortOrder: number
  createdAt: string
  updatedAt: string
}

export interface ProjectMilestone {
  id: string
  projectId: string
  name: string
  dueDate: string
  status: MilestoneStatus
  note?: string
  createdAt: string
  updatedAt: string
}

export interface Resource {
  id: string
  name: string
  email: string
  primaryJobTitle: string
  secondaryJobTitle?: string
  status: ResourceStatus
  department?: string
  location?: string
  notes?: string
  skills: string[]
  imageUrl?: string
  availabilityHoursPerWeek: number
  workingDays: Weekday[]
  /** V002 — the person panel's Overview / Extra Details / Scheduling / Financial groups. */
  jobRole?: string
  managerId?: string
  /** Resolved on the detail endpoint only; list responses omit it. */
  managerName?: string
  phone?: string
  secondarySkills: string[]
  securityClearances: string[]
  securityNpcObtainedOn?: string
  certifications: string[]
  timeZone?: string
  bookableStatus: BookableStatus
  publicHolidayCalendar?: string
  defaultRateHourly?: number
  colour?: string
  createdAt: string
  updatedAt: string
}

export interface ResourceSummary {
  id: string
  name: string
  primaryJobTitle?: string
  imageUrl?: string
}

export interface ResourceDetail extends Resource {
  allocations: Allocation[]
  allocatedHoursPerWeek: number
  timeOff: TimeOff[]
}

export interface TimeOff {
  id: string
  resourceId: string
  resourceName?: string
  startDate: string
  endDate: string
  type: TimeOffType
  /** Omitted means the whole working day. */
  hoursPerDay?: number
  /** Surfaced as "Details" on the time-off dialog. */
  note?: string
  /** V003 — the person the leave was arranged by. */
  bookerId?: string
  /** Resolved server-side from `bookerId`; read-only. */
  bookerName?: string
  createdAt: string
  updatedAt: string
}

export interface Allocation {
  id: string
  projectId: string
  projectName?: string
  resourceId: string
  resourceName?: string
  startDate: string
  endDate: string
  effort: number
  effortUnit: EffortUnit
  roleOnProject?: string
  billable: boolean
  /** V002 — per-person billable rate (project Team tab). */
  hourlyRate?: number
  /** V003 — free text from the booking dialog's Details box. */
  details?: string
  /** V003 — the person the booking was arranged by. */
  bookerId?: string
  /** Resolved server-side from `bookerId`; read-only. */
  bookerName?: string
  warnings: string[]
  createdAt: string
  updatedAt: string
}

export interface DashboardSummary {
  /**
   * Business date the figures were computed for, in the organisation's time zone.
   * Prefer this over `new Date()` so the SPA and API agree about "today" — they
   * disagreed for the first ~10 hours of each Australian day when the API
   * derived its date from UTC.
   */
  today: string
  /** IANA name of the business time zone. */
  timeZone: string
  activeProjects: number
  totalResources: number
  averageUtilisation: number
  overAllocatedResources: number
  underAllocatedResources: number
  budgetAtRisk: number
  upcomingProjectStarts: Project[]
  upcomingRollOffs: Allocation[]
}

export type GanttView = 'projects' | 'resources'

export interface GanttBar {
  refId?: string
  label?: string
  start: string
  end: string
  effort?: number
  effortUnit?: EffortUnit
  overAllocated?: boolean
  /**
   * Client-side annotation, not part of the API response: the Schedule merges
   * time-off records in as bars so leave sits on the same row as allocations.
   */
  kind?: 'allocation' | 'timeOff'
}

export interface GanttRow {
  id: string
  label: string
  bars: GanttBar[]
}

export interface GanttResponse {
  view: GanttView
  from: string
  to: string
  rows: GanttRow[]
}

export interface UtilisationRow {
  resourceId: string
  resourceName: string
  department?: string
  /** Gross availability over the window. */
  availableHours: number
  allocatedHours: number
  /**
   * allocatedHours / availableHours — measured against *gross* availability, so
   * the ratio is unchanged by V002. Effective capacity is reported separately.
   */
  utilisation: number
  /** V002 — working-day hours lost to leave in the window. */
  timeOffHours: number
  /** V002 — availableHours minus timeOffHours (FR-REP-6). */
  effectiveCapacityHours: number
}

export interface UtilisationReport {
  from: string
  to: string
  rows: UtilisationRow[]
}

export interface ReferenceItem {
  id: string
  value: string
  active: boolean
}

/** RFC 9457 problem details returned by the API on errors. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
