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

        DocumentationSubjectReference subject = CreateSubject(
            completed,
            documentationId,
            limits.ApiSurface,
            cancellationToken);
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
        using LibraryOperationLease operation =
            IssueOperation(completed);
        CompiledDocumentationQueryResult result =
            await CompiledDocumentationQuery.ExecuteAsync(
                    request,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);
        return result.Content;
    }

    private static DocumentationSubjectReference CreateSubject(
        PackageHouseLibraryMaterializationOutcome.Completed materialized,
        string documentationId,
        ApiSurfaceExtractionBounds bounds,
        CancellationToken cancellationToken)
    {
        using LibraryOperationLease operation =
            IssueOperation(materialized);
        LibraryApiSurfaceInspectionOutcome inspection =
            LibraryApiSurfaceInspection.Execute(
                new(
                    materialized.Receipt.Library,
                    ApiSurfaceExtractionScope.PublicWithNonPublicTypes,
                    bounds),
                operation,
                cancellationToken);
        if (inspection
            is not LibraryApiSurfaceInspectionOutcome.Completed completed)
        {
            throw new InvalidOperationException(
                $"The selected package API surface could not be inspected ({inspection}).");
        }

        (ApiType Type, ApiMember Member)[] matches =
        [
            .. completed.Correspondence.Surface.Types
                .SelectMany(type => type.Members.Select(
                    member => (Type: type, Member: member)))
                .Where(candidate =>
                    candidate.Member.DeclaringTypeDefinitionName is null
                    && ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                        candidate.Type,
                        candidate.Member,
                        out XmlDocMemberIdentity identity)
                    && identity.Value.Equals(
                        documentationId,
                        StringComparison.Ordinal)),
        ];
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                matches.Length == 0
                    ? $"The selected package Library has no member '{documentationId}'."
                    : $"The selected package Library has multiple members '{documentationId}'.");
        }

        return DocumentationSubjectReference.ForMember(
            completed.Correspondence,
            matches[0].Type,
            matches[0].Member);
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
