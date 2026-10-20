<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api } from '@/api/client'
import type { SessionRecord } from '@/api/types'
import { fmtSec, fmtTime, reasonType } from '@/utils/format'
import { useReasonLabel } from '@/composables/useReasonLabel'

const { t } = useI18n()
const reasonLabel = useReasonLabel()
const sessions = ref<SessionRecord[]>([])

onMounted(async () => {
  sessions.value = await api.get<SessionRecord[]>('sessions?limit=50')
})
</script>

<template>
  <el-card class="wb-card">
    <template #header>
      <div class="card-head"><span>{{ t('records.title') }}</span></div>
    </template>
    <el-table :data="sessions" style="width: 100%">
      <el-table-column :label="t('common.person')" prop="person" min-width="130" show-overflow-tooltip>
        <template #default="{ row }"><b>{{ row.person }}</b></template>
      </el-table-column>
      <el-table-column :label="t('records.machine')" prop="machine" min-width="140" show-overflow-tooltip>
        <template #default="{ row }"><span class="muted">{{ row.machine }}</span></template>
      </el-table-column>
      <el-table-column :label="t('records.started')" width="160">
        <template #default="{ row }"><span class="muted">{{ fmtTime(row.startedAt) }}</span></template>
      </el-table-column>
      <el-table-column :label="t('records.ended')" width="160">
        <template #default="{ row }">
          <el-tag v-if="row.status === 'active'" type="success" size="small" effect="light">{{ t('overview.active') }}</el-tag>
          <span v-else class="muted">{{ fmtTime(row.endedAt) }}</span>
        </template>
      </el-table-column>
      <el-table-column :label="t('records.charged')" width="95">
        <template #default="{ row }">{{ fmtSec(row.chargedSeconds) }}</template>
      </el-table-column>
      <el-table-column :label="t('records.reason')" width="150">
        <template #default="{ row }">
          <el-tag :type="reasonType(row.endReason)" size="small" effect="light">{{ reasonLabel(row.endReason) }}</el-tag>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!sessions.length" :description="t('records.noSessions')" :image-size="60" />
  </el-card>
</template>
