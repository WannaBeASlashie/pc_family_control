<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Plus, Check, Operation, Monitor } from '@element-plus/icons-vue'
import WbLogo from '@/components/WbLogo.vue'
import { useDashboard } from '@/composables/useDashboard'
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

const entries = [
  { title: 'New account', desc: 'Create a card + PIN for a family member', path: '/accounts?new=1', icon: Plus },
  { title: 'Approve requests', desc: 'Grant extra time with one tap', path: '/requests', icon: Check },
  { title: 'Edit policies', desc: 'Quotas, hours, sliding windows, offline', path: '/policies', icon: Operation },
  { title: 'Machines', desc: 'Register PCs, send lock / logoff / message', path: '/machines', icon: Monitor }
]

async function approve(id: number): Promise<void> {
  try {
    await api.post(`requests/${id}/decide`, { approve: true })
    ElMessage.success('Approved')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : 'Failed')
  }
  await refresh()
}
</script>

<template>
  <div class="hero wb-soft wb-card">
    <WbLogo :size="64" />
    <div>
      <div class="hero-name">willBeyond<span>TimeBank</span></div>
      <div class="hero-slogan">Time, well spent — every minute accounted for.</div>
    </div>
    <div class="hero-tags">
      <el-tag size="small" round>v2.1 · offline mode live</el-tag>
    </div>
  </div>

  <el-row :gutter="16" style="margin-top: 16px; align-items: stretch">
    <el-col :xs="12" :sm="12" :md="6" style="display: flex">
      <el-card class="stat-card wb-card" shadow="never" style="flex: 1; width: 100%">
        <div class="stat-label">Machines online</div>
        <div class="stat-value">{{ online }}</div>
      </el-card>
    </el-col>
    <el-col :xs="12" :sm="12" :md="6" style="display: flex">
      <el-card class="stat-card wb-card" shadow="never" style="flex: 1; width: 100%">
        <div class="stat-label">Active sessions</div>
        <div class="stat-value">{{ sessionCount }}</div>
      </el-card>
    </el-col>
    <el-col :xs="12" :sm="12" :md="6" style="display: flex">
      <el-card class="stat-card wb-card" shadow="never" style="flex: 1; width: 100%; cursor: pointer" @click="router.push('/requests')">
        <div class="stat-label">Pending requests</div>
        <div class="stat-value" :style="{ color: pendingCount > 0 ? '#ef4444' : undefined }">{{ pendingCount }}</div>
      </el-card>
    </el-col>
    <el-col :xs="12" :sm="12" :md="6" style="display: flex">
      <el-card class="stat-card wb-card" shadow="never" style="flex: 1; width: 100%">
        <div class="stat-label">Accounts</div>
        <div class="stat-value">{{ accountCount }}</div>
      </el-card>
    </el-col>
  </el-row>

  <el-row :gutter="16" style="margin-top: 16px; align-items: stretch">
    <el-col :xs="24" :md="14" style="display: flex">
      <el-card class="wb-card" style="flex: 1; width: 100%">
        <template #header>
          <div class="card-head">
            <span>Recent sessions</span>
            <el-button link type="primary" @click="router.push('/records')">All →</el-button>
          </div>
        </template>
        <el-table :data="recentSessions" size="small">
          <el-table-column prop="person" label="Person" min-width="110" show-overflow-tooltip />
          <el-table-column prop="machine" label="Machine" min-width="110" show-overflow-tooltip />
          <el-table-column label="Started" width="150">
            <template #default="{ row }">{{ fmtTime(row.startedAt) }}</template>
          </el-table-column>
          <el-table-column label="Charged" width="95">
            <template #default="{ row }">{{ fmtSec(row.chargedSeconds) }}</template>
          </el-table-column>
          <el-table-column label="Reason" width="150">
            <template #default="{ row }">
              <el-tag v-if="row.status === 'active'" type="success" size="small" effect="light">active</el-tag>
              <el-tag v-else :type="reasonType(row.endReason)" size="small" effect="light">{{ row.endReason || '-' }}</el-tag>
            </template>
          </el-table-column>
        </el-table>
        <el-empty v-if="!recentSessions.length" description="No sessions yet" :image-size="60" />
      </el-card>
    </el-col>

    <el-col :xs="24" :md="10" style="display: flex">
      <el-card class="wb-card" style="flex: 1; width: 100%">
        <template #header>
          <div class="card-head">
            <span>Pending requests</span>
            <el-button link type="primary" @click="router.push('/requests')">Review →</el-button>
          </div>
        </template>
        <template v-if="pendingPreview.length">
          <div v-for="r in pendingPreview" :key="r.id" class="list-row">
            <div class="list-main">
              <div class="list-name">{{ r.person }}</div>
              <div class="list-sub">{{ fmtTime(r.createdAt) }}</div>
            </div>
            <el-tag type="warning" size="small" effect="light" round>+{{ r.minutes }} min</el-tag>
            <el-button type="primary" size="small" @click="approve(r.id)">Approve</el-button>
          </div>
        </template>
        <el-empty v-else description="No pending requests" :image-size="60" />
      </el-card>
    </el-col>
  </el-row>

  <el-row :gutter="16" style="margin-top: 16px; align-items: stretch">
    <el-col v-for="e in entries" :key="e.path" :xs="24" :sm="12" :lg="6" style="display: flex">
      <el-card shadow="hover" class="module-card wb-card" style="flex: 1; width: 100%" @click="router.push(e.path)">
        <div style="display: flex; align-items: center; gap: 12px; height: 100%">
          <div class="module-icon">
            <el-icon :size="22"><component :is="e.icon" /></el-icon>
          </div>
          <div style="min-width: 0">
            <div style="font-weight: 700; color: var(--wb-ink)">{{ e.title }}</div>
            <div style="font-size: 12px; color: #909399; margin-top: 2px">{{ e.desc }}</div>
          </div>
        </div>
      </el-card>
    </el-col>
  </el-row>
</template>
