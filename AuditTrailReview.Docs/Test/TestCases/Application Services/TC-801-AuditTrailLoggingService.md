# TC-801 – Application Services: Audit Trail Logging Service

## Objective
Verify `AuditTrailService` (referred to in code and tests as `LogService`/`AuditTrailService`), the service every WebApp admin/config CRUD page model (TC-701, TC-702, TC-703, TC-751, TC-752, TC-771) calls to record its own create/update/delete actions into `dbo.AuditTrail`, independently of the audit-trail *rule engine* (which analyzes data uploaded from other systems, not this application's own actions).

## Preconditions
- No web server involved: `AuditTrailService` is constructed directly against an EF Core InMemory `ApplicationDbContext`.
- A minimal `SampleEntity` test class (one `[Key]` property, two mapped properties, one `[NotMapped]` property) stands in for the real entities the service logs.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `LogCreateAsync` with a new object | One entry per mapped property is saved, each with `Operation = Create`, `OldValue = null`, `NewValue` matching the property, and the supplied user name/object name/object type recorded |
| 2 | Call `LogCreateAsync` with an object whose `[Key]` or `[NotMapped]` property is set | Neither property produces an entry |
| 3 | Call `LogCreateAsync` with a `null` property value, with and without an explicit reason, and with/without a `UniqueIdentifier` override | A null value still produces an entry; a supplied reason is persisted; `UniqueIdentifier` defaults to the entry id unless explicitly overridden |
| 4 | Call `LogCreateAsync` for an object with no mapped properties at all | No entries are saved |
| 5 | Call `LogCreateAsync` with a `null` user name | The entry's user id is `null` |
| 6 | Call `LogUpdateAsync` with before/after objects where one, both, or neither property changed | Only changed properties produce entries (`Operation = Update`, old and new values both recorded); no entries are saved when nothing changed |
| 7 | Call `LogUpdateAsync` with a property changing from `null` to a value, and from a value to `null` | Old/new values are recorded correctly in both directions |
| 8 | Call `LogDeleteAsync` with a populated object | One entry per mapped property is saved (`Operation = Delete`, `NewValue = null`, `OldValue` matching the property) |
| 9 | Call `LogCreateAsync`, `LogUpdateAsync`, and `LogDeleteAsync` in sequence on the same context | Entries accumulate across calls without overwriting each other |
| 10 | Inspect the `Timestamp`, `Source`, and `UniqueIdentifier` fields on any saved entry | `Timestamp` is UTC (and identical across all entries from the same call); `Source` identifies this application; `UniqueIdentifier` is set to the entry id unless the caller supplied one |
| 11 | Call `LogCreateAsync`/`LogUpdateAsync`/`LogDeleteAsync` with an explicit `systemId` | Every entry from that call carries the supplied `SystemID` |
| 12 | Call `LogCreateAsync`/`LogUpdateAsync`/`LogDeleteAsync` without a `systemId` | Every entry's `SystemID` remains `null` |
| 13 | Call `LogCreateAsync`/`LogUpdateAsync`/`LogDeleteAsync` with an explicit `scanId` | Every entry from that call carries the supplied `ScanID` |
| 14 | Call `LogCreateAsync`/`LogUpdateAsync`/`LogDeleteAsync` without a `scanId` | Every entry's `ScanID` remains `null` |
| 15 | Call `LogCreateAsync`/`LogUpdateAsync`/`LogDeleteAsync` with an explicit `systemTypeId` | Every entry from that call carries the supplied `SystemTypeID` |
| 16 | Call `LogCreateAsync`/`LogUpdateAsync`/`LogDeleteAsync` without a `systemTypeId` | Every entry's `SystemTypeID` remains `null` |
| 17 | Call `LogUpdatesAsync` (batch) with several before/after pairs: one with one changed property, one with two, one with none | Each object gets its own entries for exactly its changed properties (1, 2 and 0), all with `Operation = Update` and the supplied user, object type, reason and `scanId` |
| 18 | Call `LogUpdatesAsync` with several changes, and with an empty list | Every saved entry's `UniqueIdentifier` is its own entry id; an empty list saves nothing |

## Assertions
- Exactly one `AuditTrail` entry is created per mapped, changed property; never for `[Key]` or `[NotMapped]` properties, and never for an unchanged property on update.
- `Operation`, `OldValue`, `NewValue`, `UserID`/user name, object name, and object type are recorded correctly for Create, Update, and Delete.
- Timestamps are always UTC (NFR-042).
- The optional `systemId`/`scanId`/`systemTypeId` parameters are persisted onto every entry produced by that call when supplied, and left `null` when omitted -- these are the scoping columns the per-entity "Audit trail" tabs (TC-751, TC-752, TC-771, TC-783) filter on. `targetUserId`/`targetRoleId` follow the same pattern and are covered directly by TC-701/TC-702 (Role/User create/edit/delete) rather than here.
- The batch `LogUpdatesAsync` (used by the Scan Details bulk review, TC-783) writes the same per-object entries as calling `LogUpdateAsync` once per object, so every record in a bulk action stays individually traceable.

## Related Code
- Test class: `AuditTrailReview.UnitTest.LogServiceTests`
- Methods: all `LogCreateAsync_*`, `LogUpdateAsync_*`, `LogDeleteAsync_*` methods, the batch methods `LogUpdatesAsync_SeveralObjects_WritesChangedPropertiesPerObject`, `LogUpdatesAsync_SeveralObjects_UniqueIdentifierSetToEachEntryId`, `LogUpdatesAsync_NoChanges_WritesNothing` (including the `*SystemIdProvided*`, `*SystemIdOmitted*`, `*ScanIdProvided*`, `*ScanIdOmitted*`, `*SystemTypeIdProvided*`, `*SystemTypeIdOmitted*` variants), plus `MultipleOperations_EntriesAccumulate`
- Production code: `AuditTrailReview.Engine.Services.AuditTrailService`
- Requirement: NFR-042 (UTC timestamps)
- Related: TC-751 (System Type), TC-752 (rule-engine Config Panel), TC-771 (System), TC-783 (Scan) each read back the `SystemID`/`ScanID`/`SystemTypeID` columns this service writes; TC-701/TC-702 read back `TargetRoleId`/`TargetUserId`

## Status
Active
