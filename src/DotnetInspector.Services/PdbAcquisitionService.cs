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
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default,
        IPdbStore? pdbStore = null)
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

        await AcquireCoreAsync(
            context,
            httpClient,
            Path.GetFileNameWithoutExtension(assemblyPath),
            packageName,
            packageVersion,
            isPlatformAssembly,
            log,
            cacheOnly,
            sourceOptions,
            cancellationToken,
            pdbStore).ConfigureAwait(false);
    }

    private static async Task AcquireCoreAsync(
        PdbContext context,
        HttpClient httpClient,
        string? assemblyName,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        Action<string>? log,
        bool cacheOnly,
        NuGetSourceOptions? sourceOptions,
        CancellationToken cancellationToken,
        IPdbStore? pdbStore)
    {
        var downloader = pdbStore is null
            ? new SymbolPackageDownloader(httpClient)
            : new SymbolPackageDownloader(httpClient, pdbStore);
        PortablePdbAcquisitionResult result =
            await downloader.AcquirePdbAsync(
                context.PdbId!.Guid,
                context.PdbId.Age,
                context.PdbId.PdbFileName,
                context.PdbId.IsPortable,
                assemblyName,
                packageName,
                packageVersion,
                log,
                isPlatformAssembly,
                cacheOnly,
                sourceOptions,
                cancellationToken).ConfigureAwait(false);

        if (result is PortablePdbAcquisitionResult.Acquired acquired)
        {
            Stream stream =
                await acquired.Pdb.OpenReadAsync(
                    cancellationToken).ConfigureAwait(false);
            context.LoadPdbFromStream(
                stream,
                "Symbol Package",
                acquired.Pdb.SymbolServer,
                acquired.Pdb.LocalPath);
        }
        else if (result.WindowsPdbDetected)
        {
            context.WindowsPdbDetected = true;
        }
    }

    public static Task AcquireAsync(
        PdbContext context,
        ResolvedAssemblyReference assembly,
        HttpClient httpClient,
        Action<string>? log,
        bool cacheOnly = false,
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default,
        IPdbStore? pdbStore = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(httpClient);

        if (!context.NeedsPdb)
            return Task.CompletedTask;

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

        return AcquireCoreAsync(
            context,
            httpClient,
            assembly.Identity.Name,
            packageName,
            packageVersion,
            isPlatformAssembly,
            log,
            cacheOnly,
            sourceOptions,
            cancellationToken,
            pdbStore);
    }
}
