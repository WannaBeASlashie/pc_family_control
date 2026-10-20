<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Pill from '@/components/Pill.vue'
import { api } from '@/api/client'
import type { TimeRequest } from '@/api/types'
import { fmtTime } from '@/utils/format'
import { showToast } from '@/composables/useToast'
import { useDashboard } from '@/composables/useDashboard'

const requests = ref<TimeRequest[]>([])
const { refresh } = useDashboard()

async function load(): Promise<void> {
  requests.value = await api.get<TimeRequest[]>('requests?status=pending&limit=50')
}

async function decide(id: number, approve: boolean): Promise<void> {
  try {
    await api.post(`requests/${id}/decide`, { approve })
    showToast(approve ? 'Approved' : 'Rejected')
  } catch (e) {
    showToast(e instanceof Error ? e.message : 'Failed')
  }
  await Promise.all([load(), refresh()])
}

onMounted(load)
</script>

<template>
  <div class="card">
    <div class="wrap-x">
      <table>
        <thead>
          <tr><th>Person</th><th>Request</th><th>Time</th><th>Actions</th></tr>
        </thead>
        <tbody>
          <tr v-for="r in requests" :key="r.id">
            <td><b>{{ r.person }}</b></td>
            <td><Pill type="amber">+{{ r.minutes }} min</Pill></td>
            <td class="soft">{{ fmtTime(r.createdAt) }}</td>
            <td>
              <div style="display:flex;gap:4px">
                <button class="btn sm primary" @click="decide(r.id, true)">Approve</button>
                <button class="btn sm danger" @click="decide(r.id, false)">Reject</button>
              </div>
            </td>
          </tr>
          <tr v-if="requests.length === 0"><td colspan="4" class="empty">No pending requests</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
