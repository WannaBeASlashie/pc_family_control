<script setup lang="ts">
import { computed } from 'vue'
import { useDashboard } from '@/composables/useDashboard'

const { overview } = useDashboard()
const pending = computed(() => overview.value?.pendingRequests ?? 0)
</script>

<template>
  <aside class="sidebar">
    <div class="brand">
      <div class="mark">∞</div>
      <div class="wordmark"><b>willBeyond</b><span>TIMEBANK</span></div>
    </div>

    <nav class="nav">
      <router-link to="/overview">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V21h14V9.5"/><path d="M9 21v-6h6v6"/></svg>
        <span class="lbl-full">Overview</span>
      </router-link>
      <router-link to="/accounts">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="9" cy="8" r="3.2"/><path d="M2.8 20c.6-3.2 3.1-5 6.2-5s5.6 1.8 6.2 5"/><path d="M16 4.5a3 3 0 0 1 0 6"/><path d="M18.5 14.6c1.7.8 2.7 2.4 2.9 4.4"/></svg>
        <span class="lbl-full">Accounts</span>
      </router-link>
      <router-link to="/requests">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="3.5" width="16" height="17" rx="2.5"/><path d="m8.5 12 2.3 2.3L16 9.5"/></svg>
        <span class="lbl-full">Requests</span>
        <span v-if="pending > 0" class="badge">{{ pending }}</span>
      </router-link>
      <router-link to="/policies">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7h10M18 7h2M4 17h2M10 17h10"/><circle cx="16" cy="7" r="2.2"/><circle cx="8" cy="17" r="2.2"/></svg>
        <span class="lbl-full">Policies</span>
      </router-link>
      <router-link to="/machines">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/></svg>
        <span class="lbl-full">Machines</span>
      </router-link>
      <router-link to="/records">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/></svg>
        <span class="lbl-full">Records</span>
      </router-link>
    </nav>

    <div class="foot">willBeyond TimeBank · LAN only</div>
  </aside>
</template>
