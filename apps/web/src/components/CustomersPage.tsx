import { Mail, Phone, Plus, Search, UserRound, X } from "lucide-react";
import { FormEvent, KeyboardEvent, useEffect, useMemo, useState } from "react";
import { apiRequest, AuthSession } from "../lib/api";

type Customer = {
  id: string;
  name: string;
  phone: string | null;
  email: string | null;
  notes: string | null;
  isActive: boolean;
  purchases: number;
  totalSpent: number;
};

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

export function CustomersPage({ session }: { session: AuthSession }) {
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [query, setQuery] = useState("");
  const [selectedSearchId, setSelectedSearchId] = useState<string | null>(null);
  const [selected, setSelected] = useState<Customer | null>(null);
  const [show, setShow] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [highlight, setHighlight] = useState(0);

  const matches = useMemo(() => {
    const term = normalizeSearch(query);
    if (!term) return [];
    return customers
      .filter((customer) =>
        normalizeSearch(
          `${customer.name} ${customer.phone ?? ""} ${customer.email ?? ""}`,
        ).includes(term),
      )
      .slice(0, 8);
  }, [customers, query]);

  function selectSearch(customer: Customer) {
    setSelectedSearchId(customer.id);
    setQuery(customer.name);
  }
  function searchKey(event: KeyboardEvent<HTMLInputElement>) {
    if (!matches.length) return;
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setHighlight((value) => Math.min(value + 1, matches.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setHighlight((value) => Math.max(value - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      selectSearch(matches[highlight] ?? matches[0]);
    } else if (event.key === "Escape") {
      setQuery("");
      setSelectedSearchId(null);
    }
  }

  const visible = useMemo(() => {
    if (selectedSearchId)
      return customers.filter((customer) => customer.id === selectedSearchId);
    const term = normalizeSearch(query);
    if (!term) return customers;
    return customers.filter((customer) =>
      normalizeSearch(
        `${customer.name} ${customer.phone ?? ""} ${customer.email ?? ""}`,
      ).includes(term),
    );
  }, [customers, query, selectedSearchId]);

  async function load() {
    try {
      setCustomers(
        await apiRequest<Customer[]>("/api/v1/customers", {}, session),
      );
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "No pudimos cargar los clientes.",
      );
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setBusy(true);
    setError("");
    const data = new FormData(e.currentTarget);
    const body = {
      name: data.get("name"),
      phone: data.get("phone") || null,
      email: data.get("email") || null,
      notes: data.get("notes") || null,
      isActive: true,
    };
    try {
      await apiRequest(
        selected ? `/api/v1/customers/${selected.id}` : "/api/v1/customers",
        { method: selected ? "PUT" : "POST", body: JSON.stringify(body) },
        session,
      );
      setShow(false);
      setSelected(null);
      await load();
    } catch (response) {
      setError(
        response instanceof Error
          ? response.message
          : "No pudimos guardar el cliente.",
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="content">
      <section className="page-title-row">
        <div>
          <p className="eyebrow">RELACIONES</p>
          <h1>Clientes</h1>
          <p>Consulta sus datos e historial de compras.</p>
        </div>
        <button
          className="button primary"
          onClick={() => {
            setSelected(null);
            setShow(true);
          }}
        >
          <Plus />
          Nuevo cliente
        </button>
      </section>
      {error && <div className="page-error">{error}</div>}
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
                  setHighlight(0);
                }}
                onKeyDown={searchKey}
                placeholder="Buscar nombre, teléfono o correo"
              />
            </label>
            {query.trim() && !selectedSearchId && (
              <div className="product-options predictive-options">
                {matches.length ? (
                  matches.map((customer, index) => (
                    <button
                      type="button"
                      className={index === highlight ? "active" : ""}
                      key={customer.id}
                      onMouseEnter={() => setHighlight(index)}
                      onClick={() => selectSearch(customer)}
                    >
                      <span>
                        <strong>{customer.name}</strong>
                        <small>
                          {[customer.phone, customer.email]
                            .filter(Boolean)
                            .join(" · ") || "Sin datos de contacto"}
                        </small>
                      </span>
                      <b>{customer.purchases} compras</b>
                    </button>
                  ))
                ) : (
                  <div className="predictive-empty">Sin coincidencias</div>
                )}
              </div>
            )}
          </div>
        </div>
        <div className="customer-grid">
          {!visible.length ? (
            <div className="empty-state">
              <UserRound />
              <strong>
                {query ? "No encontramos clientes" : "Aún no hay clientes"}
              </strong>
              <span>
                {query
                  ? "Prueba con otro nombre, teléfono o correo."
                  : "Las ventas pueden seguir registrándose como público general."}
              </span>
            </div>
          ) : (
            visible.map((customer) => (
              <article className="customer-card" key={customer.id}>
                <div className="customer-avatar">
                  <UserRound />
                </div>
                <div>
                  <strong>{customer.name}</strong>
                  {customer.phone && (
                    <span>
                      <Phone />
                      {customer.phone}
                    </span>
                  )}
                  {customer.email && (
                    <span>
                      <Mail />
                      {customer.email}
                    </span>
                  )}
                  <small>
                    {customer.purchases} compras ·{" "}
                    {money.format(customer.totalSpent)}
                  </small>
                </div>
                <button
                  className="text-button"
                  onClick={() => {
                    setSelected(customer);
                    setShow(true);
                  }}
                >
                  Editar
                </button>
              </article>
            ))
          )}
        </div>
      </section>
      {show && (
        <div className="modal-layer" onMouseDown={() => setShow(false)}>
          <form
            className="product-form"
            onSubmit={submit}
            onMouseDown={(e) => e.stopPropagation()}
          >
            <div className="form-heading">
              <div>
                <p className="eyebrow">CLIENTE</p>
                <h2>{selected ? "Editar cliente" : "Nuevo cliente"}</h2>
              </div>
              <button
                type="button"
                className="close-form"
                onClick={() => setShow(false)}
              >
                <X />
              </button>
            </div>
            <div className="form-grid">
              <label className="wide">
                Nombre
                <input name="name" defaultValue={selected?.name} required />
              </label>
              <label>
                Teléfono
                <input
                  name="phone"
                  type="tel"
                  defaultValue={selected?.phone ?? ""}
                />
              </label>
              <label>
                Correo
                <input
                  name="email"
                  type="email"
                  defaultValue={selected?.email ?? ""}
                />
              </label>
              <label className="wide">
                Notas
                <input
                  name="notes"
                  defaultValue={selected?.notes ?? ""}
                  placeholder="Preferencias, talla, observaciones..."
                />
              </label>
            </div>
            <div className="form-actions">
              <button
                type="button"
                className="button secondary"
                onClick={() => setShow(false)}
              >
                Cancelar
              </button>
              <button className="button primary" disabled={busy}>
                {busy ? "Guardando..." : "Guardar cliente"}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
