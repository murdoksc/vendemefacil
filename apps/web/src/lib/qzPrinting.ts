import qz from "qz-tray";

export type LocalPrintSettings = {
  mode: "browser" | "qz";
  printerName: string;
  paperWidth: 58 | 80;
  copies: number;
  autoPrint: boolean;
};

const storageKey = "vendemefacil.print-settings.v1";
const defaults: LocalPrintSettings = {
  mode: "browser",
  printerName: "",
  paperWidth: 80,
  copies: 1,
  autoPrint: false,
};

export function loadPrintSettings(): LocalPrintSettings {
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem(storageKey) ?? "{}") };
  } catch {
    return defaults;
  }
}

export function savePrintSettings(settings: LocalPrintSettings) {
  localStorage.setItem(storageKey, JSON.stringify(settings));
}

export function isQzConnected() {
  return Boolean(qz.websocket.isActive());
}

export async function connectQz() {
  if (!qz.websocket.isActive()) {
    await qz.websocket.connect({ retries: 2, delay: 1 });
  }
}

export async function listQzPrinters(): Promise<string[]> {
  await connectQz();
  return qz.printers.find();
}

function receiptHtml(receipt: HTMLElement, width: number) {
  return `<!doctype html><html><head><meta charset="utf-8"><style>
    @page{size:${width}mm auto;margin:0}*{box-sizing:border-box}html,body{margin:0;padding:0;background:#fff;color:#000}
    body{width:${width}mm;font-family:monospace}.receipt{width:${width}mm;padding:5mm 4mm;background:#fff}
    .receipt-brand{width:16mm;height:16mm;margin:0 auto 3mm;border:1px solid #222;border-radius:50%;display:grid;place-items:center;overflow:hidden}
    .receipt-brand img{width:100%;height:100%;object-fit:contain}.receipt h2,.receipt p{text-align:center;margin:2mm 0}.receipt p{font-size:9pt;line-height:1.5}
    .ticket-line{padding:2.5mm 0;display:flex;justify-content:space-between;border-top:1px dashed #777;font-size:9pt}.ticket-line small{display:block}
    .ticket-total,.ticket-payment{padding:2.5mm 0;display:grid;grid-template-columns:1fr auto;border-top:1px dashed #222}.ticket-total{font-size:13pt}.ticket-payment{font-size:9pt}
    .receipt footer{padding-top:4mm;border-top:1px dashed #777;text-align:center;font-size:9pt}
  </style></head><body>${receipt.outerHTML}</body></html>`;
}

export async function printWithQz(receipt: HTMLElement, settings = loadPrintSettings()) {
  if (!settings.printerName) throw new Error("Selecciona una impresora en Configuración.");
  await connectQz();
  const config = qz.configs.create(settings.printerName, {
    copies: settings.copies,
    units: "mm",
    size: { width: settings.paperWidth, height: 297 },
    margins: 0,
    scaleContent: true,
  });
  await qz.print(config, [{
    type: "pixel",
    format: "html",
    flavor: "plain",
    data: receiptHtml(receipt, settings.paperWidth),
  }]);
}

export async function printQzTest(settings: LocalPrintSettings) {
  const sample = document.createElement("div");
  sample.className = "receipt";
  sample.innerHTML = `<h2>Véndeme Fácil</h2><p>Prueba de impresión QZ Tray<br>${new Date().toLocaleString("es-MX")}</p><div class="ticket-line"><span>Conexión</span><b>Correcta</b></div><div class="ticket-total"><span>TOTAL</span><b>$123.45</b></div><footer>Esta es una impresión de prueba</footer>`;
  await printWithQz(sample, settings);
}
