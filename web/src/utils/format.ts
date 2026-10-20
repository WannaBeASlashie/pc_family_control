export type TagType = 'success' | 'danger' | 'warning' | 'primary' | 'info'

export function fmtSec(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return '-'
  const s = Math.max(0, Math.floor(seconds))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  return h > 0 ? `${h}h ${m}m` : `${m}m ${s % 60}s`
}

export function fmtTime(iso: string | null | undefined): string {
  if (!iso) return '-'
  return new Date(iso).toLocaleString('en-GB', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  })
}

export function reasonType(reason: string | null | undefined): TagType {
  if (!reason) return 'info'
  if (reason.startsWith('offline')) return 'warning'
  if (['exhausted', 'daily_quota', 'sliding_quota', 'curfew', 'session_limit', 'machine_off'].includes(reason)) {
    return 'danger'
  }
  if (reason === 'logout') return 'info'
  return 'primary'
}

export function parseWindows(json: string): { start: string; end: string }[] {
  try {
    const parsed = JSON.parse(json) as { start: string; end: string }[]
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

/** 在线判定：最近 30 秒内有心跳（与服务端 overview 的口径一致） */
export function isOnline(lastSeenAt: string | null | undefined): boolean {
  if (!lastSeenAt) return false
  return Date.now() - new Date(lastSeenAt).getTime() < 30_000
}
