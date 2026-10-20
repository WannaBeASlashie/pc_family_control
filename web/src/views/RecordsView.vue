<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Pill from '@/components/Pill.vue'
import { api } from '@/api/client'
import type { SessionRecord } from '@/api/types'
import { fmtSec, fmtTime, reasonType } from '@/utils/format'

const sessions = ref<SessionRecord[]>([])

onMounted(async () => {
  sessions.value = await api.get<SessionRecord[]>('sessions?limit=50')
})
</script>

<template>
  <div class="card">
    <div class="card-head"><h3>Recent sessions</h3></div>
    <div class="wrap-x">
      <table>
        <thead>
          <tr><th>Person</th><th>Machine</th><th>Started</th><th>Ended</th><th>Charged</th><th>Reason</th></tr>
        </thead>
        <tbody>
          <tr v-for="s in sessions" :key="s.id">
            <td><b>{{ s.person }}</b></td>
            <td class="muted">{{ s.machine }}</td>
            <td class="soft">{{ fmtTime(s.startedAt) }}</td>
            <td class="soft">
              <Pill v-if="s.status === 'active'" type="green">active</Pill>
              <template v-else>{{ fmtTime(s.endedAt) }}</template>
            </td>
            <td>{{ fmtSec(s.chargedSeconds) }}</td>
            <td><Pill :type="reasonType(s.endReason)">{{ s.endReason || '-' }}</Pill></td>
          </tr>
          <tr v-if="sessions.length === 0"><td colspan="6" class="empty">No sessions yet</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
