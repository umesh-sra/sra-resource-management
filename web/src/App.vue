<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterView, useRoute } from 'vue-router'
import ToastHost from '@/components/ToastHost.vue'
import AppAvatar from '@/components/AppAvatar.vue'

const route = useRoute()

// After client-side navigation, move focus to the main landmark so keyboard
// and screen-reader users land on the new page content (WCAG 2.4.3).
const mainEl = ref<HTMLElement | null>(null)
watch(
  () => route.path,
  () => { mainEl.value?.focus() },
)

const COLLAPSE_KEY = 'sra-rms.nav-collapsed'
const collapsed = ref(localStorage.getItem(COLLAPSE_KEY) === '1')
function toggleCollapsed() {
  collapsed.value = !collapsed.value
  localStorage.setItem(COLLAPSE_KEY, collapsed.value ? '1' : '0')
}

/**
 * Primary navigation, matching the reference application's information
 * architecture (screens/dashboard.png). `match` lists the extra path prefixes
 * that should light the item up — Clients lives under Projects & Clients, and
 * a person's detail panel is a route under People & Resources.
 */
const nav = [
  {
    to: '/dashboard', label: 'Dashboard',
    icon: 'M4 4h6v6H4V4Zm10 0h6v6h-6V4ZM4 14h6v6H4v-6Zm10 0h6v6h-6v-6Z',
  },
  {
    to: '/schedule', label: 'Schedule',
    icon: 'M3 5h10v3H3V5Zm0 5.5h16v3H3v-3ZM3 16h13v3H3v-3Z',
  },
  {
    to: '/gantt', label: 'Gantt Charts',
    icon: 'M3 4h18v2H3V4Zm2 4h10v3H5V8Zm4 5h11v3H9v-3Zm-6 5h13v3H3v-3Z',
  },
  {
    to: '/people', label: 'People & Resources', match: ['/resources'],
    icon: 'M16 11a3.5 3.5 0 1 0-3.5-3.5A3.5 3.5 0 0 0 16 11ZM8 11a3 3 0 1 0-3-3 3 3 0 0 0 3 3Zm0 2c-2.7 0-6 1.3-6 3.8V19h6.4v-1.8c0-1.3.5-2.5 1.4-3.5A10 10 0 0 0 8 13Zm8 0c-3 0-6.6 1.5-6.6 3.8V19H22v-2.2C22 14.5 19 13 16 13Z',
  },
  {
    to: '/projects', label: 'Projects & Clients', match: ['/clients'],
    icon: 'M9 3h6v2h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h3V3Zm2 2v1h2V5h-2ZM7 9h10v2H7V9Zm0 4h10v2H7v-2Z',
  },
  {
    to: '/reports', label: 'Reports',
    icon: 'M4 20V9h4v11H4Zm6 0V4h4v16h-4Zm6 0v-7h4v7h-4Z',
  },
]

/** A nav item is active for its own prefix and for any aliased prefix. */
function isActive(item: (typeof nav)[number]): boolean {
  return [item.to, ...(item.match ?? [])].some(
    (p) => route.path === p || route.path.startsWith(`${p}/`),
  )
}
</script>

