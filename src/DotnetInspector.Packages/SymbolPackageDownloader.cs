using System.IO.Compression;
using System.Net;
using DotnetInspector.Core;

namespace DotnetInspector.Packages;

/// <summary>
/// Result of downloading a PDB file (no SRM types).
/// </summary>
public record PdbDownloadResult(
    string? PdbFilePath,
    bool WindowsPdbDetected = false,
    string? SymbolServer = null
);

/// <summary>
/// Downloads and manages symbol packages (.snupkg) from NuGet for SourceLink resolution.
/// Only supports Portable PDBs (embedded or standalone) and snupkg files.
/// </summary>
/// <remarks>
/// Symbol server key generation follows the conventions from dotnet/symstore:
/// https://github.com/dotnet/symstore/blob/d66992e7c2f32288fbf1acf08cdea43098025c7c/src/Microsoft.SymbolStore/KeyGenerators/PortablePDBFileKeyGenerator.cs
/// Portable PDBs use {GUID}FFFFFFFF, Windows PDBs use {GUID}{age:x}.
/// </remarks>
public class SymbolPackageDownloader(HttpClient client)
{
    private const string SymbolMissCacheCategory = "symbol-misses";
    private static readonly TimeSpan SymbolMissCacheTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan SymbolForbiddenCacheTtl = TimeSpan.FromDays(7);
    private readonly HttpClient _client = client;
    private readonly string _cachePath = Path.Combine(NuGetCache.GetAppCachePath(), "symbols");

