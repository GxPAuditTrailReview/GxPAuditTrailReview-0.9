using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using CsvHelper;
using CsvHelper.Configuration;

namespace AuditTrailReview.Engine;

/// <summary>
/// Single shared implementation of the CSV/XML/JSON row-extraction logic used by every ingestion
/// path (manual upload via <c>Pages/Scan/Scan.cshtml.cs</c>, the preview page via
/// <c>Pages/Scan/Preview.cshtml.cs</c>, and the Worker Service/Integration Hub API path via
/// <see cref="ScanProcessingService"/>). Consolidated per <c>Path to v1.0.md</c> item 23,
/// <c>NFR-024</c>, and <c>ADR-0001</c>'s "known gap" note -- this logic used to be implemented
/// three separate times, had already drifted between copies, and risked a parsing fix applied to
/// one path being missed in the others.
/// </summary>
public static class AuditTrailFileParser
{
    /// <summary>File extensions this parser accepts, lowercase and dot-prefixed.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions = [".csv", ".xml", ".json"];

    /// <summary>
    /// True if <paramref name="extension"/> (dot-prefixed, e.g. from
    /// <see cref="Path.GetExtension(string)"/>) is one of <see cref="SupportedExtensions"/>,
    /// case-insensitively.
    /// </summary>
    public static bool IsSupportedExtension(string extension) =>
        SupportedExtensions.Contains(extension.ToLowerInvariant());

    /// <summary>
    /// Parses the full text content of an uploaded audit-trail file into row dictionaries keyed
    /// by column/element name. <paramref name="extension"/> must be one of
    /// <see cref="SupportedExtensions"/> (callers are expected to have already validated this via
    /// <see cref="IsSupportedExtension"/>). Rows with no non-blank value in any column are
    /// skipped for every format.
    /// </summary>
    public static List<Dictionary<string, object>> ParseContent(string content, string extension)
    {
        var rows = new List<Dictionary<string, object>>();
        var normalizedExtension = extension.ToLowerInvariant();

        if (normalizedExtension == ".csv")
        {
            var sampleLines = content
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Take(5)
                .ToList();

            var delimiter = sampleLines.Sum(l => l.Count(c => c == ';')) >
                            sampleLines.Sum(l => l.Count(c => c == ',')) ? ";" : ",";

            using var stringReader = new StringReader(content);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord   = true,
                Delimiter         = delimiter,
                BadDataFound      = null,
                MissingFieldFound = null,
                HeaderValidated   = null
            };

            using var csv = new CsvReader(stringReader, config);
            foreach (IDictionary<string, object> record in csv.GetRecords<dynamic>())
            {
                if (record.Values.Any(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())))
                    rows.Add(new Dictionary<string, object>(record));
            }
        }
        else if (normalizedExtension == ".json")
        {
            using var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            var elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : (IEnumerable<JsonElement>)root.EnumerateObject()
                      .Where(p => p.Value.ValueKind == JsonValueKind.Array)
                      .SelectMany(p => p.Value.EnumerateArray());

            foreach (var element in elements)
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                var row = element.EnumerateObject()
                    .ToDictionary(p => p.Name, p => (object)(p.Value.ToString() ?? string.Empty));

                if (row.Values.Any(v => !string.IsNullOrWhiteSpace(v?.ToString())))
                    rows.Add(row);
            }
        }
        else if (normalizedExtension == ".xml")
        {
            var xdoc = XDocument.Parse(content);
            foreach (var element in xdoc.Root?.Elements() ?? [])
            {
                var row = element.Elements()
                    .ToDictionary(e => e.Name.LocalName, e => (object)e.Value);

                if (row.Values.Any(v => !string.IsNullOrWhiteSpace(v?.ToString())))
                    rows.Add(row);
            }
        }

        return rows;
    }
}
