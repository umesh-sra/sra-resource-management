<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterView, useRoute } from 'vue-router'
import ToastHost from '@/components/ToastHost.vue'

const route = useRoute()
const pageTitle = computed(() => (route.meta.title as string) ?? 'SRA-RMS')

// After client-side navigation, move focus to the main landmark so keyboard
// and screen-reader users land on the new page content (WCAG 2.4.3).
const mainEl = ref<HTMLElement | null>(null)
watch(
  () => route.path,
  () => { mainEl.value?.focus() },
)

const nav = [
  { to: '/dashboard', label: 'Dashboard', icon: 'M3 13h8V3H3v10Zm0 8h8v-6H3v6Zm10 0h8V11h-8v10Zm0-18v6h8V3h-8Z' },
  { to: '/clients', label: 'Clients', icon: 'M12 12a5 5 0 1 0-5-5 5 5 0 0 0 5 5Zm0 2c-4 0-9 2-9 6v2h18v-2c0-4-5-6-9-6Z' },
  { to: '/projects', label: 'Projects', icon: 'M3 7h18v12H3V7Zm6-3h6v3H9V4Z' },
  { to: '/resources', label: 'Resources', icon: 'M16 11a4 4 0 1 0-4-4 4 4 0 0 0 4 4Zm-8 1a3 3 0 1 0-3-3 3 3 0 0 0 3 3Zm0 2c-2.7 0-6 1.3-6 4v2h7v-2c0-1.4.6-2.6 1.6-3.5A9.6 9.6 0 0 0 8 14Zm8 0c-3 0-7 1.5-7 4v2h14v-2c0-2.5-4-4-7-4Z' },
  { to: '/allocations', label: 'Allocations', icon: 'M4 5h16v3H4V5Zm2 6h12v3H6v-3Zm-2 6h16v3H4v-3Z' },
  { to: '/reports', label: 'Reports', icon: 'M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Zm2 12h2v3H7v-3Zm4-6h2v9h-2V9Zm4 3h2v6h-2v-6Z' },
]
</script>

<template>
  <div class="layout">
    <a href="#main" class="skip-link">Skip to main content</a>
    <aside class="sidebar">
      <div class="brand">
        <img src="/sra-logo-white.png" alt="SRA" class="brand-logo" />
      </div>
      <nav class="nav" aria-label="Main">
        <RouterLink v-for="item in nav" :key="item.to" :to="item.to" class="nav-link">
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path :d="item.icon" fill="currentColor" /></svg>
          <span>{{ item.label }}</span>
        </RouterLink>
      </nav>
      <div class="sidebar-foot">
        <div class="app-name">Resource Management</div>
        <div class="app-ver">v1.0 · dev</div>
      </div>
    </aside>

    <div class="content">
      <header class="topbar">
        <!-- Not a heading: each view supplies the page's single <h1>. -->
        <span class="topbar-title">{{ pageTitle }}</span>
        <div class="spacer" />
        <div class="user-chip">
          <span class="avatar" aria-hidden="true">DU</span>
          <div>
            <div class="user-name">Dev User</div>
            <div class="user-role">Administrator · General · Report</div>
          </div>
        </div>
      </header>
      <main id="main" ref="mainEl" tabindex="-1">
        <RouterView />
      </main>
    </div>

    <ToastHost />
  </div>
</template>

<style scoped>
.sidebar {
  width: var(--sidebar-w);
  flex-shrink: 0;
  background: linear-gradient(180deg, var(--brand-800), var(--brand-900));
  color: #cfe3ec;
  display: flex;
  flex-direction: column;
  position: sticky;
  top: 0;
  height: 100vh;
}
.brand { padding: 20px 20px 12px; }
.brand-logo { height: 38px; width: auto; max-width: 100%; object-fit: contain; }
.nav { display: flex; flex-direction: column; gap: 2px; padding: 12px 12px; }
.nav-link {
  display: flex; align-items: center; gap: 11px; padding: 10px 12px; border-radius: 8px;
  color: #cfe3ec; font-weight: 550; font-size: 14px;
}
.nav-link:hover { background: rgba(255, 255, 255, 0.08); text-decoration: none; color: #fff; }
.nav-link.router-link-active { background: var(--brand-600); color: #fff; box-shadow: inset 3px 0 0 var(--accent); }
.sidebar-foot { margin-top: auto; padding: 16px 20px; border-top: 1px solid rgba(255, 255, 255, 0.1); }
.app-name { font-weight: 600; color: #eaf3f7; font-size: 13px; }
.app-ver { color: #88aebd; font-size: 12px; margin-top: 2px; }

.topbar {
  height: 60px; background: var(--surface); border-bottom: 1px solid var(--border);
  display: flex; align-items: center; gap: 16px; padding: 0 28px; position: sticky; top: 0; z-index: 10;
}
.topbar-title { font-size: 22px; font-weight: 650; color: var(--gray-900); }
main:focus { outline: none; }
/* Focus indicator on the dark sidebar needs a light ring to stay visible */
.nav-link:focus-visible { outline-color: #fff; }
.user-chip { display: flex; align-items: center; gap: 10px; }
.user-chip .avatar {
  width: 34px; height: 34px; border-radius: 50%; background: var(--brand-100); color: var(--brand-700);
  display: grid; place-items: center; font-weight: 700; font-size: 13px;
}
.user-name { font-weight: 600; font-size: 13px; }
.user-role { color: var(--text-muted); font-size: 11.5px; }
</style>
