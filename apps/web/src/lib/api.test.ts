import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest, clearSession, loadSession, saveSession, type AuthSession } from './api'

const session: AuthSession = {
  accessToken: 'token',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'u1', tenantId: 't1', businessName: 'Tienda', businessSlug: 'tienda', displayName: 'Ana', email: 'ana@example.com', role: 'Owner' },
}

const storage = (() => {
  let values: Record<string, string> = {}
  return {
    getItem: (key: string) => values[key] ?? null,
    setItem: (key: string, value: string) => { values[key] = value },
    removeItem: (key: string) => { delete values[key] },
    clear: () => { values = {} },
  }
})()

Object.defineProperty(globalThis, 'localStorage', { value: storage, configurable: true })

describe('API client', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it('persists and clears a valid session', () => {
    saveSession(session)
    expect(loadSession()).toEqual(session)
    clearSession()
    expect(loadSession()).toBeNull()
  })

  it('removes an expired session', () => {
    saveSession({ ...session, expiresAtUtc: '2000-01-01T00:00:00Z' })
    expect(loadSession()).toBeNull()
    expect(localStorage.getItem('vendemefacil.session')).toBeNull()
  })

  it('adds authentication and timezone headers', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    await apiRequest('/api/v1/example', {}, session)
    expect(fetch).toHaveBeenCalledWith('/api/v1/example', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer token', 'X-Time-Zone': expect.any(String) }),
    }))
  })

  it('surfaces validation problem messages', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { name: ['El nombre es obligatorio.'] } }), { status: 400 })))
    await expect(apiRequest('/api/v1/customers')).rejects.toThrow('El nombre es obligatorio.')
  })
})
