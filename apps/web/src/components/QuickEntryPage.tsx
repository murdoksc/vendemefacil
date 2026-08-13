import { ArrowDownToLine, Boxes, Clock3, Search } from "lucide-react";
import { FormEvent, KeyboardEvent, useEffect, useMemo, useState } from "react";
import { ApiProduct, apiRequest, AuthSession } from "../lib/api";

type Branch = { id: string; name: string; isMain: boolean };
type Movement = {
  id: string;
  productVariantId: string;
  product: string;
  variant: string;
  quantity: number;
  unitCost: number;
  note: string | null;
  createdAtUtc: string;
};
const money = new Intl.NumberFormat("es-MX", {
  style: "currency",
  currency: "MXN",
});

export function QuickEntryPage({ session }: { session: AuthSession }) {
  const [products, setProducts] = useState<ApiProduct[]>([]),
    [branches, setBranches] = useState<Branch[]>([]),
    [movements, setMovements] = useState<Movement[]>([]);
  const [query, setQuery] = useState(""),
    [selectedId, setSelectedId] = useState(""),
    [error, setError] = useState(""),
    [message, setMessage] = useState(""),
    [busy, setBusy] = useState(false),
    [highlight, setHighlight] = useState(0);
  const matches = useMemo(
    () =>
      products
        .filter((x) =>
          `${x.name} ${x.sku} ${x.barcode ?? ""}`
            .toLowerCase()
            .includes(query.toLowerCase()),
        )
        .slice(0, 8),
    [products, query],
  );
  const selected = products.find((x) => x.variantId === selectedId);
  function selectProduct(product: ApiProduct) {
    setSelectedId(product.variantId);
    setQuery(`${product.name} · ${product.variant}`);
  }
  function searchKey(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") { setQuery(""); setSelectedId(""); return; }
    if (!matches.length) return;
    if (event.key === "ArrowDown") { event.preventDefault(); setHighlight((value) => Math.min(value + 1, matches.length - 1)); }
    else if (event.key === "ArrowUp") { event.preventDefault(); setHighlight((value) => Math.max(value - 1, 0)); }
    else if (event.key === "Enter") { event.preventDefault(); selectProduct(matches[highlight] ?? matches[0]); }
  }

  async function load() {
    try {
      const [p, b, m] = await Promise.all([
        apiRequest<ApiProduct[]>("/api/v1/products", {}, session),
        apiRequest<Branch[]>("/api/v1/branches", {}, session),
        apiRequest<Movement[]>("/api/v1/inventory/movements", {}, session),
      ]);
      setProducts(p);
      setBranches(b);
      setMovements(m);
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "No pudimos cargar el inventario.",
      );
    }
  }
  useEffect(() => {
    void load();
  }, []);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedId) {
      setError("Selecciona un producto.");
      return;
    }
    const form = event.currentTarget;
    setBusy(true);
    setError("");
    setMessage("");
    const data = new FormData(form);
    try {
      await apiRequest(
        "/api/v1/inventory/quick-entry",
        {
          method: "POST",
          body: JSON.stringify({
            branchId: data.get("branchId"),
            productVariantId: selectedId,
            quantity: Number(data.get("quantity")),
            unitCost: Number(data.get("cost")),
            note: data.get("note"),
          }),
        },
        session,
      );
      setMessage("Entrada registrada. El inventario ya está actualizado.");
      setQuery("");
      setSelectedId("");
      form.reset();
      await load();
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "No pudimos registrar la entrada.",
      );
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="content quick-entry-page">
      <section className="page-title-row">
        <div>
          <p className="eyebrow">INVENTARIO</p>
          <h1>Entrada rápida</h1>
          <p>Agrega mercancía en unos cuantos pasos.</p>
        </div>
      </section>
      <div className="entry-layout">
        <form className="card entry-form-card" onSubmit={submit}>
          <div className="entry-step">
            <span>1</span>
            <div>
              <strong>Busca el producto</strong>
              <small>Por nombre, SKU o código de barras</small>
            </div>
          </div>
          <label className="catalog-search entry-search">
            <Search />
            <input
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                setSelectedId("");
                setHighlight(0);
              }}
              onKeyDown={searchKey}
              placeholder="Escanea o escribe para buscar"
            />
          </label>
          {query && !selected && (
            <div className="product-options">
              {matches.map((x, index) => (
                <button
                  type="button"
                  className={index === highlight ? "active" : ""}
                  key={x.variantId}
                  onMouseEnter={() => setHighlight(index)}
                  onClick={() => selectProduct(x)}
                >
                  <span>
                    <strong>{x.name}</strong>
                    <small>
                      {x.variant} · {x.sku}
                    </small>
                  </span>
                  <b>{x.stock} pzas.</b>
                </button>
              ))}
            </div>
          )}
          {selected && (
            <div className="selected-product">
              <div className="product-art">
                <Boxes />
              </div>
              <div>
                <strong>{selected.name}</strong>
                <span>
                  {selected.variant} · SKU {selected.sku}
                </span>
              </div>
              <b>{selected.stock} actuales</b>
            </div>
          )}
          <div className="entry-step second">
            <span>2</span>
            <div>
              <strong>Captura la entrada</strong>
              <small>El costo puede permanecer en $0</small>
            </div>
          </div>
          <div className="form-grid entry-fields">
            <label>
              Sucursal
              <select name="branchId" required>
                {branches.map((x) => (
                  <option key={x.id} value={x.id}>
                    {x.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Cantidad
              <input
                name="quantity"
                required
                type="number"
                min="0.001"
                step="0.001"
                placeholder="0"
              />
            </label>
            <label>
              Costo unitario
              <input
                name="cost"
                required
                type="number"
                min="0"
                step="0.01"
                defaultValue="0"
              />
            </label>
            <label>
              Nota
              <input name="note" placeholder="Opcional" />
            </label>
          </div>
          {error && <div className="form-error">{error}</div>}
          {message && <div className="form-success">{message}</div>}
          <button
            className="button primary entry-submit"
            disabled={busy || !selected}
          >
            {busy ? (
              "Guardando..."
            ) : (
              <>
                <ArrowDownToLine />
                Registrar entrada
              </>
            )}
          </button>
        </form>
        <section className="card movement-card">
          <div className="card-heading">
            <div>
              <p className="eyebrow">HISTORIAL</p>
              <h2>Entradas recientes</h2>
            </div>
            <Clock3 />
          </div>
          <div className="movement-list">
            {movements.length === 0 ? (
              <div className="empty-state">
                <Boxes />
                <strong>Sin movimientos aún</strong>
              </div>
            ) : (
              movements.map((x) => (
                <div className="movement-row" key={x.id}>
                  <div>
                    <strong>{x.product}</strong>
                    <span>
                      {x.variant} ·{" "}
                      {new Date(x.createdAtUtc).toLocaleDateString("es-MX")}
                    </span>
                  </div>
                  <div>
                    <b>+{x.quantity}</b>
                    <small>
                      {x.unitCost
                        ? money.format(x.unitCost)
                        : "Costo pendiente"}
                    </small>
                  </div>
                </div>
              ))
            )}
          </div>
        </section>
      </div>
    </div>
  );
}
