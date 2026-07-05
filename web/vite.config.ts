import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
// Security headers for the dev/preview servers. No CSP here — a strict CSP
// breaks Vite's HMR client; the production CSP is set by the hosting layer
// (see README "Production hosting headers").
const securityHeaders = {
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'no-referrer',
}

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    // The API trusts http://localhost:5173 via CORS; the SPA calls it directly.
    headers: securityHeaders,
  },
  preview: {
    headers: securityHeaders,
  },
})
