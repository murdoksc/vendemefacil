import {
  Check,
  ImageOff,
  LayoutTemplate,
  Palette,
  Printer,
  PackageMinus,
  RefreshCw,
  Save,
  Store,
} from "lucide-react";
import { FormEvent, useEffect, useState } from "react";
import { apiRequest, AuthSession } from "../lib/api";
import {
  isQzConnected,
  listQzPrinters,
  loadPrintSettings,
  LocalPrintSettings,
  printQzTest,
  savePrintSettings,
} from "../lib/qzPrinting";
import { usePlanAccess } from "./PlanAccess";

export type BusinessSettings = {
  name: string;
  slug: string;
  primaryColor: string;
  accentColor: string;
  buttonColor: string;
  hoverColor: string;
  backgroundColor: string;
  surfaceColor: string;
  textColor: string;
  cornerRadius: number;
  layawayReminderDaysBefore: number;
  allowNegativeStock: boolean;
  logoUrl: string | null;
  operationMode: string;
  phone: string | null;
  address: string | null;
  ticketMessage: string | null;
};

type Theme = Pick<
  BusinessSettings,
  | "primaryColor"
  | "accentColor"
  | "buttonColor"
  | "hoverColor"
  | "backgroundColor"
  | "surfaceColor"
  | "textColor"
  | "cornerRadius"
> & { label: string };
type ColorKey =
  | "primaryColor"
  | "accentColor"
  | "buttonColor"
  | "hoverColor"
  | "backgroundColor"
  | "surfaceColor"
  | "textColor";
const themes: Theme[] = [
  {
    label: "Véndeme Fácil",
    primaryColor: "#153f35",
    accentColor: "#f5c45e",
    buttonColor: "#196651",
    hoverColor: "#124f3f",
    backgroundColor: "#f4f5ef",
    surfaceColor: "#ffffff",
    textColor: "#17251f",
    cornerRadius: 12,
  },
  {
    label: "Océano",
    primaryColor: "#123b5d",
    accentColor: "#5bc0eb",
    buttonColor: "#176b9b",
    hoverColor: "#0e5279",
    backgroundColor: "#f1f7fb",
    surfaceColor: "#ffffff",
    textColor: "#132d40",
    cornerRadius: 12,
  },
  {
    label: "Terracota",
    primaryColor: "#6b3428",
    accentColor: "#f0a868",
    buttonColor: "#a34f3a",
    hoverColor: "#7f3829",
    backgroundColor: "#fbf5f1",
    surfaceColor: "#ffffff",
    textColor: "#39241f",
    cornerRadius: 10,
  },
  {
    label: "Lavanda",
    primaryColor: "#40345f",
    accentColor: "#c6a7f2",
    buttonColor: "#7354a8",
    hoverColor: "#573b89",
    backgroundColor: "#f6f3fb",
    surfaceColor: "#ffffff",
    textColor: "#29223a",
    cornerRadius: 16,
  },
  {
    label: "Noche",
    primaryColor: "#20252b",
    accentColor: "#8ed081",
    buttonColor: "#3f8f68",
    hoverColor: "#2d704f",
    backgroundColor: "#eef1f2",
    surfaceColor: "#ffffff",
    textColor: "#20252b",
    cornerRadius: 8,
  },
  {
    label: "Rosa editorial",
    primaryColor: "#5a2638",
    accentColor: "#f5b7c7",
    buttonColor: "#b4476a",
    hoverColor: "#8f3452",
    backgroundColor: "#fff5f7",
    surfaceColor: "#ffffff",
    textColor: "#3b2029",
    cornerRadius: 18,
  },
  {
    label: "Minimal",
    primaryColor: "#202020",
    accentColor: "#e8e8e8",
    buttonColor: "#202020",
    hoverColor: "#444444",
    backgroundColor: "#f7f7f7",
    surfaceColor: "#ffffff",
    textColor: "#191919",
    cornerRadius: 4,
  },
  {
    label: "Eléctrico",
    primaryColor: "#27205f",
    accentColor: "#c7ff4a",
    buttonColor: "#5b45d6",
    hoverColor: "#4431ae",
    backgroundColor: "#f4f3ff",
    surfaceColor: "#ffffff",
    textColor: "#211d42",
    cornerRadius: 14,
  },
];
const colorFields: { key: ColorKey; label: string; description: string }[] = [
  {
    key: "primaryColor",
    label: "Navegación",
    description: "Menú lateral y marca",
  },
  {
    key: "accentColor",
    label: "Acento",
    description: "Selecciones y distintivos",
  },
  { key: "buttonColor", label: "Botones", description: "Acciones principales" },
  { key: "hoverColor", label: "Hover", description: "Al pasar sobre botones" },
  { key: "backgroundColor", label: "Fondo", description: "Área de trabajo" },
  {
    key: "surfaceColor",
    label: "Tarjetas",
    description: "Paneles y formularios",
  },
  { key: "textColor", label: "Texto", description: "Títulos y contenido" },
];

