using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Research;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Compares API surfaces between two package or platform versions.
/// </summary>
public class DiffCommand
{
    public const string Name = "diff";
    public static async Task<int> ExecuteAsync(DiffOptions options)
    {
        var pipeline = DiffSections.CreatePipeline();
        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select, pipeline.SelectableSectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap());
        if (SelectOutput.WriteUnresolved(selectResult))
            return 1;
        if (selectResult.Sections != null)
            options = options with { IncludeSections = selectResult.Sections };

        var hasPlatform = !string.IsNullOrEmpty(options.PlatformVersionRange);
        var hasPackage = !string.IsNullOrEmpty(options.PackageVersionRange);
        var hasLibrary = !string.IsNullOrEmpty(options.LibraryVersionRange);

        // Discovery mode: -D/--discover lists schema
        if (options.Discover != null)
        {
            var schemaMap = DiffSections.CreateSchema();
            var discoverable = pipeline.GetDiscoverableSections(new DiffDiscoveryModel(), options.IncludeSections);
            return DiscoverOutput.ExecuteEffective(options.Discover, discoverable, schemaMap,
                tree: options.Tree, json: false, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.OneLine,
                sectionCostAnnotations: pipeline.GetCostAnnotations(),
                sectionCategories: pipeline.GetCategoryMap());
        }

        if (!hasPlatform && !hasPackage && !hasLibrary)
        {
            Console.Error.WriteLine("Error: --package, --platform, or --library with version range required.");
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  --package System.Text.Json@9.0.0..10.0.2");
            Console.Error.WriteLine("  --platform System.Text.Json@8.0.23..10.0.2");
            Console.Error.WriteLine("  --library old/Foo.dll..new/Foo.dll");
            return 1;
        }

