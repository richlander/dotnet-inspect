using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NuGetFetch;

namespace DotnetInspector.Commands;

internal static class MemberShareProjection
{
    private const string ShareUrlPrefix = "https://dotnet-inspect.net/?w=";

    internal static string? ValidateOptions(MemberOptions options)
    {
        if (options.ShareFormat is null)
            return null;

        if (options.IncludeAll)
        {
            return "--share supports the Browser's public API surface and cannot "
                + "be combined with --all.";
        }
        if (options.Select is { Length: > 0 }
            || options.SelectDefault
            || options.Discover is not null
            || options.Columns is { Length: > 0 }
            || options.Fields is { Length: > 0 }
            || options.BodyKindQuery.HasFilter
            || options.PerformanceTriage.HasFilters)
        {
            return "--share emits the Browser member Overview and cannot be "
                + "combined with section, discovery, or analysis selection.";
        }
        if (options.FormatFlagExplicitlySet
            || options.LegacyUrlModeExplicitlySet
            || options.CompactJson
            || options.PlainText
            || options.MarkdownExplicitlySet
            || options.MermaidOutput
            || options.EmbeddedMermaid
            || options.Bare
            || options.NoHeader
            || options.Print
            || options.Value
            || options.Urls
            || options.Paths
            || options.JsonArray
            || options.Count
            || options.Rows is not null)
        {
            return "--share selects packet or URL output and cannot be combined "
                + "with other output formatting or projection options.";
        }
        if (options.Limit is not null
            || options.Focus is not null
            || options.RequestAllTaste
            || options.RequestReadableLocalNames
            || options.SourceRepositories.Length > 0
            || options.HasCallerScope)
        {
            return "--share cannot be combined with member rendering, source, "
                + "or caller-scope modifiers.";
        }

        return null;
    }

    internal static void WriteExactSelectorRequired() =>
        CommandError.Write(
            "--share requires one exact member overload selected with "
            + "Name~<digest>, Name:N, or --index N.");

    internal static int Write(
        ApiSourceResult source,
        ApiServices.LoadedApiSurface loaded,
        ApiType type,
        ApiMember member,
        MemberShareFormat format)
    {
        bool isNuGetOrg =
            source.PackageAuthority?.Source.IsNuGetOrg == true
            || string.Equals(
                source.PackageProducerKey,
                PackageProducerIdentity.NuGetOrg.Key,
                StringComparison.Ordinal)
            || string.Equals(
                source.PackageProducerKey,
                NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
                StringComparison.Ordinal);
        if (!string.Equals(
                source.ApiSource,
                SourceKind.NuGet,
                StringComparison.Ordinal)
            || !isNuGetOrg)
        {
            return NonProjectable(
                "--share currently supports packages acquired from NuGet.org; "
                + "local, project, platform, and private-feed inputs cannot be "
                + "restored by the published Browser.");
        }
        if (string.IsNullOrWhiteSpace(source.PackageName)
            || string.IsNullOrWhiteSpace(source.PackageVersion)
            || string.IsNullOrWhiteSpace(source.SelectedTfm)
            || string.IsNullOrWhiteSpace(source.PackageExtractPath))
        {
            return NonProjectable(
                "--share requires one resolved package id, exact version, and "
                + "target framework.");
        }
        if (type.DefinitionName is null)
        {
            return NonProjectable(
                "The selected type has no structured metadata identity and "
                + "cannot be restored by a Workspace packet.");
        }

        ResolvedAssemblyReference sourceAssembly =
            loaded.GetSourceAssembly(type);
        if (string.IsNullOrWhiteSpace(sourceAssembly.Path))
        {
            return NonProjectable(
                "The selected type has no package-library path and cannot be "
                + "restored by the Browser.");
        }

        TfmSelector.PackageLibraryResolution libraries =
            TfmSelector.SelectPackageLibraries(
                source.PackageExtractPath,
                source.SelectedTfm);
        if (!libraries.IsSelected)
        {
            return NonProjectable(
                $"The Browser library set for target framework "
                + $"'{source.SelectedTfm}' could not be determined.");
        }

        var pathComparer =
            LibraryMetadataService.ReferenceTreePathComparer(
                OperatingSystem.IsWindows());
        string selectedPath = Path.GetFullPath(sourceAssembly.Path);
        if (!libraries.Paths.Any(path =>
                pathComparer.Equals(
                    Path.GetFullPath(path),
                    selectedPath)))
        {
            return NonProjectable(
                "The selected member is implemented outside the package's "
                + "Browser-visible library set.");
        }

        int matchingTypeCount = 0;
        foreach (string libraryPath in libraries.Paths)
        {
            ApiSurface? surface = AssemblyReader.ExtractApiSurface(
                libraryPath,
                includeAll: true,
                typesOnly: true);
            if (surface is null)
            {
                return NonProjectable(
                    $"Browser type identity could not be checked in package "
                    + $"library '{Path.GetFileName(libraryPath)}'.");
            }

            matchingTypeCount += surface.Types.Count(candidate =>
                Equals(candidate.DefinitionName, type.DefinitionName));
        }
        if (matchingTypeCount != 1)
        {
            return NonProjectable(
                matchingTypeCount == 0
                    ? "The selected type is not present in the Browser-visible "
                        + "package surface."
                    : "The selected type requires an assembly-qualified Browser "
                        + "key; this first --share slice supports only package-unique "
                        + "type identities.");
        }

        string typeKey = type.DefinitionName.ToEscapedFullName();
        string libraryKey =
            Path.GetFileNameWithoutExtension(sourceAssembly.Path);
        MemberAnchor anchor =
            ApiMemberIdentity.GetMemberAnchor(type, member);
        var coordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                source.PackageName,
                source.PackageVersion,
                source.SelectedTfm);
        var workspace = new WorkspaceDefinition(
            InspectionDefinitionJson.CurrentSchemaVersion,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [
                new WorkspaceContextDefinition(
                    "g0",
                    framework: source.SelectedTfm,
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
            lens: "api",
            type: typeKey,
            memberAnchor: anchor.Fingerprint,
            libraries: [libraryKey]);
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
                $"The exact member is not projectable at {failure.Path}: "
                + failure.Message);
        }

        string packet =
            WorkspaceSharePacketCodec.Encode(projection.Packet!);
        Console.WriteLine(
            format == MemberShareFormat.Url
                ? ShareUrlPrefix + packet
                : packet);
        return 0;
    }

    private static int NonProjectable(string message)
    {
        CommandError.Write(message);
        return 1;
    }
}
