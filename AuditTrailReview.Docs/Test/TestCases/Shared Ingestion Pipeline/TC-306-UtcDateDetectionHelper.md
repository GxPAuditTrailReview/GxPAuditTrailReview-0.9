# TC-306 – Shared Ingestion Pipeline: UTC Date-String Detection Helper

## Objective
Verify `DateTimeCheck.IsUtcDate`, a small helper that inspects a raw timestamp *string* (before it is parsed into a `DateTime`) to decide whether it already carries an explicit UTC marker (`Z` suffix or `+00:00` offset).

## Preconditions
- No database or web server involved: pure unit tests against the static `DateTimeCheck.IsUtcDate(string)` method.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `IsUtcDate` with a string ending in `Z` (e.g. `2021-01-01T00:00:00Z`) | Returns `true` |
| 2 | Call it with a string carrying an explicit `+00:00` offset | Returns `true` |
| 3 | Call it with a string carrying a non-zero offset (e.g. `+02:00`) | Returns `false` |
| 4 | Call it with a string that isn't a valid date at all | Returns `false` |

## Assertions
- Only a `Z` suffix or an exact `+00:00` offset is treated as UTC; any other offset, or an unparsable string, returns `false`.

## Related Code
- Test class: `AuditTrailReview.UnitTest.DateTimeCheckTests`
- Methods: `IsUtcDate_String_ReturnsTrue_ForZSuffix`, `IsUtcDate_String_ReturnsTrue_ForZeroOffset`, `IsUtcDate_String_ReturnsFalse_ForNonZeroOffset`, `IsUtcDate_String_ReturnsFalse_ForInvalidString`
- Production code: `AuditTrailReview.Engine.DateTimeCheck.IsUtcDate`

## Status
Active
