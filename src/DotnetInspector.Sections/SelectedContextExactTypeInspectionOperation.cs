using System.Collections.Immutable;

using DotnetInspector.Queries;

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
            projectionLimits: null);

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
            projectionLimits);
    }

    static InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExecuteCore(
            WorkspaceRealizationOperationLease authority,
            WorkspaceDeclarationContext context,
            SelectedContextExactTypeInspectionRequest request,
            ApiSurfaceProjectionLimits? projectionLimits)
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
        return new(
            content,
            new InspectionShare.NonProjectable(
                "selected-context-exact-type/share",
                "A complete portable Workspace scenario is required to "
                    + "project selected-context exact Type Share."),
            ExactTypeInspectionOperation.Diagnostics(inspection));
    }
}
