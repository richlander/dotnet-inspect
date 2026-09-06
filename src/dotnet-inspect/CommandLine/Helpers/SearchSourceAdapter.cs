using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.CommandLine;

internal sealed class SearchSourceValidationException(string message) : Exception(message);

internal static class SearchSourceAdapter
{
    internal static SourceIntent Declare(
        ParseResult parseResult,
        Option<string[]> packageOption,
        Option<string[]> libraryOption,
        Option<string[]> projectOption,
        Option<bool> platformOption,
        Option<string[]> platformLibraryOption,
        Option<bool> extensionsOption,
        Option<bool> aspNetCoreOption,
        Option<string[]>? binOption = null,
        Option<string?>? prefixOption = null)
    {
        try
        {
            return SourceIntent.Create(ReadSelectors());
        }
        catch (ArgumentException error)
        {
            throw new SearchSourceValidationException(error.Message);
        }

        IEnumerable<SourceSelector> ReadSelectors()
        {
            foreach (string package in parseResult.GetValue(packageOption) ?? [])
            {
                if (package.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    yield return new SourceSelector.PackageArchive(package);
                else
                {
                    var (name, version) = PackageReferenceParser.Parse(package);
                    yield return new SourceSelector.PackageReference(name, version);
                }
            }

            if (prefixOption is not null && parseResult.GetValue(prefixOption) is { } prefix)
                yield return new SourceSelector.PackagePrefix(
                    new(prefix, ScopeConstants.PackagePrefixExpansionLimit));
            foreach (string library in parseResult.GetValue(libraryOption) ?? [])
                yield return new SourceSelector.Library(library);
            foreach (string library in parseResult.GetValue(platformLibraryOption) ?? [])
                yield return new SourceSelector.PlatformLibrary(library);
            foreach (string project in parseResult.GetValue(projectOption) ?? [])
                yield return new SourceSelector.Project(project);
            if (binOption is not null)
            {
                foreach (string directory in parseResult.GetValue(binOption) ?? [])
                    yield return new SourceSelector.BinaryDirectory(directory);
            }

            if (parseResult.GetValue(platformOption))
                yield return new SourceSelector.PlatformGroup();
            if (parseResult.GetValue(extensionsOption))
                yield return PackageGroup(PackageSetIds.MicrosoftExtensions);
            if (parseResult.GetValue(aspNetCoreOption))
                yield return PackageGroup(PackageSetIds.AspNetCore);
        }
    }

    internal static async Task<(SearchSourceSelection Selection, AssemblySetRequest Request)> BindAsync(
        SourceIntent intent,
        HttpClient client,
        bool verbose,
        NuGetSourceOptions? sourceOptions)
    {
        SearchSourceSelection selection = SearchSourceNormalizer.Normalize(intent);
        List<SourceSelector>? expanded = null;
        foreach (var prefix in selection.OtherSources.OfType<SourceSelector.PackagePrefix>())
        {
            SourceSelector.PackageReference[] packages = await CommandLineHelpers.ResolvePrefixPackagesAsync(
                prefix.Request, client, verbose, sourceOptions);
            expanded ??= [.. intent.Selectors];
            expanded.AddRange(packages);
        }

        // Augment, never replace, the declaration for acquisition ordering. The retained
        // selection still describes the user's intent, including an empty prefix result.
        var packageSources = expanded is null
            ? selection.Packages
            : SearchSourceNormalizer.Normalize(SourceIntent.Create(expanded)).Packages;
        var request = new AssemblySetRequest
        {
            Packages = packageSources.Select(PackageArgument).ToArray(),
            Assemblies = selection.OtherSources.OfType<SourceSelector.Library>()
                .Select(source => source.Path).ToArray(),
            PlatformAssemblies = selection.OtherSources.OfType<SourceSelector.PlatformLibrary>()
                .Select(source => source.Name).ToArray(),
            Projects = selection.OtherSources.OfType<SourceSelector.Project>()
                .Select(source => source.Path).ToArray(),
            Directories = selection.OtherSources.OfType<SourceSelector.BinaryDirectory>()
                .Select(source => source.Path).ToArray(),
            PlatformFrameworks = selection.Frameworks.Select(framework => framework switch
            {
                SearchPlatformFramework.Runtime => "runtime",
                SearchPlatformFramework.AspNetCore => "aspnetcore",
                SearchPlatformFramework.NetStandard => "netstandard",
                _ => throw new InvalidOperationException("Unknown search framework."),
            }).ToArray(),
            SourceOptions = sourceOptions,
        };
        return (selection, request);
    }

    private static string PackageArgument(SourceSelector.PackageSource source) => source switch
    {
        SourceSelector.PackageArchive archive => archive.Path,
        SourceSelector.PackageReference reference => Reference(reference.PackageId, reference.Version),
        SourceSelector.Package { Coordinate: { Framework: not null } or { RuntimeIdentifier: not null } } =>
            throw new SearchSourceValidationException(
                "Search package scopes do not support per-package framework or runtime qualifiers."),
        SourceSelector.Package package => Reference(package.Coordinate.PackageId, package.Coordinate.Version),
        _ => throw new InvalidOperationException("Unknown package source."),
    };

    private static string Reference(string packageId, string? version) =>
        version is null ? packageId : $"{packageId}@{version}";

    private static SourceSelector.PackageGroup PackageGroup(PackageSetId id) =>
        PackageSetCatalog.Lookup(id) switch
        {
            PackageSetLookupResult.Known known => new(known.Descriptor.Members),
            PackageSetLookupResult.Unknown unknown =>
                throw new InvalidOperationException(
                    $"Shipped package set '{unknown.Id}' is not registered."),
            _ => throw new InvalidOperationException(
                $"Shipped package-set lookup returned an unsupported result for '{id}'."),
        };
}
