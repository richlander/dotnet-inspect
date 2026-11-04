using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Services;
using DotnetInspector.Queries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

public static partial class TypeCommand
{
    static async Task<ApiSurface?> ReadPlatformCatalogAsync(
        string path,
        string? framework,
        string? version,
        TypeOptions options,
        VerboseLogger logger)
    {
        ResolvedAssemblyReference? assembly = ApiServices.SelectRootAssembly(
            path, ApiServices.CreateRootProvenance(
                SourceKind.Platform, version, null, null, options, framework));
        if (assembly is null)
        {
            return ApiServices.LoadFullApi(
                path, null, null, null, SourceKind.Platform, version,
                null, logger, options, useTypedSelection: true,
                platformFramework: framework)?.Api;
        }

        using var resolution = new TypeDefinitionResolutionSession(
            assembly, isPlatformAssembly: true,
            options.ProjectAssetsPath, options.Tfm, framework,
            sourceOptions: options.SourceOptions);
        IAssemblyBindingPolicy policy = resolution.BindingContext(
            AssemblyBindingOccurrence.Seed(assembly)).Policy;
        return await LibraryTypeCatalogInspectionOperation.ExecuteWithLiveSourcesAsync(
            assembly, policy,
            options.IncludeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public,
            (inspection, _, _) => ValueTask.FromResult(
                (inspection.Content.Assemblies.Assemblies.Single()
                    as AssemblyContextEntry<AssemblyApiSurface>.Available)?.Value.Surface))
            .ConfigureAwait(false);
    }

    static async Task<int?> TryExecuteResolvedCatalogAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        ApiSourceResult source,
        CancellationToken cancellationToken)
    {
        string? path = ApiServices.FindApiDll(source.SearchPath, source.Context.Logger);
        if (path is null)
        {
            CommandError.Write("Could not extract API from library.");
            return 1;
        }
        ResolvedAssemblyReference? assembly = ApiServices.SelectRootAssembly(
            path,
            ApiServices.CreateRootProvenance(
                source.ApiSource, source.ApiVersion, source.PackageName,
                source.SelectedTfm, options, source.PlatformFramework));
        if (assembly is null)
            return null;

        using var resolution = new TypeDefinitionResolutionSession(
            assembly,
            source.RuntimeAssemblyPath is not null || source.ApiSource == SourceKind.Platform,
            options.ProjectAssetsPath,
            options.Tfm ?? source.SelectedTfm,
            source.PlatformFramework ?? options.PlatformFramework,
            source.PackageExtractPath,
            options.SourceOptions,
            usePackageSourcePolicy: source.PackageExtractPath is not null);
        SelectedTypeBindingContext rootBinding = resolution.BindingContext(
            AssemblyBindingOccurrence.Seed(assembly));
        return await LibraryTypeCatalogInspectionOperation.ExecuteWithLiveSourcesAsync(
            assembly, rootBinding.Policy,
            options.IncludeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public,
            async (envelope, sources, token) =>
            {
                if (envelope.Content.Assemblies.Assemblies.Single()
                    is not AssemblyContextEntry<AssemblyApiSurface>.Available available)
                {
                    WriteInspectionDiagnostics(envelope.Diagnostics);
                    return 1;
                }

                ApiSurface api = available.Value.Surface;
                api.Name = string.IsNullOrEmpty(source.ResolvedPackagePath)
                    ? Path.GetFileNameWithoutExtension(path)
                    : source.PackageName
                        ?? PackageExtractor.ParsePackageReference(source.ResolvedPackagePath).Item1;
                api.Tfm = source.SelectedTfm;
                api.Source = source.ApiSource;
                api.Version = source.ApiVersion;
                api.Library = Path.GetFileName(path);
                var loaded = new ApiServices.LoadedApiSurface(
                    api, path, source.RuntimeAssemblyPath ?? path,
                    sources.ToDictionary(item => item.Key, item => item.Value.Assembly),
                    RootBindingContext: rootBinding,
                    BindingContexts: sources.ToDictionary(
                        item => item.Key,
                        item => new SelectedTypeBindingContext(
                            item.Value.Occurrence, item.Value.BindingPolicy)));
                return await ExecuteCoreAsync(
                    options, plan, source, loaded, cancellationToken: token)
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }
}
