# Path to v1.0 - UX and UI

| Field | Value |
|---|---|
| Product | GxPAuditTrailReview (WebApp) |
| Baseline | v0.9 (in development), `master` @ `4f04a52` |
| Prepared | 2026-11-05 |
| Updated | 2026-11-06: items 1, 3 and 5 resolved, item 2 partly resolved; sign-out warning added under Cybersecurity item 4 |
| Status | Draft, for prioritisation |
| Companion | [Path to v1.0.md](Path%20to%20v1.0.md) (functional, compliance and operational items) |

## Purpose

This is the user-experience and user-interface list for v1.0. It covers what reviewers, QA and admins see and do in the WebApp. The companion document covers back-end, compliance and operational work. Where an item appears in both, this document covers only the screen side.

The review was done from the Razor Pages markup, page models, `site.css` and the JavaScript in `wwwroot/js`. The app couldn't be run for this review (LocalDB failed to start on the review machine), so no item has been checked in a browser. Items marked **(verified)** were confirmed in the source. Line references are to `AuditTrailReview.WebApp`.

## Priorities

- **P1 - Required for v1.0:** a security exposure, a GxP expectation that the UI doesn't meet, or something that blocks the core review-and-sign task.
- **P2 - Should be in v1.0:** a clear usability gap in daily work, an accessibility failure, or inconsistency that makes the product look unfinished.
- **P3 - Can follow v1.0:** polish and convenience.

---

## Summary

| # | Item | Area | Priority |
|---|---|---|---|
| 1 | Remove self-registration and unused Identity scaffold pages (**resolved**) | Security / Account | P1 |
| 2 | Confirmation, re-authentication and meaning in the sign-off dialog (**partly resolved**) | Review & sign | P1 |
| 3 | Restrict the Logs menu to authorised roles (**resolved**) | Navigation / Access | P1 |
| 4 | Show the time zone on every timestamp | GxP display | P1 |
| 5 | Replace the template Error and Privacy pages (**resolved**) | Error handling | P1 |
| 6 | Bulk review and review progress on Scan Details | Review workflow | P2 |
| 7 | Filters on the Scans list and on the findings tabs | Findings triage | P2 |
| 8 | A clear "Upload" entry point | Navigation | P2 |
| 9 | Accessibility (WCAG 2.1 AA) pass | Accessibility | P2 |
| 10 | Page titles, active menu item, consistent breadcrumbs | Navigation | P2 |
| 11 | One message and alert pattern | Consistency | P2 |
| 12 | Branding, naming and visual consistency | Consistency | P2 |
| 13 | Serve Bootstrap Icons locally | Deployment / Offline | P2 |
| 14 | A dashboard focused on the reviewer's work | Dashboard | P2 |
| 15 | An upload-jobs page for users and admins | Feedback | P2 |
| 16 | Split the Application Settings page | Admin UX | P3 |
| 17 | Unsaved-change warnings and inline validation | Forms | P3 |
| 18 | Responsive layout and the navbar on small screens | Layout | P3 |
| 19 | Keyboard shortcuts and "next finding" navigation | Productivity | P3 |
| 20 | Dark mode and print stylesheet | Polish | P3 |
| 21 | Markup clean-up | Code hygiene | P3 |

---

## P1 - Required for v1.0

### 1. Remove self-registration and unused Identity scaffold pages (verified in code)

> **Resolved 2026-11-05.** `Security/DisabledIdentityPages.cs` makes the pages listed below answer 404 and requires sign-in for `/Account/Manage`. The Account menu now shows only Profile and Password. Covered by TC-791 and checked at runtime on the SQLite configuration (anonymous Register GET and POST return 404, no account is created). The remaining follow-ups (checking existing databases for accounts created through Register, auditing Profile name changes, and a Playwright test for the 404) are tracked as P1 item 6 in the companion document.

`Areas/Identity/Pages/Account/Register.cshtml.cs` is a scaffolded page with no `[Authorize]` attribute. The `AuthorizeFolder("/")` convention in `Program.cs` (line 117) only covers `Pages/`, not `Areas/Identity`. The "Register" link in `_LoginPartial.cshtml` is commented out, but the page itself still works. With `RequireConfirmedAccount = false` (`Program.cs` line 989), a visitor who opens `/Identity/Account/Register` gets an account and is signed in straight away. That account has no role, but every page that only checks `[Authorize]` is open to it: Systems, Scans (including review and sign-off) and Logs.

