using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

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
            facet: null);

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
            facet: null);
    }

    public static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        Execute(
            WorkspaceRealizationOperationLease authority,
            CompleteWorkspaceActivation activation,
            SelectedContextExactTypeInspectionRequest request,
            ViewFacetId? facet = null)
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
            facet);
    }

    static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExecuteCore(
            WorkspaceRealizationOperationLease authority,
            WorkspaceDeclarationContext context,
            SelectedContextExactTypeInspectionRequest request,
            ApiSurfaceProjectionLimits? projectionLimits,
            CompleteWorkspaceActivation? activation,
            ViewFacetId? facet)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        ExactTypeInspectionExecution execution =
            ExactTypeInspectionQuery.ExecuteSelectedContext(
                authority,
                context,
                request,
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
