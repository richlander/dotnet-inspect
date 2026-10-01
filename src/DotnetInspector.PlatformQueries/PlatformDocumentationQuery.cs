using System.Diagnostics;

using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Platform;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformQueries;

/// <summary>
/// Composes one exact PlatformHouse Library realization into unified
/// DocumentationHouse outcomes.
/// </summary>
public static class PlatformDocumentationQuery
{
    public static async ValueTask<
        IReadOnlyDictionary<string, DocumentationQueryOutcome>>
        ExecutePackageBackedManyAsync(
            PlatformFamilyTarget target,
            AssemblyReferenceIdentity assembly,
            IReadOnlyCollection<string> documentationIds,
            DocumentationDemand demand,
            PackagePlatformHouseAdapter adapter,
            PackageSourceOperationLease sourceOperation,
            PlatformHouseWorkBudget realizationWork,
            PlatformCompiledDocumentationQueryLimits? limits = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(documentationIds);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        ArgumentNullException.ThrowIfNull(realizationWork);

        using (sourceOperation)
        {
            var request = new PlatformHouseRequest(
                PlatformHouseRequestIdentity.Create(
                    "platform-documentation-query"),
                new PlatformTargetDemand.Exact(target),
                new PlatformHouseRequestOrigin.Standalone(
                    PlatformStandaloneOperationIdentity.Create(
                        "platform-documentation-query")),
                new PlatformHouseOperation.Realize(
                    new PlatformPopulationDemand.Library(
                        new PlatformLibraryDemand.Assembly(assembly)),
                    PlatformViewDemand.Reference,
                    PlatformLibraryContentDemand.CompiledXmlDocumentation),
                new PlatformSourcePlan(
                    PlatformSourcePlanIdentity.Create(
                        "platform-documentation-query"),
                    PlatformSourcePolicyGeneration.Create(
                        "platform-documentation-query-v1"),
                    [
                        new PlatformSourceSelection(
                            PlatformSourceFacet.Reference,
                            PlatformSourceSelectionMode.Precedence,
                            [adapter.ReferenceRealization]),
                    ]),
                realizationWork,
                cancellationToken);

            Stopwatch stopwatch = Stopwatch.StartNew();
            PackagePlatformHouseResult<PackageReferenceRealization>
                sourceResult =
                    await adapter.RealizeReferenceAsync(
                            request,
                            sourceOperation)
                        .ConfigureAwait(false);
            stopwatch.Stop();
            if (sourceResult
                is not PackagePlatformHouseResult<
                    PackageReferenceRealization>.Succeeded reference)
            {
                var terminal = (PackagePlatformHouseResult<
                    PackageReferenceRealization>.NotSucceeded)sourceResult;
                throw new InvalidOperationException(
                    "Platform documentation source realization failed "
                        + $"({terminal.Diagnostic.Kind}): "
                        + terminal.Diagnostic.Summary);
            }

            PlatformHouseConsumedWork consumed = new(
                sourceOperations: 1,
                targetCandidates: 0,
                assemblies: reference.Value.Libraries.Length,
                xmlDocuments: reference.Value.Libraries.Count(
                    static library => library.Documentation is not null),
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes: reference.Value.Libraries.Sum(
                    static library => library.TotalContentLength),
                forwardingHops: 0,
                targetComparisons: 0,
                elapsed: stopwatch.Elapsed);
            PackagePlatformLibraryMaterializationResult materialization =
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        consumed)
                    .ConfigureAwait(false);
            if (materialization
                is not PackagePlatformLibraryMaterializationResult.Completed
                    completed)
            {
                throw new InvalidOperationException(
                    "Platform documentation Library materialization failed "
                        + $"({materialization.Realization.Outcome.Receipt
                            .SettlementKind}).");
            }

            await using (completed.Artifacts.ConfigureAwait(false))
            await using (completed.Library.Owner.ConfigureAwait(false))
            {
                return await ExecuteManyAsync(
                        completed.Library,
                        documentationIds,
                        demand,
                        limits,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public static async ValueTask<
        IReadOnlyDictionary<string, DocumentationQueryOutcome>>
        ExecuteManyAsync(
            PlatformLibraryRealizationResult.Completed materialized,
            IReadOnlyCollection<string> documentationIds,
            DocumentationDemand demand,
            PlatformCompiledDocumentationQueryLimits? limits = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(materialized);
        ArgumentNullException.ThrowIfNull(documentationIds);
        if (demand != DocumentationDemand.CompiledXml)
        {
            throw new ArgumentOutOfRangeException(
                nameof(demand),
                demand,
                "Platform documentation currently supports compiled XML demand.");
        }
        if (documentationIds.Count == 0)
        {
            return new Dictionary<string, DocumentationQueryOutcome>();
        }

        limits ??= PlatformCompiledDocumentationQueryLimits.Default;
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

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
                materialized.Value.Library,
                materialized.Owner,
                requestedIds,
                limits.ApiSurfaceScope,
                limits.ApiSurface,
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
                        "platform-member-documentation"),
                    subject,
                    demand,
                    new DocumentationHouseOperationPlan(
                        DocumentationHouseOperationPlanIdentity.Create(
                            "platform-member-documentation"),
                        DocumentationHousePolicyGeneration.Create(
                            "platform-member-documentation-v1"),
                        limits.Documentation,
                        DateTimeOffset.UtcNow.Add(
                            limits.DocumentationTimeout),
                        [
                            PlatformDocumentationHouseAdapter
                                .CreateCompiledXmlContribution(
                                    materialized.Receipt,
                                    subject),
                        ])));
        }

        using LibraryOperationLease operation =
            IssueOperation(materialized);
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
        PlatformLibraryRealizationResult.Completed materialized) =>
        materialized.Owner.IssueOperationLease(
            materialized.Value.Library) switch
        {
            LibraryOperationLeaseIssueOutcome.Issued issued =>
                issued.Lease,
            LibraryOperationLeaseIssueOutcome outcome =>
                throw new InvalidOperationException(
                    "The selected Platform Library rejected documentation "
                        + $"access ({outcome.GetType().Name})."),
        };
}
