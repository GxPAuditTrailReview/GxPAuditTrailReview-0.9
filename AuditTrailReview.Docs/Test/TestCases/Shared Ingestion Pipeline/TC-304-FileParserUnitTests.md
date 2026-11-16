# TC-304 – Shared Ingestion Pipeline: Low-Level File Parser

## Objective
Verify `AuditTrailFileParser` (the low-level CSV/JSON/XML-to-rows parser that `ScanProcessingService.ProcessFileAsync` (TC-301) and `Pages/Scan/Preview.cshtml.cs` (TC-782) both call) in isolation from the rest of the ingestion pipeline: extension support, delimiter detection, and blank-row/blank-record skipping for each format.

## Preconditions
- No database or web server involved: pure unit tests against `AuditTrailFileParser`'s static methods.
- Each format is exercised with minimal inline CSV/JSON/XML string fixtures.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `IsSupportedExtension` with `.csv`, `.xml`, `.json` in mixed case | Returns `true`, case-insensitively |
| 2 | Call `IsSupportedExtension` with `.txt`, `.pdf`, or an empty string | Returns `false` |
| 3 | Call `ParseContent` with a comma-delimited CSV | Rows and headers are parsed correctly |
| 4 | Call `ParseContent` with a semicolon-delimited CSV | The delimiter is auto-detected; rows parse correctly |
| 5 | Call `ParseContent` with a CSV containing a fully blank row | The blank row is skipped; only genuine rows are returned |
| 6 | Call `ParseContent` with a JSON array, and with a JSON object whose property is an array of records | Both shapes parse into the same row list |
| 7 | Call `ParseContent` with a JSON row whose values are all blank, and with a single JSON object with no array-valued property | The blank row is skipped; the single-object-with-no-array case returns zero rows (the canonical `ProcessFileAsync` behavior; see the method's own comment on the now-removed `Preview.cshtml.cs` fallback) |
| 8 | Call `ParseContent` with XML using child elements per record | Rows parse correctly |
| 9 | Call `ParseContent` with an XML record whose child elements are all blank, and with a record expressed only as attributes with no child elements | The blank-children record is skipped; the attributes-only record yields zero rows (same canonical behavior as step 7, not expanded from attributes) |

## Assertions
- `IsSupportedExtension` recognizes exactly `.csv`, `.xml`, `.json` case-insensitively and nothing else.
- CSV delimiter detection and blank-row skipping work for both comma- and semicolon-delimited input.
- JSON and XML blank/empty records are skipped rather than returned as empty rows.
- A JSON object or XML element with no array/child content returns zero rows rather than being misinterpreted as one record.

## Related Code
- Test class: `AuditTrailReview.UnitTest.AuditTrailFileParserTests`
- Methods: `IsSupportedExtension_SupportedExtension_ReturnsTrue`, `IsSupportedExtension_UnsupportedExtension_ReturnsFalse`, `ParseContent_CommaDelimitedCsv_ParsesRowsAndHeaders`, `ParseContent_SemicolonDelimitedCsv_DetectsDelimiterAndParses`, `ParseContent_CsvWithBlankRow_SkipsBlankRow`, `ParseContent_JsonArray_ParsesRows`, `ParseContent_JsonObjectWithArrayProperty_ParsesRows`, `ParseContent_JsonRowWithOnlyBlankValues_IsSkipped`, `ParseContent_JsonSingleObjectWithNoArrayProperty_ReturnsNoRows`, `ParseContent_XmlWithChildElements_ParsesRows`, `ParseContent_XmlRecordWithBlankChildValues_IsSkipped`, `ParseContent_XmlRecordWithOnlyAttributesAndNoChildElements_ReturnsNoRows`
- Production code: `AuditTrailReview.Engine.AuditTrailFileParser`

## Status
Active
