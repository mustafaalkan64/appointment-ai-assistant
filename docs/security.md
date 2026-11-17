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
- The chat LLM selects a tool from its advertised descriptions and schemas; application code does not classify user intent with keyword-based `if/else` rules.
- Write tools are held for explicit user confirmation in the chat flow. This is a security policy applied after model selection, not a tool-selection rule. The selected tool name and arguments are retained as a pending action until confirmation or rejection.
- MCP tool annotations are advisory metadata only. Tool handlers and application-layer authorization/validation remain authoritative.
- Streamable HTTP requests that include an `Origin` header must match `McpServer:AllowedOrigins`; requests without Origin are allowed. Do not use wildcard CORS for the MCP endpoint.
