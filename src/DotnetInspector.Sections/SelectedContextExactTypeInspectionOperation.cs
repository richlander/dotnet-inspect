using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Detached defining source for one exact Type resolved from a selected
/// Workspace context.
/// </summary>
public sealed record SelectedContextExactTypeSource
{
    public SelectedContextExactTypeSource(
        TypeDeclarationLocatorSectionCoordinate library,
        ExactTypeDefinitionIdentity type,
        TypeDeclarationLocatorObservation observation)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Type = type
            ?? throw new ArgumentNullException(nameof(type));
        Observation = observation
            ?? throw new ArgumentNullException(nameof(observation));
    }

    public TypeDeclarationLocatorSectionCoordinate Library { get; }

    public ExactTypeDefinitionIdentity Type { get; }

    public TypeDeclarationLocatorObservation Observation { get; }
}

/// <summary>
/// Live full Type inspection target valid while the paired Workspace
/// realization operation remains admitted.
/// </summary>
public sealed record SelectedContextExactTypeLiveTarget(
    ApiSurface Surface,
    ApiType Type,
    ResolvedAssemblyReference Assembly,
    AssemblyBindingOccurrence Occurrence,
    IAssemblyBindingPolicy BindingPolicy,
    string? AssemblyPath,
    string? PackageExtractPath)
{
    /// <summary>
    /// Opens the selected Package compile asset's XML companion within the
    /// caller-supplied expanded-byte limit, or returns <c>null</c> when absent.
    /// </summary>
    public Func<long, Stream?>? OpenCompiledDocumentation { get; init; }
}

/// <summary>
/// Exact Type content plus the owner-issued defining source identities needed
/// by aggregate hosts.
/// </summary>
public sealed record SelectedContextExactTypeInspectionResult
{
    public SelectedContextExactTypeInspectionResult(
        ExactTypeInspectionResult inspection,
        ImmutableArray<SelectedContextExactTypeSource> definingSources)
    {
        Inspection = inspection
            ?? throw new ArgumentNullException(nameof(inspection));
        DefiningSources =
            !definingSources.IsDefault
            && definingSources.All(static source => source is not null)
                ? definingSources
                : throw new ArgumentException(
                    "Defining sources must be an initialized immutable array.",
                    nameof(definingSources));
    }

    public ExactTypeInspectionResult Inspection { get; }

    public ImmutableArray<SelectedContextExactTypeSource> DefiningSources
    {
        get;
    }
}

/// <summary>
/// Resolves one exact Type across every participant in one admitted realized
/// Workspace context.
/// </summary>
public static class SelectedContextExactTypeInspectionOperation
{
    public static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        Execute(
            WorkspaceRealizationOperationLease authority,
            WorkspaceDeclarationContext context,
            SelectedContextExactTypeInspectionRequest request) =>
        ExecuteCore(
            authority,
            context,
            request,
            projectionLimits: null,
            activation: null,
            facet: null,
            scope: ApiSurfaceScope.PublicWithNonPublicTypes,
            liveTargetConsumer: null);

    public static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        Execute(
            WorkspaceRealizationOperationLease authority,
            WorkspaceDeclarationContext context,
            SelectedContextExactTypeInspectionRequest request,
            ApiSurfaceProjectionLimits projectionLimits)
    {
        ArgumentNullException.ThrowIfNull(projectionLimits);
        return ExecuteCore(
            authority,
            context,
            request,
            projectionLimits,
            activation: null,
            facet: null,
            scope: ApiSurfaceScope.PublicWithNonPublicTypes,
            liveTargetConsumer: null);
    }

    public static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        Execute(
            WorkspaceRealizationOperationLease authority,
            CompleteWorkspaceActivation activation,
            SelectedContextExactTypeInspectionRequest request,
            ViewFacetId? facet = null,
            ApiSurfaceScope scope =
                ApiSurfaceScope.PublicWithNonPublicTypes) =>
        ExecuteActivation(
            authority,
            activation,
            request,
            facet,
            scope,
            liveTargetConsumer: null);

    public static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExecuteWithLiveTarget(
            WorkspaceRealizationOperationLease authority,
            CompleteWorkspaceActivation activation,
            SelectedContextExactTypeInspectionRequest request,
            Action<SelectedContextExactTypeLiveTarget> liveTargetConsumer,
            ViewFacetId? facet = null,
            ApiSurfaceScope scope =
                ApiSurfaceScope.PublicWithNonPublicTypes)
    {
        ArgumentNullException.ThrowIfNull(liveTargetConsumer);
        return ExecuteActivation(
            authority,
            activation,
            request,
            facet,
            scope,
            liveTargetConsumer);
    }

