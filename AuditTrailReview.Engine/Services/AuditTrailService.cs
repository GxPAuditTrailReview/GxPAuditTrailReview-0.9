using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using AuditTrailReview.Database;
using AuditTrailReview.Model;

namespace AuditTrailReview.Engine.Services
{
    public interface IAuditTrailService
    {
        /// <summary>Logs a Create operation — all non-null new property values are recorded.</summary>
        Task LogCreateAsync<T>(T newObject, string? userName, AuditObjectType objectType, string objectName, string? uniqueIdentifier = null, string? reason = null, int? systemId = null, int? scanId = null, int? systemTypeId = null, string? targetUserId = null, string? targetRoleId = null) where T : class;

        /// <summary>Logs an Update operation — only properties whose value changed are recorded.</summary>
        Task LogUpdateAsync<T>(T oldObject, T newObject, string? userName, AuditObjectType objectType, string objectName, string? uniqueIdentifier = null, string? reason = null, int? systemId = null, int? scanId = null, int? systemTypeId = null, string? targetUserId = null, string? targetRoleId = null) where T : class;

        /// <summary>Logs a Delete operation — all non-null old property values are recorded.</summary>
        Task LogDeleteAsync<T>(T deletedObject, string? userName, AuditObjectType objectType, string objectName, string? uniqueIdentifier = null, string? reason = null, int? systemId = null, int? scanId = null, int? systemTypeId = null, string? targetUserId = null, string? targetRoleId = null) where T : class;

        /// <summary>
        /// Logs many Update operations at once -- the same per-object, per-changed-property entries
        /// <see cref="LogUpdateAsync{T}"/> writes for each change, but saved in two round trips in
        /// total instead of two per object. Added for the Scan/Details bulk review (thousands of
        /// findings in one action), where calling LogUpdateAsync in a loop meant thousands of
        /// separate SaveChanges calls. Every object still gets its own entries, so each record
        /// stays individually traceable in the audit trail.
        /// </summary>
        Task LogUpdatesAsync<T>(IReadOnlyCollection<(T OldObject, T NewObject, string ObjectName)> changes, string? userName, AuditObjectType objectType, string? reason = null, int? systemId = null, int? scanId = null) where T : class;
    }

    public class AuditTrailService : IAuditTrailService
    {
        private const string SourceName = "GxPAuditTrailReview";

        private readonly ApplicationDbContext _context;

        public AuditTrailService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogCreateAsync<T>(T newObject, string? userName, AuditObjectType objectType, string objectName, string? uniqueIdentifier = null, string? reason = null, int? systemId = null, int? scanId = null, int? systemTypeId = null, string? targetUserId = null, string? targetRoleId = null) where T : class
        {
            var entries = BuildEntries<T>(oldObject: null, newObject: newObject,
                operation: AuditOperation.Create, userName: userName,
                objectType: objectType, objectName: objectName,
                uniqueIdentifier: uniqueIdentifier, reason: reason, systemId: systemId, scanId: scanId, systemTypeId: systemTypeId, targetUserId: targetUserId, targetRoleId: targetRoleId);

            if (entries.Count > 0)
            {
                _context.AuditTrail.AddRange(entries);
                await _context.SaveChangesAsync();

                foreach (var entry in entries)
                    entry.UniqueIdentifier = entry.Id.ToString();

                await _context.SaveChangesAsync();
            }
        }

        public async Task LogUpdateAsync<T>(T oldObject, T newObject, string? userName, AuditObjectType objectType, string objectName, string? uniqueIdentifier = null, string? reason = null, int? systemId = null, int? scanId = null, int? systemTypeId = null, string? targetUserId = null, string? targetRoleId = null) where T : class
        {
            var entries = BuildEntries<T>(oldObject: oldObject, newObject: newObject,
                operation: AuditOperation.Update, userName: userName,
                objectType: objectType, objectName: objectName,
                uniqueIdentifier: uniqueIdentifier, reason: reason, systemId: systemId, scanId: scanId, systemTypeId: systemTypeId, targetUserId: targetUserId, targetRoleId: targetRoleId);

            if (entries.Count > 0)
            {
                _context.AuditTrail.AddRange(entries);
                await _context.SaveChangesAsync();

                foreach (var entry in entries)
                    entry.UniqueIdentifier = entry.Id.ToString();

                await _context.SaveChangesAsync();
            }
        }

