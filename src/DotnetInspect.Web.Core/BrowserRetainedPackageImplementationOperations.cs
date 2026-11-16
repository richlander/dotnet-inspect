using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserRetainedPackageImplementationOperations
{
    internal static async Task<T> ExecuteAsync<T>(
        string retainedDefinitionId,
        string realizationId,
        string navigationId,
        string packageId,
        string version,
        string framework,
        string assembly,
        Func<AssemblyContextGroup, AssemblyContextParticipant, T> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
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
                framework,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ContextUnavailable: the Package coordinate does not match "
                    + "the admitted Workspace row.");
        }

        CompleteRestorationPackageLibrary[] libraries =
        [
            .. presentation.Inventory.Libraries.Where(
                candidate =>
                    string.Equals(
                        candidate.Asset.Id,
                        assembly,
                        StringComparison.Ordinal)
                    || string.Equals(
                        candidate.Asset.AssemblyName,
                        assembly,
                        StringComparison.Ordinal)),
        ];
        if (libraries.Length != 1)
        {
            throw new InvalidOperationException(
                libraries.Length == 0
                    ? $"ContextUnavailable: assembly '{assembly}' is not in "
                        + "the admitted Workspace Package row."
                    : $"ContextUnavailable: assembly '{assembly}' is "
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

        ArtifactRootResult<T> result =
            await operation.Workspace.ExecutePackageRootQueryAsync(
                correspondence,
                ready.Generation,
                (realization, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(
                        BrowserPackageImplementationParticipantQuery.Execute(
                            realization,
                            library,
                            query));
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        return result switch
        {
            ArtifactRootResult<T>.Available available => available.Value,
            ArtifactRootResult<T>.Rejected rejected =>
                throw new InvalidOperationException(
                    "ContextUnavailable: the admitted Workspace Package Root "
                        + "generation could not be opened: "
                        + rejected.Failure),
            _ => throw new InvalidOperationException(
                "ContextUnavailable: the admitted Workspace Package Root "
                    + "generation is unavailable."),
        };
    }
}

internal static class BrowserPackageImplementationParticipantQuery
{
    internal static T Execute<T>(
        PackageAssemblyContextRealization realization,
        CompleteRestorationPackageLibrary library,
        Func<AssemblyContextGroup, AssemblyContextParticipant, T> query)
    {
        ArgumentNullException.ThrowIfNull(query);
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

        return query(
            implementationGroup,
            implementation.Participant);
    }
}
