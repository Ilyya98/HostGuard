# Changelog

## 0.1.0 (unreleased)

- `BoundedReader` / `BoundedWriter` with message, string, count and depth limits; strict UTF-8; finite floats.
- `MessageRegistry` (sealed whitelist, framed dispatch, allow rule before apply).
- `RateLimiter` (token bucket per sender) and `HostGate` (strikes and disconnect).
- `SafeText`, `SafePath`, `Handshake` / `JoinRequest` / `ContentHash`.
- Tests, including a seeded fuzz test.