        public async Task LogDeleteAsync<T>(T deletedObject, string? userName, AuditObjectType objectType, string objectName, string? uniqueIdentifier = null, string? reason = null, int? systemId = null, int? scanId = null, int? systemTypeId = null, string? targetUserId = null, string? targetRoleId = null) where T : class
        {
            var entries = BuildEntries<T>(oldObject: deletedObject, newObject: null,
                operation: AuditOperation.Delete, userName: userName,
                objectType: objectType, objectName: objectName,
                uniqueIdentifier: uniqueIdentifier, reason: reason, systemId: systemId, scanId: scanId, systemTypeId: systemTypeId, targetUserId: targetUserId, targetRoleId: targetRoleId);

            if (entries.Count > 0)
            {
                _context.AuditTrail.AddRange(entries);
                await _context.SaveChangesAsync();

                foreach (var entry in entries)
                    entry.UniqueIdentifier = entry.Id.ToString();

                await _context.SaveChangesAsync();
            }
        }

        public async Task LogUpdatesAsync<T>(IReadOnlyCollection<(T OldObject, T NewObject, string ObjectName)> changes, string? userName, AuditObjectType objectType, string? reason = null, int? systemId = null, int? scanId = null) where T : class
        {
            var entries = new List<AuditTrailInfo>();
            foreach (var (oldObject, newObject, objectName) in changes)
            {
                entries.AddRange(BuildEntries<T>(oldObject: oldObject, newObject: newObject,
                    operation: AuditOperation.Update, userName: userName,
                    objectType: objectType, objectName: objectName,
                    uniqueIdentifier: null, reason: reason, systemId: systemId, scanId: scanId));
            }

            if (entries.Count > 0)
            {
                _context.AuditTrail.AddRange(entries);
                await _context.SaveChangesAsync();

                foreach (var entry in entries)
                    entry.UniqueIdentifier = entry.Id.ToString();

                await _context.SaveChangesAsync();
            }
        }

        // ---------------------------------------------------------------

        private static List<AuditTrailInfo> BuildEntries<T>(
            T? oldObject,
            T? newObject,
            AuditOperation operation,
            string? userName,
            AuditObjectType objectType,
            string objectName,
            string? uniqueIdentifier,
            string? reason,
            int? systemId = null,
            int? scanId = null,
            int? systemTypeId = null,
            string? targetUserId = null,
            string? targetRoleId = null) where T : class
        {
            var objectTypeLabel = objectType.ToString();
            var now             = DateTime.UtcNow;
            var operationLabel  = operation.ToString();

            var properties = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null
                         && p.GetCustomAttribute<KeyAttribute>()       == null)
                .ToArray();

            var entries = new List<AuditTrailInfo>();

            foreach (var prop in properties)
            {
                var oldVal = oldObject is not null ? prop.GetValue(oldObject)?.ToString() : null;
                var newVal = newObject is not null ? prop.GetValue(newObject)?.ToString() : null;

                // For Update: skip properties that have not changed
                if (operation == AuditOperation.Update && oldVal == newVal)
                    continue;

                entries.Add(new AuditTrailInfo
                {
                    Timestamp    = now,
                    TimestampRaw = now.ToString("o"),
                    UserID       = userName,
                    Operation    = operationLabel,
                    ObjectType   = objectTypeLabel,
                    Object       = objectName,
                    Field        = prop.Name,
                    OldValue     = oldVal,
                    NewValue     = newVal,
                    Reason       = reason,
                    Source       = SourceName,
                    SystemID     = systemId,
                    ScanID       = scanId,
                    SystemTypeID = systemTypeId,
                    TargetUserId = targetUserId,
                    TargetRoleId = targetRoleId
                });
            }

            return entries;
        }
    }

    internal enum AuditOperation
    {
        Create,
        Update,
        Delete
    }
}