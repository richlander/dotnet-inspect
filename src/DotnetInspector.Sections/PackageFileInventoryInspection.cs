using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using InertText;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public enum PackageFileInventoryStatus
{
    Completed,
    Unavailable,
    Rejected,
}

public sealed record PackageFileInventoryDocument
{
    private PackageFileInventoryDocument(
        PackageFileInventoryStatus status,
        string? packageId,
        string? version,
        QuerySpaceTerminalRequirement terminal,
        ImmutableArray<PackageFileInventoryEntry> files,
        int count,
        InertString? detail)
    {
        Status = status;
        PackageId = packageId;
        Version = version;
        Terminal = terminal;
        Files = files;
        Count = count;
        Detail = detail;
    }

    public PackageFileInventoryStatus Status { get; }

    public string? PackageId { get; }

    public string? Version { get; }

    public QuerySpaceTerminalRequirement Terminal { get; }

    public ImmutableArray<PackageFileInventoryEntry> Files { get; }

    public int Count { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Detail { get; }

    public static PackageFileInventoryDocument Completed(
        string packageId,
        string version,
        QuerySpaceTerminalRequirement terminal,
        IEnumerable<PackageFileInventoryEntry> files,
        int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(files);
        if (terminal is not (
                QuerySpaceTerminalRequirement.Rows
                or QuerySpaceTerminalRequirement.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(terminal));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ImmutableArray<PackageFileInventoryEntry> snapshot =
            [.. files];
        if (terminal == QuerySpaceTerminalRequirement.Rows
            && count != snapshot.Length)
        {
            throw new ArgumentException(
                "A Rows result count must match its detached rows.",
                nameof(count));
        }
        if (terminal == QuerySpaceTerminalRequirement.Count
            && !snapshot.IsEmpty)
        {
            throw new ArgumentException(
                "A Count result must not retain detached rows.",
                nameof(files));
        }

        return new(
            PackageFileInventoryStatus.Completed,
            packageId,
            version,
            terminal,
            snapshot,
            count,
            detail: null);
    }

    public static PackageFileInventoryDocument Failed(
        PackageFileInventoryStatus status,
        QuerySpaceTerminalRequirement terminal,
        string detail)
    {
        if (status == PackageFileInventoryStatus.Completed)
        {
            throw new ArgumentException(
                "A failed inventory cannot have completed status.",
                nameof(status));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        return new(
            status,
            packageId: null,
            version: null,
            terminal,
            [],
            count: 0,
            new InertString(TextPolicy.Field, detail));
    }
}

public sealed record PackageFileInventoryInspectionRequest
{
    public PackageFileInventoryInspectionRequest(
        PackageHouseSettlement.Acquired settlement,
        QuerySpaceRequest query)
    {
        Settlement = settlement
            ?? throw new ArgumentNullException(nameof(settlement));
        Query = query
            ?? throw new ArgumentNullException(nameof(query));
    }

    public PackageHouseSettlement.Acquired Settlement { get; }

    public QuerySpaceRequest Query { get; }
}

public static class PackageFileInventoryInspection
{
    private const string SharePath = "package-file-inventory";

    public static InspectionEnvelope<PackageFileInventoryDocument> Execute(
        PackageFileInventoryInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateQuery(request.Query);

        IPackageContent content = request.Settlement.Payload.Content;
        if (content is not IPackageContentEntryManifest manifest)
        {
            return Failed(
                PackageFileInventoryStatus.Unavailable,
                request.Query.Terminal,
                "package-file-inventory.manifest-unavailable",
                "The acquired package content does not expose declared file lengths.");
        }

        IReadOnlyList<PackageContentEntry> entries =
            manifest.EnumerateEntriesWithLengths();
        if (!TryCreateRows(
                entries,
                out IReadOnlyList<PackageFileInventoryEntry> rows,
                out string? validationError))
        {
            return Failed(
                PackageFileInventoryStatus.Unavailable,
                request.Query.Terminal,
                "package-file-inventory.invalid-manifest",
                validationError!);
        }

        QuerySpaceRowIntentAssociation association =
            request.Query.RowIntents.Single();
        RowQueryResolutionResult<PackageFileInventoryEntry> resolution =
            PackageFileInventoryQuery.FileRowsScope.Resolve(
                association.Intent);
        if (!resolution.IsSuccess)
        {
            return Failed(
                PackageFileInventoryStatus.Rejected,
                request.Query.Terminal,
                "package-file-inventory.query-rejected",
                "The package-file row query could not be resolved.");
        }

        var coordinate = request.Settlement.Payload.Coordinate;
        if (request.Query.Terminal
            == QuerySpaceTerminalRequirement.Count)
        {
            if (!RowQueryExecutor.TryApplyCount(
                    rows.Count,
                    resolution.Plan!,
                    out RowSelectionCountResult count))
            {
                return Failed(
                    PackageFileInventoryStatus.Rejected,
                    request.Query.Terminal,
                    "package-file-inventory.count-unavailable",
                    "The package-file query cannot be evaluated as Count.");
            }
            if (!count.IsSuccess)
                return FailedWindow(request.Query.Terminal, count.Failure!);

            var countDocument = PackageFileInventoryDocument.Completed(
                coordinate.PackageId,
                coordinate.Version,
                request.Query.Terminal,
                [],
                count.Count);
            return Completed(countDocument);
        }

        RowSelectionResult<PackageFileInventoryEntry> selection =
            RowQueryExecutor.Apply(rows, resolution.Plan!);
        if (!selection.IsSuccess)
            return FailedWindow(
                request.Query.Terminal,
                selection.Failure!);

        var document = PackageFileInventoryDocument.Completed(
            coordinate.PackageId,
            coordinate.Version,
            request.Query.Terminal,
            selection.Values,
            selection.Values.Count);
        return Completed(document);
    }

    private static InspectionEnvelope<PackageFileInventoryDocument>
        Completed(PackageFileInventoryDocument document) =>
        new(
            document,
            new InspectionShare.NonProjectable(
                SharePath,
                "Package-file inventory sharing is not yet available."));

    private static void ValidateQuery(QuerySpaceRequest query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!query.QuerySpace.Equals(
                PackageFileInventoryQuery.QuerySpaceIdentity,
                StringComparison.Ordinal)
            || query.ParticipatingRowSets is not
                [PackageFileInventoryQuery.FilesRowSet]
            || query.RowIntents is not [var association]
            || !association.Scope.Equals(
                PackageFileInventoryQuery.FileRowsScopeIdentity,
                StringComparison.Ordinal)
            || association.RowSets is not
                [PackageFileInventoryQuery.FilesRowSet])
        {
            throw new ArgumentException(
                "The request does not target the package-file inventory query space.",
                nameof(query));
        }
    }

    private static bool TryCreateRows(
        IReadOnlyList<PackageContentEntry> entries,
        out IReadOnlyList<PackageFileInventoryEntry> rows,
        out string? error)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<PackageFileInventoryEntry>(entries.Count);
        foreach (PackageContentEntry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                rows = [];
                error = "The package entry manifest contains an empty path.";
                return false;
            }
            if (entry.Length < 0)
            {
                rows = [];
                error =
                    $"Package entry '{entry.Path}' has a negative declared length.";
                return false;
            }
            if (!paths.Add(entry.Path))
            {
                rows = [];
                error =
                    $"Package entry '{entry.Path}' appears more than once.";
                return false;
            }
            if (PackageFileInventoryQuery.IsPlumbingPath(entry.Path))
                continue;

            values.Add(
                new PackageFileInventoryEntry(
                    entry.Path,
                    entry.Length));
        }

        values.Sort(
            static (left, right) =>
                string.CompareOrdinal(
                    left.Path.ToString(),
                    right.Path.ToString()));
        rows = values;
        error = null;
        return true;
    }

    private static InspectionEnvelope<PackageFileInventoryDocument> Failed(
        PackageFileInventoryStatus status,
        QuerySpaceTerminalRequirement terminal,
        string code,
        string detail) =>
        new(
            PackageFileInventoryDocument.Failed(status, terminal, detail),
            new InspectionShare.NonProjectable(SharePath, detail),
            [
                new(
                    code,
                    InspectionDiagnosticSeverity.Error,
                    detail),
            ]);

    private static InspectionEnvelope<PackageFileInventoryDocument>
        FailedWindow(
        QuerySpaceTerminalRequirement terminal,
        RowWindowFailure failure) =>
        Failed(
            PackageFileInventoryStatus.Rejected,
            terminal,
            "package-file-inventory.rows-unavailable",
            $"Package-file row stage {failure.StageNumber} requires "
                + $"position {failure.RequiredPosition}, but only "
                + $"{failure.AvailableCount} rows are available.");
}
