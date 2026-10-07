using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

internal sealed record BrowserTypeHierarchyInspection(
    InspectionEnvelope<SelectedContextExactTypeInspectionResult>
        ExactTypeInspection,
    SubjectRelationForm? Form,
    WorkspaceTypeHierarchySubjectRelationsExecution? Hierarchy);

internal static class BrowserTypeHierarchyInspectionOperation
{
    internal const int MaximumRows = 100;
    const long MaxMetadataRows = 250_000;

    static readonly MetadataOperationPolicy OperationPolicy =
        new(
            maxMetadataRows: MaxMetadataRows,
            maxMethodImplementationRows: MaxMetadataRows,
            maxDeclarationCandidates: MaxMetadataRows,
            maxRelationshipEdges: MaxMetadataRows,
            maxSignatureBytes: MaxMetadataRows,
            maxGenericSubstitutionNodes: MaxMetadataRows,
            maxStructuredNodes: MaxMetadataRows,
            maxRetainedText: MaxMetadataRows,
            maxInterfaceImplementationRows: MaxMetadataRows,
            maxRetainedHierarchyRelations: MaximumRows);

    [SupportedOSPlatform("browser")]
    internal static BrowserTypeHierarchyInspection Execute(
        BrowserInspectionScope scope,
        BrowserWorkspaceParticipant participant,
        string typeDefinitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDefinitionId);
        cancellationToken.ThrowIfCancellationRequested();

        return scope.UseSurfaceDeclarationContext(
            (workspace, context) => Execute(
                workspace,
                context,
                participant.Participant.Assembly.Registration,
                new ExactTypeInspectionRequest(
                    participant.Coordinate.PackageId,
                    participant.Coordinate.Version,
                    participant.Coordinate.Framework,
                    typeDefinitionId,
                    ExactTypeSelectionKind.DefinitionIdentity),
                typeDefinitionId,
                cancellationToken));
    }

    static BrowserTypeHierarchyInspection Execute(
        InspectionWorkspace workspace,
        WorkspaceDeclarationContext context,
        AssemblyAcquisitionRegistration definingRegistration,
        ExactTypeInspectionRequest shareRequest,
        string typeDefinitionId,
        CancellationToken cancellationToken)
    {
        InspectionEnvelope<SelectedContextExactTypeInspectionResult> exact =
            SelectedContextExactTypeInspectionOperation.ExecuteWithLiveTarget(
                workspace,
                context,
                definingRegistration,
                new SelectedContextExactTypeInspectionRequest(
                    typeDefinitionId,
                    ExactTypeSelectionKind.DefinitionIdentity),
                static _ => { },
                shareRequest);
        if (exact.Content is not
            {
                Inspection.Type: { } type,
                DefiningSources: [{ } source],
            })
        {
            return new(exact, Form: null, Hierarchy: null);
        }

        SubjectRelationForm? form = type.Kind switch
        {
            "interface" => SubjectRelationForm.Interface,
            "class" => SubjectRelationForm.BaseType,
            _ => null,
        };
        if (form is null)
            return new(exact, Form: null, Hierarchy: null);

        MetadataTypeDefinitionName focus =
            MetadataTypeDefinitionName.Create(
                source.Type.Namespace,
                source.Type.Segments)
            switch
            {
                MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
                MetadataTypeDefinitionNameResult.Rejected rejected =>
                    throw new InvalidOperationException(
                        "The exact Type source has an invalid metadata name: "
                            + rejected.Rejection.Kind),
                _ => throw new InvalidOperationException(
                    "The exact Type source returned an unknown metadata-name "
                        + "validation result."),
            };

        WorkspaceDeclarationPopulationCapture capture =
            workspace.CaptureDeclarationPopulation([context]);
        WorkspaceDeclarationPopulation population = capture switch
        {
            WorkspaceDeclarationPopulationCapture.Captured captured =>
                captured.Population,
            WorkspaceDeclarationPopulationCapture.Rejected rejected =>
                throw new InvalidOperationException(
                    "The retained Browser declaration population could not be "
                        + $"captured: {rejected.Failure}"),
            _ => throw new InvalidOperationException(
                "The retained Browser declaration population returned an "
                    + "unknown capture result."),
        };

        WorkspaceTypeHierarchySubjectRelationsExecution hierarchy =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new WorkspaceTypeHierarchySubjectRelationsRequest(
                    new WorkspaceTypeHierarchySubjectRelationsFocus(
                        source.Occurrence,
                        focus),
                    new SubjectRelationPopulationRequest(
                        new SubjectRelationPopulationSelection(
                            form,
                            form == SubjectRelationForm.Interface
                                ? MetadataRelationGraphCatalog.Interface.Id
                                : MetadataRelationGraphCatalog.BaseType.Id,
                            SubjectRelationDirectionSelection.Incoming,
                            SubjectRelationEvidenceKind.Declaration),
                        new SubjectRelationPopulationCountRequest(),
                        new SubjectRelationPopulationRowsRequest(
                            MaximumRows,
                            SubjectRelationPopulationOrdering.Producer,
                            SubjectRelationRowProjection.Canonical))),
                OperationPolicy,
                cancellationToken: cancellationToken);
        return new(exact, form, hierarchy);
    }
}
