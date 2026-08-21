import {
  Banknote,
  CreditCard,
  Landmark,
  Minus,
  Plus,
  Printer,
  Search,
  ShoppingBag,
  Trash2,
  UserPlus,
  WalletCards,
  MessageCircle,
  Mail,
  X,
} from "lucide-react";
import { FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from "react";
import { ApiProduct, apiRequest, AuthSession } from "../lib/api";
import { printReceipt } from "../lib/printReceipt";
import { loadPrintSettings } from "../lib/qzPrinting";
import { emailDocument } from "../lib/emailDocument";
import { usePlanAccess } from "./PlanAccess";
type Customer = { id: string; name: string; phone?: string | null; email?: string | null };
type Branch = { id: string; name: string };
type Cash = { id: string; branchId: string };
type Line = { product: ApiProduct; quantity: number };
type Receipt = {
  id: string;
  folio: string;
  soldAtUtc: string;
  subtotal: number;
  discount: number;
  total: number;
  paymentMethod: string;
  receivedAmount: number;
  changeAmount: number;
  customer?: string;
  customerPhone?: string | null;
  customerEmail?: string | null;
  items: {
    productName: string;
    variantName: string;
    sku: string;
    quantity: number;
    lineTotal: number;
  }[];
};
const money = new Intl.NumberFormat("es-MX", {
  style: "currency",
  currency: "MXN",
});
export function PointOfSalePage({ session, businessName, logoUrl, ticketMessage, allowNegativeStock }: { session: AuthSession; businessName?: string; logoUrl?: string | null; ticketMessage?: string | null; allowNegativeStock: boolean }) {
  const autoPrintedFolio = useRef("");
  const planAccess = usePlanAccess();
  const [products, setProducts] = useState<ApiProduct[]>([]),
    [branches, setBranches] = useState<Branch[]>([]),
    [customers, setCustomers] = useState<Customer[]>([]),
    [customerId, setCustomerId] = useState(""),
    [customerQuery, setCustomerQuery] = useState(""),
    [cash, setCash] = useState<Cash | null>(null),
    [cart, setCart] = useState<Line[]>([]),
    [query, setQuery] = useState(""),
    [opening, setOpening] = useState("0"),
    [received, setReceived] = useState(""),
    [cashPart, setCashPart] = useState(""),
    [cardPart, setCardPart] = useState(""),
    [transferPart, setTransferPart] = useState(""),
    [discount, setDiscount] = useState("0"),
    [payment, setPayment] = useState(1),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [receipt, setReceipt] = useState<Receipt | null>(null),
    [showCustomer, setShowCustomer] = useState(false),
    [highlight, setHighlight] = useState(0),
    [customerHighlight, setCustomerHighlight] = useState(0);
  useEffect(() => {
    if (!receipt || autoPrintedFolio.current === receipt.folio || !loadPrintSettings().autoPrint) return;
    autoPrintedFolio.current = receipt.folio;
    const timer = window.setTimeout(() => {
      printReceipt(document.getElementById("current-sale-receipt")).catch((reason) => {
        setError(reason instanceof Error ? reason.message : "No se pudo imprimir el ticket automáticamente.");
      });
    }, 150);
    return () => window.clearTimeout(timer);
  }, [receipt]);
  const matches = useMemo(
    () =>
      query.trim()
        ? products
            .filter(
              (x) =>
                (allowNegativeStock || x.stock > 0) &&
                `${x.name} ${x.variant} ${x.sku} ${x.barcode ?? ""}`
                  .toLowerCase()
                  .includes(query.toLowerCase()),
            )
            .slice(0, 8)
        : [],
    [products, query],
  );
  const customerMatches = useMemo(
    () => customerQuery.trim() && !customerId
      ? customers.filter((customer) =>
          `${customer.name} ${customer.phone ?? ""} ${customer.email ?? ""}`
            .toLowerCase().includes(customerQuery.toLowerCase()),
        ).slice(0, 8)
      : [],
    [customers, customerId, customerQuery],
  );
  const subtotal = cart.reduce((s, x) => s + x.product.price * x.quantity, 0),
    discountValue = Math.min(Math.max(Number(discount) || 0, 0), subtotal),
    total = subtotal - discountValue,
    change = payment === 1 ? Math.max(0, Number(received || 0) - total) : 0;
  async function load() {
    try {
      const [p, b, c, current] = await Promise.all([
        apiRequest<ApiProduct[]>("/api/v1/products", {}, session),
        apiRequest<Branch[]>("/api/v1/branches", {}, session),
        apiRequest<Customer[]>("/api/v1/customers", {}, session),
        apiRequest<Cash | undefined>("/api/v1/cash/current", {}, session),
      ]);
      setProducts(p);
      setBranches(b);
      setCustomers(c);
      setCash(current ?? null);
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "No pudimos cargar el punto de venta.",
      );
    }
  }
  useEffect(() => {
    void load();
  }, []);
  function add(p: ApiProduct) {
    setCart((current) => {
      const found = current.find((x) => x.product.variantId === p.variantId);
      return found
        ? current.map((x) =>
            x === found
              ? { ...x, quantity: allowNegativeStock ? x.quantity + 1 : Math.min(x.quantity + 1, p.stock) }
              : x,
          )
        : [...current, { product: p, quantity: 1 }];
    });
    setQuery("");
  }
  function setQty(id: string, value: number) {
    setCart((c) =>
      c
        .map((x) =>
          x.product.variantId === id
            ? { ...x, quantity: allowNegativeStock ? Math.max(0, value) : Math.min(x.product.stock, Math.max(0, value)) }
            : x,
        )
        .filter((x) => x.quantity > 0),
    );
  }
  function key(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Escape") { setQuery(""); return; }
    if (!matches.length) return;
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setHighlight((x) => Math.min(x + 1, matches.length - 1));
    }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      setHighlight((x) => Math.max(x - 1, 0));
    }
    if (e.key === "Enter") {
      e.preventDefault();
      add(
        matches.find((x) => x.sku === query || x.barcode === query) ??
          matches[highlight],
      );
    }
  }
  function selectCustomer(customer: Customer) {
    setCustomerId(customer.id);
    setCustomerQuery(customer.name);
  }
  function customerKey(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Escape") {
      setCustomerQuery("");
      setCustomerId("");
      return;
    }
    if (!customerMatches.length) return;
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setCustomerHighlight((value) => Math.min(value + 1, customerMatches.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setCustomerHighlight((value) => Math.max(value - 1, 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      selectCustomer(customerMatches[customerHighlight] ?? customerMatches[0]);
    }
  }
  async function openCash(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      setCash(
        await apiRequest<Cash>(
          "/api/v1/cash/open",
          {
            method: "POST",
            body: JSON.stringify({
              branchId: branches[0].id,
              openingAmount: Number(opening),
            }),
          },
          session,
        ),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "No pudimos abrir la caja.");
    } finally {
      setBusy(false);
    }
  }
  async function newCustomer(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    setBusy(true);
    try {
      const r = await apiRequest<{ id: string }>(
        "/api/v1/customers",
        {
          method: "POST",
          body: JSON.stringify({
            name: d.get("name"),
            phone: d.get("phone") || null,
            email: d.get("email") || null,
            notes: null,
          }),
        },
        session,
      );
      await load();
      setCustomerId(r.id);
      const created = customers.find((customer) => customer.id === r.id);
      setCustomerQuery(created?.name ?? String(d.get("name") ?? ""));
      setShowCustomer(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No pudimos crear el cliente.");
    } finally {
      setBusy(false);
    }
  }
  async function charge() {
    if (!cash || !cart.length) return;
    if (discountValue > subtotal) {
      setError("El descuento no puede superar el subtotal.");
      return;
    }
    if (payment === 1 && Number(received) < total) {
      setError("El efectivo recibido no cubre el total.");
      return;
    }
    const mixedPayments = [
      {
        method: 1,
        amount: Number(cashPart) || 0,
        receivedAmount: Number(cashPart) || 0,
      },
      {
        method: 2,
        amount: Number(cardPart) || 0,
        receivedAmount: Number(cardPart) || 0,
      },
      {
        method: 3,
        amount: Number(transferPart) || 0,
        receivedAmount: Number(transferPart) || 0,
      },
    ].filter((part) => part.amount > 0);
    if (
      payment === 4 &&
      Math.abs(
        mixedPayments.reduce((sum, part) => sum + part.amount, 0) - total,
      ) > 0.009
    ) {
      setError(`La suma del pago mixto debe ser ${money.format(total)}.`);
      return;
    }
    setBusy(true);
    setError("");
    try {
      const r = await apiRequest<Receipt>(
        "/api/v1/sales",
        {
          method: "POST",
          body: JSON.stringify({
            branchId: cash.branchId,
            cashSessionId: cash.id,
            customerId: customerId || null,
            items: cart.map((x) => ({
              productVariantId: x.product.variantId,
              quantity: x.quantity,
            })),
            paymentMethod: payment === 4 ? 1 : payment,
            receivedAmount: payment === 1 ? Number(received) : total,
            payments: payment === 4 ? mixedPayments : null,
            discount: discountValue,
          }),
        },
        session,
      );
      const selectedCustomer = customers.find((customer) => customer.id === customerId);
      setReceipt({ ...r, customer: selectedCustomer?.name ?? "Público general", customerPhone: selectedCustomer?.phone, customerEmail: selectedCustomer?.email });
      setCart([]);
      setDiscount("0");
      setReceived("");
      setCashPart("");
      setCardPart("");
      setTransferPart("");
      setCustomerId("");
      setCustomerQuery("");
      await load();
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "No pudimos completar la venta.",
      );
    } finally {
      setBusy(false);
    }
  }
  function sendSaleWhatsApp(data: Receipt) {
    if (!planAccess.require("emailAndWhatsApp", "Tickets por WhatsApp")) return;
    if (!data.customerPhone) return;
    let phone = data.customerPhone.replace(/\D/g, ""); if (phone.length === 10) phone = `52${phone}`;
    const lines = data.items.map((item) => `• ${item.quantity} x ${item.productName}${item.variantName ? ` (${item.variantName})` : ""} — ${money.format(item.lineTotal)}`).join("\n");
    const text = `🧾 *${businessName ?? session.user.businessName}*\n*Ticket de venta*\n\nFolio: ${data.folio}\nFecha: ${new Date(data.soldAtUtc).toLocaleString("es-MX")}\nCliente: ${data.customer}\n\n${lines}\n\nSubtotal: ${money.format(data.subtotal)}\nDescuento: -${money.format(data.discount)}\n*TOTAL: ${money.format(data.total)}*\n\n${ticketMessage || "¡Gracias por tu compra!"}`;
    window.open(`https://wa.me/${phone}?text=${encodeURIComponent(text)}`, "_blank", "noopener,noreferrer");
  }
  async function sendSaleEmail(data: Receipt) {
    if (!planAccess.require("emailAndWhatsApp", "Tickets por correo")) return;
    const lines = data.items.map((item) => `${item.quantity} × ${item.productName}${item.variantName ? ` (${item.variantName})` : ""} — ${money.format(item.lineTotal)}`).join("\n");
    const content = `Folio: ${data.folio}\nFecha: ${new Date(data.soldAtUtc).toLocaleString("es-MX")}\nCliente: ${data.customer}\n\n${lines}\n\nSubtotal: ${money.format(data.subtotal)}\nDescuento: -${money.format(data.discount)}\nTOTAL: ${money.format(data.total)}\n\n${ticketMessage || "¡Gracias por tu compra!"}`;
    try {
      if (await emailDocument({ session, documentType: "sale-ticket", reference: data.folio, content, defaultEmail: data.customerEmail }))
        window.alert("Ticket enviado por correo.");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No pudimos enviar el ticket por correo.");
    }
  }
  if (!cash)
    return (
      <div className="content cash-open-page">
        <section className="cash-open-card">
          <div className="cash-illustration">
            <Banknote />
          </div>
          <h1>Abre tu caja</h1>
          <p>Registra el fondo inicial. Puede ser $0.</p>
          <form onSubmit={openCash}>
            <label>
              Fondo inicial
              <div className="currency-input">
                <span>$</span>
                <input
                  value={opening}
                  onChange={(e) => setOpening(e.target.value)}
                  type="number"
                  min="0"
                />
              </div>
            </label>
            {error && <div className="form-error">{error}</div>}
            <button
              className="button primary"
              disabled={busy || !branches.length}
            >
              Abrir caja
            </button>
          </form>
        </section>
      </div>
    );
  return (
    <div className="pos-page">
      <section className="pos-catalog">
        <div className="pos-heading">
          <div>
            <p className="eyebrow">PUNTO DE VENTA</p>
            <h1>Nueva venta</h1>
          </div>
          <span className="cash-badge">Caja abierta</span>
        </div>
        <label className="catalog-search pos-search">
          <Search />
          <input
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setHighlight(0);
            }}
            onKeyDown={key}
            autoFocus
            placeholder="Nombre, variante, SKU o código"
          />
        </label>
        {query && (
          <div className="pos-suggestions">
            {matches.map((p, i) => (
              <button
                className={i === highlight ? "active" : ""}
                key={p.variantId}
                onClick={() => add(p)}
              >
                <span>
                  <strong>{p.name}</strong>
                  <small>
                    {p.variant} · {p.sku}
                  </small>
                </span>
                <b>{money.format(p.price)}</b>
              </button>
            ))}
          </div>
        )}
        <div className="pos-products">
          {products
            .filter((x) => allowNegativeStock || x.stock > 0)
            .slice(0, 30)
            .map((p, i) => (
              <button key={p.variantId} onClick={() => add(p)}>
                {p.imageUrl ? (
                  <img className="pos-product-image" src={p.imageUrl} />
                ) : (
                  <div className={`product-art art-${(i % 3) + 1}`}>
                    <ShoppingBag />
                  </div>
                )}
                <strong>{p.name}</strong>
                <small>
                  {p.variant} · {p.sku}
                </small>
                <span>{money.format(p.price)}</span>
                <b>{p.stock} disp.</b>
              </button>
            ))}
        </div>
      </section>
      <aside className="cart-panel">
        <div className="cart-heading">
          <div>
            <ShoppingBag />
            <strong>Venta actual</strong>
          </div>
          <button onClick={() => setCart([])}>Limpiar</button>
        </div>
        <div className="cart-lines">
          {cart.map((x) => (
            <div className="cart-line" key={x.product.variantId}>
              <div>
                <strong>{x.product.name}</strong>
                <small>{x.product.variant}</small>
              </div>
              <div className="qty-control">
                <button
                  onClick={() => setQty(x.product.variantId, x.quantity - 1)}
                >
                  <Minus />
                </button>
                <input
                  value={x.quantity}
                  onChange={(e) =>
                    setQty(x.product.variantId, Number(e.target.value))
                  }
                />
                <button
                  onClick={() => setQty(x.product.variantId, x.quantity + 1)}
                >
                  <Plus />
                </button>
              </div>
              <strong>{money.format(x.product.price * x.quantity)}</strong>
              <button
                className="trash"
                onClick={() => setQty(x.product.variantId, 0)}
              >
                <Trash2 />
              </button>
            </div>
          ))}
        </div>
        <div className="cart-summary">
          <div className="customer-choice predictive-search pos-customer-search">
            <label className="catalog-search">
              <Search />
              <input
                value={customerQuery}
                onChange={(e) => {
                  setCustomerQuery(e.target.value);
                  setCustomerId("");
                  setCustomerHighlight(0);
                }}
                onKeyDown={customerKey}
                placeholder="Público general o buscar cliente"
              />
            </label>
            <button onClick={() => setShowCustomer(true)}>
              <UserPlus />
            </button>
            {customerMatches.length > 0 && (
              <div className="product-options predictive-options">
                {customerMatches.map((customer, index) => (
                  <button
                    type="button"
                    className={index === customerHighlight ? "active" : ""}
                    key={customer.id}
                    onMouseEnter={() => setCustomerHighlight(index)}
                    onClick={() => selectCustomer(customer)}
                  >
                    <span><strong>{customer.name}</strong><small>{customer.phone || customer.email || "Sin datos de contacto"}</small></span>
                  </button>
                ))}
              </div>
            )}
          </div>
          <label>
            Descuento ($)
            <input
              value={discount}
              onChange={(e) => setDiscount(e.target.value)}
              type="number"
              min="0"
              max={subtotal}
            />
          </label>
          <div>
            <span>Subtotal</span>
            <strong>{money.format(subtotal)}</strong>
          </div>
          <div>
            <span>Descuento</span>
            <strong>-{money.format(discountValue)}</strong>
          </div>
          <div className="cart-total">
            <span>Total</span>
            <strong>{money.format(total)}</strong>
          </div>
          <div className="payment-methods">
            <button
              className={payment === 1 ? "active" : ""}
              onClick={() => setPayment(1)}
            >
              <Banknote />
              Efectivo
            </button>
            <button
              className={payment === 2 ? "active" : ""}
              onClick={() => setPayment(2)}
            >
              <CreditCard />
              Tarjeta
            </button>
            <button
              className={payment === 3 ? "active" : ""}
              onClick={() => setPayment(3)}
            >
              <Landmark />
              Transferencia
            </button>
            <button
              className={payment === 4 ? "active" : ""}
              onClick={() => setPayment(4)}
            >
              <WalletCards />
              Mixto
            </button>
          </div>
          {payment === 1 && (
            <label>
              Efectivo recibido
              <input
                value={received}
                onChange={(e) => setReceived(e.target.value)}
                type="number"
                min="0"
              />
            </label>
          )}
          {payment === 4 && (
            <div className="mixed-payment-fields">
              <label>
                Efectivo
                <input
                  value={cashPart}
                  onChange={(e) => setCashPart(e.target.value)}
                  type="number"
                  min="0"
                  step=".01"
                />
              </label>
              <label>
                Tarjeta
                <input
                  value={cardPart}
                  onChange={(e) => setCardPart(e.target.value)}
                  type="number"
                  min="0"
                  step=".01"
                />
              </label>
              <label>
                Transferencia
                <input
                  value={transferPart}
                  onChange={(e) => setTransferPart(e.target.value)}
                  type="number"
                  min="0"
                  step=".01"
                />
              </label>
              <div>
                <span>Capturado</span>
                <strong>
                  {money.format(
                    (Number(cashPart) || 0) +
                      (Number(cardPart) || 0) +
                      (Number(transferPart) || 0),
                  )}
                </strong>
              </div>
            </div>
          )}
          {payment === 1 && (
            <div className="change-row">
              <span>Cambio</span>
              <strong>{money.format(change)}</strong>
            </div>
          )}
          {error && <div className="form-error">{error}</div>}
          <button
            className="charge-button"
            onClick={() => void charge()}
            disabled={busy || !cart.length}
          >
            Cobrar {money.format(total)}
          </button>
        </div>
      </aside>
      {showCustomer && (
        <div className="modal-layer">
          <form className="product-form" onSubmit={newCustomer}>
            <div className="form-heading">
              <h2>Cliente rápido</h2>
              <button
                type="button"
                className="close-form"
                onClick={() => setShowCustomer(false)}
              >
                ×
              </button>
            </div>
            <div className="form-grid">
              <label className="wide">
                Nombre
                <input name="name" required />
              </label>
              <label>
                Teléfono
                <input name="phone" />
              </label>
              <label>
                Correo
                <input name="email" type="email" />
              </label>
            </div>
            <div className="form-actions">
              <button className="button primary">Guardar y seleccionar</button>
            </div>
          </form>
        </div>
      )}
      {receipt && (
        <div className="modal-layer">
          <div className="receipt-modal">
            <button className="close-form" onClick={() => setReceipt(null)}>
              <X />
            </button>
            <div className="receipt" id="current-sale-receipt">
              <div className="receipt-brand">
                {logoUrl ? <img src={logoUrl} alt={businessName ?? "Logotipo"} /> : (businessName ?? session.user.businessName).slice(0, 2).toUpperCase()}
              </div>
              <h2>{businessName ?? session.user.businessName}</h2>
              <p>
                {receipt.folio}
                <br />
                {new Date(receipt.soldAtUtc).toLocaleString("es-MX")}
              </p>
              {receipt.items.map((x) => (
                <div className="ticket-line" key={x.sku}>
                  <span>
                    {x.quantity} × {x.productName}
                    <small>{x.variantName}</small>
                  </span>
                  <b>{money.format(x.lineTotal)}</b>
                </div>
              ))}
              <div className="ticket-payment">
                <span>Subtotal</span>
                <b>{money.format(receipt.subtotal)}</b>
                <span>Descuento</span>
                <b>-{money.format(receipt.discount)}</b>
              </div>
              <div className="ticket-total">
                <span>TOTAL</span>
                <b>{money.format(receipt.total)}</b>
              </div>
              <footer>{ticketMessage || "¡Gracias por tu compra!"}</footer>
            </div>
            <button
              className="button primary print-button"
              onClick={() => printReceipt(document.getElementById("current-sale-receipt"))}
            >
              <Printer />
              Imprimir ticket
            </button>
            <button className="button secondary print-button" disabled={!receipt.customerPhone} onClick={() => sendSaleWhatsApp(receipt)}><MessageCircle />Enviar por WhatsApp</button>
            <button className="button secondary print-button" onClick={() => void sendSaleEmail(receipt)}><Mail />Enviar por email</button>
          </div>
        </div>
      )}
    </div>
  );
}
