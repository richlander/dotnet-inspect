using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspect.Web;
using ILInspector.Metadata;
using NuGetFetch;
using QuerySpace;
using QuerySpace.Rows;

namespace DotnetInspect.Web.Interop.Catalog;

public sealed record BrowserEcosystemTypeFindRequest(
    string Text,
    string[] Ecosystems,
    string[] PrefixDemand,
    int RowStart,
    int RowEnd,
    int MaximumPrefixPackages,
    string? TargetFramework);

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserEcosystemTypeFindPhase>))]
public enum BrowserEcosystemTypeFindPhase
{
    Indexed,
    Bounded,
    Workspace,
    Prefix,
}

public sealed record BrowserEcosystemTypeFindProgress(
    BrowserEcosystemTypeFindPhase Phase,
    string Source);

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserEcosystemTypeFindActivationStatus>))]
public enum BrowserEcosystemTypeFindActivationStatus
{
    Available,
    Unavailable,
    Stale,
    Ambiguous,
    Refused,
    Failed,
}

public sealed record BrowserEcosystemTypeFindActivation(
    BrowserEcosystemTypeFindActivationStatus Status,
    string? Action,
    string? Reason);

public sealed record BrowserEcosystemTypeFindMembership(
    string Ecosystem,
    string Basis,
    string? PlatformFamily,
    string? PackageId,
    string? PackageVersion,
    string? PackageFramework,
    string? PackageRuntimeIdentifier,
    string? PackagePrefix);

public sealed record BrowserEcosystemTypeFindPattern(
    int Ordinal,
    string Text,
    string Kind,
    string? ExactNamespaceFallback,
    string? Namespace,
    string? NamespaceMatch);

public sealed record BrowserEcosystemTypeFindAssemblyIdentity(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserEcosystemTypeFindProvenance(
    string Kind,
    string? PackageId,
    string? PackageVersion,
    string? TargetFramework,
    string? RuntimeIdentifier,
    string? AssetPath,
    string? Framework,
    string? FrameworkVersion,
    string? Project,
    string? ResolverSource,
    string? ContentReference,
    string? Digest,
    string? DeclaredName);

public sealed record BrowserEcosystemTypeFindSource(
    string Kind,
    string Label,
    string? PackageId,
    string? PackageVersion,
    string? PlatformFamily,
    BrowserEcosystemTypeFindAssemblyIdentity Library,
    BrowserEcosystemTypeFindProvenance Provenance,
    string? RequestedTargetFramework,
    string? RequestedRuntimeIdentifier,
    int ContextOrder,
    int MemberOrder);

public sealed record BrowserEcosystemTypeFindFinding(
    string Identity,
    string Scope,
    BrowserEcosystemTypeFindMembership[] Memberships,
    BrowserEcosystemTypeFindPattern Pattern,
    string EffectivePattern,
    string MatchKind,
    double Similarity,
    string Name,
    string Namespace,
    string FullName,
    BrowserEcosystemTypeFindSource Source,
    Guid ModuleVersionId,
    int DeclarationOrder,
    string DeclarationKind,
    string? DefinitionKind,
    bool? IsEditorBrowsableNever,
    bool? IsObsolete);

public sealed record BrowserEcosystemTypeFindItem(
    string Identity,
    BrowserEcosystemTypeFindFinding Finding,
    BrowserEcosystemTypeFindActivation Activation);

public sealed record BrowserEcosystemTypeFindItemFailure(
    string Phase,
    string Source,
    string Message);

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserEcosystemTypeFindEventKind>))]
public enum BrowserEcosystemTypeFindEventKind
{
    Progress,
    Finding,
    ItemFailure,
}

public sealed record BrowserEcosystemTypeFindEvent(
    BrowserEcosystemTypeFindEventKind Kind,
    BrowserEcosystemTypeFindProgress? Progress,
    BrowserEcosystemTypeFindItem? Finding,
    BrowserEcosystemTypeFindItemFailure? ItemFailure);

public sealed record BrowserEcosystemTypeFindSettlement(
    int? RowStart,
    int? RowEnd,
    BrowserEcosystemTypeFindFinding[] Findings,
    string Completion,
    BrowserEcosystemTypeFindItemFailure[] Failures,
    string[] Unsearched,
    bool Incomplete)
{
    internal static BrowserEcosystemTypeFindSettlement From(
        EcosystemFindResult result) =>
        new(
            result.ResultRows.DelegatedInputRows?.Start,
            result.ResultRows.DelegatedInputRows?.End,
            [
                .. result.Findings.Select(
                    BrowserEcosystemTypeFindProjection.ProjectFinding),
            ],
            result.Completion.ToString(),
            [
                .. result.Failures.Select(failure => new
                    BrowserEcosystemTypeFindItemFailure(
                        failure.Phase,
                        failure.Source,
                        failure.Message)),
            ],
            [.. result.Unsearched],
            result.Incomplete);
}

public sealed record BrowserEcosystemTypeFindInspection(
    Guid RequestIdentity,
    BrowserEcosystemTypeFindSettlement Settlement);

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserEcosystemTypeFindResultKind>))]
public enum BrowserEcosystemTypeFindResultKind
{
    Succeeded,
    Failed,
    Canceled,
}

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserEcosystemTypeFindFailureKind>))]
public enum BrowserEcosystemTypeFindFailureKind
{
    Expected,
    Unexpected,
}