export function BusinessSettingsPage({
  session,
  onSaved,
}: {
  session: AuthSession;
  onSaved: (settings: BusinessSettings) => void;
}) {
  const planAccess = usePlanAccess();
  const [settings, setSettings] = useState<BusinessSettings | null>(null);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [logoFailed, setLogoFailed] = useState(false);
  const [printSettings, setPrintSettings] = useState<LocalPrintSettings>(loadPrintSettings);
  const [printers, setPrinters] = useState<string[]>([]);
  const [qzStatus, setQzStatus] = useState<"idle" | "busy" | "connected" | "error">(isQzConnected() ? "connected" : "idle");
  const [printMessage, setPrintMessage] = useState("");
  useEffect(() => {
    apiRequest<BusinessSettings>("/api/v1/business/settings", {}, session)
      .then(setSettings)
      .catch((reason) => setError(reason.message));
  }, []);
  function change(update: Partial<BusinessSettings>) {
    setSettings((value) => (value ? { ...value, ...update } : value));
    setSaved(false);
    setError("");
  }
  function changePrint(update: Partial<LocalPrintSettings>) {
    if (update.mode === "qz" && !planAccess.require("silentPrinting", "Impresión silenciosa con QZ Tray")) return;
    const next = { ...printSettings, ...update };
    setPrintSettings(next);
    savePrintSettings(next);
    setPrintMessage("Configuración de esta caja guardada.");
  }
  async function detectPrinters() {
    if (!planAccess.require("silentPrinting", "Impresión silenciosa con QZ Tray")) return;
    setQzStatus("busy");
    setPrintMessage("");
    try {
      const found = await listQzPrinters();
      setPrinters(found);
      setQzStatus("connected");
      setPrintMessage(found.length ? `${found.length} impresora(s) encontrada(s).` : "QZ Tray está conectado, pero Windows no reportó impresoras.");
    } catch {
      setQzStatus("error");
      setPrintMessage("No se pudo conectar con QZ Tray. Instálalo, ábrelo y vuelve a intentar.");
    }
  }
  async function testPrinter() {
    if (!planAccess.require("silentPrinting", "Impresión silenciosa con QZ Tray")) return;
    setQzStatus("busy");
    setPrintMessage("");
    try {
      await printQzTest(printSettings);
      setQzStatus("connected");
      setPrintMessage("Ticket de prueba enviado correctamente.");
    } catch (reason) {
      setQzStatus("error");
      setPrintMessage(reason instanceof Error ? reason.message : "No se pudo enviar la impresión de prueba.");
    }
  }
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!settings) return;
    setBusy(true);
    setError("");
    try {
      const result = await apiRequest<BusinessSettings>(
        "/api/v1/business/settings",
        { method: "PUT", body: JSON.stringify(settings) },
        session,
      );
      setSettings(result);
      onSaved(result);
      setSaved(true);
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "No pudimos guardar la configuración.",
      );
    } finally {
      setBusy(false);
    }
  }
  if (!settings)
    return (
      <div className="content">
        <div className="empty-state">Cargando personalización...</div>
      </div>
    );
  const selectedTheme = themes.find(
    (theme) =>
      colorFields.every(({ key }) => theme[key] === settings[key]) &&
      theme.cornerRadius === settings.cornerRadius,
  )?.label;
  return (
    <div className="content settings-page">
      <section className="page-title-row">
        <div>
          <p className="eyebrow">PERSONALIZACIÓN</p>
          <h1>Diseña la experiencia de tu negocio</h1>
          <p>
            Tu identidad se aplica al menú, botones, fondos, tarjetas y tickets.
          </p>
        </div>
      </section>
      <form className="settings-studio" onSubmit={submit}>
        <div className="settings-controls">
          <section className="card settings-section">
            <div className="settings-section-title">
              <span>
                <Store />
              </span>
              <div>
                <h2>Identidad</h2>
                <p>Información que verán tus clientes.</p>
              </div>
            </div>
            <div className="form-grid">
              <label className="wide">
                Nombre del negocio
                <input
                  value={settings.name}
                  onChange={(e) => change({ name: e.target.value })}
                  required
                />
              </label>
              <label className="wide">
                URL del logotipo
                <input
                  type="url"
                  value={settings.logoUrl ?? ""}
                  onChange={(e) => {
                    change({ logoUrl: e.target.value || null });
                    setLogoFailed(false);
                  }}
                  placeholder="https://..."
                />
              </label>
              <label>
                Teléfono
                <input
                  value={settings.phone ?? ""}
                  onChange={(e) => change({ phone: e.target.value || null })}
                />
              </label>
              <label>
                Dirección
                <input
                  value={settings.address ?? ""}
                  onChange={(e) => change({ address: e.target.value || null })}
                />
              </label>
              <label className="wide">
                Mensaje del ticket
                <input
                  value={settings.ticketMessage ?? ""}
                  onChange={(e) =>
                    change({ ticketMessage: e.target.value || null })
                  }
                  placeholder="¡Gracias por tu compra!"
                />
              </label>
            </div>
          </section>
          <section className="card settings-section">
            <div className="settings-section-title">
              <span><PackageMinus /></span>
              <div>
                <h2>Control de existencias</h2>
                <p>Define qué sucede cuando un producto llega a cero.</p>
              </div>
            </div>
            <label className="negative-stock-setting">
              <input type="checkbox" checked={settings.allowNegativeStock} onChange={(event) => change({ allowNegativeStock: event.target.checked })} />
              <span><strong>Permitir ventas y apartados sin existencia</strong><small>El inventario podrá quedar en negativo hasta que registres una entrada o ajuste.</small></span>
            </label>
          </section>
          <section className="card settings-section printer-settings">
            <div className="settings-section-title">
              <span><Printer /></span>
              <div>
                <h2>Impresión en esta caja</h2>
                <p>La selección se guarda solamente en este dispositivo.</p>
              </div>
              <i className={`qz-status ${qzStatus}`}><b />{qzStatus === "connected" ? "QZ conectado" : qzStatus === "busy" ? "Conectando…" : "QZ desconectado"}</i>
            </div>
            <div className="form-grid printer-form">
              <label>
                Método de impresión
                <select value={printSettings.mode} onChange={(event) => changePrint({ mode: event.target.value as LocalPrintSettings["mode"] })}>
                  <option value="browser">Navegador (respaldo)</option>
                  <option value="qz">Directa con QZ Tray</option>
                </select>
              </label>
              <label>
                Ancho del papel
                <select value={printSettings.paperWidth} onChange={(event) => changePrint({ paperWidth: Number(event.target.value) as 58 | 80 })}>
                  <option value="80">80 mm</option>
                  <option value="58">58 mm</option>
                </select>
              </label>
              <label className="wide">
                Impresora
                <select value={printSettings.printerName} onChange={(event) => changePrint({ printerName: event.target.value })} disabled={printSettings.mode !== "qz"}>
                  <option value="">Selecciona una impresora</option>
                  {printSettings.printerName && !printers.includes(printSettings.printerName) && <option value={printSettings.printerName}>{printSettings.printerName}</option>}
                  {printers.map((printer) => <option value={printer} key={printer}>{printer}</option>)}
                </select>
              </label>
              <label>
                Copias
                <input type="number" min="1" max="5" value={printSettings.copies} onChange={(event) => changePrint({ copies: Math.max(1, Math.min(5, Number(event.target.value))) })} />
              </label>
              <label className="printer-check"><input type="checkbox" checked={printSettings.autoPrint} onChange={(event) => changePrint({ autoPrint: event.target.checked })} /> Imprimir automáticamente al cobrar</label>
            </div>
            <div className="printer-actions">
              <button type="button" className="button secondary" onClick={detectPrinters} disabled={qzStatus === "busy"}><RefreshCw />Detectar impresoras</button>
              <button type="button" className="button primary" onClick={testPrinter} disabled={qzStatus === "busy" || printSettings.mode !== "qz" || !printSettings.printerName}><Printer />Imprimir prueba</button>
            </div>
            {printMessage && <div className={qzStatus === "error" ? "form-error" : "form-success"}>{printMessage}</div>}
            <p className="printer-help">Para probar sin impresora física, selecciona <b>Microsoft Print to PDF</b>. Si aparece una confirmación de QZ, instala o actualiza la <a href="/impresion" target="_blank" rel="noreferrer">impresión silenciosa</a>.</p>
          </section>
          <section className="card settings-section">
            <div className="settings-section-title">
              <span>
                <Palette />
              </span>
              <div>
                <h2>Temas profesionales</h2>
                <p>Elige una base y después afínala a tu gusto.</p>
              </div>
            </div>
            <div className="advanced-theme-grid">
              {themes.map((theme) => (
                <button
                  type="button"
                  className={
                    selectedTheme === theme.label
                      ? "advanced-theme selected"
                      : "advanced-theme"
                  }
                  onClick={() => {
                    const { label: _, ...appearance } = theme;
                    change(appearance);
                  }}
                  key={theme.label}
                >
                  <span
                    className="theme-browser"
                    style={{ background: theme.backgroundColor }}
                  >
                    <i style={{ background: theme.primaryColor }} />
                    <b style={{ background: theme.surfaceColor }}>
                      <em style={{ background: theme.buttonColor }} />
                    </b>
                  </span>
                  <strong>{theme.label}</strong>
                  {selectedTheme === theme.label && <Check />}
                </button>
              ))}
            </div>
          </section>
          <section className="card settings-section">
            <div className="settings-section-title">
              <span>
                <LayoutTemplate />
              </span>
              <div>
                <h2>Colores y forma</h2>
                <p>Control detallado de cada zona de la aplicación.</p>
              </div>
            </div>
            <div className="advanced-colors">
              {colorFields.map((field) => (
                <label key={field.key}>
                  <input
                    type="color"
                    value={String(settings[field.key])}
                    onChange={(e) => change({ [field.key]: e.target.value })}
                  />
                  <span>
                    <strong>{field.label}</strong>
                    <small>{field.description}</small>
                  </span>
                  <code>{String(settings[field.key])}</code>
                </label>
              ))}
            </div>
            <div className="radius-control">
              <div>
                <strong>Redondez de componentes</strong>
                <small>Desde estilo cuadrado hasta muy suave.</small>
              </div>
              <input
                type="range"
                min="0"
                max="24"
                step="2"
                value={settings.cornerRadius}
                onChange={(e) =>
                  change({ cornerRadius: Number(e.target.value) })
                }
              />
              <b>{settings.cornerRadius}px</b>
            </div>
            <div className="reminder-setting">
              <div>
                <strong>Recordar apartados antes del vencimiento</strong>
                <small>Se resaltarán en el centro de recordatorios.</small>
              </div>
              <select
                value={settings.layawayReminderDaysBefore}
                onChange={(event) =>
                  change({
                    layawayReminderDaysBefore: Number(event.target.value),
                  })
                }
              >
                <option value="0">El mismo día</option>
                <option value="1">1 día antes</option>
                <option value="3">3 días antes</option>
                <option value="5">5 días antes</option>
                <option value="7">7 días antes</option>
              </select>
            </div>
          </section>
          {error && <div className="form-error">{error}</div>}
          {saved && (
            <div className="form-success">
              La identidad visual quedó guardada.
            </div>
          )}
          <button className="button primary settings-save" disabled={busy}>
            <Save />
            {busy ? "Guardando..." : "Guardar y aplicar tema"}
          </button>
        </div>
        <aside
          className="theme-live-preview"
          style={{
            background: settings.backgroundColor,
            color: settings.textColor,
            borderRadius: settings.cornerRadius,
          }}
        >
          <div
            className="preview-sidebar"
            style={{ background: settings.primaryColor }}
          >
            <div
              className="preview-logo"
              style={{ background: settings.accentColor }}
            >
              {settings.logoUrl && !logoFailed ? (
                <img
                  src={settings.logoUrl}
                  onError={() => setLogoFailed(true)}
                />
              ) : logoFailed ? (
                <ImageOff />
              ) : (
                "VF"
              )}
            </div>
            <i style={{ background: settings.accentColor }} />
            <i />
            <i />
            <i />
          </div>
          <div className="preview-workspace">
            <div
              className="preview-topbar"
              style={{ background: settings.surfaceColor }}
            />
            <p>VISTA PREVIA</p>
            <h2>{settings.name}</h2>
            <div className="preview-metrics">
              <span style={{ background: settings.surfaceColor }} />
              <span style={{ background: settings.surfaceColor }} />
              <span style={{ background: settings.surfaceColor }} />
            </div>
            <div
              className="preview-panel"
              style={{
                background: settings.surfaceColor,
                borderRadius: settings.cornerRadius,
              }}
            >
              <strong>Tu negocio, a tu estilo</strong>
              <small>
                {settings.ticketMessage || "¡Gracias por tu compra!"}
              </small>
              <button
                type="button"
                style={{
                  background: settings.buttonColor,
                  borderRadius: Math.min(settings.cornerRadius, 14),
                }}
              >
                Acción principal
              </button>
            </div>
          </div>
        </aside>
      </form>
    </div>
  );
}
