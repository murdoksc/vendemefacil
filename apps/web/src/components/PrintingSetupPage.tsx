import { ArrowLeft, Check, Download, ExternalLink, FileDown, Printer, ShieldCheck } from "lucide-react";

export function PrintingSetupPage({ onBack, onAccess }: { onBack: () => void; onAccess: () => void }) {
  return <main className="printing-setup-page">
    <nav className="setup-nav"><button onClick={onBack}><ArrowLeft /> Volver a Véndeme Fácil</button><div className="marketing-logo"><span>VF</span><strong>Véndeme Fácil</strong></div><button className="marketing-login" onClick={onAccess}>Iniciar sesión</button></nav>
    <header className="setup-hero"><span><Printer /></span><p className="section-label">IMPRESIÓN SILENCIOSA</p><h1>Imprime tickets con un solo clic</h1><p>Configura una vez la computadora de tu negocio para imprimir desde Véndeme Fácil sin confirmar cada ticket.</p></header>
    <section className="setup-steps">
      <article><b>1</b><div><h2>Instala QZ Tray</h2><p>QZ Tray conecta de forma segura el navegador con tu impresora de tickets.</p><a className="setup-download secondary" href="https://qz.io/download/" target="_blank" rel="noreferrer">Descargar QZ Tray <ExternalLink /></a></div></article>
      <article><b>2</b><div><h2>Descarga el instalador de Véndeme Fácil</h2><p>El paquete contiene nuestro certificado de confianza y un instalador automático.</p><a className="setup-download" href="/downloads/impresion-silenciosa-vendemefacil.zip" download><FileDown /> Descargar instalador</a></div></article>
      <article><b>3</b><div><h2>Descomprime y haz doble clic</h2><p>Abre el archivo ZIP, elige <strong>Extraer todo</strong> y después haz doble clic en <code>Instalar impresión Véndeme Fácil.bat</code>. Acepta el permiso de Windows.</p><span className="setup-tip"><ShieldCheck /> El instalador sólo agrega el certificado público de Véndeme Fácil a QZ Tray. Nunca instala una clave privada.</span></div></article>
      <article><b>4</b><div><h2>Selecciona tu impresora</h2><p>Entra a tu negocio, abre <strong>Configuración → Impresión</strong>, selecciona QZ Tray y prueba un ticket.</p><button className="setup-download" onClick={onAccess}>Entrar a mi negocio <ExternalLink /></button></div></article>
    </section>
    <section className="setup-ready"><Download /><div><h2>Antes de comenzar</h2><p>Necesitas Windows, permisos de administrador y tu impresora instalada. QZ Tray debe permanecer abierto mientras utilizas Véndeme Fácil.</p></div><span><Check /> Configuración por computadora</span></section>
  </main>;
}
