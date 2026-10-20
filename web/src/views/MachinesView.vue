<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { api } from '@/api/client'
import type { CreateMachineResult, MachineDetail } from '@/api/types'
import { fmtTime, isOnline } from '@/utils/format'

const machines = ref<MachineDetail[]>([])

async function load(): Promise<void> {
  machines.value = await api.get<MachineDetail[]>('machines')
}

async function send(id: number, action: string): Promise<void> {
  let text: string | undefined
  try {
    if (action === 'message') {
      const { value } = await ElMessageBox.prompt('Message content:', 'Send message', {
        confirmButtonText: 'Send',
        cancelButtonText: 'Cancel'
      })
      if (!value) return
      text = value
    }
    if (action === 'shutdown' || action === 'reboot') {
      await ElMessageBox.confirm(`Really ${action} this machine?`, 'Confirm', {
        type: 'warning',
        confirmButtonText: 'Yes',
        cancelButtonText: 'Cancel'
      })
    }
  } catch {
    return
  }
  try {
    const r = await api.post<{ ok: boolean; reason?: string }>(`machines/${id}/command`, { action, text })
    if (r.ok) ElMessage.success('Command queued (applies on next heartbeat)')
    else ElMessage.warning(r.reason || 'Failed')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : 'Failed')
  }
}

async function register(): Promise<void> {
  let name = ''
  try {
    const { value } = await ElMessageBox.prompt('Machine name (e.g. PC-1):', 'Register machine', {
      confirmButtonText: 'Create',
      cancelButtonText: 'Cancel'
    })
    name = (value || '').trim()
  } catch {
    return
  }
  if (!name) return
  try {
    const r = await api.post<CreateMachineResult>('machines', { name })
    if (r.ok) {
      await ElMessageBox.alert(
        `Machine created: id=${r.id}\nSecret: ${r.secret}\n\nUse with install-client.ps1 -MachineId/-Secret`,
        'Machine registered',
        { confirmButtonText: 'OK' }
      )
    }
    await load()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : 'Failed')
  }
}

async function remove(id: number, name: string): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `Delete machine "${name}"?\nIts session history and commands will be removed (irreversible).`,
      'Delete machine',
      { type: 'warning', confirmButtonText: 'Delete', cancelButtonText: 'Cancel' }
    )
  } catch {
    return
  }
  try {
    const r = await api.del<{ ok: boolean; reason?: string }>(`machines/${id}`)
    if (r.ok) ElMessage.success('Deleted')
    else ElMessage.warning(r.reason || 'Failed')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : 'Failed')
  }
  await load()
}

onMounted(load)
</script>

<template>
  <div style="margin-bottom: 16px">
    <el-button type="primary" :icon="Plus" @click="register">Register machine</el-button>
  </div>

  <el-card class="wb-card">
    <el-table :data="machines" style="width: 100%">
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="name" label="Name" min-width="140" show-overflow-tooltip>
        <template #default="{ row }"><b>{{ row.name }}</b></template>
      </el-table-column>
      <el-table-column label="Status" width="100">
        <template #default="{ row }">
          <el-tag :type="isOnline(row.lastSeenAt) ? 'success' : 'info'" size="small" effect="light">
            {{ isOnline(row.lastSeenAt) ? 'Online' : 'Offline' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Last heartbeat" width="170">
        <template #default="{ row }"><span class="muted">{{ fmtTime(row.lastSeenAt) }}</span></template>
      </el-table-column>
      <el-table-column label="Actions" width="430">
        <template #default="{ row }">
          <div class="row-actions">
            <el-button size="small" @click="send(row.id, 'lock')">Lock</el-button>
            <el-button size="small" @click="send(row.id, 'unlock')">Unlock</el-button>
            <el-button size="small" @click="send(row.id, 'logoff')">Logoff</el-button>
            <el-button size="small" @click="send(row.id, 'message')">Message</el-button>
            <el-button size="small" type="danger" plain @click="send(row.id, 'shutdown')">Shutdown</el-button>
            <el-button size="small" type="danger" plain @click="send(row.id, 'reboot')">Reboot</el-button>
            <el-button size="small" type="danger" text @click="remove(row.id, row.name)">Delete</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!machines.length" description="No machines registered" :image-size="60" />
  </el-card>
</template>
