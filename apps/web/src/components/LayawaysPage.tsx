import {
  CalendarClock,
  CheckCircle2,
  MessageCircle,
  PackageCheck,
  Plus,
  Search,
  WalletCards,
  Printer,
  X,
  XCircle,
} from "lucide-react";
import { FormEvent, KeyboardEvent, useEffect, useMemo, useState } from "react";
import { ApiProduct, apiRequest, AuthSession } from "../lib/api";
import { printReceipt } from "../lib/printReceipt";

type Customer = { id: string; name: string; phone: string | null };
type Branch = { id: string; name: string };
type LayawayItem = {
  id: string;
  productVariantId: string;
  productName: string;
  variantName: string;
  sku: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
};
type Payment = {
  id: string;
  amount: number;
  method: string;
  paidAtUtc: string;
  note: string | null;
};
type Layaway = {
  id: string;
  folio: string;
  openedAtUtc: string;
  dueAtUtc: string;
  status: string;
  total: number;
  paid: number;
  balance: number;
  customer: string;
  phone: string | null;
  items: LayawayItem[];
  payments: Payment[];
};
const money = new Intl.NumberFormat("es-MX", {
  style: "currency",
  currency: "MXN",
});
const normalize = (value: string) =>
  value
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .toLowerCase();
const statusLabel = (status: string) =>
  status === "Active"
    ? "Activo"
    : status === "Completed"
      ? "Liquidado"
      : "Cancelado";

