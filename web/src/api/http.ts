import axios, { AxiosError } from 'axios'
import type { ProblemDetails } from '@/types'

/**
 * Axios instance pointed at the business API. In Development the API authorises
 * every request (Dev auth), so no bearer token is attached here yet — a request
 * interceptor that injects an Entra access token is the production wiring point.
 */
export const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE ?? 'http://localhost:5163/v1',
  headers: { 'Content-Type': 'application/json' },
})

/** A normalised error surfaced to the UI. */
export class ApiError extends Error {
  status: number
  problem?: ProblemDetails
  constructor(message: string, status: number, problem?: ProblemDetails) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

http.interceptors.response.use(
  (res) => res,
  (error: AxiosError<ProblemDetails>) => {
    const problem = error.response?.data
    const status = error.response?.status ?? 0
    const message =
      problem?.detail ||
      problem?.title ||
      error.message ||
      'Request failed'
    return Promise.reject(new ApiError(message, status, problem))
  },
)
