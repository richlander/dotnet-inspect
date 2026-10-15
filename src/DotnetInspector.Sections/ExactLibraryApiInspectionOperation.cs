using System.Collections.Immutable;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections;

public sealed record ExactLibraryApiInspectionExecution(
    InspectionEnvelope<ExactLibraryApiInspectionResult> Inspection,
    ApiSurface? Surface);

/// <summary>
/// Executes one exact package Library public-API inspection from either
/// compile-role or retained-entry authority and returns its detached terminal
/// envelope plus the declaration surface used by CLI rendering.
/// </summary>
public static class ExactLibraryApiInspectionOperation
{
    public static ApiSurfaceProjectionLimits DefaultLimits { get; } =
        new(
            maxParticipants: 1,
            maxTypes: 1_000_000,
            maxMembers: 1_000_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 1_000_000,
            maxMetadataRows: 10_000_000);

    public static Task<ExactLibraryApiInspectionExecution>
        ExecuteAsync(
            ExactLibraryApiInspectionRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            request,
            house,
            sourceOperation,
            DefaultLimits,
            cancellationToken);

    public static async Task<ExactLibraryApiInspectionExecution>
        ExecuteAsync(
            ExactLibraryApiInspectionRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            ApiSurfaceProjectionLimits projectionLimits,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        ArgumentNullException.ThrowIfNull(projectionLimits);
        PackageEndpointScopeRequest endpointRequest;
        try
        {
            if (request.TargetSelectionMode
                == PackageHouseTargetSelectionMode.OwnerDefault)
            {
                throw new ArgumentException(
                    "Cold exact Library API inspection requires an exact target framework.",
                    nameof(request));
            }

            PackageLibrarySelectionKind selectionKind =
                request.SelectionKind switch
                {
                    ExactLibraryApiSelectionKind.Query =>
                        PackageLibrarySelectionKind.Query,
                    ExactLibraryApiSelectionKind.AssetId =>
                        PackageLibrarySelectionKind.AssetId,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(request)),
                };
            var selector =
                new PackageLibrarySelector(
                    request.Library,
                    selectionKind);
            PackageLibraryInspectionDemandPlan demand =
                PackageLibraryInspectionDemandPlanner.Plan(
                    selector,
                    PackageLibraryInspectionRequirement.PublicApi);
            endpointRequest = new(
                PackageSourceCoordinate.Create(
                    request.PackageId,
                    request.PackageVersion),
                request.TargetFramework,
                demand.AssetDemand,
                librarySelector: demand.Selector);
        }
        catch
        {
            sourceOperation.Dispose();
            throw;
        }

        PackageEndpointScopeOutcome opened =
            await PackageEndpointScope.OpenAsync(
                    house,
                    sourceOperation,
                    endpointRequest,
                    cancellationToken)
            .ConfigureAwait(false);
        if (opened is PackageEndpointScopeOutcome.Failed failed)
        {
            if (failed.Contribution is { } contribution)
            {
                ExactLibraryApiQueryExecution unavailable =
                    ExactLibraryApiInspectionQuery
                        .ExecuteWithoutRealizedParticipant(
                            contribution.Binding,
                            request,
                            failed.Message);
                return PreserveLegacyProducer(
                    new(
                        Envelope(unavailable.Result, request),
                        unavailable.Surface),
                    failed.LegacyProducerKey);
            }
            return UnavailableExecution(request, failed.Message);
        }

