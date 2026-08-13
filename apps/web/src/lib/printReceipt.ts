import { loadPrintSettings, printWithQz } from "./qzPrinting";

function printWithBrowser(receipt: HTMLElement) {
  const printWindow = window.open("", "_blank", "width=420,height=720");
  if (!printWindow) throw new Error("Permite las ventanas emergentes para imprimir el ticket.");
  printWindow.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Ticket</title><style>
    @page{size:80mm auto;margin:0}*{box-sizing:border-box}html,body{margin:0;padding:0;background:#fff;color:#000}
    body{width:80mm;font-family:monospace}.receipt{width:80mm;padding:6mm 5mm;background:#fff}
    .receipt-brand{width:16mm;height:16mm;margin:0 auto 3mm;border:1px solid #222;border-radius:50%;display:grid;place-items:center;overflow:hidden}
    .receipt-brand img{width:100%;height:100%;object-fit:contain}.receipt h2,.receipt p{text-align:center;margin:2mm 0}.receipt p{font-size:9pt;line-height:1.5}
    .ticket-line{padding:2.5mm 0;display:flex;justify-content:space-between;border-top:1px dashed #777;font-size:9pt}.ticket-line small{display:block}
    .ticket-total,.ticket-payment{padding:2.5mm 0;display:grid;grid-template-columns:1fr auto;border-top:1px dashed #222}.ticket-total{font-size:13pt}.ticket-payment{font-size:9pt}
    .receipt footer{padding-top:4mm;border-top:1px dashed #777;text-align:center;font-size:9pt}
  </style></head><body>${receipt.outerHTML}</body></html>`);
  printWindow.document.close();
  const print = () => { printWindow.focus(); printWindow.print(); printWindow.close(); };
  const images = Array.from(printWindow.document.images);
  if (!images.length || images.every((image) => image.complete)) window.setTimeout(print, 150);
  else Promise.all(images.map((image) => new Promise<void>((resolve) => { image.onload = image.onerror = () => resolve(); }))).then(print);
}

export async function printReceipt(receipt: HTMLElement | null) {
  if (!receipt) return;
  const settings = loadPrintSettings();
  if (settings.mode === "qz") {
    try {
      await printWithQz(receipt, settings);
      return;
    } catch (reason) {
      const detail = reason instanceof Error ? reason.message : "QZ Tray no está disponible.";
      if (!window.confirm(`${detail}\n\n¿Deseas imprimir con el navegador?`)) throw reason;
    }
  }
  printWithBrowser(receipt);
}
