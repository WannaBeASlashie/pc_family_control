<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { api } from '@/api/client'
import type { Person, Policy } from '@/api/types'
import { fmtSec } from '@/utils/format'

const route = useRoute()
const persons = ref<Person[]>([])
const policies = ref<Policy[]>([])
const showNew = ref(false)
const editing = ref<Person | null>(null)
const saving = ref(false)
const form = ref({ name: '', pin: '', enabled: 'true', policyId: 1, delta: 0 })
const newForm = ref({ cardNo: '', name: '', pin: '', init: 60, policyId: 1 })

async function load(): Promise<void> {
  const [ps, pols] = await Promise.all([api.get<Person[]>('persons'), api.get<Policy[]>('policies')])
  persons.value = ps
  policies.value = pols
  if (pols.length && !pols.some((p) => p.id === newForm.value.policyId)) newForm.value.policyId = pols[0].id
}

function policyName(id: number): string {
  return policies.value.find((p) => p.id === id)?.name ?? String(id)
}

async function grant(id: number, delta: number): Promise<void> {
  try {
    const r = await api.post<{ ok: boolean; balanceSeconds?: number; reason?: string }>(`persons/${id}/balance`, {
      deltaSeconds: delta,
      note: 'console'
    })
    if (r.ok) ElMessage.success(`Updated, balance ${fmtSec(r.balanceSeconds ?? 0)}`)
    else ElMessage.warning(r.reason || 'Failed')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : 'Failed')
  }
  await load()
}

function edit(p: Person): void {
  editing.value = p
  form.value = { name: p.displayName, pin: '', enabled: String(p.enabled), policyId: p.policyId, delta: 0 }
}

async function save(): Promise<void> {
  if (!editing.value) return
  saving.value = true
  const body: Record<string, unknown> = {
    displayName: form.value.name,
    enabled: form.value.enabled === 'true',
    policyId: form.value.policyId
  }
  if (form.value.pin) body.pin = form.value.pin
  try {
    await api.put(`persons/${editing.value.id}`, body)
    if (form.value.delta !== 0)
      await api.post(`persons/${editing.value.id}/balance`, { deltaSeconds: form.value.delta * 60, note: 'console' })
    ElMessage.success('Saved')
    editing.value = null
    await load()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : 'Failed')
  } finally {
    saving.value = false
  }
}

async function create(): Promise<void> {
  saving.value = true
  try {
    await api.post('persons', {
      cardNo: newForm.value.cardNo.trim(),
      displayName: newForm.value.name.trim(),
      pin: newForm.value.pin,
      initialSeconds: (newForm.value.init || 0) * 60,
      policyId: newForm.value.policyId
    })
    ElMessage.success('Created')
    showNew.value = false
    newForm.value = { cardNo: '', name: '', pin: '', init: 60, policyId: policies.value[0]?.id ?? 1 }
    await load()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : 'Failed')
  } finally {
    saving.value = false
  }
}

async function remove(p: Person): Promise<void> {
  try {
    await ElMessageBox.confirm(`Delete account "${p.displayName}" (${p.cardNo})?`, 'Delete account', {
      type: 'warning',
      confirmButtonText: 'Delete',
      cancelButtonText: 'Cancel'
    })
    const r = await api.del<{ ok: boolean; reason?: string }>(`persons/${p.id}`)
    if (r.ok) ElMessage.success('Deleted')
    else ElMessage.warning(r.reason || 'Failed')
  } catch {
    /* cancelled */
  }
  await load()
}

onMounted(async () => {
  await load()
  if (route.query.new) showNew.value = true
})
</script>