    /// <summary>
    /// Downloads a PDB file and returns its path on disk. No SRM types in signature.
    /// The caller (PdbContext) is responsible for opening and reading the PDB.
    /// </summary>
    public async Task<PdbDownloadResult> DownloadPdbAsync(
        Guid pdbGuid, int pdbAge, string pdbFileName, bool isPortable,
        string assemblyPath,
        string? packageName = null,
        string? packageVersion = null,
        Action<string>? log = null,
        bool isPlatformAssembly = false)
    {
        bool windowsPdbDetected = false;

        pdbFileName = GetSymbolFileName(pdbFileName);
        var guid = pdbGuid.ToString("N").ToUpperInvariant();
        var symbolKey = isPortable
            ? $"{guid}FFFFFFFF"
            : $"{guid}{pdbAge:x}";

        // For Microsoft packages or platform assemblies, try MSDL first
        bool isMicrosoftPackage = isPlatformAssembly || IsMicrosoftPackage(packageName);
        if (isMicrosoftPackage)
        {
            log?.Invoke(isPlatformAssembly ? "Platform library, trying MSDL symbol server" : "Microsoft package detected, trying MSDL symbol server first");
            var msdlResult = await TryLocateFromMsdlAsync(pdbFileName, symbolKey, log).ConfigureAwait(false);
            if (msdlResult.PdbFilePath != null)
                return msdlResult;
            if (msdlResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // Try downloading symbol package (.snupkg)
        if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
        {
            var snupkgResult = await TryLocateFromSymbolPackageAsync(
                packageName, packageVersion, assemblyPath, log).ConfigureAwait(false);
            if (snupkgResult.PdbFilePath != null)
                return snupkgResult;
            if (snupkgResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // Try NuGet symbol server, then MSDL as fallback (for non-Microsoft packages)
        if (!isMicrosoftPackage)
        {
            var symbolResult = await TryLocateFromSymbolServerAsync(pdbFileName, symbolKey, log).ConfigureAwait(false);
            if (symbolResult.PdbFilePath != null)
                return symbolResult;
            if (symbolResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        log?.Invoke("No Portable PDB available");
        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private async Task<PdbDownloadResult> TryLocateFromMsdlAsync(
        string pdbFileName, string symbolKey, Action<string>? log)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        bool windowsPdbDetected = false;

        var cachePath = GetSymbolServerCachePath(pdbFileName, symbolKey);
        if (File.Exists(cachePath))
        {
            log?.Invoke("Using cached PDB from MSDL");
            var check = CheckPdbHeader(cachePath);
            if (check == PdbHeaderKind.Portable)
                return new PdbDownloadResult(cachePath, SymbolServer: "msdl.microsoft.com");
            if (check == PdbHeaderKind.Windows)
                windowsPdbDetected = true;
        }

        var url = $"https://msdl.microsoft.com/download/symbols/{pdbFileName}/{symbolKey}/{pdbFileName}";
        if (IsCachedMiss(url, log, "MSDL symbol server"))
            return new PdbDownloadResult(null, windowsPdbDetected);

        log?.Invoke("Trying MSDL symbol server");

        try
        {
            var httpResult = await HttpRetryHelper.GetWithRetryResultAsync(
                _client, url, log: log,
                trafficKind: NetworkTrafficKind.SymbolDownload).ConfigureAwait(false);
            using var response = httpResult.Response;
            if (response == null || !response.IsSuccessStatusCode)
            {
                CacheMissIfDefinitive(url, httpResult);
                log?.Invoke("MSDL: symbol not found");
                return new PdbDownloadResult(null, windowsPdbDetected);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using (var fs = File.Create(cachePath))
            {
                await response.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            var headerCheck = CheckPdbHeader(cachePath);
            if (headerCheck == PdbHeaderKind.Portable)
            {
                log?.Invoke("Successfully downloaded PDB from MSDL");
                return new PdbDownloadResult(cachePath, SymbolServer: "msdl.microsoft.com");
            }
            if (headerCheck == PdbHeaderKind.Windows)
            {
                windowsPdbDetected = true;
                log?.Invoke("MSDL returned a Windows PDB (not supported)");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"MSDL error: {ex.Message}");
        }

        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private async Task<PdbDownloadResult> TryLocateFromSymbolPackageAsync(
        string packageName, string packageVersion, string assemblyPath, Action<string>? log)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = packageVersion.ToLowerInvariant();
        bool windowsPdbDetected = false;

        // Check cache first
        var cachedPdbPath = GetCachedPdbPath(normalizedName, normalizedVersion, assemblyPath);
        if (cachedPdbPath != null && File.Exists(cachedPdbPath))
        {
            log?.Invoke($"Using cached PDB: {Path.GetFileName(cachedPdbPath)}");
            var check = CheckPdbHeader(cachedPdbPath);
            if (check == PdbHeaderKind.Portable)
                return new PdbDownloadResult(cachedPdbPath, SymbolServer: "nuget.org");
            if (check == PdbHeaderKind.Windows)
                windowsPdbDetected = true;
        }

        // Try NuGet global CDN first
        var snupkgUrls = new[]
        {
            $"https://globalcdn.nuget.org/symbol-packages/{normalizedName}.{normalizedVersion}.snupkg",
            $"https://api.nuget.org/v3-flatcontainer/{normalizedName}/{normalizedVersion}/{normalizedName}.{normalizedVersion}.snupkg"
        };

        log?.Invoke($"Trying symbol package: {normalizedName}.{normalizedVersion}.snupkg");

        foreach (var snupkgUrl in snupkgUrls)
        {
            if (IsCachedMiss(snupkgUrl, log, "symbol package"))
                continue;

            try
            {
                var httpResult = await HttpRetryHelper.GetWithRetryResultAsync(
                    _client, snupkgUrl, log: log,
                    trafficKind: NetworkTrafficKind.SymbolDownload).ConfigureAwait(false);
                using var response = httpResult.Response;
                if (response is not { IsSuccessStatusCode: true })
                {
                    CacheMissIfDefinitive(snupkgUrl, httpResult);
                    continue;
                }

                log?.Invoke($"Found symbol package at: {snupkgUrl}");
                var result = await ExtractPdbFromSymbolPackage(
                    response, normalizedName, normalizedVersion, assemblyPath, windowsPdbDetected, log).ConfigureAwait(false);
                if (result.WindowsPdbDetected)
                    windowsPdbDetected = true;
                return result;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Error downloading symbol package: {ex.Message}");
            }
        }

        log?.Invoke("Symbol package not found on NuGet");
        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private async Task<PdbDownloadResult> ExtractPdbFromSymbolPackage(
        HttpResponseMessage response,
        string normalizedName, string normalizedVersion, string assemblyPath,
        bool windowsPdbDetected, Action<string>? log)
    {
        var tempDir = Directory.CreateTempSubdirectory("snupkg").FullName;

        try
        {
            var snupkgPath = Path.Combine(tempDir, "package.snupkg");
            using (var fs = File.Create(snupkgPath))
            {
                await response.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            var extractPath = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(snupkgPath, extractPath);

            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            var pdbFiles = Directory.GetFiles(extractPath, $"{assemblyName}.pdb", SearchOption.AllDirectories);

            if (pdbFiles.Length == 0)
            {
                log?.Invoke("No matching PDB found in symbol package");
                return new PdbDownloadResult(null, windowsPdbDetected);
            }

            var pdbFile = pdbFiles[0];
            var cachePath = EnsureCachedPdbPath(normalizedName, normalizedVersion, assemblyPath);
            if (cachePath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.Copy(pdbFile, cachePath, overwrite: true);
                log?.Invoke($"Cached PDB to: {cachePath}");
                pdbFile = cachePath;
            }

            var headerCheck = CheckPdbHeader(pdbFile);
            if (headerCheck == PdbHeaderKind.Portable)
            {
                log?.Invoke("Successfully located PDB from symbol package");
                return new PdbDownloadResult(pdbFile, SymbolServer: "nuget.org");
            }
            if (headerCheck == PdbHeaderKind.Windows)
                windowsPdbDetected = true;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private async Task<PdbDownloadResult> TryLocateFromSymbolServerAsync(
        string pdbFileName, string symbolKey, Action<string>? log)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SymbolDownload);
        bool windowsPdbDetected = false;

        // Check cache before hitting the network
        var cachePath = GetSymbolServerCachePath(pdbFileName, symbolKey);
        if (File.Exists(cachePath))
        {
            var cachedCheck = CheckPdbHeader(cachePath);
            if (cachedCheck == PdbHeaderKind.Portable)
            {
                log?.Invoke("Using cached PDB from symbol server");
                return new PdbDownloadResult(cachePath, SymbolServer: "cached");
            }
            if (cachedCheck == PdbHeaderKind.Windows)
                windowsPdbDetected = true;
        }

        var symbolServers = new[]
        {
            "https://symbols.nuget.org/download/symbols",
            "https://msdl.microsoft.com/download/symbols"
        };

        foreach (var server in symbolServers)
        {
            var url = $"{server}/{pdbFileName}/{symbolKey}/{pdbFileName}";
            if (IsCachedMiss(url, log, "symbol server"))
                continue;

            log?.Invoke($"Trying symbol server: {server}");

            try
            {
                var httpResult = await HttpRetryHelper.GetWithRetryResultAsync(
                    _client, url, log: log,
                    trafficKind: NetworkTrafficKind.SymbolDownload).ConfigureAwait(false);
                using var response = httpResult.Response;
                if (response == null || !response.IsSuccessStatusCode)
                {
                    CacheMissIfDefinitive(url, httpResult);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                using (var fs = File.Create(cachePath))
                {
                    await response.Content.CopyToAsync(fs).ConfigureAwait(false);
                }

                var headerCheck = CheckPdbHeader(cachePath);
                if (headerCheck == PdbHeaderKind.Portable)
                {
                    var serverHost = new Uri(server).Host;
                    log?.Invoke("Successfully downloaded PDB from symbol server");
                    return new PdbDownloadResult(cachePath, SymbolServer: serverHost);
                }
                if (headerCheck == PdbHeaderKind.Windows)
                {
                    windowsPdbDetected = true;
                    log?.Invoke("Symbol server returned a Windows PDB (not supported)");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Symbol server error: {ex.Message}");
            }
        }

        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private static bool IsCachedMiss(string key, Action<string>? log, string source)
    {
        if (CoreCache.TryGet(SymbolMissCacheCategory, key, SymbolForbiddenCacheTtl, extension: "forbidden") != null)
        {
            log?.Invoke($"Using cached symbol miss: {source}");
            return true;
        }

        if (CoreCache.TryGet(SymbolMissCacheCategory, key, SymbolMissCacheTtl, extension: "miss") == null)
            return false;

        log?.Invoke($"Using cached symbol miss: {source}");
        return true;
    }

    private static void CacheMissIfDefinitive(string key, HttpRetryHelper.HttpRetryResult result)
    {
        if (result.StatusCode is not { } statusCode)
            return;

        if (HttpRetryHelper.IsRetryableStatus(statusCode))
            return;

        var extension = statusCode == HttpStatusCode.Forbidden ? "forbidden" : "miss";
        CoreCache.Set(SymbolMissCacheCategory, key, ((int)statusCode).ToString(), extension);
    }

    /// <summary>
    /// Checks if a package name indicates it's a Microsoft package.
    /// </summary>
    private static bool IsMicrosoftPackage(string? packageName)
    {
        if (string.IsNullOrEmpty(packageName))
            return false;

        return packageName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
               packageName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
               packageName.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase) ||
               packageName.Equals("WindowsAzure.Storage", StringComparison.OrdinalIgnoreCase);
    }

    private enum PdbHeaderKind { Unknown, Portable, Windows }

    private static PdbHeaderKind CheckPdbHeader(string pdbPath)
    {
        try
        {
            using var stream = File.OpenRead(pdbPath);
            byte[] header = new byte[4];
            if (stream.Read(header, 0, 4) < 4)
                return PdbHeaderKind.Unknown;

            if (header[0] == 'B' && header[1] == 'S' && header[2] == 'J' && header[3] == 'B')
                return PdbHeaderKind.Portable;
            if (header[0] == 'M' && header[1] == 'i' && header[2] == 'c' && header[3] == 'r')
                return PdbHeaderKind.Windows;

            return PdbHeaderKind.Unknown;
        }
        catch
        {
            return PdbHeaderKind.Unknown;
        }
    }

    private string? GetCachedPdbPath(string packageName, string packageVersion, string assemblyPath)
    {
        NuGetCache.ValidatePathComponent(packageName, "package name");
        NuGetCache.ValidatePathComponent(packageVersion, "package version");

        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        var cachePath = Path.Combine(_cachePath, packageName, packageVersion, $"{assemblyName}.pdb");
        return cachePath;
    }

    private string? EnsureCachedPdbPath(string packageName, string packageVersion, string assemblyPath)
    {
        return GetCachedPdbPath(packageName, packageVersion, assemblyPath);
    }

    private string GetSymbolServerCachePath(string pdbName, string symbolKey)
    {
        return Path.Combine(_cachePath, "servers", pdbName, symbolKey, pdbName);
    }

    private static string GetSymbolFileName(string pdbPath)
    {
        var normalized = pdbPath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

}
