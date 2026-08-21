import { describe, it, expect, beforeEach, vi, Mock } from "vitest";
import { queueLocalAudit, getPendingAudits, syncPendingAudits } from "./audit";
import { apiRequest } from "./api";

// Simple in-memory mock of localStorage for Node environment compatibility
const localStorageMock = (() => {
  let store: Record<string, string> = {};
  return {
    getItem: (key: string) => store[key] || null,
    setItem: (key: string, value: string) => {
      store[key] = value.toString();
    },
    clear: () => {
      store = {};
    },
    removeItem: (key: string) => {
      delete store[key];
    },
  };
})();

Object.defineProperty(globalThis, "localStorage", {
  value: localStorageMock,
  writable: true,
});

// Mock the api module
vi.mock("./api", () => ({
  apiRequest: vi.fn(),
}));

describe("Audit Log System", () => {
  const sessionMock = {
    accessToken: "mock-token",
    expiresAtUtc: new Date(Date.now() + 3600 * 1000).toISOString(),
    user: {
      id: "user-123",
      tenantId: "tenant-abc",
      businessName: "Test Store",
      businessSlug: "test-store",
      displayName: "Cashier Joe",
      email: "joe@test.com",
      role: "Cashier",
    },
  };

  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
  });

  it("should successfully enqueue an audit log to localStorage", () => {
    queueLocalAudit(
      "POS_MANUAL_DISCOUNT",
      "Manual discount of $10 applied.",
      { discount: 10, originalTotal: 100 },
      "user-123",
      "branch-456"
    );

    const pending = getPendingAudits();
    expect(pending).toHaveLength(1);
    expect(pending[0].action).toBe("POS_MANUAL_DISCOUNT");
    expect(pending[0].description).toBe("Manual discount of $10 applied.");
    expect(pending[0].performedByUserId).toBe("user-123");
    expect(pending[0].branchId).toBe("branch-456");
    expect(pending[0].id).toBeDefined();
    expect(typeof pending[0].id).toBe("string");
    expect(pending[0].clientCreatedAtUtc).toBeDefined();

    const details = JSON.parse(pending[0].detailsJson || "{}");
    expect(details.discount).toBe(10);
  });

  it("should handle empty or missing localStorage queue safely", () => {
    const pending = getPendingAudits();
    expect(pending).toEqual([]);
  });

  it("should synchronize pending logs with the API and clear the queue", async () => {
    // 1. Enqueue two logs
    queueLocalAudit("ACTION_1", "Desc 1", null, "user-123", "branch-456");
    queueLocalAudit("ACTION_2", "Desc 2", { field: "value" }, "user-123", "branch-456");

    expect(getPendingAudits()).toHaveLength(2);

    // Mock API response as successful (resolving with undefined/NoContent)
    (apiRequest as Mock).mockResolvedValueOnce(undefined);

    // 2. Run synchronization
    await syncPendingAudits(sessionMock);

    // Verify apiRequest was called with correct payload
    expect(apiRequest).toHaveBeenCalledTimes(1);
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/v1/audit/sync",
      expect.objectContaining({
        method: "POST",
        body: expect.any(String),
      }),
      sessionMock
    );

    const callArgs = (apiRequest as Mock).mock.calls[0];
    const sentBody = JSON.parse(callArgs[1].body);
    expect(sentBody.logs).toHaveLength(2);
    expect(sentBody.logs[0].action).toBe("ACTION_1");
    expect(sentBody.logs[1].action).toBe("ACTION_2");

    // The local queue should now be empty
    expect(getPendingAudits()).toHaveLength(0);
  });

  it("should keep the logs in localStorage if synchronization fails", async () => {
    queueLocalAudit("ACTION_1", "Desc 1", null, "user-123", "branch-456");

    // Mock API response as failure (throw error)
    (apiRequest as Mock).mockRejectedValueOnce(new Error("Network connection error"));

    await syncPendingAudits(sessionMock);

    // The local queue should NOT be cleared
    expect(getPendingAudits()).toHaveLength(1);
  });
});
