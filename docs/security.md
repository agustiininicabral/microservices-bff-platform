# Security notes

This document describes the current implementation and deployment requirements. It is not a production security certification.

## Implemented controls

- Passwords are stored as ASP.NET Core PasswordHasher hashes, never as plaintext.
- Registration validates email syntax and requires at least 12 password characters, including letters and digits.
- Registration and login use a fixed-window limiter: 10 requests per minute per remote IP observed by Identity. Rejected requests return HTTP 429.
- JWT validation checks issuer, audience, signature, and lifetime in the Gateway and protected services.
- Access tokens expire after 30 minutes.
- Catalog, Orders, and Web BFF endpoints require authentication.
- Order list and detail queries are scoped to the authenticated customer.
- BFF and Gateway readiness checks use bounded downstream HTTP timeouts.
- SQL statements use parameters rather than string interpolation.

## Required deployment controls

1. Use HTTPS end-to-end or terminate TLS at a trusted ingress and protect internal service traffic.
2. Keep Identity, Catalog, Orders, and BFF ports on a private network. Expose only the Gateway.
3. Inject the same high-entropy `Jwt__SigningKey` into all five processes using a secret manager. Never commit secrets or put them in appsettings files.
4. When running behind a reverse proxy, configure ASP.NET Core forwarded headers to trust only known proxy addresses. Do not trust arbitrary client-supplied `X-Forwarded-For` values. Until configured, the limiter uses the remote IP seen by Identity, which may be the Gateway address.
5. Use a durable managed database and backups for multi-instance deployments. SQLite files are intended for this portfolio/local setup.
6. Restrict and monitor access to logs and database files.

## Known gaps

- No refresh tokens, token revocation, or coordinated signing-key rotation.
- No account lockout or multi-factor authentication.
- Rate-limit state is in-process and is not shared across multiple Identity instances.
- The current password policy is a baseline, not a complete enterprise identity policy.
- No automated secret scanning or dependency vulnerability gate is currently configured.
- No production ingress, network policy, managed database, or cloud deployment is included.

Do not interpret the local HTTP examples as a recommendation to expose unencrypted HTTP in production.
