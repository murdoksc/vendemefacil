import {
  ArrowRight,
  BarChart3,
  Boxes,
  Check,
  ChevronRight,
  Clock3,
  Menu,
  MessageCircle,
  MonitorSmartphone,
  ReceiptText,
  ShieldCheck,
  ShoppingBag,
  Sparkles,
  Store,
  Users,
  Crown,
  X,
} from "lucide-react";
import { FormEvent, useState } from "react";
import { apiRequest } from "../lib/api";

type LandingPageProps = {
  onAccess: (mode?: "login" | "register", planCode?: string) => void;
  onPrinting: () => void;
};

const benefits = [
  { icon: ShoppingBag, title: "Vende sin complicaciones", text: "Cobra en segundos y entrega tickets por impresión, WhatsApp o correo." },
  { icon: Boxes, title: "Inventario siempre claro", text: "Conoce existencias, costos y movimientos desde cualquier dispositivo." },
  { icon: ReceiptText, title: "Apartados y clientes", text: "Da seguimiento a saldos, fechas de pago y recordatorios en un solo lugar." },
  { icon: BarChart3, title: "Decisiones con datos", text: "Consulta ventas, utilidad, productos destacados y cortes de caja." },
];

const plans = [
  { name: "Esencial", price: 199, description: "Para comenzar a controlar un negocio pequeño.", features: ["Punto de venta e inventario", "Clientes, tickets y cortes", "1 usuario y 1 sucursal", "Soporte estándar"] },
  { name: "Negocio", price: 499, description: "Para tiendas que necesitan más control y colaboración.", featured: true, features: ["Todo lo incluido en Esencial", "Apartados y reportes completos", "Importación, roles y permisos", "5 usuarios y 2 sucursales"] },
  { name: "Pro", price: 799, description: "Para negocios con varias sucursales y planes de crecimiento.", features: ["Todo lo incluido en Negocio", "Reportes consolidados", "Personalización de marca", "15 usuarios y 5 sucursales"] },
];

