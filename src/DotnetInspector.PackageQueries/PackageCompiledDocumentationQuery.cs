using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Packages;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PackageQueries;

public sealed record PackageCompiledDocumentationQueryLimits
{
    public static PackageCompiledDocumentationQueryLimits Default { get; } =
        new();

    public PackageHouseLibraryMaterializationLimits Materialization { get; init; } =
        new();

    public ApiSurfaceExtractionBounds ApiSurface { get; init; } =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    public ApiSurfaceExtractionScope ApiSurfaceScope { get; init; } =
        ApiSurfaceExtractionScope.PublicWithNonPublicTypes;

    public DocumentationHouseLimits Documentation { get; init; } =
        new(
            maximumCompiledXmlContributions: 1,
            maximumCompiledXmlBytes: 8 * 1024 * 1024,
            XmlDocumentationReadLimits.Default);

    public TimeSpan DocumentationTimeout { get; init; } =
        TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Materialization);
        ArgumentNullException.ThrowIfNull(ApiSurface);
        ArgumentNullException.ThrowIfNull(Documentation);
        if (!Enum.IsDefined(ApiSurfaceScope))
            throw new ArgumentOutOfRangeException(nameof(ApiSurfaceScope));
        if (DocumentationTimeout <= TimeSpan.Zero
            || DocumentationTimeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DocumentationTimeout),
                DocumentationTimeout,
                "The documentation timeout must be finite and positive.");
        }
    }
}

/// <summary>
/// Composes one exact PackageHouse Library handoff into the shared
/// DocumentationHouse query and retires all transferred resources before
/// returning portable content.
/// </summary>
public static class PackageCompiledDocumentationQuery
{
    public static async ValueTask<CompiledDocumentationOutcome> ExecuteAsync(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseLibraryHandoff.Compile handoff,
        string documentationId,
        PackageCompiledDocumentationQueryLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationId);
        IReadOnlyDictionary<string, CompiledDocumentationOutcome> outcomes =
            await ExecuteManyAsync(
                    settlement,
                    handoff,
                    [documentationId],
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);
        return outcomes[documentationId];
    }

    /// <summary>
    /// Queries several exact subjects from one materialized package Library.
    /// </summary>
    public static async ValueTask<
        IReadOnlyDictionary<string, CompiledDocumentationOutcome>>
        ExecuteManyAsync(
            PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff,
            IReadOnlyCollection<string> documentationIds,
            PackageCompiledDocumentationQueryLimits? limits = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(documentationIds);
        if (documentationIds.Count == 0)
            return new Dictionary<string, CompiledDocumentationOutcome>();
        string[] requestedIds = [.. documentationIds];
        if (requestedIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Documentation IDs cannot be empty.",
                nameof(documentationIds));
        }
        if (requestedIds.Distinct(StringComparer.Ordinal).Count()
            != requestedIds.Length)
        {
            throw new ArgumentException(
                "Documentation IDs must be unique.",
                nameof(documentationIds));
        }
        limits ??= PackageCompiledDocumentationQueryLimits.Default;
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        PackageHouseLibraryMaterializationOutcome materialization =
            await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    limits.Materialization,
                    cancellationToken)
                .ConfigureAwait(false);
        if (materialization
            is PackageHouseLibraryMaterializationOutcome.Terminal terminal)
        {
            throw new InvalidOperationException(
                "The selected package Library could not be materialized: "
                    + string.Join(", ", terminal.Evidence.Failures));
        }

        var completed =
            (PackageHouseLibraryMaterializationOutcome.Completed)
                materialization;
        await using var artifacts = completed.Artifacts;
        await using var owner = completed.Owner;

        IReadOnlyDictionary<string, DocumentationSubjectReference> subjects =
            CompiledDocumentationSubjectResolver.Resolve(
                completed.Receipt.Library,
                completed.Owner,
                requestedIds,
                limits.ApiSurfaceScope,
                limits.ApiSurface,
                cancellationToken);
        var outcomes =
            new Dictionary<string, CompiledDocumentationOutcome>(
                requestedIds.Length,
                StringComparer.Ordinal);
        var requests =
            new List<DocumentationHouseRequest>(requestedIds.Length);
        foreach (string documentationId in requestedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentationSubjectReference subject =
                subjects[documentationId];
            CompiledXmlContribution contribution =
                PackageDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        completed.Receipt,
                        subject);
            var plan = new DocumentationHouseOperationPlan(
                DocumentationHouseOperationPlanIdentity.Create(
                    "package-compiled-documentation"),
                DocumentationHousePolicyGeneration.Create(
                    "package-compiled-documentation-v1"),
                limits.Documentation,
                DateTimeOffset.UtcNow.Add(limits.DocumentationTimeout),
                [contribution]);
            var request = new DocumentationHouseRequest(
                DocumentationHouseRequestIdentity.Create(
                    "package-compiled-documentation"),
                subject,
                DocumentationDemand.CompiledXml,
                plan);
            requests.Add(request);
        }
        using LibraryOperationLease operation =
            IssueOperation(completed);
        IReadOnlyList<CompiledDocumentationQueryResult> results =
            await CompiledDocumentationQuery.ExecuteManyAsync(
                    requests,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);
        for (int index = 0; index < requestedIds.Length; index++)
        {
            outcomes.Add(
                requestedIds[index],
                results[index].Content);
        }
        return outcomes;
    }

    private static LibraryOperationLease IssueOperation(
        PackageHouseLibraryMaterializationOutcome.Completed materialized) =>
        materialized.Owner.IssueOperationLease(
            materialized.Receipt.Library) switch
        {
            LibraryOperationLeaseIssueOutcome.Issued issued =>
                issued.Lease,
            LibraryOperationLeaseIssueOutcome outcome =>
                throw new InvalidOperationException(
                    "The selected package Library rejected documentation "
                        + $"access ({outcome.GetType().Name})."),
        };
}
