import { ref } from 'vue'
import { clearToken, getToken, setToken } from '@/api/client'

export interface LoginResult {
  token: string
}

const authed = ref(!!getToken())

export function useAuth() {
  async function login(password: string): Promise<void> {
    const res = await fetch('/api/admin/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ password })
    })
    if (!res.ok) throw new Error('Wrong password')
    const data = (await res.json()) as LoginResult
    setToken(data.token)
    authed.value = true
  }

  function logout(): void {
    clearToken()
    authed.value = false
  }

  return { authed, login, logout }
}
