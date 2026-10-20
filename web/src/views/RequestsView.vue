<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { api } from '@/api/client'
import type { TimeRequest } from '@/api/types'
import { fmtTime } from '@/utils/format'
import { useDashboard } from '@/composables/useDashboard'

const { t } = useI18n()
const requests = ref<TimeRequest[]>([])
const { refresh } = useDashboard()

async function load(): Promise<void> {
  requests.value = await api.get<TimeRequest[]>('requests?status=pending&limit=50')
}

async function decide(id: number, approve: boolean): Promise<void> {
  try {
    await api.post(`requests/${id}/decide`, { approve })
    ElMessage.success(approve ? t('requests.approved') : t('requests.rejected'))
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : t('common.failed'))
  }
  await Promise.all([load(), refresh()])
}

onMounted(load)
</script>

<template>
  <el-card class="wb-card">
    <template #header>
      <div class="card-head"><span>{{ t('requests.title') }}</span></div>
    </template>
    <el-table :data="requests" style="width: 100%">
      <el-table-column :label="t('common.person')" prop="person" min-width="140" show-overflow-tooltip />
      <el-table-column :label="t('requests.request')" width="130">
        <template #default="{ row }">
          <el-tag type="warning" effect="light" round>{{ t('common.plusMin', { n: row.minutes }) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="t('common.time')" width="170">
        <template #default="{ row }"><span class="muted">{{ fmtTime(row.createdAt) }}</span></template>
      </el-table-column>
      <el-table-column :label="t('common.actions')" width="210">
        <template #default="{ row }">
          <el-button type="primary" size="small" @click="decide(row.id, true)">{{ t('common.approve') }}</el-button>
          <el-button type="danger" size="small" plain @click="decide(row.id, false)">{{ t('common.reject') }}</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!requests.length" :description="t('requests.noPending')" :image-size="60" />
  </el-card>
</template>
