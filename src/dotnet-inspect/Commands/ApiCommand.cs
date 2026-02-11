using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
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
    public static async Task<int> ExecuteAsync(ApiOptions options)
    {
        var typeName = options.TypeName;
        if (options.MemberFilter.Count > 0 && string.IsNullOrEmpty(typeName))
        {
            Console.Error.WriteLine("Error: --member requires a type argument.");
            Console.Error.WriteLine("Usage: dotnet-inspect api <type> --package <pkg> --member <name>");
            return 1;
        }

        // Validate section filters against all known api sections
        var typePipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var memberPipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var allApiSections = typePipeline.AllSectionNames.Concat(memberPipeline.AllSectionNames).Distinct().ToArray();
        options = options with
        {
            IncludeSections = SectionRegistry.Resolve(allApiSections, options.IncludeSections, out var includeError),
            ExcludeSections = SectionRegistry.Resolve(allApiSections, options.ExcludeSections, out var excludeError),
        };
        if (includeError || excludeError) return 1;

        // Bare -s without input: list all potential sections and exit
        if (options.IncludeSections is { Count: 0 } &&
            string.IsNullOrEmpty(options.PackagePath) &&
            string.IsNullOrEmpty(options.AssemblyPath) &&
            string.IsNullOrEmpty(options.PlatformAssembly))
        {
            SectionRegistry.ListSections(allApiSections);
            return 0;
        }

        // Bare -s with input: discover which sections have data (set flag for later)
        bool discoverSections = options.IncludeSections is { Count: 0 };

        // Auto-promote verbosity when -s targets specific sections
        if (options.IncludeSections is { Count: > 0 })
        {
            var typeVerbosity = typePipeline.GetRequiredVerbosity(options.IncludeSections);
            var memberVerbosity = memberPipeline.GetRequiredVerbosity(options.IncludeSections);
            var requiredVerbosity = typeVerbosity > memberVerbosity ? typeVerbosity : memberVerbosity;
            if (requiredVerbosity > options.Verbosity)
                options = options with { Verbosity = requiredVerbosity };
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
                api.Tfm = selectedTfm;
                api.Source = apiSource;
                api.Version = apiVersion;
                api.Library = apiDllPath != null ? Path.GetFileName(apiDllPath) : null;

                if ((options.ShowDocs || options.ShowSamples || options.SourceLinkOnly) && pdbLookupPath != null)
                {
                    logger.Log("Enriching types with source info...");
                    foreach (var type in api.Types)
                    {
                        await SourceEnricher.EnrichTypeWithSourceInfoAsync(type, type.FullName, pdbLookupPath, options, logger, context.HttpClient);
                    }
                }

                if (discoverSections)
                {
                    typePipeline.ListEffectiveSections(api);
                    return 0;
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
                    if (options.MemberFilter.Count > 0)
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

                    // Default --docs on for single-type view unless explicitly disabled
                    var effectiveOptions = options;
                    if (!options.DocsExplicitlySet)
                        effectiveOptions = options with { ShowDocs = true };

                    // --index: select a specific overload and show IL
                    if (options.OverloadIndex.HasValue)
                    {
                        if (options.MemberFilter.Count != 1)
                        {
                            Console.Error.WriteLine("Error: --index requires exactly one member name via -m.");
                            return 1;
                        }

                        var memberName = options.MemberFilter.First();
                        var overloads = apiType.Members
                            .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        int idx = options.OverloadIndex.Value;
                        if (idx < 1 || idx > overloads.Count)
                        {
                            Console.Error.WriteLine($"Error: --index {idx} is out of range. {memberName} has {overloads.Count} overload(s).");
                            return 1;
                        }

                        var selected = overloads[idx - 1];
                        apiType.Members = [selected];
                        var exclude = effectiveOptions.ExcludeSections ?? [];
                        exclude.Add("Remote Source");
                        effectiveOptions = effectiveOptions with { DllPath = apiDllPath, ExcludeSections = exclude };
                    }

                    // --params / -of: select overload by parameter type matching
                    if (!options.OverloadIndex.HasValue && (options.ParamTypes != null || options.FirstParamType != null))
                    {
                        if (options.MemberFilter.Count != 1)
                        {
                            Console.Error.WriteLine("Error: --params/-of requires exactly one member name via -m.");
                            return 1;
                        }

                        var memberName = options.MemberFilter.First();
                        var overloads = apiType.Members
                            .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        var matches = new List<(ApiMember member, int index)>();
                        for (int i = 0; i < overloads.Count; i++)
                        {
                            var sig = overloads[i].Signature;
                            if (sig == null) continue;
                            var paramTypes = ExtractParameterTypes(sig);

                            if (options.ParamTypes != null)
                            {
                                if (MatchesParamTypes(paramTypes, options.ParamTypes))
                                    matches.Add((overloads[i], i));
                            }
                            else if (options.FirstParamType != null)
                            {
                                if (paramTypes.Count > 0 && SimpleNameMatches(paramTypes[0], options.FirstParamType))
                                    matches.Add((overloads[i], i));
                            }
                        }

                        if (matches.Count == 0)
                        {
                            var filter = options.ParamTypes != null
                                ? $"--params \"{string.Join(",", options.ParamTypes)}\""
                                : $"-of \"{options.FirstParamType}\"";
                            Console.Error.WriteLine($"Error: No overload of {memberName} matches {filter}.");
                            return 1;
                        }

                        if (matches.Count > 1)
                        {
                            Console.Error.WriteLine($"Error: Multiple overloads of {memberName} match. Use --params with more specific types to disambiguate:");
                            foreach (var (m, _) in matches)
                                Console.Error.WriteLine($"  {m.Signature}");
                            return 1;
                        }

                        var (selected, overloadIdx) = matches[0];
                        apiType.Members = [selected];
                        var exclude = effectiveOptions.ExcludeSections ?? [];
                        exclude.Add("Remote Source");
                        effectiveOptions = effectiveOptions with
                        {
                            DllPath = apiDllPath,
                            ExcludeSections = exclude,
                            OverloadIndex = overloadIdx + 1
                        };
                    }

                    // Always enrich with SourceLink info for single-type view (Sources section).
                    // Doc comment fetching is gated by ShowDocs inside the enricher.
                    {
                        var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                        if (pdbLookupPath != null)
                            await SourceEnricher.EnrichTypeWithSourceInfoAsync(apiType, typeName, pdbLookupPath, effectiveOptions, logger, context.HttpClient);
                    }

                    // Resolve method source code for --index view (after PDB acquisition)
                    if (effectiveOptions.OverloadIndex.HasValue && apiDllPath != null)
                    {
                        var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                        var methodSource = await ResolveMethodSourceAsync(
                            pdbLookupPath, apiType.FullName,
                            effectiveOptions.MemberFilter.First(),
                            effectiveOptions.OverloadIndex.Value - 1,
                            effectiveOptions, context.HttpClient, logger);

                        if (methodSource != null)
                            effectiveOptions = effectiveOptions with { MethodSource = methodSource };
                    }

                    if (discoverSections)
                    {
                        memberPipeline.ListEffectiveSections(apiType);
                        return 0;
                    }

                    WriteTypeOutput(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions);
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
                        api.Library = apiDllPath != null ? Path.GetFileName(apiDllPath) : null;

                        options = options with
                        {
                            TypeFilter = typeName,
                            Verbosity = options.Verbosity < Verbosity.Minimal ? Verbosity.Minimal : options.Verbosity
                        };
                        if (discoverSections)
                        {
                            typePipeline.ListEffectiveSections(api);
                            return 0;
                        }

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
            api.PublicMethodCount = api.Types.Sum(t => t.Members.Count(m => m.Kind is "method" or "constructor"));
            api.PublicPropertyCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "property"));
            api.PublicFieldCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "field"));
            api.PublicEventCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "event"));
        }

        // Apply unsafe filter
        if (options.UnsafeOnly)
        {
            foreach (var type in api.Types)
            {
                type.Members = type.Members.Where(m => m.IsUnsafe).ToList();
            }
            api.Types = api.Types.Where(t => t.Members.Count > 0).ToList();
            api.PublicTypeCount = api.Types.Count;
            api.PublicMethodCount = api.Types.Sum(t => t.Members.Count(m => m.Kind is "method" or "constructor"));
            api.PublicPropertyCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "property"));
            api.PublicFieldCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "field"));
            api.PublicEventCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "event"));
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

    // ===== Method Source Resolution =====

    private static async Task<MethodSourceContext?> ResolveMethodSourceAsync(
        string dllPath, string typeName, string methodName, int overloadIndex,
        ApiOptions options, HttpClient httpClient, VerboseLogger logger)
    {
        try
        {
            using var service = SourceLinkService.Open(dllPath, logger.Log);
            var context = service.Context;

            // Acquire PDB if needed (same flow as SourceEnricher)
            if (context.NeedsPdb)
            {
                var (pkgName, pkgVersion) = !string.IsNullOrEmpty(options.PackagePath)
                    ? PackageReferenceParser.ParsePackageReference(options.PackagePath)
                    : (null, null);

                await SourceEnricher.AcquirePdbAsync(context, httpClient,
                    pkgName, pkgVersion,
                    isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log);
            }

            if (!service.HasPdb || !service.HasSourceLink)
                return null;

            var methodInfo = service.ResolveMethodSource(typeName, methodName, overloadIndex, publicOnly: true);
            if (methodInfo?.SourceUrl == null)
                return null;

            var fetcher = new SourceFetcher(httpClient);
            var content = await fetcher.FetchSourceAsync(methodInfo.SourceUrl);
            if (content == null)
                return null;

            var lines = content.Split('\n');
            int startLine = methodInfo.StartLine;
            int endLine = Math.Min(methodInfo.EndLine, lines.Length);

            // Scan backward from first sequence point to capture method signature
            // Sequence points start at the first executable statement, so we need to
            // go back past the opening brace, modifiers, attributes, etc.
            int sigStart = startLine;
            for (int i = startLine - 2; i >= Math.Max(0, startLine - 15); i--)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith("///") || trimmed.StartsWith("//")
                    || trimmed.StartsWith("[") || trimmed.StartsWith("#"))
                    continue;
                if (trimmed == "{")
                    continue;

                sigStart = i + 1; // 0-based → 1-based
                // Check if this line looks like a method signature (has access modifier or return type)
                if (trimmed.StartsWith("public") || trimmed.StartsWith("private")
                    || trimmed.StartsWith("protected") || trimmed.StartsWith("internal")
                    || trimmed.StartsWith("static") || trimmed.Contains(methodName))
                    break;
            }

            // Extract lines (convert from 1-based sequence points to 0-based array)
            int from = sigStart - 1;
            int to = endLine; // endLine is 1-based, so endLine as exclusive index is correct

            // Scan forward to include the closing brace (sequence points don't cover it)
            for (int i = to; i < Math.Min(to + 3, lines.Length); i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("}"))
                {
                    to = i + 1;
                    break;
                }
                if (trimmed.Length > 0)
                    break;
            }

            if (from < 0) from = 0;
            if (to > lines.Length) to = lines.Length;

            var methodLines = lines[from..to];

            // Dedent: find minimum indentation and remove it
            int minIndent = methodLines
                .Where(l => l.TrimStart().Length > 0)
                .Select(l => l.Length - l.TrimStart().Length)
                .DefaultIfEmpty(0)
                .Min();

            var dedented = methodLines.Select(l => l.Length >= minIndent ? l[minIndent..] : l);
            var sourceCode = string.Join('\n', dedented).TrimEnd();

            return new MethodSourceContext(sourceCode, methodInfo.SourceUrl);
        }
        catch
        {
            return null;
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

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter)).ToList();

        if (options.UnsafeOnly)
            members = members.Where(m => m.IsUnsafe).ToList();

        if (options.Limit.HasValue && members.Count > options.Limit.Value)
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

    /// <summary>
    /// Extracts parameter type names from a signature string like "TValue Method(System.IO.Stream s, int count)".
    /// Returns fully qualified type names without parameter names or default values.
    /// </summary>
    static List<string> ExtractParameterTypes(string signature)
    {
        List<string> types = [];
        int parenStart = signature.IndexOf('(');
        if (parenStart < 0) return types;
        int parenEnd = signature.LastIndexOf(')');
        if (parenEnd <= parenStart + 1) return types;

        var paramSection = signature.AsSpan((parenStart + 1)..(parenEnd));
        int depth = 0;
        int segStart = 0;

        for (int i = 0; i <= paramSection.Length; i++)
        {
            char c = i < paramSection.Length ? paramSection[i] : ',';
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                var seg = paramSection[segStart..i].Trim();
                if (seg.Length > 0)
                    types.Add(ExtractTypeFromParam(seg));
                segStart = i + 1;
            }
        }

        return types;
    }

    /// <summary>
    /// Extracts the type portion from a single parameter like "System.IO.Stream utf8Json" or "int count = 0".
    /// Handles ref/out/in/params modifiers and generic types.
    /// </summary>
    static string ExtractTypeFromParam(ReadOnlySpan<char> param)
    {
        // Strip modifiers: ref, out, in, params, this (extension)
        var s = param.ToString();
        foreach (var mod in (ReadOnlySpan<string>)["ref ", "out ", "in ", "params ", "this "])
        {
            if (s.StartsWith(mod))
            {
                s = s[mod.Length..];
                break;
            }
        }

        // Strip default value: "Type name = default"
        int eqIdx = s.IndexOf(" = ");
        if (eqIdx > 0)
            s = s[..eqIdx];

        // The type is everything before the last space (the parameter name).
        // But generic types can have spaces inside <>, so find last space outside angle brackets.
        int depth = 0;
        int lastSpace = -1;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '<') depth++;
            else if (s[i] == '>') depth--;
            else if (s[i] == ' ' && depth == 0) lastSpace = i;
        }

        return lastSpace > 0 ? s[..lastSpace] : s;
    }

    /// <summary>
    /// Checks if a fully qualified type name matches a simple (unqualified) type name.
    /// "System.Text.Json.JsonDocument" matches "JsonDocument".
    /// Also matches if the user provides the full name.
    /// </summary>
    static bool SimpleNameMatches(string fullTypeName, string simpleName)
    {
        if (string.Equals(fullTypeName, simpleName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Match simple name: extract after last '.' that is outside angle brackets
        int depth = 0;
        int lastDot = -1;
        for (int i = 0; i < fullTypeName.Length; i++)
        {
            if (fullTypeName[i] == '<') depth++;
            else if (fullTypeName[i] == '>') depth--;
            else if (fullTypeName[i] == '.' && depth == 0) lastDot = i;
        }

        if (lastDot > 0)
        {
            var simple = fullTypeName[(lastDot + 1)..];
            return string.Equals(simple, simpleName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Checks if extracted parameter types match the user-specified type list.
    /// Each entry is matched by simple name. Parameter count must match exactly.
    /// </summary>
    static bool MatchesParamTypes(List<string> extractedTypes, string[] requestedTypes)
    {
        if (extractedTypes.Count != requestedTypes.Length) return false;
        for (int i = 0; i < extractedTypes.Count; i++)
        {
            if (!SimpleNameMatches(extractedTypes[i], requestedTypes[i]))
                return false;
        }
        return true;
    }

}

