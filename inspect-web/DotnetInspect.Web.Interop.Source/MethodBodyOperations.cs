using System.Runtime.Versioning;
using System.Text.Json;
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
