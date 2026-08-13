import { Check, Eye, EyeOff, LockKeyhole, Mail, Store } from 'lucide-react'
import { FormEvent, useState } from 'react'
import { apiRequest, AuthSession, saveSession } from '../lib/api'

export function AuthPage({ onAuthenticated }: { onAuthenticated: (session: AuthSession) => void }) {
  const [registering, setRegistering] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    const data = new FormData(event.currentTarget)
    try {
      const body = registering
        ? { businessName: data.get('businessName'), ownerName: data.get('ownerName'), email: data.get('email'), password: data.get('password') }
        : { businessSlug: data.get('businessSlug'), email: data.get('email'), password: data.get('password') }
      const session = await apiRequest<AuthSession>(registering ? '/api/auth/register' : '/api/auth/login', { method: 'POST', body: JSON.stringify(body) })
      saveSession(session)
      onAuthenticated(session)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'No pudimos completar la operación.')
    } finally { setBusy(false) }
  }

  return <main className="auth-page">
    <section className="auth-brand-panel">
      <div className="auth-brand"><div className="brand-mark">VF</div><strong>Véndeme Fácil</strong></div>
      <div className="auth-message"><span className="auth-kicker">TU NEGOCIO EN ORDEN</span><h1>Vende fácil.<br />Controla todo.</h1><p>Inventario, ventas y caja en un solo lugar. Sin complicaciones.</p></div>
      <div className="auth-benefits"><span><Check />Configuración rápida</span><span><Check />Funciona en cualquier dispositivo</span><span><Check />Tus datos siempre separados y seguros</span></div>
    </section>
    <section className="auth-form-panel">
      <form className="auth-form" onSubmit={submit}>
        <p className="eyebrow">{registering ? 'EMPECEMOS' : 'BIENVENIDO DE NUEVO'}</p>
        <h2>{registering ? 'Crea tu negocio' : 'Inicia sesión'}</h2>
        <p>{registering ? 'Tu primera sucursal estará lista en menos de un minuto.' : 'Ingresa para continuar con tu negocio.'}</p>
        {registering && <><label>Nombre del negocio<div className="input-with-icon"><Store /><input name="businessName" required placeholder="Mi tienda" /></div></label><label>Tu nombre<div className="input-with-icon"><Store /><input name="ownerName" required placeholder="Nombre del propietario" /></div></label></>}
        {!registering && <label>Identificador del negocio<div className="input-with-icon"><Store /><input name="businessSlug" required placeholder="mi-tienda" /></div></label>}
        <label>Correo electrónico<div className="input-with-icon"><Mail /><input name="email" type="email" required placeholder="tu@negocio.com" /></div></label>
        <label>Contraseña<div className="input-with-icon"><LockKeyhole /><input name="password" type={showPassword ? 'text' : 'password'} required minLength={8} placeholder="Mínimo 8 caracteres" /><button type="button" onClick={() => setShowPassword(value => !value)} aria-label="Mostrar contraseña">{showPassword ? <EyeOff /> : <Eye />}</button></div></label>
        {error && <div className="form-error" role="alert">{error}</div>}
        <button className="auth-submit" disabled={busy}>{busy ? 'Conectando...' : registering ? 'Crear mi negocio' : 'Entrar a Véndeme Fácil'}</button>
        <div className="auth-switch">{registering ? '¿Ya tienes una cuenta?' : '¿Aún no tienes una cuenta?'} <button type="button" onClick={() => { setRegistering(value => !value); setError('') }}>{registering ? 'Inicia sesión' : 'Crea tu negocio'}</button></div>
      </form>
    </section>
  </main>
}
