<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Pill from '@/components/Pill.vue'
import { api } from '@/api/client'
import type { Policy, PolicyPayload, PolicyWindow } from '@/api/types'
import { fmtSec, parseWindows } from '@/utils/format'
import { showToast } from '@/composables/useToast'

interface PolicyForm {
  name: string
  wd: number
  we: number
  max: number
  sw: number
  sq: number
  off: string
  enabled: string
  windows: PolicyWindow[]
}

const policies = ref<Policy[]>([])
const selectedId = ref<number | null>(null)
const form = ref<PolicyForm>(emptyForm())

function emptyForm(): PolicyForm {
  return { name: 'New policy', wd: 60, we: 120, max: 0, sw: 0, sq: 0, off: '', enabled: 'true', windows: [] }
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
    off: p.offlineQuotaSeconds === null ? '' : String(p.offlineQuotaSeconds / 60),
    enabled: String(p.enabled),
    windows: parseWindows(p.allowedWindowsJson).map((w) => ({
      start: w.start,
      end: w.end === '24:00' ? '23:59' : w.end
    }))
  }
}

function onSelect(event: Event): void {
  select(Number((event.target as HTMLSelectElement).value))
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
  const off = form.value.off.trim()
  const payload: PolicyPayload = {
    name: form.value.name,
    weekdayQuotaMinutes: Number(form.value.wd) || 0,
    weekendQuotaMinutes: Number(form.value.we) || 0,
    maxSessionMinutes: Number(form.value.max) || 0,
    slidingWindowMinutes: Number(form.value.sw) || 0,
    slidingQuotaMinutes: Number(form.value.sq) || 0,
    offlineQuotaMinutes: off === '' ? null : Number(off) || 0,
    enabled: form.value.enabled === 'true',
    windows: form.value.windows.map((w) => ({ start: w.start || '00:00', end: w.end || '24:00' }))
  }
  try {
    if (selectedId.value) {
      await api.put(`policies/${selectedId.value}`, payload)
    } else {
      const created = await api.post<{ id: number }>('policies', payload)
      selectedId.value = created.id
    }
    showToast('Saved')
    await load()
  } catch (e) {
    showToast(e instanceof Error ? e.message : 'Failed')
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
  <div class="card">
    <div class="card-head">
      <h3>Policy editor</h3>
      <span class="link" @click="newPolicy">+ New policy</span>
    </div>
    <div class="toolbar">
      <select :value="selectedId ?? ''" style="width:auto;min-width:240px" @change="onSelect">
        <option v-for="p in policies" :key="p.id" :value="p.id">#{{ p.id }} {{ p.name }}</option>
      </select>
    </div>
    <div class="grid2">
      <div><label>Name</label><input v-model="form.name" /></div>
      <div><label>Weekday daily quota (min, 0 = unlimited)</label><input v-model.number="form.wd" type="number" /></div>
      <div><label>Weekend daily quota (min, 0 = unlimited)</label><input v-model.number="form.we" type="number" /></div>
      <div><label>Max session length (min, 0 = unlimited)</label><input v-model.number="form.max" type="number" /></div>
      <div><label>Sliding window length (min, 0 = off)</label><input v-model.number="form.sw" type="number" placeholder="e.g. 120" /></div>
      <div><label>Max usage within window (min, 0 = off)</label><input v-model.number="form.sq" type="number" placeholder="e.g. 45" /></div>
      <div><label>Offline quota (min, blank = default, 0 = off)</label><input v-model="form.off" type="number" placeholder="default" /></div>
      <div>
        <label>Enabled</label>
        <select v-model="form.enabled"><option value="true">Enabled</option><option value="false">Disabled</option></select>
      </div>
    </div>
    <p class="hint">
      Sliding window example: length 120 + quota 45 = at most 45 minutes of use within any rolling 2 hours
      (cross-session; old usage ages out automatically).
    </p>

    <label>Allowed hours (outside = blocked; empty = all day)</label>
    <div v-for="(w, i) in form.windows" :key="i" class="winrow">
      <input v-model="w.start" type="time" />
      <span class="muted">to</span>
      <input v-model="w.end" type="time" />
      <button class="btn sm danger" @click="removeWindow(i)">&times;</button>
    </div>
    <button class="btn sm" @click="addWindow">+ Add window</button>

    <div class="form-actions">
      <button class="btn primary" @click="save">Save policy</button>
    </div>
  </div>

  <div class="card">
    <div class="wrap-x">
      <table>
        <thead>
          <tr><th>#</th><th>Name</th><th>Weekday</th><th>Weekend</th><th>Max session</th><th>Sliding</th><th>Offline</th><th>Hours</th><th>Status</th></tr>
        </thead>
        <tbody>
          <tr
            v-for="p in policies"
            :key="p.id"
            style="cursor:pointer"
            @click="select(p.id)"
          >
            <td>{{ p.id }}</td>
            <td><b>{{ p.name }}</b></td>
            <td>{{ p.weekdayQuotaSeconds ? fmtSec(p.weekdayQuotaSeconds) : 'unlimited' }}</td>
            <td>{{ p.weekendQuotaSeconds ? fmtSec(p.weekendQuotaSeconds) : 'unlimited' }}</td>
            <td>{{ p.maxSessionSeconds ? fmtSec(p.maxSessionSeconds) : 'unlimited' }}</td>
            <td>{{ slidingLabel(p) }}</td>
            <td>{{ offlineLabel(p) }}</td>
            <td class="soft">{{ windowLabel(p) }}</td>
            <td><Pill :type="p.enabled ? 'green' : 'gray'">{{ p.enabled ? 'Enabled' : 'Disabled' }}</Pill></td>
          </tr>
          <tr v-if="policies.length === 0"><td colspan="9" class="empty">No policies</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
