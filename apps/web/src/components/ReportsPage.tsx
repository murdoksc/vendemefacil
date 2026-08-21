import { AlertTriangle, Banknote, BarChart3, CreditCard, Landmark, PackageOpen, RefreshCw, TrendingUp, LockKeyhole } from "lucide-react";
import { FormEvent, useEffect, useMemo, useState } from "react";
import { apiRequest, AuthSession } from "../lib/api";
import { usePlanAccess, showUpgradeRequired } from "./PlanAccess";

type Report = { grossSales: number; knownCost: number; estimatedProfit: number | null; transactions: number; averageTicket: number; itemsWithPendingCost: number; dailySales: { date: string; sales: number; transactions: number }[]; payments: { method: string; total: number; transactions: number }[]; topProducts: { productVariantId: string; product: string; variant: string; sku: string; quantity: number; sales: number; estimatedProfit: number | null; costPending: boolean }[] };
const money = new Intl.NumberFormat("es-MX", { style: "currency", currency: "MXN" });
const localIso = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;

export function ReportsPage({ session }: { session: AuthSession }) {
  const planAccess = usePlanAccess();
  const hasFullReports = planAccess.subscription?.capabilities.fullReports ?? false;
  
  const [from, setFrom] = useState(localIso(new Date(Date.now() - 29 * 86400000)));
  const [to, setTo] = useState(localIso(new Date()));
  const [report, setReport] = useState<Report | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [loading, setLoading] = useState(false);

  async function load() {
    setLoading(true); setError("");
    try { setReport(await apiRequest<Report>(`/api/v1/reports/sales?from=${from}&to=${to}`, {}, session)); }
    catch (e) { setError(e instanceof Error ? e.message : "No pudimos generar el reporte."); }
    finally { setLoading(false); }
  }

  async function backfillCosts() {
    setLoading(true); setError(""); setSuccess("");
    try {
      const result = await apiRequest<{ updatedLines: number; remainingLines: number }>("/api/v1/reports/sales/backfill-costs", { method: "POST" }, session);
      setSuccess(result.updatedLines ? `Se completó el costo de ${result.updatedLines} partida${result.updatedLines === 1 ? "" : "s"} histórica${result.updatedLines === 1 ? "" : "s"}.` : "No se encontraron costos actuales para completar.");
      if (result.remainingLines) setSuccess((message) => `${message} Quedan ${result.remainingLines} partidas cuyo producto todavía tiene costo $0.`);
      await load();
    } catch (e) { setError(e instanceof Error ? e.message : "No pudimos completar los costos pendientes."); }
    finally { setLoading(false); }
  }

  useEffect(() => { void load(); }, []);
  function submit(e: FormEvent) { e.preventDefault(); void load(); }

  const max = useMemo(() => Math.max(1, ...(report?.dailySales.map((x) => x.sales) ?? [])), [report]);
  const icon = (method: string) => method === "Cash" ? <Banknote /> : method === "Card" ? <CreditCard /> : <Landmark />;
  const methodName = (method: string) => method === "Cash" ? "Efectivo" : method === "Card" ? "Tarjeta" : "Transferencia";

  const renderUpgradeOverlay = (title: string, desc: string, featureLabel: string) => (
    <div style={{
      position: "absolute",
      top: 0, left: 0, right: 0, bottom: 0,
      background: "rgba(255, 255, 255, 0.9)",
      backdropFilter: "blur(6px)",
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      justifyContent: "center",
      textAlign: "center",
      padding: "20px",
      zIndex: 10,
      borderRadius: "var(--app-radius, 12px)"
    }}>
      <span style={{
        width: "44px",
        height: "44px",
        borderRadius: "50%",
        background: "var(--brand-primary, #123f35)",
        color: "#fff",
        display: "grid",
        placeItems: "center",
        marginBottom: "12px",
        boxShadow: "0 4px 10px rgba(0,0,0,0.15)"
      }}>
        <LockKeyhole size={18} />
      </span>
      <h3 style={{ margin: "0 0 6px 0", fontSize: "1.05em", fontWeight: 700, color: "var(--app-text, #17362e)" }}>{title}</h3>
      <p style={{ margin: "0 0 14px 0", fontSize: "0.85em", color: "var(--text-muted, #6d7e77)", maxWidth: "270px", lineHeight: "1.3" }}>{desc}</p>
      <button className="button primary btn-sm" style={{ padding: "6px 14px", fontSize: "0.8em", height: "auto" }} onClick={() => showUpgradeRequired(featureLabel)}>
        Desbloquear con plan Negocio
      </button>
    </div>
  );

  return <div className="content reports-page">
    <section className="page-title-row"><div><p className="eyebrow">ANÁLISIS</p><h1>Reportes</h1><p>Conoce el desempeño real de tu negocio.</p></div><form className="date-filter" onSubmit={submit}><label>Desde<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label><label>Hasta<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label><button className="button primary">{loading ? "Calculando..." : "Aplicar"}</button></form></section>
    {error && <div className="page-error">{error}</div>}{success && <div className="form-success">{success}</div>}
    
    {report && <><section className="report-metrics">
      <article><span><TrendingUp /></span><div><small>Ventas del periodo</small><strong>{money.format(report.grossSales)}</strong><em>{report.transactions} transacciones</em></div></article>
      <article><span><Banknote /></span><div><small>Ticket promedio</small><strong>{money.format(report.averageTicket)}</strong><em>Por transacción</em></div></article>
      
      {hasFullReports ? (
        <article className={report.estimatedProfit === null ? "pending-profit" : ""}>
          <span>{report.estimatedProfit === null ? <AlertTriangle /> : <BarChart3 />}</span>
          <div>
            <small>Utilidad estimada</small>
            <strong>{report.estimatedProfit === null ? "No calculable" : money.format(report.estimatedProfit)}</strong>
            <em>{report.itemsWithPendingCost ? `${report.itemsWithPendingCost} partidas sin costo` : `Costo: ${money.format(report.knownCost)}`}</em>
            {report.itemsWithPendingCost > 0 && <button className="text-button backfill-costs" disabled={loading} onClick={() => void backfillCosts()}><RefreshCw /> Completar costos pendientes</button>}
          </div>
        </article>
      ) : (
        <article className="pending-profit" style={{ border: "1px dashed var(--border, #d6e0da)", opacity: 0.9 }}>
          <span style={{ background: "var(--brand-primary, #123f35)", color: "#fff" }}><LockKeyhole /></span>
          <div>
            <small>Utilidad estimada</small>
            <strong style={{ filter: "blur(3.5px)", userSelect: "none" }}>$9,999.00</strong>
            <button className="text-button backfill-costs" style={{ display: "inline-flex", alignItems: "center", gap: 4, fontWeight: 700 }} onClick={() => showUpgradeRequired("Utilidad Estimada")}>
              🔓 Desbloquear en plan Negocio
            </button>
          </div>
        </article>
      )}
    </section>
    
    <section className="report-grid">
      <article className="card report-chart" style={{ position: "relative" }}>
        {!hasFullReports && renderUpgradeOverlay(
          "Gráfico de Tendencias",
          "Visualiza las ventas diarias para identificar qué días de la semana son tus picos de venta.",
          "Ventas por día"
        )}
        <div style={{ filter: !hasFullReports ? "blur(4.5px)" : "none", pointerEvents: !hasFullReports ? "none" : "auto", userSelect: !hasFullReports ? "none" : "auto" }}>
          <div className="card-heading"><div><p className="eyebrow">TENDENCIA</p><h2>Ventas por día</h2></div></div>
          <div className="report-bars">{!report.dailySales.length ? <div className="empty-state"><BarChart3 /><strong>Sin ventas en este periodo</strong></div> : report.dailySales.map((x) => <div className="report-bar" key={x.date} title={money.format(x.sales)}><div style={{ height: `${Math.max(4, x.sales / max * 100)}%` }} /><span>{new Date(`${x.date}T12:00:00`).toLocaleDateString("es-MX", { day: "2-digit", month: "short" })}</span></div>)}</div>
        </div>
      </article>
      
      <article className="card payment-report" style={{ position: "relative" }}>
        {!hasFullReports && renderUpgradeOverlay(
          "Desglose de Métodos de Pago",
          "Conoce exactamente cuánto cobraste en efectivo, tarjetas o transferencias para cuadrar caja sin errores.",
          "Métodos de pago"
        )}
        <div style={{ filter: !hasFullReports ? "blur(4.5px)" : "none", pointerEvents: !hasFullReports ? "none" : "auto", userSelect: !hasFullReports ? "none" : "auto" }}>
          <div className="card-heading"><div><p className="eyebrow">COBROS</p><h2>Métodos de pago</h2></div></div>
          <div>{!report.payments.length ? <div className="empty-state"><Banknote /><strong>Sin cobros</strong></div> : report.payments.map((x) => <div className="payment-report-row" key={x.method}><span>{icon(x.method)}</span><div><strong>{methodName(x.method)}</strong><small>{x.transactions} transacciones</small></div><b>{money.format(x.total)}</b></div>)}</div>
        </div>
      </article>
    </section>
    
    <section className="card top-products" style={{ position: "relative" }}>
      {!hasFullReports && renderUpgradeOverlay(
        "Productos más vendidos",
        "Conoce la rotación de tus artículos, ventas brutas y utilidad individual para comprar con inteligencia.",
        "Productos más vendidos"
      )}
      <div style={{ filter: !hasFullReports ? "blur(4.5px)" : "none", pointerEvents: !hasFullReports ? "none" : "auto", userSelect: !hasFullReports ? "none" : "auto" }}>
        <div className="card-heading"><div><p className="eyebrow">PRODUCTOS</p><h2>Más vendidos</h2></div></div>
        <div className="top-head"><span>Producto</span><span>Unidades</span><span>Ventas</span><span>Utilidad</span></div>{!report.topProducts.length ? <div className="empty-state"><PackageOpen /><strong>Sin productos vendidos</strong></div> : report.topProducts.map((x, i) => <div className="top-row" key={x.productVariantId}><div><b>{i + 1}</b><span><strong>{x.product}</strong><small>{x.variant} · {x.sku}</small></span></div><strong>{x.quantity}</strong><strong>{money.format(x.sales)}</strong><strong className={x.costPending ? "cost-warning" : ""}>{x.costPending ? "Pendiente" : money.format(x.estimatedProfit!)}</strong></div>)}
      </div>
    </section></>}
  </div>;
}
