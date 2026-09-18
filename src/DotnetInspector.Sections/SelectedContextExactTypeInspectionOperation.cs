using System.Collections.Immutable;
using System.Text.Json.Serialization;

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
    string? PackageExtractPath);

/// <summary>
/// Exact Type content plus owner-issued defining source identities and an
/// optional live target for hosts operating within the admitted realization.
/// </summary>
public sealed record SelectedContextExactTypeInspectionResult
{
    public SelectedContextExactTypeInspectionResult(
        ExactTypeInspectionResult inspection,
        ImmutableArray<SelectedContextExactTypeSource> definingSources,
        SelectedContextExactTypeLiveTarget? liveTarget = null)
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
        LiveTarget = liveTarget;
    }

    public ExactTypeInspectionResult Inspection { get; }

    public ImmutableArray<SelectedContextExactTypeSource> DefiningSources
    {
        get;
    }

    /// <summary>
    /// Full inspection state for in-process host rendering. This value is not
    /// detached or serialized and remains valid only under the paired lease.
    /// </summary>
    [JsonIgnore]
    public SelectedContextExactTypeLiveTarget? LiveTarget { get; }
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
            ApiSurfaceScope.PublicWithNonPublicTypes);

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
            ApiSurfaceScope.PublicWithNonPublicTypes);
    }

    public static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        Execute(
            WorkspaceRealizationOperationLease authority,
            CompleteWorkspaceActivation activation,
            SelectedContextExactTypeInspectionRequest request,
            ViewFacetId? facet = null,
            ApiSurfaceScope scope =
                ApiSurfaceScope.PublicWithNonPublicTypes)
    {
        ArgumentNullException.ThrowIfNull(activation);
        if (activation.SelectedContext is not { } context)
        {
            ExactTypeInspectionResult unavailable =
                ExactTypeInspectionResult.RuntimeUnavailable(
                    request.Type,
                    "The restored Workspace has no selected context.");
            return new(
                new SelectedContextExactTypeInspectionResult(
                    unavailable,
                    []),
                new InspectionShare.NonProjectable(
                    "scenario.context",
                    "A derived Type scenario requires one selected "
                        + "Workspace context."),
                ExactTypeInspectionOperation.Diagnostics(unavailable));
        }

        return ExecuteCore(
            authority,
            context,
            request,
            projectionLimits: null,
            activation,
            facet,
            scope);
    }

    static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExecuteCore(
            WorkspaceRealizationOperationLease authority,
            WorkspaceDeclarationContext context,
            SelectedContextExactTypeInspectionRequest request,
            ApiSurfaceProjectionLimits? projectionLimits,
            CompleteWorkspaceActivation? activation,
            ViewFacetId? facet,
            ApiSurfaceScope scope)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        ExactTypeInspectionExecution execution =
            ExactTypeInspectionQuery.ExecuteSelectedContext(
                authority,
                context,
                request,
                scope,
                projectionLimits);
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
            projectedSources,
            target);
        InspectionShare share =
            new InspectionShare.NonProjectable(
                "selected-context-exact-type/share",
                "A complete portable Workspace scenario is required to "
                    + "project selected-context exact Type Share.");
        if (activation is not null
            && inspection.IsAvailable
            && !inspection.IsComplete)
        {
            share = new InspectionShare.NonProjectable(
                "selected-context-exact-type/incomplete",
                "A derived Type scenario requires complete trustworthy "
                    + "selected-context Type evidence.");
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
            content,
            share,
            ExactTypeInspectionOperation.Diagnostics(inspection));
    }

    static InspectionShare AvailableShare(WorkspaceSharePacket packet)
    {
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        return new InspectionShare.Available(
            "https://dotnet-inspect.net/?w=" + encoded,
            encoded);
    }

    static InspectionShare NonProjectableShare(
        WorkspaceSharePacketProjectionFailure failure) =>
        new InspectionShare.NonProjectable(
            failure.Path,
            failure.Message);
}
