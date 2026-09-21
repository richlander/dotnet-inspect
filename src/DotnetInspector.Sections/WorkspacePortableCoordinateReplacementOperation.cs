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
    WorkspacePackageComponentPath Component,
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
    const string SharePath = "workspace/package-update";

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
            result.CoordinateRetention;
        NavigationConsumerCoordinateOutcome? retained =
            retention is null
                ? null
                : new(
                    retention.Disposition,
                    retention.Detail,
                    retention.LibraryPairing?.Status,
                    retention.TypeCorrespondence?.Status,
                    retention.MemberCorrespondence?.Status);
        NavigationConsumerLensOutcome? lens = snapshot?.LensOutcome;
        var content = new WorkspacePortableCoordinateReplacementOutcome(
            result.Succeeded,
            request.Component,
            result.Failure,
            Coordinate(retention?.Source),
            Coordinate(retention?.Destination),
            result.ScopeResult is { } scope
                ? NavigationConsumerScopeOutcome.FromSettlement(scope)
                : null,
            retained,
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
                $"workspace.package-update.{failure.Kind}",
                InspectionDiagnosticSeverity.Error,
                failure.Detail));
        }
        if (retained is not null
            && retained.Disposition
                != NavigationCoordinateRetentionDisposition.ExactPath)
        {
            diagnostics.Add(new(
                "workspace.package-update.fallback",
                InspectionDiagnosticSeverity.Warning,
                retained.Detail));
        }
        if (lens?.Kind is NavigationOutcomeKind.Unavailable or NavigationOutcomeKind.Failed)
        {
            diagnostics.Add(new(
                "workspace.package-update.inspector",
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
                $"workspace.package-update.navigation.{diagnostic.Kind}",
                InspectionDiagnosticSeverity.Warning,
                diagnostic.Message,
                diagnostic.Library));
        }
        return new(
            new ResourcePath(SharePath),
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
                InspectionPortableProjectionFailureReason.Unavailable,
                explanation: result.Failure!.Detail);
        }

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                result.Definitions, cancellationToken);
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
        try
        {
            string packet = WorkspaceSharePacketCodec.Encode(projection.Packet!);
            return new InspectionPortableProjection.Available(
                $"https://dotnet-inspect.net/?w={packet}", packet);
        }
        catch (WorkspaceSharePacketException failure)
        {
            return new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.Failed,
                explanation: failure.Message);
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