export function LayawaysPage({ session }: { session: AuthSession }) {
  const [layaways, setLayaways] = useState<Layaway[]>([]),
    [customers, setCustomers] = useState<Customer[]>([]),
    [products, setProducts] = useState<ApiProduct[]>([]),
    [branches, setBranches] = useState<Branch[]>([]);
  const [query, setQuery] = useState(""),
    [status, setStatus] = useState(""),
    [showCreate, setShowCreate] = useState(false),
    [detail, setDetail] = useState<Layaway | null>(null),
    [error, setError] = useState(""),
    [paymentError, setPaymentError] = useState(""),
    [paymentDraftTotal, setPaymentDraftTotal] = useState(0),
    [success, setSuccess] = useState(""),
    [busy, setBusy] = useState(false);
  const [receipt, setReceipt] = useState<{ layaway: Layaway; title: string; amount: number } | null>(null),
    [confirmCancel, setConfirmCancel] = useState(false);
  const [customerQuery, setCustomerQuery] = useState(""),
    [customerId, setCustomerId] = useState(""),
    [productQuery, setProductQuery] = useState(""),
    [cart, setCart] = useState<{ product: ApiProduct; quantity: number }[]>([]),
    [term, setTerm] = useState("30"),
    [customerHighlight, setCustomerHighlight] = useState(0),
    [productHighlight, setProductHighlight] = useState(0);
  async function load() {
    try {
      const [l, c, p, b] = await Promise.all([
        apiRequest<Layaway[]>("/api/v1/layaways", {}, session),
        apiRequest<Customer[]>("/api/v1/customers", {}, session),
        apiRequest<ApiProduct[]>("/api/v1/products", {}, session),
        apiRequest<Branch[]>("/api/v1/branches", {}, session),
      ]);
      setLayaways(l);
      setCustomers(c);
      setProducts(p);
      setBranches(b);
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "No pudimos cargar los apartados.",
      );
    }
  }
  useEffect(() => {
    void load();
  }, []);
  const visible = useMemo(
    () =>
      layaways.filter(
        (x) =>
          (!status ||
            (status === "Overdue"
              ? x.status === "Active" && new Date(x.dueAtUtc) < new Date()
              : x.status === status)) &&
          normalize(`${x.folio} ${x.customer} ${x.phone ?? ""}`).includes(
            normalize(query),
          ),
      ),
    [layaways, status, query],
  );
  const customerMatches =
    customerQuery && !customerId
      ? customers
          .filter((x) =>
            normalize(`${x.name} ${x.phone ?? ""}`).includes(
              normalize(customerQuery),
            ),
          )
          .slice(0, 6)
      : [];
  const productMatches = productQuery
    ? products
        .filter(
          (x) =>
            x.stock > 0 &&
            normalize(
              `${x.name} ${x.variant} ${x.sku} ${x.barcode ?? ""}`,
            ).includes(normalize(productQuery)),
        )
        .slice(0, 6)
    : [];
  const total = cart.reduce((sum, x) => sum + x.product.price * x.quantity, 0);
  function addProduct(product: ApiProduct) {
    setCart((current) =>
      current.some((x) => x.product.variantId === product.variantId)
        ? current.map((x) =>
            x.product.variantId === product.variantId
              ? { ...x, quantity: Math.min(x.quantity + 1, x.product.stock) }
              : x,
          )
        : [...current, { product, quantity: 1 }],
    );
    setProductQuery("");
  }
  function selectCustomer(customer: Customer) {
    setCustomerId(customer.id);
    setCustomerQuery(customer.name);
  }
  function suggestionKey<T>(event: KeyboardEvent<HTMLInputElement>, matches: T[], highlight: number, setHighlight: (value: number | ((current: number) => number)) => void, select: (item: T) => void, clear: () => void) {
    if (event.key === "Escape") { event.preventDefault(); clear(); return; }
    if (!matches.length) return;
    if (event.key === "ArrowDown") { event.preventDefault(); setHighlight((value) => Math.min(value + 1, matches.length - 1)); }
    else if (event.key === "ArrowUp") { event.preventDefault(); setHighlight((value) => Math.max(value - 1, 0)); }
    else if (event.key === "Enter") { event.preventDefault(); select(matches[highlight] ?? matches[0]); }
  }
  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!customerId || !cart.length) {
      setError("Selecciona un cliente y al menos un producto.");
      return;
    }
    const data = new FormData(event.currentTarget);
    const payments = [
      [1, "depositCash"],
      [2, "depositCard"],
      [3, "depositTransfer"],
    ]
      .map(([method, name]) => ({
        method: Number(method),
        amount: Number(data.get(String(name))) || 0,
      }))
      .filter((x) => x.amount > 0);
    const deposit = payments.reduce((sum, x) => sum + x.amount, 0);
    setBusy(true);
    setError("");
    try {
      const result = await apiRequest<{ folio: string }>(
        "/api/v1/layaways",
        {
          method: "POST",
          body: JSON.stringify({
            branchId: branches[0]?.id,
            customerId,
            termDays: Number(term),
            deposit,
            paymentMethod: 1,
            payments,
            notes: data.get("notes") || null,
            items: cart.map((x) => ({
              productVariantId: x.product.variantId,
              quantity: x.quantity,
            })),
          }),
        },
        session,
      );
      setShowCreate(false);
      setCart([]);
      setCustomerId("");
      setCustomerQuery("");
      setSuccess(`Apartado ${result.folio} creado correctamente.`);
      const updated = await apiRequest<Layaway[]>("/api/v1/layaways", {}, session);
      setLayaways(updated);
      const created = updated.find((x) => x.folio === result.folio);
      if (created) setReceipt({ layaway: created, title: "Comprobante de apartado", amount: deposit });
      window.dispatchEvent(new Event("vendemefacil:reminders-changed"));
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "No pudimos crear el apartado.",
      );
    } finally {
      setBusy(false);
    }
  }
  async function addPayment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!detail) return;
    const form = event.currentTarget,
      data = new FormData(form);
    const payments = [
      [1, "paymentCash"],
      [2, "paymentCard"],
      [3, "paymentTransfer"],
    ]
      .map(([method, name]) => ({
        method: Number(method),
        amount: Number(data.get(String(name))) || 0,
      }))
      .filter((x) => x.amount > 0);
    const amount = payments.reduce((sum, x) => sum + x.amount, 0);
    if (amount <= 0) {
      setPaymentError("Captura un abono mayor a cero en efectivo, tarjeta o transferencia.");
      return;
    }
    if (amount > detail.balance) {
      setPaymentError(`El abono no puede superar el saldo de ${money.format(detail.balance)}.`);
      return;
    }
    setBusy(true);
    setPaymentError("");
    try {
      await apiRequest(
        `/api/v1/layaways/${detail.id}/payments`,
        {
          method: "POST",
          body: JSON.stringify({
            amount,
            paymentMethod: 1,
            payments,
            note: data.get("note") || null,
          }),
        },
        session,
      );
      setSuccess("Abono registrado correctamente.");
      form.reset();
      const updated = await apiRequest<Layaway[]>("/api/v1/layaways", {}, session);
      setLayaways(updated);
      const refreshed = updated.find((x) => x.id === detail.id);
      if (refreshed) setReceipt({ layaway: refreshed, title: "Comprobante de abono", amount });
      setDetail(null);
      window.dispatchEvent(new Event("vendemefacil:reminders-changed"));
    } catch (reason) {
      setPaymentError(
        reason instanceof Error
          ? reason.message
          : "No pudimos registrar el abono.",
      );
    } finally {
      setBusy(false);
    }
  }
  async function cancelLayaway() {
    if (!detail) return;
    setBusy(true);
    try {
      await apiRequest(
        `/api/v1/layaways/${detail.id}/cancel`,
        { method: "POST" },
        session,
      );
      setSuccess(
        `Apartado ${detail.folio} cancelado; la mercancía regresó al inventario.`,
      );
      setDetail(null);
      setConfirmCancel(false);
      await load();
      window.dispatchEvent(new Event("vendemefacil:reminders-changed"));
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "No pudimos cancelar el apartado.",
      );
    } finally {
      setBusy(false);
    }
  }
  function shareReceipt(data: { layaway: Layaway; title: string; amount: number }) {
    let phone = (data.layaway.phone ?? "").replace(/\D/g, ""); if (phone.length === 10) phone = `52${phone}`;
    const items = data.layaway.items.map((item) => `• ${item.quantity} x ${item.productName}${item.variantName ? ` (${item.variantName})` : ""} — ${money.format(item.lineTotal)}`).join("\n");
    const text = `🧾 *${data.title}*\n\nFolio: ${data.layaway.folio}\nCliente: ${data.layaway.customer}\nVencimiento: ${new Date(data.layaway.dueAtUtc).toLocaleDateString("es-MX")}\n\n${items}\n\nRecibido hoy: ${money.format(data.amount)}\nPagado acumulado: ${money.format(data.layaway.paid)}\n*SALDO: ${money.format(data.layaway.balance)}*\n\nConserva este mensaje como comprobante.`;
    window.open(`https://wa.me/${phone}?text=${encodeURIComponent(text)}`, "_blank", "noopener,noreferrer");
  }
  function whatsapp(x: Layaway) {
    if (!x.phone) {
      setError("Este cliente no tiene teléfono registrado.");
      return;
    }
    let phone = x.phone.replace(/\D/g, "");
    if (phone.length === 10) phone = `52${phone}`;
    const date = new Date(x.dueAtUtc).toLocaleDateString("es-MX");
    const message = `Hola ${x.customer}, te recordamos que tu apartado ${x.folio} tiene un saldo de ${money.format(x.balance)} y vence el ${date}. ¡Gracias!`;
    window.open(
      `https://wa.me/${phone}?text=${encodeURIComponent(message)}`,
      "_blank",
      "noopener,noreferrer",
    );
  }
  return (
    <div className="content layaways-page">
      <section className="page-title-row">
        <div>
          <p className="eyebrow">CLIENTES</p>
          <h1>Apartados</h1>
          <p>Controla anticipos, abonos, vencimientos y recordatorios.</p>
        </div>
        <button className="button primary" onClick={() => setShowCreate(true)}>
          <Plus />
          Nuevo apartado
        </button>
      </section>
      {error && (
        <div className="page-error">
          {error}
          <button onClick={() => setError("")}>Cerrar</button>
        </div>
      )}
      {success && <div className="form-success layaway-success">{success}</div>}
      <section className="layaway-summary">
        <article>
          <WalletCards />
          <span>
            <strong>
              {layaways.filter((x) => x.status === "Active").length}
            </strong>{" "}
            activos
          </span>
        </article>
        <article>
          <CalendarClock />
          <span>
            <strong>
              {
                layaways.filter(
                  (x) =>
                    x.status === "Active" && new Date(x.dueAtUtc) < new Date(),
                ).length
              }
            </strong>{" "}
            vencidos
          </span>
        </article>
        <article>
          <PackageCheck />
          <span>
            <strong>
              {money.format(
                layaways
                  .filter((x) => x.status === "Active")
                  .reduce((s, x) => s + x.balance, 0),
              )}
            </strong>{" "}
            por cobrar
          </span>
        </article>
      </section>
      <section className="card catalog-card">
        <div className="layaway-toolbar">
          <label className="catalog-search">
            <Search />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Folio, cliente o teléfono"
            />
          </label>
          <select value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">Todos los estados</option>
            <option value="Active">Activos</option>
            <option value="Overdue">Vencidos</option>
            <option value="Completed">Liquidados</option>
            <option value="Cancelled">Cancelados</option>
          </select>
        </div>
        <div className="layaway-head">
          <span>Folio / cliente</span>
          <span>Fechas</span>
          <span>Progreso</span>
          <span>Estado</span>
          <span />
        </div>
        {visible.map((x) => {
          const overdue =
            x.status === "Active" && new Date(x.dueAtUtc) < new Date();
          return (
            <div className="layaway-row" key={x.id}>
              <span>
                <strong>{x.folio}</strong>
                <small>
                  {x.customer} · {x.phone || "Sin teléfono"}
                </small>
              </span>
              <span>
                <strong>
                  {new Date(x.dueAtUtc).toLocaleDateString("es-MX")}
                </strong>
                <small>
                  {overdue
                    ? "Vencido"
                    : `Creado ${new Date(x.openedAtUtc).toLocaleDateString("es-MX")}`}
                </small>
              </span>
              <span>
                <strong>
                  {money.format(x.paid)} / {money.format(x.total)}
                </strong>
                <i>
                  <b
                    style={{
                      width: `${Math.min(100, x.total ? (x.paid / x.total) * 100 : 0)}%`,
                    }}
                  />
                </i>
                <small>Saldo {money.format(x.balance)}</small>
              </span>
              <span
                className={`layaway-status ${x.status.toLowerCase()} ${overdue ? "overdue" : ""}`}
              >
                {overdue ? "Vencido" : statusLabel(x.status)}
              </span>
              <span className="layaway-actions">
                {x.status === "Active" && (
                  <button
                    title="Enviar recordatorio por WhatsApp"
                    onClick={() => whatsapp(x)}
                  >
                    <MessageCircle />
                  </button>
                )}
                <button onClick={() => setDetail(x)}>Ver</button>
              </span>
            </div>
          );
        })}
        {!visible.length && (
          <div className="empty-state">
            <CalendarClock />
            <strong>No hay apartados con estos filtros</strong>
          </div>
        )}
      </section>
      {showCreate && (
        <div className="modal-layer" onMouseDown={() => setShowCreate(false)}>
          <form
            className="product-form layaway-form"
            onSubmit={create}
            onMouseDown={(e) => e.stopPropagation()}
          >
            <div className="form-heading">
              <div>
                <p className="eyebrow">NUEVO</p>
                <h2>Crear apartado</h2>
              </div>
              <button
                type="button"
                className="close-form"
                onClick={() => setShowCreate(false)}
              >
                <X />
              </button>
            </div>
            <label className="catalog-search entry-search">
              <Search />
              <input
                value={customerQuery}
                onChange={(e) => {
                  setCustomerQuery(e.target.value);
                  setCustomerId("");
                  setCustomerHighlight(0);
                }}
                onKeyDown={(event) => suggestionKey(event, customerMatches, customerHighlight, setCustomerHighlight, selectCustomer, () => { setCustomerQuery(""); setCustomerId(""); })}
                placeholder="Busca al cliente"
              />
            </label>
            {customerMatches.length > 0 && (
              <div className="product-options">
                {customerMatches.map((x, index) => (
                  <button
                    type="button"
                    className={index === customerHighlight ? "active" : ""}
                    key={x.id}
                    onMouseEnter={() => setCustomerHighlight(index)}
                    onClick={() => selectCustomer(x)}
                  >
                    <span>
                      <strong>{x.name}</strong>
                      <small>{x.phone || "Sin teléfono"}</small>
                    </span>
                  </button>
                ))}
              </div>
            )}
            <label className="catalog-search entry-search">
              <Search />
              <input
                value={productQuery}
                onChange={(e) => { setProductQuery(e.target.value); setProductHighlight(0); }}
                onKeyDown={(event) => suggestionKey(event, productMatches, productHighlight, setProductHighlight, addProduct, () => setProductQuery(""))}
                placeholder="Agrega productos por nombre o SKU"
              />
            </label>
            {productMatches.length > 0 && (
              <div className="product-options">
                {productMatches.map((x, index) => (
                  <button
                    type="button"
                    className={index === productHighlight ? "active" : ""}
                    key={x.variantId}
                    onMouseEnter={() => setProductHighlight(index)}
                    onClick={() => addProduct(x)}
                  >
                    <span>
                      <strong>{x.name}</strong>
                      <small>
                        {x.variant} · SKU {x.sku}
                      </small>
                    </span>
                    <b>{x.stock} disp.</b>
                  </button>
                ))}
              </div>
            )}
            <div className="layaway-cart">
              {cart.map((x) => (
                <div key={x.product.variantId}>
                  <span>
                    <strong>{x.product.name}</strong>
                    <small>
                      {x.product.variant} · {money.format(x.product.price)}
                    </small>
                  </span>
                  <input
                    type="number"
                    min="1"
                    max={x.product.stock}
                    value={x.quantity}
                    onChange={(e) =>
                      setCart((v) =>
                        v.map((y) =>
                          y.product.variantId === x.product.variantId
                            ? { ...y, quantity: Number(e.target.value) }
                            : y,
                        ),
                      )
                    }
                  />
                  <button
                    type="button"
                    onClick={() =>
                      setCart((v) =>
                        v.filter(
                          (y) => y.product.variantId !== x.product.variantId,
                        ),
                      )
                    }
                  >
                    <X />
                  </button>
                </div>
              ))}
            </div>
            <div className="form-grid">
              <label>
                Plazo
                <select value={term} onChange={(e) => setTerm(e.target.value)}>
                  <option value="15">15 días</option>
                  <option value="20">20 días</option>
                  <option value="30">30 días</option>
                  <option value="45">45 días</option>
                  <option value="60">60 días</option>
                </select>
              </label>
              <label>
                Anticipo efectivo
                <input
                  name="depositCash"
                  type="number"
                  min="0"
                  max={total}
                  step=".01"
                  defaultValue="0"
                />
              </label>
              <label>
                Anticipo tarjeta
                <input
                  name="depositCard"
                  type="number"
                  min="0"
                  max={total}
                  step=".01"
                  defaultValue="0"
                />
              </label>
              <label>
                Anticipo transferencia
                <input
                  name="depositTransfer"
                  type="number"
                  min="0"
                  max={total}
                  step=".01"
                  defaultValue="0"
                />
              </label>
              <label>
                Notas
                <input name="notes" placeholder="Acuerdos u observaciones" />
              </label>
            </div>
            <div className="layaway-total">
              <span>Total apartado</span>
              <strong>{money.format(total)}</strong>
            </div>
            {error && <div className="form-error" role="alert">{error}</div>}
            <button
              className="button primary entry-submit"
              disabled={busy || !customerId || !cart.length}
            >
              {busy ? "Guardando..." : "Crear apartado"}
            </button>
          </form>
        </div>
      )}
      {detail && (
        <div className="modal-layer" onMouseDown={() => setDetail(null)}>
          <div
            className="product-form layaway-detail"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <div className="form-heading">
              <div>
                <p className="eyebrow">{statusLabel(detail.status)}</p>
                <h2>{detail.folio}</h2>
                <p>
                  {detail.customer} · vence{" "}
                  {new Date(detail.dueAtUtc).toLocaleDateString("es-MX")}
                </p>
              </div>
              <button className="close-form" onClick={() => setDetail(null)}>
                <X />
              </button>
            </div>
            <div className="layaway-detail-items">
              {detail.items.map((x) => (
                <div key={x.id}>
                  <span>
                    {x.quantity} × {x.productName}
                    <small>
                      {x.variantName} · {x.sku}
                    </small>
                  </span>
                  <strong>{money.format(x.lineTotal)}</strong>
                </div>
              ))}
            </div>
            <div className="layaway-balance">
              <span>
                Total<strong>{money.format(detail.total)}</strong>
              </span>
              <span>
                Abonado<strong>{money.format(detail.paid)}</strong>
              </span>
              <span>
                Saldo<strong>{money.format(detail.balance)}</strong>
              </span>
            </div>
            {detail.payments.length > 0 && (
              <div className="payment-history">
                <strong>Historial de abonos</strong>
                {detail.payments.map((x) => (
                  <span key={x.id}>
                    {new Date(x.paidAtUtc).toLocaleDateString("es-MX")} ·{" "}
                    {x.method}
                    <b>{money.format(x.amount)}</b>
                  </span>
                ))}
              </div>
            )}
            {detail.status === "Active" && (
              <form
                onSubmit={addPayment}
                onInput={(event) => {
                  const data = new FormData(event.currentTarget);
                  setPaymentDraftTotal(["paymentCash", "paymentCard", "paymentTransfer"].reduce((sum, name) => sum + (Number(data.get(name)) || 0), 0));
                  setPaymentError("");
                }}
              >
                <div className="form-grid">
                  <label>
                    Efectivo
                    <input
                      name="paymentCash"
                      type="number"
                      min="0"
                      max={detail.balance}
                      step=".01"
                      defaultValue="0"
                    />
                  </label>
                  <label>
                    Tarjeta
                    <input
                      name="paymentCard"
                      type="number"
                      min="0"
                      max={detail.balance}
                      step=".01"
                      defaultValue="0"
                    />
                  </label>
                  <label>
                    Transferencia
                    <input
                      name="paymentTransfer"
                      type="number"
                      min="0"
                      max={detail.balance}
                      step=".01"
                      defaultValue="0"
                    />
                  </label>
                  <label className="wide">
                    Nota
                    <input name="note" placeholder="Opcional" />
                  </label>
                </div>
                {paymentError && <div className="form-error" role="alert">{paymentError}</div>}
                <button className="button primary entry-submit" disabled={busy || paymentDraftTotal <= 0 || paymentDraftTotal > detail.balance}>
                  <CheckCircle2 />
                  Registrar abono
                </button>
              </form>
            )}
            <div className="detail-actions">
              {detail.status === "Active" && (
                <button
                  className="button secondary"
                  onClick={() => whatsapp(detail)}
                >
                  <MessageCircle />
                  Recordar
                </button>
              )}
              {detail.status === "Active" && (
                <button
                  className="button danger"
                  disabled={busy}
                  onClick={() => setConfirmCancel(true)}
                >
                  <XCircle />
                  Cancelar apartado
                </button>
              )}
            </div>
          </div>
        </div>
      )}
      {receipt && <div className="modal-layer"><div className="receipt-modal"><button className="close-form" onClick={() => setReceipt(null)}><X /></button><div className="receipt" id="layaway-receipt"><h2>{receipt.title}</h2><p>{receipt.layaway.folio}<br />{receipt.layaway.customer}<br />Vence: {new Date(receipt.layaway.dueAtUtc).toLocaleDateString("es-MX")}</p>{receipt.layaway.items.map((x) => <div className="ticket-line" key={x.id}><span>{x.quantity} × {x.productName}<small>{x.variantName} · {x.sku}</small></span><b>{money.format(x.lineTotal)}</b></div>)}<div className="ticket-payment"><span>Recibido</span><b>{money.format(receipt.amount)}</b><span>Pagado acumulado</span><b>{money.format(receipt.layaway.paid)}</b></div><div className="ticket-total"><span>SALDO</span><b>{money.format(receipt.layaway.balance)}</b></div><footer>Conserva este comprobante</footer></div><div className="layaway-receipt-actions"><button className="button primary" onClick={() => printReceipt(document.getElementById("layaway-receipt"))}><Printer />Imprimir</button><button className="button secondary" disabled={!receipt.layaway.phone} onClick={() => shareReceipt(receipt)}><MessageCircle />WhatsApp</button></div></div></div>}
      {confirmCancel && detail && <div className="modal-layer confirmation-layer"><div className="confirmation-modal"><div className="confirmation-icon danger"><XCircle /></div><h2>¿Cancelar apartado?</h2><p>La mercancía regresará al inventario. Los pagos recibidos no se devolverán automáticamente.</p><div className="confirmation-actions"><button className="button secondary" onClick={() => setConfirmCancel(false)}>Conservar</button><button className="button danger" disabled={busy} onClick={() => void cancelLayaway()}>Sí, cancelar</button></div></div></div>}
    </div>
  );
}
