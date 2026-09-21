using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public sealed record ExactLibraryApiInspectionExecution(
    InspectionEnvelope<ExactLibraryApiInspectionResult> Inspection,
    ApiSurface? Surface);

/// <summary>
/// Executes one exact package Library public-API inspection and returns its
/// detached terminal envelope plus the declaration surface used by CLI
/// rendering.
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
            WorkspaceContextLoadOptions capabilities,
            CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            request,
            capabilities,
            DefaultLimits,
            cancellationToken);

    public static async Task<ExactLibraryApiInspectionExecution>
        ExecuteAsync(
            ExactLibraryApiInspectionRequest request,
            WorkspaceContextLoadOptions capabilities,
            ApiSurfaceProjectionLimits projectionLimits,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(projectionLimits);

        WorkspaceContextInput input = new()
        {
            Framework = request.TargetFramework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    request.PackageId,
                    request.PackageVersion,
                    request.TargetFramework),
            ],
        };
        var plan = new WorkspacePlan([], [input]);
        var workspace = new InspectionWorkspace(plan);
        ExactLibraryApiInspectionExecution result;
        try
        {
            WorkspacePackageRootAcquisitionOutcome acquisition =
                await WorkspaceContextLoader.AcquirePackageRootAsync(
                    input,
                    capabilities,
                    cancellationToken)
                .ConfigureAwait(false);
            if (acquisition
                is WorkspacePackageRootAcquisitionOutcome.Failed failed)
            {
                result = UnavailableExecution(
                    request,
                    string.Join(
                        Environment.NewLine,
                        failed.Failures.Select(failure => failure.Message)));
            }
            else
            {
                PackageRootBinding root =
                    ((WorkspacePackageRootAcquisitionOutcome.Acquired)
                        acquisition).Root;
                using PackageAssemblyContextRealization realization =
                    await workspace
                    .RealizePackageAssemblyContextRolesAsync(
                        root,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                result = Execute(
                    root,
                    realization,
                    request,
                    projectionLimits);
            }
        }
        catch (Exception failure)
        {
            await DirectWorkspaceOperationLifetime.CloseAfterFailureAsync(
                    workspace,
                    failure)
                .ConfigureAwait(false);
            throw;
        }

        await DirectWorkspaceOperationLifetime.CloseAsync(
                workspace,
                "Exact Library API inspection")
            .ConfigureAwait(false);
        return result;
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

    static InspectionEnvelope<ExactLibraryApiInspectionResult> Envelope(
        ExactLibraryApiInspectionResult result,
        ExactLibraryApiInspectionRequest request) =>
        new(
            new ResourcePath("exact-library-api"),
            InspectionContentKind.Result,
            result,
            ProjectShare(request, result),
            Diagnostics(result));

    static InspectionPortableProjection ProjectShare(
        ExactLibraryApiInspectionRequest request,
        ExactLibraryApiInspectionResult result)
    {
        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                request.PackageId,
                request.PackageVersion,
                request.TargetFramework);
        const int schemaVersion = InspectionDefinitionSchema.Version1;
        var workspace = new WorkspaceDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework: request.TargetFramework,
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
            return new InspectionPortableProjection.NonProjectable(
                failure.Kind
                    is WorkspaceSharePacketProjectionFailureKind
                        .InvalidDefinitionSet
                    ? InspectionPortableProjectionFailureReason.Invalid
                    : InspectionPortableProjectionFailureReason.NotSupported,
                location: failure.Path,
                explanation: failure.Message);
        }

        string encoded =
            WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new InspectionPortableProjection.Available(
            "https://dotnet-inspect.net/?w=" + encoded,
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

}
