<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api } from '@/api/client'
import type { SessionRecord } from '@/api/types'
import { fmtSec, fmtTime, reasonType } from '@/utils/format'

const sessions = ref<SessionRecord[]>([])

onMounted(async () => {
  sessions.value = await api.get<SessionRecord[]>('sessions?limit=50')
})
</script>

<template>
  <el-card class="wb-card">
    <template #header>
      <div class="card-head"><span>Recent sessions</span></div>
    </template>
    <el-table :data="sessions" style="width: 100%">
      <el-table-column prop="person" label="Person" min-width="130" show-overflow-tooltip>
        <template #default="{ row }"><b>{{ row.person }}</b></template>
      </el-table-column>
      <el-table-column prop="machine" label="Machine" min-width="140" show-overflow-tooltip>
        <template #default="{ row }"><span class="muted">{{ row.machine }}</span></template>
      </el-table-column>
      <el-table-column label="Started" width="160">
        <template #default="{ row }"><span class="muted">{{ fmtTime(row.startedAt) }}</span></template>
      </el-table-column>
      <el-table-column label="Ended" width="160">
        <template #default="{ row }">
          <el-tag v-if="row.status === 'active'" type="success" size="small" effect="light">active</el-tag>
          <span v-else class="muted">{{ fmtTime(row.endedAt) }}</span>
        </template>
      </el-table-column>
      <el-table-column label="Charged" width="95">
        <template #default="{ row }">{{ fmtSec(row.chargedSeconds) }}</template>
      </el-table-column>
      <el-table-column label="Reason" width="150">
        <template #default="{ row }">
          <el-tag :type="reasonType(row.endReason)" size="small" effect="light">{{ row.endReason || '-' }}</el-tag>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!sessions.length" description="No sessions yet" :image-size="60" />
  </el-card>
</template>
