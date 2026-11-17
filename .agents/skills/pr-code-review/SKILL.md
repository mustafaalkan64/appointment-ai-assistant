---
name: pr-code-review
description: >-
  Use when the user asks to review a pull request, branch, commit, diff, or uncommitted changes in
  the appointment-ai-assistant project, or asks "is this ready to merge". Produces a prioritized,
  file/line-referenced review covering correctness, concurrency, tenancy, chat confirmation safety,
  API/frontend contract drift, tests, and docs.
---

# PR Code Review

## 1. Collect the diff

```bash
git status
git diff --stat <base>...HEAD        # PR/branch (base usually origin/main)
git diff <base>...HEAD -- <path>     # per file
git diff HEAD                        # uncommitted work
```

Read every changed file fully where the hunk is not self-explanatory. Do not review generated/ignored paths (`bin/`, `obj/`, `.next/`, `frontend/src/app/globals.generated.css`, `Migrations/*.Designer.cs`).

## 2. Project-specific checklist

**Correctness & domain**
- Business rules live in Domain entities; handlers orchestrate only.
- `DateTimeOffset` UTC in storage; org time zone for display. No hard-coded `+03:00`.
- Status transitions use domain methods (`Confirm`, `Cancel`, `Complete`), not property sets.

**Concurrency & data integrity**
- Booking/cancel paths keep RedLock + transaction + conditional SQL + idempotency record.
- No `SaveChanges` outside the transaction in those paths; rollback on exception.
- New queries on hot paths are `AsNoTracking()` and indexed.

**Multi-tenancy**
- `SetTenant(orgId)` before queries; any `IgnoreQueryFilters()` or `isSuperAdmin: true` is justified.
- Entities with tenant data implement `IMustHaveTenant`.
- Conversation access verifies `CustomerId` ownership.

**Chat / MCP agent**
- Write tools still require confirmation; write-tool sets consistent across ChatApi, McpServer, frontend.
- Tool descriptions/schemas updated in both `McpToolRegistry` and `McpSdkTools`.
- No raw JSON/GUIDs leaked into user-facing Turkish text.
- `AgentResponseDto` changes reflected in `frontend/src/types/index.ts` and `ChatContainer.tsx`.

**Frontend**
- Types match backend DTOs; no `any`; no `setState` synchronously inside `useEffect` (project lint rule).
- React Query invalidation after mutations.

**Tests & docs**
- New behavior has a test in the right project (see `test-engineer`).
- `docs/*.md` updated when contracts, tools, or security policy change.

## 3. Verify, don't guess

Run the `test-engineer` verification commands for the touched areas. Report what actually ran and what could not run (e.g. missing Postgres/Redis).

## 4. Output format

Write the review as a markdown artifact:

```markdown
## Summary
<1–3 sentences: what the PR does, merge recommendation: Approve / Approve with nits / Request changes>

## Blocking
- [file.cs:L12-L30](file:///abs/path#L12-L30) — problem, impact, concrete fix

## Should fix
- ...

## Nits
- ...

## Verification
- `dotnet build` ✅ / `tsc --noEmit` ✅ / integration tests ⚠️ not run (no Postgres)
```

Severity guide: **Blocking** = data loss, double booking, tenant leak, unconfirmed write, broken build/contract. **Should fix** = bugs in edge cases, missing tests, perf on hot path. **Nits** = style/naming.
