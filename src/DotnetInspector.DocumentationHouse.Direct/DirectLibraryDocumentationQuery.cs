using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.DocumentationHouse.Direct;

/// <summary>
/// Executes unified DocumentationHouse settlement over one already-realized
/// direct Library without reconstructing companion correspondence.
/// </summary>
public static class DirectLibraryDocumentationQuery
{
    public static async ValueTask<
        IReadOnlyDictionary<string, DocumentationQueryOutcome>>
        ExecuteManyAsync(
            LibraryReference library,
            LibraryContentOwner owner,
            IReadOnlyCollection<string> documentationIds,
            DocumentationDemand demand,
            ApiSurfaceExtractionScope apiSurfaceScope,
            ApiSurfaceExtractionBounds apiSurfaceBounds,
            DocumentationHouseLimits documentationLimits,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(documentationIds);
        ArgumentNullException.ThrowIfNull(apiSurfaceBounds);
        ArgumentNullException.ThrowIfNull(documentationLimits);
        if (demand != DocumentationDemand.CompiledXml)
        {
            throw new ArgumentOutOfRangeException(
                nameof(demand),
                demand,
                "Direct Library documentation currently supports compiled XML demand.");
        }
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout));
        }
        if (documentationIds.Count == 0)
        {
            return new Dictionary<string, DocumentationQueryOutcome>();
        }

        string[] requestedIds = [.. documentationIds];
        if (requestedIds.Any(string.IsNullOrWhiteSpace)
            || requestedIds.Distinct(StringComparer.Ordinal).Count()
                != requestedIds.Length)
        {
            throw new ArgumentException(
                "Documentation IDs must be non-empty and unique.",
                nameof(documentationIds));
        }

        IReadOnlyDictionary<string, DocumentationSubjectReference> subjects =
            CompiledDocumentationSubjectResolver.Resolve(
                library,
                owner,
                requestedIds,
                apiSurfaceScope,
                apiSurfaceBounds,
                cancellationToken);
        var requests =
            new List<DocumentationHouseRequest>(requestedIds.Length);
        foreach (string documentationId in requestedIds)
        {
            DocumentationSubjectReference subject =
                subjects[documentationId];
            requests.Add(
                new(
                    DocumentationHouseRequestIdentity.Create(
                        "direct-member-documentation"),
                    subject,
                    demand,
                    new DocumentationHouseOperationPlan(
                        DocumentationHouseOperationPlanIdentity.Create(
                            "direct-member-documentation"),
                        DocumentationHousePolicyGeneration.Create(
                            "direct-member-documentation-v1"),
                        documentationLimits,
                        DateTimeOffset.UtcNow.Add(timeout),
                        DirectLibraryDocumentationHouseAdapter
                            .CreateCompiledXmlContributions(
                                library,
                                subject))));
        }

        using LibraryOperationLease operation =
            IssueOperation(owner, library);
        IReadOnlyList<DocumentationQueryResult> results =
            await DocumentationQuery.ExecuteManyAsync(
                    requests,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);
        var outcomes =
            new Dictionary<string, DocumentationQueryOutcome>(
                requestedIds.Length,
                StringComparer.Ordinal);
        for (int index = 0; index < requestedIds.Length; index++)
            outcomes.Add(requestedIds[index], results[index].Content);
        return outcomes;
    }

    private static LibraryOperationLease IssueOperation(
        LibraryContentOwner owner,
        LibraryReference library) =>
        owner.IssueOperationLease(library) switch
        {
            LibraryOperationLeaseIssueOutcome.Issued issued =>
                issued.Lease,
            LibraryOperationLeaseIssueOutcome outcome =>
                throw new InvalidOperationException(
                    "The direct Library rejected documentation access "
                        + $"({outcome.GetType().Name})."),
        };
}
