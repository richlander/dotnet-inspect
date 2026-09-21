using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

public enum PlatformCompiledDocumentationSubjectSelection
{
    RequireAll,
    AvailableOnly,
}

/// <summary>
/// One exact package-backed Platform compiled-documentation inspection.
/// </summary>
public sealed record PlatformCompiledDocumentationInspectionRequest
{
    public PlatformCompiledDocumentationInspectionRequest(
        PlatformFamilyTarget target,
        AssemblyReferenceIdentity assembly,
        IEnumerable<string> documentationIds,
        PlatformCompiledDocumentationSubjectSelection subjectSelection)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(documentationIds);
        if (!Enum.IsDefined(subjectSelection))
            throw new ArgumentOutOfRangeException(nameof(subjectSelection));

        ImmutableArray<string> ids = [.. documentationIds];
        if (ids.IsEmpty)
        {
            throw new ArgumentException(
                "At least one documentation subject is required.",
                nameof(documentationIds));
        }
        if (ids.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Documentation subjects cannot be empty.",
                nameof(documentationIds));
        }
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new ArgumentException(
                "Documentation subjects must be unique.",
                nameof(documentationIds));
        }

        Target = target;
        Assembly = assembly;
        DocumentationIds = ids;
        SubjectSelection = subjectSelection;
    }

    public PlatformFamilyTarget Target { get; }

    public AssemblyReferenceIdentity Assembly { get; }

    public ImmutableArray<string> DocumentationIds { get; }

    public PlatformCompiledDocumentationSubjectSelection SubjectSelection
    {
        get;
    }
}

/// <summary>Detached exact Platform selection represented in inspection content.</summary>
public sealed record PlatformCompiledDocumentationSelection(
    PlatformFamily Family,
    string TargetFramework,
    string Version,
    CompiledDocumentationAssemblyIdentity Assembly,
    ImmutableArray<string> DocumentationIds,
    PlatformCompiledDocumentationSubjectSelection SubjectSelection)
{
    public ImmutableArray<string> DocumentationIds { get; init; } =
        DocumentationIds.IsDefault ? [] : DocumentationIds;
}

/// <summary>One completed ordered package-backed Platform documentation result.</summary>
public sealed record PlatformCompiledDocumentationDocument(
    PlatformCompiledDocumentationSelection Selection,
    ImmutableArray<CompiledDocumentationOutcome> Outcomes)
{
    public ImmutableArray<CompiledDocumentationOutcome> Outcomes { get; init; } =
        Outcomes.IsDefault ? [] : Outcomes;
}

public enum PlatformCompiledDocumentationFailureStage
{
    SourceRealization,
    LibraryMaterialization,
}

/// <summary>A typed non-success rather than successful empty documentation.</summary>
public sealed record PlatformCompiledDocumentationFailure(
    PlatformCompiledDocumentationSelection Selection,
    PlatformCompiledDocumentationFailureStage Stage,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Summary,
    PackagePlatformSourceDiagnosticKind? SourceDiagnostic,
    PlatformHouseSettlementKind? Settlement);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Completed), "completed")]
[JsonDerivedType(typeof(NotAvailable), "notAvailable")]
public abstract record PlatformCompiledDocumentationInspectionOutcome
{
    private protected PlatformCompiledDocumentationInspectionOutcome()
    {
    }

    public sealed record Completed(
        PlatformCompiledDocumentationDocument Document)
        : PlatformCompiledDocumentationInspectionOutcome;

    public sealed record NotAvailable(
        PlatformCompiledDocumentationFailure Failure)
        : PlatformCompiledDocumentationInspectionOutcome;
}