Other scaffold pages under `Account/Manage` are also reachable and linked from the "Account" menu (`_ManageNav.cshtml`):
- **Delete personal data**: lets users delete their own account. This bypasses the v0.9 rule that an account with uploads can't be deleted, and it breaks the attribution of signatures and audit entries.
- **Two-factor authentication, external logins, download personal data**: MFA is out of scope (ADR-0007) and no external login providers are configured. The pages only add confusion.
- **Email**: lets users change their own e-mail address with no admin involvement or audit entry.

To do: remove or disable `Register`, `RegisterConfirmation`, `ExternalLogin`, `DeletePersonalData`, the 2FA and recovery-code pages, and `Email`, for example with an `AuthorizeAreaFolder("Identity", "/Account/Manage")` convention plus explicit `NotFound()` results. Reduce `_ManageNav` to "Profile" and "Change password". Add a UI test that `/Identity/Account/Register` returns 404. This is a security fix and should go in before anything else on this list.

### 2. Confirmation, re-authentication and meaning in the sign-off dialog (verified)

> **Partly resolved 2026-11-06.** Both buttons now open a modal (with icons, `57e9a75`) that states what is being signed, shows the signed-in login (read-only) and asks for the password; a wrong password re-opens the modal with an error. See item 1 in `Path to v1.0.md`. Not done yet: the findings-by-disposition summary, the "meaning of signature" choice, the comment, the Part 11 attestation text, and a required reason for Reopen.

On `Scan/Details.cshtml` (lines 26-47), **Close review & sign** and **Reopen review** are plain form posts. A single click signs or reopens the scan, with no confirmation and no prompt. The signature reason is always the fixed text "Reviewed & Signed".

To do: a modal that shows a summary of what is being signed (scan, system, number of findings by disposition), asks for the password, offers a required "meaning of signature" choice and an optional comment, and states the Part 11 attestation in plain words. Use the same modal for Reopen, with a required reason. This is the screen side of item 1 in the companion document.

### 3. Restrict the Logs menu to authorised roles (verified)

> **Resolved 2026-11-05.** `Log/UserLogin` and `Log/AuditTrail` now require Admin or Super User, and the Logs menu is shown only to those roles. A plain User is sent to the Access denied page. Covered by TC-810, checked at runtime with one account per role, and the User Manual, FT-15 and UAT-10 are updated. The per-record "Audit trail" tabs are unchanged.

`Log/UserLogin` and `Log/AuditTrail` only require `[Authorize]`, and the **Logs** menu is shown to every role. A plain "User" can therefore see every colleague's login history (including failed attempts and IP addresses) and the full system audit trail. Decide which roles may see them (probably Admin and QA/Super User) and both hide the menu and gate the pages. For other users, the per-record "📜 Audit trail" tabs already show the history that matters to them.

### 4. Show the time zone on every timestamp (verified)

`TagHelpers/UtcDateTimeTagHelper.cs` formats every date in the configured display time zone, but the output has no zone indicator. A reviewer looking at `05-Nov-26 21:32` can't tell whether it is UTC, server time or local time, and an exported Excel file carries the same ambiguity. For audit-trail review this matters: rules C2 and C3 are about time.

To do: add the zone abbreviation or offset to the rendered value (for example `05-Nov-26 21:32 CET`), put the UTC value in a `title` tooltip, and name the zone in Excel column headers (`Date / Time (CET)`).

### 5. Replace the template Error and Privacy pages (verified)

> **Resolved 2026-11-05.** `Pages/Error.cshtml` is now a friendly page with a status-specific heading and message, a *Reference* code (`TraceIdentifier`, also written as `CorrelationId` on every log event of that request by `RequestCorrelationMiddleware`, so support can look it up) and a link to the Dashboard or sign-in. `UseStatusCodePagesWithReExecute` gives 404, 403, 400, 413 and other empty error responses the same page, and `Identity/Account/AccessDenied` matches it. The template "Development Mode" text is gone. The Privacy page was removed rather than written: a privacy notice has to name the data controller and the customer's retention and contact details, which the product can't know, so it belongs in each customer's own documentation. Covered by TC-809 and TC-UI-007. A Playwright test for TC-UI-007 already exists (`SmokeTests.UnknownUrlReturnsNotFoundWithoutCrashing`, since `48d572a`), but it only checks the 404 status and the absence of a stack trace. Not done: extend it to check the friendly "Page not found" heading, and change TC-UI-007 from "(planned)" to that test in `Matrix-TestCases-Tests.md`.

