using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Sections;

public sealed record WorkspaceReplacementCoordinate(
    string Package,
    string Version,
    string? Framework,
    string? RuntimeIdentifier);

public sealed record WorkspaceReplacementSubject(
    StructuralSubjectKind Kind,
    string Label);

public sealed record WorkspaceReplacementInspector(
    NavigationLensBasisKind Basis,
    string? RequestedFacet,
    string? EffectiveFacet,
    NavigationOutcomeKind Outcome,
    NavigationConsumerResolution? Resolution);

public sealed record WorkspacePortableCoordinateReplacementOutcome(
    bool Succeeded,
    string Navigation,
    WorkspacePortableCoordinateReplacementFailure? Failure,
    WorkspaceReplacementCoordinate? Source,
    WorkspaceReplacementCoordinate? Destination,
    NavigationConsumerScopeOutcome? Scope,
    NavigationConsumerCoordinateOutcome? Retention,
    NavigationOutcomeKind? NavigationOutcome,
    WorkspaceReplacementSubject? ActiveSubject,
    ImmutableArray<WorkspaceReplacementSubject> RetainedPath,
    WorkspaceReplacementInspector? Inspector);

public static class WorkspacePortableCoordinateReplacementOperation
{
    const string SharePath = "workspace/coordinate-replacement";

    public static async ValueTask<
        InspectionEnvelope<WorkspacePortableCoordinateReplacementOutcome>>
        ExecuteAsync(
            CommittedScenarioDefinitionSet definitions,
            WorkspacePackageCoordinateReplacementRequest request,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken = default)
    {
        WorkspacePortableCoordinateReplacementResult result =
            await WorkspacePortableCoordinateReplacement.ExecuteAsync(
                definitions, request, options, cancellationToken)
                .ConfigureAwait(false);
        NavigationConsumerResult? navigation = result.NavigationResult?.Consumer;
        NavigationConsumerSnapshot? snapshot = navigation?.Snapshot;
        NavigationCoordinateRetentionResult? retention =
            result.NavigationResult?.CoordinateRetention;
        NavigationConsumerLensOutcome? lens = snapshot?.LensOutcome;
        var content = new WorkspacePortableCoordinateReplacementOutcome(
            result.Succeeded,
            request.Navigation,
            result.Failure,
            Coordinate(retention?.Source),
            Coordinate(retention?.Destination),
            result.ScopeResult is { } scope
                ? NavigationConsumerScopeOutcome.FromSettlement(scope)
                : null,
            navigation?.Outcome.CoordinateRetention,
            navigation?.Outcome.Kind,
            snapshot is null ? null : Subject(snapshot.ActiveSubject),
            snapshot is null
                ? []
                : [.. snapshot.Hierarchy
                    .Where(static entry => entry.IsRetained && entry.Subject is not null)
                    .Select(static entry => Subject(entry.Subject!))],
            lens is null
                ? null
                : new(
                    lens.Basis,
                    lens.Request?.Facet,
                    lens.EffectiveLens?.Facet,
                    lens.Kind,
                    lens.Resolution));
        InspectionPortableProjection share = ProjectShare(result, cancellationToken);
        var diagnostics = ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        if (result.Failure is { } failure)
        {
            diagnostics.Add(new(
                $"workspace.coordinate-replacement.{failure.Kind}",
                InspectionDiagnosticSeverity.Error,
                failure.Detail));
        }
        if (navigation?.Outcome.CoordinateRetention is { } retained
            && retained.Disposition != NavigationCoordinateRetentionDisposition.ExactPath)
        {
            diagnostics.Add(new(
                "workspace.coordinate-replacement.fallback",
                InspectionDiagnosticSeverity.Warning,
                retained.Detail));
        }
        if (lens?.Kind is NavigationOutcomeKind.Unavailable or NavigationOutcomeKind.Failed)
        {
            diagnostics.Add(new(
                "workspace.coordinate-replacement.inspector",
                InspectionDiagnosticSeverity.Warning,
                lens.Resolution?.Message
                    ?? $"The retained inspector is {lens.Kind}."));
        }
        IEnumerable<NavigationConsumerDiagnostic> nativeDiagnostics =
            (snapshot?.Diagnostics ?? [])
                .Concat(navigation?.Outcome.Diagnostics ?? [])
                .Distinct();
        foreach (NavigationConsumerDiagnostic diagnostic in nativeDiagnostics)
        {
            diagnostics.Add(new(
                $"workspace.coordinate-replacement.navigation.{diagnostic.Kind}",
                InspectionDiagnosticSeverity.Warning,
                diagnostic.Message,
                diagnostic.Library));
        }
        return new(
            InspectionContentKind.Outcome,
            content,
            share,
            diagnostics.ToImmutable());
    }

    static WorkspaceReplacementSubject Subject(NavigationConsumerSubject subject) =>
        new(subject.Kind, subject.Label);

    static WorkspaceReplacementCoordinate? Coordinate(WorkspacePackageDescriptor? package) =>
        package is null
            ? null
            : new(package.PackageId, package.PackageVersion,
                package.RequestedTargetFramework, package.RuntimeIdentifier);

    static InspectionPortableProjection ProjectShare(
        WorkspacePortableCoordinateReplacementResult result,
        CancellationToken cancellationToken)
    {
        if (result.Definitions is null)
        {
            return new InspectionPortableProjection.NonProjectable(
                SharePath,
                InspectionPortableProjectionFailureReason.Unavailable,
                result.Failure!.Detail);
        }

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                result.Definitions, cancellationToken);
        if (!projection.Succeeded)
        {
            return new InspectionPortableProjection.NonProjectable(
                projection.Failure!.Path,
                projection.Failure.Kind
                    is WorkspaceSharePacketProjectionFailureKind
                        .InvalidDefinitionSet
                    ? InspectionPortableProjectionFailureReason.Invalid
                    : InspectionPortableProjectionFailureReason.NotSupported,
                projection.Failure.Message);
        }
        try
        {
            string packet = WorkspaceSharePacketCodec.Encode(projection.Packet!);
            return new InspectionPortableProjection.Available(
                $"https://dotnet-inspect.net/?w={packet}", packet);
        }
        catch (WorkspaceSharePacketException failure)
        {
            return new InspectionPortableProjection.NonProjectable(
                SharePath,
                InspectionPortableProjectionFailureReason.Failed,
                failure.Message);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WorkspacePortableCoordinateReplacementOutcome))]
[JsonSerializable(typeof(InspectionEnvelope<WorkspacePortableCoordinateReplacementOutcome>))]
public partial class WorkspacePortableCoordinateReplacementJsonContext
    : JsonSerializerContext;
