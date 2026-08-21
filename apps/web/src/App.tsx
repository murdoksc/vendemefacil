import {
  ArrowRight,
  BarChart3,
  Bell,
  Boxes,
  ChevronDown,
  CircleDollarSign,
  CreditCard,
  ClipboardPlus,
  CalendarClock,
  CheckCircle2,
  LayoutDashboard,
  Menu,
  MessageCircle,
  Mail,
  PackageSearch,
  Plus,
  ReceiptText,
  Search,
  Settings,
  ShoppingBag,
  TrendingUp,
  Users,
  X,
} from "lucide-react";
import { useEffect, useState } from "react";
import { AuthPage } from "./components/AuthPage";
import { ProductsPage } from "./components/ProductsPage";
import { QuickEntryPage } from "./components/QuickEntryPage";
import { InventoryPage } from "./components/InventoryPage";
import {
  BusinessSettings,
  BusinessSettingsPage,
} from "./components/BusinessSettingsPage";
import { PointOfSalePage } from "./components/PointOfSalePage";
import { SalesPage } from "./components/SalesPage";
import { ReportsPage } from "./components/ReportsPage";
import { UsersPage } from "./components/UsersPage";
import { CustomersPage } from "./components/CustomersPage";
import { LayawaysPage } from "./components/LayawaysPage";
import { LandingPage } from "./components/LandingPage";
import { PrintingSetupPage } from "./components/PrintingSetupPage";
import { SubscriptionPage } from "./components/SubscriptionPage";
import { PlatformSubscriptionsPage } from "./components/PlatformSubscriptionsPage";
import { PlatformAdminHub } from "./components/PlatformAdminHub";
import { apiRequest, AuthSession, clearSession, loadSession } from "./lib/api";
import { emailDocument } from "./lib/emailDocument";

const navItems = [
  { label: "Inicio", icon: LayoutDashboard },
  { label: "Punto de venta", icon: ShoppingBag },
  { label: "Productos", icon: PackageSearch },
  { label: "Inventario", icon: Boxes },
  { label: "Ventas", icon: ReceiptText },
  { label: "Apartados", icon: CalendarClock },
  { label: "Clientes", icon: Users },
  { label: "Reportes", icon: BarChart3 },
];

type LayawayReminder = {
  id: string;
  folio: string;
  dueAtUtc: string;
  customer: string;
  phone: string | null;
  email: string | null;
  balance: number;
  isOverdue: boolean;
};
const reminderMoney = new Intl.NumberFormat("es-MX", {
  style: "currency",
  currency: "MXN",
});
const appVersion = import.meta.env.VITE_APP_VERSION || "local";

function applyTheme(settings: BusinessSettings) {
  const root = document.documentElement.style;
  root.setProperty("--brand-primary", settings.primaryColor);
  root.setProperty("--brand-accent", settings.accentColor);
  root.setProperty("--brand-button", settings.buttonColor);
  root.setProperty("--brand-hover", settings.hoverColor);
  root.setProperty("--app-background", settings.backgroundColor);
  root.setProperty("--app-surface", settings.surfaceColor);
  root.setProperty("--app-text", settings.textColor);
  root.setProperty("--app-radius", `${settings.cornerRadius}px`);
  document.title = settings.slug;
  let favicon = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
  if (settings.logoUrl) {
    if (!favicon) {
      favicon = document.createElement("link");
      favicon.rel = "icon";
      document.head.appendChild(favicon);
    }
    favicon.href = settings.logoUrl;
  } else favicon?.remove();
}

