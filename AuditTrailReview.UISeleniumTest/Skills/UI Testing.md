# Skill: Generate Selenium UI Tests for .NET

## Purpose

Generate high quality Selenium UI tests against the running `AuditTrailReview.WebApp`. Scoped to **AuditTrailReview.UISeleniumTest only** -- this skill does not cover unit tests (see `AuditTrailReview.UnitTest/Skills/Unit Testing.md`).

---

# Technology Stack

- .NET 10
- MSTest (`MSTest` metapackage)
- Selenium.WebDriver + Selenium.Support (Chrome; ChromeDriver is resolved automatically by Selenium Manager -- no separate driver NuGet package needed)
- coverlet.collector (code coverage)

Note: `WebAppFixture.cs` launches the real `AuditTrailReview.WebApp` process (`dotnet run --no-build`) for the test run, so these are true end-to-end browser tests against a live instance, not a mocked/in-memory host.

---

# Test Structure

- Page Object Pattern: one class per page under `Pages/` (e.g. `LoginPage.cs`, `DashboardPage.cs`, `NavMenuPage.cs`), exposing elements as properties (via `WebDriverWait`) and user actions as methods (e.g. `LoginPage.Login(email, password)`).
- One `[TestClass]` per test case under `TestCase/TC-0xx-<Name>.cs`; `Driver` created in `[TestInitialize]`, disposed in `[TestCleanup]`.
- Use AAA pattern within each test method:
  1. Arrange (navigate, set up page objects)
  2. Act (interact via page object methods)
  3. Assert (check URL, visible text, element state)

---

# Requirements

Always:
- test the primary happy path (page loads, login succeeds, navigation works),
- test at least one negative/edge case per flow (invalid input, unauthenticated access),
- assert on observable UI state (URL, breadcrumb/heading text, element visibility) rather than markup internals,
- prefer explicit waits (`WebDriverWait`) over fixed `Thread.Sleep` delays,
- read all environment values (`BaseUrl`, credentials, headless flag) from `Config.cs`, never hard-code them in a test case,
- pair every test case with a `TestCaseDescription/TC-0xx-<Name>.md` file and a row in `TestCaseDescription/README.md`.

---

# Naming Convention

Behavior-sentence style (not `Method_Scenario_Result`):

- Test class: `<Scenario>Test`
- Test method: `UserCan...` / `UserCannot...` / `<Actor><Behavior>`, e.g. `UserCanSeeHomePage`, `UserCanLogInAndSeeDashboard`, `UnauthenticatedUserIsRedirectedToLogin`
- Test Case ID: `TC-0xx`, sequential, shared by the `.cs` file, its `.md` description, and its README row
