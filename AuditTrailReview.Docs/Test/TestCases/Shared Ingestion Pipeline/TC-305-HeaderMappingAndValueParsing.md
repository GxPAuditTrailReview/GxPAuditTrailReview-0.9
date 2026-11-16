# TC-305 – Shared Ingestion Pipeline: Header Mapping and Value Parsing

## Objective
Verify `AuditTrailMapper`, which turns a parsed row's raw column headers and values (from `AuditTrailFileParser`, TC-304) into a common `AuditTrailInfo` model, regardless of which vendor's header names or date formats the source audit trail export uses. Covers keyword-based header recognition (including common vendor aliases), case-insensitivity, field-priority ordering where one header could match multiple fields, DateTime format/kind parsing, and empty/null/collection handling.

## Preconditions
- No database or web server involved: pure unit tests against `AuditTrailMapper.MapRowToAuditTrailInfo` / `MapRowsToAuditTrailInfo`.
- Each header-keyword family (UserID, Operation, Timestamp, OldValue, NewValue, Reason, ObjectType, Field, Source, Object, UniqueIdentifier) is exercised with a `[DataRow]` list of every recognized header spelling for that field.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Map a row with each recognized header spelling for UserID, Operation, Timestamp, OldValue, NewValue, Reason, ObjectType, Field, Source, Object, and UniqueIdentifier (including vendor aliases such as `operator`, `action type`, `change reason`, `reasonforchange`) | Each header maps to the correct `AuditTrailInfo` property |
| 2 | Map a row where one header could match two keyword families (`"object type"` vs. `ObjectType`/`Object`; `"field name"` vs. `Field`/`Object`; `"timestamp"` vs. `Timestamp`/`UserID`) | The higher-priority field wins and the lower-priority field stays null, per the mapper's documented priority order |
| 3 | Map a full row with one value per field | Every `AuditTrailInfo` property is populated with the expected value |
| 4 | Map an empty row, a row with a `null` value, a row with a whitespace-only value, and a row with an entirely unrecognized header | Empty/unrecognized rows leave all fields `null`; a `null` value leaves that field unmapped; a whitespace-only value is stored as-is (the mapper checks `IsNullOrEmpty`, not `IsNullOrWhiteSpace`) |
| 5 | Map a header in different casings (`USER`, `User`, `uSeR`; `TIMESTAMP`, `Timestamp`, `TimeStamp`) | Header matching is case-insensitive |
| 6 | Map timestamp values in each supported format: ISO-8601 with `Z`, ISO-8601 with a positive offset, ISO-8601 with a `+00:00` offset, date-only (`yyyy-MM-dd`), and three `dd`-first formats (`dd-MM-yyyy HH:mm:ss`, `dd/MM/yyyy`, `dd.MM.yyyy HH:mm:ss`) | Each parses to the correct date; the `Z` suffix yields `DateTimeKind.Utc`, an explicit offset (including `+00:00`) yields `DateTimeKind.Local`, and no timezone info yields `DateTimeKind.Unspecified` |
| 7 | Map an invalid timestamp string | `Timestamp` stays `null` while `TimestampRaw` still stores the original string verbatim |
| 8 | Call `MapRowsToAuditTrailInfo` with an empty collection, an all-blank row, a partially populated row, multiple valid rows, and a mix of valid and blank rows | Blank rows (every value empty) are excluded from the result; all other rows are mapped and returned in order |

## Assertions
- Every documented header keyword (including vendor aliases) maps to its correct `AuditTrailInfo` field, case-insensitively.
- Field-priority rules (`ObjectType` before `Object`, `Field` before `Object`, `Timestamp`/`DateTime` before `UserID`) are enforced exactly as coded.
- `TimestampRaw` always holds the original string; `Timestamp` is `null` only when parsing fails.
- `MapRowsToAuditTrailInfo` drops only rows where every value is blank.

## Related Code
- Test class: `AuditTrailReview.UnitTest.AuditTrailMapperTests`
- Methods: all `MapRowToAuditTrailInfo_*` and `MapRowsToAuditTrailInfo_*` methods, including the `_UserKeyword_`, `_OperationKeyword_`, `_DateTimeKeyword_`, `_OldValueKeyword_`, `_NewValueKeyword_`, `_ReasonKeyword_`, `_ObjectTypeKeyword_`, `_FieldKeyword_`, `_SourceKeyword_`, `_ObjectKeyword_`, and `_UniqueIdentifierKeyword_` data-driven cases, the priority-ordering cases (`_ObjectTypeBeforeObject_`, `_FieldBeforeObject_`, `_DateTimeBeforeUser_`), the DateTime-format cases, and the vendor-alias cases (`_OperatorHeader_`, `_ActionTypeHeader_`, `_ChangeReasonHeader_`, `_ReasonForChangeHeader_`)
- Production code: `AuditTrailReview.Engine.AuditTrailMapper`, `AuditTrailReview.Model.AuditTrailInfo`

## Status
Active
