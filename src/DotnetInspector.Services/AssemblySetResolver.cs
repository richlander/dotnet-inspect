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
    public CancellationToken CancellationToken { get; init; }
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
    AssemblySetSourceKind SourceKind,
    string? Tfm = null);

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

        DeleteOwnedTemporaryDirectories(_tempDirs);
        _disposed = true;
    }

    internal static void DeleteOwnedTemporaryDirectories(IEnumerable<string> tempDirs)
    {
        foreach (var dir in tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
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
        CancellationToken cancellationToken = request.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        List<AssemblySetEntry> assemblies = [];
        List<AssemblySetDiagnostic> diagnostics = [];
        List<string> tempDirs = [];

        void Warn(string message) => diagnostics.Add(new AssemblySetDiagnostic(AssemblySetDiagnosticSeverity.Warning, message));

        async Task AddPackagesAsync()
        {
            foreach (var pkg in request.Packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome =
                    await PackageExtractor.ExtractPackageWithCancellationAsync(
                        httpClient,
                        pkg,
                        log,
                        request.TempDirPrefix,
                        request.SourceOptions,
                        cancellationToken: cancellationToken);

                if (!outcome.IsSuccess)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Warn(outcome.ErrorMessage ?? $"Package '{pkg}' could not be resolved.");
                    continue;
                }

                var extracted = outcome.Result!;
                if (extracted.TempDir != null)
                    tempDirs.Add(extracted.TempDir);
                cancellationToken.ThrowIfCancellationRequested();
                AddExtractedPackage(
                    extracted, pkg, request.Tfm, request.IncludePackageRuntimeAssemblies,
                    request.PackageSelectionMode, assemblies, diagnostics,
                    cancellationToken);
            }
        }

        void AddAssemblies()
        {
            foreach (var asmPath in request.Assemblies)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
                if (!ProjectAssetsParser.TryFindAssets(projectPath, out var assetsPath, out var status))
                {
                    Warn(ProjectAssetsParser.DescribeMissingAssets(projectPath, status));
                    continue;
                }

                log?.Invoke($"Using assets: {assetsPath}");
                foreach (var (asmPath, packageName, packageVersion) in ProjectAssetsParser.Parse(assetsPath, request.Tfm, log))
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<string?> frameworkSpecs;
                if (request.PlatformAssemblyFrameworkHint is { } hint)
                {
                    frameworkSpecs = [hint];
                }
                else if (request.Tfm is { } targetFramework)
                {
                    if (!PlatformResolver
                            .TryGetFrameworkSpecsForTargetFramework(
                                targetFramework,
                                out IReadOnlyList<string> selectedSpecs))
                    {
                        Warn(
                            $"Target framework '{targetFramework}' cannot select an installed platform library, skipping '{platformAsm}'.");
                        continue;
                    }
                    frameworkSpecs = [.. selectedSpecs];
                }
                else
                {
                    frameworkSpecs = [null];
                }

                (string? AssemblyPath, string? Framework, string? Version,
                    string? Error) resolution = default;
                var errors = new List<string>();

                if (request.PlatformAssemblyFrameworkHint is null
                    && request.Tfm is not null)
                {
                    foreach (string? frameworkSpec in frameworkSpecs)
                    {
                        resolution = PlatformResolver.ResolveAssembly(
                            platformAsm,
                            frameworkSpec);
                        if (resolution.AssemblyPath is not null)
                            break;
                        if (!string.IsNullOrWhiteSpace(resolution.Error))
                            errors.Add(resolution.Error);
                    }
                }

                if (resolution.AssemblyPath is null)
                {
                    foreach (string? frameworkSpec in frameworkSpecs)
                    {
                        resolution =
                            await PlatformResolver.ResolveAssemblyAsync(
                                platformAsm,
                                httpClient,
                                log,
                                frameworkSpec,
                                sourceOptions: request.SourceOptions);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (resolution.AssemblyPath is not null)
                            break;
                        if (!string.IsNullOrWhiteSpace(resolution.Error))
                            errors.Add(resolution.Error);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();

                if (resolution.AssemblyPath is null)
                {
                    string error = errors.Count > 0
                        ? string.Join("; ", errors.Distinct())
                        : resolution.Error
                            ?? $"Library '{platformAsm}' could not be resolved.";
                    Warn($"{error}, skipping.");
                    continue;
                }

                assemblies.Add(new AssemblySetEntry(
                    resolution.AssemblyPath,
                    resolution.Framework ?? "platform",
                    resolution.Version,
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
                    await foreach (var _ in PlatformPackService.EnsurePacksAsync(
                        requests,
                        httpClient,
                        log,
                        sourceOptions: request.SourceOptions))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
            }

            foreach (var framework in request.PlatformFrameworks)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(dir))
                {
                    Warn($"Directory not found '{dir}', skipping.");
                    continue;
                }

                var dlls = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
                log?.Invoke($"Scanning {dlls.Length} libraries in {dir}");

                foreach (var dll in dlls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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

        try
        {
            foreach (var sourceKind in sourceOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

            cancellationToken.ThrowIfCancellationRequested();
            return new AssemblySet(assemblies, diagnostics, tempDirs);
        }
        catch
        {
            AssemblySet.DeleteOwnedTemporaryDirectories(tempDirs);
            throw;
        }
    }

    /// <summary>
    /// Selects assemblies from an already acquired package without acquiring it again.
    /// Ownership of the extraction's temporary directory transfers to the returned set.
    /// </summary>
    public static AssemblySet CollectExtractedPackage(
        PackageExtractionResult extracted,
        string? tfm = null,
        bool includeRuntimeAssemblies = false,
        AssemblySetPackageSelectionMode selectionMode = AssemblySetPackageSelectionMode.TargetFramework)
    {
        ArgumentNullException.ThrowIfNull(extracted);
        List<AssemblySetEntry> assemblies = [];
        List<AssemblySetDiagnostic> diagnostics = [];
        string[] tempDirs = extracted.TempDir is { } temporary ? [temporary] : [];
        try
        {
            AddExtractedPackage(
                extracted, extracted.PackageName ?? extracted.ExtractPath, tfm,
                includeRuntimeAssemblies, selectionMode, assemblies, diagnostics,
                CancellationToken.None);
            return new AssemblySet(assemblies, diagnostics, tempDirs);
        }
        catch
        {
            AssemblySet.DeleteOwnedTemporaryDirectories(tempDirs);
            throw;
        }
    }

    private static void AddExtractedPackage(
        PackageExtractionResult extracted,
        string package,
        string? tfm,
        bool includeRuntimeAssemblies,
        AssemblySetPackageSelectionMode selectionMode,
        List<AssemblySetEntry> assemblies,
        List<AssemblySetDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> dlls;
        string? selectedTfm = null;
        if (selectionMode == AssemblySetPackageSelectionMode.LibAssembliesDescending)
        {
            dlls = Directory.GetFiles(extracted.ExtractPath, "*.dll", SearchOption.AllDirectories)
                .Where(p => p.Contains("/lib/", StringComparison.Ordinal)
                    || p.Contains("\\lib\\", StringComparison.Ordinal))
                .OrderByDescending(static p => p, StringComparer.Ordinal);
        }
        else
        {
            var selection = TfmSelector.SelectHighestAssembliesFromPackage(extracted.ExtractPath, tfm);
            selectedTfm = selection.tfm;
            dlls = selection.paths;
            if (!includeRuntimeAssemblies)
            {
                dlls = dlls.Where(p => !p.Contains("/runtimes/", StringComparison.Ordinal)
                    && !p.Contains("\\runtimes\\", StringComparison.Ordinal));
            }
            dlls = dlls.OrderBy(static p => p, StringComparer.Ordinal);
        }

        bool foundAssembly = false;
        foreach (string dll in dlls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foundAssembly = true;
            assemblies.Add(new AssemblySetEntry(
                dll, extracted.PackageName ?? package, extracted.Version,
                AssemblySetSourceKind.Package, selectedTfm));
        }

        if (!foundAssembly)
        {
            string message = selectionMode == AssemblySetPackageSelectionMode.LibAssembliesDescending
                ? $"No libraries found in package '{package}'."
                : string.IsNullOrWhiteSpace(tfm)
                    ? $"No assemblies found in package '{package}'."
                    : $"No assemblies found for target framework '{tfm}' in package '{package}'.";
            diagnostics.Add(new AssemblySetDiagnostic(AssemblySetDiagnosticSeverity.Error, message));
        }
    }
}
