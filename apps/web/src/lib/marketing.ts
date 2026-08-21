type FacebookPixel = {
  (...args: unknown[]): void;
  queue: unknown[][];
  loaded?: boolean;
  version?: string;
};

declare global {
  interface Window {
    fbq?: FacebookPixel;
    _fbq?: unknown;
  }
}

const pixelId = import.meta.env.VITE_META_PIXEL_ID?.trim();

export function initializeMarketing() {
  if (!pixelId || window.fbq) return;
  const fbq = ((...args: unknown[]) => { fbq.queue.push(args); }) as unknown as FacebookPixel;
  fbq.queue = [];
  fbq.loaded = true;
  fbq.version = "2.0";
  window.fbq = fbq;
  const script = document.createElement("script");
  script.async = true;
  script.src = "https://connect.facebook.net/en_US/fbevents.js";
  document.head.appendChild(script);
  window.fbq("init", pixelId);
  window.fbq("track", "PageView");
}

export function trackMarketing(event: string, data: Record<string, unknown> = {}) {
  window.fbq?.("trackCustom", event, data);
}

export const salesWhatsAppUrl = (() => {
  const phone = import.meta.env.VITE_SALES_WHATSAPP?.replace(/\D/g, "");
  if (!phone) return "";
  const text = encodeURIComponent("Hola, quiero conocer VéndemeFácil para mi negocio.");
  return `https://wa.me/${phone}?text=${text}`;
})();
