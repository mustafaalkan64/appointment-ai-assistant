---
name: refactor-code-quality
description: >-
  Use when the user asks to refactor, clean up, simplify, reduce duplication, improve readability or
  maintainability, fix lint warnings, or split large files in the appointment-ai-assistant project
  (.NET backend or Next.js frontend). Behavior-preserving changes only, verified by tests.
---

# Refactor & Code Quality

## Ground rules

1. **Behavior-preserving.** No contract changes (DTOs, routes, MCP tool names/schemas, `AgentResponseDto` fields, `LocalDisplay` format) unless the user asks. If unavoidable, apply `pr-code-review` contract checks.
2. **Safety net first.** Confirm tests cover the code; if not, add characterization tests (see `test-engineer`) before refactoring.
3. **Small steps.** One refactoring type per step; build + test after each.
4. **Respect layers** (see `architectural-design`). Moving logic toward Domain is good; moving it into hosts is not.

## Known hotspots in this repo

| Area | Smell | Direction |
| --- | --- | --- |
| `ChatApi/Services/AiAgentOrchestrator.cs` (~1.5k lines) | God class: state machine + intent routing + Turkish date/time parsing + formatting | Extract `TurkishDateTimeParser` (`ResolveTargetDate`, `TryExtractTime`), `AppointmentMatcher` (`FindMatchingAppointment`, `TryMatchOrdinal`), `ChatReplyFormatter`; keep orchestrator as coordinator. Parsers are pure → unit-test them directly. |
| Same file | Repeated `new AgentResponseDto(..., false, null, null, false)` and `conversation.AddMessage` pairs | Single `Reply(conversation, text, intent, ...)` helper |
| Same file | Repeated slot/appointment JSON property access via `TryGetProp` | Deserialize MCP results into typed records once |
| `McpToolRegistry` vs `McpSdkTools` | Duplicated tool descriptions | Single source of descriptions (constants) |
| Application handlers (`GetCustomerAppointments`, `GetOrganizationAppointments`) | Duplicated DTO projection | Shared mapper |
| `frontend/src/components/chat/ChatContainer.tsx` | Response→card mapping inline in `handleSendMessage` | Extract pure `mapAgentResponseToMessage()` |
| `frontend/src/lib/api/client.ts` | `any` in `setWeeklyAvailability` | Typed command |
| Frontend lint | `react-hooks/set-state-in-effect`, unused imports | Lazy `useState` initializers / derived `useMemo` instead of effects |

## Workflow

1. State the target, the smell, and the intended end shape (bullet list of new files/types).
2. Add/confirm tests for current behavior.
3. Refactor incrementally; keep public signatures stable.
4. Verify:
   ```bash
   .agents/skills/test-engineer/scripts/verify.sh
   ./frontend/node_modules/.bin/eslint -c frontend/eslint.config.mjs <changed frontend files>
   ```
5. Report: what moved where, metrics (file length, duplicated blocks removed, warnings fixed), and verification results.

## Quality bar

- C#: nullable enabled, `sealed` by default, `async` all the way with `CancellationToken`, no swallowed exceptions without logging, `AsNoTracking` for reads.
- TS: no `any`, explicit types for API contracts, components < ~300 lines, pure helpers outside components.
- No dead code or commented-out blocks left behind.