/// <summary>
/// Settles package-backed Platform reference content and compiled
/// documentation behind one detached inspection envelope.
/// </summary>
public static class PlatformCompiledDocumentationInspection
{
    public static async Task<
        InspectionEnvelope<PlatformCompiledDocumentationInspectionOutcome>>
        ExecutePackageBackedAsync(
            PlatformCompiledDocumentationInspectionRequest request,
            PackagePlatformHouseAdapter adapter,
            PackageSourceOperationLease sourceOperation,
            PlatformHouseWorkBudget realizationWork,
            PlatformCompiledDocumentationQueryLimits? queryLimits = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        using (sourceOperation)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(adapter);
            ArgumentNullException.ThrowIfNull(realizationWork);
            cancellationToken.ThrowIfCancellationRequested();

            PlatformCompiledDocumentationSelection selection =
                Snapshot(request);
            PlatformHouseRequest houseRequest =
                CreateRequest(
                    request,
                    adapter.ReferenceRealization,
                    realizationWork,
                    cancellationToken);

            Stopwatch stopwatch = Stopwatch.StartNew();
            PackagePlatformHouseResult<PackageReferenceRealization>
                sourceResult =
                    await adapter.RealizeReferenceAsync(
                            houseRequest,
                            sourceOperation)
                        .ConfigureAwait(false);
            stopwatch.Stop();
            if (sourceResult
                is not PackagePlatformHouseResult<
                    PackageReferenceRealization>.Succeeded reference)
            {
                var terminal = (PackagePlatformHouseResult<
                    PackageReferenceRealization>.NotSucceeded)sourceResult;
                return FailureEnvelope(
                    new(
                        selection,
                        PlatformCompiledDocumentationFailureStage
                            .SourceRealization,
                        Field(terminal.Diagnostic.Summary),
                        terminal.Diagnostic.Kind,
                        Settlement: null),
                    "platform-compiled-documentation.source-realization");
            }

            var consumed = new PlatformHouseConsumedWork(
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
                        houseRequest,
                        reference,
                        consumed)
                    .ConfigureAwait(false);
            if (materialization
                is not PackagePlatformLibraryMaterializationResult.Completed
                    completed)
            {
                PlatformHouseSettlementKind settlement =
                    materialization.Realization.Outcome.Receipt
                        .SettlementKind;
                return FailureEnvelope(
                    new(
                        selection,
                        PlatformCompiledDocumentationFailureStage
                            .LibraryMaterialization,
                        Field(
                            "PlatformHouse could not materialize the exact "
                                + "reference Library."),
                        SourceDiagnostic: null,
                        settlement),
                    "platform-compiled-documentation.library-materialization");
            }

            await using (completed.Artifacts.ConfigureAwait(false))
            await using (completed.Library.Owner.ConfigureAwait(false))
            {
                IReadOnlyDictionary<string, CompiledDocumentationOutcome>
                    outcomes =
                        request.SubjectSelection
                            == PlatformCompiledDocumentationSubjectSelection
                                .RequireAll
                            ? await PlatformCompiledDocumentationQuery
                                .ExecuteManyAsync(
                                    completed.Library,
                                    request.DocumentationIds,
                                    queryLimits,
                                    cancellationToken)
                                .ConfigureAwait(false)
                            : await PlatformCompiledDocumentationQuery
                                .ExecuteAvailableManyAsync(
                                    completed.Library,
                                    request.DocumentationIds,
                                    queryLimits,
                                    cancellationToken)
                                .ConfigureAwait(false);
                ImmutableArray<CompiledDocumentationOutcome> ordered =
                [
                    .. request.DocumentationIds
                        .Where(outcomes.ContainsKey)
                        .Select(id => outcomes[id]),
                ];
                return new(
                    new PlatformCompiledDocumentationInspectionOutcome
                        .Completed(
                            new(selection, ordered)),
                    Share());
            }
        }
    }

    private static PlatformHouseRequest CreateRequest(
        PlatformCompiledDocumentationInspectionRequest request,
        PlatformSourceCapabilityIdentity referenceCapability,
        PlatformHouseWorkBudget work,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "platform-compiled-documentation-inspection"),
            new PlatformTargetDemand.Exact(request.Target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "platform-compiled-documentation-inspection")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(
                        request.Assembly)),
                PlatformViewDemand.Reference,
                PlatformLibraryContentDemand.CompiledXmlDocumentation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "platform-compiled-documentation-inspection"),
                PlatformSourcePolicyGeneration.Create(
                    "platform-compiled-documentation-inspection-v1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [referenceCapability]),
                ]),
            work,
            cancellationToken);

    private static PlatformCompiledDocumentationSelection Snapshot(
        PlatformCompiledDocumentationInspectionRequest request) =>
        new(
            request.Target.Family,
            request.Target.TargetFramework.ToString(),
            request.Target.Version.ToString(),
            new(
                request.Assembly.Name,
                request.Assembly.Version?.ToString(),
                request.Assembly.Culture,
                request.Assembly.PublicKeyToken),
            request.DocumentationIds,
            request.SubjectSelection);

    private static InspectionEnvelope<
        PlatformCompiledDocumentationInspectionOutcome> FailureEnvelope(
            PlatformCompiledDocumentationFailure failure,
            string diagnosticCode) =>
        new(
            new PlatformCompiledDocumentationInspectionOutcome.NotAvailable(
                failure),
            Share(),
            [
                new(
                    diagnosticCode,
                    InspectionDiagnosticSeverity.Error,
                    failure.Summary,
                    correspondence: null),
            ]);

    private static InspectionShare Share() =>
        new InspectionShare.NonProjectable(
            "platform-compiled-documentation/share",
            "Platform compiled documentation does not yet have a canonical "
                + "Workspace Share projection.");

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
