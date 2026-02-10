using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Packages;

/// <summary>
/// Result of attempting to get a PDB reader.
/// </summary>
/// <param name="SymbolServer">The server the PDB was retrieved from (e.g., "nuget.org", "msdl.microsoft.com"), or null if local.</param>
public record PdbLookupResult(
    MetadataReader? Reader,
    IDisposable? Provider,
    bool WindowsPdbDetected = false,
    string? SymbolServer = null
);

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
    private readonly HttpClient _client = client;
    private readonly string _cachePath = Path.Combine(NuGetCache.GetAppCachePath(), "symbols");

    /// <summary>
    /// Tries to get a PDB reader for an assembly, checking embedded PDB first, then external sources.
    /// Only supports Portable PDBs. Returns WindowsPdbDetected=true if a Windows PDB was found.
    /// </summary>
    public async Task<PdbLookupResult> GetPdbReaderAsync(
        PEReader peReader,
        string assemblyPath,
        string? packageName = null,
        string? packageVersion = null,
        Action<string>? log = null,
        bool isPlatformAssembly = false)
    {
        bool windowsPdbDetected = false;

        // 1. Try embedded PDB first
        foreach (var entry in peReader.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                log?.Invoke("Using embedded PDB");
                var provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                return new PdbLookupResult(provider.GetMetadataReader(), provider);
            }
        }

        // 2. Try standalone PDB next to the assembly
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(pdbPath))
        {
            var (reader, provider, isWindowsPdb) = TryReadPortablePdb(pdbPath, log);
            if (reader != null)
                return new PdbLookupResult(reader, provider);
            if (isWindowsPdb)
                windowsPdbDetected = true;
        }

        // 3. Try to get CodeView info for symbol server lookup
        // Look for all CodeView entries, preferring Portable PDB (0x504d) over Windows PDB
        CodeViewDebugDirectoryData? codeView = null;
        DebugDirectoryEntry? codeViewEntry = null;
        CodeViewDebugDirectoryData? windowsCodeView = null;
        DebugDirectoryEntry? windowsCodeViewEntry = null;

        foreach (var entry in peReader.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.CodeView)
            {
                var cv = peReader.ReadCodeViewDebugDirectoryData(entry);
                bool isPortable = entry.MinorVersion == 0x504d;

                if (isPortable)
                {
                    // Prefer Portable PDB entries
                    codeView = cv;
                    codeViewEntry = entry;
                }
                else if (codeView == null)
                {
                    // Keep Windows PDB as fallback if no Portable PDB found yet
                    codeView = cv;
                    codeViewEntry = entry;
                }

                // Also track Windows PDB entry separately for detection
                if (!isPortable)
                {
                    windowsCodeView = cv;
                    windowsCodeViewEntry = entry;
                }
            }
        }

        if (codeView == null || codeViewEntry == null)
        {
            log?.Invoke("No CodeView debug directory found");
            return new PdbLookupResult(null, null, windowsPdbDetected);
        }

        // Check if this is a Portable PDB (MinorVersion == 0x504d means "PM" for Portable Metadata)
        bool isPortablePdb = codeViewEntry.Value.MinorVersion == 0x504d;

        // If we have both Windows and Portable PDB entries, log it
        if (windowsCodeView != null && isPortablePdb)
        {
            log?.Invoke($"Found both Windows (.ni.pdb) and Portable PDB entries, using Portable");
        }

        // 4. For Microsoft packages or platform assemblies, try MSDL symbol server first (they typically don't publish snupkg)
        bool isMicrosoftPackage = isPlatformAssembly || IsMicrosoftPackage(packageName);
        if (isMicrosoftPackage)
        {
            log?.Invoke(isPlatformAssembly ? "Platform library, trying MSDL symbol server" : "Microsoft package detected, trying MSDL symbol server first");
            var msdlResult = await TryDownloadFromMsdlAsync(codeView.Value, isPortablePdb, log);
            if (msdlResult.Reader != null)
                return msdlResult;
            if (msdlResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // 5. Try downloading symbol package (.snupkg) from NuGet
        if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
        {
            var snupkgResult = await TryDownloadSymbolPackageAsync(
                packageName, packageVersion, codeView.Value, assemblyPath, log);
            if (snupkgResult.Reader != null)
                return snupkgResult;
            if (snupkgResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // 6. Try NuGet symbol server, then MSDL as fallback (for non-Microsoft packages)
        if (!isMicrosoftPackage)
        {
            var symbolResult = await TryDownloadFromSymbolServerAsync(codeView.Value, isPortablePdb, log);
            if (symbolResult.Reader != null)
                return symbolResult;
            if (symbolResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        log?.Invoke("No Portable PDB available");
        return new PdbLookupResult(null, null, windowsPdbDetected);
    }

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

        var guid = pdbGuid.ToString("N").ToUpperInvariant();
        var symbolKey = isPortable
            ? $"{guid}FFFFFFFF"
            : $"{guid}{pdbAge:x}";

        // For Microsoft packages or platform assemblies, try MSDL first
        bool isMicrosoftPackage = isPlatformAssembly || IsMicrosoftPackage(packageName);
        if (isMicrosoftPackage)
        {
            log?.Invoke(isPlatformAssembly ? "Platform library, trying MSDL symbol server" : "Microsoft package detected, trying MSDL symbol server first");
            var msdlResult = await TryLocateFromMsdlAsync(pdbFileName, symbolKey, log);
            if (msdlResult.PdbFilePath != null)
                return msdlResult;
            if (msdlResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // Try downloading symbol package (.snupkg)
        if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
        {
            var snupkgResult = await TryLocateFromSymbolPackageAsync(
                packageName, packageVersion, assemblyPath, log);
            if (snupkgResult.PdbFilePath != null)
                return snupkgResult;
            if (snupkgResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        // Try NuGet symbol server, then MSDL as fallback (for non-Microsoft packages)
        if (!isMicrosoftPackage)
        {
            var symbolResult = await TryLocateFromSymbolServerAsync(pdbFileName, symbolKey, log);
            if (symbolResult.PdbFilePath != null)
                return symbolResult;
            if (symbolResult.WindowsPdbDetected)
                windowsPdbDetected = true;
        }

        log?.Invoke("No Portable PDB available");
        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    // ===== File-path-returning variants (for DownloadPdbAsync) =====

    private async Task<PdbDownloadResult> TryLocateFromMsdlAsync(
        string pdbFileName, string symbolKey, Action<string>? log)
    {
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
        log?.Invoke("Trying MSDL symbol server");

        try
        {
            using var response = await HttpRetryHelper.GetWithRetryAsync(_client, url, log: log);
            if (response == null || !response.IsSuccessStatusCode)
            {
                log?.Invoke("MSDL: symbol not found");
                return new PdbDownloadResult(null, windowsPdbDetected);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using (var fs = File.Create(cachePath))
            {
                await response.Content.CopyToAsync(fs);
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

        HttpResponseMessage? response = null;
        try
        {
            foreach (var snupkgUrl in snupkgUrls)
            {
                response = await HttpRetryHelper.GetWithRetryAsync(_client, snupkgUrl, log: log);
                if (response != null && response.IsSuccessStatusCode)
                {
                    log?.Invoke($"Found symbol package at: {snupkgUrl}");
                    break;
                }
                response?.Dispose();
                response = null;
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                log?.Invoke("Symbol package not found on NuGet");
                return new PdbDownloadResult(null, windowsPdbDetected);
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"snupkg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var snupkgPath = Path.Combine(tempDir, "package.snupkg");
                using (var fs = File.Create(snupkgPath))
                {
                    await response.Content.CopyToAsync(fs);
                }
                response.Dispose();
                response = null;

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
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error downloading symbol package: {ex.Message}");
        }
        finally
        {
            response?.Dispose();
        }

        return new PdbDownloadResult(null, windowsPdbDetected);
    }

    private async Task<PdbDownloadResult> TryLocateFromSymbolServerAsync(
        string pdbFileName, string symbolKey, Action<string>? log)
    {
        bool windowsPdbDetected = false;

        var symbolServers = new[]
        {
            "https://symbols.nuget.org/download/symbols",
            "https://msdl.microsoft.com/download/symbols"
        };

        foreach (var server in symbolServers)
        {
            var url = $"{server}/{pdbFileName}/{symbolKey}/{pdbFileName}";
            log?.Invoke($"Trying symbol server: {server}");

            try
            {
                using var response = await HttpRetryHelper.GetWithRetryAsync(_client, url, log: log);
                if (response == null || !response.IsSuccessStatusCode)
                    continue;

                var cachePath = GetSymbolServerCachePath(pdbFileName, symbolKey);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                using (var fs = File.Create(cachePath))
                {
                    await response.Content.CopyToAsync(fs);
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

    // ===== Reader-returning variants (legacy, used by GetPdbReaderAsync) =====

    private async Task<PdbLookupResult> TryDownloadSymbolPackageAsync(
        string packageName,
        string packageVersion,
        CodeViewDebugDirectoryData codeView,
        string assemblyPath,
        Action<string>? log)
    {
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = packageVersion.ToLowerInvariant();

        bool windowsPdbDetected = false;

        // Check cache first
        var cachedPdbPath = GetCachedPdbPath(normalizedName, normalizedVersion, assemblyPath);
        if (cachedPdbPath != null && File.Exists(cachedPdbPath))
        {
            log?.Invoke($"Using cached PDB: {Path.GetFileName(cachedPdbPath)}");
            var result = TryReadPortablePdb(cachedPdbPath, log);
            if (result.reader != null)
                return new PdbLookupResult(result.reader, result.provider, SymbolServer: "nuget.org");
            if (result.isWindowsPdb)
                windowsPdbDetected = true;
        }

        // Try NuGet global CDN first (same URL NuGet Package Explorer uses)
        var snupkgUrls = new[]
        {
            $"https://globalcdn.nuget.org/symbol-packages/{normalizedName}.{normalizedVersion}.snupkg",
            $"https://api.nuget.org/v3-flatcontainer/{normalizedName}/{normalizedVersion}/{normalizedName}.{normalizedVersion}.snupkg"
        };

        log?.Invoke($"Trying symbol package: {normalizedName}.{normalizedVersion}.snupkg");

        HttpResponseMessage? response = null;
        try
        {
            foreach (var snupkgUrl in snupkgUrls)
            {
                response = await HttpRetryHelper.GetWithRetryAsync(_client, snupkgUrl, log: log);
                if (response != null && response.IsSuccessStatusCode)
                {
                    log?.Invoke($"Found symbol package at: {snupkgUrl}");
                    break;
                }
                response?.Dispose();
                response = null;
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                log?.Invoke($"Symbol package not found on NuGet");
                return new PdbLookupResult(null, null, windowsPdbDetected);
            }

            // Download to temp file and extract
            var tempDir = Path.Combine(Path.GetTempPath(), $"snupkg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var snupkgPath = Path.Combine(tempDir, "package.snupkg");
                using (var fs = File.Create(snupkgPath))
                {
                    await response.Content.CopyToAsync(fs);
                }
                response.Dispose();
                response = null;

                var extractPath = Path.Combine(tempDir, "extracted");
                ZipFile.ExtractToDirectory(snupkgPath, extractPath);

                // Find matching PDB
                var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
                var pdbFiles = Directory.GetFiles(extractPath, $"{assemblyName}.pdb", SearchOption.AllDirectories);

                if (pdbFiles.Length == 0)
                {
                    log?.Invoke("No matching PDB found in symbol package");
                    return new PdbLookupResult(null, null, windowsPdbDetected);
                }

                // Cache the PDB for future use
                var pdbFile = pdbFiles[0];
                var cachePath = EnsureCachedPdbPath(normalizedName, normalizedVersion, assemblyPath);
                if (cachePath != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    File.Copy(pdbFile, cachePath, overwrite: true);
                    log?.Invoke($"Cached PDB to: {cachePath}");
                    pdbFile = cachePath;
                }

                var result = TryReadPortablePdb(pdbFile, log);
                if (result.reader != null)
                {
                    log?.Invoke("Successfully loaded PDB from symbol package");
                    return new PdbLookupResult(result.reader, result.provider, SymbolServer: "nuget.org");
                }
                if (result.isWindowsPdb)
                    windowsPdbDetected = true;
            }
            finally
            {
                // Cleanup temp dir (but not cached PDB)
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error downloading symbol package: {ex.Message}");
        }
        finally
        {
            response?.Dispose();
        }

        return new PdbLookupResult(null, null, windowsPdbDetected);
    }

    private async Task<PdbLookupResult> TryDownloadFromSymbolServerAsync(
        CodeViewDebugDirectoryData codeView,
        bool isPortablePdb,
        Action<string>? log)
    {
        bool windowsPdbDetected = false;

        // Build symbol server URL
        // Format: {server}/{pdbname}/{symbolkey}/{pdbname}
        var pdbName = Path.GetFileName(codeView.Path);
        var guid = codeView.Guid.ToString("N").ToUpperInvariant();
        var age = codeView.Age;

        // Portable PDBs use FFFFFFFF, Windows PDBs use the actual age
        var symbolKey = isPortablePdb
            ? $"{guid}FFFFFFFF"
            : $"{guid}{age:x}";

        // Try NuGet symbol server first (typically has Portable PDBs)
        var symbolServers = new[]
        {
            "https://symbols.nuget.org/download/symbols",
            "https://msdl.microsoft.com/download/symbols"
        };

        foreach (var server in symbolServers)
        {
            var url = $"{server}/{pdbName}/{symbolKey}/{pdbName}";
            log?.Invoke($"Trying symbol server: {server}");

            try
            {
                using var response = await HttpRetryHelper.GetWithRetryAsync(_client, url, log: log);
                if (response == null || !response.IsSuccessStatusCode)
                    continue;

                // Download to cache
                var cachePath = GetSymbolServerCachePath(pdbName, symbolKey);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                using (var fs = File.Create(cachePath))
                {
                    await response.Content.CopyToAsync(fs);
                }

                var result = TryReadPortablePdb(cachePath, log);
                if (result.reader != null)
                {
                    var serverHost = new Uri(server).Host;
                    log?.Invoke($"Successfully loaded PDB from symbol server");
                    return new PdbLookupResult(result.reader, result.provider, SymbolServer: serverHost);
                }
                if (result.isWindowsPdb)
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

        return new PdbLookupResult(null, null, windowsPdbDetected);
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

    /// <summary>
    /// Tries to download a Portable PDB from Microsoft's symbol server (MSDL).
    /// </summary>
    private async Task<PdbLookupResult> TryDownloadFromMsdlAsync(
        CodeViewDebugDirectoryData codeView,
        bool isPortablePdb,
        Action<string>? log)
    {
        bool windowsPdbDetected = false;

        var pdbName = Path.GetFileName(codeView.Path);
        var guid = codeView.Guid.ToString("N").ToUpperInvariant();
        var age = codeView.Age;

        // Portable PDBs use FFFFFFFF, Windows PDBs use the actual age
        // See: https://github.com/dotnet/symstore/blob/main/src/Microsoft.SymbolStore/KeyGenerators/PortablePDBFileKeyGenerator.cs
        var symbolKey = isPortablePdb
            ? $"{guid}FFFFFFFF"
            : $"{guid}{age:x}";

        // Check cache first
        var cachePath = GetSymbolServerCachePath(pdbName, symbolKey);
        if (File.Exists(cachePath))
        {
            log?.Invoke($"Using cached PDB from MSDL");
            var cached = TryReadPortablePdb(cachePath, log);
            if (cached.reader != null)
                return new PdbLookupResult(cached.reader, cached.provider, SymbolServer: "msdl.microsoft.com");
            if (cached.isWindowsPdb)
                windowsPdbDetected = true;
        }

        var url = $"https://msdl.microsoft.com/download/symbols/{pdbName}/{symbolKey}/{pdbName}";
        log?.Invoke($"Trying MSDL symbol server");

        try
        {
            using var response = await HttpRetryHelper.GetWithRetryAsync(_client, url, log: log);
            if (response == null || !response.IsSuccessStatusCode)
            {
                log?.Invoke($"MSDL: symbol not found");
                return new PdbLookupResult(null, null, windowsPdbDetected);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            using (var fs = File.Create(cachePath))
            {
                await response.Content.CopyToAsync(fs);
            }

            var result = TryReadPortablePdb(cachePath, log);
            if (result.reader != null)
            {
                log?.Invoke("Successfully loaded PDB from MSDL");
                return new PdbLookupResult(result.reader, result.provider, SymbolServer: "msdl.microsoft.com");
            }
            if (result.isWindowsPdb)
            {
                windowsPdbDetected = true;
                log?.Invoke("MSDL returned a Windows PDB (not supported)");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"MSDL error: {ex.Message}");
        }

        return new PdbLookupResult(null, null, windowsPdbDetected);
    }

    // ===== Shared helpers =====

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

    private (MetadataReader? reader, IDisposable? provider, bool isWindowsPdb) TryReadPortablePdb(string pdbPath, Action<string>? log)
    {
        try
        {
            var stream = File.OpenRead(pdbPath);

            // Check for Portable PDB magic header (BSJB)
            byte[] header = new byte[4];
            stream.ReadExactly(header, 0, 4);
            stream.Position = 0;

            if (header[0] != 'B' || header[1] != 'S' || header[2] != 'J' || header[3] != 'B')
            {
                // Check if it's a Windows PDB (MSF 7.00 header)
                bool isWindowsPdb = header[0] == 'M' && header[1] == 'i' && header[2] == 'c' && header[3] == 'r';
                stream.Dispose();
                return (null, null, isWindowsPdb);
            }

            var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            return (provider.GetMetadataReader(), new CompositeDisposable(provider, stream), false);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error reading PDB: {ex.Message}");
            return (null, null, false);
        }
    }

    private string? GetCachedPdbPath(string packageName, string packageVersion, string assemblyPath)
    {
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

    /// <summary>
    /// Helper class to dispose multiple disposables.
    /// </summary>
    private class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;

        public CompositeDisposable(params IDisposable[] disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            foreach (var d in _disposables)
            {
                try { d.Dispose(); } catch { }
            }
        }
    }
}