- `Pages/Error.cshtml` is still the ASP.NET template text. It shows end users a "Development Mode" section explaining how to switch environments. Replace it with a friendly message, the request ID (so support can find the log entry by `CorrelationId`), and a link back to the Dashboard. Add matching 403 and 404 pages (TC-UI-007).
- `Pages/Privacy.cshtml` says "Use this page to detail your site's privacy policy." Either write a real privacy notice (the app stores names, e-mail addresses and login IPs) or remove the page.

---

## P2 - Should be in v1.0

### 6. Bulk review and review progress on Scan Details (verified)

Findings are reviewed one at a time through a modal (`Details.cshtml` around line 689). A scan with hundreds of findings, for example a mass-change burst (E1) or out-of-hours activity (C3), means hundreds of modal round trips. Each one reloads the page.

To do:
- Row checkboxes, "select all on this page" and "select all matching the filter", with a single disposition and comment applied to all selected rows. Write one audit entry per finding, so traceability is unchanged.
- A visible progress indicator ("37 of 120 findings reviewed") near the sign button, so the reviewer knows why signing is disabled.
- Saving a review without a full page reload, keeping the filter, page and scroll position.

### 7. Filters on the Scans list and on the findings tabs (verified)

`Scan/Index` has only a free-text search. The findings tabs on Scan Details have search and paging, but no structured filters.

To do:
- **Scans list:** filter by system, status (open / signed), date range and uploader, plus "assigned to me". Add columns for the number of findings and for review progress, which matter more to a reviewer than "File type".
- **Findings tabs:** filter by rule code and category (A-E), by review status (unreviewed / false positive / investigation / deviation) and by user. Make "Unreviewed only" the default view while the scan is open.

### 8. A clear "Upload" entry point (verified)

The upload page (`/Scan/Scan`) is only linked from the System list and System Details. A first-time user has to know to go via a system. `Help/Index.cshtml` (line 35) tells users to go to "Scans → Upload", which doesn't exist. Add an **Upload audit trail** button to the Scans list and the Dashboard that opens the upload page with a system picker, and correct the Help text.

### 9. Accessibility (WCAG 2.1 AA) pass (verified, partly)

- **Contrast:** the Dashboard cards use `text-white` on `bg-warning` (yellow) and `bg-info` (light blue), which fail AA. Use `text-dark` or darker theme colours.
- **Icon-only buttons:** the action buttons in lists (eye, link, trash icons) rely on `title` tooltips. Add `aria-label` and a visually hidden text label to each.
- **Notification bell:** it has no accessible name, the badge count isn't announced, and the `aria-haspopup`/`aria-expanded` attributes on the menu dropdowns are commented out (`_Layout.cshtml` lines 71, 83, 94).
- **Colour-only status:** review status uses ✔/⚠/❌ emoji plus a colour. Keep the text label next to them, and don't let emoji stand in for icons in buttons and headings ("📥 Export", "✍️ Close review", "📜 Audit trail"). Use one icon set.
- **Missing pieces:** a "Skip to main content" link; focus moved to the first field when a modal opens; announcing alerts with `role="alert"`.
- **Verification:** run axe or Lighthouse against every page and record the results. A Playwright accessibility check could become part of `MenuLinkCrawlerTests`.

### 10. Page titles, active menu item, consistent breadcrumbs (verified)

- `_Layout.cshtml` line 15 hard-codes `<title>GxPAuditTrailReview</title>`. The 35 pages that set `ViewData["Title"]` never reach the browser tab, so every tab, bookmark and history entry has the same name. Use `@ViewData["Title"] - GxPAuditTrailReview`, and replace generic titles ("Index" on 7 pages, "Details", "Edit") with real names ("Scans", "Scan #123").
- The top menu doesn't highlight the current section.
- Breadcrumbs are hand-written on each page and inconsistent. For example, `Scan/Index` has an unlinked "Scans" item and a commented-out "Users" item. Use one shared breadcrumb partial.

### 11. One message and alert pattern (verified)

Pages use ten different `TempData` keys for feedback (`Success`, `SuccessMessage`, `Error`, `WarningMessage`, `AiSuccess`, `ApiSuccess`, `McpSuccess`, `GeneralSuccess`, `SecuritySuccess`, `SettingsSuccess`), each rendered by its own markup. Replace them with one `_StatusMessages` partial (success / info / warning / error, dismissible, with `role="alert"`). Use the same style for confirmations: a modal for destructive actions instead of mixing `confirm()` (2 places), separate Delete pages and modals.

### 12. Branding, naming and visual consistency (verified)