export function LandingPage({ onAccess, onPrinting }: LandingPageProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  async function submitLead(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setMessage("");
    setError("");
    const form = event.currentTarget;
    const data = new FormData(form);
    try {
      const result = await apiRequest<{ message: string }>("/api/public/leads", {
        method: "POST",
        body: JSON.stringify(Object.fromEntries(data.entries())),
      });
      setMessage(result.message);
      form.reset();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No pudimos registrar tus datos. Intenta de nuevo.");
    } finally {
      setBusy(false);
    }
  }

  const closeAnd = (action: () => void) => {
    setMenuOpen(false);
    action();
  };

  return <main className="marketing-page">
    <nav className="marketing-nav">
      <a className="marketing-logo" href="#inicio" aria-label="Véndeme Fácil, inicio"><span>VF</span><strong>Véndeme Fácil</strong></a>
      <button className="marketing-menu" onClick={() => setMenuOpen(value => !value)} aria-label="Abrir menú">{menuOpen ? <X /> : <Menu />}</button>
      <div className={menuOpen ? "marketing-links open" : "marketing-links"}>
        <a href="#funciones" onClick={() => setMenuOpen(false)}>Funciones</a>
        <a href="#como-funciona" onClick={() => setMenuOpen(false)}>Cómo funciona</a>
        <button onClick={() => closeAnd(onPrinting)}>Impresión silenciosa</button>
        <a href="#precios" onClick={() => setMenuOpen(false)}>Precios</a>
        <a href="#contacto" onClick={() => setMenuOpen(false)}>Quiero información</a>
      </div>
      <div className="marketing-actions">
        <button className="marketing-login" onClick={() => onAccess("login")}>Iniciar sesión</button>
        <button className="marketing-cta" onClick={() => onAccess("register")}>Crear mi negocio <ArrowRight /></button>
      </div>
    </nav>

    <section className="marketing-hero" id="inicio">
      <div className="hero-copy">
        <span className="hero-pill"><Sparkles /> Tu tienda, más fácil de controlar</span>
        <h1>Vende más.<br /><em>Administra mejor.</em></h1>
        <p>Punto de venta e inventario para boutiques, zapaterías, tiendas de regalos, accesorios, papelerías y otros comercios minoristas.</p>
        <div className="hero-actions">
          <button className="marketing-cta large" onClick={() => onAccess("register")}>Crear mi negocio <ArrowRight /></button>
          <a href="#contacto">Quiero que me expliquen <MessageCircle /></a>
        </div>
        <div className="hero-trust"><span><Check /> Fácil de usar</span><span><Check /> En cualquier dispositivo</span><span><Check /> Información segura</span></div>
      </div>
      <div className="product-preview" aria-label="Vista previa del sistema">
        <div className="preview-window-bar"><i /><i /><i /><span>vendemefacil.com</span></div>
        <div className="preview-app">
          <aside><b>VF</b><i /><i className="active" /><i /><i /><i /></aside>
          <div className="preview-dashboard">
            <span className="preview-kicker">RESUMEN DE HOY</span><h2>Tu negocio en orden</h2>
            <div className="preview-stats"><article><small>Ventas de hoy</small><strong>$12,480</strong><em>+18% esta semana</em></article><article><small>Productos</small><strong>342</strong><em>18 con stock bajo</em></article><article><small>Ticket promedio</small><strong>$486</strong><em>26 ventas</em></article></div>
            <div className="preview-bottom"><div><span>Ventas de la semana</span><div className="preview-bars"><i /><i /><i /><i /><i /><i /><i /></div></div><article><span>Acciones rápidas</span><button>Nueva venta <ChevronRight /></button><button>Entrada de inventario <ChevronRight /></button></article></div>
          </div>
        </div>
        <div className="floating-proof"><ShieldCheck /><span><strong>Todo bajo control</strong><small>Información actualizada en tiempo real</small></span></div>
      </div>
    </section>

    <section className="benefit-strip"><span>Todo lo que necesitas para operar</span><b>Ventas</b><i /> <b>Inventario</b><i /> <b>Apartados</b><i /> <b>Clientes</b><i /> <b>Reportes</b></section>

    <section className="marketing-section" id="funciones">
      <div className="section-heading"><span>HECHO PARA TU DÍA A DÍA</span><h2>Menos vueltas. Más control.</h2><p>Herramientas claras para que tú y tu equipo trabajen mejor desde el primer día.</p></div>
      <div className="benefits-grid">{benefits.map(({ icon: Icon, title, text }, index) => <article key={title}><span className={`benefit-icon tone-${index}`}><Icon /></span><h3>{title}</h3><p>{text}</p><small>Conoce más <ArrowRight /></small></article>)}</div>
    </section>

    <section className="how-section" id="como-funciona">
      <div><span className="section-label">EMPIEZA HOY</span><h2>Tu negocio listo en tres pasos</h2><p>No necesitas conocimientos técnicos ni instalaciones complicadas para comenzar.</p><button className="marketing-cta" onClick={() => onAccess("register")}>Crear mi negocio <ArrowRight /></button></div>
      <ol><li><b>01</b><span><strong>Crea tu cuenta</strong><small>Registra el nombre de tu negocio y tus datos de acceso.</small></span></li><li><b>02</b><span><strong>Agrega tus productos</strong><small>Captura uno por uno o importa tu catálogo.</small></span></li><li><b>03</b><span><strong>Haz tu primera venta</strong><small>Cobra, imprime o comparte el ticket con tu cliente.</small></span></li></ol>
    </section>

    <section className="audience-section"><div className="audience-copy"><span className="section-label">PARA COMERCIOS MINORISTAS</span><h2>Crece sin perder el control</h2><p>Ideal para boutiques, zapaterías, tiendas de regalos y accesorios, papelerías y comercios similares que venden productos por pieza.</p><div><span><Store />Una o varias sucursales</span><span><Users />Usuarios con distintos permisos</span><span><MonitorSmartphone />Computadora, tablet o celular</span><span><Clock3 />Información disponible en todo momento</span></div></div><div className="audience-card"><span>VF</span><blockquote>“La operación de tu negocio debe sentirse sencilla, aunque por dentro haga muchas cosas.”</blockquote><small>Ese es el propósito de Véndeme Fácil.</small></div></section>

    <section className="pricing-section" id="precios">
      <div className="section-heading"><span>PLANES SENCILLOS</span><h2>Elige cómo quieres hacer crecer tu negocio</h2><p>Prueba todas las funciones durante 30 días. Sin tarjeta y sin compromiso.</p></div>
      <div className="pricing-grid">{plans.map(plan => <article className={plan.featured ? "pricing-card featured" : "pricing-card"} key={plan.name}>
        {plan.featured && <b className="pricing-badge"><Crown /> Más elegido</b>}
        <h3>{plan.name}</h3><p>{plan.description}</p>
        <div className="plan-price"><sup>$</sup><strong>{plan.price}</strong><span>MXN<br />al mes</span></div>
        <ul>{plan.features.map(feature => <li key={feature}><Check /> {feature}</li>)}</ul>
        <button className="marketing-cta" onClick={() => onAccess("register", plan.name.toLowerCase())}>Probar 30 días gratis <ArrowRight /></button>
      </article>)}</div>
      <p className="pricing-note">Sin límites de productos, ventas, clientes o tickets. Las funciones y límites de cada plan pueden evolucionar; siempre te avisaremos antes de cualquier cambio.</p>
    </section>

    <section className="lead-section" id="contacto">
      <div className="lead-copy"><span className="section-label">¿QUIERES CONOCERLO?</span><h2>Te ayudamos a descubrir si Véndeme Fácil es para tu negocio.</h2><p>Déjanos tus datos. Te llamaremos para escuchar lo que necesitas, mostrarte el sistema y resolver tus dudas sin compromiso.</p><div><Check /> Atención personal</div><div><Check /> Demostración enfocada en tu negocio</div><div><Check /> Sin spam ni llamadas insistentes</div></div>
      <form className="lead-form" onSubmit={submitLead}>
        <div className="lead-form-heading"><span><MessageCircle /></span><div><h3>Solicita una llamada</h3><p>Cuéntanos un poco sobre ti.</p></div></div>
        <div className="lead-fields"><label>Tu nombre<input name="contactName" required maxLength={120} placeholder="Nombre completo" /></label><label>Nombre del negocio<input name="businessName" required maxLength={160} placeholder="Mi tienda" /></label><label>Teléfono / WhatsApp<input name="phone" required maxLength={30} inputMode="tel" placeholder="(868) 000 0000" /></label><label>Correo <small>(opcional)</small><input name="email" type="email" maxLength={200} placeholder="tu@negocio.com" /></label><label>Ciudad<input name="city" maxLength={120} placeholder="Ciudad, Estado" /></label><label>Giro del negocio<select name="businessType" defaultValue=""><option value="" disabled>Selecciona una opción</option><option>Boutique o ropa</option><option>Calzado</option><option>Regalos y novedades</option><option>Accesorios, joyería o bisutería</option><option>Papelería</option><option>Otro comercio minorista</option></select></label><label className="wide">¿Cuándo podemos marcarte?<select name="preferredContactTime" defaultValue="Cualquier horario"><option>Cualquier horario</option><option>Por la mañana</option><option>Por la tarde</option><option>Después de las 6 p.m.</option></select></label><label className="wide">¿Qué te gustaría controlar mejor? <small>(opcional)</small><textarea name="notes" maxLength={1000} placeholder="Ventas, inventario, apartados..." /></label><label className="lead-honeypot" aria-hidden="true">Sitio web<input name="website" tabIndex={-1} autoComplete="off" /></label></div>
        {error && <div className="form-error" role="alert">{error}</div>}{message && <div className="form-success" role="status">{message}</div>}
        <button className="marketing-cta lead-submit" disabled={busy}>{busy ? "Enviando..." : "Quiero que me contacten"} <ArrowRight /></button><small className="privacy-note">Al enviar aceptas que usemos estos datos únicamente para contactarte sobre Véndeme Fácil.</small>
      </form>
    </section>

    <footer className="marketing-footer"><div className="marketing-logo"><span>VF</span><strong>Véndeme Fácil</strong></div><p>Punto de venta e inventario para comercios minoristas.</p><div><button onClick={() => onAccess("login")}>Iniciar sesión</button><button onClick={onPrinting}>Configurar impresión</button><a href="#contacto">Contacto</a></div><small>© {new Date().getFullYear()} Véndeme Fácil. Todos los derechos reservados.</small></footer>
  </main>;
}
