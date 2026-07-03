import { http } from './http'
import type {
  Allocation, Client, ClientDetail, DashboardSummary, Page, Project, ProjectDetail,
  ReferenceItem, Resource, ResourceDetail, UtilisationReport,
} from '@/types'

// ---- Clients ----
export const clientsApi = {
  list: (params: { q?: string; page?: number; pageSize?: number; sort?: string } = {}) =>
    http.get<Page<Client>>('/clients', { params }).then((r) => r.data),
  get: (id: string) => http.get<ClientDetail>(`/clients/${id}`).then((r) => r.data),
  create: (body: { name: string }) => http.post<Client>('/clients', body).then((r) => r.data),
  update: (id: string, body: { name: string }) =>
    http.put<Client>(`/clients/${id}`, body).then((r) => r.data),
  remove: (id: string, cascade = false) =>
    http.delete(`/clients/${id}`, { params: { cascade } }),
}

// ---- Projects ----
export interface ProjectFilters {
  q?: string; clientId?: string; billable?: boolean; status?: string
  startsAfter?: string; endsBefore?: string; page?: number; pageSize?: number; sort?: string
}
export const projectsApi = {
  list: (params: ProjectFilters = {}) =>
    http.get<Page<Project>>('/projects', { params }).then((r) => r.data),
  get: (id: string) => http.get<ProjectDetail>(`/projects/${id}`).then((r) => r.data),
  create: (body: Partial<Project>) => http.post<Project>('/projects', body).then((r) => r.data),
  update: (id: string, body: Partial<Project>) =>
    http.put<Project>(`/projects/${id}`, body).then((r) => r.data),
  remove: (id: string, cascade = false) =>
    http.delete(`/projects/${id}`, { params: { cascade } }),
  createAllocation: (projectId: string, body: Partial<Allocation>) =>
    http.post<Allocation>(`/projects/${projectId}/allocations`, body).then((r) => r.data),
}

// ---- Resources ----
export interface ResourceFilters {
  q?: string; skill?: string[]; department?: string; location?: string; status?: string
  page?: number; pageSize?: number; sort?: string
}
export const resourcesApi = {
  list: (params: ResourceFilters = {}) =>
    http.get<Page<Resource>>('/resources', { params, paramsSerializer: { indexes: null } })
      .then((r) => r.data),
  get: (id: string) => http.get<ResourceDetail>(`/resources/${id}`).then((r) => r.data),
  create: (body: Partial<Resource>) => http.post<Resource>('/resources', body).then((r) => r.data),
  update: (id: string, body: Partial<Resource>) =>
    http.put<Resource>(`/resources/${id}`, body).then((r) => r.data),
  remove: (id: string, cascade = false) =>
    http.delete(`/resources/${id}`, { params: { cascade } }),
}

// ---- Allocations ----
export const allocationsApi = {
  list: (params: { projectId?: string; resourceId?: string; from?: string; to?: string; page?: number; pageSize?: number } = {}) =>
    http.get<Page<Allocation>>('/allocations', { params }).then((r) => r.data),
  create: (body: Partial<Allocation>) => http.post<Allocation>('/allocations', body).then((r) => r.data),
  update: (id: string, body: Partial<Allocation>) =>
    http.put<Allocation>(`/allocations/${id}`, body).then((r) => r.data),
  remove: (id: string) => http.delete(`/allocations/${id}`),
}

// ---- Dashboard ----
export const dashboardApi = {
  summary: (params: { from?: string; to?: string } = {}) =>
    http.get<DashboardSummary>('/dashboard/summary', { params }).then((r) => r.data),
}

// ---- Reports ----
export const reportsApi = {
  utilisation: (params: { from: string; to: string; department?: string }) =>
    http.get<UtilisationReport>('/reports/utilisation', { params }).then((r) => r.data),
  utilisationCsvUrl: (params: { from: string; to: string }) =>
    `${import.meta.env.VITE_API_BASE}/reports/utilisation?from=${params.from}&to=${params.to}&format=csv`,
}

// ---- Reference data ----
export const referenceApi = {
  list: (collection: string) =>
    http.get<ReferenceItem[]>(`/reference/${collection}`).then((r) => r.data),
  create: (collection: string, value: string) =>
    http.post<ReferenceItem>(`/reference/${collection}`, { value }).then((r) => r.data),
}
