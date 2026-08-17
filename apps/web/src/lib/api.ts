export type UserSession = {
  id: string
  tenantId: string
  businessName: string
  businessSlug: string
  displayName: string
  email: string
  role: string
  canViewCosts?: boolean
}

export type AuthSession = {
  accessToken: string
  expiresAtUtc: string
  user: UserSession
}

export type ApiProduct = {
  id: string
  name: string
  category: string | null
  categoryId: string | null
  imageUrl: string | null
  variantId: string
  variant: string
  sku: string
  barcode: string | null
  cost: number
  price: number
  stock: number
  minimumStock: number
  isActive: boolean
}

const sessionKey = 'vendemefacil.session'

export function loadSession(): AuthSession | null {
  try {
    const session = JSON.parse(localStorage.getItem(sessionKey) ?? 'null') as AuthSession | null
    if (!session || new Date(session.expiresAtUtc) <= new Date()) {
      localStorage.removeItem(sessionKey)
      return null
    }
    return session
  } catch {
    localStorage.removeItem(sessionKey)
    return null
  }
}

export function saveSession(session: AuthSession) { localStorage.setItem(sessionKey, JSON.stringify(session)) }
export function clearSession() { localStorage.removeItem(sessionKey) }

async function readError(response: Response) {
  if (response.status === 401) return 'Los datos de acceso no son correctos.'
  try {
    const problem = await response.json()
    if (problem.errors) return Object.values(problem.errors).flat().join(' ')
    return problem.detail || problem.title || 'No pudimos completar la operación.'
  } catch { return 'No pudimos comunicarnos con el servidor.' }
}

export async function apiRequest<T>(path: string, options: RequestInit = {}, session?: AuthSession | null): Promise<T> {
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  const response = await fetch(path, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      'X-Time-Zone': timeZone,
      ...(session ? { Authorization: `Bearer ${session.accessToken}` } : {}),
      ...options.headers,
    },
  })
  if (!response.ok) throw new Error(await readError(response))
  if (response.status === 204 || response.headers.get('content-length') === '0') return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}
