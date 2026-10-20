<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { api } from '@/api/client'
import type { TimeRequest } from '@/api/types'
import { fmtTime } from '@/utils/format'
import { useDashboard } from '@/composables/useDashboard'

const requests = ref<TimeRequest[]>([])
const { refresh } = useDashboard()

async function load(): Promise<void> {
  requests.value = await api.get<TimeRequest[]>('requests?status=pending&limit=50')
}

async function decide(id: number, approve: boolean): Promise<void> {
  try {
    await api.post(`requests/${id}/decide`, { approve })
    ElMessage.success(approve ? 'Approved' : 'Rejected')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : 'Failed')
  }
  await Promise.all([load(), refresh()])
}

onMounted(load)
</script>

<template>
  <el-card class="wb-card">
    <template #header>
      <div class="card-head"><span>Pending requests</span></div>
    </template>
    <el-table :data="requests" style="width: 100%">
      <el-table-column prop="person" label="Person" min-width="140" show-overflow-tooltip />
      <el-table-column label="Request" width="120">
        <template #default="{ row }">
          <el-tag type="warning" effect="light" round>+{{ row.minutes }} min</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Time" width="170">
        <template #default="{ row }"><span class="muted">{{ fmtTime(row.createdAt) }}</span></template>
      </el-table-column>
      <el-table-column label="Actions" width="190">
        <template #default="{ row }">
          <el-button type="primary" size="small" @click="decide(row.id, true)">Approve</el-button>
          <el-button type="danger" size="small" plain @click="decide(row.id, false)">Reject</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!requests.length" description="No pending requests" :image-size="60" />
  </el-card>
</template>
