# TC-815 – Security Response Headers and Cookie Hardening

## Objective
Verify the HTTP response headers and cookie settings added by the v0.9 hardening: the WebApp sets a Content-Security-Policy and related headers, forces the Identity and antiforgery cookies to `HttpOnly` + `Secure`, and removes the Kestrel `Server` header; the Integration Hub API sets the headers that apply to an API and removes its `Server` header too.

## Preconditions
- No host is started. The helper classes `SecurityHeaders` (WebApp) and `ApiSecurityHeaders` (Integration Hub API) are applied to a `DefaultHttpContext`, and the cookie and Kestrel setup methods to freshly created options objects.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | WebApp `SecurityHeaders.Apply` | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Cross-Origin-Opener-Policy: same-origin` |
| 2 | Read the `Permissions-Policy` header | Denies camera, microphone, geolocation, payment and usb |
| 3 | Read the `Content-Security-Policy` header | Equals the shared `SecurityHeaders.ContentSecurityPolicy` constant |
| 4 | Inspect the policy text | `default-src 'self'`, `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'` |
| 5 | Inspect `style-src` and `font-src` | Allow `https://cdn.jsdelivr.net` (Bootstrap Icons); `script-src` does not allow it |
| 6 | Configure the Identity application cookie, then the antiforgery cookie | `HttpOnly` is true and `SecurePolicy` is `Always` for both |
| 7 | WebApp `ConfigureKestrel` | `AddServerHeader` is false |
| 8 | `ApiSecurityHeaders.Apply` | `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Cross-Origin-Opener-Policy: same-origin` |
| 9 | API `ConfigureKestrel` | `AddServerHeader` is false |

## Assertions
- The CSP blocks framing and plugins and limits everything else to the site itself, apart from the Bootstrap Icons CDN for styles and fonts only.
- The Identity and antiforgery cookies are never sent over plain HTTP, which is why behind a TLS-terminating proxy the app needs forwarded-header handling (see the v0.9 release notes).

## Related Code
- Test classes: `AuditTrailReview.UnitTest.Security.SecurityHeadersTests`, `AuditTrailReview.UnitTest.Security.ApiSecurityHeadersTests`
- Methods: `SecurityHeadersTests`: `Apply_WhenCalled_SetsContentTypeOptionsToNoSniff`, `Apply_WhenCalled_SetsFrameOptionsToDeny`, `Apply_WhenCalled_SetsReferrerPolicy`, `Apply_WhenCalled_SetsPermissionsPolicyDenyingSensorsAndPayment`, `Apply_WhenCalled_SetsCrossOriginOpenerPolicyToSameOrigin`, `Apply_WhenCalled_SetsContentSecurityPolicyToTheSharedConstant`, `ContentSecurityPolicy_WhenInspected_BlocksFramingAndPluginsAndRestrictsDefaultToSelf`, `ContentSecurityPolicy_WhenInspected_AllowsTheBootstrapIconsCdnOnlyForStylesAndFonts`, `ConfigureApplicationCookie_WhenCalled_SetsHttpOnly`, `ConfigureApplicationCookie_WhenCalled_ForcesSecurePolicyToAlways`, `ConfigureAntiforgeryCookie_WhenCalled_SetsHttpOnly`, `ConfigureAntiforgeryCookie_WhenCalled_ForcesSecurePolicyToAlways`, `ConfigureKestrel_WhenCalled_DisablesTheServerHeader`. `ApiSecurityHeadersTests`: `Apply_WhenCalled_SetsContentTypeOptionsToNoSniff`, `Apply_WhenCalled_SetsReferrerPolicy`, `Apply_WhenCalled_SetsCrossOriginOpenerPolicyToSameOrigin`, `ConfigureKestrel_WhenCalled_DisablesTheServerHeader`
- Production code: `AuditTrailReview.WebApp.Security.SecurityHeaders`, `AuditTrailReview.IntegrationHub.API.Security.ApiSecurityHeaders`
- Related: TC-811 (session cookie lifetime), TC-816 (authorization by default)

## Notes
- Not covered by an automated test: that the middleware is registered in each `Program.cs` and that the headers reach a real response. Check with a browser's developer tools or `curl -I` after a deployment. The MCP server's headers are not covered.

## Status
Active
