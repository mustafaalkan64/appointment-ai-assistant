# Deployment & Infrastructure Guide

## 1. Overview
The platform is packaged for deployment using Docker / Podman containers or Kubernetes.

---

## 2. Infrastructure Services

### 2.1. PostgreSQL 16
- **Container**: `turknet-eventbus-postgres` (or `docker-compose.yml`)
- **Port**: `5432`
- **Database**: `appointment_db`
- **User**: `postgres`
- **Password**: `postgrespassword`
- **Migrations**: Automatically applied via `dotnet ef database update`

### 2.2. Redis 7
- **Container**: `appointment-redis`
- **Port**: `6379`
- **Purpose**: RedLock distributed locking and high-throughput concurrency coordination

### 2.3. RabbitMQ
- **Container**: `appointment-rabbitmq`
- **Port**: `5672` (AMQP), `15672` (Management UI)
- **User**: `appointment_guest` / `appointment_guest_secret`
- **Purpose**: Asynchronous transactional outbox event bus

---

## 3. Docker Compose Orchestration
Run the complete system with:
```bash
docker compose up -d --build
```
Services included:
- `postgres` (port 5432)
- `redis` (port 6379)
- `rabbitmq` (port 5672, 15672)
- `api` (port 5000)
- `mcpserver` (port 5001)
- `chatapi` (port 5002)

---

## 4. Health Checks
All web services expose `/health` endpoints compatible with Kubernetes liveness and readiness probes.
