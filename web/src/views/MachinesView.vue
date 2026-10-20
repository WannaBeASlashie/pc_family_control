<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Pill from '@/components/Pill.vue'
import { api } from '@/api/client'
import type { CreateMachineResult, MachineDetail } from '@/api/types'
import { fmtTime, isOnline } from '@/utils/format'
import { showToast } from '@/composables/useToast'

const machines = ref<MachineDetail[]>([])

async function load(): Promise<void> {
  machines.value = await api.get<MachineDetail[]>('machines')
}

async function send(id: number, action: string): Promise<void> {
  let text: string | undefined
  if (action === 'message') {
    const input = window.prompt('Message:')
    if (!input) return
    text = input
  }
  if ((action === 'shutdown' || action === 'reboot') && !window.confirm(`Really ${action} this machine?`)) return
  try {
    const r = await api.post<{ ok: boolean; reason?: string }>(`machines/${id}/command`, { action, text })
    showToast(r.ok ? 'Command queued (applies on next heartbeat)' : r.reason || 'Failed')
  } catch (e) {
    showToast(e instanceof Error ? e.message : 'Failed')
  }
}

async function register(): Promise<void> {
  const name = window.prompt('Machine name (e.g. PC-1):')
  if (!name) return
  try {
    const r = await api.post<CreateMachineResult>('machines', { name })
    if (r.ok) {
      window.alert(`Machine created: id=${r.id}\nSecret: ${r.secret}\n\nUse with install-client.ps1 -MachineId/-Secret`)
    }
    await load()
  } catch (e) {
    showToast(e instanceof Error ? e.message : 'Failed')
  }
}

async function remove(id: number, name: string): Promise<void> {
  if (!window.confirm(`Delete machine "${name}"?\nIts session history and commands will be removed (irreversible).`)) return
  try {
    const r = await api.del<{ ok: boolean; reason?: string }>(`machines/${id}`)
    showToast(r.ok ? 'Deleted' : r.reason || 'Failed')
  } catch (e) {
    showToast(e instanceof Error ? e.message : 'Failed')
  }
  await load()
}

onMounted(load)
</script>

<template>
  <div class="toolbar"><button class="btn primary" @click="register">+ Register machine</button></div>

  <div class="card">
    <div class="wrap-x">
      <table>
        <thead>
          <tr><th>ID</th><th>Name</th><th>Online</th><th>Last heartbeat</th><th>Actions</th></tr>
        </thead>
        <tbody>
          <tr v-for="m in machines" :key="m.id">
            <td class="soft">{{ m.id }}</td>
            <td><b>{{ m.name }}</b></td>
            <td>
              <Pill :type="isOnline(m.lastSeenAt) ? 'green' : 'gray'">{{ isOnline(m.lastSeenAt) ? 'Online' : 'Offline' }}</Pill>
            </td>
            <td class="soft">{{ fmtTime(m.lastSeenAt) }}</td>
            <td>
              <div style="display:flex;gap:4px;flex-wrap:wrap">
                <button class="btn sm" @click="send(m.id, 'lock')">Lock</button>
                <button class="btn sm" @click="send(m.id, 'unlock')">Unlock</button>
                <button class="btn sm" @click="send(m.id, 'logoff')">Logoff</button>
                <button class="btn sm" @click="send(m.id, 'message')">Message</button>
                <button class="btn sm danger" @click="send(m.id, 'shutdown')">Shutdown</button>
                <button class="btn sm danger" @click="send(m.id, 'reboot')">Reboot</button>
                <button class="btn sm" @click="remove(m.id, m.name)">Delete</button>
              </div>
            </td>
          </tr>
          <tr v-if="machines.length === 0"><td colspan="5" class="empty">No machines registered</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
