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
            FormatExplicitlySet = options.FormatExplicitlySet,
            NoHeader = options.NoHeader, Limit = options.Limit, MemberFilter = options.MemberFilter,
            KindFilter = options.KindFilter, UnsafeOnly = options.UnsafeOnly,
            IncludeSections = options.IncludeSections,
            Select = options.Select, Columns = options.Columns, Fields = options.Fields,
            Schema = options.Schema, Count = options.Count, SourceOptions = options.SourceOptions,
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
        var typePipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var memberPipeline = ApiMemberSectionDescriptors.CreatePipeline();
        bool hasTypeName = !string.IsNullOrWhiteSpace(options.TypeName);
        bool typeNameIsGlob = hasTypeName && (options.TypeName!.Contains('*') || options.TypeName!.Contains('?'));
        bool singleTypeMode = options is MemberOptions || (hasTypeName && !typeNameIsGlob);
        var knownSections = singleTypeMode ? memberPipeline.AllSectionNames : typePipeline.AllSectionNames;

        // Discovery mode: -D/--discover lists effective sections (resolves source) by
        // default; --schema opts out to the cheap, offline static schema listing.
        if (options.Discover != null && !options.EffectiveDiscovery)
        {
            var schema = singleTypeMode
                ? ApiViewContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema()
                : ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();

            // Restrict plain discovery to columns/sections queryable under the active options.
            if (singleTypeMode)
                schema = ToQueryableSchema(schema, options);

            return (null!, DiscoverOutput.Execute(options.Discover, schema,
                tree: options.Tree, json: options.JsonOutput, markdown: !options.OneLine && !options.JsonOutput));
        }

        // -S/--select with values: resolve as section filter for backpressure
        var selectResult = SelectResolver.ResolveSelectAsSections(options.Select, knownSections);
        if (SelectOutput.WriteUnresolved(selectResult))
            return (null!, 1);
        if (selectResult.Sections != null)
            options = options with { IncludeSections = selectResult.Sections };

        if (options.Count && !CountOutput.ValidateSingleSection(options.IncludeSections))
            return (null!, 1);

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
        if (!options.Count)
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

    internal static void ApplySurfaceFilters(ApiSurface api, ApiOptions options, string? typeFilter = null)
    {
        if (!string.IsNullOrEmpty(typeFilter))
        {
            api.Types = api.Types
                .Where(t => TypeMatcher.MatchesTypeFilter(t.FullName, typeFilter))
                .ToList();
            api.PublicTypeCount = api.Types.Count;
        }

        if (options.KindFilter.Count > 0)
        {
            api.Types = api.Types.Where(t => options.KindFilter.Contains(t.Kind)).ToList();
            api.PublicTypeCount = api.Types.Count;
        }

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
    }

    /// <summary>
    /// Writes a stderr note when sections explicitly requested via -S matched the schema
    /// but produced no data for this type (e.g. the enum-only "Values" section on a class).
    /// This distinguishes "valid but empty" from a typo (which yields a "not found" error)
    /// and from a silent empty render. Only meaningful for section-rendering output, so the
    /// caller must skip JSON (ignores -S), shape, and one-line output.
    /// </summary>
    internal static void WarnEmptySelectedSections(ApiType type, ApiOptions options, SectionPipeline<ApiType> pipeline)
    {
        if (options.IncludeSections is not { Count: > 0 })
            return;

        var filtered = BuildFilteredTypeForSections(type, options);
        var (empty, _) = pipeline.GetEmptySections(filtered, options.Verbosity, options.IncludeSections);
        if (empty.Count == 0)
            return;

        bool filtersActive = options.MemberFilter.Count > 0 || options.KindFilter.Count > 0
            || options.UnsafeOnly || options.Limit.HasValue;
        var suffix = filtersActive ? " after filters" : "";

        if (empty.Count == 1)
            Console.Error.WriteLine($"Note: section '{empty[0]}' has no data for {type.FullName}{suffix}.");
        else
            Console.Error.WriteLine($"Note: {empty.Count} sections have no data for {type.FullName}{suffix}: {string.Join(", ", empty)}.");
    }

    internal static ApiType BuildFilteredTypeForSections(ApiType type, ApiOptions options)
    {
        var members = type.Members.Where(m => !MemberFilters.IsCompilerGenerated(m.Name));

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter));

        if (options.UnsafeOnly)
            members = members.Where(m => m.IsUnsafe);

        if (options.KindFilter.Count > 0)
            members = members.Where(m => options.KindFilter.Contains(m.Kind));

        var filteredMembers = members.ToList();
        if (options.Limit.HasValue && options.Limit.Value < filteredMembers.Count)
            filteredMembers = filteredMembers.Take(options.Limit.Value).ToList();

        return new ApiType
        {
            Namespace = type.Namespace,
            Name = type.Name,
            Kind = type.Kind,
            IsSealed = type.IsSealed,
            IsAbstract = type.IsAbstract,
            IsStatic = type.IsStatic,
            BaseType = type.BaseType,
            Interfaces = type.Interfaces,
            DerivedTypes = type.DerivedTypes,
            TypeParameters = type.TypeParameters,
            Members = filteredMembers,
            SourceFilePath = type.SourceFilePath,
            SourceUrl = type.SourceUrl,
            GitHubBrowseUrl = type.GitHubBrowseUrl,
            SourceLineNumber = type.SourceLineNumber,
            SourceResolution = type.SourceResolution,
            AdditionalSourceFiles = type.AdditionalSourceFiles,
            IsForwarded = type.IsForwarded,
            Documentation = type.Documentation
        };
    }

    // ===== Full API Surface Rendering =====

    internal static void WriteFullApiOutput(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        ApplySurfaceFilters(api, options, (options as TypeOptions)?.TypeFilter);

        if (options.JsonOutput && !options.Count)
        {
            Console.WriteLine(JsonSerializer.Serialize(api, ApiJsonContext.Default.ApiSurface));
            return;
        }

        var (view, _) = ApiOutputFormatter.BuildFullApiView(api, options);

        if (options.Count)
        {
            var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
            var markdown = MarkoutSerializer.Serialize(view, ApiViewContext.Default, writerOptions);
            markdown = MarkdownTableRowLimiter.Apply(markdown, options.Rows);
            CountOutput.WriteCountFromMarkdown(markdown);
        }
        else if (options.OneLine)
        {
            var (oneLineView, _) = ApiOutputFormatter.BuildSurfaceOneLineView(api, options);
            var writerOpts = new MarkoutWriterOptions
            {
                Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields)
            };
            MarkoutSerializer.Serialize(oneLineView, Console.Out, new Markout.OneLineFormatter(showHeader: !options.NoHeader), ApiViewContext.Default, writerOpts);
        }
        else
        {
            var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
            if (options.PlainText)
            {
                MarkoutSerializer.Serialize(view, Console.Out, options.CreateFormatter(), ApiViewContext.Default, writerOptions);
            }
            else
            {
                var markdown = MarkoutSerializer.Serialize(view, ApiViewContext.Default, writerOptions).TrimEnd();
                Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, options.Rows));
            }
        }
    }

    // ===== Method Source Resolution =====

    internal sealed record ResolvedMethodSource(MethodSourceContext? Source, string? PdbPath);

    internal static async Task<ResolvedMethodSource> ResolveMethodSourceAsync(
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

            // Capture the acquired portable PDB path now so the decompiler can reuse it for local
            // names even when SourceLink/source resolution below fails (PDB available, source not).
            string? pdbPath = context.PortablePdbPath;

            if (!service.HasPdb || !service.HasSourceLink)
                return new ResolvedMethodSource(null, pdbPath);

            var methodInfo = service.ResolveMethodSource(typeName, methodName, overloadIndex, publicOnly: true);
            if (methodInfo?.SourceUrl == null)
                return new ResolvedMethodSource(null, pdbPath);

            var fetcher = new SourceFetcher(httpClient);
            var content = await fetcher.FetchSourceAsync(methodInfo.SourceUrl);
            if (content == null)
                return new ResolvedMethodSource(null, pdbPath);

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

            return new ResolvedMethodSource(new MethodSourceContext(sourceCode, methodInfo.SourceUrl), pdbPath);
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Failed to resolve method source for {typeName}.{methodName}: {ex.Message}");
            return new ResolvedMethodSource(null, null);
        }
    }

    // ===== Single Type Rendering =====

    internal static void WriteTypeOutput(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options, TextWriter? output = null)
    {
        var sink = output ?? Console.Out;

        if (options is TypeOptions { ShapeOutput: true } && !options.Count)
        {
            ApiOutputFormatter.WriteShapeOutput(type, foundIn, packageName, packageVersion, options.MemberFilter, options.KindFilter);
            return;
        }

        if (options.JsonOutput && !options.Count)
        {
            WriteJsonTypeOutput(type, options);
            return;
        }

        var view = ApiOutputFormatter.BuildTypeView(type, foundIn, packageName, packageVersion, apiSource, selectedTfm, options);

        // Populate enum values declaratively (pipeline controls visibility via IncludeSections)
        if (type.Kind == "enum")
            ApiOutputFormatter.PopulateEnumValues(view, type, options);

        bool isMember = options is MemberOptions;
        bool fullSerializer = options.Verbosity != Verbosity.Quiet;

        if (fullSerializer && view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (options is MemberOptions { CtorOnly: true } && options.Verbosity >= Verbosity.Normal
                && type.Members.Any(m => m.Kind == "constructor"))
            {
                ApiOutputFormatter.PopulateConstructorOverloads(view, type, options);
            }
            else if (options.Verbosity == Verbosity.Minimal && !isMember)
            {
                ApiOutputFormatter.PopulateMemberSummarySections(view, type, options);
            }
            else
            {
                ApiOutputFormatter.PopulateMemberSections(view, type, options);
            }

            // --index: populate code sections and custom attributes
            if (options is MemberOptions { OverloadIndex: not null, DllPath: not null } mo4)
            {
                var methods = type.Members
                    .Where(m => m.Kind is "method" or "constructor" && !m.IsAbstract)
                    .ToList();
                if (methods.Count > 0)
                    ApiOutputFormatter.PopulateIndexSections(view, type, methods, mo4.DllPath, mo4.OverloadIndex.Value - 1, mo4.PdbPath);
            }

            // Source code (already resolved in command layer)
            if (options is MemberOptions { MethodSource: not null } mo5)
            {
                view.MemberCode ??= new MemberCodeView();
                view.MemberCode.SourceCode = new Markout.CodeSection("csharp", mo5.MethodSource.SourceCode);
            }
        }

        if (options.Count)
        {
            var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
            var sw = new StringWriter();
            var writer = new Markout.MarkoutWriter(sw, new MarkdownFormatter(), writerOptions);
            ApiViewContext.Default.Serialize(view, writer);

            if (view.MemberCode != null)
                ApiViewContext.Default.Serialize(view.MemberCode, writer);

            writer.Flush();
            CountOutput.WriteCountFromMarkdown(MarkdownTableRowLimiter.Apply(sw.ToString(), options.Rows));
        }
        else if (options.OneLine)
        {
            var (oneLineView, _) = ApiOutputFormatter.BuildTypeOneLineView(type, options);
            var writerOpts = new MarkoutWriterOptions
            {
                Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields)
            };
            MarkoutSerializer.Serialize(oneLineView, sink, new Markout.OneLineFormatter(showHeader: !options.NoHeader), ApiViewContext.Default, writerOpts);
        }
        else
        {
            var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
            if (options.PlainText)
            {
                var writer = new Markout.MarkoutWriter(sink, options.CreateFormatter(), writerOptions);
                ApiViewContext.Default.Serialize(view, writer);

                if (view.MemberCode != null)
                    ApiViewContext.Default.Serialize(view.MemberCode, writer);

                writer.Flush();
            }
            else
            {
                var sw = new StringWriter();
                var writer = new Markout.MarkoutWriter(sw, new MarkdownFormatter(), writerOptions);
                ApiViewContext.Default.Serialize(view, writer);

                if (view.MemberCode != null)
                    ApiViewContext.Default.Serialize(view.MemberCode, writer);

                writer.Flush();
                sink.WriteLine(MarkdownTableRowLimiter.Apply(sw.ToString().TrimEnd(), options.Rows));
            }
        }
    }

    /// <summary>
    /// Restricts a plain-discovery schema to the columns queryable under the active options.
    /// The view schema is a union of all rendering variants, so it advertises columns that only
    /// specific options surface. When the enabling option is off, the column is not queryable
    /// (e.g. <c>--columns Select</c> does nothing), so it is hidden from discovery to keep what
    /// is listed consistent with what the user can actually project. This is the option/contract
    /// level gate (data-independent); the data-level gate is effective discovery.
    /// </summary>
    /// <remarks>
    /// Currently the only option-gated column is the <c>Select</c> overload-index column, which
    /// is surfaced only by <c>member --show-index</c>. Add future gated columns here.
    /// </remarks>
    internal static DocumentSchema ToQueryableSchema(DocumentSchema schema, ApiOptions options)
    {
        if (options is not MemberOptions { ShowSelect: true })
            schema = DiscoverOutput.WithoutColumn(schema, "Select");
        return schema;
    }

    /// <summary>
    /// Executes effective discovery (<c>-D</c>) for a single type. Shared by the
    /// type and member commands so both paths apply identical queryability filtering:
    /// <list type="bullet">
    /// <item>Section gate: <see cref="DiscoverOutput.RestrictToSchemaSections"/> drops pipeline
    /// sections absent from the type schema (the member-detail code sections Source/IL/...),
    /// then <see cref="DiscoverOutput.RestrictToRenderedSections"/> drops schema sections that
    /// render no data for this type (e.g. Custom Attributes when the type has no attributes),
    /// so every listed section is queryable via <c>-D &lt;Section&gt;</c> and actually has data.</item>
    /// <item>Column gate: <see cref="DiscoverOutput.FilterSchemaToRenderedHeaders"/> renders the
    /// type at the active options and keeps only columns that appear, dropping columns the
    /// active options never surface (e.g. Select without --show-index) and columns with no data
    /// (e.g. Obsolete when no member is obsolete).</item>
    /// </list>
    /// This keeps effective discovery consistent with what the user can actually query and see.
    /// </summary>
    internal static int ExecuteEffectiveDiscovery(
        ApiType apiType, SectionPipeline<ApiType> memberPipeline, ApiOptions options)
    {
        var fullSchema = ApiViewContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema();
        var filteredType = BuildFilteredTypeForSections(apiType, options);
        var effective = memberPipeline.GetEffectiveSections(filteredType, Verbosity.Detailed, options.IncludeSections);
        effective = DiscoverOutput.RestrictToSchemaSections(effective, fullSchema);
        var rendered = RenderTypeSectionsMarkdown(filteredType, options);
        effective = DiscoverOutput.RestrictToRenderedSections(effective, fullSchema, rendered);
        var schema = DiscoverOutput.FilterSchemaToRenderedHeaders(effective, fullSchema, rendered);
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, schema,
            tree: options.Tree, json: options.JsonOutput, markdown: !options.OneLine && !options.JsonOutput,
            verbosity: (int)options.Verbosity, fullSchema: fullSchema);
    }

    /// <summary>
    /// Renders the type's member/enum sections to a markdown string for effective-column
    /// discovery. Replicates <see cref="WriteTypeOutput"/>'s verbosity branching so the
    /// rendered table headers reflect exactly which columns the user would actually see
    /// (e.g. summary columns at Minimal, full member columns at Detailed, the Select column
    /// only with --show-index). Projection (--columns/--fields) is intentionally dropped so
    /// the result reflects all renderable columns, not a user-narrowed subset.
    /// </summary>
    internal static string RenderTypeSectionsMarkdown(ApiType type, ApiOptions options)
    {
        var renderOptions = options with
        {
            Columns = null,
            Fields = null,
            PlainText = false,
            JsonOutput = false,
            OneLine = false,
        };

        var view = ApiOutputFormatter.BuildTypeView(type, null, null, null, null, null, renderOptions);

        if (type.Kind == "enum")
            ApiOutputFormatter.PopulateEnumValues(view, type, renderOptions);

        bool isMember = renderOptions is MemberOptions;
        if (view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (renderOptions.Verbosity == Verbosity.Minimal && !isMember)
                ApiOutputFormatter.PopulateMemberSummarySections(view, type, renderOptions);
            else
                ApiOutputFormatter.PopulateMemberSections(view, type, renderOptions);
        }

        var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, renderOptions);
        var sw = new StringWriter();
        var writer = new MarkoutWriter(sw, new Markout.MarkdownFormatter(), writerOptions);
        ApiViewContext.Default.Serialize(view, writer);
        writer.Flush();
        return sw.ToString();
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

        // -S/--select scopes JSON to the requested sections, mirroring the markdown view.
        if (options.IncludeSections is { Count: > 0 } sections)
        {
            outputType = ProjectTypeToSections(type, members, sections);
        }
        else if (members != type.Members)
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
    /// Maps each member section name to the predicate that selects its members.
    /// </summary>
    private static readonly Dictionary<string, Func<ApiMember, bool>> MemberSectionPredicates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SectionNames.Values] = m => m.Kind == "field" && m.EnumValue.HasValue,
            [SectionNames.Fields] = m => m.Kind == "field" && !m.EnumValue.HasValue,
            [SectionNames.Properties] = m => m.Kind == "property",
            [SectionNames.Methods] = m => m.Kind == "method",
            [SectionNames.Constructors] = m => m.Kind == "constructor",
            [SectionNames.Events] = m => m.Kind == "event",
        };

    /// <summary>
    /// Builds a copy of <paramref name="type"/> scoped to the requested sections: members are
    /// restricted to the selected member sections, and the Baseclass / Interfaces / Type
    /// Parameters facets are retained only when their section is selected. Identity fields
    /// (namespace, name, kind) are always preserved.
    /// </summary>
    private static ApiType ProjectTypeToSections(ApiType type, IEnumerable<ApiMember> members, HashSet<string> sections)
    {
        var predicates = MemberSectionPredicates
            .Where(kv => sections.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();

        var scopedMembers = predicates.Count > 0
            ? members.Where(m => predicates.Any(p => p(m))).ToList()
            : [];

        return new ApiType
        {
            Namespace = type.Namespace,
            Name = type.Name,
            Kind = type.Kind,
            IsSealed = type.IsSealed,
            IsAbstract = type.IsAbstract,
            IsStatic = type.IsStatic,
            BaseType = sections.Contains(SectionNames.Baseclass) && IsRenderableBaseType(type.BaseType) ? type.BaseType : null,
            Interfaces = sections.Contains(SectionNames.TypeInterfaces) ? type.Interfaces : [],
            TypeParameters = sections.Contains(SectionNames.TypeParameters) ? type.TypeParameters : [],
            Members = scopedMembers,
            SourceFilePath = type.SourceFilePath,
            SourceUrl = type.SourceUrl,
            GitHubBrowseUrl = type.GitHubBrowseUrl,
            SourceLineNumber = type.SourceLineNumber,
            Documentation = type.Documentation
        };
    }

    /// <summary>
    /// Mirrors the Baseclass section's CanRender: a base type is meaningful only when it is
    /// present and not one of the implicit roots (Object/ValueType/Enum).
    /// </summary>
    private static bool IsRenderableBaseType(string? baseType)
        => !string.IsNullOrEmpty(baseType)
           && baseType is not ("System.Object" or "System.ValueType" or "System.Enum");



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
