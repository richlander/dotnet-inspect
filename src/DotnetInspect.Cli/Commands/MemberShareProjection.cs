using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal static class MemberShareProjection
{
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

    internal static InspectionShare Project(
        ApiSourceResult source,
        ApiServices.LoadedApiSurface loaded,
        ApiType type,
        ShareProjectionPlan plan)
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
        // A shared PackageHouse route retains its package content, which may
        // be ranged rather than extracted; the compatibility route extracts.
        IPackageContent? retainedContent = source.PackageContent;
        if (string.IsNullOrWhiteSpace(source.PackageName)
            || string.IsNullOrWhiteSpace(source.PackageVersion)
            || string.IsNullOrWhiteSpace(source.SelectedTfm)
            || retainedContent is null
                && string.IsNullOrWhiteSpace(source.PackageExtractPath))
        {
            return NonProjectable(
                "--share requires one resolved package id, exact version, and "
                + "target framework.");
        }
        if (plan.Basis.Target.TypeDefinition is null)
        {
            return NonProjectable(
                "The selected type has no structured metadata identity and "
                + "cannot be restored by a Workspace packet.");
        }

        string? selectedAssetPath;
        if (retainedContent is not null)
        {
            selectedAssetPath = loaded.LibraryAssetPath;
        }
        else
        {
            ResolvedAssemblyReference sourceAssembly =
                loaded.GetSourceAssembly(type);
            selectedAssetPath =
                string.IsNullOrWhiteSpace(sourceAssembly.Path)
                    ? null
                    : Path.GetRelativePath(
                            source.PackageExtractPath!,
                            Path.GetFullPath(sourceAssembly.Path))
                        .Replace(Path.DirectorySeparatorChar, '/');
        }
        if (string.IsNullOrWhiteSpace(selectedAssetPath))
        {
            return NonProjectable(
                "The selected type has no package-library path and cannot be "
                + "restored by the Browser.");
        }

        IPackageContent content = retainedContent
            ?? new FileSystemPackageContent(
                source.PackageExtractPath!,
                nupkgPath: null,
                fromCache: true,
                source.PackageProducerKey ?? "member-share-projection");
        PackageCompileAssetSelection compileAssets =
            PackageCompileAssetSelector.Select(
                content,
                source.PackageName,
                source.SelectedTfm);
        if (!compileAssets.IsSelected)
        {
            return NonProjectable(
                $"The Browser compile-asset set for target framework "
                + $"'{source.SelectedTfm}' could not be determined "
                + $"({compileAssets.Status}).");
        }

        var pathComparer =
            LibraryMetadataService.ReferenceTreePathComparer(
                OperatingSystem.IsWindows());
        if (!compileAssets.Assets.Any(asset =>
                pathComparer.Equals(
                    asset.Path,
                    selectedAssetPath)))
        {
            return NonProjectable(
                "The selected member is implemented outside the package's "
                + "Browser compile-asset set.");
        }

        int matchingTypeCount = 0;
        foreach (PackageCompileAsset asset in compileAssets.Assets)
        {
            ApiSurface? surface = ReadBrowserTypeSurface(
                content,
                asset.Path,
                MaxCompileAssetBytes,
                out string? refusal);
            if (surface is null)
                return NonProjectable(refusal!);

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

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(plan);
        if (!projection.Succeeded)
        {
            WorkspaceSharePacketProjectionFailure failure =
                projection.Failure!;
            return NonProjectable(
                $"The exact member is not projectable at {failure.Path}: "
                + failure.Message);
        }

        string encoded = WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new InspectionShare.Available(
            WorkspaceShareUrl.Create(encoded), encoded);
    }

    /// <summary>
    /// The per-entry bound under which package role realization admits a
    /// selected assembly entry by default. The shared route has already
    /// admitted these compile assets under it, so Share never reads more.
    /// </summary>
    internal static readonly long MaxCompileAssetBytes =
        new PackageAssemblyContextRealizationOptions().MaxAssemblyEntryBytes;

    /// <summary>
    /// Reads one compile asset's type declarations within
    /// <paramref name="maxBytes"/>. A declared entry length over the bound is
    /// refused before opening; otherwise the bound is enforced while reading,
    /// and a forward-only entry is buffered only up to that bound.
    /// </summary>
    internal static ApiSurface? ReadBrowserTypeSurface(
        IPackageContent content,
        string assetPath,
        long maxBytes,
        out string? refusal)
    {
        string uncheckedLibrary =
            "Browser type identity could not be checked in package library "
            + $"'{Path.GetFileName(assetPath)}'";
        string overBound =
            $"{uncheckedLibrary}: it exceeds the {maxBytes}-byte assembly-entry limit.";
        refusal = uncheckedLibrary + ".";
        if (content is IPackageContentEntryManifest manifest
            && manifest.TryGetEntryLength(assetPath, out long declared)
            && declared > maxBytes)
        {
            refusal = overBound;
            return null;
        }
        if (!content.TryOpenEntry(assetPath, maxBytes, out Stream? stream))
            return null;

        using (stream)
        {
            Stream image;
            if (stream.CanSeek)
            {
                if (stream.Length > maxBytes)
                {
                    refusal = overBound;
                    return null;
                }
                image = stream;
            }
            else
            {
                var buffer = new MemoryStream();
                byte[] chunk = new byte[81920];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > maxBytes)
                    {
                        refusal = overBound;
                        return null;
                    }
                    buffer.Write(chunk, 0, read);
                }
                buffer.Position = 0;
                image = buffer;
            }

            ApiSurface? surface = AssemblyReader.ExtractApiSurface(
                image,
                includeAll: true,
                typesOnly: true);
            if (surface is not null)
                refusal = null;
            return surface;
        }
    }

    private static InspectionShare NonProjectable(string message) =>
        new InspectionShare.NonProjectable("member/share", message);
}
