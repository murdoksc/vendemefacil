import { FormEvent, useEffect, useState } from "react";
import {
  ArrowLeft,
  CreditCard,
  PauseCircle,
  PlayCircle,
  RefreshCw,
} from "lucide-react";
type Session = { accessToken: string; expiresAtUtc: string };
type Tenant = {
  id: string;
  name: string;
  slug: string;
  planCode: string;
  subscriptionStatus: string;
  trialEndsAtUtc: string;
  currentPeriodEndsAtUtc: string | null;
  subscriptionNotes: string | null;
  isActive: boolean;
};
type Detail = {
  tenant: Tenant;
  owner: { displayName: string; email: string } | null;
  payments: Array<{
    id: string;
    amount: number;
    paidAtUtc: string;
    method: string;
    reference: string | null;
  }>;
  history: Array<{
    id: string;
    type: string;
    description: string;
    createdAtUtc: string;
    performedBy: string | null;
  }>;
};
const key = "vendemefacil.platform-session",
  date = (value: string | null) =>
    value ? new Date(value).toISOString().slice(0, 10) : "",
  money = new Intl.NumberFormat("es-MX", {
    style: "currency",
    currency: "MXN",
  });
export function PlatformSubscriptionsPage({ onBack }: { onBack: () => void }) {
  const [session] = useState<Session | null>(() => {
      try {
        return JSON.parse(localStorage.getItem(key) ?? "null");
      } catch {
        return null;
      }
    }),
    [tenants, setTenants] = useState<Tenant[]>([]),
    [detail, setDetail] = useState<Detail | null>(null),
    [error, setError] = useState(""),
    [message, setMessage] = useState(""),
    [days, setDays] = useState(30),
    [busy, setBusy] = useState(false);
  async function request(path: string, options: RequestInit = {}) {
    if (!session) throw new Error("Inicia sesión primero en /administracion.");
    const response = await fetch(path, {
      ...options,
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${session.accessToken}`,
      },
    });
    if (!response.ok) throw new Error("No pudimos completar la operación.");
    return response.status === 204 ? null : response.json();
  }
  async function load() {
    try {
      const result = await request("/api/platform/dashboard");
      setTenants(result.tenants);
      if (detail) await open(detail.tenant.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error inesperado");
    }
  }
  async function open(id: string) {
    try {
      setDetail(await request(`/api/platform/tenants/${id}`));
      setError("");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error inesperado");
    }
  }
  useEffect(() => {
    void load();
  }, []);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!detail) return;
    const form = new FormData(event.currentTarget),
      tenant = detail.tenant;
    await request(`/api/platform/tenants/${tenant.id}/subscription`, {
      method: "PUT",
      body: JSON.stringify({
        planCode: form.get("planCode"),
        status: form.get("status"),
        trialEndsAtUtc: new Date(String(form.get("trial"))).toISOString(),
        currentPeriodEndsAtUtc: form.get("period")
          ? new Date(String(form.get("period"))).toISOString()
          : null,
        notes: form.get("notes"),
        isActive: tenant.isActive,
      }),
    });
    await load();
  }
  async function payment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!detail) return;
    const form = new FormData(event.currentTarget);
    await request(`/api/platform/tenants/${detail.tenant.id}/payments`, {
      method: "POST",
      body: JSON.stringify({
        amount: Number(form.get("amount")),
        method: form.get("method"),
        reference: form.get("reference"),
        paidAtUtc: new Date(String(form.get("paid"))).toISOString(),
        periodStartsAtUtc: new Date(String(form.get("start"))).toISOString(),
        periodEndsAtUtc: new Date(String(form.get("end"))).toISOString(),
        notes: form.get("paymentNotes"),
      }),
    });
    event.currentTarget.reset();
    await load();
  }
  async function adjust(action: "add-days" | "suspend" | "activate") {
    if (!detail || busy) return;
    if (
      action === "suspend" &&
      !window.confirm(`¿Suspender el acceso de ${detail.tenant.name}?`)
    )
      return;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await request(
        `/api/platform/tenants/${detail.tenant.id}/subscription/adjust`,
        {
          method: "POST",
          body: JSON.stringify({
            action,
            days: action === "add-days" ? days : null,
          }),
        },
      );
      setMessage(
        action === "add-days"
          ? `Se agregaron ${days} días de vigencia.`
          : action === "suspend"
            ? "La membresía quedó suspendida."
            : "La membresía quedó activa.",
      );
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error inesperado");
    } finally {
      setBusy(false);
    }
  }
  return (
    <main className="subscriptions-admin">
      <header>
        <button onClick={onBack}>
          <ArrowLeft />
          Panel general
        </button>
        <div>
          <p className="eyebrow">ADMINISTRACIÓN</p>
          <h1>Suscripciones y pagos</h1>
        </div>
        <button onClick={() => void load()}>
          <RefreshCw />
          Actualizar
        </button>
      </header>
      {error && <div className="admin-error">{error}</div>}
      {message && <div className="form-success subscription-admin-message">{message}</div>}
      <div className="subscriptions-layout">
        <aside>
          {tenants.map((x) => (
            <button
              className={detail?.tenant.id === x.id ? "active" : ""}
              key={x.id}
              onClick={() => void open(x.id)}
            >
              <strong>{x.name}</strong>
              <small>
                {x.planCode} · {x.subscriptionStatus}
              </small>
            </button>
          ))}
        </aside>
        <section>
          {detail ? (
            <>
              <div className="subscription-owner">
                <h2>{detail.tenant.name}</h2>
                <p>
                  {detail.owner?.displayName} · {detail.owner?.email}
                </p>
              </div>
              <div className="subscription-quick-actions">
                <div>
                  <strong>Agregar vigencia</strong>
                  <span>
                    <input aria-label="Días por agregar" type="number" min="1" max="3650" value={days} onChange={(event) => setDays(Number(event.target.value))} />
                    <button disabled={busy} onClick={() => void adjust("add-days")}>Agregar días</button>
                  </span>
                  <small>Se suma al fin de la prueba o del periodo actual.</small>
                </div>
                <button className="activate" disabled={busy || detail.tenant.isActive} onClick={() => void adjust("activate")}><PlayCircle /> Activar</button>
                <button className="suspend" disabled={busy || !detail.tenant.isActive} onClick={() => void adjust("suspend")}><PauseCircle /> Suspender</button>
              </div>
              <form className="subscription-admin-form" onSubmit={save}>
                <h3>Plan y vigencia</h3>
                <label>
                  Plan
                  <select name="planCode" defaultValue={detail.tenant.planCode}>
                    <option value="esencial">Esencial</option>
                    <option value="negocio">Negocio</option>
                    <option value="pro">Pro</option>
                  </select>
                </label>
                <label>
                  Estado
                  <select
                    name="status"
                    defaultValue={detail.tenant.subscriptionStatus}
                  >
                    <option value="Trial">Prueba</option>
                    <option value="Active">Activo</option>
                    <option value="PastDue">Pago vencido</option>
                    <option value="Suspended">Suspendido</option>
                    <option value="Cancelled">Cancelado</option>
                  </select>
                </label>
                <label>
                  Fin de prueba
                  <input
                    name="trial"
                    type="date"
                    required
                    defaultValue={date(detail.tenant.trialEndsAtUtc)}
                  />
                </label>
                <label>
                  Fin del periodo
                  <input
                    name="period"
                    type="date"
                    defaultValue={date(detail.tenant.currentPeriodEndsAtUtc)}
                  />
                </label>
                <label className="wide">
                  Notas
                  <textarea
                    name="notes"
                    defaultValue={detail.tenant.subscriptionNotes ?? ""}
                  />
                </label>
                <button>Guardar cambios</button>
              </form>
              <form className="subscription-admin-form" onSubmit={payment}>
                <h3>
                  <CreditCard />
                  Registrar pago
                </h3>
                <label>
                  Importe
                  <input
                    name="amount"
                    type="number"
                    min="1"
                    step=".01"
                    required
                  />
                </label>
                <label>
                  Método
                  <select name="method">
                    <option>Transferencia</option>
                    <option>Efectivo</option>
                    <option>Tarjeta</option>
                    <option>Otro</option>
                  </select>
                </label>
                <label>
                  Fecha de pago
                  <input name="paid" type="date" required />
                </label>
                <label>
                  Referencia
                  <input name="reference" />
                </label>
                <label>
                  Inicio del periodo
                  <input name="start" type="date" required />
                </label>
                <label>
                  Fin del periodo
                  <input name="end" type="date" required />
                </label>
                <label className="wide">
                  Notas
                  <input name="paymentNotes" />
                </label>
                <button>Registrar pago</button>
              </form>
              <div className="subscription-admin-history">
                <h3>Historial</h3>
                {detail.history.map((x) => (
                  <article key={x.id}>
                    <strong>{x.type}</strong>
                    <span>{x.description}</span>
                    <small>
                      {new Date(x.createdAtUtc).toLocaleString("es-MX")} ·{" "}
                      {x.performedBy}
                    </small>
                  </article>
                ))}
              </div>
              <div className="subscription-admin-history">
                <h3>Pagos</h3>
                {detail.payments.map((x) => (
                  <article key={x.id}>
                    <strong>{money.format(x.amount)}</strong>
                    <span>
                      {x.method} · {x.reference ?? "Sin referencia"}
                    </span>
                    <small>
                      {new Date(x.paidAtUtc).toLocaleDateString("es-MX")}
                    </small>
                  </article>
                ))}
              </div>
            </>
          ) : (
            <p>Selecciona un negocio para administrar su suscripción.</p>
          )}
        </section>
      </div>
    </main>
  );
}
