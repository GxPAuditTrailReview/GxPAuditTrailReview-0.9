# TC-807 – Application Services: MCP Server API Key Authentication

## Objective
Verify `AuditTrailReview.McpServer.Security.McpApiKeyMiddleware`, the API-key gate in front of the MCP server's `/mcp` endpoint (ADR-0010). Without it, anyone who can reach the endpoint could read every system and scan. The gate must fail closed, accept the key in the configured header or as a Bearer token, and leave `/health` open for container-platform probes.

**2026-11-05: moved from a static `McpAuth:Keys` config list to DB-backed, encrypted keys.** Keys now live in `dbo.McpApiKeyCredential` (`AuditTrailReview.Model.McpApiKeyCredential`), encrypted via `ApiKeyProtector` and managed from the WebApp's AdminPanel -> Settings -> MCP tab (see TC-705), replacing the old `appsettings.json` list and its shipped placeholder (`CHANGE_ME_MCP_API_KEY`, now retired along with the config-based "is it the placeholder" check). The decrypt/compare logic itself is `AuditTrailReview.Engine.Security.ApiKeyCredentialValidator`, shared with `IntegrationHub.API`'s `ApiKeyAuthenticationHandler` (TC-808) -- but the two services' *keys* are a deliberately disjoint pool: a key generated on the MCP tab can never authenticate `IntegrationHub.API`, and vice versa, because they're stored in separate tables (`McpApiKeyCredential` vs. `ApiKeyCredential`).

## Preconditions
- No web server involved: the middleware is invoked directly (`InvokeAsync(httpContext, dbContext)`) with a `DefaultHttpContext` and an EF Core InMemory `ApplicationDbContext`. `ApplicationDbContext` is scoped, so (unlike `IConfiguration`) it cannot be a constructor parameter on this singleton middleware -- it's resolved as an `InvokeAsync` method parameter instead, which `UseMiddleware<T>` fills from the request's own DI scope; the tests emulate that by passing it to `InvokeAsync` directly.
- Each test builds its own `IConfiguration` via `ConfigurationBuilder().AddInMemoryCollection(...)` (`Security:EncryptionKey`, a valid base64 32-byte key) and seeds `dbo.McpApiKeyCredential` rows directly via the context, encrypting each raw key with `ApiKeyProtector.Encrypt` the same way the AdminPanel "MCP" tab's generate handler does (TC-705).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Request `/mcp` with no key, one active key seeded | `401`; the next middleware is not called |
| 2 | Request `/mcp` with a key that doesn't match the one active key | `401`; next not called |
| 3 | Request `/mcp` with the active key's raw value in `X-Api-Key` | Next is called (request passes the gate) |
| 4 | Request `/mcp` with the active key's raw value as `Authorization: Bearer <key>` | Next is called |
| 5 | Seed two active keys, send the second's raw value | Next is called (any active key is accepted, so keys can be rotated by generating a new one before revoking the old) |
| 6 | Seed a key, then mark it revoked (`RevokedUtc` set), send its raw value | `401` -- a revoked key is rejected even though it still decrypts and matches |
| 7 | Request `/mcp` with no rows in `dbo.McpApiKeyCredential` at all, any key | `401` (fails closed) |
| 8 | Seed an active `ApiKeyCredential` (IntegrationHub.API's own table, not `McpApiKeyCredential`) with a known raw key, then request `/mcp` with that same raw value | `401` -- the two key pools are disjoint; an API-tab key never authenticates the MCP server |
| 9 | Request `/health` with no key | Next is called (liveness probe stays unauthenticated) |
| 10 | Seed a user and a key whose `UserId` is that user (plus another unrelated key), send the user key | Next-bound `HttpContext.User` has name = the user's username, `NameIdentifier` = the user id, and the `atr:mcp_key_id` claim = the matched key's id (the right key is identified among several) |
| 11 | Send a shared key (`UserId` null) | `HttpContext.User` name = the key's `Name`; no `NameIdentifier` claim |

## Assertions
- `/mcp` is never reachable without a matching, active (non-revoked) key in `dbo.McpApiKeyCredential`.
- No rows, a wrong key, or a revoked key all reject every request instead of allowing it.
- The key is accepted from the configured header or a Bearer token.
- Paths outside `/mcp` are not gated.
- A key from `dbo.ApiKeyCredential` (IntegrationHub.API's table) never authenticates here, confirming the two pools are structurally separate, not just conventionally.
- An authenticated request is attributed to the person the key was issued to (`McpApiKeyCredential.UserId`), or to the key name for a shared key.

## Related Code
- Test class: `AuditTrailReview.UnitTest.McpServer.McpApiKeyMiddlewareTests`
- Methods: `Invoke_McpPathWithoutKey_Returns401AndSkipsNext`, `Invoke_McpPathWithWrongKey_Returns401`, `Invoke_McpPathWithValidHeaderKey_CallsNext`, `Invoke_McpPathWithValidBearerToken_CallsNext`, `Invoke_MultipleActiveKeysConfigured_AcceptsAnyOfThem`, `Invoke_RevokedKey_Returns401`, `Invoke_NoActiveKeysInDatabase_RejectsEvenWithAKey`, `Invoke_ApiKeyCredentialKeyNotMcpApiKeyCredential_IsRejected`, `Invoke_KeyIssuedToUser_SetsPrincipalWithUserNameAndKeyId`, `Invoke_SharedKeyWithoutUser_AttributesToKeyName`, `Invoke_HealthPathWithoutKey_CallsNext`
- Requirement: URS-035 / FR-043 (see Validation Package)
- Production code: `AuditTrailReview.McpServer.Security.McpApiKeyMiddleware`, wired in `AuditTrailReview.McpServer/Program.cs`; `AuditTrailReview.Model.McpApiKeyCredential`; `AuditTrailReview.Engine.Security.ApiKeyCredentialValidator` (TC-808, shared decrypt/compare logic)

## Notes
- The per-IP rate limiter (`McpRateLimiting`, 429 when exceeded) and the `/health` endpoint itself are configured in `Program.cs` and are not unit tested; they were checked manually against a running instance.
- Per-user attribution (2026-11-07): a key may be issued to a WebApp user (`UserId`, ATR 0.39); the tests check the resulting `ClaimsPrincipal`. The per-request log line/scope is not asserted, there is no persisted MCP access log, and the AdminPanel "Issued to user" dropdown (including the unknown-user rejection) has no unit test yet (TC-705).
- There is no automated end-to-end test of a real HTTP request through the MCP server's middleware pipeline with a key generated by the WebApp's MCP tab against a real (non-InMemory) database -- that cutover should be verified manually once per environment.
- The previously shipped placeholder key (`CHANGE_ME_MCP_API_KEY`) is intentionally not migrated into `dbo.McpApiKeyCredential` -- any caller relying on it will get `401` after this change until an admin generates and distributes a real key from the MCP tab.

## Status
Active

## Change history
- 2026-11-07: Added per-user key attribution (`McpApiKeyCredential.UserId`, `HttpContext.User` claims, per-request log line); steps 10-11 and two test methods.
- 2026-11-05: Moved from a static `McpAuth:Keys` config list (with a shipped placeholder key) to DB-backed, encrypted keys in `dbo.McpApiKeyCredential`, managed from a new AdminPanel -> Settings -> MCP tab (TC-705). Replaced the old config-based test steps (multiple configured keys, placeholder rejection) with DB-backed equivalents (multiple active rows, revoked-row rejection, cross-table isolation from `ApiKeyCredential`).
