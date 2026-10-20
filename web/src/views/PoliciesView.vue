<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Delete } from '@element-plus/icons-vue'
import { api } from '@/api/client'
import type { Policy, PolicyPayload, PolicyWindow } from '@/api/types'
import { fmtSec, parseWindows } from '@/utils/format'

interface PolicyForm {
  name: string
  wd: number
  we: number
  max: number
  sw: number
  sq: number
  off: number | undefined
  enabled: string
  windows: PolicyWindow[]
}

const policies = ref<Policy[]>([])
const selectedId = ref<number | null>(null)
const saving = ref(false)
const form = ref<PolicyForm>(emptyForm())

function emptyForm(): PolicyForm {
  return { name: 'New policy', wd: 60, we: 120, max: 0, sw: 0, sq: 0, off: undefined, enabled: 'true', windows: [] }
}

async function load(): Promise<void> {
  policies.value = await api.get<Policy[]>('policies')
  if (selectedId.value && policies.value.some((p) => p.id === selectedId.value)) {
    select(selectedId.value)
  } else if (policies.value.length) {
    select(policies.value[0].id)
  }
}

function select(id: number): void {
  const p = policies.value.find((x) => x.id === id)
  if (!p) return
  selectedId.value = id
  form.value = {
    name: p.name,
    wd: p.weekdayQuotaSeconds / 60,
    we: p.weekendQuotaSeconds / 60,
    max: p.maxSessionSeconds / 60,
    sw: p.slidingWindowSeconds / 60,
    sq: p.slidingQuotaSeconds / 60,
    off: p.offlineQuotaSeconds === null ? undefined : p.offlineQuotaSeconds / 60,
    enabled: String(p.enabled),
    windows: parseWindows(p.allowedWindowsJson).map((w) => ({
      start: w.start,
      end: w.end === '24:00' ? '23:59' : w.end
    }))
  }
}

function onSelect(value: number): void {
  select(value)
}

function newPolicy(): void {
  selectedId.value = null
  form.value = emptyForm()
  form.value.windows = [{ start: '08:00', end: '21:30' }]
}

function addWindow(): void {
  form.value.windows.push({ start: '08:00', end: '21:30' })
}

function removeWindow(index: number): void {
  form.value.windows.splice(index, 1)
}

async function save(): Promise<void> {
  const payload: PolicyPayload = {
    name: form.value.name,
    weekdayQuotaMinutes: Number(form.value.wd) || 0,
    weekendQuotaMinutes: Number(form.value.we) || 0,
    maxSessionMinutes: Number(form.value.max) || 0,
    slidingWindowMinutes: Number(form.value.sw) || 0,
    slidingQuotaMinutes: Number(form.value.sq) || 0,
    offlineQuotaMinutes: form.value.off === undefined || form.value.off === null ? null : Number(form.value.off),
    enabled: form.value.enabled === 'true',
    windows: form.value.windows.map((w) => ({ start: w.start || '00:00', end: w.end || '24:00' }))
  }
  saving.value = true
  try {
    if (selectedId.value) {
      await api.put(`policies/${selectedId.value}`, payload)
    } else {
      const created = await api.post<{ id: number }>('policies', payload)
      selectedId.value = created.id
    }
    ElMessage.success('Saved')
    await load()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : 'Failed')
  } finally {
    saving.value = false
  }
}

function slidingLabel(p: Policy): string {
  return p.slidingWindowSeconds ? `${fmtSec(p.slidingQuotaSeconds)} / ${fmtSec(p.slidingWindowSeconds)}` : 'off'
}

function offlineLabel(p: Policy): string {
  if (p.offlineQuotaSeconds === null || p.offlineQuotaSeconds === undefined) return 'default'
  return p.offlineQuotaSeconds === 0 ? 'off' : fmtSec(p.offlineQuotaSeconds)
}

function windowLabel(p: Policy): string {
  const windows = parseWindows(p.allowedWindowsJson)
  return windows.length ? windows.map((w) => `${w.start}-${w.end}`).join(' / ') : '-'
}

onMounted(load)
</script>

