import {
  Banknote,
  Eye,
  History,
  Printer,
  MessageCircle,
  Mail,
  ReceiptText,
  Search,
  X,
  XCircle,
} from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { apiRequest, AuthSession } from "../lib/api";
import { printReceipt } from "../lib/printReceipt";
import { emailDocument } from "../lib/emailDocument";
type Sale = {
  id: string;
  folio: string;
  soldAtUtc: string;
  status: string;
  total: number;
  itemCount: number;
};
type Detail = Sale & {
  customer: string;
  customerPhone: string | null;
  customerEmail: string | null;
  items: {
    id: string;
    productVariantId: string;
    productName: string;
    variantName: string;
    sku: string;
    quantity: number;
    returnedQuantity: number;
    availableToReturn: number;
    unitPrice: number;
    lineTotal: number;
  }[];
  payments: { method: string; receivedAmount: number; changeAmount: number }[];
};
type CashSummary = {
  id: string;
  openingAmount: number;
  openedAtUtc: string;
  expectedAmount: number;
  salesTotal: number;
  layawayTotal: number;
  transactions: number;
  layawayTransactions: number;
  payments: { method: string; total: number; transactions: number }[];
  salePayments: { method: string; total: number; transactions: number }[];
  layawayPayments: { method: string; total: number; transactions: number }[];
};
type Cut = {
  id: string;
  openedAtUtc: string;
  closedAtUtc: string | null;
  status: string;
  openingAmount: number;
  expectedAmount: number | null;
  countedAmount: number | null;
  differenceAmount: number | null;
  user: string;
  branch: string;
};
type CashReport = {
  id: string; openedAtUtc: string; closedAtUtc: string | null; openingAmount: number; expectedAmount: number; countedAmount: number | null; differenceAmount: number | null; user: string; branch: string; salesTotal: number; layawayTotal: number;
  salePayments: { method: string; total: number }[]; layawayPayments: { method: string; total: number }[];
  sales: { id: string; folio: string; soldAtUtc: string; status: string; total: number; items: { productName: string; variantName: string; sku: string; quantity: number; lineTotal: number }[] }[];
  layawayDetails: { id: string; layawayId: string; folio: string; customer: string; paidAtUtc: string; method: string; amount: number; note: string | null }[];
};
const money = new Intl.NumberFormat("es-MX", {
  style: "currency",
  currency: "MXN",
});
const method = (x: string) =>
  x === "Cash" ? "Efectivo" : x === "Card" ? "Tarjeta" : "Transferencia";
