# Model Context Protocol (MCP) Server Specification

## 1. Overview
`AppointmentBooking.McpServer` exposes the MCP server through the official .NET SDK and Streamable HTTP at `/mcp`. Tool selection for the optional `/mcp/chat` experience is performed by the LLM from tool descriptions and schemas; the server performs deterministic name-to-handler dispatch only after a tool has been selected.

The MCP endpoint uses stateless Streamable HTTP. There is no separate `/mcp/sse` endpoint. The product-specific `/mcp/chat` endpoint is not an MCP protocol endpoint.

Requests without an `Origin` header (typical MCP clients) are accepted. If an Origin is supplied, it must exactly match an authority configured in `McpServer:AllowedOrigins`; configure browser origins explicitly. The default is an empty allowlist, and CORS does not allow arbitrary origins.

Tool annotations (`readOnly`, `destructive`, `idempotent`, and `openWorld`) are published as advisory metadata; they do not replace authorization, validation, or explicit confirmation for writes.

## 2. Protocol Endpoints
- `POST /mcp`: MCP Streamable HTTP endpoint, including JSON and event-stream responses as negotiated by the client.
- `GET /mcp`: MCP Streamable HTTP endpoint for negotiated server-to-client streams.
- `GET /health`: Service health.
- `POST /mcp/chat`: Product-specific Ollama chat integration (not part of MCP).

## 3. Resources

Resources are discovered with `resources/list` and read by URI using `resources/read`:

| URI | Contents |
| --- | --- |
| `appointment://system/booking-guidelines` | Slot validation, user confirmation, and safe booking/cancellation principles. |
| `appointment://system/tool-selection` | Guidance for choosing tools from their descriptions and handling missing arguments. |

Organization-specific information is retrieved through `get_organization_info`; the server does not expose a hard-coded organization-list resource.

## 4. Prompts

Reusable prompt templates are discovered with `prompts/list` and expanded with `prompts/get`:

- `find_availability`: Find available dates/slots for a requested date range.
- `book_appointment`: Check availability and guide a user through selecting and booking a slot.
- `manage_appointments`: List, inspect, confirm, or cancel a customer's appointments.

Prompt arguments provide user/context input; tool handlers and application validation remain responsible for enforcing valid operations.

## 5. Tool Catalog

### 3.1. `get_organization_info`
- **Description**: Retrieves organization details, working hours, address, and timezone.
- **Parameters**:
  - `slug` (string, optional): Organization slug (e.g., `'kadikoy-poliklinigi'`).
  - `organization_id` (string, optional): Organization GUID.

### 3.2. `get_available_dates`
- **Description**: Returns calendar dates with open appointment slots.
- **Parameters**:
  - `organization_id` (string, required): Organization GUID.
  - `start_date` (string, optional): ISO-8601 start date.
  - `end_date` (string, optional): ISO-8601 end date.

### 3.3. `get_available_slots`
- **Description**: Retrieves open appointment slots formatted with local timezone times and IDs.
- **Parameters**:
  - `organization_id` (string, required): Organization GUID.
  - `start_date` (string, optional): ISO-8601 start date.
  - `end_date` (string, optional): ISO-8601 end date.

### 3.4. `create_appointment`
- **Description**: Executes appointment booking under Redis RedLock and PostgreSQL partial unique index protection.
- **Parameters**:
  - `organization_id` (string, required): Organization GUID.
  - `customer_id` (string, required): Customer GUID.
  - `slot_id` (string, required): Slot GUID.
  - `idempotency_key` (string, required): Unique idempotency string.
  - `note` (string, optional): Customer description.

### 3.5. `get_appointment`
- **Description**: Retrieves full appointment metadata and status.
- **Parameters**:
  - `organization_id` (string, required): Organization GUID.
  - `appointment_id` (string, required): Appointment GUID.

### 3.6. `get_customer_appointments`
- **Description**: Lists all active and historical appointments for a customer.
- **Parameters**:
  - `organization_id` (string, required): Organization GUID.
  - `customer_id` (string, required): Customer GUID.

### 3.7. `cancel_appointment`
- **Description**: Cancels appointment, releases slot, and automatically reassigns slot to next waiting customer.
- **Parameters**:
  - `organization_id` (string, required): Organization GUID.
  - `appointment_id` (string, required): Appointment GUID.
  - `customer_id` (string, required): Customer GUID.
  - `reason` (string, required): Cancellation explanation.
  - `idempotency_key` (string, required): Unique idempotency string.

### 3.8. `get_queue_status`
- **Description**: Checks current queue position, estimated wait time, and reservation expiry.
- **Parameters**:
  - `organization_id` (string, required): Organization GUID.
  - `slot_id` (string, required): Slot GUID.
  - `customer_id` (string, optional): Customer GUID.
  - `request_id` (string, optional): Queue request ID.

### 3.9. `cancel_queue_request`
- **Description**: Removes customer from waiting queue and cleans up reservation.
- **Parameters**:
  - `organization_id` (string, required): Organization GUID.
  - `slot_id` (string, required): Slot GUID.
  - `customer_id` (string, optional): Customer GUID.
  - `request_id` (string, optional): Queue request ID.
