# TC-804 – Application Services: Date/Time Display Formatting

## Objective
Verify `DateTimeDisplayService` (`Pages/AdminPanel/Settings/EditGeneral.cshtml.cs` is the admin UI for the setting it reads), which converts a stored UTC `DateTime` into the admin-configured `GeneralSettings.DisplayTimeZone` and formats it with `GeneralSettings.DateTimeFormat` -- the single place every page/export in the app turns a UTC timestamp into display text -- and the `<utc-datetime>` TagHelper (`TagHelpers/UtcDateTimeTagHelper.cs`) that calls it from Razor views.

## Preconditions
- `DateTimeDisplayService` is constructed directly against an EF Core InMemory `ApplicationDbContext` and a real (non-mocked) `Microsoft.Extensions.Caching.Memory.MemoryCache` (`PageModelTestHelpers.CreateDateTimeDisplayService`).
- `UtcDateTimeTagHelperTests` builds a real `TagHelperContext`/`TagHelperOutput` with `TagMode.SelfClosing` -- matching exactly what Razor's generated code produces for every real `<utc-datetime value="..." />` usage in this app (always written self-closing) before invoking `ProcessAsync`.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `FormatAsync` with a `null` `DateTime?` | Returns an empty string |
| 2 | `FormatAsync` with no `GeneralSettings` row in the database | Falls back to the default format (`dd-MMM-yy HH:mm`) and UTC |
| 3 | `FormatAsync` with a configured `DateTimeFormat` | The UTC value is formatted with that exact format string |
| 4 | `FormatAsync` with a configured `DisplayTimeZone` | The UTC value is converted to that time zone before formatting |
| 5 | `FormatAsync` with an unresolvable `DisplayTimeZone` (not a real IANA/Windows id) | Falls back to UTC rather than throwing |
| 6 | `FormatAsync` with a `DateTime` of `Kind = Unspecified` (as EF Core round-trips DateTime columns) | Formats the same as an explicitly UTC-kinded value, rather than throwing |
| 7 | `FormatAsync` twice, with `GeneralSettings` changed and `InvalidateCache` called in between | The first call reflects the original settings; after `InvalidateCache`, the next call reflects the new settings immediately rather than a stale cached value |
| 8 | `UtcDateTimeTagHelper.ProcessAsync` with `Value` set, against an `output` whose `TagMode` starts as `SelfClosing` (as Razor always produces for the self-closing `<utc-datetime value="..." />` source syntax) | `output.TagMode` is switched to `StartTagAndEndTag` and `output.Content` holds the formatted text -- regression coverage for a real bug where leaving `TagMode` at its inherited `SelfClosing` value made the renderer silently discard `output.Content`, rendering every `<utc-datetime>` on every page as blank |
| 9 | `UtcDateTimeTagHelper.ProcessAsync` with `Value` set | `output.TagName` is `"span"` and `data-sort-value` is set to the raw UTC instant as an ISO 8601 string, for `wwwroot/js/table-sort.js` to sort by (see TC-781/TC-783's date columns) |
| 10 | `UtcDateTimeTagHelper.ProcessAsync` with `Value` left `null` | Renders nothing (`output.TagName` is `null`, content is empty) |

## Assertions
- `DateTimeDisplayService.FormatAsync` never throws regardless of missing settings, an unresolvable time zone, or `DateTimeKind.Unspecified` input; it always degrades to UTC / the default format instead.
- A `GeneralSettings` change takes effect on the very next `FormatAsync` call after `InvalidateCache` is called (see `EditGeneral.cshtml.cs`'s `OnPostAsync`), not after the cache's own TTL expires.
- `<utc-datetime>` always renders as `StartTagAndEndTag` with the formatted text as its content, never silently empty, and always carries a `data-sort-value` attribute so date columns sort correctly regardless of the configured display format.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.Services.DateTimeDisplayServiceTests`, `AuditTrailReview.UnitTest.WebApp.TagHelpers.UtcDateTimeTagHelperTests`
- Methods: `FormatAsync_NullDateTime_ReturnsEmptyString`, `FormatAsync_NoGeneralSettingsRow_UsesDefaultFormatAndUtc`, `FormatAsync_ConfiguredFormat_AppliesThatFormat`, `FormatAsync_ConfiguredTimeZone_ConvertsBeforeFormatting`, `FormatAsync_UnresolvableTimeZone_FallsBackToUtc`, `FormatAsync_UnspecifiedKindDateTime_TreatedAsUtc`, `InvalidateCache_SettingsChangedAfterFirstRead_NextFormatReflectsNewSettings`, `ProcessAsync_ValueSet_SwitchesToStartTagAndEndTagSoContentRenders`, `ProcessAsync_ValueSet_SetsDataSortValueToUtcIsoString`, `ProcessAsync_NullValue_RendersNothing`
- Production code: `AuditTrailReview.WebApp.Services.DateTimeDisplayService`, `AuditTrailReview.WebApp.TagHelpers.UtcDateTimeTagHelper`, `AuditTrailReview.Model.GeneralSettings`, `AuditTrailReview.WebApp.Pages.AdminPanel.Settings.EditGeneralModel`
- Related: `AuditTrailReview.WebApp.Helpers.TimeZoneHelper.GetIanaTimeZoneIds` (shared with the [C3] working-hours time zone dropdown on `RuleBasedReviewEngineModel`, TC-752) populates the Display Time Zone dropdown; `wwwroot/js/table-sort.js` consumes `data-sort-value`; every page listing scan/audit-trail dates (TC-781–TC-784) renders through `<utc-datetime>`

## Status
Active
