<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import StatCard from '@/components/StatCard.vue'
import Pill from '@/components/Pill.vue'
import { useDashboard } from '@/composables/useDashboard'
import { showToast } from '@/composables/useToast'
import { api } from '@/api/client'
import { fmtSec, fmtTime, reasonType } from '@/utils/format'

const router = useRouter()
const { overview, recentSessions, pendingPreview, refresh } = useDashboard()

const online = computed(() =>
  overview.value ? `${overview.value.machines.filter((m) => m.online).length}/${overview.value.machines.length}` : '-'
)
const sessionCount = computed(() => overview.value?.activeSessions.length ?? 0)
const pendingCount = computed(() => overview.value?.pendingRequests ?? 0)
const accountCount = computed(() => overview.value?.persons.length ?? 0)

async function approve(id: number): Promise<void> {
  try {
    await api.post(`requests/${id}/decide`, { approve: true })
    showToast('Approved')
  } catch (e) {
    showToast(e instanceof Error ? e.message : 'Failed')
  }
  await refresh()
}
</script>

<template>
  <div class="hero">
    <div class="mark">∞</div>
    <div>
      <h2>willBeyond <b>TimeBank</b></h2>
      <p>Time, well spent — every minute accounted for.</p>
    </div>
    <span class="chip">v2.1 · offline mode live</span>
  </div>

  <div class="stats">
    <StatCard label="Machines online" :value="online" />
    <StatCard label="Active sessions" :value="sessionCount" />
    <StatCard label="Pending requests" :value="pendingCount" danger clickable @click="router.push('/requests')" />
    <StatCard label="Accounts" :value="accountCount" />
  </div>

  <div class="cols">
    <div class="card">
      <div class="card-head">
        <h3>Recent sessions</h3>
        <span class="link" @click="router.push('/records')">All →</span>
      </div>
      <div class="wrap-x">
        <table>
          <thead>
            <tr><th>Person</th><th>Machine</th><th>Started</th><th>Charged</th><th>Reason</th></tr>
          </thead>
          <tbody>
            <tr v-for="s in recentSessions" :key="s.id">
              <td>{{ s.person }}</td>
              <td class="muted">{{ s.machine }}</td>
              <td class="soft">{{ fmtTime(s.startedAt) }}</td>
              <td>{{ fmtSec(s.chargedSeconds) }}</td>
              <td>
                <Pill v-if="s.status === 'active'" type="green">active</Pill>
                <Pill v-else :type="reasonType(s.endReason)">{{ s.endReason || '-' }}</Pill>
              </td>
            </tr>
            <tr v-if="recentSessions.length === 0"><td colspan="5" class="empty">No sessions yet</td></tr>
          </tbody>
        </table>
      </div>
    </div>

    <div class="card">
      <div class="card-head">
        <h3>Pending requests</h3>
        <span class="link" @click="router.push('/requests')">Review →</span>
      </div>
      <div v-if="pendingPreview.length === 0" class="empty">No pending requests</div>
      <div
        v-for="r in pendingPreview"
        :key="r.id"
        style="display:flex;align-items:center;gap:10px;padding:9px 0;border-bottom:1px solid var(--line)"
      >
        <div style="flex:1">
          <b style="font-size:13.5px">{{ r.person }}</b>
          <div class="soft" style="font-size:12px">{{ fmtTime(r.createdAt) }}</div>
        </div>
        <Pill type="amber">+{{ r.minutes }} min</Pill>
        <button class="btn sm primary" @click="approve(r.id)">Approve</button>
      </div>
    </div>
  </div>

  <div class="quick">
    <router-link to="/accounts?new=1" class="q">
      <div class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg></div>
      <div><b>New account</b><span>Create a card + PIN for a family member</span></div>
    </router-link>
    <router-link to="/requests" class="q">
      <div class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12 4.5 4.5L19 7"/></svg></div>
      <div><b>Approve requests</b><span>Grant extra time with one tap</span></div>
    </router-link>
    <router-link to="/policies" class="q">
      <div class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M4 8h16M4 16h16"/><circle cx="9" cy="8" r="2.2"/><circle cx="15" cy="16" r="2.2"/></svg></div>
      <div><b>Edit policies</b><span>Daily quotas, hours, sliding windows, offline</span></div>
    </router-link>
    <router-link to="/machines" class="q">
      <div class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/></svg></div>
      <div><b>Machines</b><span>Register PCs, send lock / logoff / message</span></div>
    </router-link>
  </div>
</template>
