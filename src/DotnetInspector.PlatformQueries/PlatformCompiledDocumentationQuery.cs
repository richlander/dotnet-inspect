using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Platform;
using DotnetInspector.Libraries;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformQueries;

public sealed record PlatformCompiledDocumentationQueryLimits
{
    public static PlatformCompiledDocumentationQueryLimits Default { get; } =
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
/// Composes one exact PlatformHouse Library realization into the shared
/// DocumentationHouse query without acquiring or retaining source resources.
/// </summary>
public static class PlatformCompiledDocumentationQuery
{
    public static async ValueTask<CompiledDocumentationOutcome> ExecuteAsync(
        PlatformLibraryRealizationResult.Completed materialized,
        string documentationId,
        PlatformCompiledDocumentationQueryLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(materialized);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationId);
        IReadOnlyDictionary<string, CompiledDocumentationOutcome> outcomes =
            await ExecuteManyAsync(
                    materialized,
                    [documentationId],
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);
        return outcomes[documentationId];
    }

    public static async ValueTask<
        IReadOnlyDictionary<string, CompiledDocumentationOutcome>>
        ExecuteManyAsync(
            PlatformLibraryRealizationResult.Completed materialized,
            IReadOnlyCollection<string> documentationIds,
            PlatformCompiledDocumentationQueryLimits? limits = null,
            CancellationToken cancellationToken = default)
            => await ExecuteManyCoreAsync(
                    materialized,
                    documentationIds,
                    requireAllSubjects: true,
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);

    /// <summary>
    /// Queries the requested IDs represented by the exact Platform reference
    /// surface and omits IDs that belong only to another platform view.
    /// </summary>
    public static async ValueTask<
            IReadOnlyDictionary<string, CompiledDocumentationOutcome>>
            ExecuteAvailableManyAsync(
                PlatformLibraryRealizationResult.Completed materialized,
                IReadOnlyCollection<string> documentationIds,
                PlatformCompiledDocumentationQueryLimits? limits = null,
                CancellationToken cancellationToken = default)
            => await ExecuteManyCoreAsync(
                    materialized,
                    documentationIds,
                    requireAllSubjects: false,
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);

    private static async ValueTask<
            IReadOnlyDictionary<string, CompiledDocumentationOutcome>>
            ExecuteManyCoreAsync(
            PlatformLibraryRealizationResult.Completed materialized,
            IReadOnlyCollection<string> documentationIds,
            bool requireAllSubjects,
            PlatformCompiledDocumentationQueryLimits? limits,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(materialized);
        ArgumentNullException.ThrowIfNull(documentationIds);
        if (documentationIds.Count == 0)
            return new Dictionary<string, CompiledDocumentationOutcome>();

        limits ??= PlatformCompiledDocumentationQueryLimits.Default;
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        string[] requestedIds = [.. documentationIds];
        IReadOnlyDictionary<string, DocumentationSubjectReference> subjects =
            requireAllSubjects
                ? CompiledDocumentationSubjectResolver.Resolve(
                    materialized.Value.Reference,
                    materialized.Owner,
                    requestedIds,
                    limits.ApiSurfaceScope,
                    limits.ApiSurface,
                    cancellationToken)
                : CompiledDocumentationSubjectResolver.ResolveAvailable(
                    materialized.Value.Reference,
                    materialized.Owner,
                    requestedIds,
                    limits.ApiSurfaceScope,
                    limits.ApiSurface,
                    cancellationToken);
        string[] resolvedIds =
        [
            .. requestedIds.Where(subjects.ContainsKey),
        ];
        if (resolvedIds.Length == 0)
        {
            return new Dictionary<
                string,
                CompiledDocumentationOutcome>();
        }
        var requests =
            new List<DocumentationHouseRequest>(resolvedIds.Length);
        foreach (string documentationId in resolvedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentationSubjectReference subject =
                subjects[documentationId];
            CompiledXmlContribution contribution =
                PlatformDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        materialized.Receipt,
                        subject);
            requests.Add(
                new DocumentationHouseRequest(
                    DocumentationHouseRequestIdentity.Create(
                        "platform-compiled-documentation"),
                    subject,
                    DocumentationDemand.CompiledXml,
                    new DocumentationHouseOperationPlan(
                        DocumentationHouseOperationPlanIdentity.Create(
                            "platform-compiled-documentation"),
                        DocumentationHousePolicyGeneration.Create(
                            "platform-compiled-documentation-v1"),
                        limits.Documentation,
                        DateTimeOffset.UtcNow.Add(
                            limits.DocumentationTimeout),
                        [contribution])));
        }

        using LibraryOperationLease operation =
            IssueOperation(materialized);
        IReadOnlyList<CompiledDocumentationQueryResult> results =
            await CompiledDocumentationQuery.ExecuteManyAsync(
                    requests,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);
        var outcomes =
            new Dictionary<string, CompiledDocumentationOutcome>(
                resolvedIds.Length,
                StringComparer.Ordinal);
        for (int index = 0; index < resolvedIds.Length; index++)
            outcomes.Add(resolvedIds[index], results[index].Content);
        return outcomes;
    }

    private static LibraryOperationLease IssueOperation(
        PlatformLibraryRealizationResult.Completed materialized) =>
        materialized.Owner.IssueOperationLease(
            materialized.Value.Reference) switch
        {
            LibraryOperationLeaseIssueOutcome.Issued issued =>
                issued.Lease,
            LibraryOperationLeaseIssueOutcome outcome =>
                throw new InvalidOperationException(
                    "The selected Platform Library rejected documentation "
                        + $"access ({outcome.GetType().Name})."),
        };
}
