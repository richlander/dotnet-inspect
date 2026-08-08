using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// Acquires a matching portable PDB for an already-open metadata context.
/// </summary>
public static class PdbAcquisitionService
{
    public static async Task AcquireAsync(
        PdbContext context,
        HttpClient httpClient,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        Action<string>? log,
        bool cacheOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(httpClient);

        if (!context.NeedsPdb)
            return;

        if (context.AssemblyPathOrNull is not { } assemblyPath)
        {
            log?.Invoke(
                "External PDB acquisition is unavailable because the resolved assembly descriptor has no filesystem path.");
            return;
        }

        var downloader = new SymbolPackageDownloader(httpClient);
        var result = await downloader.DownloadPdbAsync(
            context.PdbId!.Guid,
            context.PdbId.Age,
            context.PdbId.PdbFileName,
            context.PdbId.IsPortable,
            assemblyPath,
            packageName,
            packageVersion,
            log,
            isPlatformAssembly,
            cacheOnly,
            cancellationToken).ConfigureAwait(false);

        if (result.PdbFilePath != null)
            context.LoadPdbFromFile(result.PdbFilePath, "Symbol Package", result.SymbolServer);
        else if (result.WindowsPdbDetected)
            context.WindowsPdbDetected = true;
    }

    public static Task AcquireAsync(
        PdbContext context,
        ResolvedAssemblyReference assembly,
        HttpClient httpClient,
        Action<string>? log,
        bool cacheOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string? packageName = null;
        string? packageVersion = null;
        bool isPlatformAssembly = false;

        switch (assembly.Provenance)
        {
            case AssemblyResolutionProvenance.PackageAsset package:
                packageName = package.PackageId;
                packageVersion = package.PackageVersion;
                break;
            case AssemblyResolutionProvenance.PlatformAsset:
                isPlatformAssembly = true;
                break;
        }

        return AcquireAsync(
            context,
            httpClient,
            packageName,
            packageVersion,
            isPlatformAssembly,
            log,
            cacheOnly,
            cancellationToken);
    }
}
