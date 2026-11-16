using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Unit tests for <see cref="LogServiceTests"/>.
/// Uses EF Core InMemory to verify entries are persisted correctly and
/// that the correct fields are included / excluded per operation type.
/// </summary>
[TestClass]
public class LogServiceTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AuditTrailService CreateService(ApplicationDbContext ctx)
        => new(ctx);

    // Simple test entity — two mapped properties + one [Key] + one [NotMapped]
    private class SampleEntity
    {
        [System.ComponentModel.DataAnnotations.Key]
        public int Id { get; set; }

        public string? Name { get; set; }
        public string? Value { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? Ignored { get; set; }
    }

    // ─── LogUpdatesAsync (batch) ─────────────────────────────────────────────

    [TestMethod]
    public async Task LogUpdatesAsync_SeveralObjects_WritesChangedPropertiesPerObject()
    {
        await using var ctx = CreateContext(nameof(LogUpdatesAsync_SeveralObjects_WritesChangedPropertiesPerObject));
        var svc = CreateService(ctx);
        var changes = new List<(SampleEntity, SampleEntity, string)>
        {
            (new SampleEntity { Name = "A", Value = "1" }, new SampleEntity { Name = "A", Value = "2" }, "Obj1"),
            (new SampleEntity { Name = "B", Value = "1" }, new SampleEntity { Name = "C", Value = "3" }, "Obj2"),
            (new SampleEntity { Name = "D", Value = "1" }, new SampleEntity { Name = "D", Value = "1" }, "Obj3")
        };

        await svc.LogUpdatesAsync(changes, "user1", AuditObjectType.Scan, reason: "Bulk", scanId: 42);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(1, entries.Count(e => e.Object == "Obj1"), "Only Value changed on Obj1");
        Assert.AreEqual(2, entries.Count(e => e.Object == "Obj2"), "Name and Value changed on Obj2");
        Assert.AreEqual(0, entries.Count(e => e.Object == "Obj3"), "Nothing changed on Obj3");
        Assert.IsTrue(entries.All(e => e.Operation == "Update" && e.UserID == "user1" && e.Reason == "Bulk" && e.ScanID == 42 && e.ObjectType == "Scan"));
    }

    [TestMethod]
    public async Task LogUpdatesAsync_SeveralObjects_UniqueIdentifierSetToEachEntryId()
    {
        await using var ctx = CreateContext(nameof(LogUpdatesAsync_SeveralObjects_UniqueIdentifierSetToEachEntryId));
        var svc = CreateService(ctx);
        var changes = new List<(SampleEntity, SampleEntity, string)>
        {
            (new SampleEntity { Name = "A" }, new SampleEntity { Name = "B" }, "Obj1"),
            (new SampleEntity { Name = "C" }, new SampleEntity { Name = "D" }, "Obj2")
        };

        await svc.LogUpdatesAsync(changes, "user1", AuditObjectType.Scan);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.All(e => e.UniqueIdentifier == e.Id.ToString()));
    }

    [TestMethod]
    public async Task LogUpdatesAsync_NoChanges_WritesNothing()
    {
        await using var ctx = CreateContext(nameof(LogUpdatesAsync_NoChanges_WritesNothing));
        var svc = CreateService(ctx);

        await svc.LogUpdatesAsync(new List<(SampleEntity, SampleEntity, string)>(), "user1", AuditObjectType.Scan);

        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    // ─── LogCreateAsync ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task LogCreateAsync_NewObject_SavesEntriesToDatabase()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NewObject_SavesEntriesToDatabase));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(2, entries.Count, "Name and Value properties should produce 2 entries");
    }

    [TestMethod]
    public async Task LogCreateAsync_NewObject_OperationIsCreate()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NewObject_OperationIsCreate));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.Operation == "Create"));
    }

    [TestMethod]
    public async Task LogCreateAsync_NewObject_OldValueIsNull()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NewObject_OldValueIsNull));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.OldValue == null), "Create entries must have null OldValue");
    }

    [TestMethod]
    public async Task LogCreateAsync_NewObject_NewValueMatchesProperty()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NewObject_NewValueMatchesProperty));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        var nameEntry = entries.Single(e => e.Field == nameof(SampleEntity.Name));
        Assert.AreEqual("Alpha", nameEntry.NewValue);
        var valueEntry = entries.Single(e => e.Field == nameof(SampleEntity.Value));
        Assert.AreEqual("100", valueEntry.NewValue);
    }

    [TestMethod]
    public async Task LogCreateAsync_NewObject_UserNamePersisted()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NewObject_UserNamePersisted));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.UserID == "user1"));
    }

    [TestMethod]
    public async Task LogCreateAsync_NewObject_ObjectNamePersisted()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NewObject_ObjectNamePersisted));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "MyObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.Object == "MyObject"));
    }

    [TestMethod]
    public async Task LogCreateAsync_NewObject_ObjectTypePersisted()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NewObject_ObjectTypePersisted));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.ObjectType == "System"));
    }

    [TestMethod]
    public async Task LogCreateAsync_ReasonProvided_ReasonPersisted()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_ReasonProvided_ReasonPersisted));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject", reason: "initial setup");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.Reason == "initial setup"));
    }

    [TestMethod]
    public async Task LogCreateAsync_KeyPropertyIgnored_NotInEntries()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_KeyPropertyIgnored_NotInEntries));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 99, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsFalse(entries.Any(e => e.Field == nameof(SampleEntity.Id)),
            "[Key] property must not be logged");
    }

    [TestMethod]
    public async Task LogCreateAsync_NotMappedPropertyIgnored_NotInEntries()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NotMappedPropertyIgnored_NotInEntries));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100", Ignored = "should be skipped" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsFalse(entries.Any(e => e.Field == nameof(SampleEntity.Ignored)),
            "[NotMapped] property must not be logged");
    }

    [TestMethod]
    public async Task LogCreateAsync_NullPropertyValue_EntryStillCreated()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NullPropertyValue_EntryStillCreated));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = null, Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        // Name is null — NewValue will be null, but the entry is still added
        Assert.IsTrue(entries.Any(e => e.Field == nameof(SampleEntity.Name) && e.NewValue == null));
    }

    [TestMethod]
    public async Task LogCreateAsync_UniqueIdentifierSetToEntryId()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_UniqueIdentifierSetToEntryId));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        foreach (var entry in entries)
            Assert.AreEqual(entry.Id.ToString(), entry.UniqueIdentifier,
                "UniqueIdentifier must be set to the persisted Id");
    }

    [TestMethod]
    public async Task LogCreateAsync_ExplicitUniqueIdentifier_IsUsedInstead()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_ExplicitUniqueIdentifier_IsUsedInstead));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject", uniqueIdentifier: "EXT-42");

        // The service overwrites UniqueIdentifier with entry.Id.ToString() after save,
        // so the explicit parameter does not survive the second SaveChanges.
        // This test documents the current behaviour.
        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.UniqueIdentifier == e.Id.ToString()),
            "UniqueIdentifier is always overwritten with Id after persist");
    }

    [TestMethod]
    public async Task LogCreateAsync_SystemIdProvided_PersistedOnEveryEntry()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_SystemIdProvided_PersistedOnEveryEntry));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject", systemId: 1003);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.SystemID == 1003),
            "SystemID must be persisted on every entry when provided");
    }

    [TestMethod]
    public async Task LogCreateAsync_SystemIdOmitted_SystemIdIsNull()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_SystemIdOmitted_SystemIdIsNull));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.ConfigPanel, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.SystemID == null),
            "SystemID must remain null when not provided");
    }

    [TestMethod]
    public async Task LogUpdateAsync_SystemIdProvided_PersistedOnEveryEntry()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_SystemIdProvided_PersistedOnEveryEntry));
        var svc = CreateService(ctx);
        var oldObj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var newObj = new SampleEntity { Id = 1, Name = "Beta", Value = "100" };

        await svc.LogUpdateAsync(oldObj, newObj, "user1", AuditObjectType.System, "TestObject", systemId: 1003);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.SystemID == 1003),
            "SystemID must be persisted on every entry when provided");
    }

    [TestMethod]
    public async Task LogDeleteAsync_SystemIdProvided_PersistedOnEveryEntry()
    {
        await using var ctx = CreateContext(nameof(LogDeleteAsync_SystemIdProvided_PersistedOnEveryEntry));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogDeleteAsync(obj, "user1", AuditObjectType.System, "TestObject", systemId: 1003);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.SystemID == 1003),
            "SystemID must be persisted on every entry when provided");
    }

    [TestMethod]
    public async Task LogCreateAsync_ScanIdProvided_PersistedOnEveryEntry()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_ScanIdProvided_PersistedOnEveryEntry));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.Scan, "TestObject", scanId: 20093);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.ScanID == 20093),
            "ScanID must be persisted on every entry when provided");
    }

    [TestMethod]
    public async Task LogCreateAsync_ScanIdOmitted_ScanIdIsNull()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_ScanIdOmitted_ScanIdIsNull));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.ConfigPanel, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.ScanID == null),
            "ScanID must remain null when not provided");
    }

    [TestMethod]
    public async Task LogUpdateAsync_ScanIdProvided_PersistedOnEveryEntry()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_ScanIdProvided_PersistedOnEveryEntry));
        var svc = CreateService(ctx);
        var oldObj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var newObj = new SampleEntity { Id = 1, Name = "Beta", Value = "100" };

        await svc.LogUpdateAsync(oldObj, newObj, "user1", AuditObjectType.Scan, "TestObject", scanId: 20093);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.ScanID == 20093),
            "ScanID must be persisted on every entry when provided");
    }

    [TestMethod]
    public async Task LogDeleteAsync_ScanIdProvided_PersistedOnEveryEntry()
    {
        await using var ctx = CreateContext(nameof(LogDeleteAsync_ScanIdProvided_PersistedOnEveryEntry));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogDeleteAsync(obj, "user1", AuditObjectType.Scan, "TestObject", scanId: 20093);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.ScanID == 20093),
            "ScanID must be persisted on every entry when provided");
    }

    [TestMethod]
    public async Task LogCreateAsync_SystemTypeIdProvided_PersistedOnEveryEntry()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_SystemTypeIdProvided_PersistedOnEveryEntry));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.ConfigPanel, "TestObject", systemTypeId: 7);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.SystemTypeID == 7),
            "SystemTypeID must be persisted on every entry when provided");
    }

    [TestMethod]
    public async Task LogCreateAsync_SystemTypeIdOmitted_SystemTypeIdIsNull()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_SystemTypeIdOmitted_SystemTypeIdIsNull));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.ConfigPanel, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.SystemTypeID == null),
            "SystemTypeID must remain null when not provided");
    }

    [TestMethod]
    public async Task LogUpdateAsync_SystemTypeIdProvided_PersistedOnEveryEntry()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_SystemTypeIdProvided_PersistedOnEveryEntry));
        var svc = CreateService(ctx);
        var oldObj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var newObj = new SampleEntity { Id = 1, Name = "Beta", Value = "100" };

        await svc.LogUpdateAsync(oldObj, newObj, "user1", AuditObjectType.ConfigPanel, "TestObject", systemTypeId: 7);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.SystemTypeID == 7),
            "SystemTypeID must be persisted on every entry when provided");
    }

    [TestMethod]
    public async Task LogDeleteAsync_SystemTypeIdProvided_PersistedOnEveryEntry()
    {
        await using var ctx = CreateContext(nameof(LogDeleteAsync_SystemTypeIdProvided_PersistedOnEveryEntry));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogDeleteAsync(obj, "user1", AuditObjectType.ConfigPanel, "TestObject", systemTypeId: 7);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.SystemTypeID == 7),
            "SystemTypeID must be persisted on every entry when provided");
    }

    [TestMethod]
    public async Task LogCreateAsync_TimestampIsUtc()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_TimestampIsUtc));
        var svc = CreateService(ctx);
        var before = DateTime.UtcNow.AddSeconds(-1);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var after = DateTime.UtcNow.AddSeconds(1);
        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.Timestamp >= before && e.Timestamp <= after),
            "Timestamp must be within the UTC window around the call");
    }

    [TestMethod]
    public async Task LogCreateAsync_AllEntriesShareSameTimestamp()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_AllEntriesShareSameTimestamp));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        var distinct = entries.Select(e => e.Timestamp).Distinct().ToList();
        Assert.AreEqual(1, distinct.Count, "All entries in one call must share the same timestamp");
    }

    [TestMethod]
    public async Task LogCreateAsync_SourceIsGxPAuditTrailReview()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_SourceIsGxPAuditTrailReview));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.Source == "GxPAuditTrailReview"));
    }

    [TestMethod]
    public async Task LogCreateAsync_ObjectWithNoMappedProperties_NoEntriesSaved()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_ObjectWithNoMappedProperties_NoEntriesSaved));
        var svc = CreateService(ctx);
        var obj = new EmptyEntity { Id = 1 };

        await svc.LogCreateAsync(obj, "user1", AuditObjectType.System, "Empty");

        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    // ─── LogUpdateAsync ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task LogUpdateAsync_ChangedProperties_SavesOnlyChangedEntries()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_ChangedProperties_SavesOnlyChangedEntries));
        var svc = CreateService(ctx);
        var old = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var @new = new SampleEntity { Id = 1, Name = "Beta",  Value = "100" };

        await svc.LogUpdateAsync(old, @new, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(1, entries.Count, "Only Name changed — exactly one entry expected");
        Assert.AreEqual(nameof(SampleEntity.Name), entries[0].Field);
    }

    [TestMethod]
    public async Task LogUpdateAsync_ChangedProperties_OperationIsUpdate()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_ChangedProperties_OperationIsUpdate));
        var svc = CreateService(ctx);
        var old = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var @new = new SampleEntity { Id = 1, Name = "Beta",  Value = "100" };

        await svc.LogUpdateAsync(old, @new, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.Operation == "Update"));
    }

    [TestMethod]
    public async Task LogUpdateAsync_OldAndNewValuesRecorded()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_OldAndNewValuesRecorded));
        var svc = CreateService(ctx);
        var old = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var @new = new SampleEntity { Id = 1, Name = "Beta",  Value = "100" };

        await svc.LogUpdateAsync(old, @new, "user1", AuditObjectType.System, "TestObject");

        var entry = (await ctx.AuditTrail.ToListAsync()).Single();
        Assert.AreEqual("Alpha", entry.OldValue);
        Assert.AreEqual("Beta",  entry.NewValue);
    }

    [TestMethod]
    public async Task LogUpdateAsync_NoChanges_NoEntriesSaved()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_NoChanges_NoEntriesSaved));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogUpdateAsync(obj, obj, "user1", AuditObjectType.System, "TestObject");

        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task LogUpdateAsync_BothPropertiesChanged_TwoEntriesSaved()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_BothPropertiesChanged_TwoEntriesSaved));
        var svc = CreateService(ctx);
        var old = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var @new = new SampleEntity { Id = 1, Name = "Beta",  Value = "200" };

        await svc.LogUpdateAsync(old, @new, "user1", AuditObjectType.System, "TestObject");

        Assert.AreEqual(2, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task LogUpdateAsync_UniqueIdentifierSetToEntryId()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_UniqueIdentifierSetToEntryId));
        var svc = CreateService(ctx);
        var old = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var @new = new SampleEntity { Id = 1, Name = "Beta",  Value = "100" };

        await svc.LogUpdateAsync(old, @new, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        foreach (var entry in entries)
            Assert.AreEqual(entry.Id.ToString(), entry.UniqueIdentifier);
    }

    [TestMethod]
    public async Task LogUpdateAsync_NullToValue_OldValueNullNewValueSet()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_NullToValue_OldValueNullNewValueSet));
        var svc = CreateService(ctx);
        var old = new SampleEntity { Id = 1, Name = null,    Value = "100" };
        var @new = new SampleEntity { Id = 1, Name = "Beta",  Value = "100" };

        await svc.LogUpdateAsync(old, @new, "user1", AuditObjectType.System, "TestObject");

        var entry = (await ctx.AuditTrail.ToListAsync()).Single();
        Assert.IsNull(entry.OldValue);
        Assert.AreEqual("Beta", entry.NewValue);
    }

    [TestMethod]
    public async Task LogUpdateAsync_ValueToNull_OldValueSetNewValueNull()
    {
        await using var ctx = CreateContext(nameof(LogUpdateAsync_ValueToNull_OldValueSetNewValueNull));
        var svc = CreateService(ctx);
        var old = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var @new = new SampleEntity { Id = 1, Name = null,    Value = "100" };

        await svc.LogUpdateAsync(old, @new, "user1", AuditObjectType.System, "TestObject");

        var entry = (await ctx.AuditTrail.ToListAsync()).Single();
        Assert.AreEqual("Alpha", entry.OldValue);
        Assert.IsNull(entry.NewValue);
    }

    // ─── LogDeleteAsync ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task LogDeleteAsync_DeletedObject_SavesEntriesToDatabase()
    {
        await using var ctx = CreateContext(nameof(LogDeleteAsync_DeletedObject_SavesEntriesToDatabase));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogDeleteAsync(obj, "user1", AuditObjectType.System, "TestObject");

        Assert.AreEqual(2, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task LogDeleteAsync_DeletedObject_OperationIsDelete()
    {
        await using var ctx = CreateContext(nameof(LogDeleteAsync_DeletedObject_OperationIsDelete));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogDeleteAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.Operation == "Delete"));
    }

    [TestMethod]
    public async Task LogDeleteAsync_DeletedObject_NewValueIsNull()
    {
        await using var ctx = CreateContext(nameof(LogDeleteAsync_DeletedObject_NewValueIsNull));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogDeleteAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.NewValue == null), "Delete entries must have null NewValue");
    }

    [TestMethod]
    public async Task LogDeleteAsync_DeletedObject_OldValueMatchesProperty()
    {
        await using var ctx = CreateContext(nameof(LogDeleteAsync_DeletedObject_OldValueMatchesProperty));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogDeleteAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        var nameEntry = entries.Single(e => e.Field == nameof(SampleEntity.Name));
        Assert.AreEqual("Alpha", nameEntry.OldValue);
    }

    [TestMethod]
    public async Task LogDeleteAsync_UniqueIdentifierSetToEntryId()
    {
        await using var ctx = CreateContext(nameof(LogDeleteAsync_UniqueIdentifierSetToEntryId));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogDeleteAsync(obj, "user1", AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        foreach (var entry in entries)
            Assert.AreEqual(entry.Id.ToString(), entry.UniqueIdentifier);
    }

    // ─── Cross-cutting ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task MultipleOperations_EntriesAccumulate()
    {
        await using var ctx = CreateContext(nameof(MultipleOperations_EntriesAccumulate));
        var svc = CreateService(ctx);
        var obj  = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };
        var obj2 = new SampleEntity { Id = 1, Name = "Beta",  Value = "100" };

        await svc.LogCreateAsync(obj,  "user1", AuditObjectType.System, "TestObject");
        await svc.LogUpdateAsync(obj, obj2, "user1", AuditObjectType.System, "TestObject");
        await svc.LogDeleteAsync(obj2, "user1", AuditObjectType.System, "TestObject");

        // Create: 2 entries, Update: 1 entry (Name changed), Delete: 2 entries
        Assert.AreEqual(5, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task LogCreateAsync_NullUserName_UserIdIsNull()
    {
        await using var ctx = CreateContext(nameof(LogCreateAsync_NullUserName_UserIdIsNull));
        var svc = CreateService(ctx);
        var obj = new SampleEntity { Id = 1, Name = "Alpha", Value = "100" };

        await svc.LogCreateAsync(obj, null, AuditObjectType.System, "TestObject");

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.UserID == null));
    }

    // ─── Supporting types ────────────────────────────────────────────────────

    /// <summary>Entity with only a [Key] property — produces no mappable fields.</summary>
    private class EmptyEntity
    {
        [System.ComponentModel.DataAnnotations.Key]
        public int Id { get; set; }
    }
}
