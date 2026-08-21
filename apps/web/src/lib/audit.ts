import { apiRequest, AuthSession } from "./api";

export interface LocalAuditLog {
  id: string; // client-side generated UUID
  action: string;
  description: string;
  detailsJson?: string;
  performedByUserId: string;
  branchId: string;
  clientCreatedAtUtc: string;
}

const AUDIT_QUEUE_KEY = "vendemefacil.audit_queue";

/**
 * Enqueues a critical action audit log locally in the browser's localStorage.
 */
export function queueLocalAudit(
  action: string,
  description: string,
  details: Record<string, any> | null,
  userId: string,
  branchId: string
): void {
  try {
    const queue: LocalAuditLog[] = JSON.parse(
      localStorage.getItem(AUDIT_QUEUE_KEY) ?? "[]"
    );

    const newLog: LocalAuditLog = {
      id: crypto.randomUUID(), // Standard browser native UUID generation
      action,
      description,
      detailsJson: details ? JSON.stringify(details) : undefined,
      performedByUserId: userId,
      branchId,
      clientCreatedAtUtc: new Date().toISOString(),
    };

    queue.push(newLog);
    localStorage.setItem(AUDIT_QUEUE_KEY, JSON.stringify(queue));
  } catch (e) {
    console.error("No se pudo encolar el registro de auditoría local:", e);
  }
}

/**
 * Reads all pending audits in the localStorage queue.
 */
export function getPendingAudits(): LocalAuditLog[] {
  try {
    return JSON.parse(localStorage.getItem(AUDIT_QUEUE_KEY) ?? "[]");
  } catch {
    return [];
  }
}

/**
 * Synchronizes the local audit logs with the backend.
 * This is designed to be idempotent: the server filters out any logs already in the database using their client-side generated UUIDs.
 */
export async function syncPendingAudits(session: AuthSession | null): Promise<void> {
  if (!session) return;

  const queue = getPendingAudits();
  if (queue.length === 0) return;

  try {
    // Send the batch of local audit logs to the backend
    await apiRequest<void>(
      "/api/v1/audit/sync",
      {
        method: "POST",
        body: JSON.stringify({ logs: queue }),
      },
      session
    );

    // Filter out the successfully synchronized items from the queue.
    // We check the queue in localStorage again in case new entries were added while the sync was in progress.
    const currentQueue = getPendingAudits();
    const sentIds = new Set(queue.map((x) => x.id));
    const updatedQueue = currentQueue.filter((x) => !sentIds.has(x.id));

    localStorage.setItem(AUDIT_QUEUE_KEY, JSON.stringify(updatedQueue));
  } catch (error) {
    console.warn("La sincronización de auditoría falló (modo offline o error de red):", error);
  }
}
