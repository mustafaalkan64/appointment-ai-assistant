---
name: test-engineer
description: >-
  Use when writing, fixing, or running tests for the appointment-ai-assistant project, when the
  user asks to verify a change, increase coverage, reproduce a bug with a test, or run the
  unit / architecture / integration / concurrency suites or frontend type/lint checks.
---

# Test Engineer

## Test projects

| Project | Scope | Infra needed |
| --- | --- | --- |
| `tests/AppointmentBooking.UnitTests` | Domain invariants, value objects, MCP orchestrator with mocked `IOllamaClient`, summarizer | none |
| `tests/AppointmentBooking.ArchitectureTests` | Layer dependency rules (NetArchTest) | none |
| `tests/AppointmentBooking.IntegrationTests` | `WebApplicationFactory` for Api/ChatApi/McpServer, chat multi-turn flows, MCP JSON-RPC | PostgreSQL + Redis (see `docker-compose`) |
| `tests/AppointmentBooking.ConcurrencyTests` | 10–500 parallel bookings, zero double-booking | PostgreSQL + Redis |

Stack: xUnit, FluentAssertions, NSubstitute. Integration tests that touch ChatApi + McpServer use `extern alias ChatApiAssembly` / `McpServerAssembly` and the deterministic `TestOllamaClient`.

## Commands

Run [scripts/verify.sh](./scripts/verify.sh) for the fast gate (build + unit + architecture + frontend typecheck). Individual commands:

```bash
export DOTNET_CLI_HOME=/tmp DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
dotnet build --no-restore -m:1
dotnet test tests/AppointmentBooking.UnitTests --no-build
dotnet test tests/AppointmentBooking.ArchitectureTests --no-build
dotnet test tests/AppointmentBooking.IntegrationTests --no-build --filter "FullyQualifiedName~Chat"
./frontend/node_modules/.bin/tsc -p frontend/tsconfig.json --noEmit
./frontend/node_modules/.bin/eslint -c frontend/eslint.config.mjs <changed files>
```

> [!NOTE]
> In the agent sandbox `DOTNET_CLI_HOME` must be set, and integration/concurrency tests hang without reachable Postgres/Redis — run them as background tasks, and if they stall, report "not run: infrastructure unavailable" instead of claiming success. `npm run lint` on the whole frontend has pre-existing errors; lint only changed files.

## Writing tests

- **Bug fix → failing test first**, then fix.
- **Naming:** `Method_Condition_ExpectedResult` (existing convention).
- **Domain:** test through public entity methods; assert exceptions (`InvalidAppointmentStateException`, `DomainValidationException`).
- **Chat scenarios:** seed `Organization`, `AppointmentSlot` (UTC; 13:00 UTC = 16:00 Istanbul), `Customer` with unique phone; post to `/api/chat/messages`; assert `requiresConfirmation`, `proposedAction`, `isCompleted`, `content`, **and DB state** (appointment row/status, slot status). Always assert no row exists before confirmation.
- **Use dynamic dates** (`DateTime.UtcNow.AddDays(1)`), never fixed calendar dates, and unique slugs (`"x-" + Guid.NewGuid().ToString("N")[..8]`).
- **Concurrency:** assert exactly one `201`, rest `202/409`, and exactly one active DB row.
- **MCP tools:** update `Mcp_ToolsList_ShouldReturnAllRegisteredTools` when adding tools.

## Reporting

List each command run with pass/fail counts, failing test names with the first relevant assertion message, and anything skipped with the reason.
