using System.Runtime.Versioning;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Interop.CallGraph;

[SupportedOSPlatform("browser")]
internal static class RetainedCallGraphOperations
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
        CancellationToken cancellationToken = default)
    {
        BrowserRetainedWorkspacePackageOperationAdmission admission =
            await BrowserRetainedWorkspaceActivationRegistry.Owner
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
        AssemblyContextParticipant[] participants =
        [
            .. admitted.Group.Participants.Where(
                candidate =>
                    AssemblyReferenceIdentity.EquivalentComparer.Equals(
                        candidate.Assembly.Identity,
                        library.Subject.Identity)),
        ];
        if (participants.Length != 1)
        {
            throw new InvalidOperationException(
                participants.Length == 0
                    ? "ContextUnavailable: the admitted Workspace Package "
                        + "implementation participant is unavailable."
                    : "ContextUnavailable: the admitted Workspace Package "
                        + "implementation participant is ambiguous.");
        }

        AssemblyContextMemberBody member =
            BrowserMemberResolution.ResolveImplementationMember(
                admitted.Group,
                participants[0],
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken);
        ProjectedMethodAddress address =
            AssemblyContextMethodAddressQuery.ExecuteParticipant(
                admitted.Group,
                participants[0],
                member.BodyToken) switch
            {
                AssemblyContextEntry<ProjectedMethodAddress>.Available
                    available => available.Value,
                AssemblyContextEntry<ProjectedMethodAddress>.Rejected
                    rejected => throw new InvalidOperationException(
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
                platformPruning,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
