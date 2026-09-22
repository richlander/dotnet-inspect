using System.Runtime.Versioning;

using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Packages;
using DotnetInspector.DocumentationHouse.Source;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.SourceHouse;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserPackageDocumentationQuery
{
    internal static async ValueTask<DocumentationQueryOutcome> ExecuteAsync(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseLibraryHandoff.Compile handoff,
        string documentationId,
        IReadOnlyList<ISourceHouseSourceCapability> sourceCapabilities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationId);
        ArgumentNullException.ThrowIfNull(sourceCapabilities);

        var queryLimits = new PackageCompiledDocumentationQueryLimits
        {
            ApiSurface = BrowserApiSurfacePolicy.ExtractionBounds,
        };
        PackageHouseLibraryMaterializationOutcome materialization =
            await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    PackageHouseLibraryOptionalArtifacts
                        .ImplementationPortablePdb,
                    queryLimits.Materialization,
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

        DocumentationSubjectReference subject =
            CompiledDocumentationSubjectResolver.Resolve(
                completed.Receipt.Library,
                completed.Owner,
                [documentationId],
                queryLimits.ApiSurfaceScope,
                queryLimits.ApiSurface,
                cancellationToken)[documentationId];
        DocumentationImplementationSubjectResolution
            implementationSubject =
            DocumentationImplementationSubjectResolver.Resolve(
                completed.Receipt.Library,
                completed.Owner,
                subject,
                queryLimits.ApiSurfaceScope,
                queryLimits.ApiSurface,
                cancellationToken);
        CompiledXmlContribution compiled =
            PackageDocumentationHouseAdapter
                .CreateCompiledXmlContribution(
                    completed.Receipt,
                    subject);
        DocumentationQueryPlan query = ResolveCombinedQuery(
            cancellationToken);
        DocumentationHouseRequestIdentity requestIdentity =
            DocumentationHouseRequestIdentity.Create(
                "inspect-web-package-documentation");
        DocumentationHouseOperationPlanIdentity planIdentity =
            DocumentationHouseOperationPlanIdentity.Create(
                "inspect-web-package-documentation");
        DocumentationHousePolicyGeneration policyGeneration =
            DocumentationHousePolicyGeneration.Create(
                "inspect-web-package-documentation-v1");
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow.Add(
                queryLimits.DocumentationTimeout);
        DocumentationAuthoredSourceChannelPlan? authored =
            CreateAuthoredSourcePlan(
                requestIdentity,
                planIdentity,
                policyGeneration,
                subject,
                implementationSubject,
                completed.Receipt.Library,
                sourceCapabilities,
                deadline);
        var operationPlan = new DocumentationHouseOperationPlan(
            planIdentity,
            policyGeneration,
            queryLimits.Documentation,
            deadline,
            [compiled],
            authored);
        LibraryOperationLease operation =
            IssueOperation(completed);

        DocumentationQueryResult result =
            await DocumentationQuery.ExecuteAsync(
                    query,
                    requestIdentity,
                    subject,
                    operationPlan,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);
        return result.Content;
    }

    private static DocumentationAuthoredSourceChannelPlan?
        CreateAuthoredSourcePlan(
            DocumentationHouseRequestIdentity requestIdentity,
            DocumentationHouseOperationPlanIdentity planIdentity,
            DocumentationHousePolicyGeneration policyGeneration,
            DocumentationSubjectReference subject,
            DocumentationImplementationSubjectResolution
                implementationResolution,
            LibraryReference library,
            IReadOnlyList<ISourceHouseSourceCapability> sourceCapabilities,
            DateTimeOffset deadline)
    {
        if (library.ImplementationAssembly
            is not { } implementation)
        {
            return null;
        }

        var documentationLimits =
            CSharpAuthoredDocumentationLimits.Default;
        AssemblyContextSourceQueryContext sourceContext =
            BrowserSourceQueryContext.Create();
        SourceHouseLimits sourceLimits = DocumentationSourceLimits(
            sourceContext.MemberSourceLimits,
            documentationLimits);
        DocumentationImplementationSubjectReference?
            implementationSubject =
                (implementationResolution
                    as DocumentationImplementationSubjectResolution
                        .Resolved)
                    ?.Subject;
        var binding = new DocumentationAuthoredSourceOperationBinding(
            requestIdentity,
            planIdentity,
            policyGeneration,
            subject,
            implementation,
            implementationSubject);
        IDocumentationAuthoredSourceOperation operation =
            implementationSubject is not null
                ? SourceHouseDocumentationHouseAdapter.CreateOperation(
                    binding,
                    new SourceHouseAuthoredRequest(
                        SourceHouseRequestIdentity.Create(
                            "inspect-web-package-documentation"),
                        library,
                        implementation,
                        new SourceHouseTarget.MemberTarget(
                            implementationSubject.TypeIdentity,
                            implementationSubject.MemberIdentity!,
                            implementationSubject.MetadataToken!.Value,
                            SourceHouseMemberSourceForm.DocumentParts),
                        new SourceHouseOperationPlan(
                            SourceHouseOperationPlanIdentity.Create(
                                "inspect-web-package-documentation"),
                            SourceHousePolicyGeneration.Create(
                                "inspect-web-package-documentation-v1"),
                            sourceLimits,
                            deadline,
                            sourceCapabilities)))
                : DocumentationImplementationSubjectResolver
                    .CreateTerminalOperation(
                        binding,
                        implementationResolution);
        return new(
            binding,
            new(
                sourceLimits.MaximumDocuments,
                sourceLimits.MaximumSourceBytes,
                documentationLimits),
            operation);
    }

    private static SourceHouseLimits DocumentationSourceLimits(
        SourceHouseLimits source,
        CSharpAuthoredDocumentationLimits documentation) =>
        new(
            source.MaximumAssemblyBytes,
            source.MaximumPortablePdbBytes,
            source.TargetBounds,
            source.SourceLinkReadLimits,
            source.MaximumDocuments,
            source.MaximumTargetMappings,
            source.MaximumCandidateAttempts,
            Math.Min(
                source.MaximumSourceBytes,
                documentation.MaxSourceCharacters),
            Math.Min(
                source.MaximumSourceTextCharacters,
                documentation.MaxSourceCharacters),
            source.MaximumAttestationContributions,
            Math.Min(
                source.MaximumPhysicalDeclarationCharacters,
                documentation.MaxSourceCharacters));

    private static DocumentationQueryPlan ResolveCombinedQuery(
        CancellationToken cancellationToken) =>
        DocumentationQuery.ResolveRequest(
            DocumentationQuery.CreateRequest(
                DocumentationDemand
                    .CompiledXmlAndAuthoredSourceDocumentation),
            cancellationToken) switch
        {
            DocumentationQueryRequestResult.Accepted accepted =>
                accepted.Plan,
            DocumentationQueryRequestResult.Rejected rejected =>
                throw new InvalidOperationException(
                    "The browser documentation QuerySpace request was "
                        + $"rejected ({rejected.Kind})."),
            DocumentationQueryRequestResult.IntentRejected rejected =>
                throw new InvalidOperationException(
                    "The browser documentation demand was rejected "
                        + $"({rejected.Failure.GetType().Name})."),
            _ => throw new InvalidOperationException(
                "Unknown documentation QuerySpace result."),
        };

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
