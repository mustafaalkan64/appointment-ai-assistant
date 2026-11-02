# Architecture Decision Records (ADR) & Technical Decisions

## ADR 001: Architecture Pattern Selection
- **Status**: Accepted
- **Context**: The platform needs to support high-concurrency multi-tenant appointment booking, AI Agent interaction via Model Context Protocol (MCP), and independent maintainability of features without coupling.
- **Decision**: Combine **Clean Architecture** with **Vertical Slice Architecture**.
  - `Domain`: Enterprise business rules, aggregate roots, entities, domain events, value objects, domain exceptions. Zero framework dependencies.
  - `Application`: Features organized by vertical slices (`Features/{Aggregate}/{FeatureName}/Command, Handler, Validator, Response, Endpoint`). Cross-cutting pipeline behaviors (Validation, Logging, Idempotency, Tenant resolution).
  - `Infrastructure`: Data persistence (EF Core 10), Redis & RedLock, RabbitMQ/Redis messaging, Outbox background worker, OpenTelemetry & Serilog observability.
  - `Api`: RESTful presentation, JWT & Tenant middleware, OpenAPI/Swagger.
  - `McpServer`: Model Context Protocol host exposing standardized AI tools directly delegating to Application MediatR handlers.
  - `ChatApi`: Customer-facing conversational interface orchestrating AI Agent, MCP Client, and explicit user confirmation flow.
- **Trade-offs**: Slightly more files per feature, but delivers extreme cohesion, zero feature bleed, and clear isolation for unit and integration testing.

---

## ADR 002: Multi-Tenancy Strategy
- **Status**: Accepted
- **Context**: The system serves multiple organizations (tenants). Data leak between tenants is catastrophic.
- **Decision**: Implement **Logical Multi-Tenancy with Global Query Filters and Row-Level Tenant Isolation**:
  - Every tenant-scoped entity implements `IMustHaveTenant` with a non-nullable `OrganizationId`.
  - Database schema enforces composite indexes starting with `OrganizationId`.
  - EF Core `TenantInterceptor` and `HasQueryFilter(e => e.OrganizationId == _tenantContext.OrganizationId)` applied globally.
  - SuperAdmin bypass capability through an explicit scope `ITenantBypassScope`.
  - HTTP requests resolve tenant via Hostname/Subdomain, `X-Organization-Id` header, or JWT claims.
- **Alternatives Considered**:
  - *Database-per-tenant*: High infrastructure overhead for hundreds of small clinics/salons.
  - *Schema-per-tenant*: Complex migration management in EF Core.
  - *Selected*: Shared database, shared schema with automated EF Core query filtering + DB-level composite constraints.

---

## ADR 003: Multi-Layer Concurrency & Zero Double-Booking Strategy
- **Status**: Accepted
- **Context**: Under flash-crowds or high-demand appointments, 100 to 1,000 customers can request the same 30-minute appointment slot simultaneously.
- **Decision**: Enforce a **6-layer Defense-in-Depth Concurrency Model**:
  1. **Layer 1: Distributed Lock (RedLock.net on Redis)**:
     - Resource key: `appointment:slot:{organizationId}:{slotId}`.
     - Fast failure fast path: If lock acquisition times out (e.g. 1500ms), redirect caller to the Waiting Queue instead of blocking worker threads.
  2. **Layer 2: Atomic State Transition Conditional SQL Update**:
     ```sql
     UPDATE "AppointmentSlots"
     SET "Status" = 'Booked', "UpdatedAt" = NOW()
     WHERE "Id" = @SlotId AND "Status" = 'Available' AND "IsActive" = TRUE;
     ```
     If rows affected != 1, rollback transaction immediately.
  3. **Layer 3: Database Filtered / Partial Unique Index**:
     ```sql
     CREATE UNIQUE INDEX "IX_Appointments_SlotId_Active"
     ON "Appointments" ("SlotId")
     WHERE "Status" != 'Cancelled';
     ```
     Guarantees that even if Redis completely crashes or distributed locks fail, the database engine physically rejects duplicate active bookings.
  4. **Layer 4: Optimistic Concurrency Control (RowVersion / xmin)**:
     - Detect concurrent slot modifications or administrative slot deactivations.
  5. **Layer 5: Idempotency Key Tracking**:
     - Unique constraint on `(CustomerId, Operation, IdempotencyKey)`.
     - Repeated API/MCP calls return the previously created booking without generating duplicate state.
  6. **Layer 6: Waiting Queue FIFO Fallback**:
     - Requests that miss the lock or find the slot currently pending enter a FIFO queue with bounded TTL reservations.
- **Trade-offs**: Adds coordination overhead, but ensures 100% mathematical guarantee against double-booking under any load.

---

## ADR 004: Waiting Queue & Asynchronous Processing
- **Status**: Accepted
- **Context**: When a slot is congested or an existing reservation is cancelled/expires, queued users should have a fair FIFO chance to acquire the slot.
- **Decision**:
  - Persist queue entries in `WaitingQueueEntries` table with Redis-assisted FIFO sequencing.
  - Queue worker evaluates the head of the queue when a slot becomes available.
  - Transition flow: `Waiting -> Processing -> Reserved -> Confirmed`.
  - `Reserved` status has a strict expiration window (e.g., 5 minutes) giving the customer time to confirm via Chat API. If expired, worker automatically advances to the next customer.

---

## ADR 005: Model Context Protocol (MCP) Server Architecture
- **Status**: Accepted
- **Context**: AI Agents must perform appointment operations on behalf of users, but must NOT have direct database access or bypass business rules.
- **Decision**:
  - Create a dedicated `AppointmentBooking.McpServer` implementing standard MCP tool endpoints.
  - Tools are pure orchestrators: they map JSON schema inputs into MediatR Commands/Queries inside `AppointmentBooking.Application`.
  - Every MCP Tool invocation validates OrganizationId, CustomerId, and passes through the same validation and concurrency pipeline as standard HTTP endpoints.
  - Chat API acts as the MCP Client, calling the MCP Server over Streamable HTTP/SSE.

---

## ADR 006: TimeZone & DateTime Handling
- **Status**: Accepted
- **Context**: Organizations operate in diverse geographic time zones. DST (Daylight Saving Time) changes can cause offset errors.
- **Decision**:
  - All database timestamps are stored in UTC (`DateTimeOffset` or UTC `DateTime`).
  - Organizations configure their IANA TimeZone ID (e.g., `"Europe/Istanbul"`, `"America/New_York"`).
  - Slot generation converts organization working hours into unambiguous UTC intervals using `TimeZoneInfo`.
  - Client responses include ISO-8601 UTC timestamps alongside localized display strings for AI and user prompts.
