import { ClipboardCheck, History, PackagePlus, Search } from "lucide-react";
import { KeyboardEvent, useEffect, useMemo, useState } from "react";
import { ApiProduct, apiRequest, AuthSession } from "../lib/api";
import { QuickEntryPage } from "./QuickEntryPage";
type Branch = { id: string; name: string };
type Movement = {
  id: string;
  productVariantId: string;
  createdAtUtc: string;
  type: string;
  quantity: number;
  note: string | null;
  product: string;
  variant: string;
  sku: string;
  user: string | null;
};
const labels: Record<string, string> = {
  InitialStock: "Inicial",
  Entry: "Entrada",
  Sale: "Venta",
  Return: "Devolución",
  Adjustment: "Ajuste",
  Layaway: "Apartado",
};
export function InventoryPage({ session }: { session: AuthSession }) {
  const [tab, setTab] = useState<"entry" | "kardex" | "count">("entry"),
    [products, setProducts] = useState<ApiProduct[]>([]),
    [branches, setBranches] = useState<Branch[]>([]),
    [moves, setMoves] = useState<Movement[]>([]),
    [filter, setFilter] = useState(""),
    [selectedProductId, setSelectedProductId] = useState(""),
    [filterHighlight, setFilterHighlight] = useState(0),
    [counts, setCounts] = useState<Record<string, string>>({}),
    [note, setNote] = useState(""),
    [message, setMessage] = useState(""),
    [error, setError] = useState("");
  async function load() {
    try {
      const [p, b, m] = await Promise.all([
        apiRequest<ApiProduct[]>("/api/v1/products", {}, session),
        apiRequest<Branch[]>("/api/v1/branches", {}, session),
        apiRequest<Movement[]>("/api/v1/inventory/kardex", {}, session),
      ]);
      setProducts(p);
      setBranches(b);
      setMoves(m);
      setCounts(
        Object.fromEntries(p.map((x) => [x.variantId, String(x.stock)])),
      );
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "No pudimos cargar inventario.",
      );
    }
  }
  useEffect(() => {
    void load();
  }, []);
  async function applyCount() {
    try {
      await apiRequest(
        "/api/v1/inventory/physical-count",
        {
          method: "POST",
          body: JSON.stringify({
            branchId: branches[0].id,
            note,
            items: products.map((x) => ({
              productVariantId: x.variantId,
              countedQuantity: Number(counts[x.variantId] || 0),
            })),
          }),
        },
        session,
      );
      setMessage("Conteo aplicado.");
      await load();
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "No pudimos aplicar el conteo.",
      );
    }
  }
  const productMatches = useMemo(() => filter.trim() && !selectedProductId
    ? products.filter((x) => `${x.name} ${x.variant} ${x.sku} ${x.barcode ?? ""}`.toLowerCase().includes(filter.toLowerCase())).slice(0, 8)
    : [], [products, filter, selectedProductId]);
  const visible = moves.filter((x) => selectedProductId
    ? x.productVariantId === selectedProductId
    : `${x.product} ${x.variant} ${x.sku} ${labels[x.type] ?? x.type}`.toLowerCase().includes(filter.toLowerCase()));
  function selectProduct(product: ApiProduct) {
    setSelectedProductId(product.variantId);
    setFilter(`${product.name} · ${product.variant}`);
  }
  function filterKey(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") { setFilter(""); setSelectedProductId(""); return; }
    if (!productMatches.length) return;
    if (event.key === "ArrowDown") { event.preventDefault(); setFilterHighlight((value) => Math.min(value + 1, productMatches.length - 1)); }
    else if (event.key === "ArrowUp") { event.preventDefault(); setFilterHighlight((value) => Math.max(value - 1, 0)); }
    else if (event.key === "Enter") { event.preventDefault(); selectProduct(productMatches[filterHighlight] ?? productMatches[0]); }
  }
  return (
    <div>
      <div className="inventory-tabs">
        <button
          className={tab === "entry" ? "active" : ""}
          onClick={() => setTab("entry")}
        >
          <PackagePlus />
          Entrada
        </button>
        <button
          className={tab === "kardex" ? "active" : ""}
          onClick={() => setTab("kardex")}
        >
          <History />
          Kardex
        </button>
        <button
          className={tab === "count" ? "active" : ""}
          onClick={() => setTab("count")}
        >
          <ClipboardCheck />
          Conteo
        </button>
      </div>
      {tab === "entry" ? (
        <QuickEntryPage session={session} />
      ) : (
        <div className="content inventory-tools">
          {error && <div className="page-error">{error}</div>}
          {tab === "kardex" ? (
            <section className="card">
              <div className="catalog-toolbar">
                <div className="predictive-search">
                  <label className="catalog-search">
                    <Search />
                    <input
                      value={filter}
                      onChange={(e) => { setFilter(e.target.value); setSelectedProductId(""); setFilterHighlight(0); }}
                      onKeyDown={filterKey}
                      placeholder="Buscar producto, variante, SKU o código"
                    />
                  </label>
                  {productMatches.length > 0 && (
                    <div className="product-options predictive-options">
                      {productMatches.map((product, index) => (
                        <button type="button" className={index === filterHighlight ? "active" : ""} key={product.variantId} onMouseEnter={() => setFilterHighlight(index)} onClick={() => selectProduct(product)}>
                          <span><strong>{product.name}</strong><small>{product.variant} · SKU {product.sku}</small></span>
                          <b>{product.stock} disponibles</b>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              </div>
              <div className="kardex-head">
                <span>Fecha</span>
                <span>Producto</span>
                <span>Movimiento</span>
                <span>Cantidad</span>
                <span>Usuario</span>
              </div>
              {visible.map((x) => (
                <div className="kardex-row" key={x.id}>
                  <span>
                    {new Date(x.createdAtUtc).toLocaleString("es-MX")}
                  </span>
                  <span>
                    <strong>{x.product}</strong>
                    <small>
                      {x.variant} · {x.sku}
                    </small>
                  </span>
                  <span>
                    {labels[x.type] ?? x.type}
                    <small>{x.note}</small>
                  </span>
                  <strong className={x.quantity < 0 ? "negative" : ""}>
                    {x.quantity > 0 ? "+" : ""}
                    {x.quantity}
                  </strong>
                  <span>{x.user ?? "Sistema"}</span>
                </div>
              ))}
            </section>
          ) : (
            <section className="card count-card">
              <h1>Conteo físico</h1>
              <p>
                Captura la cantidad real. Se registrarán solamente las
                diferencias.
              </p>
              <label>
                Nota
                <input
                  value={note}
                  onChange={(e) => setNote(e.target.value)}
                  placeholder="Conteo mensual..."
                />
              </label>
              <div className="count-list">
                {products.map((x) => (
                  <div key={x.variantId}>
                    <span>
                      <strong>{x.name}</strong>
                      <small>
                        {x.variant} · {x.sku} · sistema: {x.stock}
                      </small>
                    </span>
                    <input
                      type="number"
                      min="0"
                      step=".001"
                      value={counts[x.variantId] ?? ""}
                      onChange={(e) =>
                        setCounts((v) => ({
                          ...v,
                          [x.variantId]: e.target.value,
                        }))
                      }
                    />
                  </div>
                ))}
              </div>
              {message && <div className="form-success">{message}</div>}
              <button
                className="button primary"
                onClick={() => void applyCount()}
              >
                Aplicar conteo
              </button>
            </section>
          )}
        </div>
      )}
    </div>
  );
}