        await using PackageEndpointScope scope =
            ((PackageEndpointScopeOutcome.Opened)opened).Scope;
        ArtifactRootResult<ExactLibraryApiInspectionExecution> execution =
            await scope.UsePackageAssemblyRolesAsync(
                    (_, root, realization, _) =>
                        ValueTask.FromResult(
                            Execute(
                                root,
                                realization,
                                request,
                                projectionLimits)),
                    cancellationToken)
                .ConfigureAwait(false);
        return execution switch
        {
            ArtifactRootResult<ExactLibraryApiInspectionExecution>.Available
                available =>
                PreserveLegacyProducer(
                    available.Value,
                    scope.Payload.LegacyProducerKey),
            ArtifactRootResult<ExactLibraryApiInspectionExecution>.Rejected
                rejected =>
                UnavailableExecution(
                    request,
                    "The admitted package Root rejected exact Library API "
                        + $"inspection: {rejected.Failure}"),
            _ => throw new InvalidOperationException(
                "Package endpoint execution returned an unknown result."),
        };
    }

    /// <summary>
    /// Executes under caller-retained package Root and assembly-role authority.
    /// </summary>
    public static ExactLibraryApiInspectionExecution Execute(
        PackageRootBinding package,
        PackageAssemblyContextRealization realization,
        ExactLibraryApiInspectionRequest request,
        ApiSurfaceProjectionLimits projectionLimits)
    {
        ExactLibraryApiQueryExecution execution =
            ExactLibraryApiInspectionQuery.Execute(
                package,
                realization,
                request,
                projectionLimits);
        return new ExactLibraryApiInspectionExecution(
            Envelope(execution.Result, request),
            execution.Surface);
    }

    /// <summary>
    /// Executes one exact retained Package tool entry without treating it as
    /// a compile-role asset.
    /// </summary>
    public static async Task<ExactLibraryApiInspectionExecution>
        ExecuteToolEntryAsync(
            PackageInspectionInput input,
            ExactLibraryApiInspectionRequest request,
            string selectedTargetFramework,
            PackageAssemblyContextRealizationOptions realizationOptions,
            ApiSurfaceProjectionLimits projectionLimits,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedTargetFramework);
        ArgumentNullException.ThrowIfNull(realizationOptions);
        ArgumentNullException.ThrowIfNull(projectionLimits);

        PackageInspectionSelection selection =
            input.SelectAssemblies(
                [
                    new(
                        request.Library,
                        selectedTargetFramework,
                        selectedTargetFramework),
                ]);
        await using var workspace = new InspectionWorkspace();
        using PackageInspectionAssemblyContext realization =
            await workspace.RealizePackageInspectionAsync(
                    selection,
                    options: realizationOptions,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        ExactLibraryApiQueryExecution execution =
            ExactLibraryApiInspectionQuery.ExecuteToolEntry(
                input,
                realization.Assemblies.Single(),
                request,
                projectionLimits);
        return new(
            Envelope(execution.Result, request),
            execution.Surface);
    }

    static InspectionEnvelope<ExactLibraryApiInspectionResult> Envelope(
        ExactLibraryApiInspectionResult result,
        ExactLibraryApiInspectionRequest request) =>
        new(
            result,
            ProjectShare(request, result),
            Diagnostics(result));

    static InspectionShare ProjectShare(
        ExactLibraryApiInspectionRequest request,
        ExactLibraryApiInspectionResult result)
    {
        string framework =
            request.TargetSelectionMode
                == PackageHouseTargetSelectionMode.OwnerDefault
                ? result.Asset?.TargetFramework ?? ""
                : request.TargetFramework;
        if (framework.Length == 0)
        {
            return new InspectionShare.NonProjectable(
                "exact-library-api-share/default-target",
                "An unavailable owner-default Library has no selected target framework to share.");
        }
        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                request.PackageId,
                request.PackageVersion,
                framework);
        const int schemaVersion = InspectionDefinitionSchema.Version1;
        var workspace = new WorkspaceDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework,
                    members: [coordinate]),
            ]);
        var navigation = new NavigationDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.NavigationId,
            [
                new NavigationTabDefinition(
                    "t0",
                    coordinate: coordinate),
            ],
            "t0");
        var view = new ViewDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.ViewId,
            lens: "overview",
            libraries: [result.Asset?.Id ?? request.Library]);
        var scenario = new ScenarioDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.ScenarioId,
            workspace: workspace.Id,
            context: "g0",
            view: view.Id,
            navigation: navigation.Id);
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                new WorkspaceSharePacketDefinitionSet(
                    workspace,
                    navigation,
                    view,
                    scenario));
        if (!projection.Succeeded)
        {
            WorkspaceSharePacketProjectionFailure failure =
                projection.Failure!;
            return new InspectionShare.NonProjectable(
                $"exact-library-api-share/{failure.Path}",
                failure.Message);
        }

        string encoded =
            WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new InspectionShare.Available(
            WorkspaceShareUrl.Create(encoded),
            encoded);
    }

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        ExactLibraryApiInspectionResult result)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        foreach (ExactLibraryApiInspectionFailure failure in result.Failures)
        {
            InspectionDiagnosticSeverity severity =
                failure.Kind
                    is ExactLibraryApiInspectionFailureKind.InspectionIncomplete
                    or ExactLibraryApiInspectionFailureKind.ProjectionTruncated
                    ? InspectionDiagnosticSeverity.Warning
                    : InspectionDiagnosticSeverity.Error;
            diagnostics.Add(
                new InspectionDiagnostic(
                    DiagnosticCode(failure.Kind),
                    severity,
                    failure.Detail,
                    failure.SubjectAssembly?.Name));
        }
        return diagnostics.DrainToImmutable();
    }

    static string DiagnosticCode(
        ExactLibraryApiInspectionFailureKind kind) =>
        kind switch
        {
            ExactLibraryApiInspectionFailureKind.ContextLoad =>
                "exact-library-api.context-load",
            ExactLibraryApiInspectionFailureKind.PackageMismatch =>
                "exact-library-api.package-mismatch",
            ExactLibraryApiInspectionFailureKind.CompileSelectionUnavailable =>
                "exact-library-api.compile-selection-unavailable",
            ExactLibraryApiInspectionFailureKind.LibraryNotFound =>
                "exact-library-api.not-found",
            ExactLibraryApiInspectionFailureKind.LibraryAmbiguous =>
                "exact-library-api.ambiguous",
            ExactLibraryApiInspectionFailureKind.ParticipantUnavailable =>
                "exact-library-api.participant-unavailable",
            ExactLibraryApiInspectionFailureKind.InspectionIncomplete =>
                "exact-library-api.inspection-incomplete",
            ExactLibraryApiInspectionFailureKind.ProjectionTruncated =>
                "exact-library-api.projection-truncated",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static ExactLibraryApiInspectionResult Unavailable(
        ExactLibraryApiInspectionRequest request,
        string detail) =>
        new(
            ExactLibraryApiInspectionOutcome.Unavailable,
            request.PackageId,
            request.PackageVersion,
            request.TargetFramework,
            request.Library,
            null,
            null,
            null,
            null,
            null,
            [
                new ExactLibraryApiInspectionFailure(
                    ExactLibraryApiInspectionFailureKind.ContextLoad,
                    detail),
            ],
            IsComplete: false);

    static ExactLibraryApiInspectionExecution UnavailableExecution(
        ExactLibraryApiInspectionRequest request,
        string detail) =>
        new(
            Envelope(Unavailable(request, detail), request),
            Surface: null);

    static ExactLibraryApiInspectionExecution PreserveLegacyProducer(
        ExactLibraryApiInspectionExecution execution,
        string? legacyProducerKey)
    {
        ExactLibraryApiInspectionResult content =
            execution.Inspection.Content;
        if (legacyProducerKey is null
            || content.Source is not { } source
            || source.Producer.Equals(
                legacyProducerKey,
                StringComparison.Ordinal))
        {
            return execution;
        }

        ExactLibraryApiInspectionResult compatible = content with
        {
            Source = source with
            {
                Producer = legacyProducerKey,
            },
        };
        return new(
            new InspectionEnvelope<ExactLibraryApiInspectionResult>(
                compatible,
                execution.Inspection.Share,
                execution.Inspection.Diagnostics),
            execution.Surface);
    }

}
