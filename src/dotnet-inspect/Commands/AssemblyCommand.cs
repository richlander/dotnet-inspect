using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a single .NET assembly.
/// </summary>
public class AssemblyCommand
{
    public static async Task<int> ExecuteAsync(string? assemblyPath, AssemblyOptions options)
    {
        // Check for valid input source
        if (string.IsNullOrEmpty(assemblyPath) &&
            string.IsNullOrEmpty(options.PackagePath) &&
            string.IsNullOrEmpty(options.PlatformAssembly))
        {
            Console.Error.WriteLine("Error: Assembly path, --package, or --platform required.");
            Console.Error.WriteLine("Run 'dotnet-inspect assembly --help' for usage.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            // Parse package name and version for symbol package download
            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = PackageReferenceParser.ParsePackageReference(options.PackagePath);
            }

            if (!string.IsNullOrEmpty(options.PlatformAssembly))
            {
                // Resolve platform assembly - use runtime assemblies for full debug info
                var (resolvedPath, framework, version, error) = PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly,
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: true);  // Use runtime for audit (has debug info)

                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return 1;
                }

                logger.Log($"Using platform runtime assembly: {framework} {version}");

                var audit = await LibraryMetadataService.InspectAsync(resolvedPath!, options, logger, null, null, context.HttpClient, isPlatformAssembly: true);
                if (audit == null)
                {
                    Console.Error.WriteLine($"Error: Could not read assembly: {resolvedPath}");
                    return 1;
                }

                OutputFormatter.WriteAssemblyResult(audit, options);
                return 0;
            }
            else if (!string.IsNullOrEmpty(options.PackagePath))
            {
                // Extract from package
                var extractResult = await ExtractFromPackageAsync(assemblyPath, options.PackagePath, options.Tfm, logger, context.HttpClient);
                if (extractResult == null)
                {
                    return 1;
                }

                var (assemblyPaths, extractPath, extractTempDir, nupkgPath) = extractResult.Value;
                tempDir = extractTempDir;

                // Verify package signature if symbols or sourcelink-audit is specified and nupkg is available
                SignatureVerificationResult? signatureResult = null;
                if (options.HasAuditTier && nupkgPath != null)
                {
                    logger.Log($"Verifying package signature: {Path.GetFileName(nupkgPath)}");
                    signatureResult = await SignatureVerifier.VerifyAsync(nupkgPath);
                }

                // Inspect all assemblies
                var audits = await CollectPackageAuditsAsync(
                    assemblyPaths, options, logger, packageName, packageVersion,
                    extractPath, context.HttpClient, signatureResult);

                if (audits.Count == 0)
                {
                    Console.Error.WriteLine("Error: No assemblies could be read from the package.");
                    return 1;
                }

                if (audits.Count == 1)
                    OutputFormatter.WriteAssemblyResult(audits[0], options);
                else
                    OutputFormatter.WriteAssemblyResults(audits, options);

                return 0;
            }
            else
            {
                // Load from filesystem
                if (!File.Exists(assemblyPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {assemblyPath}");
                    return 1;
                }

                var audit = await LibraryMetadataService.InspectAsync(assemblyPath!, options, logger, null, null, context.HttpClient);
                if (audit == null)
                {
                    Console.Error.WriteLine($"Error: Could not read assembly: {assemblyPath}");
                    return 1;
                }

                OutputFormatter.WriteAssemblyResult(audit, options);
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            // Cleanup temp directory if we extracted from a package
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static async Task<List<AssemblyAudit>> CollectPackageAuditsAsync(
        List<string> assemblyPaths, AssemblyOptions options, VerboseLogger logger,
        string? packageName, string? packageVersion, string extractPath,
        HttpClient httpClient, SignatureVerificationResult? signatureResult)
    {
        var audits = new List<AssemblyAudit>();

        foreach (var targetPath in assemblyPaths)
        {
            var version = packageVersion ?? (packageName != null ? ExtractVersionFromPath(targetPath, packageName) : null);

            var audit = await LibraryMetadataService.InspectAsync(targetPath, options, logger, packageName, version, httpClient);
            if (audit == null)
            {
                logger.Log($"Warning: Could not read assembly: {Path.GetFileName(targetPath)}");
                continue;
            }

            // Populate TFM from path for multi-TFM display
            var relativePath = Path.GetRelativePath(extractPath, targetPath).Replace('\\', '/');
            audit.Tfm = TfmResolver.ExtractTfmFromPath(relativePath);

            if (signatureResult != null)
            {
                audit.Publisher = signatureResult.Publisher;
                audit.PublisherVerified = signatureResult.AuthorVerified;
                audit.RepositoryVerified = signatureResult.RepositoryVerified;
                audit.SignatureStatus = signatureResult.StatusMessage;
            }

            audits.Add(audit);
        }

        return audits;
    }

    private static async Task<(List<string> assemblyPaths, string extractPath, string? tempDir, string? nupkgPath)?> ExtractFromPackageAsync(string? assemblyName, string packageSource, string? tfm, VerboseLogger logger, HttpClient httpClient)
    {
        var resolution = await PackageResolverService.ResolvePackageAsync(packageSource, null, logger.Log, httpClient);
        if (resolution == null)
        {
            bool isLocalFile = packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
            if (isLocalFile)
                Console.Error.WriteLine($"Error: Package not found: {packageSource}");
            else
                Console.Error.WriteLine($"Error: Package '{packageSource}' not found on nuget.org");
            return null;
        }

        string extractPath = resolution.ExtractPath;
        string? tempDir = resolution.TempDir;
        string? nupkgPath = resolution.NupkgPath;

        // Find DLLs in the extracted package
        string[] allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);

        // --tfm all: return all assemblies from every TFM
        if (string.Equals(tfm, "all", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = TfmSelector.GetPackageDlls(extractPath);
            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("Error: No DLLs found in package.");
                if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }
            return (candidates, extractPath, tempDir, nupkgPath);
        }

        // --tfm <specific>: find assembly by TFM
        if (!string.IsNullOrEmpty(tfm))
        {
            var tfmAssembly = TfmSelector.FindAssemblyByTfm(extractPath, tfm);
            if (tfmAssembly == null)
            {
                Console.Error.WriteLine($"Error: No assembly found for TFM '{tfm}'.");
                Console.Error.WriteLine("Available TFMs:");
                var tfms = allDlls
                    .Select(d => TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, d).Replace('\\', '/')))
                    .Where(t => t != null)
                    .Distinct()
                    .OrderByDescending(t => TfmResolver.GetTfmPriority(t!));
                foreach (var t in tfms)
                {
                    Console.Error.WriteLine($"  {t}");
                }
                if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }
            logger.Log($"Using TFM: {tfm}");
            return ([tfmAssembly], extractPath, tempDir, nupkgPath);
        }

