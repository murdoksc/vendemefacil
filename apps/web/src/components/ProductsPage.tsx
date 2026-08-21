import {
  Boxes,
  Download,
  Image,
  Pencil,
  Plus,
  Search,
  Upload,
} from "lucide-react";
import { FormEvent, KeyboardEvent, useEffect, useMemo, useState } from "react";
import { ApiProduct, apiRequest, AuthSession } from "../lib/api";
import { queueLocalAudit } from "../lib/audit";
type Branch = { id: string; name: string };
type Category = { id: string; name: string };
const money = new Intl.NumberFormat("es-MX", {
  style: "currency",
  currency: "MXN",
});
const normalizeSearch = (value: string) =>
  value
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .toLowerCase()
    .trim();
export function ProductsPage({
  session,
  openCreate = 0,
}: {
  session: AuthSession;
  openCreate?: number;
}) {
  const [products, setProducts] = useState<ApiProduct[]>([]),
    [branches, setBranches] = useState<Branch[]>([]),
    [categories, setCategories] = useState<Category[]>([]),
    [query, setQuery] = useState(""),
    [selectedSearchId, setSelectedSearchId] = useState<string | null>(null),
    [mode, setMode] = useState<"new" | "edit" | "variant" | "adjust" | null>(
      null,
    ),
    [selected, setSelected] = useState<ApiProduct | null>(null),
    [nextSku, setNextSku] = useState(""),
    [error, setError] = useState(""),
    [success, setSuccess] = useState(""),
    [saving, setSaving] = useState(false),
    [newCategory, setNewCategory] = useState(""),
    [searchHighlight, setSearchHighlight] = useState(0);
  const matches = useMemo(() => {
    const term = normalizeSearch(query);
    if (!term) return [];
    return products
      .filter((x) =>
        normalizeSearch(
          `${x.name} ${x.variant} ${x.sku} ${x.barcode ?? ""} ${x.category ?? ""}`,
        ).includes(term),
      )
      .slice(0, 8);
  }, [products, query]);
  function selectSearch(product: ApiProduct) {
    setSelectedSearchId(product.variantId);
    setQuery(`${product.name} · ${product.variant}`);
  }
  function searchKey(event: KeyboardEvent<HTMLInputElement>) {
    if (!matches.length) return;
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setSearchHighlight((value) => Math.min(value + 1, matches.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setSearchHighlight((value) => Math.max(value - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      selectSearch(matches[searchHighlight] ?? matches[0]);
    } else if (event.key === "Escape") {
      setQuery("");
      setSelectedSearchId(null);
    }
  }
  const visible = useMemo(() => {
    if (selectedSearchId)
      return products.filter((x) => x.variantId === selectedSearchId);
    const term = normalizeSearch(query);
    if (!term) return products;
    return products.filter((x) =>
      normalizeSearch(
        `${x.name} ${x.variant} ${x.sku} ${x.barcode ?? ""} ${x.category ?? ""}`,
      ).includes(term),
    );
  }, [products, query, selectedSearchId]);
  async function load() {
    try {
      const [p, b, c] = await Promise.all([
        apiRequest<ApiProduct[]>("/api/v1/products", {}, session),
        apiRequest<Branch[]>("/api/v1/branches", {}, session),
        apiRequest<Category[]>("/api/v1/categories", {}, session),
      ]);
      setProducts(p);
      setBranches(b);
      setCategories(c);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No pudimos cargar productos.");
    }
  }
  useEffect(() => {
    void load();
  }, []);
  async function next() {
    setNextSku(
      (
        await apiRequest<{ sku: string }>(
          "/api/v1/products/next-sku",
          {},
          session,
        )
      ).sku,
    );
  }
  async function create() {
    setSelected(null);
    setMode("new");
    setError("");
    await next();
  }
  useEffect(() => {
    if (openCreate > 0) void create();
  }, [openCreate]);
  async function addCategory() {
    if (!newCategory.trim()) return;
    try {
      await apiRequest(
        "/api/v1/categories",
        { method: "POST", body: JSON.stringify({ name: newCategory }) },
        session,
      );
      setNewCategory("");
      await load();
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "No pudimos crear la categoría.",
      );
    }
  }
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setSaving(true);
    setError("");
    setSuccess("");
    const d = new FormData(e.currentTarget);
    const common = {
      variantName: d.get("variant"),
      sku: d.get("sku"),
      barcode: d.get("barcode") || null,
      cost: Number(d.get("cost")),
      price: Number(d.get("price")),
      minimumStock: Number(d.get("minimumStock") || 0),
      initialStock: Number(d.get("stock") || 0),
      branchId: branches[0]?.id ?? null,
    };
    try {
      if (mode === "variant")
        await apiRequest(
          `/api/v1/products/${selected!.id}/variants`,
          { method: "POST", body: JSON.stringify(common) },
          session,
        );
      else {
        const body = {
          ...common,
          name: d.get("name"),
          categoryId: d.get("categoryId") || null,
          imageUrl: d.get("imageUrl") || null,
          isActive: d.get("isActive") === "on",
        };
        await apiRequest(
          mode === "edit"
            ? `/api/v1/products/${selected!.variantId}`
            : "/api/v1/products",
          {
            method: mode === "edit" ? "PUT" : "POST",
            body: JSON.stringify(body),
          },
          session,
        );
      }

      if (mode === "edit" && session && selected) {
        const originalPrice = selected.price;
        const originalCost = selected.cost;
        const newPrice = Number(d.get("price"));
        const newCost = Number(d.get("cost"));

        if (originalPrice !== newPrice || originalCost !== newCost) {
          queueLocalAudit(
            "CATALOG_PRICE_CHANGE",
            `Se modificaron los precios de ${selected.name} (${selected.variant}). Precio: $${originalPrice} → $${newPrice}, Costo: $${originalCost} → $${newCost}.`,
            {
              productVariantId: selected.variantId,
              productName: selected.name,
              variantName: selected.variant,
              sku: selected.sku,
              oldPrice: originalPrice,
              newPrice: newPrice,
              oldCost: originalCost,
              newCost: newCost,
            },
            session.user.id,
            branches[0]?.id
          );
        }
      }

      setMode(null);
      setSelected(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No pudimos guardar.");
    } finally {
      setSaving(false);
    }
  }
  async function adjust(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setSaving(true);
    const d = new FormData(e.currentTarget);
    try {
      await apiRequest(
        "/api/v1/inventory/adjustment",
        {
          method: "POST",
          body: JSON.stringify({
            branchId: branches[0].id,
            productVariantId: selected!.variantId,
            quantityChange: Number(d.get("quantityChange")),
            reason: d.get("reason"),
          }),
        },
        session,
      );

      if (session && selected) {
        queueLocalAudit(
          "INVENTORY_ADJUSTMENT",
          `Ajuste manual de inventario para ${selected.name} (${selected.variant}): ${Number(d.get("quantityChange")) > 0 ? "+" : ""}${d.get("quantityChange")} piezas. Motivo: ${d.get("reason") || "Sin motivo"}.`,
          {
            productVariantId: selected.variantId,
            productName: selected.name,
            variantName: selected.variant,
            sku: selected.sku,
            quantityChange: Number(d.get("quantityChange")),
            reason: d.get("reason"),
          },
          session.user.id,
          branches[0]?.id
        );
      }

      setMode(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No pudimos ajustar.");
    } finally {
      setSaving(false);
    }
  }
  function openVariant(p: ApiProduct) {
    setSelected(p);
    setMode("variant");
    void next();
  }
  function csvCell(value: unknown) {
    return `"${String(value ?? "").replaceAll('"', '""')}"`;
  }
  function exportCsv() {
    const headers = [
      "Nombre",
      "Categoría",
      "URL imagen",
      "Variante",
      "SKU",
      "Código de barras",
      "Costo",
      "Precio",
      "Stock mínimo",
      "Existencia inicial",
    ];
    const rows = products.map((x) => [
      x.name,
      x.category ?? "",
      x.imageUrl ?? "",
      x.variant,
      x.sku,
      x.barcode ?? "",
      x.cost,
      x.price,
      x.minimumStock,
      x.stock,
    ]);
    const text =
      "\uFEFF" +
      [headers, ...rows].map((r) => r.map(csvCell).join(",")).join("\r\n");
    const a = document.createElement("a");
    a.href = URL.createObjectURL(
      new Blob([text], { type: "text/csv;charset=utf-8" }),
    );
    a.download = "catalogo-vendemefacil.csv";
    a.click();
    URL.revokeObjectURL(a.href);
  }
  function parseLine(line: string, delimiter: string) {
    const cells: string[] = [];
    let value = "",
      quoted = false;
    for (let i = 0; i < line.length; i++) {
      const c = line[i];
      if (c === '"' && quoted && line[i + 1] === '"') {
        value += '"';
        i++;
      } else if (c === '"') quoted = !quoted;
      else if (c === delimiter && !quoted) {
        cells.push(value);
        value = "";
      } else value += c;
    }
    cells.push(value);
    return cells;
  }
  async function importCsv(file: File) {
    setSaving(true);
    setError("");
    try {
      if (!branches.length)
        throw new Error(
          "No hay una sucursal disponible para recibir la existencia.",
        );
      const lines = (await file.text())
        .replace(/^\uFEFF/, "")
        .split(/\r?\n/)
        .filter((line) => line.trim());
      if (lines.length < 2)
        throw new Error("El CSV está vacío o no contiene filas de productos.");
      const delimiter =
        (lines[0].match(/;/g)?.length ?? 0) >
        (lines[0].match(/,/g)?.length ?? 0)
          ? ";"
          : ",";
      const header = parseLine(lines[0], delimiter).map(normalizeSearch);
      const expected = [
        "nombre",
        "categoria",
        "url imagen",
        "variante",
        "sku",
        "codigo de barras",
        "costo",
        "precio",
        "stock minimo",
        "existencia inicial",
      ];
      const invalidHeader = expected.findIndex(
        (column, index) => header[index] !== column,
      );
      if (invalidHeader >= 0)
        throw new Error(
          `Encabezado inválido: la columna ${invalidHeader + 1} debe llamarse “${expected[invalidHeader]}”. Descarga la plantilla con Exportar Excel.`,
        );
      if (lines.length - 1 > 2000)
        throw new Error(
          "El archivo supera el límite de 2,000 productos por importación.",
        );

      const numberValue = (
        value: string | undefined,
        row: number,
        column: string,
      ) => {
        const raw = (value ?? "").trim();
        if (!raw) return 0;
        const parsed = Number(raw.replace(/\s/g, "").replace(",", "."));
        if (!Number.isFinite(parsed))
          throw new Error(
            `Fila ${row}: “${raw}” no es un número válido para ${column}.`,
          );
        if (parsed < 0)
          throw new Error(`Fila ${row}: ${column} no puede ser negativo.`);
        return parsed;
      };
      const rows = lines.slice(1).map((line, index) => {
        const rowNumber = index + 2;
        const cells = parseLine(line, delimiter);
        if (cells.length !== 10)
          throw new Error(
            `Fila ${rowNumber}: se esperaban 10 columnas, pero se encontraron ${cells.length}. Revisa comas, punto y coma o comillas.`,
          );
        if (!cells[0]?.trim())
          throw new Error(
            `Fila ${rowNumber}: el nombre del producto es obligatorio.`,
          );
        if (!cells[4]?.trim())
          throw new Error(`Fila ${rowNumber}: el SKU es obligatorio.`);
        return {
          name: cells[0].trim(),
          category: cells[1]?.trim() || null,
          imageUrl: cells[2]?.trim() || null,
          variant: cells[3]?.trim() || "Única",
          sku: cells[4].trim(),
          barcode: cells[5]?.trim() || null,
          cost: numberValue(cells[6], rowNumber, "costo"),
          price: numberValue(cells[7], rowNumber, "precio"),
          minimumStock: numberValue(cells[8], rowNumber, "stock mínimo"),
          initialStock: numberValue(cells[9], rowNumber, "existencia inicial"),
        };
      });
      const result = await apiRequest<{ imported: number; updated: number }>(
        "/api/v1/products/import",
        {
          method: "POST",
          body: JSON.stringify({ branchId: branches[0].id, rows }),
        },
        session,
      );
      await load();
      setSuccess(
        `Importación terminada: ${result.imported} producto${result.imported === 1 ? " creado" : "s creados"} y ${result.updated} producto${result.updated === 1 ? " actualizado" : "s actualizados"}.`,
      );
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "No pudimos importar el archivo.",
      );
    } finally {
      setSaving(false);
    }
  }
  return (
    <div className="content">
      <section className="page-title-row">
        <div>
          <p className="eyebrow">CATÁLOGO</p>
          <h1>Productos y variantes</h1>
          <p>
            Organiza fotografías, categorías, tallas, colores y presentaciones.
          </p>
        </div>
        <div className="product-actions">
          <button className="button secondary" onClick={exportCsv}>
            <Download />
            Exportar Excel
          </button>
          <label className="button secondary">
            <Upload />
            Importar CSV
            <input
              type="file"
              accept=".csv,text/csv"
              hidden
              onChange={(e) => {
                const input = e.currentTarget;
                const file = input.files?.[0];
                if (file)
                  void importCsv(file).finally(() => {
                    input.value = "";
                  });
              }}
            />
          </label>
          <button className="button primary" onClick={() => void create()}>
            <Plus />
            Nuevo producto
          </button>
        </div>
      </section>
      {error && <div className="page-error">{error}</div>}
      {success && <div className="form-success import-success">{success}</div>}
      <section className="category-tools card">
        <strong>Categorías</strong>
        <div>
          {categories.map((x) => (
            <span key={x.id}>{x.name}</span>
          ))}
        </div>
        <input
          value={newCategory}
          onChange={(e) => setNewCategory(e.target.value)}
          placeholder="Nueva categoría"
        />
        <button className="button secondary" onClick={() => void addCategory()}>
          Agregar
        </button>
      </section>
      <section className="card catalog-card">
        <div className="catalog-toolbar">
          <div className="predictive-search">
            <label className="catalog-search">
              <Search />
              <input
                value={query}
                onChange={(e) => {
                  setQuery(e.target.value);
                  setSelectedSearchId(null);
                  setSearchHighlight(0);
                }}
                onKeyDown={searchKey}
                placeholder="Nombre, categoría, variante, SKU o código"
              />
            </label>
            {query.trim() && !selectedSearchId && (
              <div className="product-options predictive-options">
                {matches.length ? (
                  matches.map((product, index) => (
                    <button
                      type="button"
                      className={index === searchHighlight ? "active" : ""}
                      key={product.variantId}
                      onMouseEnter={() => setSearchHighlight(index)}
                      onClick={() => selectSearch(product)}
                    >
                      <span>
                        <strong>{product.name}</strong>
                        <small>
                          {product.variant} · SKU {product.sku}
                          {product.category ? ` · ${product.category}` : ""}
                        </small>
                      </span>
                      <b>{product.stock} disponibles</b>
                    </button>
                  ))
                ) : (
                  <div className="predictive-empty">Sin coincidencias</div>
                )}
              </div>
            )}
          </div>
        </div>
        <div className="catalog-head">
          <span>Producto</span>
          <span>Existencia</span>
          <span>Costo</span>
          <span>Precio</span>
          <span>Estado</span>
          <span />
        </div>
        {visible.map((p, i) => (
          <div className="catalog-row" key={p.variantId}>
            <div className="catalog-product">
              {p.imageUrl ? (
                <img className="product-thumb" src={p.imageUrl} alt="" />
              ) : (
                <div className={`product-art art-${(i % 3) + 1}`}>
                  <Image />
                </div>
              )}
              <div>
                <strong>{p.name}</strong>
                <small>
                  {p.category ?? "Sin categoría"} · {p.variant} · SKU {p.sku}
                </small>
                <button
                  className="inline-action"
                  onClick={() => openVariant(p)}
                >
                  + Otra variante
                </button>
              </div>
            </div>
            <button
              className={
                p.stock <= p.minimumStock ? "stock-pill low" : "stock-pill"
              }
              onClick={() => {
                setSelected(p);
                setMode("adjust");
              }}
            >
              {p.stock} pzas.
            </button>
            <strong>{p.cost ? money.format(p.cost) : "Pendiente"}</strong>
            <strong>{money.format(p.price)}</strong>
            <span className="status-pill">Activo</span>
            <button
              className="icon-button"
              onClick={() => {
                setSelected(p);
                setMode("edit");
              }}
            >
              <Pencil />
            </button>
          </div>
        ))}
      </section>
      {(mode === "new" || mode === "edit" || mode === "variant") && (
        <div className="modal-layer">
          <form className="product-form" onSubmit={submit}>
            <div className="form-heading">
              <div>
                <p className="eyebrow">
                  {mode === "variant" ? "VARIANTE" : "PRODUCTO"}
                </p>
                <h2>
                  {mode === "new"
                    ? "Nuevo producto"
                    : mode === "variant"
                      ? `Nueva variante de ${selected?.name}`
                      : "Editar producto"}
                </h2>
              </div>
              <button
                type="button"
                className="close-form"
                onClick={() => setMode(null)}
              >
                ×
              </button>
            </div>
            <div className="form-grid">
              {mode !== "variant" && (
                <>
                  <label className="wide">
                    Nombre
                    <input name="name" defaultValue={selected?.name} required />
                  </label>
                  <label>
                    Categoría
                    <select
                      name="categoryId"
                      defaultValue={selected?.categoryId ?? ""}
                    >
                      <option value="">Sin categoría</option>
                      {categories.map((x) => (
                        <option value={x.id} key={x.id}>
                          {x.name}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>
                    URL de fotografía
                    <input
                      name="imageUrl"
                      type="url"
                      defaultValue={selected?.imageUrl ?? ""}
                    />
                  </label>
                </>
              )}
              <label>
                Variante (talla/color)
                <input
                  name="variant"
                  defaultValue={mode === "variant" ? "" : selected?.variant}
                  placeholder="Ej. Talla 26 · Negro"
                />
              </label>
              <label>
                SKU
                <input
                  name="sku"
                  key={mode === "edit" ? selected?.sku : nextSku}
                  defaultValue={mode === "edit" ? selected?.sku : nextSku}
                  required
                />
              </label>
              <label>
                Código de barras
                <input
                  name="barcode"
                  defaultValue={
                    mode === "edit" ? (selected?.barcode ?? "") : ""
                  }
                />
              </label>
              <label>
                Costo
                <input
                  name="cost"
                  type="number"
                  min="0"
                  step=".01"
                  defaultValue={mode === "edit" ? selected?.cost : 0}
                />
              </label>
              <label>
                Precio
                <input
                  name="price"
                  type="number"
                  min="0"
                  step=".01"
                  defaultValue={mode === "edit" ? selected?.price : 0}
                />
              </label>
              <label>
                Stock mínimo
                <input
                  name="minimumStock"
                  type="number"
                  min="0"
                  defaultValue={mode === "edit" ? selected?.minimumStock : 0}
                />
              </label>
              {mode !== "edit" && (
                <label>
                  Existencia inicial
                  <input name="stock" type="number" min="0" defaultValue="0" />
                </label>
              )}
              <label className="checkbox-label">
                <input name="isActive" type="checkbox" defaultChecked /> Activo
              </label>
            </div>
            <div className="form-actions">
              <button
                type="button"
                className="button secondary"
                onClick={() => setMode(null)}
              >
                Cancelar
              </button>
              <button className="button primary" disabled={saving}>
                {saving ? "Guardando..." : "Guardar"}
              </button>
            </div>
          </form>
        </div>
      )}
      {mode === "adjust" && selected && (
        <div className="modal-layer">
          <form className="product-form" onSubmit={adjust}>
            <div className="form-heading">
              <h2>Ajustar {selected.name}</h2>
              <button
                type="button"
                className="close-form"
                onClick={() => setMode(null)}
              >
                ×
              </button>
            </div>
            <div className="form-grid">
              <label>
                Cantidad
                <input
                  name="quantityChange"
                  type="number"
                  step=".001"
                  required
                />
              </label>
              <label>
                Motivo
                <input name="reason" required />
              </label>
            </div>
            <div className="form-actions">
              <button className="button primary">Aplicar</button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
