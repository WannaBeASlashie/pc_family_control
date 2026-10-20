<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { api } from '@/api/client'
import type { CreateMachineResult, MachineDetail } from '@/api/types'
import { fmtTime, isOnline } from '@/utils/format'

const { t } = useI18n()
const machines = ref<MachineDetail[]>([])

async function load(): Promise<void> {
  machines.value = await api.get<MachineDetail[]>('machines')
}

async function send(id: number, action: string): Promise<void> {
  let text: string | undefined
  try {
    if (action === 'message') {
      const { value } = await ElMessageBox.prompt(t('machines.messagePrompt'), t('machines.message'), {
        confirmButtonText: t('machines.send'),
        cancelButtonText: t('common.cancel')
      })
      if (!value) return
      text = value
    }
    if (action === 'shutdown' || action === 'reboot') {
      await ElMessageBox.confirm(
        action === 'shutdown' ? t('machines.confirmShutdown') : t('machines.confirmReboot'),
        action === 'shutdown' ? t('machines.shutdown') : t('machines.reboot'),
        { type: 'warning', confirmButtonText: t('common.approve'), cancelButtonText: t('common.cancel') }
      )
    }
  } catch {
    return
  }
  try {
    const r = await api.post<{ ok: boolean; reason?: string }>(`machines/${id}/command`, { action, text })
    if (r.ok) ElMessage.success(t('machines.commandQueued'))
    else ElMessage.warning(r.reason || t('common.failed'))
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : t('common.failed'))
  }
}

async function register(): Promise<void> {
  let name = ''
  try {
    const { value } = await ElMessageBox.prompt(t('machines.registerPrompt'), t('machines.registerTitle'), {
      confirmButtonText: t('common.create'),
      cancelButtonText: t('common.cancel')
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
        t('machines.createdMessage', { id: r.id, secret: r.secret }),
        t('machines.createdTitle'),
        { confirmButtonText: 'OK' }
      )
    }
    await load()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : t('common.failed'))
  }
}

async function remove(id: number, name: string): Promise<void> {
  try {
    await ElMessageBox.confirm(t('machines.deleteConfirm', { name }), t('machines.deleteTitle'), {
      type: 'warning',
      confirmButtonText: t('common.delete'),
      cancelButtonText: t('common.cancel')
    })
  } catch {
    return
  }
  try {
    const r = await api.del<{ ok: boolean; reason?: string }>(`machines/${id}`)
    if (r.ok) ElMessage.success(t('machines.deleted'))
    else ElMessage.warning(r.reason || t('common.failed'))
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : t('common.failed'))
  }
  await load()
}

onMounted(load)
</script>

<template>
  <div style="margin-bottom: 16px">
    <el-button type="primary" :icon="Plus" @click="register">{{ t('machines.register') }}</el-button>
  </div>

  <el-card class="wb-card">
    <el-table :data="machines" style="width: 100%">
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="name" :label="t('common.name')" min-width="140" show-overflow-tooltip>
        <template #default="{ row }"><b>{{ row.name }}</b></template>
      </el-table-column>
      <el-table-column :label="t('common.status')" width="100">
        <template #default="{ row }">
          <el-tag :type="isOnline(row.lastSeenAt) ? 'success' : 'info'" size="small" effect="light">
            {{ isOnline(row.lastSeenAt) ? t('machines.online') : t('machines.offline') }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="t('machines.lastHeartbeat')" width="170">
        <template #default="{ row }"><span class="muted">{{ fmtTime(row.lastSeenAt) }}</span></template>
      </el-table-column>
      <el-table-column :label="t('common.actions')" width="450">
        <template #default="{ row }">
          <div class="row-actions">
            <el-button size="small" @click="send(row.id, 'lock')">{{ t('machines.lock') }}</el-button>
            <el-button size="small" @click="send(row.id, 'unlock')">{{ t('machines.unlock') }}</el-button>
            <el-button size="small" @click="send(row.id, 'logoff')">{{ t('machines.logoff') }}</el-button>
            <el-button size="small" @click="send(row.id, 'message')">{{ t('machines.message') }}</el-button>
            <el-button size="small" type="danger" plain @click="send(row.id, 'shutdown')">{{ t('machines.shutdown') }}</el-button>
            <el-button size="small" type="danger" plain @click="send(row.id, 'reboot')">{{ t('machines.reboot') }}</el-button>
            <el-button size="small" type="danger" text @click="remove(row.id, row.name)">{{ t('common.delete') }}</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!machines.length" :description="t('machines.noMachines')" :image-size="60" />
  </el-card>
</template>
