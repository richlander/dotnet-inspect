using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace DotnetInspector.Commands;

internal static class DependsShareProjection
{
    internal static string? ValidateOptions(DependsOptions options)
    {
        if (options.ShareFormat is null)
            return null;

        if (options.OutputFormatExplicitlySet
            || options.CompactJson
            || options.Rows is not null
            || options.Count)
        {
            return "--share selects packet or URL output and cannot be combined "
                + "with other output formatting or projection options.";
        }

        NuGetSourceOptions sources =
            options.SourceOptions ?? NuGetSourceOptions.Default;
        if (sources.Sources.Length > 0
            || sources.AdditionalSources.Length > 0
            || sources.ConfigFile is not null
            || sources.ConfigDirectory is not null)
        {
            return "--share targets the published Browser's NuGet.org source "
                + "and cannot be combined with source configuration.";
        }

        return null;
    }

    internal static int Write(DependsOptions options)
    {
        string packageReference = options.PackageName!;
        if (packageReference.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase))
        {
            return NonProjectable(
                "--share requires an exact NuGet.org package coordinate; "
                + "local package archives cannot be restored by the published Browser.");
        }

        (string packageId, string? versionText) =
            PackageReferenceParser.Parse(packageReference);
        if (!PackageCoordinateResolver.IsCanonicalPackageId(packageId))
        {
            return NonProjectable(
                "--share requires a valid NuGet package id.");
        }
        if (versionText is null
            || versionText.Contains('*', StringComparison.Ordinal)
            || string.Equals(
                versionText,
                "latest",
                StringComparison.OrdinalIgnoreCase)
            || versionText.Contains('+', StringComparison.Ordinal)
            || !NuGetVersion.TryParse(
                versionText,
                out NuGetVersion? version))
        {
            return NonProjectable(
                "--share requires one exact NuGet package version without "
                + "ranges, wildcards, or build metadata.");
        }
        if (!TryNormalizeFramework(
                options.Tfm,
                out string? framework))
        {
            return NonProjectable(
                "--share requires one valid target framework with --tfm.");
        }

        string normalizedVersion =
            version.ToNormalizedString().ToLowerInvariant();
        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                packageId,
                normalizedVersion,
                framework);
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework: framework,
                    members: [coordinate]),
            ]);
        var navigation = new NavigationDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.NavigationId,
            [
                new NavigationTabDefinition(
                    "t0",
                    coordinate: coordinate),
            ],
            "t0");
        var view = new ViewDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.ViewId,
            lens: "dependencies");
        var scenario = new ScenarioDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
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
            return NonProjectable(
                $"The package dependency view is not projectable at "
                + $"{failure.Path}: {failure.Message}");
        }

        return WorkspaceShareOutput.Write(
            projection.Packet!,
            options.ShareFormat!.Value);
    }

    private static bool TryNormalizeFramework(
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

    private static int NonProjectable(string message)
    {
        CommandError.Write(message);
        return 1;
    }
}
