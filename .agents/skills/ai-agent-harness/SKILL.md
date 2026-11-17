---
name: ai-agent-harness
description: >-
  Use when changing, debugging, or evaluating the AI chat agent of the appointment-ai-assistant
  project: ChatApi AiAgentOrchestrator, the McpServer (MCP tools, /mcp/chat, Ollama function
  calling, prompt builder, tool result summarizer), the confirmation state machine, or chat
  latency. Also use when adding a new MCP tool or a new conversational scenario
  (booking, cancellation, availability, working hours, appointment history).
---

# AI Agent Harness

## Runtime map

```
Frontend ChatContainer ──POST /api/chat/messages──▶ ChatApi (AiAgentOrchestrator)
                                                     │
                     ┌───────────────────────────────┤
                     ▼                               ▼
     /mcp  (JSON-RPC tools/call, fast, deterministic)   /mcp/chat (Ollama LLM tool calling, slow)
                     │                               │
                     └──────────▶ McpToolRegistry ◀──┘ ──MediatR──▶ Application handlers
```

| Concern | File |
| --- | --- |
| Conversation state machine, pending confirmation, Turkish date/time parsing | `src/AppointmentBooking.ChatApi/Services/AiAgentOrchestrator.cs` |
| HTTP MCP client (`CallToolAsync`, `ChatAsync`) | `src/AppointmentBooking.ChatApi/Services/McpClient.cs` |
| Tool catalog, schemas, write-tool set, dispatch | `src/AppointmentBooking.McpServer/Services/McpToolRegistry.cs` |
| SDK-exposed tools/resources/prompts (must mirror the registry) | `src/AppointmentBooking.McpServer/Services/McpSdkTools.cs` |
| LLM loop (max 2 tool rounds), confirmation hold for writes | `src/AppointmentBooking.McpServer/Services/Ollama/McpFunctionCallingOrchestrator.cs` |
| System prompt | `src/AppointmentBooking.McpServer/Services/Ollama/OllamaPromptBuilder.cs` |
| JSON → Turkish summary fallback | `src/AppointmentBooking.McpServer/Services/McpToolResultSummarizer.cs` |
| Chat UI card mapping (`requiresConfirmation`, `proposedAction`, `isCompleted`) | `frontend/src/components/chat/ChatContainer.tsx`, `ChatMessageBubble.tsx` |

## Non-negotiable invariants

1. **No write without confirmation.** `create_appointment`, `cancel_appointment`, `confirm_appointment`, `cancel_queue_request` must first return `RequiresConfirmation: true` + `ProposedAction`, persist via `Conversation.SetPendingConfirmation(...)`, and only execute on an explicit "Evet". Keep the write-tool sets in `AiAgentOrchestrator`, `McpToolRegistry.WriteToolNames` and the frontend in sync.
2. **Never invent IDs.** `slot_id` / `appointment_id` must come from a tool result (`get_available_slots`, `get_customer_appointments`).
3. **Times are organization-local.** Convert with the org `TimeZone` (default `Europe/Istanbul`); never hard-code `+03:00`. `LocalDisplay` format is `dd.MM.yyyy HH:mm - HH:mm` — frontend regexes depend on it.
4. **Response contract** (`AgentResponseDto`) is consumed by the frontend:
   - booking proposal → `requiresConfirmation=true, proposedAction="create_appointment"`
   - cancel proposal → `requiresConfirmation=true, proposedAction="cancel_appointment"` (renders red cancel card)
   - completed write → `isCompleted=true`; success text contains `APT-...` number
   - Content must be human Turkish, no JSON / GUID / field names.
5. **Latency budget:** deterministic paths (via `/mcp` `tools/call`) should answer in < 300 ms. Only route to `/mcp/chat` (Ollama) for genuinely open-ended input, and never feed raw slot JSON back into the LLM (`BuildModelHistory` truncation).

> [!IMPORTANT]
> `docs/security.md` states tool selection should be done by the LLM, not keyword if/else. The current orchestrator uses deterministic Turkish parsing for speed. When changing intent routing, state explicitly which path you are using and update `docs/security.md` / `docs/mcp-tools.md` if the policy changes.

## Adding a new MCP tool

1. Add `McpToolDefinition` + dispatch case in `McpToolRegistry` (Turkish description stating *when* to use it, required args).
2. Mirror it in `McpSdkTools` with correct `ReadOnly` / `Destructive` / `Idempotent` annotations.
3. If it writes, add it to every write-tool set (see invariant 1).
4. Add a summarizer branch in `McpToolResultSummarizer` if its output is JSON.
5. Update `tests/AppointmentBooking.IntegrationTests/Mcp/McpServerTests.cs` (`tools/list` expectations) and `docs/mcp-tools.md`.

## Adding a conversational scenario

1. Write the scenario first as a multi-turn test in `tests/AppointmentBooking.IntegrationTests/Chat/ChatApiIntegrationTests.cs` (seed org/slot/customer, post messages, assert `requiresConfirmation`, `proposedAction`, DB state). Use `TestOllamaClient` as the deterministic LLM double.
2. Cover Turkish variants: `yarın`, `bugün`, `yarından sonra`, `haftaya cuma`, `20 kasım`, `20.11.2026`, `saat 16da`, `öğleden sonra 4te`, `4 buçukta`, `16:00`.
3. Implement, then verify with the `test-engineer` skill.

## Manual smoke test

Start services (ports from `launchSettings.json`: Api 5099, ChatApi 5111, McpServer 5251), then:

```bash
curl -s -X POST http://localhost:5111/api/chat/messages -H 'Content-Type: application/json' \
  -d '{"organizationId":"<ORG>","customerId":"<CUSTOMER>","message":"Yarın öğleden sonra 4te randevu almak istiyorum"}'
```

Expect `requiresConfirmation: true`, then send `"Evet"` with the returned `conversationId` and expect `isCompleted: true`. Measure with `curl -w '%{time_total}'`.
