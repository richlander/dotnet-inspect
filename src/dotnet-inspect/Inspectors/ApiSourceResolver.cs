using DotnetInspector.Commands;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

internal sealed record ApiSourceResult(
    string SearchPath,
    string? RuntimeAssemblyPath,
    string? PackageName,
    string? PackageVersion,
    string? ApiSource,
    string? ApiVersion,
    string? SelectedTfm,
    string? TempDir,
    string? TypeName,
    CommandContext Context);

internal static class ApiSourceResolver
{
    public static async Task<(ApiSourceResult Result, int? Error)> ResolveAsync(ApiOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;
        string? selectedTfm = null;

        string searchPath;
        string? runtimeAssemblyPath = null;
        string? packageName = null;
        string? packageVersion = null;
        string? apiSource = null;
        string? apiVersion = null;
        var typeName = options.TypeName;

        if (!string.IsNullOrEmpty(options.PackagePath))
        {
            var outcome = await PackageExtractor.ExtractPackageAsync(context.HttpClient, options.PackagePath, context.Logger.Log, "inspect-api", options.SourceOptions);
            if (!outcome.IsSuccess)
            {
                Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
                return (null!, 1);
            }
            var extracted = outcome.Result!;
            (searchPath, tempDir, packageName, packageVersion) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);
            apiSource = SourceKind.NuGet;
            apiVersion = packageVersion;

            if (!string.IsNullOrEmpty(options.AssemblyPath))
            {
                var (matchedAssembly, matchedTfm) = TfmSelector.FindAssemblyInPackage(searchPath, options.AssemblyPath, options.Tfm);
                if (matchedAssembly == null)
                {
                    Console.Error.WriteLine($"Error: Library '{options.AssemblyPath}' not found in package.");
                    return (null!, 1);
                }
                searchPath = matchedAssembly;
                selectedTfm = matchedTfm;
                if (selectedTfm != null)
                {
                    logger.Log($"Using TFM: {selectedTfm}");
                }
            }
            else if (!string.IsNullOrEmpty(typeName))
            {
                var (typeAssembly, matchedTfm) = TfmSelector.FindAssemblyContainingType(searchPath, typeName, options.Tfm);
                if (typeAssembly != null)
                {
                    searchPath = typeAssembly;
                    selectedTfm = matchedTfm;
                    logger.Log($"Resolved type '{typeName}' to {Path.GetFileName(searchPath)}");
                }
            }
            else if (!string.IsNullOrEmpty(options.Tfm))
            {
                var tfmAssembly = TfmSelector.FindAssemblyByTfm(searchPath, options.Tfm, packageName);
                if (tfmAssembly == null)
                {
                    Console.Error.WriteLine($"Error: No library found for TFM '{options.Tfm}'.");
                    return (null!, 1);
                }
                searchPath = tfmAssembly;
                selectedTfm = options.Tfm;
                logger.Log($"Using TFM: {options.Tfm}");
            }
        }
        else if (!string.IsNullOrEmpty(options.AssemblyPath))
        {
            if (!File.Exists(options.AssemblyPath))
            {
                Console.Error.WriteLine($"Error: File not found: {options.AssemblyPath}");
                return (null!, 1);
            }
            searchPath = options.AssemblyPath;
            apiSource = SourceKind.Library;
        }
        else if (!string.IsNullOrEmpty(options.PlatformAssembly))
        {
            var (assemblyPath, framework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                options.PlatformAssembly,
                context.HttpClient,
                logger.Log,
                options.PlatformFramework);

            if (error != null)
            {
                var frameworkShortName = TypeLookupService.TryMapFrameworkName(options.PlatformAssembly);
                if (frameworkShortName != null && !string.IsNullOrEmpty(typeName))
                {
                    logger.Log($"'{options.PlatformAssembly}' is a framework name, searching for type '{typeName}' in {frameworkShortName}");
                    List<string> lookupTempDirs = [];
                    var lookupResult = await TypeLookupService.FindTypeAsync(
                        typeName,
                        [frameworkShortName],
                        context.HttpClient,
                        logger,
                        lookupTempDirs);

                    if (lookupResult != null)
                    {
                        searchPath = lookupResult.AssemblyPath;
                        apiSource = SourceKind.Platform;
                        apiVersion = lookupResult.Version;
                        framework = lookupResult.Framework;
                        typeName = lookupResult.FullTypeName;
                        logger.Log($"Found type in {lookupResult.AssemblyName} ({lookupResult.Framework} {lookupResult.Version})");

                        var (runtimePath2, _, _, runtimeError2) = PlatformResolver.ResolveAssembly(
                            lookupResult.AssemblyName,
                            frameworkShortName,
                            packsDirectory: null,
                            useRuntimeAssemblies: true);

                        if (runtimeError2 == null && runtimePath2 != null)
                        {
                            runtimeAssemblyPath = runtimePath2;
                            logger.Log($"Using runtime library for PDB lookup: {runtimePath2}");
                        }
                    }
                    else
                    {
                        var allFrameworks = new[] { "runtime", "aspnetcore", "netstandard" };
                        var otherFrameworks = allFrameworks.Where(f => f != frameworkShortName).ToArray();
                        var foundElsewhere = await TypeLookupService.FindTypeAsync(
                            typeName,
                            otherFrameworks,
                            context.HttpClient,
                            logger,
                            lookupTempDirs);

                        if (foundElsewhere != null)
                        {
                            Console.Error.WriteLine($"Note: '{typeName}' not in {frameworkShortName}, found in {foundElsewhere.Framework}");
                            searchPath = foundElsewhere.AssemblyPath;
                            apiSource = SourceKind.Platform;
                            apiVersion = foundElsewhere.Version;
                            framework = foundElsewhere.Framework;
                            typeName = foundElsewhere.FullTypeName;
                            logger.Log($"Found type in {foundElsewhere.AssemblyName} ({foundElsewhere.Framework} {foundElsewhere.Version})");

                            var (runtimePath3, _, _, runtimeError3) = PlatformResolver.ResolveAssembly(
                                foundElsewhere.AssemblyName,
                                foundElsewhere.Framework,
                                packsDirectory: null,
                                useRuntimeAssemblies: true);

                            if (runtimeError3 == null && runtimePath3 != null)
                            {
                                runtimeAssemblyPath = runtimePath3;
                                logger.Log($"Using runtime library for PDB lookup: {runtimePath3}");
                            }
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Type '{typeName}' not found in any platform framework.");
                            return (null!, 1);
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return (null!, 1);
                }
            }
            else
            {
                searchPath = assemblyPath!;
                apiSource = SourceKind.Platform;
                apiVersion = version;
                logger.Log($"Using platform ref library: {framework} {version}");

                var (runtimePath, _, _, runtimeError) = PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly,
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: true);

                if (runtimeError == null && runtimePath != null)
                {
                    runtimeAssemblyPath = runtimePath;
                    logger.Log($"Using runtime library for PDB lookup: {runtimePath}");
                }
            }
        }
        else
        {
            Console.Error.WriteLine("Error: No package, library, or platform specified.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  dotnet-inspect type --package System.Text.Json");
            Console.Error.WriteLine("  dotnet-inspect member JsonSerializer --package System.Text.Json");
            return (null!, 1);
        }

        if (apiSource == SourceKind.Platform && apiVersion != null)
        {
            var dotIndex = apiVersion.IndexOf('.');
            if (dotIndex > 0)
            {
                var secondDot = apiVersion.IndexOf('.', dotIndex + 1);
                var majorMinor = secondDot > 0 ? apiVersion[..secondDot] : apiVersion;
                selectedTfm = $"net{majorMinor}";
            }
        }

        if (Directory.Exists(searchPath))
        {
            var dlls = TfmSelector.GetPackageDlls(searchPath);
            if (dlls.Count > 1)
            {
                var (selectedPath, tfm) = TfmSelector.SelectHighestTfmAssembly(dlls, searchPath, packageName);
                if (selectedPath != null)
                {
                    searchPath = selectedPath;
                    selectedTfm = tfm;
                    logger.Log($"Auto-selected TFM: {tfm}");
                }
                else
                {
                    Console.Error.WriteLine("Error: Multiple libraries found. Please specify one with --library or --tfm.");
                    return (null!, 1);
                }
            }
        }

        return (new ApiSourceResult(searchPath, runtimeAssemblyPath, packageName, packageVersion,
            apiSource, apiVersion, selectedTfm, tempDir, typeName, context), null);
    }
}
