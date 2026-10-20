import { ref } from 'vue'
import { api } from '@/api/client'
import type { OverviewData, SessionRecord, TimeRequest } from '@/api/types'

// 共享仪表盘数据：App.vue 每 5s 轮询一次，侧边栏徽标/总览页共用
const overview = ref<OverviewData | null>(null)
const recentSessions = ref<SessionRecord[]>([])
const pendingPreview = ref<TimeRequest[]>([])
let inflight = false

async function refresh(): Promise<void> {
  if (inflight) return
  inflight = true
  try {
    const [o, sessions, pending] = await Promise.all([
      api.get<OverviewData>('overview'),
      api.get<SessionRecord[]>('sessions?limit=5'),
      api.get<TimeRequest[]>('requests?status=pending&limit=3')
    ])
    overview.value = o
    recentSessions.value = sessions
    pendingPreview.value = pending
  } catch {
    /* 401 由客户端统一事件处理；其余错误静默，等待下轮 */
  } finally {
    inflight = false
  }
}

export function useDashboard() {
  return { overview, recentSessions, pendingPreview, refresh }
}
