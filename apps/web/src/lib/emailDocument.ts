import { apiRequest, AuthSession } from "./api";

type EmailDocumentOptions = {
  session: AuthSession;
  documentType: "sale-ticket" | "layaway-receipt" | "layaway-reminder" | "cash-report";
  reference: string;
  content: string;
  defaultEmail?: string | null;
};

function requestRecipient(defaultEmail?: string | null) {
  return new Promise<string | null>((resolve) => {
    const layer = document.createElement("div");
    layer.className = "email-dialog-layer";
    const form = document.createElement("form");
    form.className = "email-dialog";
    form.innerHTML = `
      <div class="email-dialog-icon" aria-hidden="true">@</div>
      <h2>Enviar por email</h2>
      <p>Captura el correo electrónico que recibirá el documento.</p>
      <label>Correo del destinatario<input type="email" name="email" required autocomplete="email" placeholder="cliente@correo.com"></label>
      <div class="email-dialog-error" role="alert"></div>
      <div class="email-dialog-actions"><button type="button" class="button secondary">Cancelar</button><button class="button primary">Enviar</button></div>`;
    const input = form.elements.namedItem("email") as HTMLInputElement;
    const cancel = form.querySelector<HTMLButtonElement>('button[type="button"]')!;
    const error = form.querySelector<HTMLDivElement>(".email-dialog-error")!;
    input.value = defaultEmail?.trim() ?? "";

    const finish = (value: string | null) => {
      document.removeEventListener("keydown", onKeyDown);
      layer.remove();
      resolve(value);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") finish(null);
    };
    cancel.addEventListener("click", () => finish(null));
    layer.addEventListener("mousedown", (event) => {
      if (event.target === layer) finish(null);
    });
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const email = input.value.trim().toLowerCase();
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
        error.textContent = "Captura un correo electrónico válido.";
        input.focus();
        return;
      }
      finish(email);
    });
    document.addEventListener("keydown", onKeyDown);
    layer.appendChild(form);
    document.body.appendChild(layer);
    window.setTimeout(() => input.focus(), 0);
  });
}

export async function emailDocument(options: EmailDocumentOptions) {
  const email = await requestRecipient(options.defaultEmail);
  if (email === null) return false;

  await apiRequest("/api/v1/documents/email", {
    method: "POST",
    body: JSON.stringify({
      email,
      documentType: options.documentType,
      reference: options.reference,
      content: options.content,
    }),
  }, options.session);
  return true;
}
