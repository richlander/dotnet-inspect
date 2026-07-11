using DotnetInspector.Packages;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Services;

/// <summary>
/// Neutral request for collecting assemblies from package, platform, project, and local sources.
/// </summary>
public sealed record AssemblySetRequest
{
    public IReadOnlyList<string> Packages { get; init; } = [];
    public IReadOnlyList<string> Assemblies { get; init; } = [];
    public IReadOnlyList<string> PlatformAssemblies { get; init; } = [];
    public IReadOnlyList<string> PlatformFrameworks { get; init; } = [];
    public IReadOnlyList<string> Projects { get; init; } = [];
    public IReadOnlyList<string> Directories { get; init; } = [];
    public string? Tfm { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
    public string TempDirPrefix { get; init; } = "inspect";
    public string? PlatformAssemblyFrameworkHint { get; init; }
    public bool IncludePackageRuntimeAssemblies { get; init; }
    public AssemblySetPackageSelectionMode PackageSelectionMode { get; init; } =
        AssemblySetPackageSelectionMode.TargetFramework;
    public IReadOnlyList<AssemblySetSourceKind> SourceOrder { get; init; } =
    [
        AssemblySetSourceKind.Package,
        AssemblySetSourceKind.Assembly,
        AssemblySetSourceKind.Project,
        AssemblySetSourceKind.PlatformAssembly,
        AssemblySetSourceKind.PlatformFramework,
        AssemblySetSourceKind.Directory,
    ];
}

public sealed record AssemblySetEntry(
    string Path,
    string Source,
    string? Version,
    AssemblySetSourceKind SourceKind);

public enum AssemblySetSourceKind
{
    Package,
    Assembly,
    PlatformAssembly,
    PlatformFramework,
    Project,
    Directory,
}

public enum AssemblySetPackageSelectionMode
{
    TargetFramework,
    LibAssembliesDescending,
}

public sealed record AssemblySetDiagnostic(AssemblySetDiagnosticSeverity Severity, string Message);

public enum AssemblySetDiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>
/// Owned assembly collection. Disposing it removes temporary package extraction directories.
/// </summary>
public sealed class AssemblySet : IDisposable
{
    private readonly IReadOnlyList<string> _tempDirs;
    private bool _disposed;

    internal AssemblySet(
        IReadOnlyList<AssemblySetEntry> assemblies,
        IReadOnlyList<AssemblySetDiagnostic> diagnostics,
        IReadOnlyList<string> tempDirs)
    {
        Assemblies = assemblies;
        Diagnostics = diagnostics;
        _tempDirs = tempDirs;
    }

    public IReadOnlyList<AssemblySetEntry> Assemblies { get; }
    public IReadOnlyList<AssemblySetDiagnostic> Diagnostics { get; }
    public IReadOnlyList<string> OwnedTemporaryDirectories => _tempDirs;

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }

        _disposed = true;
    }
}

/// <summary>
/// Collects assemblies from reusable lower-layer sources without depending on CLI options,
/// loggers, or views.
/// </summary>
public static class AssemblySetResolver
{
    public static async Task<AssemblySet> CollectAsync(
        HttpClient httpClient,
        AssemblySetRequest request,
        Action<string>? log = null)
    {
        List<AssemblySetEntry> assemblies = [];
        List<AssemblySetDiagnostic> diagnostics = [];
        List<string> tempDirs = [];

        void Warn(string message) => diagnostics.Add(new AssemblySetDiagnostic(AssemblySetDiagnosticSeverity.Warning, message));
        void Error(string message) => diagnostics.Add(new AssemblySetDiagnostic(AssemblySetDiagnosticSeverity.Error, message));

        async Task AddPackagesAsync()
        {
            foreach (var pkg in request.Packages)
            {
                var outcome = await PackageExtractor.ExtractPackageAsync(
                    httpClient,
                    pkg,
                    log,
                    request.TempDirPrefix,
                    request.SourceOptions);

                if (!outcome.IsSuccess)
                {
                    Warn(outcome.ErrorMessage ?? $"Package '{pkg}' could not be resolved.");
                    continue;
                }

                var extracted = outcome.Result!;
                if (extracted.TempDir != null)
                    tempDirs.Add(extracted.TempDir);

                IEnumerable<string> dlls;
                if (request.PackageSelectionMode == AssemblySetPackageSelectionMode.LibAssembliesDescending)
                {
                    dlls = Directory.GetFiles(extracted.ExtractPath, "*.dll", SearchOption.AllDirectories)
                        .Where(p => p.Contains("/lib/", StringComparison.Ordinal)
                            || p.Contains("\\lib\\", StringComparison.Ordinal))
                        .OrderByDescending(static p => p, StringComparer.Ordinal);
                }
                else
                {
                    var searchPath = TfmResolver.ResolvePackagePath(extracted.ExtractPath, request.Tfm)
                        ?? extracted.ExtractPath;

                    if (File.Exists(searchPath) && searchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        dlls = [searchPath];
                    }
                    else
                    {
                        dlls = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories);
                        if (!request.IncludePackageRuntimeAssemblies)
                        {
                            dlls = dlls.Where(p => !p.Contains("/runtimes/", StringComparison.Ordinal)
                                && !p.Contains("\\runtimes\\", StringComparison.Ordinal));
                        }
                    }
                }

                var foundAssembly = false;
                foreach (var dll in dlls)
                {
                    foundAssembly = true;
                    assemblies.Add(new AssemblySetEntry(
                        dll,
                        extracted.PackageName ?? pkg,
                        extracted.Version,
                        AssemblySetSourceKind.Package));
                }

                if (!foundAssembly && request.PackageSelectionMode == AssemblySetPackageSelectionMode.LibAssembliesDescending)
                    Error($"No libraries found in package '{pkg}'.");
            }
        }