        // No --tfm and no assembly name: select the highest-priority TFM (default)
        if (string.IsNullOrEmpty(assemblyName))
        {
            var candidates = TfmSelector.GetPackageDlls(extractPath);
            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("Error: No DLLs found in package.");
                if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }

            var (selectedPath, selectedTfm) = TfmSelector.SelectHighestTfmAssembly(candidates, extractPath, resolution.PackageName);
            if (selectedPath == null)
            {
                // No TFM structure found, fall back to first DLL
                return ([candidates[0]], extractPath, tempDir, nupkgPath);
            }

            logger.Log($"Using TFM: {selectedTfm}");
            return ([selectedPath], extractPath, tempDir, nupkgPath);
        }

        // Normalize the assembly path for comparison
        string normalizedAssemblyName = assemblyName.Replace('\\', '/');

        // First try to match by relative path (for disambiguation)
        string[] matchingFiles = allDlls
            .Where(f =>
            {
                string relativePath = Path.GetRelativePath(extractPath, f).Replace('\\', '/');
                return relativePath.Equals(normalizedAssemblyName, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        // If no exact path match, try matching by filename
        if (matchingFiles.Length == 0)
        {
            matchingFiles = allDlls
                .Where(f => Path.GetFileName(f).Equals(assemblyName, StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(f).Equals(assemblyName + ".dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (matchingFiles.Length == 0)
        {
            Console.Error.WriteLine($"Error: Assembly '{assemblyName}' not found in package.");
            Console.Error.WriteLine("Use 'dotnet-inspect package <name> --files' to list available assemblies.");
            if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        if (matchingFiles.Length > 1)
        {
            Console.Error.WriteLine($"Multiple matches found for '{assemblyName}':");
            foreach (var f in matchingFiles)
            {
                Console.Error.WriteLine($"  {Path.GetRelativePath(extractPath, f)}");
            }
            Console.Error.WriteLine("Specify the full relative path to disambiguate.");
            if (tempDir != null) try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        logger.Log($"Found: {Path.GetRelativePath(extractPath, matchingFiles[0])}");
        return ([matchingFiles[0]], extractPath, tempDir, nupkgPath);
    }


    /// <summary>
    /// Extracts version from a cached package path.
    /// Path format: .../packages/packagename/version/lib/tfm/assembly.dll
    /// </summary>
    private static string? ExtractVersionFromPath(string dllPath, string packageName)
    {
        var normalizedPath = dllPath.Replace('\\', '/');
        var normalizedPackageName = packageName.ToLowerInvariant();

        // Look for pattern: /packagename/version/
        var searchPattern = $"/{normalizedPackageName}/";
        var index = normalizedPath.ToLowerInvariant().IndexOf(searchPattern, StringComparison.Ordinal);
        if (index < 0)
            return null;

        // Extract what comes after the package name
        var afterPackage = normalizedPath[(index + searchPattern.Length)..];
        var nextSlash = afterPackage.IndexOf('/');
        if (nextSlash > 0)
        {
            var possibleVersion = afterPackage[..nextSlash];
            // Verify it looks like a version (starts with digit)
            if (possibleVersion.Length > 0 && char.IsDigit(possibleVersion[0]))
            {
                return possibleVersion;
            }
        }

        return null;
    }
}
