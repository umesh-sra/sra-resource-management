// TypeScript mirrors of the OpenAPI/API DTOs (docs/openapi.yaml).

export type ProjectStatus = 'planned' | 'active' | 'onHold' | 'completed' | 'cancelled'
export type ResourceStatus = 'active' | 'inactive' | 'onLeave'
export type EffortUnit = 'hoursPerWeek' | 'percent'
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
  createdAt: string
  updatedAt: string
}

export interface ProjectDetail extends Project {
  allocations: Allocation[]
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
  warnings: string[]
  createdAt: string
  updatedAt: string
}

export interface DashboardSummary {
  activeProjects: number
  totalResources: number
  averageUtilisation: number
  overAllocatedResources: number
  underAllocatedResources: number
  budgetAtRisk: number
  upcomingProjectStarts: Project[]
  upcomingRollOffs: Allocation[]
}

export interface UtilisationRow {
  resourceId: string
  resourceName: string
  department?: string
  availableHours: number
  allocatedHours: number
  utilisation: number
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
