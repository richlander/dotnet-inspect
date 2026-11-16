using System.Runtime.Versioning;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserRetainedCallGraphOperations
{
    internal static async Task<
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome>> QueryAsync(
        string retainedDefinitionId,
        string realizationId,
        string navigationId,
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken,
        string traversalTargetFramework,
        string? runtimeIdentifier,
        PackageDependencyMemberCallGraphPlatformPruning? platformPruning,
        CancellationToken cancellationToken = default) =>
        await QueryAsync(
            BrowserRetainedWorkspaceActivationRegistry.Owner,
            retainedDefinitionId,
            realizationId,
            navigationId,
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken,
            traversalTargetFramework,
            runtimeIdentifier,
            platformPruning,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome>> QueryAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string retainedDefinitionId,
        string realizationId,
        string navigationId,
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken,
        string traversalTargetFramework,
        string? runtimeIdentifier,
        PackageDependencyMemberCallGraphPlatformPruning? platformPruning,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        BrowserRetainedWorkspacePackageOperationAdmission admission =
            await owner
                .EnterPackageOperationAsync(
                    retainedDefinitionId,
                    realizationId,
                    navigationId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is BrowserRetainedWorkspacePackageOperationAdmission.Superseded)
        {
            throw new InvalidOperationException(
                "ContextUnavailable: the retained Workspace realization "
                    + "is not active.");
        }
        if (admission
            is BrowserRetainedWorkspacePackageOperationAdmission.Unavailable
                unavailable)
        {
            throw new InvalidOperationException(
                $"ContextUnavailable: {unavailable.Message}");
        }

        var admitted =
            (BrowserRetainedWorkspacePackageOperationAdmission.Admitted)
                admission;
        using WorkspaceRealizationOperationLease operation =
            admitted.Operation;
        BrowserRetainedWorkspacePackagePresentation presentation =
            admitted.Presentation;
        BrowserPackageSurfaceInfo surface = presentation.Surface;
        if (!string.Equals(surface.Package, packageId, StringComparison.Ordinal)
            || !string.Equals(surface.Version, version, StringComparison.Ordinal)
            || !string.Equals(
                surface.ActiveFramework,
                targetFramework,
                StringComparison.Ordinal)
            || !string.Equals(
                presentation.Inventory.Package.RuntimeIdentifier,
                runtimeIdentifier,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ContextUnavailable: the Call Graph Package coordinate "
                    + "does not match the admitted Workspace row.");
        }

        CompleteRestorationPackageLibrary[] libraries =
        [
            .. presentation.Inventory.Libraries.Where(
                candidate =>
                    string.Equals(
                        candidate.Asset.Id,
                        assemblyName,
                        StringComparison.Ordinal)
                    || string.Equals(
                        candidate.Asset.AssemblyName,
                        assemblyName,
                        StringComparison.Ordinal)),
        ];
        if (libraries.Length != 1)
        {
            throw new InvalidOperationException(
                libraries.Length == 0
                    ? $"ContextUnavailable: assembly '{assemblyName}' is not "
                        + "in the admitted Workspace Package row."
                    : $"ContextUnavailable: assembly '{assemblyName}' is "
                        + "ambiguous in the admitted Workspace Package row.");
        }

        CompleteRestorationPackageLibrary library = libraries[0];
        WorkspacePackageOccurrenceDescriptor? occurrence =
            operation.Scope.FindExactPackageOccurrence(admitted.Binding);
        if (occurrence?.Occurrence.Correspondence
                is not PackageArtifactRootCorrespondence correspondence
            || occurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready ready)
        {
            throw new InvalidOperationException(
                "ContextUnavailable: the admitted Workspace Package Root "
                    + "generation is unavailable.");
        }

        ArtifactRootResult<ProjectedMethodAddress> addressQuery =
            await operation.Workspace.ExecutePackageRootQueryAsync(
                correspondence,
                ready.Generation,
                (realization, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(
                        ResolveImplementationAddress(
                            realization,
                            library,
                            typeIdentity,
                            memberName,
                            selectorKey,
                            metadataToken));
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        ProjectedMethodAddress address = addressQuery switch
        {
            ArtifactRootResult<ProjectedMethodAddress>.Available available =>
                available.Value,
            ArtifactRootResult<ProjectedMethodAddress>.Rejected rejected =>
                throw new InvalidOperationException(
                    "ContextUnavailable: the admitted Workspace Package Root "
                        + $"generation could not be opened: {rejected.Failure}"),
            _ => throw new InvalidOperationException(
                "ContextUnavailable: the admitted Workspace Package Root "
                    + "generation is unavailable."),
        };
        var focus =
            new PackageDependencyMemberCallGraphInspectionFocus(
                address.ModuleVersionId,
                address.MetadataToken);
        return await BrowserPackageWorkspace
            .QueryDependencyMemberCallGraphAsync(
                admitted.Binding,
                focus,
                traversalTargetFramework,
                operation.Definition.Plan,
                PackageSupplyChainBaseline.SelfAndRegisteredEcosystems,
                admitted.PackageSources,
                platformPruning,
                cancellationToken)
            .ConfigureAwait(false);
    }

    static ProjectedMethodAddress ResolveImplementationAddress(
        PackageAssemblyContextRealization realization,
        CompleteRestorationPackageLibrary library,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        if (!realization.HasAssemblyContexts
            || realization.ImplementationGroup
                is not { } implementationGroup)
        {
            throw new InvalidOperationException(
                "ContextUnavailable: the admitted Workspace Package "
                    + "implementation context is unavailable.");
        }

        PackageAssemblyRoleParticipant[] surfaceParticipants =
        [
            .. realization.SurfaceParticipants.Where(
                candidate =>
                    candidate.Asset == library.Asset
                    && AssemblyReferenceIdentity.EquivalentComparer.Equals(
                        candidate.Participant.Assembly.Identity,
                        library.Subject.Identity)),
        ];
        if (surfaceParticipants.Length != 1)
        {
            throw new InvalidOperationException(
                surfaceParticipants.Length == 0
                    ? "ContextUnavailable: the admitted Workspace Package "
                        + "surface participant is unavailable."
                    : "ContextUnavailable: the admitted Workspace Package "
                        + "surface participant is ambiguous.");
        }

        PackageAssemblyRoleParticipant? implementation =
            realization.ImplementationParticipant(surfaceParticipants[0]);
        if (implementation is null)
        {
            throw new InvalidOperationException(
                "ContextUnavailable: the admitted Workspace Package "
                    + "implementation participant is unavailable.");
        }
        AssemblyContextMemberBody member =
            BrowserMemberResolution.ResolveImplementationMember(
                implementationGroup,
                implementation.Participant,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken);
        return AssemblyContextMethodAddressQuery.ExecuteParticipant(
            implementationGroup,
            implementation.Participant,
            member.BodyToken) switch
        {
            AssemblyContextEntry<ProjectedMethodAddress>.Available available =>
                available.Value,
            AssemblyContextEntry<ProjectedMethodAddress>.Rejected rejected =>
                throw new InvalidOperationException(
                    "ContextUnavailable: the admitted Call Graph "
                        + "implementation could not be opened: "
                        + rejected.Failure),
            AssemblyContextEntry<ProjectedMethodAddress>.Failed failed =>
                throw new InvalidOperationException(
                    "ContextUnavailable: the admitted Call Graph "
                        + "implementation could not be inspected.",
                    failed.Error),
            _ => throw new InvalidOperationException(
                "ContextUnavailable: the admitted Call Graph "
                    + "implementation address is unavailable."),
        };
    }
}
