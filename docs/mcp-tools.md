# Model Context Protocol (MCP) Server Specification

## 1. Overview
The platform exposes an **MCP Server** (`AppointmentBooking.McpServer`) compliant with the standard **Model Context Protocol (JSON-RPC 2.0)** over HTTP POST (`/mcp`) and Server-Sent Events (`/mcp/sse`).

AI Agents (such as Claude, ChatGPT, or custom LLM agents) connect directly to this server to discover tools, inspect schemas, and execute actions that delegate directly to Application CQRS handlers.

---

## 2. Protocol Endpoints
- `POST /mcp`: Standard JSON-RPC 2.0 transport.
- `GET /mcp/sse`: Server-Sent Events stream for streamable persistent sessions.
- `GET /health`: Health and protocol capabilities verification.

---

## 3. Tool Catalog

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
