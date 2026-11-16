using AuditTrailReview.Model;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AuditTrailReview.Engine
{
    /// <summary>
    /// Provides utilities for mapping CSV rows to AuditTrailInfo objects.
    /// </summary>
    public static class AuditTrailMapper
    {
        private static readonly string[] UserIDKeywords =
        {
            "user id",
            "user_id",
            "username",
            "user name", // GxPAuditTrailReview standard
            "full name",
            "fullname",
            "operator", // common LIMS terminology
            "performed by", // common eQMS/CSV terminology
            "login",
            "performedby",
            "modified by",
            "modifiedby",
            "changed by",
            "changedby",
            "executed by",
            "executedby",
            "User"
        };

        private static readonly string[] OperationKeywords =
        {
            "operation", // generic fallback
            "action", // generic fallback
            "action type", // common eQMS/CSV terminology
            "event",
            "activity",
            "transaction",
            "change type", "changetype",
            "audit type",  "audittype"
        };

        private static readonly string[] DateTimeKeywords =
        {
            "date",                   
            "time",
            "timestamp_utc",
            "timestamp",
            "event timestamp",
            "EventTime",
            "created at",
            "createdat",
            "recorded at",
            "recordedat",
            "logged at",
            "loggedat",
            "Date/Time"
        };

        private static readonly string[] UniqueIdentifierKeywords =
        {
            "eventID",
            "event ID",
            "object id",
            "EventID",
            "AuditID",
            "Record ID",
            "AuditEntryID",
            "AuditTrailEntryID",
            "AuditTrailID",
            "AuditRecordID",
            "record no",
            "recordno",
            "seq no",
            "seqno",       
            "ref no",
            "refno"  ,
            "logID",
            "id"
        };

        private static readonly string[] OldValueKeywords =
        {
            "previous value",
            "previousvalue",
            "prior value",
            "priorvalue",
            "old value",
            "oldvalue",  
            "before",    
            "old"                            
        };

        private static readonly string[] NewValueKeywords =
        {
            "updated value",
            "updatedvalue",    
            "current value",
            "currentvalue",    
            "new value",
            "newvalue",       
            "after",                             
            "new"                                
        };

        private static readonly string[] ReasonKeywords =
        {
            "reason for change",
            "reasonforchange",                      // common CDS terminology
            "Change Reason",                        // common eQMS/CSV terminology
            "reason",                                 // standard
            "justification",                          // QMS systems
            "comment",                                // common MES/general terminology
            "remarks",                                // common
            "rationale"                               // formal GxP systems
        };

        // Must be checked BEFORE ObjectKeywords — "object" is a substring of every entry here
        private static readonly string[] ObjectTypeKeywords =
        {
            "object type",
            "objecttype",
            "entity type",
            "entitytype",
            "record type",
            "recordtype",
            "item type",
            "itemtype",
            "module type",
            "moduletype",
            "data category",
            "datacategory",
            "category"
        };

        private static readonly string[] FieldKeywords =
        {
            "property",
            "field name",
            "fieldname",
            "field",
            "attribute"
        };

        private static readonly string[] SourceKeywords =
        {
            "source",
            "location",
            "machine id",
            "machineid",
            "workstation",
            "terminal id",
            "terminalid",
            "terminal",
            "station",
            "app"
        };

        private static readonly string[] ObjectKeywords =
        {
            "object name",
            "objectname",
            "object",           // generic — kept last inside the array so more-specific entries match first
            "entity",
            "module",
            "table",
            "parameter name",
            "parametername",
            "parameter",
            "target",
            "subject",
            "resource",
            "record name",
            "recordname",
            "item"
        };

        /// <summary>
        /// Maps a collection of CSV rows (represented as dictionaries) to AuditTrailInfo objects
        /// using header name heuristics.
        /// </summary>
        /// <param name="rows">The CSV rows to map</param>
        /// <returns>A list of mapped AuditTrailInfo objects</returns>
        public static List<AuditTrailInfo> MapRowsToAuditTrailInfo(IEnumerable<Dictionary<string, object>> rows)
        {
            var mappedRows = new List<AuditTrailInfo>();

            foreach (var row in rows)
            {
                var info = MapRowToAuditTrailInfo(row);

                // Only add if at least one property populated
                if (info.UniqueIdentifier != null || info.Timestamp != null || info.UserID != null || info.Operation != null ||
                    info.ObjectType != null || info.Object != null || info.Field != null ||
                    info.OldValue != null || info.NewValue != null || info.Reason != null || info.Source != null)
                {
                    mappedRows.Add(info);
                }
            }

            return mappedRows;
        }

        /// <summary>
        /// Maps a single CSV row (represented as a dictionary) to an AuditTrailInfo object
        /// using header name heuristics.
        /// </summary>
        /// <param name="row">The CSV row to map</param>
        /// <returns>A mapped AuditTrailInfo object</returns>
        public static AuditTrailInfo MapRowToAuditTrailInfo(Dictionary<string, object> row)
        {
            StringBuilder builder = new StringBuilder();

            var info = new AuditTrailInfo();

            foreach (var kv in row)
            {
                var key = (kv.Key ?? string.Empty).Trim();
                var val = kv.Value?.ToString();
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(val))
                    continue;

                var lower = key.ToLowerInvariant();

                // Check DateTime FIRST (more specific pattern)
                if (DateTimeKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.TimestampRaw = val;
                    info.Timestamp = TryParseDateTime(val);
                }
                else if (UserIDKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.UserID = val;
                }
                else if (OperationKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.Operation = val;
                }
                // ObjectType BEFORE Object — "object" is a substring of "object type"
                else if (ObjectTypeKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.ObjectType = val;
                }
                // Field BEFORE Object — "field name" is more specific than generic object keywords
                else if (FieldKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.Field = val;
                }
                else if (ObjectKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.Object = val;
                }
                else if (OldValueKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.OldValue = val;
                }
                else if (NewValueKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.NewValue = val;
                }
                else if (ReasonKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.Reason = val;
                }
                else if (SourceKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.Source = val;
                }
                // Check for ID LAST (most generic pattern)
                else if (UniqueIdentifierKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    info.UniqueIdentifier = val;
                }
            }

            return info;
        }

        /// <summary>
        /// Attempts to parse a datetime string using multiple strategies and formats.
        /// </summary>
        /// <param name="value">The string value to parse</param>
        /// <returns>The parsed DateTime, or null if parsing fails</returns>
        public static DateTime? TryParseDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmedVal = value.Trim();

            // GxP Pre-processing: Many LIMS/QMS systems append " UTC" or " GMT".
            // C# handles "Z" natively but can struggle with literal " UTC" in non-exact parsing.
            if (trimmedVal.EndsWith(" UTC", StringComparison.OrdinalIgnoreCase))
                trimmedVal = trimmedVal.Substring(0, trimmedVal.Length - 4) + "Z";
            else if (trimmedVal.EndsWith(" GMT", StringComparison.OrdinalIgnoreCase))
                trimmedVal = trimmedVal.Substring(0, trimmedVal.Length - 4) + "Z";

            // 1. Built-in Roundtrip & Standard parsing (Best for standard ISO 8601)
            if (DateTime.TryParse(trimmedVal, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var dt))
            {
                return dt;
            }

            if (DateTime.TryParse(trimmedVal, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out dt))
            {
                return dt;
            }

            if (DateTime.TryParse(trimmedVal, CultureInfo.CurrentCulture,
                    DateTimeStyles.AllowWhiteSpaces, out dt))
            {
                return dt;
            }

            // 2. Unix Timestamps (Common in GxP API payloads and system-to-system integrations)
            if (Regex.IsMatch(trimmedVal, @"^-?\d+$") && long.TryParse(trimmedVal, out long unixTime))
            {
                try
                {
                    if (trimmedVal.Length <= 10)
                        return DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
                    if (trimmedVal.Length == 13)
                        return DateTimeOffset.FromUnixTimeMilliseconds(unixTime).UtcDateTime;
                    if (trimmedVal.Length >= 14 && trimmedVal.Length <= 18)
                        return new DateTime(unixTime, DateTimeKind.Utc);
                }
                catch (ArgumentOutOfRangeException) { /* Fall through */ }
            }

            // 3. GxP / Database / Legacy Formats
            string[] formats = {
            // High-Precision ISO 8601 (Microseconds & Ticks - Critical for Audit Trail ordering)
            "yyyy-MM-ddTHH:mm:ss.ffffff", "yyyy-MM-ddTHH:mm:ss.ffffffZ",
            "yyyy-MM-ddTHH:mm:ss.fffffff", "yyyy-MM-ddTHH:mm:ss.fffffffZ",

            // Legacy Oracle & SAP Formats (Very common in Pharma/Manufacturing)
            "dd-MMM-yy HH:mm:ss", "dd-MMM-yyyy HH:mm:ss",
            "dd-MMM-yy HH:mm:ss.fff", "dd-MMM-yyyy HH:mm:ss.fff",
            "dd-MMM-yy", "dd-MMM-yyyy",

            // RFC 1123 / Web Formats
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'", "ddd, dd MMM yyyy HH:mm:ss 'UTC'",
            
            // Standard YMD
            "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
            "yyyy/MM/dd HH:mm:ss.fff", "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd",
            "yyyy.MM.dd HH:mm:ss.fff", "yyyy.MM.dd HH:mm:ss", "yyyy.MM.dd",
            "yyyyMMdd", 
            
            // Standard DMY
            "dd-MM-yyyy HH:mm:ss.fff", "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy",
            "dd/MM/yyyy HH:mm:ss.fff", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy",
            "dd.MM.yyyy HH:mm:ss.fff", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy",
            
            // Standard MDY
            "MM-dd-yyyy HH:mm:ss.fff", "MM-dd-yyyy HH:mm:ss", "MM-dd-yyyy",
            "MM/dd/yyyy HH:mm:ss.fff", "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy",
            "M/d/yyyy h:mm:ss tt", "M/d/yyyy h:mm tt", "M/d/yyyy",

            // Textual Months (e.g., 24 Oct 2023)
            "dd MMM yyyy HH:mm:ss", "dd MMM yyyy",
            "MMM dd, yyyy HH:mm:ss", "MMM dd, yyyy", 
            
            // Compact Log Signatures
            "yyyyMMddHHmmss", "yyyyMMddHHmmssfff", "yyyyMMdd_HHmmss", "yyyy-MM-dd_HH-mm-ss"
        };

            // Deliberately NOT DateTimeStyles.AssumeLocal here. None of the formats[] patterns
            // above carry any offset/timezone info, so "AssumeLocal" would only tag the result
            // as DateTimeKind.Local without ever actually knowing a real offset -- and a later
            // Kind == Local check (see ScanResultService's C2/C3 rules) would then convert it
            // using this *web server's* own OS time zone, which has nothing to do with either
            // the source system's time zone or EngineSettings.WorkdayTimeZone. That was a real,
            // user-reported bug: an ambiguous timestamp's rule-engine evaluation silently
            // depended on wherever this app happens to be hosted. Leaving these as Unspecified
            // instead makes them share the same, deliberate "assume it's local plant time in
            // WorkdayTimeZone" handling as every other ambiguous timestamp -- see
            // ScanResultService.ResolveTimestampToUtc.
            if (DateTime.TryParseExact(trimmedVal, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out dt))
            {
                return dt;
            }

            if (DateTime.TryParseExact(trimmedVal, formats, CultureInfo.CurrentCulture,
                    DateTimeStyles.AllowWhiteSpaces, out dt))
            {
                return dt;
            }

            return null;
        }
    }
}