<template>
  <el-card class="wb-card" style="margin-bottom: 16px">
    <template #header>
      <div class="card-head">
        <span>Policy editor</span>
        <el-button link type="primary" @click="newPolicy">+ New policy</el-button>
      </div>
    </template>

    <el-form label-position="top">
      <el-row :gutter="16">
        <el-col :xs="24" :sm="12" :md="5">
          <el-form-item label="Policy">
            <el-select :model-value="selectedId ?? undefined" placeholder="Select policy" style="width: 100%" @update:model-value="onSelect">
              <el-option v-for="p in policies" :key="p.id" :label="`#${p.id} ${p.name}`" :value="p.id" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="12" :md="4"><el-form-item label="Name"><el-input v-model="form.name" /></el-form-item></el-col>
        <el-col :xs="12" :sm="8" :md="3"><el-form-item label="Weekday quota (min)"><el-input-number v-model="form.wd" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :xs="12" :sm="8" :md="3"><el-form-item label="Weekend quota (min)"><el-input-number v-model="form.we" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :xs="12" :sm="8" :md="3"><el-form-item label="Max session (min)"><el-input-number v-model="form.max" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :xs="12" :sm="8" :md="3"><el-form-item label="Window length (min)"><el-input-number v-model="form.sw" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :xs="12" :sm="8" :md="3"><el-form-item label="Window quota (min)"><el-input-number v-model="form.sq" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :xs="12" :sm="8" :md="3">
          <el-form-item label="Offline quota (min)">
            <el-input-number v-model="form.off" :min="0" controls-position="right" placeholder="default" style="width: 100%" />
          </el-form-item>
        </el-col>
        <el-col :xs="12" :sm="8" :md="2">
          <el-form-item label="Enabled">
            <el-select v-model="form.enabled" style="width: 100%">
              <el-option label="Enabled" value="true" /><el-option label="Disabled" value="false" />
            </el-select>
          </el-form-item>
        </el-col>
      </el-row>

      <div style="font-size: 13px; color: #909399; margin: 2px 0 14px">
        Sliding window example: length 120 + quota 45 = at most 45 minutes within any rolling 2 hours
        (cross-session, old usage ages out automatically). Offline quota: blank = global default, 0 = disabled.
      </div>

      <el-form-item label="Allowed hours (outside = blocked; empty = all day)">
        <div style="width: 100%">
          <div v-for="(w, i) in form.windows" :key="i" style="display: flex; gap: 8px; align-items: center; margin-bottom: 8px">
            <el-time-picker v-model="w.start" format="HH:mm" value-format="HH:mm" :clearable="false" placeholder="Start" style="width: 130px" />
            <span style="color: #909399">to</span>
            <el-time-picker v-model="w.end" format="HH:mm" value-format="HH:mm" :clearable="false" placeholder="End" style="width: 130px" />
            <el-button :icon="Delete" text type="danger" @click="removeWindow(i)">Remove</el-button>
          </div>
          <el-button size="small" @click="addWindow">+ Add window</el-button>
        </div>
      </el-form-item>
    </el-form>

    <el-button type="primary" :loading="saving" @click="save">Save policy</el-button>
  </el-card>

  <el-card class="wb-card">
    <el-table :data="policies" style="width: 100%" @row-click="(row: Policy) => select(row.id)">
      <el-table-column prop="id" label="#" width="52" />
      <el-table-column prop="name" label="Name" min-width="120" show-overflow-tooltip>
        <template #default="{ row }"><b>{{ row.name }}</b></template>
      </el-table-column>
      <el-table-column label="Weekday" width="100">
        <template #default="{ row }">{{ row.weekdayQuotaSeconds ? fmtSec(row.weekdayQuotaSeconds) : 'unlimited' }}</template>
      </el-table-column>
      <el-table-column label="Weekend" width="100">
        <template #default="{ row }">{{ row.weekendQuotaSeconds ? fmtSec(row.weekendQuotaSeconds) : 'unlimited' }}</template>
      </el-table-column>
      <el-table-column label="Max session" width="110">
        <template #default="{ row }">{{ row.maxSessionSeconds ? fmtSec(row.maxSessionSeconds) : 'unlimited' }}</template>
      </el-table-column>
      <el-table-column label="Sliding" width="130">
        <template #default="{ row }">{{ slidingLabel(row) }}</template>
      </el-table-column>
      <el-table-column label="Offline" width="100">
        <template #default="{ row }">{{ offlineLabel(row) }}</template>
      </el-table-column>
      <el-table-column label="Hours" min-width="140" show-overflow-tooltip>
        <template #default="{ row }"><span class="muted">{{ windowLabel(row) }}</span></template>
      </el-table-column>
      <el-table-column label="Status" width="100">
        <template #default="{ row }">
          <el-tag :type="row.enabled ? 'success' : 'info'" size="small" effect="light">{{ row.enabled ? 'Enabled' : 'Disabled' }}</el-tag>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!policies.length" description="No policies" :image-size="60" />
  </el-card>
</template>
