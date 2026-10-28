using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

// Keeps the restored occurrence and its binding policy alive throughout the
// ordinary Type or Member pipeline, including descriptor-backed body access
// and any required temporary assembly/XML access.
internal static class WorkspaceTypeInspectionTarget
{
    internal static async Task<int> ExecuteAsync(
        ApiOptions options,
        ExactTypeRenderSource renderSource,
        SelectedContextExactTypeLiveTarget target,
        Func<ApiSourceResult, ApiServices.LoadedApiSurface, Task<int>> execute,
        string? packageExtractPathOverride = null,
        bool requiresLocalAssemblyPath = true)
    {
        try
        {
            using WorkspaceTypeAssemblyPath assemblyPath =
                WorkspaceTypeAssemblyPath.Create(
                    target,
                    requiresLocalAssemblyPath);
            string libraryAssetPath =
                renderSource.SelectedLibraryPath
                ?? assemblyPath.Value;
            string operationalAssemblyPath = assemblyPath.Value;
            ApiSurface api = target.Surface;
            api.Name = renderSource.Name;
            api.Version = renderSource.Version;
            api.Source = renderSource.Source;
            api.Tfm = renderSource.TargetFramework;
            api.Library =
                Path.GetFileName(libraryAssetPath);

            var sourceAssemblies =
                new Dictionary<ApiType, ResolvedAssemblyReference>(
                    ReferenceEqualityComparer.Instance)
                {
                    [target.Type] = target.Assembly,
                };
            var bindingContext = new SelectedTypeBindingContext(
                target.Occurrence,
                target.BindingPolicy);
            var bindingContexts =
                new Dictionary<ApiType, SelectedTypeBindingContext>(
                    ReferenceEqualityComparer.Instance)
                {
                    [target.Type] = bindingContext,
                };
            var loaded = new ApiServices.LoadedApiSurface(
                api,
                operationalAssemblyPath,
                assemblyPath.Value,
                sourceAssemblies,
                RootBindingContext: bindingContext,
                BindingContexts: bindingContexts,
                LibraryAssetPath: renderSource.SelectedLibraryPath);
            var source = new ApiSourceResult(
                SearchPath: operationalAssemblyPath,
                RuntimeAssemblyPath:
                    renderSource.Source == SourceKind.Platform
                        ? operationalAssemblyPath
                        : null,
                PackageName: renderSource.PackageName,
                PackageVersion: renderSource.PackageVersion,
                ResolvedPackagePath: renderSource.ResolvedPackagePath,
                PackageExtractPath:
                    packageExtractPathOverride
                    ?? target.PackageExtractPath,
                ApiSource: renderSource.Source,
                ApiVersion: renderSource.Version,
                PlatformFramework: renderSource.PlatformFramework,
                SelectedTfm: renderSource.TargetFramework,
                ProjectAssetsPath: null,
                TempDir: null,
                TypeName:
                    target.Type.DefinitionName?.ToEscapedFullName()
                    ?? target.Type.FullName,
                PackageReplaySourceUrls: null,
                PackageReplayUsesOriginalSources: false,
                Context: new CommandContext(options.Verbose));
            return await execute(source, loaded).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    internal sealed record ExactTypeRenderSource(
    string Name,
    string? Version,
    string Source,
    string? TargetFramework,
    string? PackageName,
    string? PackageVersion,
    string? ResolvedPackagePath,
    string? PlatformFramework,
    string? SelectedLibraryPath)
    {
        internal static ExactTypeRenderSource From(
            ExactTypeInspectionRequest request) =>
            new(
                request.PackageId,
                request.Version,
                SourceKind.NuGet,
                request.TargetFramework,
                request.PackageId,
                request.Version,
                $"{request.PackageId}@{request.Version}",
                PlatformFramework: null,
                SelectedLibraryPath: null);

        internal static ExactTypeRenderSource From(
            SelectedContextExactTypeSource source)
        {
            return source.Observation.Realization switch
            {
                TypeDeclarationLocatorRealization.PackageRealization package =>
                    new(
                        package.PackageId,
                        package.Version,
                        SourceKind.NuGet,
                        package.Framework,
                        package.PackageId,
                        package.Version,
                        $"{package.PackageId}@{package.Version}",
                        PlatformFramework: null,
                        SelectedLibraryPath: null),
                TypeDeclarationLocatorRealization.PlatformRealization platform =>
                    new(
                        platform.Family,
                        platform.Version,
                        SourceKind.Platform,
                        platform.Framework,
                        PackageName: null,
                        PackageVersion: null,
                        ResolvedPackagePath: null,
                        PlatformFramework: platform.Framework,
                        SelectedLibraryPath: null),
                _ => new(
                    source.Library.LibraryIdentity.Name,
                    source.Library.LibraryIdentity.Version?.ToString(),
                    source.Library
                        is TypeDeclarationLocatorSectionCoordinate
                            .ProjectCoordinate
                            ? SourceKind.Project
                            : SourceKind.Library,
                    source.Observation.Selection switch
                    {
                        TypeDeclarationLocatorSelection.ProjectSelection project =>
                            project.Tfm,
                        _ => null,
                    },
                    PackageName: null,
                    PackageVersion: null,
                    ResolvedPackagePath: null,
                    PlatformFramework: null,
                    SelectedLibraryPath: null),
            };
        }

        internal static ExactTypeRenderSource From(
            ApiSourceResult source) =>
            new(
                source.PackageName
                    ?? Path.GetFileNameWithoutExtension(source.SearchPath),
                source.PackageVersion ?? source.ApiVersion,
                source.ApiSource ?? SourceKind.Library,
                source.SelectedTfm,
                source.PackageName,
                source.PackageVersion,
                source.ResolvedPackagePath,
                source.PlatformFramework,
                string.Equals(
                    source.ApiSource,
                    SourceKind.Platform,
                    StringComparison.Ordinal)
                        ? source.SearchPath
                        : null);
    }

    sealed class WorkspaceTypeAssemblyPath : IDisposable
    {
        const long MaximumCompiledDocumentationBytes = 8 * 1024 * 1024;

        WorkspaceTypeAssemblyPath(
            string value,
            string? temporaryDirectory)
        {
            Value = value;
            _temporaryDirectory = temporaryDirectory;
        }

        readonly string? _temporaryDirectory;

        internal string Value { get; }

        internal static WorkspaceTypeAssemblyPath Create(
            SelectedContextExactTypeLiveTarget target,
            bool requiresLocalAssemblyPath)
        {
            if (target.AssemblyPath is { } existing)
            {
                return new WorkspaceTypeAssemblyPath(
                    Path.GetFullPath(existing),
                    temporaryDirectory: null);
            }
            if (target.PackageExtractPath is { } packageExtractPath
                && target.PackageAssetPath is { } packageAssetPath)
            {
                return new WorkspaceTypeAssemblyPath(
                    Path.GetFullPath(
                        Path.Combine(
                            packageExtractPath,
                            packageAssetPath.Replace(
                                '/',
                                Path.DirectorySeparatorChar))),
                    temporaryDirectory: null);
            }

            if (!requiresLocalAssemblyPath)
            {
                string logicalPath =
                    target.PackageAssetPath
                    ?? target.Assembly.Path
                    ?? $"{target.Assembly.Identity.Name}.dll";
                return new WorkspaceTypeAssemblyPath(
                    Path.GetFullPath(logicalPath),
                    temporaryDirectory: null);
            }

            string temporaryDirectory =
                Directory.CreateTempSubdirectory(
                    "inspect-type-workspace").FullName;
            string path = Path.Combine(
                temporaryDirectory,
                "selected.dll");
            try
            {
                using Stream input = target.Assembly.OpenRead();
                using FileStream output = File.Create(path);
                input.CopyTo(output);
                if (target.OpenCompiledDocumentation is { } openDocumentation)
                {
                    using Stream? documentation =
                        openDocumentation(
                            MaximumCompiledDocumentationBytes);
                    if (documentation is not null)
                    {
                        using FileStream documentationOutput =
                            File.Create(
                                Path.ChangeExtension(path, ".xml"));
                        CopyBounded(
                            documentation,
                            documentationOutput,
                            MaximumCompiledDocumentationBytes);
                    }
                }
                return new WorkspaceTypeAssemblyPath(
                    path,
                    temporaryDirectory);
            }
            catch
            {
                TryDeleteTemporaryDirectory(temporaryDirectory);
                throw;
            }
        }

        static void CopyBounded(
            Stream input,
            Stream output,
            long maximumBytes)
        {
            byte[] buffer = GC.AllocateUninitializedArray<byte>(81920);
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += read;
                if (total > maximumBytes)
                {
                    throw new InvalidDataException(
                        "Compiled XML documentation exceeds the configured "
                            + "byte limit.");
                }

                output.Write(buffer, 0, read);
            }
        }

        public void Dispose()
        {
            if (_temporaryDirectory is not null)
                TryDeleteTemporaryDirectory(_temporaryDirectory);
        }

        static void TryDeleteTemporaryDirectory(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }

}
