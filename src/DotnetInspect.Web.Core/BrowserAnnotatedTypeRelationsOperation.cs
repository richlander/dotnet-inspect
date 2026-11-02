using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

internal abstract record BrowserAnnotatedTypeRelationsExecution
{
    private BrowserAnnotatedTypeRelationsExecution()
    {
    }

    internal sealed record Available(
        InspectionEnvelope<AnnotatedTypeRelationsDocument> Inspection)
        : BrowserAnnotatedTypeRelationsExecution;

    internal sealed record Rejected(
        AnnotatedTypeRelationsRequestFailure Failure)
        : BrowserAnnotatedTypeRelationsExecution;

    internal sealed record Unavailable(string Reason)
        : BrowserAnnotatedTypeRelationsExecution;
}

[SupportedOSPlatform("browser")]
internal static class BrowserAnnotatedTypeRelationsOperation
{
    static readonly string[] CompatibilityColors =
    [
        CompatibilityInspectionGraphCatalog.RequiresUnreferencedCode.Id,
        CompatibilityInspectionGraphCatalog.RequiresDynamicCode.Id,
        CompatibilityInspectionGraphCatalog.RequiresAssemblyFiles.Id,
    ];

    internal static BrowserAnnotatedTypeRelationsExecution Execute(
        BrowserInspectionScope scope,
        BrowserWorkspaceParticipant surfaceParticipant,
        string typeDefinitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(surfaceParticipant);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDefinitionId);
        cancellationToken.ThrowIfCancellationRequested();

        if (MetadataTypeDefinitionName.ParseSerialized(typeDefinitionId)
            is not MetadataTypeDefinitionNameResult.Valid validType)
        {
            return new BrowserAnnotatedTypeRelationsExecution.Unavailable(
                $"Type '{typeDefinitionId}' is not one exact metadata "
                    + "definition identity.");
        }

        BrowserWorkspaceParticipant implementationParticipant;
        try
        {
            implementationParticipant =
                scope.ImplementationParticipant(surfaceParticipant);
        }
        catch (InvalidOperationException exception)
        {
            return new BrowserAnnotatedTypeRelationsExecution.Unavailable(
                exception.Message);
        }

        return scope.UseImplementationWorkspaceParticipant<
            BrowserAnnotatedTypeRelationsExecution>(
            implementationParticipant,
            (workspace, group, participant) =>
            {
                StructuralSubjectIdentity.WorkspaceSubject workspaceSubject =
                    StructuralSubjectIdentity.ForWorkspace(
                        workspace.Identity);
                SubjectRelationFocusAuthority focus =
                    SubjectRelationFocusAuthority.ForAcquiredType(
                        workspaceSubject,
                        participant.Assembly.Registration,
                        participant.Assembly.Identity,
                        participant.Assembly.Provenance,
                        validType.Name);
                SubjectRelationPopulationAuthority population =
                    SubjectRelationPopulationAuthority.Capture(
                        workspaceSubject,
                        group.BindingPolicyVersion);
                var request = new AnnotatedTypeRelationsRequest(
                    focus,
                    AnnotatedTypeRelationsFamilySelection.AllApplicable,
                    SubjectRelationDirectionSelection.Outgoing,
                    CompatibilityColors,
                    requiredColorIds: [],
                    AnnotatedTypeRelationsTerminal.Rows);
                return AnnotatedTypeRelationsCallInspection.Execute(
                        group,
                        participant.Assembly.Registration,
                        validType.Name,
                        population,
                        request,
                        cancellationToken)
                    switch
                    {
                        AnnotatedTypeRelationsCallInspectionResult.Available
                            available =>
                            new BrowserAnnotatedTypeRelationsExecution
                                .Available(available.Inspection),
                        AnnotatedTypeRelationsCallInspectionResult.Rejected
                            rejected =>
                            new BrowserAnnotatedTypeRelationsExecution
                                .Rejected(rejected.Failure),
                        _ => throw new InvalidOperationException(
                            "Unknown Annotated Type Relations inspection "
                                + "result."),
                    };
            });
    }
}
