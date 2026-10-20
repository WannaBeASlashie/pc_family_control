export interface Person {
  id: number
  cardNo: string
  displayName: string
  enabled: boolean
  balanceSeconds: number
  points: number
  policyId: number
  createdAt?: string
}

export interface MachineInfo {
  id: number
  name: string
  lastSeenAt: string | null
  online: boolean
}

export interface MachineDetail {
  id: number
  name: string
  secret: string
  lastSeenAt: string | null
  createdAt?: string
}

export interface ActiveSession {
  id: number
  person: string
  machine: string
  startedAt: string
  chargedSeconds: number
}

export interface OverviewData {
  machines: MachineInfo[]
  activeSessions: ActiveSession[]
  persons: Person[]
  pendingRequests: number
}

export interface TimeRequest {
  id: number
  person: string
  personId: number
  machineId: number
  minutes: number
  reason: string | null
  status: string
  createdAt: string
  decidedAt: string | null
}

export interface PolicyWindow {
  start: string
  end: string
}

export interface Policy {
  id: number
  name: string
  weekdayQuotaSeconds: number
  weekendQuotaSeconds: number
  maxSessionSeconds: number
  slidingWindowSeconds: number
  slidingQuotaSeconds: number
  offlineQuotaSeconds: number | null
  allowedWindowsJson: string
  enabled: boolean
}

export interface PolicyPayload {
  name: string
  weekdayQuotaMinutes: number
  weekendQuotaMinutes: number
  maxSessionMinutes: number
  slidingWindowMinutes: number
  slidingQuotaMinutes: number
  offlineQuotaMinutes: number | null
  enabled: boolean
  windows: PolicyWindow[]
}

export interface SessionRecord {
  id: number
  person: string
  machine: string
  startedAt: string
  endedAt: string | null
  chargedSeconds: number
  status: string
  endReason: string | null
}

export interface BalanceResult {
  ok: boolean
  balanceSeconds?: number
  reason?: string
}

export interface CreateMachineResult {
  ok: boolean
  id: number
  secret: string
}

export interface OkResult {
  ok: boolean
  reason?: string
}