public sealed record BrowserEcosystemTypeFindResult(
    BrowserEcosystemTypeFindResultKind Kind,
    BrowserEcosystemTypeFindInspection? Inspection,
    BrowserEcosystemTypeFindFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason)
{
    internal static BrowserEcosystemTypeFindResult From(
        BrowserManagedOperationResult<
            BrowserEcosystemTypeFindInspection,
            string,
            string> result) =>
        result switch
        {
            BrowserManagedOperationResult<
                BrowserEcosystemTypeFindInspection,
                string,
                string>.Succeeded succeeded =>
                new(
                    BrowserEcosystemTypeFindResultKind.Succeeded,
                    succeeded.Value,
                    FailureKind: null,
                    Error: null,
                    Diagnostic: null,
                    Reason: null),
            BrowserManagedOperationResult<
                BrowserEcosystemTypeFindInspection,
                string,
                string>.Failed failed =>
                new(
                    BrowserEcosystemTypeFindResultKind.Failed,
                    Inspection: null,
                    failed.FailureKind
                        is BrowserManagedOperationFailureKind.Expected
                            ? BrowserEcosystemTypeFindFailureKind.Expected
                            : BrowserEcosystemTypeFindFailureKind.Unexpected,
                    failed.Error,
                    failed.Diagnostic,
                    Reason: null),
            BrowserManagedOperationResult<
                BrowserEcosystemTypeFindInspection,
                string,
                string>.Canceled canceled =>
                new(
                    BrowserEcosystemTypeFindResultKind.Canceled,
                    Inspection: null,
                    FailureKind: null,
                    Error: null,
                    Diagnostic: null,
                    BrowserManagedOperationCancelReasons.Format(
                        canceled.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

public sealed record BrowserEcosystemTypeFindCancellation(
    string Status,
    string? Reason)
{
    internal static BrowserEcosystemTypeFindCancellation From(
        BrowserManagedCancellationRequestResult result) =>
        result switch
        {
            BrowserManagedCancellationRequestResult.Requested requested =>
                new(
                    "Requested",
                    BrowserManagedOperationCancelReasons.Format(
                        requested.Reason)),
            BrowserManagedCancellationRequestResult.AlreadyRequested requested =>
                new(
                    "AlreadyRequested",
                    BrowserManagedOperationCancelReasons.Format(
                        requested.Reason)),
            BrowserManagedCancellationRequestResult.NotActive =>
                new("NotActive", Reason: null),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

internal static class BrowserEcosystemTypeFindProjection
{
    internal static BrowserEcosystemTypeFindFinding ProjectFinding(
        EcosystemFindFinding finding)
    {
        if (finding.Finding is not FindSemanticFinding.Type type)
        {
            throw new InvalidOperationException(
                "Browser Ecosystem Type Find received a non-Type finding.");
        }

        TypeFindSemanticMatch match = type.Match;
        string identity =
            BrowserSpotlightTypeIdentity.TryCreate(match)
            ?? throw new InvalidOperationException(
                "An Ecosystem Type finding has no stable Browser identity.");
        TypeDeclarationDiscoveryAttributes? discovery =
            match.DiscoveryAttributes;
        return new(
            identity,
            finding.Scope.ToString(),
            [.. finding.Memberships.Select(ProjectMembership)],
            ProjectPattern(match.Pattern),
            match.EffectivePattern,
            match.Match.ToString(),
            match.Similarity,
            match.TypeName,
            match.Namespace,
            match.FullName,
            ProjectSource(match.Declaration.Source),
            match.Declaration.ModuleVersionId,
            match.Declaration.DeclarationOrder,
            match.DeclarationKind.ToString(),
            match.DefinitionKind?.ToString(),
            discovery?.IsEditorBrowsableNever,
            discovery?.IsObsolete);
    }

    static BrowserEcosystemTypeFindMembership ProjectMembership(
        EcosystemFindMembership membership) =>
        new(
            membership.Ecosystem.Value,
            membership.Basis.ToString(),
            membership.PlatformPopulation?.Family.ToString(),
            membership.CorePackage?.PackageId,
            membership.CorePackage?.Version,
            membership.CorePackage?.Framework,
            membership.CorePackage?.RuntimeIdentifier,
            membership.PackagePrefix?.Prefix);

    static BrowserEcosystemTypeFindPattern ProjectPattern(
        TypeFindPattern pattern) =>
        pattern switch
        {
            TypeFindPattern.Ordinary ordinary => new(
                ordinary.Ordinal,
                ordinary.Text,
                "Ordinary",
                ordinary.ExactNamespaceFallback,
                Namespace: null,
                NamespaceMatch: null),
            TypeFindPattern.Glob glob => new(
                glob.Ordinal,
                glob.Text,
                "Glob",
                ExactNamespaceFallback: null,
                Namespace: null,
                NamespaceMatch: null),
            TypeFindPattern.Namespace @namespace => new(
                @namespace.Ordinal,
                @namespace.Text,
                "Namespace",
                ExactNamespaceFallback: null,
                @namespace.NamespaceName,
                @namespace.Match.ToString()),
            _ => throw new InvalidOperationException(
                "Type Find returned an unsupported pattern."),
        };

    static BrowserEcosystemTypeFindSource ProjectSource(
        FindSourceIdentity source)
    {
        ExactLibrarySourceCoordinate coordinate = source.Coordinate;
        AssemblyReferenceIdentity library = source.AssemblyIdentity;
        (string kind, string label, string? packageId,
            string? packageVersion, string? platformFamily) =
            coordinate switch
            {
                ExactLibrarySourceCoordinate.Package package => (
                    "Package",
                    $"{package.PackageCoordinate.PackageId}@"
                        + package.PackageCoordinate.Version,
                    package.PackageCoordinate.PackageId,
                    package.PackageCoordinate.Version,
                    (string?)null),
                ExactLibrarySourceCoordinate.Platform platform => (
                    "Platform",
                    platform.Population.Family.ToString(),
                    (string?)null,
                    (string?)null,
                    platform.Population.Family.ToString()),
                ExactLibrarySourceCoordinate.Project => (
                    "Project",
                    "Project",
                    (string?)null,
                    (string?)null,
                    (string?)null),
                ExactLibrarySourceCoordinate.Local => (
                    "Local",
                    "Local",
                    (string?)null,
                    (string?)null,
                    (string?)null),
                _ => throw new InvalidOperationException(
                    "Type Find returned an unsupported source."),
            };
        return new(
            kind,
            label,
            packageId,
            packageVersion,
            platformFamily,
            new(
                library.Name,
                library.Version?.ToString()
                    ?? throw new InvalidOperationException(
                        "An exact Type source has no assembly version."),
                library.Culture,
                library.PublicKeyToken),
            ProjectProvenance(source.Selection),
            source.PackageRequest?.RequestedTargetFramework,
            source.PackageRequest?.RequestedRuntimeIdentifier,
            source.ContextOrder,
            source.MemberOrder);
    }

    static BrowserEcosystemTypeFindProvenance ProjectProvenance(
        AssemblyResolutionProvenance provenance) =>
        provenance switch
        {
            AssemblyResolutionProvenance.PackageAsset package => new(
                "Package",
                package.PackageId,
                package.PackageVersion,
                package.Tfm,
                package.Rid,
                package.AssetPath,
                Framework: null,
                FrameworkVersion: null,
                Project: null,
                ResolverSource: null,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.PlatformAsset platform => new(
                "Platform",
                PackageId: null,
                PackageVersion: null,
                TargetFramework: null,
                RuntimeIdentifier: null,
                AssetPath: null,
                platform.Framework,
                platform.FrameworkVersion,
                Project: null,
                platform.ResolverSource,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.ProjectAsset project => new(
                "Project",
                PackageId: null,
                PackageVersion: null,
                project.Tfm,
                project.Rid,
                AssetPath: null,
                Framework: null,
                FrameworkVersion: null,
                project.Project,
                ResolverSource: null,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.LocalAsset local => new(
                "Local",
                PackageId: null,
                PackageVersion: null,
                TargetFramework: null,
                RuntimeIdentifier: null,
                AssetPath: null,
                Framework: null,
                FrameworkVersion: null,
                Project: null,
                local.ResolverSource,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.DesignatedAsset designated => new(
                "Designated",
                PackageId: null,
                PackageVersion: null,
                TargetFramework: null,
                RuntimeIdentifier: null,
                AssetPath: null,
                Framework: null,
                FrameworkVersion: null,
                Project: null,
                designated.ResolverSource,
                ContentReference: null,
                Digest: null,
                DeclaredName: null),
            AssemblyResolutionProvenance.EmbeddedAsset embedded => new(
                "Embedded",
                PackageId: null,
                PackageVersion: null,
                TargetFramework: null,
                RuntimeIdentifier: null,
                AssetPath: null,
                Framework: null,
                FrameworkVersion: null,
                Project: null,
                ResolverSource: null,
                embedded.ContentRef,
                embedded.Digest,
                embedded.DeclaredName),
            _ => throw new InvalidOperationException(
                "Type Find returned unsupported assembly provenance."),
        };
}

[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    static readonly BrowserManagedOperationBridge EcosystemTypeFindOperations =
        new();

    [JSExport]
    public static string CancelEcosystemTypeFind(
        string operationId,
        string reason)
    {
        BrowserEcosystemTypeFindCancellation result =
            BrowserEcosystemTypeFindCancellation.From(
                EcosystemTypeFindOperations.RequestCancellation(
                    BrowserManagedOperationId.From(operationId),
                    BrowserManagedOperationCancelReasons.Parse(reason)));
        return JsonSerializer.Serialize(
            result,
            BrowserEcosystemTypeFindJsonContext.Default
                .BrowserEcosystemTypeFindCancellation);
    }

    [JSExport]
    public static async Task<string> FindEcosystemTypes(
        string operationId,
        string retainedDefinitionId,
        string realizationId,
        int resultGeneration,
        string requestJson,
        JSObject eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        BrowserEcosystemTypeFindRequest request =
            JsonSerializer.Deserialize(
                requestJson,
                BrowserEcosystemTypeFindJsonContext.Default
                    .BrowserEcosystemTypeFindRequest)
            ?? throw new ArgumentException(
                "An Ecosystem Type Find request is required.",
                nameof(requestJson));

        BrowserManagedOperationResult<
            BrowserEcosystemTypeFindInspection,
            string,
            string> operation =
            await EcosystemTypeFindOperations.RunAsync<
                BrowserEcosystemTypeFindInspection,
                string,
                string,
                BrowserEcosystemTypeFindEvent>(
                    BrowserManagedOperationId.From(operationId),
                    findEvent => eventSink.SetProperty(
                        "event",
                        JsonSerializer.Serialize(
                            findEvent,
                            BrowserEcosystemTypeFindJsonContext.Default
                                .BrowserEcosystemTypeFindEvent)),
                    async (token, events) =>
                    {
                        BrowserEcosystemFindIndexAssetLoad assets =
                            await BrowserEcosystemFindIndexAssets.LoadAsync(
                                    RequestedIndexEcosystems(request))
                                .ConfigureAwait(false);
                        BrowserEcosystemTypeFindInspection inspection =
                            await BrowserEcosystemTypeFindOperation.ExecuteAsync(
                                    BrowserRetainedWorkspaceActivationService
                                        .Owner,
                                    retainedDefinitionId,
                                    realizationId,
                                    resultGeneration,
                                    request,
                                    assets,
                                    events.Report,
                                    token)
                                .ConfigureAwait(false);
                        return new BrowserManagedOperationBodyResult<
                            BrowserEcosystemTypeFindInspection,
                            string,
                            string>.Succeeded(inspection);
                    },
                    exception => new(
                        exception.Message,
                        exception.ToString()))
                .ConfigureAwait(false);

        return JsonSerializer.Serialize(
            BrowserEcosystemTypeFindResult.From(operation),
            BrowserEcosystemTypeFindJsonContext.Default
                .BrowserEcosystemTypeFindResult);
    }

    static ImmutableArray<EcosystemPackId> RequestedIndexEcosystems(
        BrowserEcosystemTypeFindRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dictionary<string, EcosystemPackId> supported =
            BrowserEcosystemFindIndexAssets.SupportedEcosystems.ToDictionary(
                static ecosystem => ecosystem.Value,
                StringComparer.Ordinal);
        return
        [
            .. request.Ecosystems
                .Where(supported.ContainsKey)
                .Select(ecosystem => supported[ecosystem])
                .Distinct(),
        ];
    }
}

[SupportedOSPlatform("browser")]
internal static class BrowserEcosystemTypeFindOperation
{
    internal static async ValueTask<BrowserEcosystemTypeFindInspection>
        ExecuteAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string retainedDefinitionId,
            string realizationId,
            long resultGeneration,
            BrowserEcosystemTypeFindRequest input,
            BrowserEcosystemFindIndexAssetLoad assets,
            Action<BrowserEcosystemTypeFindEvent> emit,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentOutOfRangeException.ThrowIfNegative(resultGeneration);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(emit);

        WorkspaceRealizationOperationAdmission admission =
            await owner.EnterOperationAsync(
                    retainedDefinitionId,
                    realizationId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            throw new InvalidOperationException(
                "The requested retained Workspace realization is unavailable.");
        }

        using WorkspaceRealizationOperationLease operation = admitted.Lease;
        EcosystemFindSearchRequest request = CreateRequest(operation, input);
        var pending = new PendingEventSink();
        var results = new EcosystemFindResultSession(
            request.ResultRows,
            pending);
        var activations =
            new Dictionary<string, BrowserEcosystemTypeFindActivation>(
                StringComparer.Ordinal);
        BrowserTypeFindExecutionResult? resident = null;

        async Task<BrowserTypeFindExecutionResult> ResidentAsync()
        {
            if (resident is not null)
                return resident;
            pending.Progress(
                BrowserEcosystemTypeFindPhase.Bounded,
                "retained Workspace");
            resident = await owner.FindSemanticTypesAsync(
                    retainedDefinitionId,
                    realizationId,
                    (TypeFindQuestion)request.Question,
                    resultGeneration,
                    cancellationToken)
                .ConfigureAwait(false);
            RecordResidentActivations(resident, activations);
            return resident;
        }

        using var session =
            new EcosystemFindSearchSession<BrowserEcosystemTypeFindBlock>(
                request,
                async (layer, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    BrowserTypeFindExecutionResult execution =
                        await ResidentAsync().ConfigureAwait(false);
                    return ResidentBlock(request, layer, execution);
                },
                (prefix, take, token) =>
                    EnumeratePrefixAsync(prefix, take, pending, token),
                async (candidate, _, token) =>
                    await EvaluatePrefixCandidateAsync(
                            request,
                            input.TargetFramework,
                            candidate,
                            activations,
                            pending,
                            token)
                        .ConfigureAwait(false),
                publish: block =>
                {
                    if (block.Failure is { } failure)
                    {
                        pending.Failure(
                            block.Candidate is null
                                ? "Bounded"
                                : "Prefix",
                            block.Candidate?.PackageId
                                ?? block.Ecosystem?.Value
                                ?? "unknown",
                            failure);
                    }
                },
                indexed: (remaining, token) =>
                    EvaluateIndexedAsync(
                        owner,
                        retainedDefinitionId,
                        realizationId,
                        resultGeneration,
                        request,
                        input.Text,
                        remaining,
                        assets,
                        activations,
                        pending,
                        token),
                results: results);

        EcosystemFindBoundedOutcome<BrowserEcosystemTypeFindBlock> bounded =
            await session.RunBoundedAsync(
                    pauseBeforeCompletion: true,
                    cancellationToken)
                .ConfigureAwait(false);
        EcosystemFindSearchSummary<BrowserEcosystemTypeFindBlock> summary;
        if (bounded.Completed is { } completed)
        {
            summary = completed;
        }
        else
        {
            pending.Progress(
                BrowserEcosystemTypeFindPhase.Workspace,
                "retained Workspace");
            BrowserTypeFindExecutionResult execution =
                await ResidentAsync().ConfigureAwait(false);
            PublishWorkspaceFindings(
                request,
                execution,
                results,
                cancellationToken);
            summary = await bounded.Continuation!.ResumeAsync(
                    results.RemainingCandidateCapacity,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        EcosystemFindResult settlement =
            summary.Result
            ?? throw new InvalidOperationException(
                "Ecosystem Type Find completed without a result settlement.");
        pending.Emit(
            activations,
            emit,
            cancellationToken);
        return new(
            request.Identity,
            BrowserEcosystemTypeFindSettlement.From(settlement));
    }

    static EcosystemFindSearchRequest CreateRequest(
        WorkspaceRealizationOperationLease operation,
        BrowserEcosystemTypeFindRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Text))
            throw new ArgumentException(
                "Ecosystem Type Find text must not be empty.",
                nameof(input));
        if (input.Ecosystems is null
            || input.Ecosystems.Length == 0
            || input.PrefixDemand is null)
        {
            throw new ArgumentException(
                "Ecosystem Type Find requires explicit Ecosystem selection.",
                nameof(input));
        }
        if (input.RowStart <= 0 || input.RowEnd < input.RowStart)
            throw new ArgumentOutOfRangeException(nameof(input));

        ImmutableArray<WorkspaceRegistration.Ecosystem> registrations =
        [
            .. operation.Definition.Plan.Registrations
                .OfType<WorkspaceRegistration.Ecosystem>(),
        ];
        WorkspaceEcosystemRegistrationId[] selected =
        [
            .. input.Ecosystems.Select(
                WorkspaceEcosystemRegistrationId.Create),
        ];
        EcosystemFindLayer[] layers =
        [
            .. selected.Select(id =>
            {
                WorkspaceRegistration.Ecosystem registration =
                    registrations.SingleOrDefault(candidate =>
                        candidate.Declaration.Id == id)
                    ?? throw new ArgumentException(
                        $"Ecosystem '{id.Value}' is not admitted by the active Workspace.",
                        nameof(input));
                return new EcosystemFindLayer(
                    registration.Declaration,
                    registration.Declaration.Populations.Any(static population =>
                        population
                            is WorkspaceEcosystemPopulationDeclaration.Platform));
            }),
        ];
        TypeFindQuestion question =
            TypeFindQuestion.Create(
                [input.Text],
                FindVisibility.Public);
        RowSelectionIntent<string> rows =
            RowSelectionIntent<string>.Create(
            [
                input.RowStart == 1
                    ? RowSelectionIntentOperation<string>.Head(input.RowEnd)
                    : RowSelectionIntentOperation<string>.Window(
                        input.RowStart,
                        input.RowEnd),
            ]);
        FindQueryPlanResult resolution =
            FindQuery.ResolveIntent(
                FindQueryRouteKind.TypeResults,
                PortableQueryIntent.Create(
                    [],
                    [],
                    PortableQueryRowSelection.ToStages(rows),
                    []));
        FindQueryPlan plan =
            resolution is FindQueryPlanResult.Accepted accepted
                ? accepted.Plan
                : throw new ArgumentException(
                    "Ecosystem Type Find row selection was rejected.",
                    nameof(input));
        FindResultRowPlan resultRows =
            FindQuery.AssociateResultRows(
                plan,
                question,
                TypeFindResultScope.TypeOnly);
        return new(
            question,
            layers,
            resultRows,
            input.PrefixDemand.Select(
                WorkspaceEcosystemRegistrationId.Create),
            input.MaximumPrefixPackages);
    }

    static async Task<EcosystemFindIndexedOutcome<
        BrowserEcosystemTypeFindBlock>> EvaluateIndexedAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string retainedDefinitionId,
            string realizationId,
            long resultGeneration,
            EcosystemFindSearchRequest request,
            string text,
            int? remaining,
            BrowserEcosystemFindIndexAssetLoad assets,
            Dictionary<string, BrowserEcosystemTypeFindActivation> activations,
            PendingEventSink pending,
            CancellationToken cancellationToken)
    {
        pending.Progress(
            BrowserEcosystemTypeFindPhase.Indexed,
            "checked-in indexes");
        if (assets is BrowserEcosystemFindIndexAssetLoad.Invalid invalid)
        {
            pending.Failure(
                "Indexed",
                "checked-in indexes",
                invalid.Reason);
            return new(
                [],
                EcosystemFindIndexedCompletion.Exhausted,
                [new("Indexed", "checked-in indexes", invalid.Reason)]);
        }

        HashSet<string> selected =
            request.Layers.Select(layer => layer.Registration.Id.Value)
                .ToHashSet(StringComparer.Ordinal);
        EcosystemFindIndexArtifact[] artifacts =
        [
            .. ((BrowserEcosystemFindIndexAssetLoad.Available)assets)
                .Artifacts
                .Where(artifact => selected.Contains(
                    artifact.Ecosystem.Value)),
        ];
        BrowserIndexedTypeFindExecutionResult execution =
            await BrowserRetainedWorkspaceActivationService
                .FindIndexedTypesAsync(
                    owner,
                    retainedDefinitionId,
                    realizationId,
                    artifacts,
                    text,
                    remaining ?? request.ResultRows.DelegatedInputRows!.End,
                    resultGeneration,
                    cancellationToken)
                .ConfigureAwait(false);
        if (execution
            is not BrowserIndexedTypeFindExecutionResult.Completed completed)
        {
            string failure = execution switch
            {
                BrowserIndexedTypeFindExecutionResult.Rejected rejected =>
                    rejected.Reason,
                BrowserIndexedTypeFindExecutionResult.Unavailable unavailable =>
                    unavailable.Reason,
                BrowserIndexedTypeFindExecutionResult.Stale stale =>
                    stale.Reason,
                _ => "Indexed Type Find returned an unsupported result.",
            };
            pending.Failure(
                "Indexed",
                "checked-in indexes",
                failure);
            return new(
                [],
                EcosystemFindIndexedCompletion.Exhausted,
                [new("Indexed", "checked-in indexes", failure)]);
        }

        EcosystemFindIndexedIntegrationResult.Type find =
            completed.Result.Find.Content;
        foreach (BrowserIndexedTypeFindActivationAssociation activation
            in completed.Result.Activations)
        {
            if (activation.Identity is { } identity)
            {
                activations.TryAdd(
                    identity,
                    new(
                        Status(activation.Status),
                        activation.Action,
                        activation.Reason));
            }
        }

        EcosystemFindFinding[] findings =
        [
            .. find.Matches.Select(match => EcosystemFindFinding.Create(
                match,
                RequireMemberships(request, match))),
        ];
        bool incomplete = find.Coverage.Any(static coverage =>
            !coverage.Gaps.IsEmpty);
        var block = new EcosystemFindBlock<
            BrowserEcosystemTypeFindBlock>(
                new(find.Matches),
                findings.Length,
                incomplete,
                incomplete)
        {
            Findings = findings,
        };
        EcosystemFindSourceFailure[] failures =
        [
            .. find.Coverage
                .Where(static coverage => !coverage.Gaps.IsEmpty)
                .Select(coverage => new EcosystemFindSourceFailure(
                    "Indexed",
                    $"{coverage.Source.Ecosystem}/"
                        + $"{coverage.Source.Prefix.Prefix}",
                    string.Join(
                        "; ",
                        coverage.Gaps.Select(static gap =>
                            $"{gap.Kind}: {gap.Detail}")))),
        ];
        foreach (EcosystemFindSourceFailure failure in failures)
        {
            pending.Failure(
                failure.Phase,
                failure.Source,
                failure.Message);
        }
        return new(
            [block],
            find.Completion
                is EcosystemFindIndexedIntegrationCompletion.RowLimitReached
                    ? EcosystemFindIndexedCompletion.RowLimitReached
                    : EcosystemFindIndexedCompletion.Exhausted,
            failures)
        {
            AdmittedIndexes =
            [
                .. request.Layers
                    .Where(layer => artifacts.Any(artifact =>
                        artifact.Ecosystem.Value
                            == layer.Registration.Id.Value))
                    .Select(layer => layer.Registration.Id),
            ],
        };
    }

    static EcosystemFindBlock<BrowserEcosystemTypeFindBlock> ResidentBlock(
        EcosystemFindSearchRequest request,
        EcosystemFindLayer layer,
        BrowserTypeFindExecutionResult execution)
    {
        if (execution
            is not BrowserTypeFindExecutionResult.Completed completed)
        {
            string failure = execution switch
            {
                BrowserTypeFindExecutionResult.Rejected rejected =>
                    rejected.Reason,
                BrowserTypeFindExecutionResult.Unavailable unavailable =>
                    unavailable.Reason,
                BrowserTypeFindExecutionResult.Stale stale =>
                    stale.Reason,
                _ => "Type Find returned an unsupported result.",
            };
            return new(
                BrowserEcosystemTypeFindBlock.Empty,
                RowCount: 0,
                HasFailures: true,
                Incomplete: true)
            {
                Failure = failure,
            };
        }

        TypeFindSemanticMatch[] matches =
        [
            .. completed.Result.SemanticMatches.Where(match =>
                request.MembershipsFor(match.Declaration.Source.Coordinate)
                    .Any(membership =>
                        membership.Ecosystem == layer.Registration.Id)),
        ];
        EcosystemFindFinding[] findings =
        [
            .. matches.Select(match => EcosystemFindFinding.Create(
                match,
                RequireMemberships(request, match))),
        ];
        bool incomplete = ResidentIncomplete(completed);
        return new(
            new([.. matches]),
            findings.Length,
            incomplete,
            incomplete)
        {
            Findings = findings,
            Failure = incomplete
                ? "Type Find completed with incomplete retained Workspace coverage."
                : null,
        };
    }

    static void PublishWorkspaceFindings(
        EcosystemFindSearchRequest request,
        BrowserTypeFindExecutionResult execution,
        EcosystemFindResultSession results,
        CancellationToken cancellationToken)
    {
        if (execution
            is not BrowserTypeFindExecutionResult.Completed completed)
            return;
        foreach (TypeFindSemanticMatch match
            in completed.Result.SemanticMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.MembershipsFor(
                    match.Declaration.Source.Coordinate).IsEmpty)
            {
                continue;
            }
            if (results.Publish(
                    EcosystemFindFinding.CreateWorkspace(match))
                is EcosystemFindFindingPublication.RowLimitReached)
            {
                break;
            }
        }
    }

    static async IAsyncEnumerable<EcosystemFindPrefixPage>
        EnumeratePrefixAsync(
            PackagePrefixDeclaration prefix,
            int take,
            PendingEventSink pending,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
    {
        pending.Progress(
            BrowserEcosystemTypeFindPhase.Prefix,
            prefix.Prefix);
        await foreach (PackageSourceOperationResult<PackageSearchResult> page
            in BrowserPackageWorkspace.Gallery.SearchByPrefixPagesAsync(
                prefix.Prefix,
                take,
                prerelease: false,
                cancellationToken))
        {
            if (page.Failure is { } failure)
            {
                pending.Failure(
                    "Prefix",
                    prefix.Prefix,
                    failure.Message);
                yield return new(
                    [],
                    EcosystemFindPrefixPageCompletion.Failed,
                    failure.Message,
                    failure.Source.Producer.Display.ToString());
                yield break;
            }

            PackageSearchResult value = page.Value!;
            yield return new(
                [
                    .. value.Matches.Select(match =>
                        new EcosystemFindCandidate(
                            match.Metadata.Id,
                            match.Metadata.Version)),
                ],
                value.TruncationReason switch
                {
                    PackageSearchTruncationReason.None =>
                        EcosystemFindPrefixPageCompletion.Exhausted,
                    PackageSearchTruncationReason.RequestedLimit =>
                        EcosystemFindPrefixPageCompletion.RequestedLimit,
                    PackageSearchTruncationReason.SourcePageLimit =>
                        EcosystemFindPrefixPageCompletion.SourcePageLimit,
                    PackageSearchTruncationReason.ClientPageLimit =>
                        EcosystemFindPrefixPageCompletion.ClientPageLimit,
                    _ => throw new InvalidOperationException(
                        "Package prefix search returned an unknown completion."),
                });
        }
    }

    static async Task<EcosystemFindBlock<BrowserEcosystemTypeFindBlock>>
        EvaluatePrefixCandidateAsync(
            EcosystemFindSearchRequest request,
            string? targetFramework,
            EcosystemFindCandidate candidate,
            Dictionary<string, BrowserEcosystemTypeFindActivation> activations,
            PendingEventSink pending,
            CancellationToken cancellationToken)
    {
        BrowserPackageAcquisitionResult acquisition =
            await BrowserPackageWorkspace.AcquireWithSettlementAsync(
                    candidate.PackageId,
                    candidate.Version,
                    cancellationToken)
                .ConfigureAwait(false);
        if (acquisition is not BrowserPackageAcquisitionResult.Acquired acquired)
        {
            string failure =
                ((BrowserPackageAcquisitionResult.NotSettled)acquisition)
                    .VersionSettlement.Content.ToString()
                ?? "Package acquisition did not settle.";
            return new(
                BrowserEcosystemTypeFindBlock.Empty,
                RowCount: 0,
                HasFailures: true,
                Incomplete: true)
            {
                Failure = failure,
            };
        }

        await using BrowserScopeLease<BrowserInspectionScope> scope =
            await BrowserPackageWorkspace.OpenScopeAsync(
                    acquired.Acquisition.Package,
                    targetFramework,
                    cancellationToken)
                .ConfigureAwait(false);
        TypeDeclarationLocatorResult locator =
            scope.Scope.UseSurfaceDeclarationContext(
                (workspace, context) =>
                {
                    WorkspaceDeclarationPopulationCapture capture =
                        workspace.CaptureDeclarationPopulation([context]);
                    WorkspaceDeclarationPopulation population =
                        capture
                            is WorkspaceDeclarationPopulationCapture.Captured
                                captured
                            ? captured.Population
                            : throw new InvalidOperationException(
                                "The acquired Package declaration population "
                                    + "could not be captured.");
                    return TypeDeclarationLocatorQuery.Execute(
                        population,
                        [new TypeDeclarationLocatorRequest.Semantic(
                            (TypeFindQuestion)request.Question)],
                        includeAll: true,
                        cancellationToken: cancellationToken);
                });
        TypeFindSemanticPopulation population =
            TypeFindSourceEvaluator.EvaluateLocatorMatches(
                (TypeFindQuestion)request.Question,
                locator);
        TypeFindBlock block =
            FindSemanticReducer.ReduceType(
                (TypeFindQuestion)request.Question,
                population);
        EcosystemFindFinding[] findings =
        [
            .. block.Matches.Select(match =>
            {
                string identity =
                    BrowserSpotlightTypeIdentity.TryCreate(match)
                    ?? throw new InvalidOperationException(
                        "A prefix Type finding has no stable Browser identity.");
                activations.TryAdd(
                    identity,
                    new(
                        BrowserEcosystemTypeFindActivationStatus.Unavailable,
                        Action: null,
                        "Live prefix Type activation is not yet published."));
                return EcosystemFindFinding.Create(
                    match,
                    RequireMemberships(request, match));
            }),
        ];
        bool incomplete =
            !block.IsSourceCoverageComplete
            || !block.PopulationGaps.IsEmpty;
        return new(
            new(block.Matches),
            findings.Length,
            incomplete,
            incomplete)
        {
            Findings = findings,
            Failure = incomplete
                ? string.Join(
                    "; ",
                    block.PopulationGaps.Select(static gap =>
                        $"{gap.Kind}: {gap.Detail}"))
                : null,
        };
    }

    static ImmutableArray<EcosystemFindMembership> RequireMemberships(
        EcosystemFindSearchRequest request,
        TypeFindSemanticMatch match)
    {
        ImmutableArray<EcosystemFindMembership> memberships =
            request.MembershipsFor(match.Declaration.Source.Coordinate);
        if (memberships.IsEmpty)
        {
            throw new InvalidOperationException(
                "An Ecosystem Type finding has no selected membership.");
        }
        return memberships;
    }

    static void RecordResidentActivations(
        BrowserTypeFindExecutionResult execution,
        Dictionary<string, BrowserEcosystemTypeFindActivation> activations)
    {
        if (execution
            is not BrowserTypeFindExecutionResult.Completed completed)
            return;
        foreach (BrowserTypeFindCandidateActivation activation
            in completed.Result.Activations)
        {
            if (activation.Identity is { } identity)
            {
                activations.TryAdd(
                    identity,
                    new(
                        Status(activation.Status),
                        activation.Action,
                        activation.Reason));
            }
        }
    }

    static bool ResidentIncomplete(
        BrowserTypeFindExecutionResult.Completed completed) =>
        completed.Result.Find.Content
            is TypeDeclarationLocatorSectionResult.Evaluated evaluated
        && (!evaluated.IsSuccess
            || evaluated.Contexts.Any(context =>
                !context.IsRealized || !context.Failures.IsEmpty)
            || evaluated.Members.Any(member => !member.IsComplete));

    static BrowserEcosystemTypeFindActivationStatus Status(
        BrowserTypeFindActivationStatus status) =>
        status switch
        {
            BrowserTypeFindActivationStatus.Available =>
                BrowserEcosystemTypeFindActivationStatus.Available,
            BrowserTypeFindActivationStatus.Unavailable =>
                BrowserEcosystemTypeFindActivationStatus.Unavailable,
            BrowserTypeFindActivationStatus.Stale =>
                BrowserEcosystemTypeFindActivationStatus.Stale,
            BrowserTypeFindActivationStatus.Ambiguous =>
                BrowserEcosystemTypeFindActivationStatus.Ambiguous,
            BrowserTypeFindActivationStatus.Refused =>
                BrowserEcosystemTypeFindActivationStatus.Refused,
            BrowserTypeFindActivationStatus.Failed =>
                BrowserEcosystemTypeFindActivationStatus.Failed,
            _ => throw new InvalidOperationException(
                "Type Find returned an unsupported activation status."),
        };

    sealed record BrowserEcosystemTypeFindBlock(
        ImmutableArray<TypeFindSemanticMatch> Matches)
    {
        internal static BrowserEcosystemTypeFindBlock Empty { get; } =
            new([]);
    }

    sealed class PendingEventSink : IEcosystemFindFindingSink
    {
        readonly List<PendingEvent> _events = [];

        public void Publish(EcosystemFindFinding finding) =>
            _events.Add(new PendingEvent.Finding(finding));

        internal void Progress(
            BrowserEcosystemTypeFindPhase phase,
            string source) =>
            _events.Add(new PendingEvent.Progress(new(phase, source)));

        internal void Failure(
            string phase,
            string source,
            string message) =>
            _events.Add(new PendingEvent.Failure(
                new(phase, source, message)));

        internal void Emit(
            IReadOnlyDictionary<
                string,
                BrowserEcosystemTypeFindActivation> activations,
            Action<BrowserEcosystemTypeFindEvent> emit,
            CancellationToken cancellationToken)
        {
            foreach (PendingEvent pending in _events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                emit(pending switch
                {
                    PendingEvent.Progress progress =>
                        new(
                            BrowserEcosystemTypeFindEventKind.Progress,
                            progress.Value,
                            Finding: null,
                            ItemFailure: null),
                    PendingEvent.Failure failure =>
                        new(
                            BrowserEcosystemTypeFindEventKind.ItemFailure,
                            Progress: null,
                            Finding: null,
                            failure.Value),
                    PendingEvent.Finding finding =>
                        new(
                            BrowserEcosystemTypeFindEventKind.Finding,
                            Progress: null,
                            Project(finding.Value, activations),
                            ItemFailure: null),
                    _ => throw new InvalidOperationException(
                        "Ecosystem Type Find retained an unknown event."),
                });
            }
        }

        static BrowserEcosystemTypeFindItem Project(
            EcosystemFindFinding finding,
            IReadOnlyDictionary<
                string,
                BrowserEcosystemTypeFindActivation> activations)
        {
            BrowserEcosystemTypeFindFinding projected =
                BrowserEcosystemTypeFindProjection.ProjectFinding(finding);
            BrowserEcosystemTypeFindActivation activation =
                activations.GetValueOrDefault(projected.Identity)
                ?? new(
                    BrowserEcosystemTypeFindActivationStatus.Unavailable,
                    Action: null,
                    "Type activation correspondence is unavailable.");
            return new(projected.Identity, projected, activation);
        }
    }

    abstract record PendingEvent
    {
        private PendingEvent() { }

        internal sealed record Progress(
            BrowserEcosystemTypeFindProgress Value) : PendingEvent;

        internal sealed record Finding(
            EcosystemFindFinding Value) : PendingEvent;

        internal sealed record Failure(
            BrowserEcosystemTypeFindItemFailure Value) : PendingEvent;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserEcosystemTypeFindRequest))]
[JsonSerializable(typeof(BrowserEcosystemTypeFindEvent))]
[JsonSerializable(typeof(BrowserEcosystemTypeFindResult))]
[JsonSerializable(typeof(BrowserEcosystemTypeFindCancellation))]
internal sealed partial class BrowserEcosystemTypeFindJsonContext :
    JsonSerializerContext;
