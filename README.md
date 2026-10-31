# Microservices & BFF Platform

A .NET 8 reference platform demonstrating independently hosted services, JWT authentication, a YARP API Gateway, and a Web Backend-for-Frontend (BFF) that aggregates downstream APIs.

> Status: portfolio-ready vertical slice with independently persisted services, authenticated APIs, gateway routing, BFF aggregation, automated tests, and an end-to-end smoke test. It is not a production deployment; remaining hardening is listed below.

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
- The smoke test requires Bash, curl, jq, OpenSSL, and free ports 5080–5103.

## Configuration

All five processes must use the same signing key. Generate a random secret of at least 32 bytes and keep it out of source control. Generate the key once in PowerShell:

    $key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    Write-Output $key

Copy that exact value into Jwt__SigningKey in each of the five service terminals. Do not generate a different key per process. Never commit signing keys. Use HTTPS and a managed secret store in deployed environments.

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
- Registration and login fixed-window rate limiting: 10 requests per minute per remote IP observed by Identity. Rejections return HTTP 429; behind a reverse proxy, configure trusted forwarded headers as described in [Security Notes](docs/security.md).
- JWT issuer, audience, signature, and lifetime validation at the Gateway and protected services.
- Orders are scoped to the authenticated user.
- BFF and Gateway readiness endpoints verify their downstream dependencies with bounded timeouts.
- Public Identity endpoints are limited to registration and login.

## Known limitations and next milestones

1. Add versioned schema migrations, backup/restore procedures, and transactional workflows where required.
2. Expand automated unit, API integration, and consumer/provider contract tests. Current coverage includes identity registration/login integration, SQLite persistence and ownership tests, and a multi-process end-to-end smoke test through the Gateway.
3. Add retry policies with jitter where safe, circuit breakers, bulkheads, and more granular downstream failure handling. BFF and Gateway calls already have bounded timeouts.
4. Add structured logs, distributed trace correlation, metrics, and dashboards. Gateway and BFF readiness already probe their downstream dependencies.
5. Add richer contract tests, dependency vulnerability enforcement, and environment-specific deployment automation. The CI workflow builds, tests, smoke-tests, and publishes five service artifacts. The tag-based Release workflow packages the same services but does not deploy to a cloud environment.
6. Add production hardening: refresh-token lifecycle, account lockout, key rotation, HTTPS enforcement, secret management, and network isolation.

The current implementation is a learning/portfolio foundation, not a production-ready deployment. See [Security Notes](docs/security.md) for the controls already present and the gaps that remain.

## Build

    dotnet restore MicroservicesBffPlatform.sln
    dotnet build MicroservicesBffPlatform.sln --configuration Release --no-restore
    dotnet test MicroservicesBffPlatform.sln --configuration Release --no-build
    bash scripts/smoke-test.sh

## References

- https://learn.microsoft.com/dotnet/architecture/microservices/architect-microservice-container-applications/direct-client-to-microservice-communication-versus-the-api-gateway-pattern
- https://learn.microsoft.com/azure/architecture/guide/architecture-styles/microservices