using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using QuerySpace.Rows;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal static class DependsShareProjection
{
    private const string BrowserPlatformPackageId = "Microsoft.NETCore.App";

    internal sealed record AssetSharePreparation(
        InspectionPortableProjection PortableProjection,
        PackageSourceCoordinate? Coordinate = null,
        PackageSourceAuthorization? Authorization = null);

    internal static string? ValidateOptions(DependsOptions options)
    {
        if (options.ShareFormat is null)
            return null;

        if (options.EnvelopeOutput || options.OutputPath is not null)
        {
            return "--share cannot be combined with --envelope or --out while "
                + "Package Dependencies uses scalar-only Share output.";
        }

        if (options.OutputFormatExplicitlySet
            || options.LineWindowExplicitlySet
            || options.CompactJson
            || options.Tree
            || options.NoHeader
            || options.Rows is not null
            || options.Count
            || options.Depth is not null
            || options.IncludePrerelease
            || options.MaxPackages is not null
            || options.PackagePrefix is not null
            || options.Discover is not null
            || options.Effective
            || options.Schema
            || options.Select is not null
            || options.SelectDefault
            || options.Columns is not null
            || options.Fields is not null)
        {
            return "--share selects packet or URL output and cannot be combined "
                + "with other output formatting or projection options.";
        }

        return null;
    }

    internal static async Task<int> WriteAsync(
        DependsOptions options,
        HttpClient httpClient,
        VerboseLogger logger,
        CancellationToken cancellationToken = default)
    {
        AssetSharePreparation preparation =
            await PrepareAssetAsync(
                options,
                httpClient,
                logger,
                cancellationToken).ConfigureAwait(false);
        return WriteAsset(
            preparation.PortableProjection,
            options.ShareFormat!.Value);
    }

    internal static int WriteAsset(
        InspectionPortableProjection share,
        WorkspaceShareFormat format)
    {
        return WorkspaceShareOutput.WriteScalar(share, format);
    }

    internal static async Task<InspectionPortableProjection> ProjectAssetAsync(
        DependsOptions options,
        HttpClient httpClient,
        VerboseLogger logger,
        CancellationToken cancellationToken = default) =>
        (await PrepareAssetAsync(
            options,
            httpClient,
            logger,
            cancellationToken).ConfigureAwait(false)).PortableProjection;

    internal static async Task<AssetSharePreparation> PrepareAssetAsync(
        DependsOptions options,
        HttpClient httpClient,
        VerboseLogger logger,
        CancellationToken cancellationToken = default)
    {
        string packageReference = options.PackageName!;
        if (packageReference.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase))
        {
            return NonProjectableAssetPreparation(
                "--share requires an exact NuGet.org package coordinate; "
                + "local package archives cannot be restored by the published Browser.");
        }

        (string packageId, string? versionText) =
            PackageReferenceParser.Parse(packageReference);
        if (!PackageCoordinateResolver.IsCanonicalPackageId(packageId))
        {
            return NonProjectableAssetPreparation(
                "--share requires a valid NuGet package id.");
        }
        if (string.Equals(
                packageId,
                BrowserPlatformPackageId,
                StringComparison.OrdinalIgnoreCase))
        {
            return NonProjectableAssetPreparation(
                $"--share cannot project NuGet package '{packageId}' because "
                + "the published Browser reserves that id for the .NET Platform.");
        }
        if (!TryNormalizeFramework(
                options.Tfm,
                out string? framework))
        {
            return NonProjectableAssetPreparation(
                "--share requires one valid target framework with --tfm.");
        }

        PackageSourceAuthorization sourceAuthorization =
            new SourcePolicyPackageSourceAuthorization(options.SourceOptions)
                .AuthorizeSourcesFor(packageId);
        if (sourceAuthorization.DenialReason is { } denialReason)
        {
            return NonProjectableAssetPreparation(
                "--share could not apply the effective package source policy: "
                + denialReason);
        }
        if (sourceAuthorization.Sources.Count != 1
            || !sourceAuthorization.Sources[0].IsNuGetOrg)
        {
            return NonProjectableAssetPreparation(
                "--share requires the effective package source policy to "
                + "authorize exactly one NuGet.org source because the "
                + "published Browser cannot preserve another source selection.");
        }

        string? requestedVersion = string.Equals(
                versionText,
                "latest",
                StringComparison.OrdinalIgnoreCase)
            ? null
            : versionText;
        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                httpClient,
                new PackageCoordinate(
                    packageId,
                    requestedVersion,
                    framework),
                sourceAuthorization.Sources,
                logger.Log,
                includePrerelease: false,
                useVersionCache: false,
                requireStableFloating: false,
                cancellationToken).ConfigureAwait(false);
        if (resolution
            is not PackageCoordinateResolution.Resolved resolved)
        {
            (
                InspectionPortableProjectionFailureReason reason,
                string message) = resolution switch
            {
                PackageCoordinateResolution.Invalid invalid =>
                    (
                        InspectionPortableProjectionFailureReason.Invalid,
                        invalid.Message),
                PackageCoordinateResolution.Unavailable unavailable =>
                    (
                        InspectionPortableProjectionFailureReason.Unavailable,
                        unavailable.Message),
                _ => (
                    InspectionPortableProjectionFailureReason.Failed,
                    "The package coordinate could not be resolved."),
            };
            return new AssetSharePreparation(
                new InspectionPortableProjection.NonProjectable(
                    reason,
                    location: "package-coordinate",
                    explanation: "--share could not resolve an exact NuGet.org "
                        + $"package coordinate: {message}"));
        }
        string normalizedVersion = resolved.Coordinate.Version;
        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                packageId,
                normalizedVersion,
                framework);
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework: framework,
                    members: [coordinate]),
            ]);
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.NavigationId,
            [
                new NavigationTabDefinition(
                    "t0",
                    coordinate: coordinate),
            ],
            "t0");
        var view = new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.ViewId,
            lens: "dependencies");
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
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
                    scenario));
        if (!projection.Succeeded)
        {
            WorkspaceSharePacketProjectionFailure failure =
                projection.Failure!;
            return NonProjectableAssetPreparation(
                $"The package dependency view is not projectable at "
                + $"{failure.Path}: {failure.Message}");
        }

        string encoded =
            WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new AssetSharePreparation(
            new InspectionPortableProjection.Available(
                WorkspaceShareOutput.UrlPrefix + encoded,
                encoded),
            PackageSourceCoordinate.Create(
                resolved.Coordinate.PackageId,
                resolved.Coordinate.Version),
            sourceAuthorization);
    }

    internal static InspectionPortableProjection ProjectType(
        DependsOptions options,
        string typeName)
    {
        if (options.Packages.Length != 1
            || options.Assemblies.Length > 0
            || options.Projects.Length > 0
            || options.PlatformAssemblies.Length > 0
            || options.PlatformFrameworks.Length > 0)
        {
            return NonProjectableShare(
                "type dependency Share requires exactly one package root and no other dependency source.");
        }
        if (options.Depth is not null)
        {
            return NonProjectableShare(
                "the published Browser cannot preserve --depth for type dependencies.");
        }
        RowQueryIntent? relationshipRows =
            options.QueryPlan?.RelationshipRows
            ?? options.TypeDependencyRowQuery;
        if (relationshipRows is not null
            && (relationshipRows.Predicates.Count > 0
                || relationshipRows.BaselineOrder is not null
                || relationshipRows.Selection.Operations.Any(operation =>
                    operation.Kind is RowSelectionStageKind.Top)))
        {
            return NonProjectableShare(
                "the published Browser cannot preserve type dependency query "
                + "predicates, ordering, or --top.");
        }

        string packageReference = options.Packages[0];
        if (packageReference.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase))
        {
            return NonProjectableShare(
                "local package archives cannot be restored by the published Browser.");
        }

        (string packageId, string? versionText) =
            PackageReferenceParser.Parse(packageReference);
        if (!PackageCoordinateResolver.IsCanonicalPackageId(packageId))
        {
            return NonProjectableShare("the package id is not canonical.");
        }
        if (string.Equals(
                packageId,
                BrowserPlatformPackageId,
                StringComparison.OrdinalIgnoreCase))
        {
            return NonProjectableShare(
                $"the published Browser reserves package '{packageId}' for the .NET Platform.");
        }
        if (string.IsNullOrWhiteSpace(versionText)
            || string.Equals(versionText, "latest", StringComparison.OrdinalIgnoreCase)
            || versionText.Contains('+', StringComparison.Ordinal)
            || !NuGetVersion.TryParse(versionText, out NuGetVersion? version))
        {
            return NonProjectableShare(
                "the package root must include one exact normalized NuGet version.");
        }
        if (!TryNormalizeFramework(options.Tfm, out string? framework))
        {
            return NonProjectableShare(
                "one valid target framework is required with --tfm.");
        }

        PackageSourceAuthorization sourceAuthorization =
            new SourcePolicyPackageSourceAuthorization(options.SourceOptions)
                .AuthorizeSourcesFor(packageId);
        if (sourceAuthorization.DenialReason is { } denialReason)
        {
            return NonProjectableShare(
                "the effective package source policy was denied: "
                + denialReason);
        }
        if (sourceAuthorization.Sources.Count != 1
            || !sourceAuthorization.Sources[0].IsNuGetOrg)
        {
            return NonProjectableShare(
                "the effective package source policy must authorize exactly one NuGet.org source.");
        }

        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                packageId,
                version.ToNormalizedString(),
                framework);
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework: framework,
                    members: [coordinate]),
            ]);
        var navigation = new NavigationDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.NavigationId,
            [
                new NavigationTabDefinition(
                    "t0",
                    coordinate: coordinate),
            ],
            "t0");
        var view = new ViewDefinition(
            InspectionDefinitionSchema.Version1,
            WorkspaceSharePacketTransposer.ViewId,
            lens: "dependencies",
            type: typeName);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version1,
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
                    scenario));
        if (!projection.Succeeded)
        {
            WorkspaceSharePacketProjectionFailure failure =
                projection.Failure!;
            return NonProjectableShare(
                $"the type dependency view is not projectable at "
                + $"{failure.Path}: {failure.Message}");
        }

        string encoded = WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new InspectionPortableProjection.Available(
            WorkspaceShareOutput.UrlPrefix + encoded,
            encoded);
    }

    private static InspectionPortableProjection NonProjectableShare(string reason) =>
        new InspectionPortableProjection.NonProjectable(
            InspectionPortableProjectionFailureReason.NotSupported,
            explanation: reason);

    internal static bool TryNormalizeFramework(
        string? value,
        out string? framework)
    {
        framework = null;
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            NuGetFramework parsed = NuGetFramework.ParseFolder(value);
            if (parsed.IsUnsupported)
                return false;

            string normalized =
                parsed.GetShortFolderName().ToLowerInvariant();
            if (!PackageCoordinateResolver.IsAcquisitionTargetText(normalized))
                return false;

            framework = normalized;
            return true;
        }
        catch (FrameworkException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static InspectionPortableProjection.NonProjectable
        NonProjectableAssetShare(string reason) =>
            new(
                InspectionPortableProjectionFailureReason.NotSupported,
                explanation: reason);

    private static AssetSharePreparation
        NonProjectableAssetPreparation(string reason) =>
            new(NonProjectableAssetShare(reason));
}
