# Security & Tenant Isolation Architecture

## 1. Multi-Tenant Data Isolation
- **Tenant Context**: Managed per asynchronous request execution flow via `AsyncLocal<Guid>` in `TenantContext.cs`.
- **Global Query Filters**: Parameterized at EF Core model compilation, preventing accidental cross-tenant data leakage across all queries.
- **Auditable & Outbox Interceptors**: Automatically enforce `OrganizationId` on entity insertion.

---

## 2. Concurrency Security & Fail-Closed Defense
1. **Redis Unavailable**: If Redis cluster is offline or unreachable, distributed lock acquisition fails immediately, preventing corrupted writes without distributed guarantees.
2. **PostgreSQL Partial Unique Index**: Physical database engine constraint `IX_Appointments_SlotId_Active` guarantees that even if application server memory or network fails, the database rejects any attempt to insert a second active appointment for the same slot.
3. **Idempotency Hashing**: SHA-256 request hashing with composite keys (`CustomerId:Operation:IdempotencyKey`) prevents replay attacks and accidental duplicate billing or reservations.

---

## 3. AI Agent Confirmation Security
- The AI Agent in `AppointmentBooking.ChatApi` enforces a strict two-phase state machine:
  - Destructive actions (`create_appointment`, `cancel_appointment`) **CANNOT** be executed in a single turn without explicit customer confirmation (`requiresConfirmation: true`).
  - Proposed parameters (slot time, appointment ID, reason) are frozen in JSON context until confirmed.
