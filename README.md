# Microservices & BFF Platform

A .NET 8 reference platform demonstrating independently hosted services, JWT authentication, a YARP API Gateway, and a Web Backend-for-Frontend (BFF) that aggregates downstream APIs.

> Status: initial runnable architecture. Catalog and Orders use independent SQLite databases. Broader automated API integration and contract coverage, advanced resilience policies, observability, and deployment automation are still required before production use.

## Architecture

Clients -> API Gateway (YARP) -> Identity.Api / Catalog.Api / Orders.Api / Web.Bff

- Identity.Api registers users, stores password hashes in a dedicated SQLite database, and issues short-lived JWT access tokens.
- Catalog.Api exposes authenticated product queries backed by its own SQLite database.
- Orders.Api exposes authenticated, durable order operations scoped to the token subject using its own SQLite database.
- Web.Bff aggregates Catalog and Orders concurrently and forwards the caller's bearer token.
- ApiGateway is the single external entry point and routes requests with YARP.
- Services own their API contracts. No business-domain entities or database schema are shared.

## Requirements

- .NET SDK 8.0.408 or a compatible .NET 8 patch.
- No container runtime is required for this local development setup.

## Configuration

All five processes must use the same signing key. Generate a random secret of at least 32 bytes and keep it out of source control. PowerShell example:

    $key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $env:Jwt__SigningKey = $key

Set Jwt__SigningKey in each terminal. Never commit signing keys. Use HTTPS and a managed secret store in deployed environments.

## Run locally

Start each process in a separate terminal from the repository root:

| Process | Command | Port |
| --- | --- | ---: |
| Identity | dotnet run --project src/Identity.Api --urls http://localhost:5100 | 5100 |
| Catalog | dotnet run --project src/Catalog.Api --urls http://localhost:5101 | 5101 |
| Orders | dotnet run --project src/Orders.Api --urls http://localhost:5102 | 5102 |
| Web BFF | dotnet run --project src/Web.Bff --urls http://localhost:5103 | 5103 |
| Gateway | dotnet run --project src/ApiGateway --urls http://localhost:5080 | 5080 |

Identity creates identity.db, Catalog creates catalog.db, and Orders creates orders.db on first startup. Each service owns its own database file. Do not expose service ports publicly; only the Gateway should be reachable by clients.

## API walkthrough

Register: POST http://localhost:5080/api/auth/register with JSON { "email": "developer@example.com", "password": "StrongPassword123" }

Log in: POST http://localhost:5080/api/auth/login with the same JSON shape. Use the returned accessToken as a Bearer token.

Authenticated examples:

- GET http://localhost:5080/api/catalog/products
- GET http://localhost:5080/api/web/dashboard
- POST http://localhost:5080/api/orders with JSON { "items": [{ "productId": "10000000-0000-0000-0000-000000000001", "quantity": 2 }] }

## Security implemented

- ASP.NET Core PasswordHasher; plaintext passwords are never stored.
- Registration and login fixed-window rate limiting.
- JWT issuer, audience, signature, and lifetime validation at the Gateway and protected services.
- Orders are scoped to the authenticated user.
- Public Identity endpoints are limited to registration and login.

## Known limitations and next milestones

1. Replace in-memory Catalog and Orders storage with independent durable databases and migrations.
2. Expand automated unit, API integration, and consumer/provider contract tests. Initial SQLite store tests cover identity persistence, catalog filtering, and customer-scoped orders.
3. Add downstream timeouts, retries with jitter where safe, circuit breakers, and explicit cancellation handling.
4. Add structured logs, trace correlation, metrics, and dependency-aware readiness checks.
5. Add OpenAPI documentation, CI test gates, dependency/security scanning, and deployment workflows.
6. Add production hardening: refresh-token lifecycle, account lockout, key rotation, HTTPS enforcement, secret management, and network isolation.

The current implementation is a learning/portfolio foundation, not a production-ready deployment.

## Build

    dotnet restore MicroservicesBffPlatform.sln
    dotnet build MicroservicesBffPlatform.sln --configuration Release --no-restore

## References

- https://learn.microsoft.com/dotnet/architecture/microservices/architect-microservice-container-applications/direct-client-to-microservice-communication-versus-the-api-gateway-pattern
- https://learn.microsoft.com/azure/architecture/guide/architecture-styles/microservices