<template>
  <div class="layout">
    <a href="#main" class="skip-link">Skip to main content</a>

    <aside class="sidebar" :class="{ collapsed }">
      <div class="brand">
        <img v-if="!collapsed" src="/sra-logo-white.png" alt="SRA" class="brand-logo" />
        <span v-else class="brand-mark" aria-hidden="true">SRA</span>
      </div>

      <nav class="nav" aria-label="Main">
        <RouterLink
          v-for="item in nav" :key="item.to" :to="item.to" class="nav-link"
          :class="{ active: isActive(item) }"
          :aria-current="isActive(item) ? 'page' : undefined"
          :title="collapsed ? item.label : undefined"
        >
          <svg viewBox="0 0 24 24" width="19" height="19" aria-hidden="true"><path :d="item.icon" fill="currentColor" /></svg>
          <span class="nav-label">{{ item.label }}</span>
        </RouterLink>
      </nav>

      <button
        class="collapse-toggle" type="button" @click="toggleCollapsed"
        :aria-expanded="!collapsed" aria-controls="main"
        :aria-label="collapsed ? 'Expand navigation' : 'Collapse navigation'"
      >
        <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true">
          <path :d="collapsed ? 'M9 6l6 6-6 6' : 'M15 6l-6 6 6 6'" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </button>

      <div class="sidebar-foot">
        <a class="nav-link" href="https://github.com/" target="_blank" rel="noopener" :title="collapsed ? 'Help' : undefined">
          <svg viewBox="0 0 24 24" width="19" height="19" aria-hidden="true">
            <path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm.1 15.5a1.2 1.2 0 1 1 0-2.4 1.2 1.2 0 0 1 0 2.4Zm1.7-5.6c-.8.6-1 .9-1 1.5v.3h-1.7v-.4c0-1.2.4-1.9 1.4-2.6.8-.6 1-.9 1-1.4 0-.6-.5-1-1.3-1s-1.4.5-1.5 1.3H9c.1-1.7 1.3-2.8 3.2-2.8 1.8 0 3.1 1 3.1 2.5 0 1-.4 1.6-1.5 2.6Z" fill="currentColor" />
          </svg>
          <span class="nav-label">Help</span>
        </a>
        <div class="user-chip" :title="collapsed ? 'Dev User' : undefined">
          <AppAvatar name="Dev User" :size="28" />
          <div class="nav-label user-meta">
            <div class="user-name">Dev User</div>
            <div class="user-role">Administrator</div>
          </div>
        </div>
      </div>
    </aside>

    <div class="content">
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
  transition: width .16s ease;
}
.sidebar.collapsed { width: var(--sidebar-w-collapsed); }
.sidebar.collapsed .nav-label { display: none; }
.sidebar.collapsed .nav-link { justify-content: center; padding: 11px 0; }
.sidebar.collapsed .brand { padding: 18px 0; text-align: center; }
.sidebar.collapsed .user-chip { justify-content: center; padding: 10px 0; }

.brand { padding: 18px 16px 10px; }
.brand-logo { height: 34px; width: auto; max-width: 100%; object-fit: contain; }
.brand-mark { font-weight: 800; letter-spacing: .06em; color: #fff; font-size: 14px; }

.nav { display: flex; flex-direction: column; gap: 3px; padding: 10px 10px; }
.nav-link {
  display: flex; align-items: center; gap: 11px; padding: 10px 11px; border-radius: 8px;
  color: #cfe3ec; font-weight: 550; font-size: 13.5px; white-space: nowrap;
}
.nav-link:hover { background: rgba(255, 255, 255, 0.09); text-decoration: none; color: #fff; }
.nav-link.active { background: var(--brand-600); color: #fff; box-shadow: inset 3px 0 0 var(--accent); }
/* Focus indicator on the dark sidebar needs a light ring to stay visible */
.nav-link:focus-visible { outline-color: #fff; }

/* Circular pull-tab on the sidebar's outer edge, as in the reference. */
.collapse-toggle {
  position: absolute; top: 50%; right: -13px; width: 26px; height: 26px; border-radius: 50%;
  border: 0; background: var(--brand-600); color: #fff; cursor: pointer;
  display: grid; place-items: center; box-shadow: var(--shadow-sm); z-index: 30;
}
.collapse-toggle:hover { background: var(--brand-500); }

.sidebar-foot { margin-top: auto; padding: 10px 10px 14px; border-top: 1px solid rgba(255, 255, 255, 0.1); }
.user-chip { display: flex; align-items: center; gap: 10px; padding: 10px 11px; }
.user-meta { min-width: 0; }
.user-name { font-weight: 600; color: #fff; font-size: 13px; }
.user-role { color: #8fb2c6; font-size: 11.5px; }

.content { min-height: 100vh; }
main { flex: 1; display: flex; flex-direction: column; min-width: 0; }
main:focus { outline: none; }
</style>
