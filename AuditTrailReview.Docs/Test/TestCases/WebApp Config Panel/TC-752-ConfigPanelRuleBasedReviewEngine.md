# TC-752 – WebApp Config Panel: Rule-Based Review Engine Settings

## Objective
Verify `RuleBasedReviewEngineModel` (`Pages/ConfigPanel/RuleBasedReviewEngine.cshtml.cs`), the "Rules" and "Engine Settings" tabs where administrators enable/disable individual rules and configure their parameters (working-hours window, weekend allowance, username-length minimum, mass-change thresholds, workday time zone, additional system-account patterns).

## Preconditions
- `RuleBasedReviewEngineModel` is constructed with an EF Core InMemory `ApplicationDbContext` and a real `AuditTrailService` (TC-801).
- Rule categories and rules are seeded per test via a `SeedCategoryWithRuleAsync` helper.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `OnGetAsync` with seeded categories/rules, and with none | Categories are ordered by code and rules within each category ordered by rule code; no categories yields an empty list |
| 2 | `OnGetAsync` for rules with each of the three regulatory/guidance-basis states | All three states are populated correctly |
| 3 | `OnGetAsync` with no `EngineSettings` row, and with an existing row | Settings default to a new instance when no row exists; an existing row's values are loaded |
| 4 | `OnPostSaveRulesAsync` with `null` or empty categories | Sets an error and redirects without saving |
| 5 | `OnPostSaveRulesAsync` with a rule's enabled flag changed, with it unchanged, and with several rules changed at once | A changed rule is updated in the database and logs one audit "Update" entry per changed rule; an unchanged rule logs nothing |
| 6 | `OnPostSaveRulesAsync` with a valid save | Sets a success `TempData` message and redirects |
| 7 | `OnPostSaveSettingsAsync` with an invalid model state | Sets an error and redirects without saving |
| 8 | `OnPostSaveSettingsAsync` with no existing `EngineSettings` row, and with an existing row | Creates a new row and logs an audit "Create" entry; an existing row logs an audit entry only for the fields that actually changed |
| 9 | `OnPostSaveSettingsAsync` with AI analysis already disabled on the existing row | Saving other settings does not spuriously log an "AI analysis enabled" change |
| 10 | `OnPostSaveSettingsAsync` with no actual change | No audit entry is logged |
| 11 | `OnPostSaveSettingsAsync` with an unrecognized workday time zone, a recognized one, and a blank one | Unrecognized: sets a model error and does not save; recognized: saves; blank: normalized to `null` rather than stored as an empty string |
| 12 | `OnPostSaveSettingsAsync` with a mass-change threshold count below 2, a non-positive window in seconds, and valid values for both | Invalid values set a model error and do not save; valid values save |
| 13 | `OnPostSaveSettingsAsync` with no existing row and with an existing row, each supplying additional system-account patterns | The patterns are persisted on create, and updated (with an audit entry) when changed on an existing row |
| 14 | `OnPostSaveSettingsAsync` with a valid save | Sets a success `TempData` message and redirects |

### Audit Trail tab (Rules / Settings sub-tabs)
| 15 | `OnGetAsync` with a rule-enablement audit entry present (`Object` set to the rule's own name, e.g. `"Rule A1"`) | Appears in `AuditTrailRulesEntries` only; `AuditTrailSettingsEntries` stays empty |
| 16 | `OnGetAsync` with an `EngineSettings` audit entry present | Appears in `AuditTrailSettingsEntries` only; `AuditTrailRulesEntries` stays empty |
| 17 | `OnGetAsync` with a System Type audit entry present (`Object == "SystemType"`, logged by TC-751 under the same `AuditObjectType.ConfigPanel`) | Appears in neither sub-tab -- that entry has its own "Audit trail" tab on `ConfigPanel/SystemType/Details` (TC-751) |
| 18 | `OnGetAsync` with `AuditTrailRulesSearchTerm` set, and with `AuditTrailSettingsSearchTerm` set | Each search term filters its own sub-tab's entries independently |
| 19 | `OnGetAsync` with more Rules-tab entries than one page size, and more Settings-tab entries than one page size, each requesting page 2 | Each sub-tab paginates independently, honoring `AuditTrailPageSize` and returning the correct `AuditTrailRulesTotalPages`/`AuditTrailSettingsTotalPages` |

## Assertions
- Rule-enabled changes and engine-settings changes each log an audit entry only for what actually changed, never a spurious entry for an untouched field.
- Workday time zone and mass-change threshold inputs are validated server-side before saving, with `ModelState`/custom errors on invalid input.
- The Rules and Settings sub-tabs of the "Audit Trail" tab are mutually exclusive and exclude entries from other `ConfigPanel` pages (e.g. System Type) that happen to share the same `ObjectType`; each sub-tab supports its own search and paging independently.

## Related Code
- Test class: `AuditTrailReview.UnitTest.WebApp.ConfigPanel.RuleBasedReviewEngineModelTests`
- Methods: all methods in `WebApp/ConfigPanel/RuleBasedReviewEngineModelTests.cs`, including the Audit Trail tab methods `OnGetAsync_RuleEnablementChange_AppearsInRulesSubTabOnly`, `OnGetAsync_EngineSettingsChange_AppearsInSettingsSubTabOnly`, `OnGetAsync_SystemTypeAuditEntry_AppearsInNeitherSubTab`, `OnGetAsync_AuditTrailRulesSearchTerm_FiltersToMatchingEntries`, `OnGetAsync_AuditTrailSettingsSearchTerm_FiltersToMatchingEntries`, `OnGetAsync_AuditTrailRulesPaging_ReturnsRequestedPage`, `OnGetAsync_AuditTrailSettingsPaging_ReturnsRequestedPage`
- Production code: `AuditTrailReview.WebApp.Pages.ConfigPanel.RuleBasedReviewEngineModel`
- Related: the rules and settings configured here are applied by `ScanResultService.ApplyRulesAndGenerateResults` (TC-101–TC-111); TC-801 (`AuditTrailService`); TC-751 (System Type's own Audit trail tab)

## Status
Active
