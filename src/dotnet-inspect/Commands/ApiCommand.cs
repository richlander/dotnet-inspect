using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays the public API shape of a specific type.
/// Uses hybrid Markout serializer + imperative rendering.
/// </summary>
public class ApiCommand
{
    public const string Name = "api";
    public static async Task<int> ExecuteAsync(string? typeName, ApiOptions options)
    {
        if (options.MemberFilter?.Count > 0 && string.IsNullOrEmpty(typeName))
        {
            Console.Error.WriteLine("Error: --member requires a type argument.");
            Console.Error.WriteLine("Usage: dotnet-inspect api <type> --package <pkg> --member <name>");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            string searchPath;
            string? runtimeAssemblyPath = null;
            string? packageName = null;
            string? packageVersion = null;
            string? apiSource = null;
            string? apiVersion = null;

            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                var extracted = await Packages.PackageExtractor.ExtractPackageAsync(context.HttpClient, options.PackagePath, context.Logger.Log, "inspect-api", options.SourceOptions);
                if (extracted == null)
                    return 1;
                (searchPath, tempDir, packageName, packageVersion) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);
                apiSource = "NuGet";
                apiVersion = packageVersion;

                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = TfmSelector.FindAssemblyByTfm(searchPath, options.Tfm);
                    if (tfmAssembly == null)
                    {
                        Console.Error.WriteLine($"Error: No library found for TFM '{options.Tfm}'.");
                        return 1;
                    }
                    searchPath = tfmAssembly;
                    logger.Log($"Using TFM: {options.Tfm}");
                }
                else if (!string.IsNullOrEmpty(options.AssemblyPath))
                {
                    var targetPath = Path.Combine(searchPath, options.AssemblyPath.Replace('\\', '/'));
                    if (!File.Exists(targetPath))
                    {
                        Console.Error.WriteLine($"Error: Library '{options.AssemblyPath}' not found in package.");
                        return 1;
                    }
                    searchPath = targetPath;
                }
            }
            else if (!string.IsNullOrEmpty(options.AssemblyPath))
            {
                if (!File.Exists(options.AssemblyPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {options.AssemblyPath}");
                    return 1;
                }
                searchPath = options.AssemblyPath;
                apiSource = "Library";
            }
            else if (!string.IsNullOrEmpty(options.PlatformAssembly))
            {
                var (assemblyPath, framework, version, error) = PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly,
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: false);

                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return 1;
                }

                searchPath = assemblyPath!;
                apiSource = $"Platform ({framework})";
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
            else
            {
                Console.Error.WriteLine("Error: No package, library, or platform specified.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Example: dotnet-inspect api System.Text.Json JsonSerializer");
                return 1;
            }

            string? selectedTfm = null;

            // Derive TFM for platform assemblies from the version
            if (apiSource?.StartsWith("Platform") == true && apiVersion != null)
            {
                var dotIndex = apiVersion.IndexOf('.');
                if (dotIndex > 0)
                {
                    var secondDot = apiVersion.IndexOf('.', dotIndex + 1);
                    var majorMinor = secondDot > 0 ? apiVersion[..secondDot] : apiVersion;
                    selectedTfm = $"net{majorMinor}";
                }
            }

            // Auto-select TFM when searchPath is a directory with multiple DLLs
            if (Directory.Exists(searchPath))
            {
                var dlls = TfmSelector.GetPackageDlls(searchPath);
                if (dlls.Count > 1)
                {
                    var (selectedPath, tfm) = TfmSelector.SelectHighestTfmAssembly(dlls, searchPath);
                    if (selectedPath != null)
                    {
                        searchPath = selectedPath;
                        selectedTfm = tfm;
                        logger.Log($"Auto-selected TFM: {tfm}");
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: Multiple libraries found. Please specify one with --library or --tfm.");
                        return 1;
                    }
                }
            }

            if (string.IsNullOrEmpty(typeName))
            {
                // No type specified - list all types
                var (api, apiDllPath) = ApiServices.ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from library.");
                    return 1;
                }

                if (api.Types.Count == 0 && api.TypeForwarders.Count > 0 && apiDllPath != null)
                {
                    ApiServices.ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);
                }

                if (!string.IsNullOrEmpty(options.PackagePath))
                {
                    var (pkgName, _) = PackageReferenceParser.ParsePackageReference(options.PackagePath);
                    api.Name = pkgName;
                }
                else if (apiDllPath != null)
                {
                    api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
                }

                var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                if (pdbLookupPath != null)
                {
                    api.RepositoryUrl = await SourceEnricher.ExtractRepositoryUrlAsync(pdbLookupPath, options, logger, context.HttpClient);
                }
                api.Tfm = selectedTfm;
                api.Source = apiSource;
                api.Version = apiVersion;

                if ((options.ShowDocs || options.ShowSamples || options.SourceLinkOnly) && pdbLookupPath != null)
                {
                    logger.Log("Enriching types with source info...");
                    foreach (var type in api.Types)
                    {
                        await SourceEnricher.EnrichTypeWithSourceInfoAsync(type, type.FullName, pdbLookupPath, options, logger, context.HttpClient);
                    }
                }

                WriteFullApiOutput(api, options, selectedTfm);
            }
            else
            {
                typeName = GenericTypeNameConverter.Convert(typeName);

                var (api, apiDllPath) = ApiServices.ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from library.");
                    return 1;
                }

                if (api.Types.Count == 0 && api.TypeForwarders.Count > 0 && apiDllPath != null)
                    ApiServices.ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);

                var allTypeNames = api.Types.Select(t => t.FullName).ToList();
                var lookupResult = TypeMatcher.Lookup(allTypeNames, typeName);

                if (lookupResult.Match != null)
                {
                    var apiType = api.Types.First(t => t.FullName == lookupResult.Match);

                    // Check each member filter before producing output
                    if (options.MemberFilter?.Count > 0 && apiType.Members != null)
                    {
                        var memberNames = apiType.Members.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        List<string> missedFilters = [];

                        foreach (var filter in options.MemberFilter)
                        {
                            bool isGlob = filter.Contains('*') || filter.Contains('?');
                            bool anyMatch = isGlob
                                ? memberNames.Any(n => TypeMatcher.MatchesGlob(n, filter))
                                : memberNames.Any(n => string.Equals(n, filter, StringComparison.OrdinalIgnoreCase));

                            if (!anyMatch)
                                missedFilters.Add(filter);
                        }

                        if (missedFilters.Count > 0)
                        {
                            Console.Error.WriteLine($"Error: No members matched filter '{string.Join(", ", missedFilters)}'");
                            var memberResult = TypeMatcher.LookupMembers(memberNames, missedFilters);
                            if (memberResult.Suggestions.Count > 0)
                            {
                                Console.Error.WriteLine();
                                Console.Error.WriteLine("Did you mean:");
                                foreach (var s in memberResult.Suggestions)
                                    Console.Error.WriteLine($"  {s}");
                            }
                            return 1;
                        }
                    }

                    var foundIn = apiDllPath != null ? Path.GetFileNameWithoutExtension(apiDllPath) : null;
                    if (options.ShowDocs || options.ShowSamples || options.SourceLinkOnly)
                    {
                        var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                        if (pdbLookupPath != null)
                            await SourceEnricher.EnrichTypeWithSourceInfoAsync(apiType, typeName, pdbLookupPath, options, logger, context.HttpClient);
                    }

                    WriteTypeOutput(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, options);
                }
                else if (lookupResult.Suggestions.Count > 0)
                {
                    bool isGlob = typeName.Contains('*') || typeName.Contains('?');
                    if (isGlob)
                    {
                        // Glob matched multiple types — show types view with filter
                        if (!string.IsNullOrEmpty(options.PackagePath))
                        {
                            var (pkgName, _) = PackageReferenceParser.ParsePackageReference(options.PackagePath);
                            api.Name = pkgName;
                        }
                        else if (apiDllPath != null)
                        {
                            api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
                        }
                        api.Tfm = selectedTfm;
                        api.Source = apiSource;
                        api.Version = apiVersion;

                        options = options with
                        {
                            TypeFilter = typeName,
                            Verbosity = options.Verbosity < Verbosity.Minimal ? Verbosity.Minimal : options.Verbosity
                        };
                        WriteFullApiOutput(api, options, selectedTfm);
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("Did you mean:");
                        foreach (var s in lookupResult.Suggestions)
                            Console.Error.WriteLine($"  {s}");
                        return 1;
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                    return 1;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    // ===== Full API Surface Rendering =====

    private static void WriteFullApiOutput(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        // Apply type filter
        if (!string.IsNullOrEmpty(options.TypeFilter))
        {
            api.Types = api.Types
                .Where(t => TypeMatcher.MatchesTypeFilter(t.FullName, options.TypeFilter))
                .ToList();
            api.PublicTypeCount = api.Types.Count;
        }

        // Apply sourcelink-only filter
        if (options.SourceLinkOnly)
        {
            api.Types = api.Types.Where(t => !string.IsNullOrEmpty(t.SourceUrl)).ToList();
            api.PublicTypeCount = api.Types.Count;
            api.PublicMethodCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind is "method" or "constructor") ?? 0);
            api.PublicPropertyCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "property") ?? 0);
            api.PublicFieldCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "field") ?? 0);
            api.PublicEventCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "event") ?? 0);
        }

        // Apply unsafe filter
        if (options.UnsafeOnly)
        {
            foreach (var type in api.Types)
            {
                if (type.Members != null)
                    type.Members = type.Members.Where(m => m.IsUnsafe).ToList();
            }
            api.Types = api.Types.Where(t => t.Members?.Count > 0).ToList();
            api.PublicTypeCount = api.Types.Count;
            api.PublicMethodCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind is "method" or "constructor") ?? 0);
            api.PublicPropertyCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "property") ?? 0);
            api.PublicFieldCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "field") ?? 0);
            api.PublicEventCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "event") ?? 0);
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(api, ApiJsonContext.Default.ApiSurface));
        }
        else
        {
            Console.WriteLine(ApiOutputFormatter.RenderFullApiMarkdown(api, options));
        }
    }

    // ===== Single Type Rendering =====

    private static void WriteTypeOutput(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options)
    {
        if (options.ShapeOutput)
        {
            ApiOutputFormatter.WriteShapeOutput(type, foundIn, packageName, packageVersion, options.MemberFilter);
        }
        else if (options.JsonOutput)
        {
            WriteJsonTypeOutput(type, options);
        }
        else
        {
            Console.WriteLine(ApiOutputFormatter.RenderTypeMarkdown(type, foundIn, packageName, packageVersion, apiSource, selectedTfm, options));
        }
    }

    private static void WriteJsonTypeOutput(ApiType type, ApiOptions options)
    {
        var outputType = type;
        var members = type.Members;

        if (options.MemberFilter?.Count > 0 && members != null)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter)).ToList();

        if (options.UnsafeOnly && members != null)
            members = members.Where(m => m.IsUnsafe).ToList();

        if (options.Limit.HasValue && members != null && members.Count > options.Limit.Value)
            members = members.Take(options.Limit.Value).ToList();

        if (members != type.Members)
        {
            outputType = new ApiType
            {
                Namespace = type.Namespace,
                Name = type.Name,
                Kind = type.Kind,
                IsSealed = type.IsSealed,
                IsAbstract = type.IsAbstract,
                IsStatic = type.IsStatic,
                BaseType = type.BaseType,
                Interfaces = type.Interfaces,
                Members = members,
                SourceFilePath = type.SourceFilePath,
                SourceUrl = type.SourceUrl,
                GitHubBrowseUrl = type.GitHubBrowseUrl,
                SourceLineNumber = type.SourceLineNumber,
                Documentation = type.Documentation
            };
        }

        if (options.CompactJson)
            Console.WriteLine(JsonSerializer.Serialize(outputType, ApiTypeCompactJsonContext.Default.ApiType));
        else
            Console.WriteLine(JsonSerializer.Serialize(outputType, ApiTypeJsonContext.Default.ApiType));
    }

}

