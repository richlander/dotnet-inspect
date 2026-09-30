using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Web.Interop.Source.Operations;

[SupportedOSPlatform("browser")]
internal static class MethodBodyOperations
{
    internal static async Task<T> WithParticipantAsync<T>(
        string packageId,
        string version,
        string framework,
        string assembly,
        Func<AssemblyContextGroup, AssemblyContextParticipant, T> query)
    {
        if (packageId.Length == 0)
        {
            await using BrowserPlatformScopeResolution platform =
                Select(() =>
                    BrowserPlatformWorkspace.LeaseRetainedAssembly(
                        framework,
                        version,
                        assembly));
            return platform.Scope.UseParticipant(
                platform.Participant,
                query);
        }

        await using BrowserScopeLease<BrowserInspectionScope> lease =
            Select(() =>
                BrowserPackageWorkspace.LeaseRetainedPackageScope(
                    packageId,
                    version,
                    framework));
        BrowserInspectionScope scope = lease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserWorkspaceParticipant implementation = Select(() =>
            scope.ImplementationParticipant(
                scope.SurfaceParticipant(
                    coordinate,
                    coordinate.CompileAsset(assembly))));
        return scope.UseImplementationParticipant(
            implementation,
            query);
    }

    internal static async Task<T> WithRetainedPackageParticipantAsync<T>(
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
            throw new MethodBodyUnavailableException(
                "ContextUnavailable: the retained Workspace realization "
                    + "is not active.");
        }
        if (admission
            is BrowserRetainedWorkspacePackageOperationAdmission.Unavailable
                unavailable)
        {
            throw new MethodBodyUnavailableException(
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
            throw new MethodBodyUnavailableException(
                "ContextUnavailable: the method-body Package coordinate "
                    + "does not match the admitted Workspace row.");
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
            throw new MethodBodyUnavailableException(
                libraries.Length == 0
                    ? $"ContextUnavailable: assembly '{assembly}' is not in "
                        + "the admitted Workspace Package row."
                    : $"ContextUnavailable: assembly '{assembly}' is "
                        + "ambiguous in the admitted Workspace Package row.");
        }

        WorkspaceDeclarationContext[] contexts =
        [
            .. operation.Workspace.GetDeclarationContextsSnapshot().Where(
                candidate =>
                    ReferenceEquals(
                        candidate.Receipt.Workspace,
                        operation.Realization)
                    && candidate.Receipt.Order
                        == presentation.ContextIndex),
        ];
        if (contexts.Length != 1 || contexts[0].Group is not { } group)
        {
            throw new MethodBodyUnavailableException(
                "ContextUnavailable: the admitted Workspace Package context "
                    + "is unavailable.");
        }

        CompleteRestorationPackageLibrary library = libraries[0];
        AssemblyContextParticipant[] participants =
        [
            .. group.Participants.Where(
                candidate =>
                    AssemblyReferenceIdentity.EquivalentComparer.Equals(
                        candidate.Assembly.Identity,
                        library.Subject.Identity)),
        ];
        if (participants.Length != 1)
        {
            throw new MethodBodyUnavailableException(
                participants.Length == 0
                    ? "ContextUnavailable: the admitted Workspace Package "
                        + "implementation participant is unavailable."
                    : "ContextUnavailable: the admitted Workspace Package "
                        + "implementation participant is ambiguous.");
        }

        return query(group, participants[0]);
    }

    internal static MetadataMethodAddress RequireAddress(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        int token) =>
        AssemblyContextMethodAddressQuery.ExecuteParticipant(
            group,
            participant,
            token) switch
        {
            AssemblyContextEntry<MetadataMethodAddress>.Available available =>
                available.Value,
            AssemblyContextEntry<MetadataMethodAddress>.Rejected rejected =>
                throw new MethodBodyUnavailableException(
                    $"AddressRejected: {rejected.Failure.Kind}: "
                    + rejected.Failure.Detail),
            AssemblyContextEntry<MetadataMethodAddress>.Failed failed =>
                throw new MethodBodyUnavailableException(
                    $"AddressFailed: {failed.Error.Message}",
                    failed.Error),
            _ => throw new InvalidOperationException(
                "Unknown method-address projection outcome."),
        };

    internal static BrowserMethodBodySelection[] Inventory(
        ApiSurface surface)
    {
        if (surface.InspectionFailures.Count > 0)
        {
            throw new MethodBodyUnavailableException(
                "InventoryFailed: "
                + string.Join(
                    "; ",
                    surface.InspectionFailures.Select(
                        failure => failure.ToString())));
        }

        var methods =
            new Dictionary<int, BrowserMethodBodySelection>();
        foreach (ApiType type in surface.Types)
        {
            string identity =
                type.DefinitionName?.ToEscapedFullName()
                ?? throw new MethodBodyUnavailableException(
                    $"InventoryFailed: '{type.FullName}' "
                    + "has no definition identity.");
            foreach (ApiMember member in type.Members)
            foreach (CallGraphMemberBodySelector body
                in CallGraphMemberResolver.CreateBodySelectors(
                    type,
                    member))
            {
                if ((body.BodyToken
                    & unchecked((int)0xff000000)) != 0x06000000)
                {
                    continue;
                }
                string label =
                    $"{type.FullName} / "
                    + $"{member.Signature ?? member.Name}";
                if (body.MemberName != member.Name)
                    label += $" [{body.MemberName}]";
                methods.TryAdd(
                    body.BodyToken,
                    new(
                        identity,
                        body.MemberName,
                        body.SelectorKey,
                        body.BodyToken,
                        label));
            }
        }
        return [.. methods.Values];
    }

    internal static T Select<T>(Func<T> select)
    {
        try
        {
            return select();
        }
        catch (Exception error) when (
            error is ArgumentException
                or InvalidOperationException
                or JsonException
                or FormatException)
        {
            throw new MethodBodyUnavailableException(
                error.Message,
                error);
        }
    }
}

internal sealed class MethodBodyUnavailableException(
    string message,
    Exception? inner = null) : Exception(message, inner);