    public static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExecuteWithLiveTarget(
            InspectionWorkspace workspace,
            CompleteWorkspaceActivation activation,
            SelectedContextExactTypeInspectionRequest request,
            Action<SelectedContextExactTypeLiveTarget> liveTargetConsumer,
            ViewFacetId? facet = null,
            ApiSurfaceScope scope =
                ApiSurfaceScope.PublicWithNonPublicTypes)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(liveTargetConsumer);
        return ExecuteActivation(
            workspace,
            activation,
            request,
            facet,
            scope,
            liveTargetConsumer);
    }

    static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExecuteActivation(
            WorkspaceRealizationOperationLease authority,
            CompleteWorkspaceActivation activation,
            SelectedContextExactTypeInspectionRequest request,
            ViewFacetId? facet,
            ApiSurfaceScope scope,
            Action<SelectedContextExactTypeLiveTarget>? liveTargetConsumer)
    {
        ArgumentNullException.ThrowIfNull(activation);
        if (activation.SelectedContext is not { } context)
        {
            ExactTypeInspectionResult unavailable =
                ExactTypeInspectionResult.RuntimeUnavailable(
                    request.Type,
                    "The restored Workspace has no selected context.");
            return new(
                new ResourcePath("exact-type"),
                InspectionContentKind.Result,
                new SelectedContextExactTypeInspectionResult(
                    unavailable,
                    []),
                new InspectionPortableProjection.NonProjectable(
                    InspectionPortableProjectionFailureReason.Unavailable,
                    location: "scenario.context"),
                ExactTypeInspectionOperation.Diagnostics(unavailable));
        }

        return ExecuteCore(
            authority,
            context,
            request,
            projectionLimits: null,
            activation,
            facet,
            scope,
            liveTargetConsumer);
    }

    static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExecuteActivation(
            InspectionWorkspace workspace,
            CompleteWorkspaceActivation activation,
            SelectedContextExactTypeInspectionRequest request,
            ViewFacetId? facet,
            ApiSurfaceScope scope,
            Action<SelectedContextExactTypeLiveTarget>? liveTargetConsumer)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(activation);
        if (activation.SelectedContext is not { } context)
        {
            ExactTypeInspectionResult unavailable =
                ExactTypeInspectionResult.RuntimeUnavailable(
                    request.Type,
                    "The restored Workspace has no selected context.");
            return new(
                new ResourcePath("exact-type"),
                InspectionContentKind.Result,
                new SelectedContextExactTypeInspectionResult(
                    unavailable,
                    []),
                new InspectionPortableProjection.NonProjectable(
                    InspectionPortableProjectionFailureReason.Unavailable,
                    location: "scenario.context"),
                ExactTypeInspectionOperation.Diagnostics(unavailable));
        }

        return ExecuteCore(
            workspace,
            context,
            request,
            projectionLimits: null,
            activation,
            facet,
            scope,
            liveTargetConsumer);
    }

    static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExecuteCore(
            WorkspaceRealizationOperationLease authority,
            WorkspaceDeclarationContext context,
            SelectedContextExactTypeInspectionRequest request,
            ApiSurfaceProjectionLimits? projectionLimits,
            CompleteWorkspaceActivation? activation,
            ViewFacetId? facet,
            ApiSurfaceScope scope,
            Action<SelectedContextExactTypeLiveTarget>? liveTargetConsumer)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        using WorkspaceRealizationOperationUse operation =
            authority.EnterUse();

        return Complete(
            ExactTypeInspectionQuery.ExecuteSelectedContext(
                operation.Workspace,
                context,
                request,
                scope,
                projectionLimits),
            context,
            request,
            activation,
            facet,
            liveTargetConsumer);
    }

    static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExecuteCore(
            InspectionWorkspace workspace,
            WorkspaceDeclarationContext context,
            SelectedContextExactTypeInspectionRequest request,
            ApiSurfaceProjectionLimits? projectionLimits,
            CompleteWorkspaceActivation? activation,
            ViewFacetId? facet,
            ApiSurfaceScope scope,
            Action<SelectedContextExactTypeLiveTarget>? liveTargetConsumer)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        return Complete(
            ExactTypeInspectionQuery.ExecuteSelectedContext(
                workspace,
                context,
                request,
                scope,
                projectionLimits),
            context,
            request,
            activation,
            facet,
            liveTargetConsumer);
    }

    static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        Complete(
            ExactTypeInspectionExecution execution,
            WorkspaceDeclarationContext context,
            SelectedContextExactTypeInspectionRequest request,
            CompleteWorkspaceActivation? activation,
            ViewFacetId? facet,
            Action<SelectedContextExactTypeLiveTarget>? liveTargetConsumer)
    {
        ImmutableArray<ExactTypeDefiningSource> definingSources =
            execution.DefiningSources;
        ExactTypeInspectionResult inspection = execution.Result;
        bool requiresDefiningSources =
            inspection.Outcome
                is ExactTypeInspectionOutcome.Available
                or ExactTypeInspectionOutcome.Ambiguous;
        if (requiresDefiningSources
            && (definingSources.IsEmpty
                || definingSources.Any(
                    static source => source.Member.Coordinate is null)))
        {
            inspection = inspection with
            {
                Outcome = ExactTypeInspectionOutcome.Unavailable,
                Type = null,
                RequestedAssembly = null,
                SupplierAssembly = null,
                ForwardingHops = [],
                Suggestions = [],
                Failures = inspection.Failures.Add(
                    new ExactTypeInspectionFailure(
                        ExactTypeInspectionFailureKind
                            .DefiningSourceUnavailable,
                        "The exact Type result did not retain an exact "
                            + "defining Library source coordinate.")),
            };
            definingSources = [];
        }

        SelectedContextExactTypeLiveTarget? target =
            inspection.IsAvailable
                ? execution.Target is { } selected
                    ? new(
                        selected.Surface.Surface,
                        selected.Type,
                        selected.Occurrence.Assembly,
                        selected.Occurrence,
                        selected.BindingPolicy,
                        selected.AssemblyPath,
                        selected.PackageExtractPath)
                    {
                        OpenCompiledDocumentation =
                            selected.OpenCompiledDocumentation,
                    }
                    : throw new InvalidOperationException(
                        "An available selected-context exact Type requires "
                            + "one live inspection target.")
                : null;
        var references = new TypeDeclarationLocatorReferenceProjection();
        ImmutableArray<SelectedContextExactTypeSource> projectedSources =
        [
            .. definingSources.Select(source =>
                new SelectedContextExactTypeSource(
                    TypeDeclarationLocatorSectionCoordinate.FromSource(
                        source.Member.Coordinate!),
                    ExactTypeDefinitionIdentity.From(source.Type),
                    TypeDeclarationLocatorSection.ProjectObservation(
                        source.Member,
                        references))),
        ];
        var content = new SelectedContextExactTypeInspectionResult(
            inspection,
            projectedSources);
        if (target is not null)
            liveTargetConsumer?.Invoke(target);
        InspectionPortableProjection share =
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported);
        if (activation is not null
            && inspection.IsAvailable
            && !inspection.IsComplete)
        {
            share = new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.Incomplete,
                location: "inspection");
        }
        else if (activation is not null
            && inspection.IsAvailable
            && definingSources.Length == 1)
        {
            WorkspaceSharePacketProjectionResult projection =
                WorkspaceTypeScenarioProjection.Project(
                    activation,
                    context,
                    definingSources[0].Member,
                    definingSources[0].Type,
                    facet);
            share = projection.Succeeded
                ? AvailableShare(
                    projection.Packet
                    ?? throw new InvalidOperationException(
                        "A successful Type scenario projection requires "
                            + "a packet."))
                : NonProjectableShare(
                    projection.Failure
                    ?? throw new InvalidOperationException(
                        "A failed Type scenario projection requires "
                            + "a failure."));
        }
        return new(
            new ResourcePath("exact-type"),
            InspectionContentKind.Result,
            content,
            share,
            ExactTypeInspectionOperation.Diagnostics(inspection));
    }

    static InspectionPortableProjection AvailableShare(WorkspaceSharePacket packet)
    {
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        return new InspectionPortableProjection.Available(
            "https://dotnet-inspect.net/?w=" + encoded,
            encoded);
    }

    static InspectionPortableProjection NonProjectableShare(
        WorkspaceSharePacketProjectionFailure failure) =>
        new InspectionPortableProjection.NonProjectable(
            failure.Kind
                is WorkspaceSharePacketProjectionFailureKind
                    .InvalidDefinitionSet
                ? InspectionPortableProjectionFailureReason.Invalid
                : InspectionPortableProjectionFailureReason.NotSupported,
            location: failure.Path,
            explanation: failure.Message);
}
