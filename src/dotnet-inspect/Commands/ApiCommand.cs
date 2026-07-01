using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Net;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using Markout;
using DotnetInspector.Services;
using DotnetInspector.Views;

using Decompiler = ILInspector.Decompiler;

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
            OneLine = options.OneLine, Tsv = options.Tsv, Jsonl = options.Jsonl,
            OneLineExplicitlySet = options.OneLineExplicitlySet,
            FormatExplicitlySet = options.FormatExplicitlySet,
            NoHeader = options.NoHeader, Limit = options.Limit, MemberFilter = options.MemberFilter,
            KindFilter = options.KindFilter, UnsafeOnly = options.UnsafeOnly,
            IncludeSections = options.IncludeSections,
            Print = options.Print, PrintAll = options.PrintAll, PrintRow = options.PrintRow,
            Value = options.Value, Urls = options.Urls, Paths = options.Paths,
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
        var memberPipeline = ApiMemberSectionPipelines.Create(options);
        bool hasTypeName = !string.IsNullOrWhiteSpace(options.TypeName);
        bool typeNameIsGlob = hasTypeName && (options.TypeName!.Contains('*') || options.TypeName!.Contains('?'));
        bool singleTypeMode = options is MemberOptions || (hasTypeName && !typeNameIsGlob);
        var knownSections = singleTypeMode ? memberPipeline.SelectableSectionNames : typePipeline.SelectableSectionNames;

        // Discovery mode: -D/--discover lists effective sections (resolves source) by
        // default; --schema opts out to the cheap, offline static schema listing.
        if (options.Discover != null && !options.EffectiveDiscovery)
        {
            var schema = singleTypeMode
                ? GetTypeDocumentSchema(options)
                : ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();

            // Restrict plain discovery to columns/sections queryable under the active options.
            if (singleTypeMode)
            {
                schema = RestrictSchemaToSections(schema, knownSections);
                schema = ToQueryableSchema(schema, options);
            }

            return (null!, DiscoverOutput.Execute(options.Discover, schema,
                tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.OneLine && !options.JsonOutput,
                sectionCostAnnotations: singleTypeMode ? memberPipeline.GetCostAnnotations() : null,
                sectionCategories: singleTypeMode ? memberPipeline.GetCategoryMap() : typePipeline.GetCategoryMap()));
        }

        // -S/--select with values: resolve as section filter for backpressure
        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select,
            knownSections,
            singleTypeMode ? memberPipeline.InfoSectionNames : typePipeline.InfoSectionNames,
            singleTypeMode ? memberPipeline.GetCategoryMap() : typePipeline.GetCategoryMap());
        if (SelectOutput.WriteUnresolved(selectResult))
            return (null!, 1);
        if (selectResult.Sections != null)
            options = options with { IncludeSections = selectResult.Sections };

        if (options.Count && !CountOutput.ValidateSingleSection(options.IncludeSections))
            return (null!, 1);

        var shapeCount = ShapeProjectionOutput.ActiveShapeCount(options.Value, options.Urls, options.Paths);
        if (shapeCount > 1)
        {
            Console.Error.WriteLine("Error: specify only one of --value, --urls, or --paths.");
            return (null!, 1);
        }

        if (shapeCount == 1)
        {
            var optionName = options.Value ? "--value" : options.Urls ? "--urls" : "--paths";
            if (!ShapeProjectionOutput.ValidateSingleSection(options.IncludeSections, optionName))
                return (null!, 1);
            if (options.Count || options.Print || options.PrintAll)
            {
                Console.Error.WriteLine($"Error: {optionName} cannot be combined with --count, --print, or --print-all.");
                return (null!, 1);
            }
            if (options.Rows is not null)
            {
                Console.Error.WriteLine($"Error: --rows cannot be combined with {optionName}; use -n N to limit projected output lines or --row N to select a projected row.");
                return (null!, 1);
            }
        }

        if (options.JsonArray && shapeCount == 0 && !options.Print && !options.PrintAll)
        {
            Console.Error.WriteLine("Error: --json-array requires --value, --urls, --paths, --print, or --print-all.");
            return (null!, 1);
        }

        if (options.JsonArray && (options.JsonOutput || options.Jsonl))
        {
            Console.Error.WriteLine("Error: --json-array cannot be combined with --json or --jsonl.");
            return (null!, 1);
        }

        if ((options.Print || options.PrintAll) && !ValidateApiPrintSelection(options.IncludeSections))
            return (null!, 1);

        if ((options.Print || options.PrintAll) && options.Rows is not null)
        {
            Console.Error.WriteLine("Error: --rows cannot be combined with --print or --print-all; use --row N to choose a printed row.");
            return (null!, 1);
        }

        if (options.PrintRow is not null && !options.Print && shapeCount == 0)
        {
            Console.Error.WriteLine("Error: --row requires --print, --value, --urls, or --paths.");
            return (null!, 1);
        }

        if (options.Print && options.PrintAll)
        {
            Console.Error.WriteLine("Error: --print cannot be combined with --print-all.");
            return (null!, 1);
        }

        if (!OutputFormatResolver.ValidateSingleSectionForTabular(options.OneLineExplicitlySet, options.IncludeSections))
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

        // Warn if tabular output is combined with detailed verbosity without section selector
        if (!options.Count)
            OutputFormatResolver.WarnIfOneLineDetailMismatch(options.OneLine, options.Verbosity, options.IncludeSections);

        return (new PreambleResult(options, typePipeline, memberPipeline), null);
    }

    private static bool ValidateApiPrintSelection(HashSet<string>? includeSections)
    {
        if (includeSections is { Count: 1 })
            return true;

        Console.Error.WriteLine("Error: --print/--print-all requires -S/--select to match exactly one printable section.");
        return false;
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
            api.PublicMethodCount = api.Types.Sum(t => t.Members.Count(ApiMemberSectionDescriptors.IsMethodLike));
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
    /// caller must skip JSON (ignores -S), shape, and tabular output.
    /// </summary>
    internal static void WarnEmptySelectedSections(ApiType type, ApiOptions options, SectionPipeline<ApiType> pipeline)
    {
        if (options.IncludeSections is not { Count: > 0 })
            return;
        if (SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections)
            || SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections))
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

    internal static DocumentSchema GetTypeDocumentSchema(ApiOptions options)
    {
        var schema = MergeSchemas(
            ApiViewContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<MethodGroupsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<MethodsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<MemberIndexView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<OperatorsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<ExplicitInterfaceImplementationsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<ExtensionMethodsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<EventsView>()!.ToDocumentSchema());
        // MemberCodeView owns source/IL/fact/call-graph sections. Type discovery also needs
        // those schema entries because the type pipeline exposes whole-type code sections.
        var detailSchema = MergeSchemas(schema,
            ApiViewContext.Default.GetSchemaInfo<MemberCodeView>()!.ToDocumentSchema());
        if (!ApiMemberSectionPipelines.UsesDetailPipeline(options))
            return detailSchema;
        if (detailSchema.GetSection(SectionNames.Calls) == null)
            detailSchema.Add(SectionNames.Calls, "column", "Callee", "Kind", "IL", "Token");
        if (detailSchema.GetSection(SectionNames.Callers) == null)
            detailSchema.Add(SectionNames.Callers, "column", "Caller", "Kind", "IL", "Token");
        if (detailSchema.GetSection(SectionNames.UnsafeOperations) == null)
            detailSchema.Add(SectionNames.UnsafeOperations, "column", "Reason", "Detail", "Kind", "IL", "Token");
        detailSchema.Add(SectionNames.CallGraph, "field",
            "Fanout", "FanoutCount",
            "Fanin", "FaninCount",
            "Depth", "MaxDepth",
            "Loop", "InLoop", "Looping",
            "Alloc", "Allocations",
            "Copy", "Copies",
            "Unsafe",
            "Reflection",
            "Throw", "Throws", "ThrowSites",
            "Exceptions", "ExceptionTypes", "ConstructedExceptions",
            "Catch", "Catches",
            "Finally", "Finallys",
            "EvidenceIL", "Evidence", "IL");
        detailSchema.Add(SectionNames.CallerGraph, "field",
            "Fanin", "FaninCount",
            "Depth", "MaxDepth",
            "Loop", "InLoop", "Looping",
            "Root", "RootKind", "Classification",
            "Source", "Assembly",
            "Alloc", "Allocations",
            "Copy", "Copies",
            "Unsafe",
            "Reflection",
            "Throw", "Throws", "ThrowSites",
            "Exceptions", "ExceptionTypes", "ConstructedExceptions",
            "Catch", "Catches",
            "Finally", "Finallys",
            "EvidenceIL", "Evidence", "IL");
        return detailSchema;
    }

    private static DocumentSchema MergeSchemas(params DocumentSchema[] schemas)
    {
        var merged = new DocumentSchema();
        foreach (var schema in schemas)
        {
            foreach (var name in schema.SectionNames)
            {
                var section = schema.GetSection(name);
                if (section == null)
                {
                    merged.AddSection(name);
                    continue;
                }

                var items = section.Items.Select(i => i.Name).ToArray();
                if (items.Length > 0)
                    merged.Add(name, section.ItemKind, items);
                else
                    merged.AddSection(name);
            }
        }

        return merged;
    }

    private static DocumentSchema RestrictSchemaToSections(DocumentSchema schema, IReadOnlyCollection<string> sectionNames)
    {
        var filtered = new DocumentSchema();
        foreach (var name in sectionNames)
        {
            var section = schema.GetSection(name);
            if (section == null)
                continue;

            var items = section.Items.Select(i => i.Name).ToArray();
            if (items.Length > 0)
                filtered.Add(name, section.ItemKind, items);
            else
                filtered.AddSection(name);
        }

        return filtered;
    }

    /// <summary>
    /// Acquires the portable PDB for an assembly (symbol server / symbol
    /// package) and returns its on-disk path, so the decompiler can render
    /// real local-variable names instead of V_n slots. Best-effort: returns
    /// null when no PDB can be obtained (offline, Windows PDB, no symbols).
    /// </summary>
    internal static async Task<string?> TryAcquirePdbPathAsync(
        string dllPath, ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        try
        {
            using var service = SourceLinkService.Open(dllPath, logger.Log);
            var context = service.Context;
            if (context.NeedsPdb)
            {
                var (pkgName, pkgVersion) = !string.IsNullOrEmpty(options.PackagePath)
                    ? PackageExtractor.ParsePackageReference(options.PackagePath)
                    : (null, null);
                await SourceEnricher.AcquirePdbAsync(context, httpClient, pkgName, pkgVersion,
                    isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log);
            }
            return context.PortablePdbPath;
        }
        catch
        {
            return null;
        }
    }

    internal static HashSet<string> GetRequestedMemberSections(ApiType type, ApiOptions options)
    {
        var pipeline = ApiMemberSectionPipelines.Create(options);
        var sections = new HashSet<string>(
            pipeline.GetEffectiveSections(type, options.Verbosity, options.IncludeSections),
            StringComparer.OrdinalIgnoreCase);
        if (options.Discover is { Length: > 0 } discover)
        {
            var resolved = SelectResolver.ResolveSelectAsSections(
                discover, pipeline.SelectableSectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap());
            if (!resolved.HasError && resolved.Sections is { Count: > 0 })
                sections.UnionWith(resolved.Sections);
        }
        return sections;
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
            markdown = OutputFormatter.ApplyRowLimit(markdown, options.Rows);
            CountOutput.WriteCountFromMarkdown(markdown);
        }
        else if (options.OneLine)
        {
            var (oneLineView, _) = ApiOutputFormatter.BuildSurfaceOneLineView(api, options);
            var rendered = OutputFormatter.RenderProjectedTable(!options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(oneLineView, writer, formatter, ApiViewContext.Default, writerOptions));
            ProjectionDiagnostics.DiagnoseRendered(options.Fields ?? options.Columns, rendered);
            Console.Out.Write(OutputFormatter.LimitRenderedTableRows(rendered, options.Rows, !options.NoHeader));
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
                OutputFormatter.WriteLimitedMarkdown(Console.Out,
                    MarkoutSerializer.Serialize(view, ApiViewContext.Default, writerOptions), options.Rows);
            }
        }
    }

    // ===== Method Source Resolution =====

    /// <summary>
    /// Forwarder-following policy for whole-type listings: implementations
    /// beside the starting assembly first, then the installed shared
    /// framework (so package facades resolve to the platform assemblies
    /// they forward to at runtime).
    /// </summary>
    internal static ILInspector.Metadata.AssemblyLocator PlatformAssemblyLocator(string startingDll)
    {
        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(startingDll)
        {
            IncludeDepsJsonAssets = false,
            IncludeAspNetCoreSharedFramework = false,
            PreferImplementationAssemblies = true,
        });
        return resolver.ToAssemblyLocator();
    }

    internal sealed record ResolvedMethodSource(MethodSourceContext? Source, string? PdbPath);

    internal static async Task<ResolvedMethodSource> ResolveMethodSourceAsync(
        string dllPath, string typeName, string methodName, int overloadIndex,
        ApiOptions options, HttpClient httpClient, VerboseLogger logger, bool fetchSource = true,
        bool publicOnly = true)
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

            if (!fetchSource || !service.HasPdb || !service.HasSourceLink)
                return new ResolvedMethodSource(null, pdbPath);

            var methodInfo = service.ResolveMethodSource(typeName, methodName, overloadIndex, publicOnly);
            if (methodInfo?.SourceUrl == null)
                return new ResolvedMethodSource(null, pdbPath);

            var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
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

    internal static async Task<int> WriteTypeOutputAsync(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options, TextWriter? output = null)
    {
        var sink = output ?? Console.Out;

        if (options is TypeOptions { ShapeOutput: true } && !options.Count)
        {
            ApiOutputFormatter.WriteShapeOutput(type, foundIn, packageName, packageVersion, options.MemberFilter, options.KindFilter, options.Verbosity);
            return 0;
        }

        if (options.JsonOutput && !options.Count)
        {
            WriteJsonTypeOutput(type, options);
            return 0;
        }

        var view = ApiOutputFormatter.BuildTypeView(type, foundIn, packageName, packageVersion, apiSource, selectedTfm, options);
        EventsView? eventsView = null;
        MethodGroupsView? methodGroupsView = null;
        MethodsView? methodsView = null;
        MemberIndexView? memberIndexView = null;
        OperatorsView? operatorsView = null;
        ExplicitInterfaceImplementationsView? explicitInterfaceImplementationsView = null;
        ExtensionMethodsView? extensionMethodsView = null;

        // Populate enum values declaratively (pipeline controls visibility via IncludeSections)
        if (type.Kind == "enum")
            ApiOutputFormatter.PopulateEnumValues(view, type, options);

        bool fullSerializer = options.Verbosity != Verbosity.Quiet;

        if (fullSerializer && view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (options is MemberOptions { OverloadIndex: not null })
            {
                ApiOutputFormatter.PopulateMemberSignature(view, type, options);
            }
            else if (options is MemberOptions { CtorOnly: true } && options.Verbosity >= Verbosity.Normal
                && type.Members.Any(m => m.Kind == "constructor"))
            {
                ApiOutputFormatter.PopulateConstructorOverloads(view, type, options);
            }
            else
            {
                var renderMemberGroups = ApiOutputFormatter.ShouldRenderMemberGroups(options);
                var renderMemberRows = ApiOutputFormatter.ShouldRenderMemberRows(options);
                var renderSupplementalRows = ApiOutputFormatter.ShouldRenderSupplementalMemberRows(options);
                if (renderMemberGroups)
                {
                    methodGroupsView ??= new MethodGroupsView();
                    eventsView ??= new EventsView();
                    ApiOutputFormatter.PopulateMemberSummarySections(
                        view, methodGroupsView, eventsView, type, options, methodGroupsOnly: renderMemberRows);
                }
                if (renderMemberRows || renderSupplementalRows)
                {
                    methodsView ??= new MethodsView();
                    operatorsView ??= new OperatorsView();
                    explicitInterfaceImplementationsView ??= new ExplicitInterfaceImplementationsView();
                    extensionMethodsView ??= new ExtensionMethodsView();
                    eventsView ??= new EventsView();
                    ApiOutputFormatter.PopulateMemberSections(
                        view,
                        methodsView,
                        operatorsView,
                        explicitInterfaceImplementationsView,
                        extensionMethodsView,
                        eventsView,
                        type,
                        options,
                        renderSupplementalRows ? ApiOutputFormatter.SupplementalMemberKinds : null);
                }
                if (ShouldRenderMemberIndex(options))
                {
                    memberIndexView ??= new MemberIndexView();
                    ApiOutputFormatter.PopulateMemberIndex(memberIndexView, type, options);
                }
            }

            if (ShouldRenderSourceLocations(options))
                ApiOutputFormatter.PopulateMemberSourceLocations(view, type, options);

            // --index: populate code sections and custom attributes
            // Can be called with a specific overload for all sections, or without an overload
            // for Callers-only mode (aggregates across all overloads).
            if (options is MemberOptions { DllPath: not null } mo4 
                && (mo4.OverloadIndex.HasValue || mo4.HasCallerScope))
            {
                var requestedSections = GetRequestedMemberSections(type, mo4);
                var methods = type.Members
                    .Where(m => m.Kind is "method" or "constructor" or "operator" or "explicit-interface-implementation" or "extension-method"
                        && (!m.IsAbstract || requestedSections.Contains(SectionNames.UnsafeOperations)))
                    .ToList();
                if (methods.Count > 0)
                    ApiOutputFormatter.PopulateIndexSections(view, type, methods, mo4.DllPath!,
                        mo4.OverloadIndex.HasValue ? mo4.OverloadIndex.Value - 1 : null,
                        requestedSections, mo4.PdbPath, mo4.IncludeSections,
                        mo4.CallerScopeAssemblies, mo4);
            }

            if (options.DllPath is { } unsafeDllPath
                && GetRequestedMemberSections(type, options).Contains(SectionNames.UnsafeMembers))
            {
                ApiOutputFormatter.PopulateUnsafeMembers(view, type, unsafeDllPath);
            }

            if (options.DllPath is { } optimizationDllPath
                && GetRequestedMemberSections(type, options).Contains(SectionNames.PerformanceTriage))
            {
                ApiOutputFormatter.PopulateOptimizationOpportunities(view, type, optimizationDllPath, options.IncludeSections,
                    options.PerformanceTriage,
                    restrictToModelMembers: ApiMemberSectionPipelines.UsesDetailPipeline(options)
                        || ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(options));
            }

            if (options.DllPath is { } leverageDllPath
                && GetRequestedMemberSections(type, options).Contains(SectionNames.TopLeverage))
            {
                ApiOutputFormatter.PopulateTopLeverage(view, type, leverageDllPath,
                    restrictToModelMembers: ApiMemberSectionPipelines.UsesDetailPipeline(options)
                        || ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(options));
            }

            // Source code (already resolved in command layer)
            if (options is MemberOptions { MethodSource: not null } mo5
                && GetRequestedMemberSections(type, mo5).Contains(SectionNames.OriginalSource))
            {
                view.MemberCode ??= new MemberCodeView();
                view.MemberCode.OriginalSourceCode = new Markout.CodeSection("csharp", mo5.MethodSource.SourceCode);
            }

        }

        // Whole-type decompilation (type command; member flows populate per
        // member above). Explicit-only: requires -S "Decompiled Source".
        // Sits OUTSIDE the member-sections region so enum types (which
        // populate EnumValues and skip that region) also compose.
        if (fullSerializer
            && options is not MemberOptions
            && options.DllPath is { } typeDllPath
            && options.IncludeSections is { Count: > 0 }
            && GetRequestedMemberSections(type, options).Contains(SectionNames.DecompiledSource))
        {
            var listing = Decompiler.TypeSourceComposer.Compose(
                type, typeDllPath, options.PdbPath, PlatformAssemblyLocator(typeDllPath));
            if (listing is not null)
            {
                view.MemberCode ??= new MemberCodeView();
                view.MemberCode.DecompiledSourceCode = new Markout.CodeSection("csharp", listing);
            }
        }

        if (options.Print || options.PrintAll)
            return await PrintApiProjectionAsync(view, options);

        if (options.Value || options.Urls || options.Paths)
            return WriteApiShapeProjection(view, options);

        if (options.Count)
        {
            var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
            var sw = new StringWriter();
            var writer = new Markout.MarkoutWriter(sw, new MarkdownFormatter(), writerOptions);
            ApiOutputFormatter.SerializeTypeDocument(
                view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, writer);
            writer.Flush();
            CountOutput.WriteCountFromMarkdown(OutputFormatter.ApplyRowLimit(sw.ToString(), options.Rows));
            return 0;
        }

        // --bare: only the selected payload — no heading, fence, separator, or tips.
        if (options.Bare)
        {
            if (!TryGetBareApiPayload(view, options, out var raw, out var error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }
            sink.WriteLine(raw.TrimEnd());
            return 0;
        }

        if (options.OneLine)
        {
            if (ApiOutputFormatter.ShouldRenderSectionedTabularView(type, options))
            {
                var writerOpts = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
                OutputFormatter.ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                OutputFormatter.WriteTable(sink, !options.NoHeader,
                    (writer, formatter) =>
                    {
                        var markoutWriter = new MarkoutWriter(writer, formatter, writerOpts);
                        ApiOutputFormatter.SerializeTypeDocument(
                            view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                            explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, markoutWriter);
                        markoutWriter.Flush();
                    }, options.Rows);
            }
            else
            {
                var (oneLineView, _) = ApiOutputFormatter.BuildTypeOneLineView(type, options);
                OutputFormatter.WriteProjectedTable(sink, !options.NoHeader, options.Tsv, options.Jsonl,
                    options.Columns, options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(oneLineView, writer, formatter, ApiViewContext.Default, writerOptions),
                    options.Rows);
            }
        }
        else
        {
            var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
            if (options.PlainText)
            {
                var writer = new Markout.MarkoutWriter(sink, options.CreateFormatter(), writerOptions);
                ApiOutputFormatter.SerializeTypeDocument(
                    view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                    explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, writer);
                writer.Flush();
            }
            else
            {
                var sw = new StringWriter();
                var writer = new Markout.MarkoutWriter(sw, new MarkdownFormatter(), writerOptions);
                ApiOutputFormatter.SerializeTypeDocument(
                    view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                    explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, writer);
                writer.Flush();
                var markdown = sw.ToString().TrimEnd();
                if (SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections))
                {
                    var pipeline = ApiMemberSectionPipelines.Create(options);
                    markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(type));
                }
                else if (SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections))
                {
                    var pipeline = ApiMemberSectionPipelines.Create(options);
                    markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.InfoSectionNames);
                }
                sink.WriteLine(OutputFormatter.ApplyRowLimit(markdown, options.Rows));
            }
        }
        return 0;
    }

    private static async Task<int> PrintApiProjectionAsync(TypeView view, ApiOptions options)
    {
        var section = options.IncludeSections!.Single();
        if (section.Equals(SectionNames.SourceFiles, StringComparison.OrdinalIgnoreCase))
        {
            return await PrintUrlProjectionAsync(
                section,
                view.SourceFileRows?.Select((row, index) => (Row: index + 1, Label: (string?)row.Url, Url: (string?)row.Url)),
                options);
        }

        if (section.Equals(SectionNames.SourceLocations, StringComparison.OrdinalIgnoreCase))
        {
            return await PrintUrlProjectionAsync(
                section,
                view.SourceLocationRows?.Select((row, index) => (Row: index + 1, Label: (string?)row.File ?? row.Url, Url: row.Url)),
                options);
        }

        var documents = section switch
        {
            SectionNames.OriginalSource => CodeSectionDocument(section, "Original Source", (options as MemberOptions)?.MethodSource?.SourceUrl, view.MemberCode?.OriginalSourceCode.Content),
            SectionNames.DecompiledSource => CodeSectionDocument(section, "Decompiled Source", null, view.MemberCode?.DecompiledSourceCode.Content),
            SectionNames.AnnotatedSource => CodeSectionDocument(section, "Annotated Source", null, view.MemberCode?.AnnotatedSourceCode.Content),
            SectionNames.IL => CodeSectionDocument(section, "IL", null, view.MemberCode?.ILCode.Content),
            _ => []
        };

        if (documents.Count == 0
            && section is not (SectionNames.SourceFiles or SectionNames.SourceLocations or SectionNames.OriginalSource
                or SectionNames.DecompiledSource or SectionNames.AnnotatedSource or SectionNames.IL))
        {
            Console.Error.WriteLine($"Error: section '{section}' is not printable.");
            return 1;
        }

        return PrintProjectionOutput.Write(
            documents,
            new PrintProjectionOptions(
                options.PrintAll,
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.Bare,
                OutputPath: null));
    }

    private static int WriteApiShapeProjection(TypeView view, ApiOptions options)
    {
        var kind = ShapeProjectionOutput.GetKind(options.Value, options.Urls, options.Paths);
        var section = options.IncludeSections!.Single();
        var rows = section switch
        {
            SectionNames.SourceFiles => ProjectTypeSourceFiles(view, section, kind),
            SectionNames.SourceLocations => ProjectSourceLocations(view, section, kind, options),
            _ => []
        };

        if (rows.Count == 0
            && section is not (SectionNames.SourceFiles or SectionNames.SourceLocations))
        {
            Console.Error.WriteLine($"Error: section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }

        return ShapeProjectionOutput.Write(
            rows,
            new ShapeProjectionOptions(kind, options.PrintRow, options.JsonOutput, options.Jsonl, options.JsonArray));
    }

    private static List<ShapeProjectionRow> ProjectTypeSourceFiles(TypeView view, string section, ShapeProjectionKind kind)
    {
        return (view.SourceFileRows ?? [])
            .Select((row, index) => row.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select((url, index) =>
            {
                var value = url!;
                return kind switch
                {
                    ShapeProjectionKind.Urls or ShapeProjectionKind.Value =>
                        new ShapeProjectionRow(index + 1, section, value, Url: value),
                    _ => null
                };
            })
            .Where(row => row is not null)
            .Cast<ShapeProjectionRow>()
            .ToList();
    }

    private static List<ShapeProjectionRow> ProjectSourceLocations(TypeView view, string section, ShapeProjectionKind kind, ApiOptions options)
    {
        List<ShapeProjectionRow> rows = [];
        var sourceRows = view.SourceLocationRows ?? [];
        for (var i = 0; i < sourceRows.Count; i++)
        {
            var row = sourceRows[i];
            string? value = kind switch
            {
                ShapeProjectionKind.Urls => row.Url,
                ShapeProjectionKind.Paths => Uncode(row.File),
                ShapeProjectionKind.Value => SelectSourceLocationValue(row, options),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(value))
                continue;
            rows.Add(new ShapeProjectionRow(
                i + 1,
                section,
                value,
                Label: Uncode(row.Selector),
                Url: kind == ShapeProjectionKind.Urls ? value : row.Url,
                Path: kind == ShapeProjectionKind.Paths ? value : Uncode(row.File)));
        }

        return rows;
    }

    private static string? SelectSourceLocationValue(MemberSourceLocationRow row, ApiOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "selector" => Uncode(row.Selector),
            "signature" => Uncode(row.Signature),
            "file" or "path" => Uncode(row.File),
            "line" => row.Line?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "end line" or "end_line" => row.EndLine?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "url" => row.Url,
            _ => row.Url
        };
    }

    private static string? Uncode(string? value)
    {
        if (value is null)
            return null;
        if (value is { Length: > 1 } && value[0] == '`' && value[^1] == '`')
            return WebUtility.HtmlDecode(value[1..^1]);
        const string open = "<code>";
        const string close = "</code>";
        return value.StartsWith(open, StringComparison.OrdinalIgnoreCase)
            && value.EndsWith(close, StringComparison.OrdinalIgnoreCase)
                ? WebUtility.HtmlDecode(value[open.Length..^close.Length])
                : WebUtility.HtmlDecode(value);
    }

    private static List<PrintableDocument> CodeSectionDocument(string section, string label, string? url, string? content)
        => string.IsNullOrEmpty(content)
            ? []
            : [new PrintableDocument(1, section, label, null, url, content)];

    private static async Task<int> PrintUrlProjectionAsync(
        string section,
        IEnumerable<(int Row, string? Label, string? Url)>? rows,
        ApiOptions options)
    {
        var printableRows = (rows ?? [])
            .Where(row => !string.IsNullOrWhiteSpace(row.Url))
            .ToList();
        if (printableRows.Count == 0)
            return PrintProjectionOutput.Write(
                [],
                new PrintProjectionOptions(options.PrintAll, null, options.JsonOutput, options.Jsonl, options.JsonArray, options.Bare, OutputPath: null));

        if (!options.PrintAll)
        {
            if (options.PrintRow is null && printableRows.Count != 1)
            {
                Console.Error.WriteLine($"Error: selected section has {printableRows.Count} printable rows; use --row N to choose one row or --print-all.");
                return 1;
            }

            var targetIndex = options.PrintRow ?? 1;
            if (targetIndex < 1 || targetIndex > printableRows.Count)
            {
                Console.Error.WriteLine($"Error: printable row {targetIndex} is out of range. Use 1 through {printableRows.Count}.");
                return 1;
            }

            printableRows = [printableRows[targetIndex - 1]];
        }

        var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
        List<PrintableDocument> documents = [];
        foreach (var row in printableRows)
        {
            var rawUrl = GitHubUrlResolver.ConvertBlobToRawUrl(row.Url!);
            var content = await fetcher.FetchSourceAsync(rawUrl);
            if (content == null)
                continue;

            documents.Add(new PrintableDocument(
                row.Row,
                section,
                string.IsNullOrWhiteSpace(row.Label) ? rawUrl : row.Label!,
                null,
                rawUrl,
                content));
        }

        return PrintProjectionOutput.Write(
            documents,
            new PrintProjectionOptions(
                options.PrintAll,
                Row: null,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.Bare,
                OutputPath: null));
    }

    private static bool TryGetBareApiPayload(TypeView view, ApiOptions options, out string raw, out string error)
    {
        raw = "";
        error = "";

        if (options.IncludeSections is not { Count: 1 } included)
        {
            error = "Error: --bare requires exactly one -S section.";
            return false;
        }

        var section = included.First();
        raw = section switch
        {
            SectionNames.DecompiledSource => view.MemberCode?.DecompiledSourceCode.Content ?? "",
            SectionNames.AnnotatedSource => view.MemberCode?.AnnotatedSourceCode.Content ?? "",
            SectionNames.CostOverlay => view.MemberCode?.CostOverlayCode.Content ?? "",
            SectionNames.SemanticsOverlay => view.MemberCode?.SemanticsOverlayCode.Content ?? "",
            SectionNames.OriginalSource => view.MemberCode?.OriginalSourceCode.Content ?? "",
            SectionNames.IL => view.MemberCode?.ILCode.Content ?? "",
            SectionNames.SourceFiles => BareUrlColumn(view.SourceFileRows?.Select(row => row.Url), SectionNames.SourceFiles, out error),
            SectionNames.SourceLocations => BareUrlColumn(view.SourceLocationRows?.Select(row => row.Url), SectionNames.SourceLocations, out error),
            _ => ""
        };

        if (raw.Length > 0)
            return true;

        if (error.Length == 0)
            error = "Error: --bare requires a single selected payload with content.";
        return false;
    }

    private static string BareUrlColumn(IEnumerable<string?>? urls, string section, out string error)
    {
        error = "";
        var values = urls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .ToList() ?? [];

        if (values.Count > 0)
            return string.Join('\n', values);

        error = $"Error: --bare found no URL in section '{section}'.";
        return "";
    }

    /// <summary>
    /// Restricts a plain-discovery schema to the columns queryable under the active options.
    /// The view schema is a union of all rendering variants, so it advertises columns that only
    /// specific historical variants surface. Deprecated columns are hidden from discovery to keep
    /// what is listed consistent with what the user can actually project. This is the
    /// option/contract level gate (data-independent); the data-level gate is effective discovery.
    /// </summary>
    /// <remarks>
    /// <c>Select</c> was the old overload-index column. Member selectors now live in the
    /// dedicated <c>Member Index</c> section, so the historical column is not queryable.
    /// </remarks>
    internal static DocumentSchema ToQueryableSchema(DocumentSchema schema, ApiOptions options)
    {
        return DiscoverOutput.WithoutColumn(schema, "Select");
    }

    /// <summary>
    /// Executes effective discovery (<c>-D</c>) for a single type. Shared by the
    /// type and member commands so both paths apply identical queryability filtering:
    /// <list type="bullet">
    /// <item>Section gate: <see cref="DiscoverOutput.RestrictToSchemaSections"/> drops pipeline
    /// sections absent from the active schema, then <see cref="DiscoverOutput.RestrictToRenderedSections"/> drops schema sections that
    /// render no data for this type (e.g. Custom Attributes when the type has no attributes),
    /// so every listed section is queryable via <c>-D &lt;Section&gt;</c> and actually has data.</item>
    /// <item>Column gate: <see cref="DiscoverOutput.FilterSchemaToRenderedHeaders"/> renders the
    /// type at the active options and keeps only columns that appear, dropping columns the
    /// active options never surface and columns with no data
    /// (e.g. Obsolete when no member is obsolete).</item>
    /// </list>
    /// This keeps effective discovery consistent with what the user can actually query and see.
    /// </summary>
    internal static int ExecuteEffectiveDiscovery(
        ApiType apiType, SectionPipeline<ApiType> memberPipeline, ApiOptions options)
    {
        var fullSchema = GetTypeDocumentSchema(options);
        var filteredType = BuildFilteredTypeForSections(apiType, options);
        var effective = memberPipeline.GetDiscoverableSections(filteredType, options.IncludeSections);
        effective = DiscoverOutput.RestrictToSchemaSections(effective, fullSchema);
        var unprobed = memberPipeline.GetUnprobedSections();
        var bareDiscover = options.Discover is null or { Length: 0 };
        var discoveryRenderSections = bareDiscover
            ? options is MemberOptions { OverloadIndex: not null }
                ? [.. effective.Where(s => !unprobed.Contains(s))]
                : [.. effective.Where(memberPipeline.GetCostAnnotations().ContainsKey)]
            : (IReadOnlyCollection<string>?)null;
        var rendered = RenderTypeSectionsMarkdown(filteredType, options, discoveryRenderSections);
        // Unprobed sections may render empty and must be opt-in by policy, so the
        // normal opt-in annotation is sufficient and avoids double labels.
        var displayAnnotations = memberPipeline.GetCostAnnotations();
        var queryEffective = effective;
        var specificSectionDiscover = options.Discover is { Length: > 0 }
            && options.Discover.Any(name => !name.StartsWith("@", StringComparison.Ordinal));
        if (specificSectionDiscover)
        {
            var renderedKept = DiscoverOutput.RestrictToRenderedSections(effective, fullSchema, rendered);
            var keep = new HashSet<string>(renderedKept, StringComparer.OrdinalIgnoreCase);
            foreach (var section in effective)
            {
                if (unprobed.Contains(section)
                    || displayAnnotations.TryGetValue(section, out var annotation)
                       && annotation.Equals(SectionAnnotations.OptIn, StringComparison.OrdinalIgnoreCase))
                    keep.Add(section);
            }
            queryEffective = effective.Where(keep.Contains).ToList();
        }
        var schema = DiscoverOutput.FilterSchemaToRenderedHeaders(queryEffective, fullSchema, rendered);
        return DiscoverOutput.ExecuteEffective(options.Discover, queryEffective, schema,
            tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.OneLine && !options.JsonOutput,
            verbosity: (int)options.Verbosity, fullSchema: fullSchema,
            sectionCostAnnotations: displayAnnotations,
            sectionCategories: memberPipeline.GetCategoryMap());
    }

    /// <summary>
    /// Renders the type's member/enum sections to a markdown string for effective-column
    /// discovery. Replicates <see cref="WriteTypeOutput"/>'s verbosity branching so the
    /// rendered table headers reflect exactly which columns the user would actually see
    /// (e.g. summary columns at Minimal, full member columns at Detailed, and the dedicated
    /// Member Index section when requested). Projection (--columns/--fields) is intentionally dropped so
    /// the result reflects all renderable columns, not a user-narrowed subset.
    /// </summary>
    internal static string RenderTypeSectionsMarkdown(ApiType type, ApiOptions options, IReadOnlyCollection<string>? discoverySections = null)
    {
        if (discoverySections is { Count: > 0 })
        {
            var baseText = RenderTypeSectionsMarkdown(type, options with { Discover = null }, discoverySections: null);
            var explicitText = RenderTypeSectionsMarkdown(type, options with
            {
                Discover = null,
                IncludeSections = new HashSet<string>(discoverySections, StringComparer.OrdinalIgnoreCase),
            }, discoverySections: null);
            return string.Concat(baseText, Environment.NewLine, explicitText);
        }

        var renderOptions = options with
        {
            Columns = null,
            Fields = null,
            PlainText = false,
            JsonOutput = false,
            OneLine = false,
        };
        if (options.Discover is { Length: > 0 } discover)
        {
            var pipeline = ApiMemberSectionPipelines.Create(options);
            var resolved = SelectResolver.ResolveSelectAsSections(
                discover, pipeline.SelectableSectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap());
            if (!resolved.HasError && resolved.Sections is { Count: > 0 })
            {
                var include = renderOptions.IncludeSections is null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(renderOptions.IncludeSections, StringComparer.OrdinalIgnoreCase);
                include.UnionWith(resolved.Sections);
                renderOptions = renderOptions with { IncludeSections = include };
            }
        }

        var view = ApiOutputFormatter.BuildTypeView(type, null, null, null, null, null, renderOptions);
        EventsView? eventsView = null;
        MethodGroupsView? methodGroupsView = null;
        MethodsView? methodsView = null;
        MemberIndexView? memberIndexView = null;
        OperatorsView? operatorsView = null;
        ExplicitInterfaceImplementationsView? explicitInterfaceImplementationsView = null;
        ExtensionMethodsView? extensionMethodsView = null;

        if (type.Kind == "enum")
            ApiOutputFormatter.PopulateEnumValues(view, type, renderOptions);

        if (view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (renderOptions is MemberOptions { OverloadIndex: not null })
                ApiOutputFormatter.PopulateMemberSignature(view, type, renderOptions);
            else
            {
                var renderMemberGroups = ApiOutputFormatter.ShouldRenderMemberGroups(renderOptions);
                var renderMemberRows = ApiOutputFormatter.ShouldRenderMemberRows(renderOptions);
                var renderSupplementalRows = ApiOutputFormatter.ShouldRenderSupplementalMemberRows(renderOptions);
                if (renderMemberGroups)
                {
                    methodGroupsView ??= new MethodGroupsView();
                    eventsView ??= new EventsView();
                    ApiOutputFormatter.PopulateMemberSummarySections(
                        view, methodGroupsView, eventsView, type, renderOptions, methodGroupsOnly: renderMemberRows);
                }
                if (renderMemberRows || renderSupplementalRows)
                {
                    methodsView ??= new MethodsView();
                    operatorsView ??= new OperatorsView();
                    explicitInterfaceImplementationsView ??= new ExplicitInterfaceImplementationsView();
                    extensionMethodsView ??= new ExtensionMethodsView();
                    eventsView ??= new EventsView();
                    ApiOutputFormatter.PopulateMemberSections(
                        view,
                        methodsView,
                        operatorsView,
                        explicitInterfaceImplementationsView,
                        extensionMethodsView,
                        eventsView,
                        type,
                        renderOptions,
                        renderSupplementalRows ? ApiOutputFormatter.SupplementalMemberKinds : null);
                }
                if (ShouldRenderMemberIndex(renderOptions))
                {
                    memberIndexView ??= new MemberIndexView();
                    ApiOutputFormatter.PopulateMemberIndex(memberIndexView, type, renderOptions);
                }
            }

            if (ShouldRenderSourceLocations(renderOptions))
                ApiOutputFormatter.PopulateMemberSourceLocations(view, type, renderOptions);

            if (renderOptions is MemberOptions { DllPath: not null } memberOptions
                && (memberOptions.OverloadIndex.HasValue || memberOptions.HasCallerScope))
            {
                var requestedSections = GetRequestedMemberSections(type, memberOptions);
                var methods = type.Members
                    .Where(m => m.Kind is "method" or "constructor" or "operator" or "explicit-interface-implementation" or "extension-method"
                        && (!m.IsAbstract || requestedSections.Contains(SectionNames.UnsafeOperations)))
                    .ToList();
                if (methods.Count > 0)
                    ApiOutputFormatter.PopulateIndexSections(view, type, methods,
                        memberOptions.DllPath!,
                        memberOptions.OverloadIndex.HasValue ? memberOptions.OverloadIndex.Value - 1 : null,
                        requestedSections, memberOptions.PdbPath, memberOptions.IncludeSections,
                        memberOptions.CallerScopeAssemblies, memberOptions);

                if (memberOptions.MethodSource != null && requestedSections.Contains(SectionNames.OriginalSource))
                {
                    view.MemberCode ??= new MemberCodeView();
                    view.MemberCode.OriginalSourceCode = new Markout.CodeSection("csharp", memberOptions.MethodSource.SourceCode);
                }
            }

            if (renderOptions.DllPath is { } unsafeDllPath
                && GetRequestedMemberSections(type, renderOptions).Contains(SectionNames.UnsafeMembers))
            {
                ApiOutputFormatter.PopulateUnsafeMembers(view, type, unsafeDllPath);
            }

            if (renderOptions.DllPath is { } optimizationDllPath
                && GetRequestedMemberSections(type, renderOptions).Contains(SectionNames.PerformanceTriage))
            {
                ApiOutputFormatter.PopulateOptimizationOpportunities(view, type, optimizationDllPath, renderOptions.IncludeSections,
                    restrictToModelMembers: ApiMemberSectionPipelines.UsesDetailPipeline(renderOptions)
                        || ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(renderOptions));
            }

            if (renderOptions.DllPath is { } leverageDllPath
                && GetRequestedMemberSections(type, renderOptions).Contains(SectionNames.TopLeverage))
            {
                ApiOutputFormatter.PopulateTopLeverage(view, type, leverageDllPath,
                    restrictToModelMembers: ApiMemberSectionPipelines.UsesDetailPipeline(renderOptions)
                        || ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(renderOptions));
            }
        }

        var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, renderOptions);
        var sw = new StringWriter();
        var writer = new MarkoutWriter(sw, new Markout.MarkdownFormatter(), writerOptions);
        ApiOutputFormatter.SerializeTypeDocument(
            view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
            explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, writer);
        writer.Flush();
        return sw.ToString();
    }

    private static void WriteJsonTypeOutput(ApiType type, ApiOptions options)
    {
        var outputType = type;
        var members = type.Members;

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter)).ToList();

        if (options.KindFilter.Count > 0)
            members = members.Where(m => options.KindFilter.Contains(m.Kind)).ToList();

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

    private static bool ShouldRenderMemberIndex(ApiOptions options)
        => options.IncludeSections?.Contains(SectionNames.MemberIndex) == true
           || options is MemberOptions { ShowMemberIndex: true };

    private static bool ShouldRenderSourceLocations(ApiOptions options)
        => options.IncludeSections?.Contains(SectionNames.SourceLocations) == true;

    /// <summary>
    /// Maps each member section name to the predicate that selects its members.
    /// </summary>
    private static readonly Dictionary<string, Func<ApiMember, bool>> MemberSectionPredicates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SectionNames.Values] = m => m.Kind == "field" && m.EnumValue.HasValue,
            [SectionNames.Fields] = m => m.Kind == "field" && !m.EnumValue.HasValue,
            [SectionNames.Properties] = m => m.Kind == "property",
            [SectionNames.MethodGroups] = m => m.Kind == "method",
            [SectionNames.Methods] = m => m.Kind == "method",
            [SectionNames.Operators] = m => m.Kind == "operator",
            [SectionNames.ExplicitInterfaceImplementations] = m => m.Kind == "explicit-interface-implementation",
            [SectionNames.ExtensionMethods] = m => m.Kind == "extension-method",
            [SectionNames.Constructors] = m => m.Kind == "constructor",
            [SectionNames.Events] = m => m.Kind == "event",
            [SectionNames.SourceLocations] = ApiMemberSectionDescriptors.IsMethodLike,
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

}
