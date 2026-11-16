# TC-UI-005 – All Navbar Links Load Without an HTTP Error

## Objective
Verify that every item in the navbar — including every dropdown's items (Dashboard, Systems, Scans, Config panel, Admin panel, Logs, Help) — loads successfully for the seeded Admin account, discovering the links from the rendered DOM rather than hardcoding each one, so a menu item added later is covered automatically.

This test **already exists** as `MenuLinkCrawlerTests.AllMenuLinksLoadSuccessfully` in `AuditTrailReview.UIPlaywrightTest`; this document formalizes it as a traceable test case (it previously had no corresponding TC doc). It covers the same surface as the Selenium `TC-603-MenuNavigation`, but by crawling the DOM instead of hardcoding each link.

## Preconditions
- `AuditTrailReview.WebApp` is running and reachable at `TestConfig.BaseUrl`.
- `TestConfig.LoginEmail` / `TestConfig.LoginPassword` are set to a valid account (defaults to the seeded Admin account); the test reports `Inconclusive` rather than failing if they are blank.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Log in via `/Identity/Account/Login` | Redirected to `/Dashboard` |
| 2 | Enumerate every `header nav ul.navbar-nav > li.nav-item` | At least one nav item is found |
| 3 | For each item that is a plain link, click its `a.nav-link[href]` | Navigation completes |
| 4 | For each item that is a dropdown toggle (`a.dropdown-toggle`), open it and click each `div.dropdown-menu a.dropdown-item[href]` in turn | Navigation completes for each dropdown item |
| 5 | After each visit, assert the resulting navigation response is OK, then return to `/Dashboard/Index` before visiting the next link | No link returns a non-OK HTTP status |

## Assertions
- At least one navigation link is discovered and visited (guards against a markup change silently breaking the crawler itself).
- No visited link's navigation response has a non-OK HTTP status.

## Related Code
- Test class: `MyMSTestPlaywright.MenuLinkCrawlerTests` (`AuditTrailReview.UIPlaywrightTest/MenuLinkCrawlerTests.cs`)
- Method: `AllMenuLinksLoadSuccessfully`
- Config: `TestConfig.BaseUrl`, `LoginEmail`, `LoginPassword`, `Headless`

## Page Elements Used
| Element | Selector |
|---------|----------|
| Nav items | `header nav ul.navbar-nav > li.nav-item` |
| Dropdown toggle | `a.dropdown-toggle` |
| Dropdown item | `div.dropdown-menu a.dropdown-item[href]` |
| Plain nav link | `a.nav-link[href]` |

## Notes
- The brand logo link is intentionally excluded — it always points at Dashboard, already covered by the "Dashboard" nav item.
- Driven with raw Playwright (`IPlaywright`/`IBrowser`/`IPage`) rather than `Microsoft.Playwright.MSTest`'s `PageTest`/`BrowserTest` base classes, which silently register zero tests against this project's MSTest version — see the remarks in `MenuLinkCrawlerTests.cs`.

## Status
Active
