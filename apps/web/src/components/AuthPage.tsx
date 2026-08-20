import { ArrowLeft, Check, Eye, EyeOff, LockKeyhole, Mail, Store } from "lucide-react";
import { FormEvent, useState } from "react";
import { apiRequest, AuthSession, saveSession } from "../lib/api";

type AuthMode = "login" | "register" | "forgot" | "reset";

function resetTokenFromLocation() {
  if (window.location.pathname !== "/reset-password") return "";
  const params = new URLSearchParams(window.location.hash.slice(1));
  return params.get("token") ?? "";
}

export function AuthPage({ onAuthenticated, initialMode = "login", onBack }: { onAuthenticated: (session: AuthSession) => void; initialMode?: "login" | "register"; onBack?: () => void }) {
  const [resetToken] = useState(resetTokenFromLocation);
  const [mode, setMode] = useState<AuthMode>(() => resetTokenFromLocation() ? "reset" : initialMode);
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  function changeMode(nextMode: AuthMode) {
    setMode(nextMode);
    setError("");
    setMessage("");
  }

  async function submitAccess(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    const data = new FormData(event.currentTarget);
    try {
      const body = mode === "register"
        ? { businessName: data.get("businessName"), ownerName: data.get("ownerName"), email: data.get("email"), password: data.get("password") }
        : { businessSlug: data.get("businessSlug"), email: data.get("email"), password: data.get("password") };
      const session = await apiRequest<AuthSession>(mode === "register" ? "/api/auth/register" : "/api/auth/login", { method: "POST", body: JSON.stringify(body) });
      saveSession(session);
      onAuthenticated(session);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No pudimos completar la operación.");
    } finally {
      setBusy(false);
    }
  }

  async function requestReset(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    setMessage("");
    const data = new FormData(event.currentTarget);
    try {
      const response = await apiRequest<{ message: string }>("/api/auth/forgot-password", {
        method: "POST",
        body: JSON.stringify({ businessSlug: data.get("businessSlug"), email: data.get("email") }),
      });
      setMessage(response.message);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No pudimos completar la operación.");
    } finally {
      setBusy(false);
    }
  }

  async function resetPassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    const data = new FormData(event.currentTarget);
    try {
      await apiRequest<void>("/api/auth/reset-password", {
        method: "POST",
        body: JSON.stringify({ token: resetToken, newPassword: data.get("newPassword"), confirmPassword: data.get("confirmPassword") }),
      });
      window.history.replaceState({}, "", "/");
      changeMode("login");
      setMessage("Contraseña actualizada. Ya puedes iniciar sesión.");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No pudimos cambiar la contraseña.");
    } finally {
      setBusy(false);
    }
  }

  const title = mode === "register" ? "Crea tu negocio" : mode === "forgot" ? "Recupera tu acceso" : mode === "reset" ? "Crea una contraseña" : "Inicia sesión";

  return <main className="auth-page">
    <section className="auth-brand-panel">
      <div className="auth-brand"><div className="brand-mark">VF</div><strong>Véndeme Fácil</strong></div>
      <div className="auth-message"><span className="auth-kicker">TU NEGOCIO EN ORDEN</span><h1>Vende fácil.<br />Controla todo.</h1><p>Inventario, ventas y caja en un solo lugar. Sin complicaciones.</p></div>
      <div className="auth-benefits"><span><Check />Configuración rápida</span><span><Check />Funciona en cualquier dispositivo</span><span><Check />Tus datos siempre separados y seguros</span></div>
    </section>
    <section className="auth-form-panel">
      {onBack && <button className="demo-back" onClick={onBack}><ArrowLeft /> Volver al inicio</button>}
      <form className="auth-form" onSubmit={mode === "forgot" ? requestReset : mode === "reset" ? resetPassword : submitAccess}>
        <p className="eyebrow">{mode === "forgot" || mode === "reset" ? "RECUPERACIÓN SEGURA" : mode === "register" ? "EMPECEMOS" : "BIENVENIDO DE NUEVO"}</p>
        <h2>{title}</h2>
        <p>{mode === "forgot" ? "Te enviaremos un enlace válido durante 30 minutos." : mode === "reset" ? "Elige una nueva contraseña para tu cuenta." : mode === "register" ? "Tu primera sucursal estará lista en menos de un minuto." : "Ingresa para continuar con tu negocio."}</p>

        {mode === "register" && <><label>Nombre del negocio<div className="input-with-icon"><Store /><input name="businessName" required placeholder="Mi tienda" /></div></label><label>Tu nombre<div className="input-with-icon"><Store /><input name="ownerName" required placeholder="Nombre del propietario" /></div></label></>}
        {(mode === "login" || mode === "forgot") && <label>Identificador del negocio<div className="input-with-icon"><Store /><input name="businessSlug" required placeholder="mi-tienda" autoComplete="organization" /></div></label>}
        {mode !== "reset" && <label>Correo electrónico<div className="input-with-icon"><Mail /><input name="email" type="email" required placeholder="tu@negocio.com" autoComplete="email" /></div></label>}
        {(mode === "login" || mode === "register") && <label>Contraseña<div className="input-with-icon"><LockKeyhole /><input name="password" type={showPassword ? "text" : "password"} required minLength={8} placeholder="Mínimo 8 caracteres" autoComplete={mode === "register" ? "new-password" : "current-password"} /><button type="button" onClick={() => setShowPassword(value => !value)} aria-label="Mostrar contraseña">{showPassword ? <EyeOff /> : <Eye />}</button></div></label>}
        {mode === "reset" && <><label>Nueva contraseña<div className="input-with-icon"><LockKeyhole /><input name="newPassword" type="password" required minLength={8} autoComplete="new-password" /></div></label><label>Confirma la contraseña<div className="input-with-icon"><LockKeyhole /><input name="confirmPassword" type="password" required minLength={8} autoComplete="new-password" /></div></label></>}

        {error && <div className="form-error" role="alert">{error}</div>}
        {message && <div className="form-success" role="status">{message}</div>}
        <button className="auth-submit" disabled={busy || (mode === "reset" && !resetToken)}>{busy ? "Procesando..." : mode === "forgot" ? "Enviar enlace" : mode === "reset" ? "Cambiar contraseña" : mode === "register" ? "Crear mi negocio" : "Entrar a Véndeme Fácil"}</button>
        {mode === "login" && <button type="button" className="auth-recovery" onClick={() => changeMode("forgot")}>¿Olvidaste tu contraseña?</button>}
        <div className="auth-switch">{mode === "register" ? <>¿Ya tienes una cuenta? <button type="button" onClick={() => changeMode("login")}>Inicia sesión</button></> : mode === "login" ? <>¿Aún no tienes una cuenta? <button type="button" onClick={() => changeMode("register")}>Crea tu negocio</button></> : <button type="button" onClick={() => { window.history.replaceState({}, "", "/"); changeMode("login"); }}>Volver al inicio de sesión</button>}</div>
      </form>
    </section>
  </main>;
}