- The footer hard-codes `v 0.9`. Read it from the assembly version so screenshots in validation evidence always show the real build.
- Button colours carry different meanings on different pages: "View details" is yellow `btn-warning` (a warning colour), "Reopen" is also yellow, "Close & sign" is black. Define a small palette: primary for the main action, secondary for neutral actions, danger only for delete or irreversible actions.
- Labels mix sentence case and title case ("Rule-based review engine" in the menu, "Rule-Based Review Engine" as the page title).

### 13. Serve Bootstrap Icons locally (verified)

`_Layout.cshtml` line 22 loads Bootstrap Icons 1.8.1 from `cdn.jsdelivr.net`. In an air-gapped or locked-down GxP network every icon disappears, and many buttons are icon-only. A CDN is also an external dependency for a validated system and has to be allowed in the CSP. Ship the icons in `wwwroot/lib` like Bootstrap and jQuery, update them to a current version, and add them to the SBOM.

### 14. A dashboard focused on the reviewer's work (verified)

The Dashboard shows four total counts and "My latest 5 scans". The cards aren't clickable, and the table still has a "File type" column. For a reviewer, the useful questions are: what is waiting for me, what is overdue, and what failed.

To do:
- Make each card a link to the matching filtered list (for example "Unreviewed findings" opens the Scans list filtered to scans with unreviewed findings).
- Add "Assigned to me / waiting for my signature" and "Uploads in progress or failed" panels.
- Show the age of open scans, so reviews that have been open too long stand out.

### 15. An upload-jobs page for users and admins

Large uploads (`Tier.Immediate` / `Tier.Worker`) report back only through the notification bell. The bell polls every 30 s and keeps only items not yet acknowledged, so once a notification is dismissed there is nowhere to see the job again. The upload page shows a simulated progress bar (`Scan.cshtml` line 148: "Purely a perceived-progress animation"). Add a "My uploads" view (queued / processing / done / failed, position in the queue, error text, retry for admins), and link each notification to it. Item 7 in the companion document is the admin side of the same page.

---

## P3 - Can follow v1.0

### 16. Split the Application Settings page

`AdminPanel/Settings/Index.cshtml` is 1,759 lines in a single page with 13 tabs (General, Security, Email, AI, API, MCP, each with its own audit-trail sub-tab). Give each section its own URL (deep-linkable, smaller pages), with a left-hand settings menu, and keep the shared audit-trail view.

### 17. Unsaved-change warnings and inline validation

The Rule-based review engine page and the Settings tabs lose edits without warning when the user switches tabs or leaves the page. Add a "you have unsaved changes" prompt, a sticky Save bar, and inline client-side validation (time ranges, thresholds) before the round trip to the server.

### 18. Responsive layout and the navbar on small screens

The navbar uses `navbar-expand-sm` with nine top-level items plus the user name, so it wraps awkwardly between about 576 and 1,000 px wide. Only 9 of the 16 pages with tables wrap them in `table-responsive`. Test at tablet width, collapse the menu to `navbar-expand-lg`, and make every wide table scroll horizontally.

### 19. Keyboard shortcuts and "next finding" navigation

For long reviews: keys for the disposition choices (F / I / D), "save and go to the next unreviewed finding", and opening the source row in context, so a reviewer can work through findings without the mouse.

### 20. Dark mode and print stylesheet

Bootstrap 5.3's `data-bs-theme` gives dark mode almost for free. The custom colours in `site.css` (hard-coded `#e9ecef`, `#495057`, `#fff`) need converting to CSS variables first. A print stylesheet for Scan Details (findings plus the signature block) would give QA a paper or PDF record without the Excel step.

### 21. Markup clean-up (verified)

- Remove the commented-out columns and breadcrumb items in `Scan/Index.cshtml` and other list pages.
- Remove the misspelled `nx-2` class (meant to be `mx-2`) used on the action buttons, and the undefined `w-35` class.
- Move the 25 inline `style="..."` attributes into `site.css`.
- Remove the empty `<script type="importmap"></script>` in `_Layout.cshtml`.
- Fix the typo "collumn" in the `Dashboard/Index.cshtml` comment.

---

## Suggested order

1. Item 1 (self-registration), done 2026-11-05.
2. Items 2-5, together with P1 items 1 and 4 of the companion document, since they touch the same sign-off and role work. Items 3 and 5 done 2026-11-05, item 2 partly done 2026-11-06 (re-authentication); item 4 and the rest of item 2 are open.
3. Items 6-8 and 14, the reviewer workflow, best designed together in one pass with a reviewer from QA.
4. Items 9-13, consistency and accessibility, ideally with a short UI style guide (colours, buttons, icons, messages) so new pages stay consistent.
5. P3 as time allows.
