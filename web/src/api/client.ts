const TOKEN_KEY = 'tb_token'

export const getToken = (): string => localStorage.getItem(TOKEN_KEY) || ''
export const setToken = (token: string): void => localStorage.setItem(TOKEN_KEY, token)
export const clearToken = (): void => localStorage.removeItem(TOKEN_KEY)

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const token = getToken()
  const res = await fetch('/api/admin/' + path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: 'Bearer ' + token } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  })
  if (res.status === 401) {
    clearToken()
    window.dispatchEvent(new Event('tb:unauthorized'))
    throw new Error('unauthorized')
  }
  if (!res.ok) {
    let message = res.statusText
    try {
      const data = (await res.json()) as { reason?: string; title?: string }
      message = data.reason || data.title || message
    } catch {
      /* ignore */
    }
    throw new Error(message)
  }
  return (await res.json()) as T
}

export const api = {
  get: <T>(path: string): Promise<T> => request<T>('GET', path),
  post: <T>(path: string, body?: unknown): Promise<T> => request<T>('POST', path, body),
  put: <T>(path: string, body?: unknown): Promise<T> => request<T>('PUT', path, body),
  del: <T>(path: string): Promise<T> => request<T>('DELETE', path)
}
