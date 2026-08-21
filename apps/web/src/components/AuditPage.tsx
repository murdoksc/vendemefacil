import { ArrowLeft, Clock, ShieldAlert, User, Globe, ChevronDown, ChevronUp } from "lucide-react";
import { useEffect, useState } from "react";
import { apiRequest, AuthSession } from "../lib/api";

type AuditEntry = {
  id: string;
  action: string;
  description: string;
  detailsJson?: string;
  performedByUser: string;
  clientCreatedAtUtc: string;
  ipAddress: string;
};

export function AuditPage({ session, onBack }: { session: AuthSession; onBack: () => void }) {
  const [logs, setLogs] = useState<AuditEntry[]>([]);
  const [error, setError] = useState("");
  const [expandedId, setExpandedId] = useState<string | null>(null);

  useEffect(() => {
    apiRequest<AuditEntry[]>("/api/v1/audit", {}, session)
      .then(setLogs)
      .catch((e) => setError(e instanceof Error ? e.message : "No pudimos cargar la bitácora."));
  }, [session]);

  const formatAction = (act: string) => {
    switch (act) {
      case "POS_MANUAL_DISCOUNT":
        return "🏷️ Descuento Manual";
      case "INVENTORY_ADJUSTMENT":
        return "📦 Ajuste Inventario";
      case "CATALOG_PRICE_CHANGE":
        return "💲 Cambio de Precios";
      case "SALE_CANCEL":
        return "❌ Venta Cancelada";
      case "CASH_SESSION_DIFF":
        return "⚠️ Diferencia de Caja";
      default:
        return `⚡ ${act}`;
    }
  };

  return (
    <div className="content">
      <section className="page-title-row">
        <div>
          <button
            className="text-button"
            onClick={onBack}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 4,
              marginBottom: 12,
              padding: 0,
              background: "none",
              border: "none",
              cursor: "pointer",
              color: "var(--text-muted)",
              fontWeight: 700,
            }}
          >
            <ArrowLeft size={16} /> Volver al menú
          </button>
          <p className="eyebrow">AUDITORÍA Y SEGURIDAD</p>
          <h1>Bitácora de Actividad</h1>
          <p>Historial inmutable de operaciones críticas realizadas en el sistema.</p>
        </div>
      </section>

      {error && <div className="page-error">{error}</div>}

      <div className="card" style={{ padding: 0, marginTop: 20, overflow: "hidden" }}>
        <div className="admin-table">
          <table>
            <thead>
              <tr>
                <th style={{ textAlign: "left" }}>Operación</th>
                <th style={{ textAlign: "left" }}>Descripción</th>
                <th style={{ textAlign: "left" }}>Usuario</th>
                <th style={{ textAlign: "left" }}>Fecha</th>
                <th style={{ textAlign: "left" }}>IP</th>
                <th style={{ textAlign: "center" }}>Detalles</th>
              </tr>
            </thead>
            <tbody>
              {logs.length === 0 ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: "center", padding: "30px", color: "var(--text-muted)" }}>
                    No se han registrado eventos de auditoría todavía.
                  </td>
                </tr>
              ) : (
                logs.map((log) => (
                  <tr key={log.id}>
                    <td>
                      <span style={{ fontWeight: 700, fontSize: "0.95em" }}>
                        {formatAction(log.action)}
                      </span>
                    </td>
                    <td>{log.description}</td>
                    <td>
                      <span style={{ display: "flex", alignItems: "center", gap: 5 }}>
                        <User size={13} style={{ color: "var(--text-muted)" }} />
                        {log.performedByUser}
                      </span>
                    </td>
                    <td>
                      <span
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: 5,
                          fontSize: "0.9em",
                          color: "var(--text-muted)",
                        }}
                      >
                        <Clock size={13} />
                        {new Date(log.clientCreatedAtUtc).toLocaleString("es-MX")}
                      </span>
                    </td>
                    <td>
                      <span
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: 5,
                          fontSize: "0.9em",
                          color: "var(--text-muted)",
                        }}
                      >
                        <Globe size={13} />
                        {log.ipAddress}
                      </span>
                    </td>
                    <td style={{ textAlign: "center" }}>
                      {log.detailsJson ? (
                        <button
                          className="text-button"
                          onClick={() => setExpandedId(expandedId === log.id ? null : log.id)}
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            gap: 4,
                            cursor: "pointer",
                            border: "none",
                            background: "none",
                            color: "var(--primary)",
                            fontWeight: 700,
                          }}
                        >
                          {expandedId === log.id ? (
                            <>
                              Ocultar <ChevronUp size={14} />
                            </>
                          ) : (
                            <>
                              Ver <ChevronDown size={14} />
                            </>
                          )}
                        </button>
                      ) : (
                        <span style={{ color: "var(--text-muted)", fontSize: "0.9em" }}>—</span>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Renderizado de detalles en formato JSON */}
      {expandedId && (
        <div
          className="card"
          style={{
            marginTop: 20,
            background: "#181a1b",
            color: "#a9dc76",
            border: "1px solid var(--border)",
            padding: 20,
            borderRadius: "12px",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: 12,
              borderBottom: "1px solid #2a2d2f",
              paddingBottom: 8,
            }}
          >
            <span style={{ color: "#fff", fontWeight: 700, fontSize: "0.95em" }}>
              📋 Valores y contexto técnico
            </span>
            <button
              className="text-button"
              onClick={() => setExpandedId(null)}
              style={{
                color: "#ff6188",
                border: "none",
                background: "none",
                cursor: "pointer",
                fontWeight: 700,
              }}
            >
              Cerrar detalles
            </button>
          </div>
          <pre
            style={{
              margin: 0,
              overflowX: "auto",
              fontFamily: "'Fira Code', Consolas, Monaco, monospace",
              fontSize: "0.9em",
              lineHeight: "1.4",
            }}
          >
            {JSON.stringify(JSON.parse(logs.find((x) => x.id === expandedId)?.detailsJson || "{}"), null, 2)}
          </pre>
        </div>
      )}
    </div>
  );
}
