<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import Pill from '@/components/Pill.vue'
import { api } from '@/api/client'
import type { Person, Policy } from '@/api/types'
import { fmtSec } from '@/utils/format'
import { showToast } from '@/composables/useToast'

const route = useRoute()
const persons = ref<Person[]>([])
const policies = ref<Policy[]>([])
const showNew = ref(false)
const editing = ref<Person | null>(null)
const form = ref({ name: '', pin: '', enabled: 'true', policyId: 1, delta: '' })
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
    showToast(r.ok ? `Updated, balance ${fmtSec(r.balanceSeconds ?? 0)}` : r.reason || 'Failed')
  } catch (e) {
    showToast(e instanceof Error ? e.message : 'Failed')
  }
  await load()
}

function edit(p: Person): void {
  editing.value = p
  form.value = { name: p.displayName, pin: '', enabled: String(p.enabled), policyId: p.policyId, delta: '' }
}

async function save(): Promise<void> {
  if (!editing.value) return
  const body: Record<string, unknown> = {
    displayName: form.value.name,
    enabled: form.value.enabled === 'true',
    policyId: form.value.policyId
  }
  if (form.value.pin) body.pin = form.value.pin
  try {
    await api.put(`persons/${editing.value.id}`, body)
    const delta = parseInt(form.value.delta || '0', 10) * 60
    if (delta !== 0) await api.post(`persons/${editing.value.id}/balance`, { deltaSeconds: delta, note: 'console' })
    showToast('Saved')
  } catch (e) {
    showToast(e instanceof Error ? e.message : 'Failed')
  }
  editing.value = null
  await load()
}

async function create(): Promise<void> {
  try {
    await api.post('persons', {
      cardNo: newForm.value.cardNo.trim(),
      displayName: newForm.value.name.trim(),
      pin: newForm.value.pin,
      initialSeconds: (newForm.value.init || 0) * 60,
      policyId: newForm.value.policyId
    })
    showToast('Created')
    showNew.value = false
    newForm.value = { cardNo: '', name: '', pin: '', init: 60, policyId: policies.value[0]?.id ?? 1 }
    await load()
  } catch (e) {
    showToast(e instanceof Error ? e.message : 'Failed')
  }
}

onMounted(async () => {
  await load()
  if (route.query.new) showNew.value = true
})
</script>

<template>
  <div v-if="showNew" class="card">
    <div class="card-head"><h3>New account</h3></div>
    <div class="grid2">
      <div><label>Card no.</label><input v-model="newForm.cardNo" /></div>
      <div><label>Name</label><input v-model="newForm.name" /></div>
      <div><label>PIN</label><input v-model="newForm.pin" maxlength="8" /></div>
      <div><label>Initial time (min)</label><input v-model.number="newForm.init" type="number" /></div>
      <div>
        <label>Policy</label>
        <select v-model.number="newForm.policyId">
          <option v-for="p in policies" :key="p.id" :value="p.id">{{ p.name }}</option>
        </select>
      </div>
    </div>
    <div class="form-actions">
      <button class="btn primary" @click="create">Create</button>
      <button class="btn" @click="showNew = false">Cancel</button>
    </div>
  </div>

  <div v-if="editing" class="card">
    <div class="card-head"><h3>Edit account <span class="soft">({{ editing.cardNo }})</span></h3></div>
    <div class="grid2">
      <div><label>Name</label><input v-model="form.name" /></div>
      <div><label>PIN (blank = unchanged)</label><input v-model="form.pin" /></div>
      <div>
        <label>Status</label>
        <select v-model="form.enabled"><option value="true">Enabled</option><option value="false">Disabled</option></select>
      </div>
      <div>
        <label>Policy</label>
        <select v-model.number="form.policyId">
          <option v-for="p in policies" :key="p.id" :value="p.id">{{ p.name }}</option>
        </select>
      </div>
      <div><label>Adjust balance (min, negative = deduct)</label><input v-model="form.delta" type="number" placeholder="e.g. 30 or -15" /></div>
    </div>
    <div class="form-actions">
      <button class="btn primary" @click="save">Save</button>
      <button class="btn" @click="editing = null">Cancel</button>
    </div>
  </div>

  <div class="toolbar"><button class="btn primary" @click="showNew = true">+ New account</button></div>

  <div class="card">
    <div class="wrap-x">
      <table>
        <thead>
          <tr><th>Card</th><th>Name</th><th>Balance</th><th>Policy</th><th>Status</th><th>Actions</th></tr>
        </thead>
        <tbody>
          <tr v-for="p in persons" :key="p.id">
            <td>{{ p.cardNo }}</td>
            <td>{{ p.displayName }}</td>
            <td class="balance">{{ fmtSec(p.balanceSeconds) }}</td>
            <td class="muted">{{ policyName(p.policyId) }}</td>
            <td>
              <Pill :type="p.enabled ? 'green' : 'gray'">{{ p.enabled ? 'Enabled' : 'Disabled' }}</Pill>
            </td>
            <td>
              <div style="display:flex;gap:4px;flex-wrap:wrap">
                <button class="btn sm" @click="grant(p.id, 900)">+15m</button>
                <button class="btn sm" @click="grant(p.id, 1800)">+30m</button>
                <button class="btn sm" @click="grant(p.id, 3600)">+1h</button>
                <button class="btn sm danger" @click="grant(p.id, -900)">-15m</button>
                <button class="btn sm" @click="edit(p)">Edit</button>
              </div>
            </td>
          </tr>
          <tr v-if="persons.length === 0"><td colspan="6" class="empty">No accounts yet</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
