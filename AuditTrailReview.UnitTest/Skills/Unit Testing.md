# Skill: Generate Unit Tests for .NET

## Purpose

Generate high quality unit tests for .NET applications. Scoped to **AuditTrailReview.UnitTest only** -- this skill does not cover UI/Selenium tests.

---

# Technology Stack

- .NET 10
- MSTest (MSTest.TestFramework + MSTest.TestAdapter)
- Microsoft.EntityFrameworkCore.InMemory (for DB-backed logic without a real SQL Server)
- coverlet.collector (code coverage)

Note: this project references `AuditTrailReview.Database`, `AuditTrailReview.Engine`, and `AuditTrailReview.IntegrationHub.API` (added to cover `ScanProcessingController`) -- there is still no ASP.NET Core / Razor Pages dependency here. Razor Page handlers have no automated test coverage today (see NFR-029).

---

# Test Structure

Use AAA pattern:

1. Arrange
2. Act
3. Assert

---

# Requirements

Always:
- test happy path,
- test edge cases,
- test invalid inputs,
- test exceptions,
- test async methods.

---

# Naming Convention

Use format:

```text id="v0l2as"
MethodName_Scenario_ExpectedResult
```

(matches the convention already used across the existing test suite, e.g. `ApplyRulesAndGenerateResults_NullRows_ReturnsEmptyList`)