        void AddAssemblies()
        {
            foreach (var asmPath in request.Assemblies)
            {
                if (!File.Exists(asmPath))
                {
                    Warn($"Library not found '{asmPath}', skipping.");
                    continue;
                }

                assemblies.Add(new AssemblySetEntry(
                    asmPath,
                    System.IO.Path.GetFileName(asmPath),
                    null,
                    AssemblySetSourceKind.Assembly));
            }
        }

        void AddProjects()
        {
            foreach (var projectPath in request.Projects)
            {
                if (!ProjectAssetsParser.TryFindAssets(projectPath, out var assetsPath, out var status))
                {
                    Warn(ProjectAssetsParser.DescribeMissingAssets(projectPath, status));
                    continue;
                }

                log?.Invoke($"Using assets: {assetsPath}");
                foreach (var (asmPath, packageName, packageVersion) in ProjectAssetsParser.Parse(assetsPath, request.Tfm, log))
                {
                    assemblies.Add(new AssemblySetEntry(
                        asmPath,
                        packageName,
                        packageVersion,
                        AssemblySetSourceKind.Project));
                }
            }
        }

        async Task AddPlatformAssembliesAsync()
        {
            foreach (var platformAsm in request.PlatformAssemblies)
            {
                var (assemblyPath, resolvedFramework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                    platformAsm,
                    httpClient,
                    log,
                    request.PlatformAssemblyFrameworkHint);

                if (error != null)
                {
                    Warn($"{error}, skipping.");
                    continue;
                }

                assemblies.Add(new AssemblySetEntry(
                    assemblyPath!,
                    resolvedFramework ?? "platform",
                    version,
                    AssemblySetSourceKind.PlatformAssembly));
            }
        }

        async Task AddPlatformFrameworksAsync()
        {
            if (request.PlatformFrameworks.Count > 0)
            {
                var requests = PlatformPackService.GetMissingPackRequests(request.PlatformFrameworks);
                if (requests.Count > 0)
                {
                    await foreach (var _ in PlatformPackService.EnsurePacksAsync(requests, httpClient, log))
                    {
                    }
                }
            }

            foreach (var framework in request.PlatformFrameworks)
            {
                var (refPath, resolvedVersion, error) = PlatformResolver.ResolveFramework(framework);
                if (error != null)
                {
                    Warn($"{error}, skipping.");
                    continue;
                }

                var frameworkAssemblies = PlatformResolver.GetAssemblies(refPath!);
                log?.Invoke($"Scanning {frameworkAssemblies.Count} libraries in {framework}@{resolvedVersion}");

                foreach (var asmInfo in frameworkAssemblies)
                {
                    assemblies.Add(new AssemblySetEntry(
                        asmInfo.Path,
                        framework,
                        resolvedVersion,
                        AssemblySetSourceKind.PlatformFramework));
                }
            }
        }

        void AddDirectories()
        {
            foreach (var dir in request.Directories)
            {
                if (!Directory.Exists(dir))
                {
                    Warn($"Directory not found '{dir}', skipping.");
                    continue;
                }

                var dlls = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
                log?.Invoke($"Scanning {dlls.Length} libraries in {dir}");

                foreach (var dll in dlls)
                {
                    assemblies.Add(new AssemblySetEntry(
                        dll,
                        System.IO.Path.GetFileName(dir),
                        null,
                        AssemblySetSourceKind.Directory));
                }
            }
        }

        var defaultSourceOrder = new AssemblySetRequest().SourceOrder;
        var requestedSourceOrder = request.SourceOrder.Count > 0
            ? request.SourceOrder
            : defaultSourceOrder;
        var sourceOrder = new List<AssemblySetSourceKind>(defaultSourceOrder.Count);
        var seenSourceKinds = new HashSet<AssemblySetSourceKind>();

        foreach (var sourceKind in requestedSourceOrder)
        {
            if (!seenSourceKinds.Add(sourceKind))
            {
                Warn($"Duplicate assembly source order entry '{sourceKind}' ignored.");
                continue;
            }

            sourceOrder.Add(sourceKind);
        }

        foreach (var sourceKind in defaultSourceOrder)
        {
            if (seenSourceKinds.Add(sourceKind))
                sourceOrder.Add(sourceKind);
        }

        foreach (var sourceKind in sourceOrder)
        {
            switch (sourceKind)
            {
                case AssemblySetSourceKind.Package:
                    await AddPackagesAsync();
                    break;
                case AssemblySetSourceKind.Assembly:
                    AddAssemblies();
                    break;
                case AssemblySetSourceKind.PlatformAssembly:
                    await AddPlatformAssembliesAsync();
                    break;
                case AssemblySetSourceKind.PlatformFramework:
                    await AddPlatformFrameworksAsync();
                    break;
                case AssemblySetSourceKind.Project:
                    AddProjects();
                    break;
                case AssemblySetSourceKind.Directory:
                    AddDirectories();
                    break;
                default:
                    Warn($"Unknown assembly source kind '{sourceKind}' ignored.");
                    break;
            }
        }

        return new AssemblySet(assemblies, diagnostics, tempDirs);
    }
}
