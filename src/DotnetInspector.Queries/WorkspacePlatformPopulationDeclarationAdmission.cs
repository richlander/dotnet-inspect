using System.Collections.Immutable;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum WorkspacePlatformPopulationDeclarationAdmissionRejection
{
    ForeignWorkspace,
    WorkspaceUnavailable,
    UnknownLibraryAdmission,
    PopulationMismatch,
    LibraryCoordinateUnavailable,
    AssemblyIdentityUnavailable,
    SourceCorrespondenceUnavailable,
}

public abstract record
    WorkspacePlatformPopulationDeclarationAdmissionOutcome
{
    private WorkspacePlatformPopulationDeclarationAdmissionOutcome()
    {
    }

    public sealed record Admitted(WorkspaceDeclarationContext Context)
        : WorkspacePlatformPopulationDeclarationAdmissionOutcome;

    public sealed record Rejected(
        WorkspacePlatformPopulationDeclarationAdmissionRejection Reason,
        int? MemberIndex = null)
        : WorkspacePlatformPopulationDeclarationAdmissionOutcome;
}

/// <summary>
/// Admits one already-owned, complete PlatformHouse reference population to
/// the Workspace declaration locator without reacquisition or image copying.
/// </summary>
public static class WorkspacePlatformPopulationDeclarationAdmission
{
    public static WorkspacePlatformPopulationDeclarationAdmissionOutcome
        Admit(
            InspectionWorkspace workspace,
            WorkspaceLibraryAdmissionReceipt admission,
            PlatformPopulationRealizationValue population,
            PlatformPopulationRealizationReceipt populationReceipt,
            LibraryTypeDeclarationInventoryInspectionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(populationReceipt);
        ArgumentNullException.ThrowIfNull(bounds);

        if (!ReferenceEquals(admission.Workspace, workspace.Identity))
        {
            return new WorkspacePlatformPopulationDeclarationAdmissionOutcome
                .Rejected(
                    WorkspacePlatformPopulationDeclarationAdmissionRejection
                        .ForeignWorkspace);
        }
        if (!workspace.ContainsLibraryAdmission(admission))
        {
            return new WorkspacePlatformPopulationDeclarationAdmissionOutcome
                .Rejected(
                    WorkspacePlatformPopulationDeclarationAdmissionRejection
                        .UnknownLibraryAdmission);
        }
        if (populationReceipt.RealizedMembers is null
            || population.Members.Count != admission.Occurrences.Length
            || population.Members.Count
                != populationReceipt.RealizedMembers.Count)
        {
            return new WorkspacePlatformPopulationDeclarationAdmissionOutcome
                .Rejected(
                    WorkspacePlatformPopulationDeclarationAdmissionRejection
                        .PopulationMismatch);
        }

        int order;
        try
        {
            order = workspace.BeginDeclarationContext();
        }
        catch (ObjectDisposedException)
        {
            return new WorkspacePlatformPopulationDeclarationAdmissionOutcome
                .Rejected(
                    WorkspacePlatformPopulationDeclarationAdmissionRejection
                        .WorkspaceUnavailable);
        }
        var members =
            ImmutableArray.CreateBuilder<WorkspaceDeclarationMember>(
                population.Members.Count);
        var occurrences =
            ImmutableArray.CreateBuilder<WorkspaceLibraryOccurrence>(
                population.Members.Count);
        for (int index = 0; index < population.Members.Count; index++)
        {
            PlatformPopulationMember member = population.Members[index];
            WorkspaceLibraryOccurrence occurrence =
                admission.Occurrences[index];
            if (!ReferenceEquals(member, populationReceipt.RealizedMembers[index])
                || !ReferenceEquals(member.Library, occurrence.Library))
            {
                return Rejected(
                    WorkspacePlatformPopulationDeclarationAdmissionRejection
                        .PopulationMismatch,
                    index);
            }
            if (member.Library.SourceCoordinate
                is not ExactLibrarySourceCoordinate.Platform coordinate
                || coordinate.Population.Family != member.Target.Family)
            {
                return Rejected(
                    WorkspacePlatformPopulationDeclarationAdmissionRejection
                        .LibraryCoordinateUnavailable,
                    index);
            }
            ManagedMetadataIdentity.Assembly? assembly =
                member.Library.ApiAssembly.AssemblyIdentity;
            if (assembly is null)
            {
                return Rejected(
                    WorkspacePlatformPopulationDeclarationAdmissionRejection
                        .AssemblyIdentityUnavailable,
                    index);
            }
            if (member.Library.ApiAssembly.Provenance
                is not PlatformLibraryArtifactProvenance provenance
                || !ReferenceEquals(
                    provenance.Contribution.Target,
                    member.Target)
                && provenance.Contribution.Target != member.Target)
            {
                return Rejected(
                    WorkspacePlatformPopulationDeclarationAdmissionRejection
                        .SourceCorrespondenceUnavailable,
                    index);
            }

            members.Add(
                new(
                    new(workspace.Identity, order, index),
                    coordinate,
                    assembly.Identity,
                    new WorkspaceDeclarationOrigin.PlatformPopulation(
                        member.Target,
                        member.Role,
                        provenance.Contribution.Capability.Name,
                        assembly.Identity.Name),
                    AssemblyResolutionProvenance.Platform(
                        member.Target.Family switch
                        {
                            PlatformFamily.DotNetRuntime =>
                                "Microsoft.NETCore.App",
                            PlatformFamily.AspNetCore =>
                                "Microsoft.AspNetCore.App",
                            _ => throw new InvalidOperationException(
                                "Unknown Platform family."),
                        },
                        member.Target.Version.Value,
                        provenance.Contribution.Capability.Name)));
            occurrences.Add(occurrence);
        }

        WorkspaceDeclarationContext context = new(
            new(
                workspace.Identity,
                order,
                new WorkspaceDeclarationRequest.PlatformPopulation(
                    populationReceipt),
                isRealized: true,
                members.MoveToImmutable(),
                []),
            occurrences.MoveToImmutable(),
            bounds);
        try
        {
            return new WorkspacePlatformPopulationDeclarationAdmissionOutcome
                .Admitted(workspace.PublishDeclarationContext(context));
        }
        catch (ObjectDisposedException)
        {
            return new WorkspacePlatformPopulationDeclarationAdmissionOutcome
                .Rejected(
                    WorkspacePlatformPopulationDeclarationAdmissionRejection
                        .WorkspaceUnavailable);
        }

        static WorkspacePlatformPopulationDeclarationAdmissionOutcome
            .Rejected Rejected(
                WorkspacePlatformPopulationDeclarationAdmissionRejection
                    reason,
                int memberIndex) =>
            new(reason, memberIndex);
    }
}