function CashReportReceipt({ report, businessName, logoUrl }: { report: CashReport; businessName: string; logoUrl?: string | null }) {
  return <div className="receipt cash-report-receipt" id="cash-report-receipt">
    <div className="receipt-brand">{logoUrl ? <img src={logoUrl} alt={businessName} /> : businessName.slice(0, 2).toUpperCase()}</div>
    <h2>{businessName}</h2><p>CORTE DE CAJA<br />{report.branch} · {report.user}<br />Apertura: {new Date(report.openedAtUtc).toLocaleString("es-MX")}<br />Cierre: {report.closedAtUtc ? new Date(report.closedAtUtc).toLocaleString("es-MX") : "Caja abierta"}</p>
    <div className="ticket-payment"><span>Fondo inicial</span><b>{money.format(report.openingAmount)}</b><span>Ventas</span><b>{money.format(report.salesTotal)}</b><span>Apartados</span><b>{money.format(report.layawayTotal)}</b></div>
    {report.salePayments.map((x) => <div className="ticket-line" key={`sale-${x.method}`}><span>Ventas · {method(x.method)}</span><b>{money.format(x.total)}</b></div>)}
    {report.layawayPayments.map((x) => <div className="ticket-line" key={`layaway-${x.method}`}><span>Apartados · {method(x.method)}</span><b>{money.format(x.total)}</b></div>)}
    <div className="ticket-total"><span>ESPERADO</span><b>{money.format(report.expectedAmount)}</b>{report.countedAmount !== null && <><span>CONTADO</span><b>{money.format(report.countedAmount)}</b><span>DIFERENCIA</span><b>{money.format(report.differenceAmount ?? 0)}</b></>}</div>
    <p>DETALLE DE VENTAS</p>
    {report.sales.length ? report.sales.map((sale) => <div key={sale.id}><div className="ticket-line"><span>{sale.folio}{sale.status === "Cancelled" ? " · CANCELADA" : ""}</span><b>{money.format(sale.total)}</b></div>{sale.items.map((item, index) => <div className="ticket-line" key={`${sale.id}-${index}`}><span>{item.quantity} × {item.productName}<small>{item.variantName} · {item.sku}</small></span><b>{money.format(item.lineTotal)}</b></div>)}</div>) : <p>Sin ventas</p>}
    <p>ANTICIPOS Y ABONOS</p>
    {report.layawayDetails.length ? report.layawayDetails.map((payment) => <div className="ticket-line" key={payment.id}><span>{payment.folio} · {payment.customer}<small>{method(payment.method)}{payment.note ? ` · ${payment.note}` : ""}</small></span><b>{money.format(payment.amount)}</b></div>) : <p>Sin movimientos de apartados</p>}
    <footer>Fin del corte</footer>
  </div>;
}
export function SalesPage({ session, businessName, logoUrl, ticketMessage }: { session: AuthSession; businessName?: string; logoUrl?: string | null; ticketMessage?: string | null }) {
  const [sales, setSales] = useState<Sale[]>([]),
    [cuts, setCuts] = useState<Cut[]>([]),
    [query, setQuery] = useState(""),
    [detail, setDetail] = useState<Detail | null>(null),
    [cash, setCash] = useState<CashSummary | null>(null),
    [cutReport, setCutReport] = useState<CashReport | null>(null),
    [showClose, setShowClose] = useState(false),
    [counted, setCounted] = useState(""),
    [result, setResult] = useState<{
      expectedAmount: number;
      countedAmount: number;
      differenceAmount: number;
    } | null>(null),
    [tab, setTab] = useState<"sales" | "cuts">("sales"),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [showCancel, setShowCancel] = useState(false),
    [showReturn, setShowReturn] = useState(false),
    [returnAmounts, setReturnAmounts] = useState<Record<string, string>>({}),
    [returnReason, setReturnReason] = useState("");
  const visible = useMemo(
    () =>
      sales.filter((x) => x.folio.toLowerCase().includes(query.toLowerCase())),
    [sales, query],
  );
  function sendSaleWhatsApp(data: Detail) {
    if (!data.customerPhone) return;
    let phone = data.customerPhone.replace(/\D/g, ""); if (phone.length === 10) phone = `52${phone}`;
    const lines = data.items.map((item) => `• ${item.quantity} x ${item.productName}${item.variantName ? ` (${item.variantName})` : ""} — ${money.format(item.lineTotal)}`).join("\n");
    const text = `🧾 *${businessName ?? session.user.businessName}*\n*Ticket de venta*\n\nFolio: ${data.folio}\nFecha: ${new Date(data.soldAtUtc).toLocaleString("es-MX")}\nCliente: ${data.customer}\n\n${lines}\n\n*TOTAL: ${money.format(data.total)}*\n\n${ticketMessage || "¡Gracias por tu compra!"}`;
    window.open(`https://wa.me/${phone}?text=${encodeURIComponent(text)}`, "_blank", "noopener,noreferrer");
  }
  async function sendSaleEmail(data: Detail) {
    const lines = data.items.map((item) => `${item.quantity} × ${item.productName}${item.variantName ? ` (${item.variantName})` : ""} — ${money.format(item.lineTotal)}`).join("\n");
    const content = `Folio: ${data.folio}\nFecha: ${new Date(data.soldAtUtc).toLocaleString("es-MX")}\nCliente: ${data.customer}\n\n${lines}\n\nTOTAL: ${money.format(data.total)}\n\n${ticketMessage || "¡Gracias por tu compra!"}`;
    try {
      if (await emailDocument({ session, documentType: "sale-ticket", reference: data.folio, content, defaultEmail: data.customerEmail }))
        window.alert("Ticket enviado por correo.");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No pudimos enviar el ticket por correo.");
    }
  }
  async function sendCutEmail(report: CashReport) {
    const paymentLines = [...report.salePayments.map((x) => `Ventas · ${method(x.method)}: ${money.format(x.total)}`), ...report.layawayPayments.map((x) => `Apartados · ${method(x.method)}: ${money.format(x.total)}`)].join("\n");
    const content = `Sucursal: ${report.branch}\nResponsable: ${report.user}\nApertura: ${new Date(report.openedAtUtc).toLocaleString("es-MX")}\nCierre: ${report.closedAtUtc ? new Date(report.closedAtUtc).toLocaleString("es-MX") : "Caja abierta"}\n\nFondo inicial: ${money.format(report.openingAmount)}\nVentas: ${money.format(report.salesTotal)}\nApartados: ${money.format(report.layawayTotal)}\n${paymentLines}\n\nEsperado: ${money.format(report.expectedAmount)}${report.countedAmount !== null ? `\nContado: ${money.format(report.countedAmount)}\nDiferencia: ${money.format(report.differenceAmount ?? 0)}` : ""}`;
    try {
      if (await emailDocument({ session, documentType: "cash-report", reference: report.id.slice(0, 8).toUpperCase(), content, defaultEmail: session.user.email }))
        window.alert("Corte de caja enviado por correo.");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No pudimos enviar el corte por correo.");
    }
  }
  async function load() {
    try {
      const [s, h, c] = await Promise.all([
        apiRequest<Sale[]>("/api/v1/sales", {}, session),
        apiRequest<Cut[]>("/api/v1/cash/history", {}, session),
        apiRequest<CashSummary | undefined>(
          "/api/v1/cash/current/summary",
          {},
          session,
        ),
      ]);
      setSales(s);
      setCuts(h);
      setCash(c ?? null);
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "No pudimos cargar las ventas y cajas.",
      );
    }
  }
  useEffect(() => {
    void load();
  }, []);
  async function openDetail(id: string) {
    try {
      setDetail(await apiRequest<Detail>(`/api/v1/sales/${id}`, {}, session));
    } catch (e) {
      setError(e instanceof Error ? e.message : "No pudimos abrir la venta.");
    }
  }
  async function cancel() {
    if (!detail) return;
    setBusy(true);
    try {
      await apiRequest(
        `/api/v1/sales/${detail.id}/cancel`,
        {
          method: "POST",
          body: JSON.stringify({
            reason: "Devolución completa desde el historial",
          }),
        },
        session,
      );
      setDetail(null);
      setShowCancel(false);
      await load();
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "No pudimos registrar la devolución.",
      );
    } finally {
      setBusy(false);
    }
  }
  async function partialReturn(exchange = false) {
    if (!detail) return;
    const items = detail.items
      .map((x) => ({
        saleItemId: x.id,
        quantity: Number(returnAmounts[x.id] || 0),
      }))
      .filter((x) => x.quantity > 0);
    if (!items.length) {
      setError("Captura al menos una cantidad para devolver.");
      return;
    }
    setBusy(true);
    try {
      await apiRequest(
        `/api/v1/sales/${detail.id}/return`,
        {
          method: "POST",
          body: JSON.stringify({
            reason: returnReason || "Devolución parcial",
            items,
          }),
        },
        session,
      );
      setShowReturn(false);
      setDetail(null);
      setReturnAmounts({});
      await load();
      if (exchange)
        window.dispatchEvent(
          new CustomEvent("vendemefacil:navigate", {
            detail: "Punto de venta",
          }),
        );
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "No pudimos registrar la devolución.",
      );
    } finally {
      setBusy(false);
    }
  }
  async function closeCash() {
    if (!cash) return;
    setBusy(true);
    try {
      await apiRequest<{ id: string; expectedAmount: number; countedAmount: number; differenceAmount: number }>(
          `/api/v1/cash/${cash.id}/close`,
          {
            method: "POST",
            body: JSON.stringify({ countedAmount: Number(counted) }),
          },
          session,
        );
      setCutReport(await apiRequest<CashReport>(`/api/v1/cash/${cash.id}/report`, {}, session));
      setCash(null);
      setShowClose(false);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No pudimos cerrar la caja.");
    } finally {
      setBusy(false);
    }
  }
  async function openCutReport(id: string) {
    setBusy(true); setError("");
    try { setCutReport(await apiRequest<CashReport>(`/api/v1/cash/${id}/report`, {}, session)); }
    catch (e) { setError(e instanceof Error ? e.message : "No pudimos cargar el detalle del corte."); }
    finally { setBusy(false); }
  }
  return (
    <div className="content sales-page">
      <section className="page-title-row">
        <div>
          <p className="eyebrow">OPERACIÓN</p>
          <h1>Ventas y caja</h1>
          <p>Consulta ventas, cortes y diferencias de efectivo.</p>
        </div>
        {cash && (
          <button
            className="button secondary"
            onClick={() => setShowClose(true)}
          >
            <Banknote />
            Cerrar caja
          </button>
        )}
      </section>
      {error && <div className="page-error">{error}</div>}
      <div className="sales-tabs">
        <button
          className={tab === "sales" ? "active" : ""}
          onClick={() => setTab("sales")}
        >
          <ReceiptText />
          Ventas
        </button>
        <button
          className={tab === "cuts" ? "active" : ""}
          onClick={() => setTab("cuts")}
        >
          <History />
          Cortes de caja
        </button>
      </div>
      {tab === "sales" ? (
        <section className="card catalog-card">
          <div className="catalog-toolbar">
            <label className="catalog-search">
              <Search />
              <input
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Buscar por folio"
              />
            </label>
          </div>
          <div className="sales-head">
            <span>Folio</span>
            <span>Fecha</span>
            <span>Productos</span>
            <span>Total</span>
            <span>Estado</span>
            <span />
          </div>
          {!visible.length ? (
            <div className="empty-state">
              <ReceiptText />
              <strong>Aún no hay ventas</strong>
            </div>
          ) : (
            visible.map((x) => (
              <div className="sale-row" key={x.id}>
                <strong>{x.folio}</strong>
                <span>{new Date(x.soldAtUtc).toLocaleString("es-MX")}</span>
                <span>{x.itemCount} artículos</span>
                <strong>{money.format(x.total)}</strong>
                <span
                  className={
                    x.status === "Cancelled"
                      ? "status-pill cancelled"
                      : "status-pill"
                  }
                >
                  {x.status === "Cancelled" ? "Cancelada" : "Completada"}
                </span>
                <button
                  className="icon-button"
                  onClick={() => void openDetail(x.id)}
                >
                  <Eye />
                </button>
              </div>
            ))
          )}
        </section>
      ) : (
        <section className="card cut-history">
          <div className="cut-head">
            <span>Apertura / cierre</span>
            <span>Usuario</span>
            <span>Esperado</span>
            <span>Contado</span>
            <span>Diferencia</span>
            <span />
          </div>
          {cuts.map((x) => (
            <div className="cut-row" key={x.id}>
              <span>
                <strong>
                  {new Date(x.openedAtUtc).toLocaleString("es-MX")}
                </strong>
                <small>
                  {x.closedAtUtc
                    ? new Date(x.closedAtUtc).toLocaleString("es-MX")
                    : "Caja abierta"}{" "}
                  · {x.branch}
                </small>
              </span>
              <span>{x.user}</span>
              <strong>{money.format(x.expectedAmount ?? 0)}</strong>
              <strong>
                {x.countedAmount === null ? "—" : money.format(x.countedAmount)}
              </strong>
              <strong
                className={
                  (x.differenceAmount ?? 0) !== 0 ? "cut-difference" : ""
                }
              >
                {x.differenceAmount === null
                  ? "—"
                  : money.format(x.differenceAmount)}
              </strong>
              <button className="icon-button" title="Ver e imprimir corte" disabled={busy} onClick={() => void openCutReport(x.id)}><Printer /></button>
            </div>
          ))}
        </section>
      )}
      {detail && (
        <div className="modal-layer">
          <div className="sale-detail">
            <button className="close-form" onClick={() => setDetail(null)}>
              <X />
            </button>
            <div className="receipt" id="historic-sale-receipt">
              <div className="receipt-brand">
                {logoUrl ? <img src={logoUrl} alt={businessName ?? "Logotipo"} /> : (businessName ?? session.user.businessName).slice(0, 2).toUpperCase()}
              </div>
              <h2>{businessName ?? session.user.businessName}</h2>
              <p>
                {detail.status === "Cancelled"
                  ? "VENTA CANCELADA"
                  : "Ticket de venta"}
                <br />
                {detail.folio}
                <br />
                {new Date(detail.soldAtUtc).toLocaleString("es-MX")}
              </p>
              {detail.items.map((x) => (
                <div className="ticket-line" key={x.sku}>
                  <span>
                    {x.quantity} × {x.productName}
                    <small>{x.variantName}</small>
                  </span>
                  <b>{money.format(x.lineTotal)}</b>
                </div>
              ))}
              <div className="ticket-total">
                <span>TOTAL</span>
                <b>{money.format(detail.total)}</b>
              </div>
              {detail.payments.map((x) => (
                <div className="ticket-payment" key={x.method}>
                  <span>{method(x.method)}</span>
                  <b>{money.format(x.receivedAmount)}</b>
                  <span>Cambio</span>
                  <b>{money.format(x.changeAmount)}</b>
                </div>
              ))}
              <footer>{ticketMessage || "¡Gracias por tu compra!"}</footer>
            </div>
            <div className="detail-actions">
              <button
                className="button secondary"
                onClick={() => printReceipt(document.getElementById("historic-sale-receipt"))}
              >
                <Printer />
                Imprimir
              </button>
              <button className="button secondary" disabled={!detail.customerPhone} onClick={() => sendSaleWhatsApp(detail)}><MessageCircle />WhatsApp</button>
              <button className="button secondary" onClick={() => void sendSaleEmail(detail)}><Mail />Email</button>
              {detail.status !== "Cancelled" && (
                <button
                  className="button secondary"
                  onClick={() => setShowReturn(true)}
                >
                  <XCircle />
                  Devolver / cambiar
                </button>
              )}
              {detail.status !== "Cancelled" && (
                <button
                  className="button danger"
                  disabled={busy}
                  onClick={() => setShowCancel(true)}
                >
                  <XCircle />
                  Cancelar venta
                </button>
              )}
            </div>
          </div>
        </div>
      )}
      {showCancel && detail && (
        <div
          className="modal-layer confirmation-layer"
          onMouseDown={() => setShowCancel(false)}
        >
          <div
            className="confirmation-modal"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="confirmation-icon danger">
              <XCircle />
            </div>
            <p className="eyebrow">ACCIÓN IMPORTANTE</p>
            <h2>¿Cancelar esta venta?</h2>
            <p>
              Se registrará la devolución completa de{" "}
              <strong>{detail.folio}</strong> y todos los artículos volverán al
              inventario.
            </p>
            <div className="confirmation-actions">
              <button
                className="button secondary"
                disabled={busy}
                onClick={() => setShowCancel(false)}
              >
                Conservar venta
              </button>
              <button
                className="button danger"
                disabled={busy}
                onClick={() => void cancel()}
              >
                {busy ? "Procesando..." : "Sí, cancelar venta"}
              </button>
            </div>
          </div>
        </div>
      )}
      {showReturn && detail && (
        <div className="modal-layer">
          <div className="close-cash-modal">
            <button className="close-form" onClick={() => setShowReturn(false)}>
              <X />
            </button>
            <h2>Devolución o cambio</h2>
            <p>
              Indica las piezas que regresan. Para un cambio se abrirá el POS
              después de devolverlas.
            </p>
            <div className="return-list">
              {detail.items
                .filter((x) => x.availableToReturn > 0)
                .map((x) => (
                  <label className="return-line" key={x.id}>
                    <span>
                      <strong>{x.productName}</strong>
                      <small>
                        {x.variantName} · disponibles: {x.availableToReturn}
                      </small>
                    </span>
                    <input
                      type="number"
                      min="0"
                      max={x.availableToReturn}
                      step=".001"
                      value={returnAmounts[x.id] ?? ""}
                      onChange={(e) =>
                        setReturnAmounts((v) => ({
                          ...v,
                          [x.id]: e.target.value,
                        }))
                      }
                    />
                  </label>
                ))}
            </div>
            <label>
              Motivo
              <div className="currency-input">
                <input
                  value={returnReason}
                  onChange={(e) => setReturnReason(e.target.value)}
                  placeholder="Talla, defecto, cambio..."
                />
              </div>
            </label>
            <div className="detail-actions">
              <button
                className="button secondary"
                disabled={busy}
                onClick={() => void partialReturn(false)}
              >
                Solo devolver
              </button>
              <button
                className="button primary"
                disabled={busy}
                onClick={() => void partialReturn(true)}
              >
                Devolver y cambiar
              </button>
            </div>
          </div>
        </div>
      )}
      {showClose && cash && (
        <div className="modal-layer">
          <div className="close-cash-modal">
            <button className="close-form" onClick={() => setShowClose(false)}>
              <X />
            </button>
            <div className="cash-illustration">
              <Banknote />
            </div>
            <h2>Cerrar caja</h2>
            <div className="cash-preview">
              <span>Fondo inicial</span>
              <strong>{money.format(cash.openingAmount)}</strong>
              <span>Ventas ({cash.transactions})</span>
              <strong>{money.format(cash.salesTotal)}</strong>
              {cash.salePayments.map((x) => (
                <>
                  <span>Ventas · {method(x.method)}</span>
                  <strong>{money.format(x.total)}</strong>
                </>
              ))}
              <span>Apartados ({cash.layawayTransactions})</span>
              <strong>{money.format(cash.layawayTotal)}</strong>
              {cash.layawayPayments.map((x) => (
                <>
                  <span>Apartados · {method(x.method)}</span>
                  <strong>{money.format(x.total)}</strong>
                </>
              ))}
              <span>Efectivo esperado</span>
              <strong>{money.format(cash.expectedAmount)}</strong>
            </div>
            <label>
              Efectivo contado
              <div className="currency-input">
                <span>$</span>
                <input
                  value={counted}
                  onChange={(e) => setCounted(e.target.value)}
                  type="number"
                  min="0"
                  step="0.01"
                  autoFocus
                />
              </div>
            </label>
            <button
              className="button primary"
              disabled={busy || counted === ""}
              onClick={() => void closeCash()}
            >
              Cerrar y comparar
            </button>
          </div>
        </div>
      )}
      {result && (
        <div className="modal-layer">
          <div className="close-result">
            <h2>Caja cerrada</h2>
            <div>
              <span>Esperado</span>
              <strong>{money.format(result.expectedAmount)}</strong>
              <span>Contado</span>
              <strong>{money.format(result.countedAmount)}</strong>
              <span>Diferencia</span>
              <strong>{money.format(result.differenceAmount)}</strong>
            </div>
            <button
              className="button primary"
              onClick={() => {
                setResult(null);
                setShowClose(false);
                setTab("cuts");
              }}
            >
              Ver cortes
            </button>
          </div>
        </div>
      )}
      {cutReport && (
        <div className="modal-layer">
          <div className="sale-detail cash-report-modal">
            <button className="close-form" onClick={() => { setCutReport(null); setTab("cuts"); }}><X /></button>
            <CashReportReceipt report={cutReport} businessName={businessName ?? session.user.businessName} logoUrl={logoUrl} />
            <div className="detail-actions">
              <button className="button primary" onClick={() => printReceipt(document.getElementById("cash-report-receipt"))}><Printer /> Imprimir corte</button>
              <button className="button secondary" onClick={() => void sendCutEmail(cutReport)}><Mail /> Enviar por email</button>
              <button className="button secondary" onClick={() => { setCutReport(null); setTab("cuts"); }}>Cerrar</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
