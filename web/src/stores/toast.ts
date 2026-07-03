import { defineStore } from 'pinia'

export interface Toast {
  id: number
  message: string
  kind: 'info' | 'success' | 'error'
}

let nextId = 1

export const useToastStore = defineStore('toast', {
  state: () => ({ toasts: [] as Toast[] }),
  actions: {
    push(message: string, kind: Toast['kind'] = 'info') {
      const id = nextId++
      this.toasts.push({ id, message, kind })
      setTimeout(() => this.dismiss(id), kind === 'error' ? 6000 : 3500)
    },
    success(message: string) { this.push(message, 'success') },
    error(message: string) { this.push(message, 'error') },
    dismiss(id: number) { this.toasts = this.toasts.filter((t) => t.id !== id) },
  },
})
