using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal static class DecompilationInspectionPreparation
{
    internal sealed record Prepared(
        ResolvedAssemblyReference Assembly,
        AssemblyContextParticipant Participant,
        AssemblyContextSourceQueryContext QueryContext,
        AssemblyContextLibraryPortablePdb? PortablePdb);

    internal static async Task<Prepared> CreateAsync(
        string assemblyPath,
        ResolvedAssemblyReference? selectedAssembly,
        ApiOptions options,
        HttpClient httpClient,
        string provenanceDescription,
        CancellationToken cancellationToken = default)
    {
        ResolvedAssemblyReference assembly =
            selectedAssembly
            ?? ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local(
                    provenanceDescription));
        var bindingPolicy =
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(
                    assembly.Path
                    ?? assemblyPath)
                {
                    ProjectAssetsPath =
                        options.ProjectAssetsPath,
                    TargetFramework = options.Tfm,
                    IncludeDepsJsonAssets = false,
                    IncludeAspNetCoreSharedFramework = false,
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                });
        var participant =
            new AssemblyContextParticipant(
                assembly,
                bindingPolicy);
        var queryContext =
            new AssemblyContextSourceQueryContext(
                httpClient,
                new InMemoryPdbStore(),
                new SourcePolicyPackageSourceAuthorization(
                    options.SourceOptions),
                new SourceFetch(
                    DotnetInspector.Networking.HttpClientFactory
                        .SharedUntrustedFetch))
            {
                NuGetSourceOptions = options.SourceOptions,
            };

        string? pdbPath = options.PdbPath;
        if (pdbPath is null
            && assembly.Path is { } selectedPath)
        {
            string adjacentPath =
                Path.ChangeExtension(selectedPath, ".pdb");
            if (File.Exists(adjacentPath))
                pdbPath = adjacentPath;
        }
        AssemblyContextLibraryPortablePdb? portablePdb =
            pdbPath is null
                ? null
                : new(
                    ImmutableArray.CreateRange(
                        await File.ReadAllBytesAsync(
                                pdbPath,
                                cancellationToken)
                            .ConfigureAwait(false)),
                    new AssemblySourcePdbProvenance(
                        assembly.Registration,
                        Identity: null,
                        Location: pdbPath,
                        Path: pdbPath,
                        SymbolServer: null));

        return new(
            assembly,
            participant,
            queryContext,
            portablePdb);
    }
}