        if ((hasPlatform ? 1 : 0) + (hasPackage ? 1 : 0) + (hasLibrary ? 1 : 0) > 1)
        {
            Console.Error.WriteLine("Error: Cannot specify more than one of --package, --platform, and --library.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            DiffInputs inputs;

            if (hasPackage)
            {
                var result = await ExecutePackageDiffAsync(options, logger, context.HttpClient);
                if (result.error != null)
                {
                    Console.Error.WriteLine(result.error);
                    return 1;
                }
                inputs = result.inputs!;
            }
            else if (hasPlatform)
            {
                var result = await ExecutePlatformDiffAsync(options, logger, context.HttpClient);
                if (result.error != null)
                {
                    Console.Error.WriteLine(result.error);
                    return 1;
                }
                inputs = result.inputs!;
            }
            else
            {
                var result = ExecuteLibraryDiff(options);
                if (result.error != null)
                {
                    Console.Error.WriteLine(result.error);
                    return 1;
                }
                inputs = result.inputs!;
            }

            try
            {
                if (SelectsAnalysisDiff(options))
                {
                    var analysis = BuildAnalysisDiff(inputs.FromPaths, inputs.ToPaths, options);
                    var view = DiffOutputFormatter.BuildAnalysisDiffView(inputs.Name, analysis.Rows, analysis.Summary, inputs.FromVersion, inputs.ToVersion);
                    if (options.Tsv || options.Jsonl)
                    {
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        var output = DiffOutputFormatter.RenderAnalysisDiffView(view);
                        Console.WriteLine(OutputFormatter.ApplyRowLimit(output, options.Rows));
                    }
                    return 0;
                }

                var diff = BuildApiDiff(inputs.FromSurface, inputs.ToSurface, options);

                if (options.OneLine)
                {
                    var typeDiffs = ApplyFilters(diff, options);
                    var view = DiffOutputFormatter.BuildOneLineView(inputs.Name, typeDiffs, inputs.FromVersion, inputs.ToVersion);
                    OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                        options.Columns, options.Fields,
                        (writer, formatter, writerOptions) =>
                            MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                        options.Rows);
                }
                else
                {
                    var output = options.Verbosity == Verbosity.Minimal && options.IncludeSections is null
                        ? DiffOutputFormatter.RenderOverview(BuildOverview(inputs, diff, options))
                        : RenderDiff(inputs.Name, diff, inputs.FromVersion, inputs.ToVersion, options);
                    Console.WriteLine(output);
                }

                return 0;
            }
            finally
            {
                inputs.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    internal sealed record DiffInputs(
        ApiSurface FromSurface,
        ApiSurface ToSurface,
        string FromVersion,
        string ToVersion,
        string Name,
        IReadOnlyList<string> FromPaths,
        IReadOnlyList<string> ToPaths,
        string? FromTempDir = null,
        string? ToTempDir = null) : IDisposable
    {
        public void Dispose()
        {
            DeleteTemp(FromTempDir);
            DeleteTemp(ToTempDir);
        }

        static void DeleteTemp(string? tempDir)
        {
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static async Task<(DiffInputs? inputs, string? error)>
        ExecutePackageDiffAsync(DiffOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        var (packageName, fromVersion, toVersion) = ParseVersionRange(options.PackageVersionRange!);
        if (packageName == null || fromVersion == null || toVersion == null)
        {
            return (null, "Error: Invalid version range. Use format: Package@v1..v2");
        }

        logger.Log($"Comparing {packageName} v{fromVersion} -> v{toVersion}");

        var from = await ExtractPackageInputAsync($"{packageName}@{fromVersion}", options, logger, httpClient);
        if (from.error is not null)
            return (null, $"Error resolving v{fromVersion}: {from.error}");
        var to = await ExtractPackageInputAsync($"{packageName}@{toVersion}", options, logger, httpClient);
        if (to.error is not null)
        {
            DeleteTempDir(from.tempDir);
            return (null, $"Error resolving v{toVersion}: {to.error}");
        }

        return (new DiffInputs(
            from.surface!, to.surface!, fromVersion, toVersion, packageName,
            from.paths!, to.paths!, from.tempDir, to.tempDir), null);
    }

    private static async Task<(DiffInputs? inputs, string? error)>
        ExecutePlatformDiffAsync(DiffOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        var (assemblyName, fromVersion, toVersion) = ParseVersionRange(options.PlatformVersionRange!);
        if (assemblyName == null || fromVersion == null || toVersion == null)
        {
            return (null, "Error: Invalid version range. Use format: Library@v1..v2");
        }

        var framework = options.Framework ?? "runtime";
        logger.Log($"Comparing {assemblyName} in {framework} v{fromVersion} -> v{toVersion}");

        // Resolve assemblies for both versions (downloads ref packs as needed)
        var (fromPath, _, _, fromError) = await PlatformResolver.ResolveAssemblyAsync(
            assemblyName,
            httpClient,
            logger.Log,
            $"{framework}@{fromVersion}");

        if (fromError != null)
        {
            return (null, $"Error resolving v{fromVersion}: {fromError}");
        }

        var (toPath, _, _, toError) = await PlatformResolver.ResolveAssemblyAsync(
            assemblyName,
            httpClient,
            logger.Log,
            $"{framework}@{toVersion}");

        if (toError != null)
        {
            return (null, $"Error resolving v{toVersion}: {toError}");
        }

        // Extract API surfaces from both assemblies
        var fromSurface = ExtractApiSurface(fromPath!, options.IncludeAll);
        var toSurface = ExtractApiSurface(toPath!, options.IncludeAll);

        if (fromSurface == null || toSurface == null)
        {
            return (null, "Error: Failed to extract API surface from one or both versions.");
        }

        return (new DiffInputs(
            fromSurface, toSurface, fromVersion, toVersion, assemblyName,
            [fromPath!], [toPath!]), null);
    }

    private static (DiffInputs? inputs, string? error) ExecuteLibraryDiff(DiffOptions options)
    {
        var (fromPath, toPath) = ParsePathRange(options.LibraryVersionRange!);
        if (fromPath is null || toPath is null)
            return (null, "Error: Invalid library range. Use format: old/Foo.dll..new/Foo.dll");
        if (!File.Exists(fromPath))
            return (null, $"Error: File not found: {fromPath}");
        if (!File.Exists(toPath))
            return (null, $"Error: File not found: {toPath}");
        var fromSurface = ExtractApiSurface(fromPath, options.IncludeAll);
        var toSurface = ExtractApiSurface(toPath, options.IncludeAll);
        if (fromSurface is null || toSurface is null)
            return (null, "Error: Failed to extract API surface from one or both libraries.");

        var name = Path.GetFileNameWithoutExtension(toPath);
        return (new DiffInputs(
            fromSurface, toSurface,
            Path.GetFileName(fromPath), Path.GetFileName(toPath), name,
            [fromPath], [toPath]), null);
    }

    private static async Task<(ApiSurface? surface, List<string>? paths, string? tempDir, string? error)> ExtractPackageInputAsync(
        string packageReference, DiffOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        var outcome = await PackageExtractor.ExtractPackageAsync(httpClient, packageReference, logger.Log, "inspect-diff", options.SourceOptions).ConfigureAwait(false);
        if (!outcome.IsSuccess)
            return (null, null, null, outcome.ErrorMessage);

        var extracted = outcome.Result!;
        var (paths, selectedTfm) = TfmSelector.SelectHighestAssembliesFromPackage(extracted.ExtractPath, options.Tfm);
        if (paths.Count == 0 && string.IsNullOrEmpty(options.Tfm))
            return (null, null, extracted.TempDir, "No DLLs found in package.");

        paths = paths.OrderBy(path => path, StringComparer.Ordinal).ToList();

        if (paths.Count == 0)
            return (null, null, extracted.TempDir, "No DLLs found for selected TFM.");

        var surface = MergeSurfaces(paths, extracted.PackageName, selectedTfm, options.IncludeAll, logger);
        return surface is null
            ? (null, null, extracted.TempDir, "Failed to extract API surface.")
            : (surface, paths, extracted.TempDir, null);
    }

    private static ApiSurface? MergeSurfaces(IReadOnlyList<string> paths, string? name, string? tfm, bool includeAll, VerboseLogger logger)
    {
        if (paths.Count == 1)
        {
            var single = AssemblyReader.ExtractApiSurface(paths[0], includeAll);
            if (single is not null)
            {
                single.Name = name ?? Path.GetFileNameWithoutExtension(paths[0]);
                single.Tfm = tfm;
            }
            return single;
        }

        var merged = new ApiSurface { Name = name, Tfm = tfm };
        foreach (var path in paths)
        {
            var surface = AssemblyReader.ExtractApiSurface(path, includeAll);
            if (surface is null)
                continue;
            logger.Log($"  + {Path.GetFileNameWithoutExtension(path)}: {surface.PublicTypeCount} types");
            merged.Types.AddRange(surface.Types);
            merged.PublicTypeCount += surface.PublicTypeCount;
            merged.PublicMethodCount += surface.PublicMethodCount;
            merged.PublicPropertyCount += surface.PublicPropertyCount;
            merged.PublicEventCount += surface.PublicEventCount;
            merged.PublicFieldCount += surface.PublicFieldCount;
        }
        merged.Types = merged.Types.OrderBy(type => type.FullName).ToList();
        return merged.Types.Count == 0 ? null : merged;
    }

    private static (string? fromPath, string? toPath) ParsePathRange(string input)
    {
        int dotDotIndex = input.IndexOf("..", StringComparison.Ordinal);
        if (dotDotIndex <= 0 || dotDotIndex + 2 >= input.Length)
            return (null, null);
        return (input[..dotDotIndex], input[(dotDotIndex + 2)..]);
    }

    private static bool SelectsAnalysisDiff(DiffOptions options)
        => options.AllocRegressionsOnly
            || options.IncludeSections?.Contains("Analysis Diff") == true;

    internal sealed record AnalysisDiffResult(List<AnalysisDiffRow> Rows, string Summary);

    // A diff row plus the metadata used to rank and classify it. Magnitude is the
    // absolute numeric movement (for ordering); Direction is +1 regression (more
    // cost), -1 improvement (less cost), 0 neutral; InBoth is true when the member
    // is present in both versions (an in-place change vs an added/removed member).
    internal sealed record RankedAnalysisRow(AnalysisDiffRow Row, int Magnitude, int Direction, bool InBoth, bool InLoop = false);

    internal static AnalysisDiffResult BuildAnalysisDiff(IReadOnlyList<string> fromPaths, IReadOnlyList<string> toPaths, DiffOptions options)
    {
        var research = ResearchDiff.Compare(
            ResearchDiffInput.FromAssemblies(fromPaths),
            ResearchDiffInput.FromAssemblies(toPaths),
            new ResearchDiffOptions(
                ResearchDiffMechanism.BodySignals,
                TypeFilters: options.TypeFilter));
        var ranked = research.Subjects
            .SelectMany(subject => subject.Evidence.Select(evidence => (subject, evidence)))
            .Where(entry => entry.evidence.Category == ResearchDiffChangeCategory.BodySignal
                && entry.evidence.ChangeId.StartsWith("analysis.", StringComparison.Ordinal)
                && entry.evidence.Signal is { Length: > 0 })
            .Select(entry => new RankedAnalysisRow(
                new AnalysisDiffRow(
                    MarkoutInline.Code(entry.subject.Subject.Display),
                    entry.evidence.Signal!,
                    entry.evidence.OldValue ?? "",
                    entry.evidence.NewValue ?? "",
                    entry.evidence.Delta ?? "changed",
                    entry.evidence.Shape,
                    entry.evidence.Detail),
                entry.evidence.Magnitude ?? 1,
                entry.evidence.DirectionScore,
                entry.evidence.SubjectInBoth,
                entry.evidence.InLoop))
            .ToList();

        return RankAnalysisRows(ranked, options.ChangedOnly, options.AllocRegressionsOnly);
    }

    // Applies the changed-only / allocation-regression filters, ranks rows (in-place
    // changes first, then by descending movement magnitude), and builds the summary
    // line. Pure function over already-classified rows so it can be unit-tested
    // without assemblies. In allocation-regression focus mode, only in-place
    // allocation increases are kept and in-loop (hot) ones are surfaced first.
    internal static AnalysisDiffResult RankAnalysisRows(IReadOnlyList<RankedAnalysisRow> ranked, bool changedOnly, bool allocRegressionsOnly = false)
    {
        IEnumerable<RankedAnalysisRow> filtered = ranked;
        if (allocRegressionsOnly)
            filtered = filtered.Where(row => row.InBoth && row.Direction > 0 && row.Row.Signal == "allocations");
        else if (changedOnly)
            filtered = filtered.Where(row => row.InBoth);
        var selected = filtered.ToList();

        var regressions = selected.Count(row => row.InBoth && row.Direction > 0);
        var improvements = selected.Count(row => row.InBoth && row.Direction < 0);
        var addedRemoved = selected.Count(row => !row.InBoth);
        var inLoopRegressions = selected.Count(row => row.InLoop && row.Direction > 0);

        var rows = selected
            .OrderByDescending(row => allocRegressionsOnly && row.InLoop)
            .ThenByDescending(row => row.InBoth)
            .ThenByDescending(row => row.Magnitude)
            .ThenBy(row => row.Row.Member, StringComparer.Ordinal)
            .ThenBy(row => row.Row.Signal, StringComparer.Ordinal)
            .ThenBy(row => row.Row.Shape ?? "", StringComparer.Ordinal)
            .Select(row => row.Row)
            .ToList();

        var summary = allocRegressionsOnly
            ? BuildAllocRegressionSummary(rows.Count, inLoopRegressions)
            : BuildAnalysisSummary(rows.Count, regressions, improvements, addedRemoved, changedOnly);
        return new AnalysisDiffResult(rows, summary);
    }

    internal static string BuildAllocRegressionSummary(int total, int inLoop)
    {
        if (total == 0)
            return "No allocation regressions detected.";
        var hot = inLoop > 0 ? $", {inLoop} in loop" : "";
        return $"{total} allocation regression{(total == 1 ? "" : "s")}{hot} ({total} signal{(total == 1 ? "" : "s")}).";
    }

    internal static string BuildAnalysisSummary(int total, int regressions, int improvements, int addedRemoved, bool changedOnly)
    {
        if (total == 0)
            return changedOnly ? "No in-place analysis signal changes detected." : "No analysis signal changes detected.";
        var parts = new List<string>(3);
        if (regressions > 0) parts.Add($"{regressions} regression{(regressions == 1 ? "" : "s")}");
        if (improvements > 0) parts.Add($"{improvements} improvement{(improvements == 1 ? "" : "s")}");
        if (!changedOnly && addedRemoved > 0) parts.Add($"{addedRemoved} added/removed");
        var detail = parts.Count > 0 ? string.Join(", ", parts) : $"{total} changed signal{(total == 1 ? "" : "s")}";
        return $"{detail} ({total} signal{(total == 1 ? "" : "s")})";
    }

    internal static string MethodKey(MethodIdentity method)
        => $"{method.AssemblyName}|{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    private static void DeleteTempDir(string? tempDir)
    {
        if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static ApiSurface? ExtractApiSurface(string assemblyPath, bool includeAll)
    {
        return AssemblyReader.ExtractApiSurface(assemblyPath, includeAll);
    }

    private static (string? package, string? fromVersion, string? toVersion) ParseVersionRange(string input)
    {
        // Format: Package@v1..v2
        int atIndex = input.IndexOf('@');
        if (atIndex <= 0)
            return (null, null, null);

        string packageName = input[..atIndex];
        string versionPart = input[(atIndex + 1)..];

        int dotDotIndex = versionPart.IndexOf("..", StringComparison.Ordinal);
        if (dotDotIndex <= 0)
            return (null, null, null);

        string fromVersion = versionPart[..dotDotIndex];
        string toVersion = versionPart[(dotDotIndex + 2)..];

        if (string.IsNullOrEmpty(fromVersion) || string.IsNullOrEmpty(toVersion))
            return (null, null, null);

        return (packageName, fromVersion, toVersion);
    }

    internal static IReadOnlyList<TypeDiff> ApplyFilters(ApiDiff diff, DiffOptions options)
    {
        var typeDiffs = ApplyTypeFilters(diff.TypeDiffs, options.TypeFilter, writeNote: true);

        // Apply classification filter
        return FilterByClassification(typeDiffs, options);
    }

    static IReadOnlyList<TypeDiff> ApplyTypeFilters(IReadOnlyList<TypeDiff> typeDiffs, HashSet<string> filters, bool writeNote)
    {
        if (filters.Count == 0)
            return typeDiffs;

        var filtered = typeDiffs
            .Where(td => MatchesAnyDiffTypeFilter(td.TypeFullName, filters))
            .ToList();

        if (writeNote && filtered.Count == 0)
            Console.Error.WriteLine($"Note: type filter matched no changed types: {string.Join(", ", filters)}.");
        return filtered;
    }

    private static bool MatchesAnyDiffTypeFilter(string typeFullName, IEnumerable<string> filters)
    {
        foreach (var filter in filters)
        {
            if (MatchesDiffTypeFilter(typeFullName, filter))
                return true;
        }

        return false;
    }

    private static bool MatchesDiffTypeFilter(string typeFullName, string filter)
    {
        if (TypeMatcher.MatchesTypeFilter(typeFullName, filter))
            return true;

        if (filter.Contains('*') || filter.Contains('?'))
            return false;

        var normalizedFilter = TypeMatcher.Normalize(filter);
        return typeFullName.StartsWith(normalizedFilter + ".", StringComparison.OrdinalIgnoreCase)
               || typeFullName.Contains("." + normalizedFilter + ".", StringComparison.OrdinalIgnoreCase);
    }

    internal static string RenderDiff(string name, ApiDiff diff, string fromVersion, string toVersion, DiffOptions options)
    {
        var typeDiffs = ApplyFilters(diff, options);

        if (options.NameOnly)
        {
            return OutputFormatter.RenderTable(showHeader: false, (writer, formatter) =>
            {
                var nameWriter = new Markout.MarkoutWriter(writer, formatter, OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl));
                DiffOutputFormatter.RenderNameOnly(nameWriter, typeDiffs);
                nameWriter.Flush();
            });
        }

        var markdown = DiffOutputFormatter.RenderFullMarkdown(name, typeDiffs, fromVersion, toVersion);
        return OutputFormatter.ApplyRowLimit(markdown, options.Rows);
    }

    internal static DiffOverview BuildOverview(DiffInputs inputs, ApiDiff apiDiff, DiffOptions options)
    {
        var apiChanged = FilterByClassification(ApplyTypeFilters(apiDiff.TypeDiffs, options.TypeFilter, writeNote: false), options).Count > 0;
        var attributeDiff = ResearchDiff.Compare(
            ResearchDiffInput.FromApiSurface(inputs.FromSurface),
            ResearchDiffInput.FromApiSurface(inputs.ToSurface),
            new ResearchDiffOptions(
                ResearchDiffMechanism.Api,
                IncludeAllApi: options.IncludeAll,
                ApiScope: ApiDiffScope.Attributes)).ApiDiff ?? new ApiDiff();
        var attributeChanged = FilterByClassification(ApplyTypeFilters(attributeDiff.TypeDiffs, options.TypeFilter, writeNote: false), options).Count > 0;
        var bodyChanged = inputs.FromPaths.Count > 0
            && inputs.ToPaths.Count > 0
            && ResearchDiff.Compare(
                ResearchDiffInput.FromAssemblies(inputs.FromPaths),
                ResearchDiffInput.FromAssemblies(inputs.ToPaths),
                new ResearchDiffOptions(
                    ResearchDiffMechanism.BodySignals | ResearchDiffMechanism.IlBody | ResearchDiffMechanism.CSharp,
                    TypeFilters: options.TypeFilter)).Subjects.Any(subject => subject.ImplementationChanged);

        return new DiffOverview(
            inputs.Name,
            SourceKind(options),
            inputs.FromVersion,
            inputs.ToVersion,
            [.. options.TypeFilter.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)],
            apiChanged,
            attributeChanged,
            bodyChanged);
    }

    internal static ApiDiff BuildApiDiff(ApiSurface fromSurface, ApiSurface toSurface, DiffOptions options)
        => ResearchDiff.Compare(
            ResearchDiffInput.FromApiSurface(fromSurface),
            ResearchDiffInput.FromApiSurface(toSurface),
            new ResearchDiffOptions(
                ResearchDiffMechanism.Api,
                IncludeAllApi: options.IncludeAll,
                ApiScope: ApiDiffScope.Signature)).ApiDiff
            ?? new ApiDiff();

    static string SourceKind(DiffOptions options)
        => options.PackageVersionRange is not null ? "package"
            : options.PlatformVersionRange is not null ? "platform library"
            : "library";

    private static IReadOnlyList<TypeDiff> FilterByClassification(IReadOnlyList<TypeDiff> typeDiffs, DiffOptions options)
    {
        if (!options.Breaking && !options.Additive)
            return typeDiffs;

        HashSet<ChangeClassification> allowed = [];
        if (options.Breaking) allowed.Add(ChangeClassification.Breaking);
        if (options.Additive) allowed.Add(ChangeClassification.Additive);

        List<TypeDiff> filtered = [];
        foreach (var td in typeDiffs)
        {
            var changes = td.Changes.Where(c => allowed.Contains(c.Classification)).ToList();
            if (changes.Count > 0)
                filtered.Add(new TypeDiff(td.TypeFullName, changes));
        }
        return filtered;
    }
}

/// <summary>
/// Options for the diff command.
/// </summary>
public record DiffOptions
{
    public string? PackageVersionRange { get; init; }
    public string? PlatformVersionRange { get; init; }
    public string? LibraryVersionRange { get; init; }
    public string? Framework { get; init; }
    public string? Tfm { get; init; }
    public bool IncludeAll { get; init; }
    public bool Verbose { get; init; }
    public HashSet<string> TypeFilter { get; init; } = [];
    public bool OneLine { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool FormatExplicitlySet { get; init; }
    public bool NoHeader { get; init; }
    public bool NameOnly { get; init; }
    public bool Breaking { get; init; }
    public bool Additive { get; init; }
    public bool ChangedOnly { get; init; }
    public bool AllocRegressionsOnly { get; init; }
    public bool Legend { get; init; }
    public string[]? Discover { get; init; }
    public bool Tree { get; init; }
    public string[]? Select { get; init; }
    public HashSet<string>? IncludeSections { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public int? Rows { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public bool IsRawOutput => OneLine || Jsonl || NoHeader || NameOnly;
}