function App() {
  const [publicPath, setPublicPath] = useState(() => window.location.pathname);
  const [authMode, setAuthMode] = useState<"login" | "register">("login");
  const [selectedPlan, setSelectedPlan] = useState("negocio");
  const [menuOpen, setMenuOpen] = useState(false);
  const [activePage, setActivePage] = useState("Inicio");
  const [productCreateRequest, setProductCreateRequest] = useState(0);
  const [session, setSession] = useState<AuthSession | null>(() =>
    loadSession(),
  );
  const [businessSettings, setBusinessSettings] =
    useState<BusinessSettings | null>(null);
  const [brandLogoFailed, setBrandLogoFailed] = useState(false);
  const [reminders, setReminders] = useState<LayawayReminder[]>([]);
  const [remindersOpen, setRemindersOpen] = useState(false);
  const [online, setOnline] = useState(navigator.onLine);
  const [dashboard, setDashboard] = useState({
    salesToday: 0,
    transactionsToday: 0,
    averageTicket: 0,
    productsInStock: 0,
    unitsInStock: 0,
    lowStockProducts: 0,
    cashOpen: false,
    weeklySales: [] as { date: string; sales: number }[],
    recentProducts: [] as {
      id: string;
      name: string;
      variant: string;
      sku: string;
      price: number;
      stock: number;
      minimumStock: number;
    }[],
  });

  useEffect(() => {
    const syncPath = () => setPublicPath(window.location.pathname);
    window.addEventListener("popstate", syncPath);
    return () => window.removeEventListener("popstate", syncPath);
  }, []);
  useEffect(() => {
    if (!session) document.title = "Véndeme Fácil | Tu negocio en orden";
  }, [session, publicPath]);
  useEffect(() => {
    const updateConnection = () => setOnline(navigator.onLine);
    window.addEventListener("online", updateConnection);
    window.addEventListener("offline", updateConnection);
    return () => { window.removeEventListener("online", updateConnection); window.removeEventListener("offline", updateConnection); };
  }, []);
  useEffect(() => {
    if (session)
      apiRequest<typeof dashboard>("/api/v1/dashboard", {}, session)
        .then(setDashboard)
        .catch(() => undefined);
  }, [session, activePage]);
  useEffect(() => {
    if (!session) return;
    const refresh = () =>
      apiRequest<LayawayReminder[]>("/api/v1/layaways/reminders", {}, session)
        .then(setReminders)
        .catch(() => undefined);
    void refresh();
    window.addEventListener("vendemefacil:reminders-changed", refresh);
    return () => {
      window.removeEventListener("vendemefacil:reminders-changed", refresh);
    };
  }, [session, activePage]);
  useEffect(() => {
    if (session)
      apiRequest<BusinessSettings>("/api/v1/business/settings", {}, session)
        .then((settings) => {
          setBusinessSettings(settings);
          setBrandLogoFailed(false);
          applyTheme(settings);
        })
        .catch(() => undefined);
  }, [session]);
  useEffect(() => {
    const navigate = (event: Event) =>
      setActivePage((event as CustomEvent<string>).detail);
    window.addEventListener("vendemefacil:navigate", navigate);
    return () => window.removeEventListener("vendemefacil:navigate", navigate);
  }, []);

  const navigatePublic = (path: string) => {
    window.history.pushState({}, "", path);
    setPublicPath(path);
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  if (publicPath === "/impresion")
    return <PrintingSetupPage
      onBack={() => navigatePublic("/")}
      onAccess={() => session ? navigatePublic("/") : (setAuthMode("login"), navigatePublic("/acceso"))}
    />;

  if (publicPath === "/administracion")
    return <PlatformAdminHub onBack={() => navigatePublic("/")} />;
  if (publicPath === "/administracion/suscripciones")
    return <PlatformSubscriptionsPage onBack={() => navigatePublic("/administracion")} />;

  if (!session) {
    if (publicPath === "/reset-password")
      return <AuthPage onAuthenticated={setSession} />;
    if (publicPath === "/acceso")
      return <AuthPage onAuthenticated={setSession} initialMode={authMode} initialPlan={selectedPlan} onBack={() => navigatePublic("/")} />;
    return <LandingPage
      onAccess={(mode = "login", planCode) => { setAuthMode(mode); if (planCode) setSelectedPlan(planCode); navigatePublic("/acceso"); }}
      onPrinting={() => navigatePublic("/impresion")}
    />;
  }

  const firstName = session.user.displayName.split(" ")[0];
  const initials = session.user.displayName
    .split(" ")
    .slice(0, 2)
    .map((part) => part[0])
    .join("")
    .toUpperCase();
  const logout = () => {
    clearSession();
    setSession(null);
    setAuthMode("login");
    window.history.pushState({}, "", "/acceso");
    setPublicPath("/acceso");
  };
  const openReminderWhatsApp = (reminder: LayawayReminder) => {
    if (!reminder.phone) return;
    let phone = reminder.phone.replace(/\D/g, "");
    if (phone.length === 10) phone = `52${phone}`;
    const dueDate = new Date(reminder.dueAtUtc).toLocaleDateString("es-MX");
    const message = `Hola ${reminder.customer}, te recordamos que tu apartado ${reminder.folio} tiene un saldo de ${reminderMoney.format(reminder.balance)} y vence el ${dueDate}. ¡Gracias!`;
    window.open(
      `https://wa.me/${phone}?text=${encodeURIComponent(message)}`,
      "_blank",
      "noopener,noreferrer",
    );
  };
  const openReminderEmail = async (reminder: LayawayReminder) => {
    const dueDate = new Date(reminder.dueAtUtc).toLocaleDateString("es-MX");
    const content = `Hola ${reminder.customer},\n\nTe recordamos que tu apartado ${reminder.folio} tiene un saldo de ${reminderMoney.format(reminder.balance)} y vence el ${dueDate}.\n\n¡Gracias!`;
    try {
      if (await emailDocument({ session, documentType: "layaway-reminder", reference: reminder.folio, content, defaultEmail: reminder.email }))
        window.alert("Recordatorio enviado por correo.");
    } catch (reason) {
      window.alert(reason instanceof Error ? reason.message : "No pudimos enviar el recordatorio por correo.");
    }
  };
  const isManager =
    session.user.role === "Owner" || session.user.role === "Administrator";

  return (
    <div className="app-shell">
      <aside className={menuOpen ? "sidebar open" : "sidebar"}>
        <div className="brand-row">
          <div className="brand-mark">
            {businessSettings?.logoUrl && !brandLogoFailed ? (
              <img
                src={businessSettings.logoUrl}
                alt=""
                onError={() => setBrandLogoFailed(true)}
              />
            ) : (
              (businessSettings?.slug ?? session.user.businessSlug)
                .slice(0, 2)
                .toUpperCase()
            )}
          </div>
          <div>
            <strong>
              {businessSettings?.slug ?? session.user.businessSlug}
            </strong>
            <span>{businessSettings?.name ?? session.user.businessName}</span>
          </div>
          <button
            className="icon-button mobile-close"
            aria-label="Cerrar menú"
            onClick={() => setMenuOpen(false)}
          >
            <X />
          </button>
        </div>
        <nav>
          <p className="nav-label">OPERACIÓN</p>
          {navItems
            .filter(({ label }) => isManager || (label !== "Inventario" && label !== "Reportes"))
            .map(({ label, icon: Icon }) => (
            <button
              className={activePage === label ? "nav-item active" : "nav-item"}
              key={label}
              onClick={() => {
                setActivePage(label);
                setMenuOpen(false);
              }}
            >
              <Icon />
              {label}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          {isManager && (
            <>
              <button
                className={
                  activePage === "Usuarios" ? "nav-item active" : "nav-item"
                }
                onClick={() => setActivePage("Usuarios")}
              >
                <Users />
                Usuarios
              </button>
              <button
                className={
                  activePage === "Configuración"
                    ? "nav-item active"
                    : "nav-item"
                }
                onClick={() => setActivePage("Configuración")}
              >
                <Settings />
                Configuración
              </button>
              <button className={activePage === "Mi plan" ? "nav-item active" : "nav-item"} onClick={() => setActivePage("Mi plan")}>
                <CreditCard />
                Mi plan
              </button>
            </>
          )}
          <div className="app-version" title={`Build ${appVersion}`}>
            Versión {appVersion}
          </div>
        </div>
      </aside>

      {menuOpen && (
        <button
          className="backdrop"
          aria-label="Cerrar menú"
          onClick={() => setMenuOpen(false)}
        />
      )}

      <main>
        <header>
          <button
            className="icon-button menu-button"
            aria-label="Abrir menú"
            onClick={() => setMenuOpen(true)}
          >
            <Menu />
          </button>
          <div className="search">
            <Search />
            <input
              aria-label="Buscar"
              placeholder="Buscar productos, ventas..."
            />
          </div>
          <div className="header-actions">
            <span className={online ? "connection-status online" : "connection-status offline"}><i />{online ? "En línea" : "Sin conexión"}</span>
            <div className="reminder-center">
              <button
                className={
                  remindersOpen
                    ? "icon-button notification active"
                    : "icon-button notification"
                }
                aria-label={`Recordatorios${reminders.length ? `, ${reminders.length} pendientes` : ""}`}
                onClick={() => setRemindersOpen((open) => !open)}
              >
                <Bell />
                {reminders.length > 0 && (
                  <b>{reminders.length > 99 ? "99+" : reminders.length}</b>
                )}
              </button>
              {remindersOpen && (
                <div className="reminder-panel">
                  <div className="reminder-heading">
                    <div>
                      <p className="eyebrow">APARTADOS</p>
                      <h2>Recordatorios</h2>
                    </div>
                    <button
                      className="close-form"
                      onClick={() => setRemindersOpen(false)}
                    >
                      <X />
                    </button>
                  </div>
                  <p className="reminder-description">
                    Próximos a vencer según la anticipación configurada.
                  </p>
                  <div className="reminder-list">
                    {reminders.length ? (
                      reminders.map((reminder) => (
                        <article
                          className={
                            reminder.isOverdue
                              ? "reminder-item overdue"
                              : "reminder-item"
                          }
                          key={reminder.id}
                        >
                          <button
                            className="reminder-main"
                            onClick={() => {
                              setActivePage("Apartados");
                              setRemindersOpen(false);
                            }}
                          >
                            <span>
                              <strong>{reminder.customer}</strong>
                              <small>
                                {reminder.folio} ·{" "}
                                {reminder.isOverdue
                                  ? "Vencido"
                                  : `vence ${new Date(reminder.dueAtUtc).toLocaleDateString("es-MX")}`}
                              </small>
                            </span>
                            <b>{reminderMoney.format(reminder.balance)}</b>
                          </button>
                          <button
                            className="reminder-whatsapp"
                            title={
                              reminder.phone
                                ? "Preparar WhatsApp"
                                : "Cliente sin teléfono"
                            }
                            disabled={!reminder.phone}
                            onClick={() => openReminderWhatsApp(reminder)}
                          >
                            <MessageCircle />
                          </button>
                          <button className="reminder-whatsapp reminder-email" title="Enviar por email" onClick={() => void openReminderEmail(reminder)}><Mail /></button>
                        </article>
                      ))
                    ) : (
                      <div className="reminder-empty">
                        <CheckCircle2 />
                        <strong>Todo al día</strong>
                        <span>No hay apartados por recordar.</span>
                      </div>
                    )}
                  </div>
                  <button
                    className="reminder-footer"
                    onClick={() => {
                      setActivePage("Apartados");
                      setRemindersOpen(false);
                    }}
                  >
                    Ver todos los apartados <ArrowRight />
                  </button>
                </div>
              )}
            </div>
            <button className="profile" onClick={logout} title="Cerrar sesión">
              <span>{initials}</span>
              <div>
                <strong>{session.user.displayName}</strong>
                <small>Cerrar sesión</small>
              </div>
              <ChevronDown />
            </button>
          </div>
        </header>

        {activePage === "Usuarios" && isManager ? (
          <UsersPage session={session} />
        ) : activePage === "Clientes" ? (
          <CustomersPage session={session} />
        ) : activePage === "Reportes" && isManager ? (
          <ReportsPage session={session} />
        ) : activePage === "Ventas" ? (
          <SalesPage session={session} businessName={businessSettings?.name} logoUrl={businessSettings?.logoUrl} ticketMessage={businessSettings?.ticketMessage} />
        ) : activePage === "Apartados" ? (
          <LayawaysPage session={session} allowNegativeStock={businessSettings?.allowNegativeStock ?? false} />
        ) : activePage === "Punto de venta" ? (
          <PointOfSalePage session={session} businessName={businessSettings?.name} logoUrl={businessSettings?.logoUrl} ticketMessage={businessSettings?.ticketMessage} allowNegativeStock={businessSettings?.allowNegativeStock ?? false} />
        ) : activePage === "Productos" ? (
          <ProductsPage session={session} openCreate={productCreateRequest} />
        ) : activePage === "Inventario" && isManager ? (
          <InventoryPage session={session} />
        ) : activePage === "Mi plan" && isManager ? (
          <SubscriptionPage session={session} />
        ) : activePage === "Configuración" && isManager ? (
          <BusinessSettingsPage
            session={session}
            onSaved={(settings) => {
              setBusinessSettings(settings);
              setBrandLogoFailed(false);
              applyTheme(settings);
            }}
          />
        ) : (
          <div className="content">
            <section className="welcome-row">
              <div>
                <p className="eyebrow">
                  {session.user.businessName.toUpperCase()}
                </p>
                <h1>Hola, {firstName}</h1>
                <p>Esto es lo que está pasando hoy en tu negocio.</p>
              </div>
              <div className="primary-actions">
                {isManager && (
                  <button
                    className="button secondary"
                    onClick={() => setActivePage("Inventario")}
                  >
                    <ClipboardPlus />
                    Entrada rápida
                  </button>
                )}
                <button
                  className="button primary"
                  onClick={() => setActivePage("Punto de venta")}
                >
                  <Plus />
                  Nueva venta
                </button>
              </div>
            </section>

            <section className="metrics-grid">
              <article className="metric featured">
                <div className="metric-icon">
                  <CircleDollarSign />
                </div>
                <div>
                  <span>Ventas de hoy</span>
                  <strong>
                    {new Intl.NumberFormat("es-MX", {
                      style: "currency",
                      currency: "MXN",
                    }).format(dashboard.salesToday)}
                  </strong>
                  <small>
                    <TrendingUp /> {dashboard.transactionsToday} ventas
                    completadas
                  </small>
                </div>
              </article>
              <article className="metric">
                <div className="metric-icon coral">
                  <ShoppingBag />
                </div>
                <div>
                  <span>Ticket promedio</span>
                  <strong>
                    {new Intl.NumberFormat("es-MX", {
                      style: "currency",
                      currency: "MXN",
                    }).format(dashboard.averageTicket)}
                  </strong>
                  <small>Calculado con ventas de hoy</small>
                </div>
              </article>
              <article className="metric">
                <div className="metric-icon blue">
                  <Boxes />
                </div>
                <div>
                  <span>Productos en stock</span>
                  <strong>{dashboard.productsInStock}</strong>
                  <small>{dashboard.unitsInStock} unidades en total</small>
                </div>
              </article>
              <article className="metric warning">
                <div className="metric-icon amber">
                  <Bell />
                </div>
                <div>
                  <span>Stock bajo</span>
                  <strong>{dashboard.lowStockProducts}</strong>
                  <small>Requieren tu atención</small>
                </div>
              </article>
            </section>

            <section className="dashboard-grid">
              <article className="card chart-card">
                <div className="card-heading">
                  <div>
                    <span className="eyebrow">RESUMEN SEMANAL</span>
                    <h2>Consulta el comportamiento de tus ventas</h2>
                  </div>
                  <button
                    className="text-button"
                    onClick={() => setActivePage("Reportes")}
                  >
                    Ver reporte <ArrowRight />
                  </button>
                </div>
                <div className="chart-area">
                  <div className="chart-total">
                    <strong>
                      {new Intl.NumberFormat("es-MX", {
                        style: "currency",
                        currency: "MXN",
                      }).format(
                        dashboard.weeklySales.reduce(
                          (sum, x) => sum + x.sales,
                          0,
                        ),
                      )}
                    </strong>
                    <span>
                      {dashboard.cashOpen ? "Caja abierta" : "Caja cerrada"}
                    </span>
                  </div>
                  <div
                    className="bars"
                    aria-label="Gráfica de ventas semanales"
                  >
                    {dashboard.weeklySales.map((day) => (
                      <div className="bar-column" key={day.date}>
                        <div
                          className="bar"
                          style={{
                            height: `${Math.max(4, (day.sales / Math.max(...dashboard.weeklySales.map((x) => x.sales), 1)) * 100)}%`,
                          }}
                        />
                        <span>
                          {new Date(`${day.date}T12:00:00`).toLocaleDateString(
                            "es-MX",
                            { weekday: "narrow" },
                          )}
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              </article>

              <article className="card quick-card">
                <div className="card-heading">
                  <div>
                    <span className="eyebrow">ACCESOS RÁPIDOS</span>
                    <h2>¿Qué quieres hacer?</h2>
                  </div>
                </div>
                <div className="quick-grid">
                  <button onClick={() => setActivePage("Punto de venta")}>
                    <span>
                      <ShoppingBag />
                    </span>
                    <strong>Nueva venta</strong>
                    <small>Abre el punto de venta</small>
                  </button>
                  <button onClick={() => setActivePage("Inventario")}>
                    <span>
                      <ClipboardPlus />
                    </span>
                    <strong>Dar entrada</strong>
                    <small>Agrega existencias</small>
                  </button>
                  <button
                    onClick={() => {
                      setProductCreateRequest((value) => value + 1);
                      setActivePage("Productos");
                    }}
                  >
                    <span>
                      <PackageSearch />
                    </span>
                    <strong>Nuevo producto</strong>
                    <small>Amplía tu catálogo</small>
                  </button>
                  <button onClick={() => setActivePage("Ventas")}>
                    <span>
                      <CircleDollarSign />
                    </span>
                    <strong>Cerrar caja</strong>
                    <small>Termina tu turno desde Ventas</small>
                  </button>
                </div>
              </article>
            </section>

            <section className="card inventory-card">
              <div className="card-heading">
                <div>
                  <span className="eyebrow">INVENTARIO</span>
                  <h2>Productos recientes</h2>
                </div>
                <button
                  className="text-button"
                  onClick={() => setActivePage("Productos")}
                >
                  Ver inventario <ArrowRight />
                </button>
              </div>
              <div className="product-table">
                {dashboard.recentProducts.map((product, index) => (
                  <div className="product-row" key={product.id}>
                    <div className={`product-art art-${(index % 3) + 1}`}>
                      <ShoppingBag />
                    </div>
                    <div className="product-name">
                      <strong>{product.name}</strong>
                      <span>
                        {product.variant} · {product.sku}
                      </span>
                    </div>
                    <div className="stock">
                      <span>Existencia</span>
                      <strong
                        className={
                          product.stock <= product.minimumStock ? "low" : ""
                        }
                      >
                        {product.stock} piezas
                      </strong>
                    </div>
                    <div className="price">
                      <span>Precio</span>
                      <strong>
                        {new Intl.NumberFormat("es-MX", {
                          style: "currency",
                          currency: "MXN",
                        }).format(product.price)}
                      </strong>
                    </div>
                    <button
                      className="row-action"
                      onClick={() => setActivePage("Productos")}
                    >
                      Ver
                    </button>
                  </div>
                ))}
              </div>
            </section>
          </div>
        )}
      </main>
    </div>
  );
}

export default App;
