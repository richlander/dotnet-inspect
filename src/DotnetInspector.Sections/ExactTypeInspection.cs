using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspector.Sections;

public static class ExactTypeInspection
{
    public static async Task<InspectionEnvelope<ExactTypeInspectionResult>> ExecuteAsync(
        ExactTypeInspectionRequest request,
        WorkspaceRealizationOperationLease operation,
        PackageRootBinding binding,
        PackageAssemblyContextRealization realization,
        CancellationToken cancellationToken = default)
    {
        ExactTypeInspectionResult content =
            await ExactTypeInspectionQuery.ExecuteAsync(
                request,
                operation,
                binding,
                realization,
                cancellationToken).ConfigureAwait(false);
        return new(
            content,
            ProjectShare(content, operation, cancellationToken),
            Diagnostics(content));
    }

    static InspectionShare ProjectShare(
        ExactTypeInspectionResult content,
        WorkspaceRealizationOperationLease operation,
        CancellationToken cancellationToken)
    {
        if (content is not ExactTypeInspectionResult.Available available)
        {
            return NonProjectable(
                "type",
                "An exact type was not successfully resolved.");
        }
        if (!available.IsContextUnique)
        {
            return NonProjectable(
                "type.assembly",
                "The package has no unique canonical type selection without its exact Library selection.");
        }
        if (available.Candidate.SupplierSource is not
            RealizedMemberCoordinate.Package source
            || source.Producer != PackageProducerIdentity.NuGetOrg.Key
                && source.Producer != NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url))
        {
            return NonProjectable(
                "type.source",
                "The resolved source cannot be restored by the published Browser.");
        }

        ExactTypeInspectionRequest request = content.Request;
        WorkspacePlan plan = operation.Definition.Plan;
        if (request.ContextIndex < 0
            || request.ContextIndex >= plan.Contexts.Length)
        {
            return NonProjectable(
                "request.context",
                "The selected Workspace context is outside the admitted definition.");
        }

        WorkspaceContextInput context = plan.Contexts[request.ContextIndex];
        if (context.Members is not
            [WorkspaceMemberCoordinate.PackageMember package]
            || string.IsNullOrWhiteSpace(package.Version)
            || string.IsNullOrWhiteSpace(context.Framework)
            || string.IsNullOrWhiteSpace(source.Framework)
            || available.Candidate.Supplier is not
                ExactLibrarySourceCoordinate.Package supplier)
        {
            return NonProjectable(
                "workspace.contexts",
                "Exact type Share requires one exactly versioned package and one target framework.");
        }

        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                supplier.PackageCoordinate.PackageId,
                supplier.PackageCoordinate.Version,
                source.Framework,
                source.RuntimeIdentifier);
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    source.Framework,
                    source.RuntimeIdentifier,
                    members: [coordinate]),
            ]);
        var navigation = new NavigationDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.NavigationId,
            [new NavigationTabDefinition("t0", coordinate: coordinate)],
            "t0");
        var view = new ViewDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.ViewId,
            type: available.Candidate.Definition.ToEscapedFullName());
        var scenario = new ScenarioDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
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
                    scenario),
                cancellationToken);
        if (!projection.Succeeded)
        {
            WorkspaceSharePacketProjectionFailure failure =
                projection.Failure!;
            return NonProjectable(
                failure.Path,
                failure.Message);
        }

        string packet =
            WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new InspectionShare.Available(
            "https://dotnet-inspect.net/?w=" + packet,
            packet);
    }

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        ExactTypeInspectionResult content) =>
        [
            .. content.Failures.Select((failure, index) =>
                new InspectionDiagnostic(
                    $"exact-type.{FailureCode(failure.Kind)}",
                    InspectionDiagnosticSeverity.Warning,
                    $"Exact type inspection limitation {index + 1}: "
                        + FailureSummary(failure.Kind))),
        ];

    static string FailureCode(ExactTypeInspectionFailureKind kind) =>
        kind switch
        {
            ExactTypeInspectionFailureKind.InvalidRequest =>
                "invalid-request",
            ExactTypeInspectionFailureKind.DefinitionMismatch =>
                "definition-mismatch",
            ExactTypeInspectionFailureKind.ContextUnavailable =>
                "context-unavailable",
            ExactTypeInspectionFailureKind.ContextLoadFailed =>
                "context-load-failed",
            ExactTypeInspectionFailureKind.PopulationUnavailable =>
                "population-unavailable",
            ExactTypeInspectionFailureKind.DeclarationInventoryIncomplete =>
                "declaration-inventory-incomplete",
            ExactTypeInspectionFailureKind.TypeResolutionRejected =>
                "type-resolution-rejected",
            ExactTypeInspectionFailureKind.TypeResolutionUnavailable =>
                "type-resolution-unavailable",
            ExactTypeInspectionFailureKind.TypeResolutionAmbiguous =>
                "type-resolution-ambiguous",
            ExactTypeInspectionFailureKind.ApiSurfaceRejected =>
                "api-surface-rejected",
            ExactTypeInspectionFailureKind.ApiSurfaceFailed =>
                "api-surface-failed",
            ExactTypeInspectionFailureKind.ApiSurfaceIncomplete =>
                "api-surface-incomplete",
            ExactTypeInspectionFailureKind.AsyncClassificationUnavailable =>
                "async-classification-unavailable",
            ExactTypeInspectionFailureKind.ResolvedTypeMissing =>
                "resolved-type-missing",
            _ => "unknown",
        };

    static string FailureSummary(ExactTypeInspectionFailureKind kind) =>
        kind switch
        {
            ExactTypeInspectionFailureKind.InvalidRequest =>
                "the request was invalid.",
            ExactTypeInspectionFailureKind.DefinitionMismatch =>
                "the selected context did not belong to the admitted definition generation.",
            ExactTypeInspectionFailureKind.ContextUnavailable =>
                "a selected Workspace context participant was unavailable.",
            ExactTypeInspectionFailureKind.ContextLoadFailed =>
                "the selected Workspace context could not be realized.",
            ExactTypeInspectionFailureKind.PopulationUnavailable =>
                "the selected declaration population was unavailable.",
            ExactTypeInspectionFailureKind.DeclarationInventoryIncomplete =>
                "the declaration inventory was incomplete.",
            ExactTypeInspectionFailureKind.TypeResolutionRejected =>
                "type resolution rejected a participant.",
            ExactTypeInspectionFailureKind.TypeResolutionUnavailable =>
                "type resolution could not establish a terminal definition.",
            ExactTypeInspectionFailureKind.TypeResolutionAmbiguous =>
                "type resolution remained ambiguous.",
            ExactTypeInspectionFailureKind.ApiSurfaceRejected =>
                "API extraction rejected the supplying participant.",
            ExactTypeInspectionFailureKind.ApiSurfaceFailed =>
                "API extraction failed for the supplying participant.",
            ExactTypeInspectionFailureKind.ApiSurfaceIncomplete =>
                "API extraction reached an explicit work bound.",
            ExactTypeInspectionFailureKind.AsyncClassificationUnavailable =>
                "async member classification was unavailable.",
            ExactTypeInspectionFailureKind.ResolvedTypeMissing =>
                "the resolved definition was absent from the supplier API surface.",
            _ => "the operation returned an unknown limitation.",
        };

    static InspectionShare NonProjectable(
        string path,
        string reason) =>
        new InspectionShare.NonProjectable(
            $"exact-type/{path}",
            reason);
}