<template>
  <el-card v-if="showNew" class="wb-card" style="margin-bottom: 16px">
    <template #header>
      <div class="card-head"><span>New account</span></div>
    </template>
    <el-form label-position="top">
      <el-row :gutter="16">
        <el-col :xs="24" :sm="12" :md="4"><el-form-item label="Card no."><el-input v-model="newForm.cardNo" /></el-form-item></el-col>
        <el-col :xs="24" :sm="12" :md="4"><el-form-item label="Name"><el-input v-model="newForm.name" /></el-form-item></el-col>
        <el-col :xs="24" :sm="12" :md="3"><el-form-item label="PIN"><el-input v-model="newForm.pin" maxlength="8" /></el-form-item></el-col>
        <el-col :xs="24" :sm="12" :md="3"><el-form-item label="Initial (min)"><el-input-number v-model="newForm.init" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :xs="24" :sm="12" :md="4">
          <el-form-item label="Policy">
            <el-select v-model="newForm.policyId" style="width: 100%">
              <el-option v-for="p in policies" :key="p.id" :label="p.name" :value="p.id" />
            </el-select>
          </el-form-item>
        </el-col>
      </el-row>
      <el-button type="primary" :loading="saving" @click="create">Create</el-button>
      <el-button @click="showNew = false">Cancel</el-button>
    </el-form>
  </el-card>

  <el-card v-if="editing" class="wb-card" style="margin-bottom: 16px">
    <template #header>
      <div class="card-head"><span>Edit account <span class="muted">({{ editing.cardNo }})</span></span></div>
    </template>
    <el-form label-position="top">
      <el-row :gutter="16">
        <el-col :xs="24" :sm="12" :md="4"><el-form-item label="Name"><el-input v-model="form.name" /></el-form-item></el-col>
        <el-col :xs="24" :sm="12" :md="4"><el-form-item label="PIN (blank = unchanged)"><el-input v-model="form.pin" /></el-form-item></el-col>
        <el-col :xs="24" :sm="12" :md="3">
          <el-form-item label="Status">
            <el-select v-model="form.enabled" style="width: 100%">
              <el-option label="Enabled" value="true" /><el-option label="Disabled" value="false" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="12" :md="4">
          <el-form-item label="Policy">
            <el-select v-model="form.policyId" style="width: 100%">
              <el-option v-for="p in policies" :key="p.id" :label="p.name" :value="p.id" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="12" :md="4">
          <el-form-item label="Adjust balance (min)">
            <el-input-number v-model="form.delta" :step="15" controls-position="right" style="width: 100%" />
          </el-form-item>
        </el-col>
      </el-row>
      <el-button type="primary" :loading="saving" @click="save">Save</el-button>
      <el-button @click="editing = null">Cancel</el-button>
    </el-form>
  </el-card>

  <div style="margin-bottom: 16px">
    <el-button type="primary" :icon="Plus" @click="showNew = true">New account</el-button>
  </div>

  <el-card class="wb-card">
    <el-table :data="persons" style="width: 100%">
      <el-table-column prop="cardNo" label="Card" width="110" />
      <el-table-column prop="displayName" label="Name" min-width="120" show-overflow-tooltip />
      <el-table-column label="Balance" width="110">
        <template #default="{ row }"><span class="mono">{{ fmtSec(row.balanceSeconds) }}</span></template>
      </el-table-column>
      <el-table-column label="Policy" min-width="120" show-overflow-tooltip>
        <template #default="{ row }"><span class="muted">{{ policyName(row.policyId) }}</span></template>
      </el-table-column>
      <el-table-column label="Status" width="100">
        <template #default="{ row }">
          <el-tag :type="row.enabled ? 'success' : 'info'" size="small" effect="light">{{ row.enabled ? 'Enabled' : 'Disabled' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Actions" width="330">
        <template #default="{ row }">
          <div class="row-actions">
            <el-button size="small" @click="grant(row.id, 900)">+15m</el-button>
            <el-button size="small" @click="grant(row.id, 1800)">+30m</el-button>
            <el-button size="small" @click="grant(row.id, 3600)">+1h</el-button>
            <el-button size="small" type="danger" plain @click="grant(row.id, -900)">-15m</el-button>
            <el-button size="small" type="primary" plain @click="edit(row)">Edit</el-button>
            <el-button size="small" type="danger" text @click="remove(row)">Delete</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!persons.length" description="No accounts yet" :image-size="60" />
  </el-card>
</template>
