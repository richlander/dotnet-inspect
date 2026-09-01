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
        IPdbStore? pdbStore = null,
        IPackageSourceAuthorization? sourceAuthorization = null)
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
            pdbStore,
            sourceAuthorization).ConfigureAwait(false);
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
        IPdbStore? pdbStore,
        IPackageSourceAuthorization? sourceAuthorization,
        SymbolAcquisitionLimits? limits = null)
    {
        var downloader = pdbStore is null
            ? new SymbolPackageDownloader(httpClient)
            : sourceAuthorization is null
                ? new SymbolPackageDownloader(
                    httpClient,
                    pdbStore)
                : limits is not null
                    ? new SymbolPackageDownloader(
                        httpClient,
                        pdbStore,
                        sourceAuthorization,
                        limits)
                : new SymbolPackageDownloader(
                    httpClient,
                    pdbStore,
                    sourceAuthorization);
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
                cancellationToken,
                context.PdbId.Stamp).ConfigureAwait(false);

        if (result is PortablePdbAcquisitionResult.Acquired acquired)
        {
            string? localPath = acquired.Pdb.LocalPath;
            Stream stream =
                await acquired.Pdb.OpenReadAsync(
                    cancellationToken).ConfigureAwait(false);
            context.LoadPdbFromStream(
                stream,
                "Symbol Package",
                acquired.Pdb.SymbolServer,
                localPath,
                throwOnReadFailure: true);
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
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(httpClient);

        var (packageName, packageVersion, isPlatformAssembly) =
            GetAcquisitionCoordinates(
                assembly,
                fallbackPackageName,
                fallbackPackageVersion);
        return AcquireAsync(
            context,
            httpClient,
            packageName,
            packageVersion,
            isPlatformAssembly,
            log,
            cacheOnly,
            sourceOptions,
            cancellationToken);
    }

    public static Task AcquireAsync(
        PdbContext context,
        ResolvedAssemblyReference assembly,
        HttpClient httpClient,
        IPdbStore pdbStore,
        IPackageSourceAuthorization sourceAuthorization,
        Action<string>? log,
        bool cacheOnly = false,
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default,
        SymbolAcquisitionLimits? limits = null,
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(pdbStore);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);

        if (!context.NeedsPdb)
            return Task.CompletedTask;

        var (packageName, packageVersion, isPlatformAssembly) =
            GetAcquisitionCoordinates(
                assembly,
                fallbackPackageName,
                fallbackPackageVersion);

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
            pdbStore,
            sourceAuthorization,
            limits);
    }

    private static (
        string? PackageName,
        string? PackageVersion,
        bool IsPlatformAssembly)
        GetAcquisitionCoordinates(
            ResolvedAssemblyReference assembly,
            string? fallbackPackageName,
            string? fallbackPackageVersion)
        => assembly.Provenance switch
        {
            AssemblyResolutionProvenance.PackageAsset package =>
                (package.PackageId, package.PackageVersion, false),
            AssemblyResolutionProvenance.PlatformAsset =>
                (null, null, true),
            AssemblyResolutionProvenance.ProjectAsset
                or AssemblyResolutionProvenance.LocalAsset
                or AssemblyResolutionProvenance.DesignatedAsset
                when !string.IsNullOrWhiteSpace(
                        fallbackPackageName)
                    && !string.IsNullOrWhiteSpace(
                        fallbackPackageVersion) =>
                (fallbackPackageName, fallbackPackageVersion, false),
            _ => (null, null, false),
        };
}
