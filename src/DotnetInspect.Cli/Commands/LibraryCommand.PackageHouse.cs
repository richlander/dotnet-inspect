using System.Diagnostics.CodeAnalysis;

using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspect.Cli.Commands;

public partial class LibraryCommand
{
    internal static bool TryCreatePackageNamespaceRequest(
        LibraryOptions options,
        [NotNullWhen(true)] out PackageEndpointScopeRequest? request)
    {
        ArgumentNullException.ThrowIfNull(options);
        request = null;
        if (string.IsNullOrWhiteSpace(options.PackagePath)
            || File.Exists(options.PackagePath)
            || options.PackagePath.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase)
            || options.PackagePath.Contains("::", StringComparison.Ordinal)
            || options.WorkspacePacket is not null
            || options.PlatformAssembly is not null
            || options.PlatformFramework is not null
            || options.PlatformVersion is not null
            || options.NamesakeLibrary
            || options.FirstLibrary
            || string.IsNullOrWhiteSpace(options.Tfm)
            || options.Tfm.Equals("all", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(options.AssemblyName)
            || options.AssemblyName.IndexOfAny(['*', '?']) >= 0
            || options.TypeNamespace is null)
        {
            return false;
        }

        (string packageId, string? version) =
            PackageExtractor.ParsePackageReference(options.PackagePath);
        if (string.IsNullOrWhiteSpace(packageId)
            || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        try
        {
            request = new PackageEndpointScopeRequest(
                PackageSourceCoordinate.Create(packageId, version),
                options.Tfm,
                PackageAssetDemand.Surface,
                librarySelector:
                    new PackageLibrarySelector(options.AssemblyName));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static async Task<int?> TryExecutePackageNamespaceAsync(
        LibraryOptions options,
        CancellationToken cancellationToken)
    {
        if (!TryCreatePackageNamespaceRequest(options, out var request))
            return null;

        var logger = new VerboseLogger(options.Verbose);
        await using var composition =
            new DesktopPackageSourceComposition(
                HttpClientFactory.Shared.Timeout);
        using var stores =
            new SearchPackageStores("inspect-library-namespace");
        PackageHouse house =
            composition.CreateDependencyContentQueryHouse(
                stores.GetLegacyCompatibleStore,
                options.SourceOptions,
                options.Verbose ? logger.Log : null);
        return await TryExecutePackageNamespaceAsync(
                options,
                request,
                house,
                composition.IssueSettlementOperation,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<int?> TryExecutePackageNamespaceAsync(
        LibraryOptions options,
        PackageEndpointScopeRequest request,
        PackageHouse house,
        Func<CancellationToken, PackageSourceOperationLease>
            issueSourceOperation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(issueSourceOperation);

        string @namespace =
            options.TypeNamespace
            ?? throw new InvalidOperationException(
                "Namespace Type listing requires a namespace.");
        if (!LibraryNamespaceListingCommand.TryGetNamespaceMatch(
                options,
                out MetadataNamespaceMatch namespaceMatch))
        {
            return 1;
        }
        var selection =
            new LibraryTypeListingCommand.LibraryTypePopulationSelection(
                LibraryTypeDeclarationSelection.DefinitionsAndForwarders,
                ApiTypeInventoryKinds.All);

        try
        {
            PackageEndpointScopeOutcome opened =
                await PackageEndpointScope.OpenAsync(
                        house,
                        issueSourceOperation(cancellationToken),
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (opened is PackageEndpointScopeOutcome.Failed failed)
            {
                if (failed.Kind
                    == PackageEndpointScopeFailureKind.NoCompatibleFramework)
                {
                    return null;
                }
                CommandError.Write(failed.Message);
                return 1;
            }

            await using PackageEndpointScope scope =
                ((PackageEndpointScopeOutcome.Opened)opened).Scope;
            if (scope.SurfaceParticipants.Length != 1)
            {
                CommandError.Write(
                    "The exact Package Library namespace request did not "
                        + "select one surface Library.");
                return 1;
            }

            PackageEndpointParticipant selected =
                scope.SurfaceParticipants[0];
            string requestedLibrary = options.AssemblyName
                ?? throw new InvalidOperationException(
                    "An exact Package Library is required.");
            if ((requestedLibrary.Contains('/')
                    || requestedLibrary.Contains('\\'))
                && !SelectedPathMatches(
                    selected.Asset.Path,
                    requestedLibrary))
            {
                return null;
            }
            ArtifactRootResult<LibraryTypeListingResult?> execution =
                await scope.UseSurfaceParticipantAsync(
                        selected,
                        async (_, participant, token) =>
                            await ExactLibraryInspectionExecutor
                                .ExecuteAsync(
                                    participant.Assembly,
                                    session =>
                                        LibraryTypeListingCommand.ReadRows(
                                            session,
                                            selection,
                                            token,
                                            @namespace,
                                            namespaceMatch),
                                    token)
                                .ConfigureAwait(false),
                        cancellationToken)
                    .ConfigureAwait(false);

            return execution switch
            {
                ArtifactRootResult<LibraryTypeListingResult?>.Available
                    { Value: { } result } =>
                    ApiCommand.WriteLibraryNamespaceListingOutput(result),
                ArtifactRootResult<LibraryTypeListingResult?>.Available => 1,
                ArtifactRootResult<LibraryTypeListingResult?>.Rejected
                    rejected =>
                    WriteRejectedPackageNamespace(rejected.Failure),
                _ => throw new InvalidOperationException(
                    "Package Library namespace execution returned an "
                        + "unknown result."),
            };
        }
        catch (Exception failure)
        {
            CommandError.Write(failure);
            return 1;
        }
    }

    static bool SelectedPathMatches(
        string selectedPath,
        string requestedPath)
    {
        string normalized = requestedPath.Replace('\\', '/');
        if (!normalized.EndsWith(
                ".dll",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".dll";
        }
        return selectedPath.Equals(
            normalized,
            StringComparison.OrdinalIgnoreCase);
    }

    private static int WriteRejectedPackageNamespace(
        ArtifactRootFailure failure)
    {
        CommandError.Write(
            "The admitted Package Library namespace request was rejected.",
            $"Reason: {failure}");
        return 1;
    }
}
