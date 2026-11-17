---
name: security-review
description: >-
  Use when the user asks for a security review, threat model, vulnerability check, or hardening of
  the appointment-ai-assistant project, or when a change touches authentication, tenant isolation,
  MCP/chat agent tools, CORS/Origin handling, idempotency, secrets/configuration, or dependency
  upgrades (including Next.js security advisories).
---

# Security Review

Baseline policy: `docs/security.md`. Findings must reference concrete files/lines.

## Threat areas & what to check

**1. Tenant isolation (highest impact)**
- Every tenant entity: `IMustHaveTenant` + EF global query filter; handlers call `SetTenant(orgId)`.
- Grep for bypasses and justify each:
  ```bash
  grep -rn "IgnoreQueryFilters\|isSuperAdmin: true" src/
  ```
- `organizationId` / `customerId` arrive from the client body (`SendMessageRequest`, frontend fallback GUIDs in `ChatContainer.tsx`). Check they are bound to an authenticated identity, not trusted blindly (IDOR).
- Conversation endpoints: `GET /api/chat/conversations/{id}` uses `IgnoreQueryFilters()` without ownership check — verify/flag.

**2. AI agent / MCP**
- Prompt injection: user text must not be able to trigger write tools without the confirmation state machine (`AiAgentOrchestrator` pending confirmation).
- Write tools set consistent in ChatApi, McpServer, frontend; tool args (`appointment_id`, `slot_id`) validated against the caller's tenant/customer in Application handlers, not just in the agent.
- MCP `Origin` allowlist (`McpServer:AllowedOrigins`); no wildcard CORS on `/mcp`. Note ChatApi uses `AllowAnyOrigin()` — acceptable only for local dev.
- Tool results / error messages must not leak stack traces or other tenants' data into chat replies (`Execution failed: {ex.Message}` in `McpToolRegistry`).

**3. Concurrency & integrity**
- RedLock fail-closed, partial unique index, idempotency keys scoped by `CustomerId:Operation:Key`.
- Idempotency keys generated server-side for chat flows must be deterministic per logical action.

**4. Authentication & authorization**
- Check for `RequireAuthorization` / auth middleware on Api, ChatApi, McpServer endpoints. Frontend stores session in `localStorage` (`ab_session`) — note XSS exposure.

**5. Configuration & secrets**
- `appsettings*.json` contain DB/RabbitMQ passwords — must be dev-only; production via env vars/secret store. Grep:
  ```bash
  grep -rniE "password=|secret|apikey|token" --include=*.json --include=*.cs --include=*.ts src frontend/src
  ```

**6. Input validation & errors**
- FluentValidation validators exist for every command; `GlobalExceptionHandlerMiddleware` returns ProblemDetails without internals.
- Regex on user input (Turkish parsing) bounded — no catastrophic backtracking.

**7. Dependencies**
```bash
DOTNET_CLI_HOME=/tmp dotnet list package --vulnerable --include-transitive
npm --prefix frontend audit --omit=dev
```
Network access is required; request it only for these commands. `next build` currently reports a Next.js security advisory — recommend `next upgrade` in a separate branch.

## Output

Markdown artifact with a table:

| # | Severity (Critical/High/Medium/Low) | Area | Location | Finding | Recommendation |
| --- | --- | --- | --- | --- | --- |

Then "Verified safe" (checks that passed) and "Not verified" (what couldn't be checked and why). Do not apply fixes unless asked; when fixing, add a regression test via `test-engineer`.
