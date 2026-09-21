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
/// One exact Platform compiled-documentation inspection.
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

/// <summary>One completed ordered Platform documentation result.</summary>
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

public enum PlatformCompiledDocumentationSource
{
    Package,
    Installed,
}

/// <summary>Source-owner diagnostic identity retained across the envelope.</summary>
public sealed record PlatformCompiledDocumentationSourceDiagnostic
{
    public PlatformCompiledDocumentationSourceDiagnostic(
        PlatformCompiledDocumentationSource source,
        string code)
    {
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Source = source;
        Code = code;
    }

    public PlatformCompiledDocumentationSource Source { get; }

    public string Code { get; }
}

/// <summary>A typed non-success rather than successful empty documentation.</summary>
public sealed record PlatformCompiledDocumentationFailure(
    PlatformCompiledDocumentationSelection Selection,
    PlatformCompiledDocumentationFailureStage Stage,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Summary,
    PlatformCompiledDocumentationSourceDiagnostic? SourceDiagnostic,
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
/// Settles Platform reference content and compiled documentation behind one
/// detached inspection envelope.
/// </summary>
public static class PlatformCompiledDocumentationInspection
{
    public sealed class Execution
    {
        private readonly PlatformCompiledDocumentationInspectionRequest
            _request;
        private readonly PlatformCompiledDocumentationSelection _selection;

        internal Execution(
            PlatformCompiledDocumentationInspectionRequest request,
            PlatformCompiledDocumentationSelection selection,
            PlatformHouseRequest houseRequest)
        {
            _request = request;
            _selection = selection;
            HouseRequest = houseRequest;
        }

        public PlatformHouseRequest HouseRequest { get; }

        public InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome>
            SourceFailure(
                string summary,
                PlatformCompiledDocumentationSourceDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(summary);
            ArgumentNullException.ThrowIfNull(diagnostic);
            return FailureEnvelope(
                new(
                    _selection,
                    PlatformCompiledDocumentationFailureStage
                        .SourceRealization,
                    Field(summary),
                    diagnostic,
                    Settlement: null),
                "platform-compiled-documentation.source-realization");
        }

        public InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome>
            MaterializationFailure(PlatformHouseSettlementKind settlement) =>
            FailureEnvelope(
                new(
                    _selection,
                    PlatformCompiledDocumentationFailureStage
                        .LibraryMaterialization,
                    Field(
                        "PlatformHouse could not materialize the exact "
                            + "reference Library."),
                    SourceDiagnostic: null,
                    settlement),
                "platform-compiled-documentation.library-materialization");

        public Task<
            InspectionEnvelope<
                PlatformCompiledDocumentationInspectionOutcome>>
            CompleteAsync(
                PlatformLibraryRealizationResult.Completed library,
                IAsyncDisposable artifacts,
                PlatformCompiledDocumentationQueryLimits? queryLimits = null,
                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(library);
            ArgumentNullException.ThrowIfNull(artifacts);
            return PlatformCompiledDocumentationInspection.CompleteAsync(
                _request,
                _selection,
                library,
                artifacts,
                queryLimits,
                cancellationToken);
        }
    }

    public static Execution Prepare(
        PlatformCompiledDocumentationInspectionRequest request,
        PlatformSourceCapabilityIdentity referenceCapability,
        PlatformHouseWorkBudget realizationWork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(referenceCapability);
        ArgumentNullException.ThrowIfNull(realizationWork);
        cancellationToken.ThrowIfCancellationRequested();

        return new(
            request,
            Snapshot(request),
            CreateRequest(
                request,
                referenceCapability,
                realizationWork,
                cancellationToken));
    }

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
            ArgumentNullException.ThrowIfNull(adapter);
            Execution execution =
                Prepare(
                    request,
                    adapter.ReferenceRealization,
                    realizationWork,
                    cancellationToken);

            Stopwatch stopwatch = Stopwatch.StartNew();
            PackagePlatformHouseResult<PackageReferenceRealization>
                sourceResult =
                    await adapter.RealizeReferenceAsync(
                            execution.HouseRequest,
                            sourceOperation)
                        .ConfigureAwait(false);
            stopwatch.Stop();
            if (sourceResult
                is not PackagePlatformHouseResult<
                    PackageReferenceRealization>.Succeeded reference)
            {
                var terminal = (PackagePlatformHouseResult<
                    PackageReferenceRealization>.NotSucceeded)sourceResult;
                return execution.SourceFailure(
                    terminal.Diagnostic.Summary,
                    new(
                        PlatformCompiledDocumentationSource.Package,
                        terminal.Diagnostic.Kind.ToString()));
            }

            PlatformHouseConsumedWork consumed = ConsumedWork(
                reference.Value.Libraries.Length,
                reference.Value.Libraries.Count(
                    static library => library.Documentation is not null),
                reference.Value.Libraries.Sum(
                    static library => library.TotalContentLength),
                stopwatch.Elapsed);
            PackagePlatformLibraryMaterializationResult materialization =
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        execution.HouseRequest,
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
                return execution.MaterializationFailure(settlement);
            }

            return await execution.CompleteAsync(
                    completed.Library,
                    completed.Artifacts,
                    queryLimits,
                    cancellationToken)
                .ConfigureAwait(false);
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

    private static PlatformHouseConsumedWork ConsumedWork(
        int assemblies,
        int xmlDocuments,
        long bytes,
        TimeSpan elapsed) =>
        new(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies,
            xmlDocuments,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed);

    private static async Task<
        InspectionEnvelope<PlatformCompiledDocumentationInspectionOutcome>>
        CompleteAsync(
            PlatformCompiledDocumentationInspectionRequest request,
            PlatformCompiledDocumentationSelection selection,
            PlatformLibraryRealizationResult.Completed library,
            IAsyncDisposable artifacts,
            PlatformCompiledDocumentationQueryLimits? queryLimits,
            CancellationToken cancellationToken)
    {
        await using (artifacts.ConfigureAwait(false))
        await using (library.Owner.ConfigureAwait(false))
        {
            IReadOnlyDictionary<string, CompiledDocumentationOutcome>
                outcomes =
                    request.SubjectSelection
                        == PlatformCompiledDocumentationSubjectSelection
                            .RequireAll
                        ? await PlatformCompiledDocumentationQuery
                            .ExecuteManyAsync(
                                library,
                                request.DocumentationIds,
                                queryLimits,
                                cancellationToken)
                            .ConfigureAwait(false)
                        : await PlatformCompiledDocumentationQuery
                            .ExecuteAvailableManyAsync(
                                library,
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
                new PlatformCompiledDocumentationInspectionOutcome.Completed(
                    new(selection, ordered)),
                Share());
        }
    }

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
