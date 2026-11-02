# Frontend-to-Backend API Integration Mapping

## 1. Service Endpoints Overview

| Service | Default Local URL | Responsibilities |
|---|---|---|
| **Core API** | `http://localhost:5099` | Organizations, Weekly Availability, Slot Generation, Appointments, Customer Registration, Waiting Queue |
| **Chat API** | `http://localhost:5111` | AI Conversation Orchestrator, Ollama/OpenAI Qwen Integration, Multi-turn Booking Assistant |
| **MCP Server** | `http://localhost:5251` | Tool execution server for Core API commands (internal to ChatApi) |

---

## 2. API Endpoint Mapping

### Organizations (`organizationApi`)
- `GET /api/organizations` – Retrieve all registered organizations (Public & Discovery).
- `GET /api/organizations/{id}` – Retrieve organization details and timezone.
- `GET /api/organizations/by-slug/{slug}` – Retrieve organization by unique URL slug.
- `POST /api/organizations` – Register a new organization with initial admin account.

### Availability & Slots (`availabilityApi` / `organizationApi`)
- `POST /api/organizations/{orgId}/availability/weekly` – Update weekly working hours (Monday–Sunday intervals, slot duration).
- `POST /api/organizations/{orgId}/slots/generate` – Batch generate appointment slots for a date range (`startDate`, `endDate`, `skipExisting`).
- `PATCH /api/organizations/{orgId}/slots/{slotId}/toggle` – Activate or deactivate an individual slot (lock/unlock).
- `GET /api/organizations/{orgId}/slots/available` – Query available slots filtered by date or date range.

### Appointments (`appointmentApi`)
- `GET /api/organizations/{orgId}/appointments` – Retrieve organization appointment records for Admin Dashboard.
- `GET /api/customers/{customerId}/appointments` – Retrieve customer appointments filtered by organization and status.
- `GET /api/appointments/{id}` – Retrieve appointment details.
- `POST /api/appointments` – Create a new appointment with idempotency key.
- `POST /api/appointments/{id}/cancel` – Cancel an existing appointment with reason.

### Waiting Queue (`waitingQueueApi`)
- `GET /api/queue/status` – Retrieve queue position, status, and reservation expiration for contested slots.
- `POST /api/queue/cancel` – Remove customer entry from waiting queue.

### Chat API (`chatApi`)
- `POST /api/chat/messages` – Send multi-turn customer message with `{ organizationId, customerId, message, conversationId }`.
- `POST /api/chat/conversations` – Start new conversation session.
- `GET /api/chat/conversations/{id}` – Retrieve conversation history.

---

## 3. Tenancy & Authentication Headers

All requests sent through `lib/api/client.ts` automatically inject:
1. `Authorization: Bearer <token>` when a user session exists.
2. `X-Organization-Id: <orgGuid>` resolved by `TenantResolutionMiddleware` on the backend.
3. Centralized `ApiError` class parses standard ProblemDetails (`title`, `detail`, `error`) returned by ASP.NET Core.
