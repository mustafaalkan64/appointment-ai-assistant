# Web API Documentation (Minimal APIs)

## 1. Overview
All platform endpoints are built with **ASP.NET Core Minimal APIs**, modularized via `IEndpoint` vertical slices and documented via OpenAPI (Swagger).

---

## 2. Authentication & Tenancy
- **Header**: `X-Tenant-Id: <OrganizationGuid>` or URL slug parameter.
- **Authorization**: Bearer JWT / API Key.
- SuperAdmin bypass scope available for internal workers.

---

## 3. Endpoint Catalog

### 3.1. Organizations
| Method | Endpoint | Description | Status Codes |
|---|---|---|---|
| `POST` | `/api/organizations` | Register a new tenant organization with admin user | `201 Created`, `400 Bad Request` |
| `GET` | `/api/organizations/{id:guid}` | Get organization details by GUID | `200 OK`, `404 Not Found` |
| `GET` | `/api/organizations/by-slug/{slug}` | Get organization details by URL slug | `200 OK`, `404 Not Found` |

### 3.2. Availability & Slot Generation
| Method | Endpoint | Description | Status Codes |
|---|---|---|---|
| `POST` | `/api/organizations/{organizationId}/availability/weekly` | Define weekly working hours schedule | `200 OK`, `400 Bad Request` |
| `POST` | `/api/organizations/{organizationId}/slots/generate` | Generate UTC slots from local schedule rules | `200 OK`, `400 Bad Request` |
| `GET` | `/api/organizations/{organizationId}/slots/available` | Query available open slots for date range | `200 OK` |
| `PATCH` | `/api/organizations/{organizationId}/slots/{slotId}/toggle-status` | Block, unblock, or toggle slot status | `200 OK`, `409 Conflict` |

### 3.3. Customers
| Method | Endpoint | Description | Status Codes |
|---|---|---|---|
| `POST` | `/api/customers` | Register customer and link to organization | `201 Created`, `400 Bad Request` |

### 3.4. Appointments & Concurrency
| Method | Endpoint | Description | Status Codes |
|---|---|---|---|
| `POST` | `/api/appointments` | Book an appointment with 6-layer defense & RedLock | `201 Created`, `202 Accepted` (Queue), `409 Conflict` |
| `GET` | `/api/appointments/{id:guid}` | Retrieve appointment by ID | `200 OK`, `404 Not Found` |
| `GET` | `/api/customers/{customerId}/appointments` | List all appointments for customer | `200 OK` |
| `POST` | `/api/appointments/{id:guid}/cancel` | Cancel appointment, release slot & advance queue | `200 OK`, `400 Bad Request`, `409 Conflict` |

### 3.5. Waiting Queue
| Method | Endpoint | Description | Status Codes |
|---|---|---|---|
| `GET` | `/api/queue/status` | Get customer queue position & lease countdown | `200 OK`, `400 Bad Request` |
| `POST` | `/api/queue/cancel` | Cancel waiting queue entry | `200 OK`, `400 Bad Request` |

### 3.6. Health & Diagnostics
| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/health` | Health check for PostgreSQL, Redis, and workers |
