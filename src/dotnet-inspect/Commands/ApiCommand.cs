using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using Markout;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.Commands;

/// <summary>
/// Shared helpers for type and member commands.
/// Also provides a compatibility shim for callers that use ApiCommand.ExecuteAsync directly.
/// </summary>
public class ApiCommand
{
    public const string Name = "api";

    // ===== Compatibility Shim =====

    public static Task<int> ExecuteAsync(ApiOptions options) => options switch
    {
        MemberOptions mo => MemberCommand.ExecuteAsync(mo),
        TypeOptions to => TypeCommand.ExecuteAsync(to),
        _ => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = options.TypeName, PackagePath = options.PackagePath, AssemblyPath = options.AssemblyPath,
            PlatformAssembly = options.PlatformAssembly, PlatformFramework = options.PlatformFramework,
            Tfm = options.Tfm, IncludeAll = options.IncludeAll, Verbose = options.Verbose,
            ShowDocs = options.ShowDocs, DocsExplicitlySet = options.DocsExplicitlySet,
            UseLocalDocs = options.UseLocalDocs, ShowSamples = options.ShowSamples,
            BrowsableUrls = options.BrowsableUrls, Verbosity = options.Verbosity,
            JsonOutput = options.JsonOutput, CompactJson = options.CompactJson,
            OneLine = options.OneLine, OneLineExplicitlySet = options.OneLineExplicitlySet,
            NoHeader = options.NoHeader, Limit = options.Limit, MemberFilter = options.MemberFilter,
            KindFilter = options.KindFilter, UnsafeOnly = options.UnsafeOnly,
            IncludeSections = options.IncludeSections, ExcludeSections = options.ExcludeSections,
            Select = options.Select, Columns = options.Columns, SourceOptions = options.SourceOptions,
            TipLevel = options.TipLevel
        })
    };

    // ===== Shared Preamble =====

    internal record PreambleResult(
        ApiOptions Options,
        SectionPipeline<ApiSurface> TypePipeline,
        SectionPipeline<ApiType> MemberPipeline);

    internal static (PreambleResult Result, int? Error) RunPreamble(ApiOptions options)
    {
        // Validate exclude section filters against all known api sections
        var typePipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var memberPipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var allApiSections = typePipeline.AllSectionNames.Concat(memberPipeline.AllSectionNames).Distinct().ToArray();
        var (_, resolvedExclude) = SectionRegistry.ResolveFilters(
            allApiSections, null, options.ExcludeSections, out var sectionError);
        if (sectionError)
            return (null!, 1);
        options = options with { ExcludeSections = resolvedExclude };

        // Discovery mode: any bare projection flag lists available names
        if (SelectResolver.IsDiscovery(options.Select, options.Columns, options.Fields))
        {
            var context2 = new MarkoutContext();
            var typeSchema = context2.GetSchemaInfo<CliApiSurface>();
            var memberSchema = context2.GetSchemaInfo<TypeView>();
            SelectResolver.Discover(options.Select, options.Columns, options.Fields,
                allApiSections, typeSchema, memberSchema);
            return (null!, 0);
        }

        // -S/--select with values: resolve as section filter for backpressure
        var selectSections = SelectResolver.ResolveSelectAsSections(
            options.Select, allApiSections, out var selectError);
        if (selectError)
            return (null!, 1);
        if (selectSections != null)
            options = options with { IncludeSections = selectSections };

        // Auto-promote verbosity when -S targets specific sections
        if (options.IncludeSections is { Count: > 0 })
        {
            var typeVerbosity = typePipeline.GetRequiredVerbosity(options.IncludeSections);
            var memberVerbosity = memberPipeline.GetRequiredVerbosity(options.IncludeSections);
            var requiredVerbosity = typeVerbosity > memberVerbosity ? typeVerbosity : memberVerbosity;
            if (requiredVerbosity > options.Verbosity)
                options = options with { Verbosity = requiredVerbosity };
        }

        // Warn if --oneline combined with detailed verbosity without section selector
        OutputFormatResolver.WarnIfOneLineDetailMismatch(options.OneLine, options.Verbosity, options.IncludeSections);

        return (new PreambleResult(options, typePipeline, memberPipeline), null);
    }

    // ===== Shared Source Resolution =====

    internal record SourceResult(
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

    internal static async Task<(SourceResult Result, int? Error)> ResolveSourceAsync(ApiOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        string searchPath;
        string? runtimeAssemblyPath = null;
        string? packageName = null;
        string? packageVersion = null;
        string? apiSource = null;
        string? apiVersion = null;
        var typeName = options.TypeName;

        if (!string.IsNullOrEmpty(options.PackagePath))
        {
            var outcome = await Packages.PackageExtractor.ExtractPackageAsync(context.HttpClient, options.PackagePath, context.Logger.Log, "inspect-api", options.SourceOptions);
            if (!outcome.IsSuccess)
            {
                Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
                return (null!, 1);
            }
            var extracted = outcome.Result!;
            (searchPath, tempDir, packageName, packageVersion) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);
            apiSource = SourceKind.NuGet;
            apiVersion = packageVersion;

            if (!string.IsNullOrEmpty(options.Tfm))
            {
                var tfmAssembly = TfmSelector.FindAssemblyByTfm(searchPath, options.Tfm, packageName);
                if (tfmAssembly == null)
                {
                    Console.Error.WriteLine($"Error: No library found for TFM '{options.Tfm}'.");
                    return (null!, 1);
                }
                searchPath = tfmAssembly;
                logger.Log($"Using TFM: {options.Tfm}");
            }
            else if (!string.IsNullOrEmpty(options.AssemblyPath))
            {
                var targetPath = Path.Combine(searchPath, options.AssemblyPath.Replace('\\', '/'));
                // If it's a bare filename, search for it within the package
                if (!File.Exists(targetPath) && !options.AssemblyPath.Contains('/') && !options.AssemblyPath.Contains('\\'))
                {
                    var found = Directory.EnumerateFiles(searchPath, options.AssemblyPath, SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null) targetPath = found;
                }
                if (!File.Exists(targetPath))
                {
                    Console.Error.WriteLine($"Error: Library '{options.AssemblyPath}' not found in package.");
                    return (null!, 1);
                }
                searchPath = targetPath;
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
                // Check if PlatformAssembly is actually a framework name and we have a type to search for
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
                        typeName = lookupResult.FullTypeName; // Use the resolved full name
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
                        // Type not found in specified framework - search all frameworks
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
                            // Found in a different framework - use it and hint
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

        string? selectedTfm = null;

        // Derive TFM for platform assemblies from the version
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

        // Auto-select TFM when searchPath is a directory with multiple DLLs
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

        return (new SourceResult(searchPath, runtimeAssemblyPath, packageName, packageVersion,
            apiSource, apiVersion, selectedTfm, tempDir, typeName, context), null);
    }

    // ===== Full API Surface Rendering =====

    internal static void WriteFullApiOutput(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        // Apply type filter
        var typeFilter = (options as TypeOptions)?.TypeFilter;
        if (!string.IsNullOrEmpty(typeFilter))
        {
            api.Types = api.Types
                .Where(t => TypeMatcher.MatchesTypeFilter(t.FullName, typeFilter))
                .ToList();
            api.PublicTypeCount = api.Types.Count;
        }

        // Apply kind filter (type kinds for multi-type listing)
        if (options.KindFilter.Count > 0)
        {
            api.Types = api.Types.Where(t => options.KindFilter.Contains(t.Kind)).ToList();
            api.PublicTypeCount = api.Types.Count;
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
            return;
        }

        var (view, truncatedCount) = ApiOutputFormatter.BuildFullApiView(api, options);

        if (options.OneLine)
        {
            var (oneLineView, _) = ApiOutputFormatter.BuildSurfaceOneLineView(api, options);
            var writerOpts = new MarkoutWriterOptions
            {
                Projection = OutputFormatter.BuildProjection(options.Select, options.Columns, options.Fields)
            };
            new MarkoutContext().Serialize(oneLineView, Console.Out, new Markout.OneLineFormatter(showHeader: !options.NoHeader), writerOpts);
        }
        else
        {
            var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
            var writer = new Markout.MarkoutWriter(new Markout.MarkdownFormatter(), writerOptions);
            new MarkoutContext().Serialize(view, writer);

            if (truncatedCount > 0)
                writer.WriteParagraph($"... *and {truncatedCount} more types*");

            Console.WriteLine(writer.ToString().TrimEnd());
        }
    }

    // ===== Method Source Resolution =====

    internal static async Task<MethodSourceContext?> ResolveMethodSourceAsync(
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
                    ? PackageExtractor.ParsePackageReference(options.PackagePath)
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
            int sigStart = startLine;
            for (int i = startLine - 2; i >= Math.Max(0, startLine - 15); i--)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith("///") || trimmed.StartsWith("//")
                    || trimmed.StartsWith("[") || trimmed.StartsWith("#"))
                    continue;
                if (trimmed == "{")
                    continue;
                if (trimmed.StartsWith("}"))
                {
                    sigStart = i + 2;
                    break;
                }

                sigStart = i + 1;
                if (trimmed.StartsWith("public") || trimmed.StartsWith("private")
                    || trimmed.StartsWith("protected") || trimmed.StartsWith("internal")
                    || trimmed.StartsWith("static") || trimmed.Contains(methodName))
                    break;
            }

            int from = sigStart - 1;
            int to = endLine;

            // Scan forward to include the closing brace
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

            while (from < to && lines[from].TrimStart().Length == 0)
                from++;

            var methodLines = lines[from..to];

            int minIndent = methodLines
                .Where(l => l.TrimStart().Length > 0)
                .Select(l => l.Length - l.TrimStart().Length)
                .DefaultIfEmpty(0)
                .Min();

            var dedented = methodLines.Select(l => l.Length >= minIndent ? l[minIndent..] : l);
            var sourceCode = string.Join('\n', dedented).TrimEnd();

            return new MethodSourceContext(sourceCode, methodInfo.SourceUrl);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Failed to resolve method source for {typeName}.{methodName}: {ex.Message}");
            return null;
        }
    }

    // ===== Single Type Rendering =====

    internal static void WriteTypeOutput(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options)
    {
        if (options is TypeOptions { ShapeOutput: true })
        {
            ApiOutputFormatter.WriteShapeOutput(type, foundIn, packageName, packageVersion, options.MemberFilter, options.KindFilter);
            return;
        }

        if (options.JsonOutput)
        {
            WriteJsonTypeOutput(type, options);
            return;
        }

        var view = ApiOutputFormatter.BuildTypeView(type, foundIn, packageName, packageVersion, apiSource, selectedTfm, options);

        // Populate enum values declaratively for Normal+ enums
        if (type.Kind == "enum" && options.Verbosity >= Verbosity.Normal)
            ApiOutputFormatter.PopulateEnumValues(view, type, options);

        bool isMember = options is MemberOptions;
        bool fullSerializer = isMember || options.Verbosity != Verbosity.Quiet;

        int truncatedCount = 0;
        string truncatedNoun = "";

        if (fullSerializer && view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (options is MemberOptions { CtorOnly: true } && options.Verbosity >= Verbosity.Normal
                && type.Members.Any(m => m.Kind == "constructor"))
            {
                ApiOutputFormatter.PopulateConstructorOverloads(view, type, options);
            }
            else if (isMember && options.Verbosity == Verbosity.Quiet)
            {
                (truncatedCount, truncatedNoun) = ApiOutputFormatter.PopulateMemberSummarySections(view, type, options);
            }
            else if (options.Verbosity == Verbosity.Minimal && !isMember)
            {
                (truncatedCount, truncatedNoun) = ApiOutputFormatter.PopulateMemberSummarySections(view, type, options);
            }
            else
            {
                (truncatedCount, truncatedNoun) = ApiOutputFormatter.PopulateMemberSections(view, type, options);
            }

            // --index: populate code sections and custom attributes
            if (options is MemberOptions { OverloadIndex: not null, DllPath: not null } mo4)
            {
                var methods = type.Members
                    .Where(m => m.Kind is "method" or "constructor" && !m.IsAbstract)
                    .ToList();
                if (methods.Count > 0)
                    ApiOutputFormatter.PopulateIndexSections(view, type, methods, mo4.DllPath, mo4.OverloadIndex.Value - 1);
            }

            // Source code (already resolved in command layer)
            if (options is MemberOptions { MethodSource: not null } mo5)
            {
                view.MemberCode ??= new MemberCodeView();
                view.MemberCode.SourceCode = new Markout.CodeSection("csharp", mo5.MethodSource.SourceCode);
            }
        }

        if (options.OneLine)
        {
            var (oneLineView, _) = ApiOutputFormatter.BuildTypeOneLineView(type, options);
            var writerOpts = new MarkoutWriterOptions
            {
                Projection = OutputFormatter.BuildProjection(options.Select, options.Columns, options.Fields)
            };
            new MarkoutContext().Serialize(oneLineView, Console.Out, new Markout.OneLineFormatter(showHeader: !options.NoHeader), writerOpts);
        }
        else
        {
            var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
            var writer = new Markout.MarkoutWriter(new Markout.MarkdownFormatter(), writerOptions);
            new MarkoutContext().Serialize(view, writer);

            if (view.MemberCode != null)
                new MarkoutContext().Serialize(view.MemberCode, writer);

            if (truncatedCount > 0)
                writer.WriteParagraph($"... *and {truncatedCount} more {truncatedNoun}*");

            Console.WriteLine(writer.ToString().TrimEnd());
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

    // ===== Parameter Type Matching Helpers =====

    internal static List<string> ExtractParameterTypes(string signature)
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

    static string ExtractTypeFromParam(ReadOnlySpan<char> param)
    {
        var s = param.ToString();
        foreach (var mod in (ReadOnlySpan<string>)["ref ", "out ", "in ", "params ", "this "])
        {
            if (s.StartsWith(mod))
            {
                s = s[mod.Length..];
                break;
            }
        }

        int eqIdx = s.IndexOf(" = ");
        if (eqIdx > 0)
            s = s[..eqIdx];

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

    internal static bool SimpleNameMatches(string fullTypeName, string simpleName)
    {
        if (string.Equals(fullTypeName, simpleName, StringComparison.OrdinalIgnoreCase))
            return true;

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

    internal static bool MatchesParamTypes(List<string> extractedTypes, string[] requestedTypes)
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
