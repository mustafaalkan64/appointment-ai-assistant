---
name: architectural-design
description: >-
  Use when designing a new feature, service, endpoint, entity, or integration for the
  appointment-ai-assistant project, when deciding which layer/project code belongs in, or when
  the user asks for an architecture proposal, ADR, design review, or diagram. Covers Clean
  Architecture + vertical slices (.NET 10, MediatR, EF Core, PostgreSQL, Redis RedLock), MCP server,
  ChatApi, and the Next.js 16 frontend.
---

# Architectural Design

Read first (only what is relevant): `docs/architecture.md`, `docs/technical-decisions.md`,
`docs/concurrency-strategy.md`, `docs/database-design.md`, `docs/waiting-queue-design.md`,
`docs/frontend-architecture.md`, `docs/chat-ui-architecture.md`.

## Layer rules (enforced by `tests/AppointmentBooking.ArchitectureTests`)

| Project | May depend on | Contains |
| --- | --- | --- |
| `Domain` | nothing | Aggregates (`Appointment`, `AppointmentSlot`, `Organization`, `Conversation`, `WaitingQueueEntry`), value objects, domain events, domain exceptions |
| `Application` | Domain | `Features/<Area>/<UseCase>/<UseCase>.cs` = Command/Query + Validator + Handler + `IEndpoint` in one file; abstractions (`IApplicationDbContext`, `ITenantContext`, `IDistributedLockService`) |
| `Infrastructure` | Application, Domain | EF Core, migrations, interceptors, RedLock, background workers |
| `Api` / `McpServer` / `ChatApi` | Application, Infrastructure | Hosts only: DI, middleware, endpoint mapping, protocol adapters |

- Business rules go in Domain methods (e.g. `Appointment.Cancel`), not handlers or controllers.
- Hosts never touch `DbContext` for business writes; they `ISender.Send(...)` a MediatR request. (`ChatApi` persists only `Conversation`.)
- MCP tools are thin adapters over MediatR requests — no business logic in `McpToolRegistry`.

## Cross-cutting decisions to respect

- **Multi-tenancy:** every tenant entity implements `IMustHaveTenant`; handlers call `_tenantContext.SetTenant(orgId)`. Cross-tenant reads require explicit `isSuperAdmin` and justification.
- **Double-booking defense (6 layers):** RedLock → transaction → conditional SQL update → insert → partial unique index `IX_Appointments_SlotId_Active` → waiting-queue fallback. New booking-like writes must reuse this pattern.
- **Idempotency:** writes accept an `IdempotencyKey`; persist `IdempotencyRecord` in the same transaction.
- **Time:** store UTC (`DateTimeOffset`), present in organization time zone.
- **Write confirmation in chat** (see `ai-agent-harness`).
- **Frontend:** Next.js 16 App Router with breaking changes — read `frontend/node_modules/next/dist/docs/` before using framework APIs. Data via `@tanstack/react-query` + `frontend/src/lib/api/client.ts`; contracts in `frontend/src/types/index.ts` must mirror backend DTOs.

## Design workflow

1. **Clarify**: actors, tenant scope, read vs write, concurrency exposure, chat/MCP exposure.
2. **Place it**: list each new/changed type with its target project and folder.
3. **Contract**: request/response DTOs, endpoint route, MCP tool schema (if any), frontend type changes.
4. **Data**: entity changes, indexes, migration name, tenant filter impact.
5. **Failure modes**: lock timeout, Redis down (fail-closed), duplicate retry, past-time slots, closed days.
6. **Tests**: which of Unit / Architecture / Integration / Concurrency projects get new tests.
7. **Output**: a short design doc (artifact) with a mermaid diagram (`flowchart` or `sequenceDiagram`) and an ADR-style "Decision / Alternatives / Consequences" section. Update the matching file in `docs/` when the design is accepted.

## Verification

```bash
DOTNET_CLI_HOME=/tmp DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet build --no-restore -m:1
DOTNET_CLI_HOME=/tmp dotnet test tests/AppointmentBooking.ArchitectureTests --no-build
```
