using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Findings;
using ILInspector.Research;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Compares API surfaces or selected body-level evidence between two versions.
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

        if (SelectsFindingTransitions(options))
        {
            if (options.IncludeSections is { Count: > 0 } sections
                && (sections.Count != 1 || !sections.Contains("Finding Transitions")))
            {
                Console.Error.WriteLine("Error: Finding Transitions must be selected by itself.");
                return 1;
            }
            if (options.Finding is null
                && options.TypeFilter.Count == 0
                && options.MemberFilter.Count == 0)
            {
                Console.Error.WriteLine("Error: Finding Transitions requires --type or a type-qualified --member target.");
                return 1;
            }
            if (!TryResolveFindingDescriptor(options, out var findingDescriptor, out var findingError))
            {
                Console.Error.WriteLine($"Error: {findingError}");
                return 1;
            }
            if (findingDescriptor == AnalysisFindings.AllocationDescriptor.Id)
            {
                if (options.MemberFilter.Count != 1)
                {
                    Console.Error.WriteLine("Error: --finding analysis.allocation requires exactly one --member target.");
                    return 1;
                }
            }
            else if (findingDescriptor == MetadataFindings.TypeDescriptor.Id)
            {
                if (options.TypeFilter.Count == 0 || options.MemberFilter.Count > 0)
                {
                    Console.Error.WriteLine("Error: --finding api.type requires --type and cannot be combined with --member.");
                    return 1;
                }
            }
            else if (options.MemberFilter.Count == 0)
            {
                Console.Error.WriteLine("Error: --finding api.member requires --member.");
                return 1;
            }
            if (options.Breaking || options.Additive || options.ChangedOnly
                || options.AllocRegressionsOnly || options.NameOnly)
            {
                Console.Error.WriteLine(
                    "Error: Finding Transitions reports the exact PairFinding kind and cannot be combined with change-classification, analysis, or name-only filters.");
                return 1;
            }
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
                if (SelectsFindingTransitions(options))
                {
                    TryResolveFindingDescriptor(options, out var findingDescriptor, out _);
                    var rows = findingDescriptor == AnalysisFindings.AllocationDescriptor.Id
                        ? BuildAllocationFindingTransitions(
                            inputs.FromPaths,
                            inputs.ToPaths,
                            inputs.FromSurface,
                            inputs.ToSurface,
                            inputs.FromVersion,
                            inputs.ToVersion,
                            options)
                        : BuildFindingTransitions(
                            inputs.FromSurface,
                            inputs.ToSurface,
                            inputs.FromVersion,
                            inputs.ToVersion,
                            options);
                    var view = DiffOutputFormatter.BuildFindingTransitionsView(
                        inputs.Name,
                        rows,
                        inputs.FromVersion,
                        inputs.ToVersion);
                    if (options.OneLine || options.Tsv || options.Jsonl)
                    {
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        var output = DiffOutputFormatter.RenderFindingTransitionsView(view);
                        Console.WriteLine(OutputFormatter.ApplyRowLimit(output, options.Rows));
                    }
                    return 0;
                }

                if (SelectsImplementationDiff(options) && !SelectsAnalysisDiff(options))
                {
                    var implementation = BuildImplementationDiff(
                        inputs.FromPaths,
                        inputs.ToPaths,
                        options,
                        inputs.FromSurface,
                        inputs.ToSurface);
                    var view = DiffOutputFormatter.BuildImplementationDiffView(
                        inputs.Name,
                        implementation,
                        inputs.FromVersion,
                        inputs.ToVersion);
                    if (options.OneLine)
                    {
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        var output = DiffOutputFormatter.RenderImplementationDiffView(view);
                        Console.WriteLine(OutputFormatter.ApplyRowLimit(output, options.Rows));
                    }
                    return 0;
                }

                if (SelectsAnalysisDiff(options))
                {
                    var analysis = BuildAnalysisDiff(inputs.FromPaths, inputs.ToPaths, options, inputs.FromSurface, inputs.ToSurface);
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
                    if (SelectsDetailedChanges(options))
                    {
                        var view = DiffOutputFormatter.BuildDetailedChangesView(inputs.Name, typeDiffs, inputs.FromVersion, inputs.ToVersion);
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }
                    else
                    {
                        var view = DiffOutputFormatter.BuildOneLineView(inputs.Name, typeDiffs, inputs.FromVersion, inputs.ToVersion);
                        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                            options.Columns, options.Fields,
                            (writer, formatter, writerOptions) =>
                                MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOptions),
                            options.Rows);
                    }
                }
                else
                {
                    var output = RenderDiff(inputs.Name, diff, inputs.FromVersion, inputs.ToVersion, options);
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

    sealed record DiffInputs(
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

    private static bool SelectsFindingTransitions(DiffOptions options)
        => options.Finding is not null
            || options.IncludeSections?.Contains("Finding Transitions") == true;

    private static bool TryResolveFindingDescriptor(
        DiffOptions options,
        out string descriptor,
        out string? error)
    {
        descriptor = options.Finding
            ?? (options.MemberFilter.Count > 0
                ? MetadataFindings.MemberDescriptor.Id
                : MetadataFindings.TypeDescriptor.Id);
        if (string.Equals(descriptor, MetadataFindings.TypeDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = MetadataFindings.TypeDescriptor.Id;
            error = null;
            return true;
        }
        if (string.Equals(descriptor, MetadataFindings.MemberDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = MetadataFindings.MemberDescriptor.Id;
            error = null;
            return true;
        }
        if (string.Equals(descriptor, AnalysisFindings.AllocationDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            descriptor = AnalysisFindings.AllocationDescriptor.Id;
            error = null;
            return true;
        }

        error = $"Unsupported Finding descriptor '{descriptor}'. Supported descriptors: api.type, api.member, analysis.allocation.";
        return false;
    }

    private static bool SelectsImplementationDiff(DiffOptions options)
        => options.IncludeSections?.Contains("Implementation Diff") == true;

    private static bool SelectsDetailedChanges(DiffOptions options)
        => options.IncludeSections?.Contains("Changes") == true;

    internal sealed record AnalysisDiffResult(List<AnalysisDiffRow> Rows, string Summary);

    // A diff row plus the metadata used to rank and classify it. Magnitude is the
    // absolute numeric movement (for ordering); Direction is +1 regression (more
    // cost), -1 improvement (less cost), 0 neutral; InBoth is true when the member
    // is present in both versions (an in-place change vs an added/removed member).
    internal sealed record RankedAnalysisRow(AnalysisDiffRow Row, int Magnitude, int Direction, bool InBoth, bool InLoop = false);

    internal static AnalysisDiffResult BuildAnalysisDiff(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        DiffOptions options,
        ApiSurface? fromSurface = null,
        ApiSurface? toSurface = null)
    {
        var memberTargetIdentities = options.MemberFilter.Count == 0
            ? null
            : ResolveMemberTargetIdentities(
                fromSurface ?? MergeSurfaces(fromPaths, name: null, tfm: null, includeAll: options.IncludeAll, logger: new VerboseLogger(enabled: false)) ?? new ApiSurface(),
                toSurface ?? MergeSurfaces(toPaths, name: null, tfm: null, includeAll: options.IncludeAll, logger: new VerboseLogger(enabled: false)) ?? new ApiSurface(),
                options.MemberFilter,
                options.TypeFilter,
                requireBodyTargets: true).MemberIdentities;
        var research = ResearchDiff.Compare(
            ResearchDiffInput.FromAssemblies(fromPaths),
            ResearchDiffInput.FromAssemblies(toPaths),
            new ResearchDiffOptions(
                ResearchChangeMechanism.BodySignals,
                TypeFilters: options.TypeFilter,
                MemberTargetIdentities: memberTargetIdentities));
        var ranked = research.Changes
            .Where(change => change.Category == ResearchChangeCategory.BodySignal
                && change.Descriptor.Id.StartsWith("analysis.", StringComparison.Ordinal)
                && change.Signal is { Length: > 0 })
            .Select(change => new RankedAnalysisRow(
                new AnalysisDiffRow(
                    MarkoutInline.Code(change.Subject.Display),
                    change.Signal!,
                    change.OldValue ?? "",
                    change.NewValue ?? "",
                    change.Delta ?? "changed",
                    change.Shape,
                    change.Detail),
                change.Magnitude ?? 1,
                change.DirectionScore,
                change.SubjectInBoth,
                change.InLoop))
            .ToList();

        return RankAnalysisRows(ranked, options.ChangedOnly, options.AllocRegressionsOnly);
    }

    internal static ImplementationDiffResult BuildImplementationDiff(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        DiffOptions options,
        ApiSurface? fromSurface = null,
        ApiSurface? toSurface = null)
    {
        var memberTargetIdentities = options.MemberFilter.Count == 0
            ? null
            : ResolveMemberTargetIdentities(
                fromSurface ?? MergeSurfaces(fromPaths, name: null, tfm: null, includeAll: options.IncludeAll, logger: new VerboseLogger(enabled: false)) ?? new ApiSurface(),
                toSurface ?? MergeSurfaces(toPaths, name: null, tfm: null, includeAll: options.IncludeAll, logger: new VerboseLogger(enabled: false)) ?? new ApiSurface(),
                options.MemberFilter,
                options.TypeFilter,
                requireBodyTargets: true,
                bodySectionName: "Implementation Diff").MemberIdentities;

        return ImplementationDiff.Compare(
            ResearchDiffInput.FromAssemblies(fromPaths),
            ResearchDiffInput.FromAssemblies(toPaths),
            new ImplementationDiffOptions(
                TypeFilters: options.TypeFilter,
                MemberTargetIdentities: memberTargetIdentities));
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
        var typeDiffs = diff.TypeDiffs;
        var beforeTypeFilterCount = typeDiffs.Count;

        // Apply type filter post-Compare
        if (options.TypeFilter.Count > 0)
        {
            typeDiffs = typeDiffs
                .Where(td => MatchesAnyDiffTypeFilter(td.TypeFullName, options.TypeFilter))
                .ToList();

            if (typeDiffs.Count == 0 && beforeTypeFilterCount > 0 && options.MemberFilter.Count == 0)
                Console.Error.WriteLine($"Note: type filter matched no changed types: {string.Join(", ", options.TypeFilter)}.");
        }

        // Apply classification filter
        var filtered = FilterByClassification(typeDiffs, options);
        var classificationFilterActive = options.Breaking || options.Additive;
        if (filtered.Count == 0 && typeDiffs.Count > 0 && classificationFilterActive)
        {
            Console.Error.WriteLine("Note: classification filter removed all changes after type/member filters.");
        }

        return filtered;
    }

    private static IReadOnlyList<TypeDiff> ApplyTypeFilterOnly(IReadOnlyList<TypeDiff> typeDiffs, IReadOnlyCollection<string> typeFilters)
        => typeFilters.Count == 0
            ? typeDiffs
            : typeDiffs.Where(td => MatchesAnyDiffTypeFilter(td.TypeFullName, typeFilters)).ToList();

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

    internal static ApiDiff BuildApiDiff(ApiSurface fromSurface, ApiSurface toSurface, DiffOptions options)
    {
        var diff = ResearchDiff.Compare(
                ResearchDiffInput.FromApiSurface(fromSurface),
                ResearchDiffInput.FromApiSurface(toSurface),
                new ResearchDiffOptions(
                    ResearchChangeMechanism.Api,
                    IncludeAllApi: options.IncludeAll,
                    ApiScope: ApiDiffScope.Signature)).ApiDiff
            ?? new ApiDiff();

        if (options.MemberFilter.Count == 0)
            return diff;

        var candidateTypeDiffs = ApplyTypeFilterOnly(diff.TypeDiffs, options.TypeFilter);
        if (candidateTypeDiffs.Count == 0 && diff.TypeDiffs.Count > 0 && options.TypeFilter.Count > 0)
            Console.Error.WriteLine($"Note: type filter matched no changed types: {string.Join(", ", options.TypeFilter)}.");

        var filtered = FilterApiDiffByMemberTargets(diff, fromSurface, toSurface, options);
        if (filtered.TypeDiffs.Count == 0 && candidateTypeDiffs.Count > 0)
            Console.Error.WriteLine($"Note: member filter matched no changed members after type filters: {string.Join(", ", options.MemberFilter)}.");

        return filtered;
    }

    internal static IReadOnlyList<FindingTransitionRow> BuildFindingTransitions(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
    {
        if (options.TypeFilter.Count == 0 && options.MemberFilter.Count == 0)
            throw new InvalidOperationException("Finding Transitions requires --type or a type-qualified --member target.");

        var subject = new FindingSubject("api", "API surface");
        var diffOptions = new ApiDiffOptions(
            options.IncludeAll ? ApiDiffScope.All : ApiDiffScope.Signature);
        if (options.MemberFilter.Count == 0)
        {
            var typeComparison = MetadataFindings.CompareApiTypes(
                fromSurface,
                toSurface,
                subject,
                diffOptions);
            return CompletePairs(typeComparison)
                .Where(pair => options.TypeFilter.Any(filter =>
                    MatchesDiffTypeFilter(TypeTarget(pair), filter)))
                .Select(pair => ToTypeTransitionRow(pair, fromVersion, toVersion))
                .OrderBy(row => row.Target, StringComparer.Ordinal)
                .ToList();
        }

        var targets = ResolveMemberTargetIdentities(
            fromSurface,
            toSurface,
            options.MemberFilter,
            options.TypeFilter);
        var memberComparison = MetadataFindings.CompareApiMembers(
            fromSurface,
            toSurface,
            subject,
            diffOptions);
        return CompletePairs(memberComparison)
            .Where(pair => MatchesMemberPair(pair, targets))
            .Select(pair => ToMemberTransitionRow(pair, fromVersion, toVersion))
            .OrderBy(row => row.Target, StringComparer.Ordinal)
            .ToList();
    }

    internal static IReadOnlyList<FindingTransitionRow> BuildAllocationFindingTransitions(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
    {
        if (options.MemberFilter.Count != 1)
            throw new InvalidOperationException("--finding analysis.allocation requires exactly one --member target.");

        var targets = ResolveMemberTargetIdentities(
            fromSurface,
            toSurface,
            options.MemberFilter,
            options.TypeFilter,
            requireBodyTargets: true,
            bodySectionName: "Finding Transitions");
        var research = ResearchDiff.Compare(
            ResearchDiffInput.FromAssemblies(fromPaths),
            ResearchDiffInput.FromAssemblies(toPaths),
            new ResearchDiffOptions(
                ResearchChangeMechanism.BodySignals,
                TypeFilters: options.TypeFilter,
                MemberTargetIdentities: targets.MemberIdentities)
            {
                RetainAllocationComparisons = true,
            });

        return research.AllocationComparisons
            .SelectMany(comparison => CompletePairs(comparison.Comparison)
                .Select(pair => ToAllocationTransitionRow(
                    comparison.Subject,
                    pair,
                    fromVersion,
                    toVersion)))
            .OrderBy(row => row.Target, StringComparer.Ordinal)
            .ThenBy(row => row.Transition, StringComparer.Ordinal)
            .ToList();
    }

    static IReadOnlyList<PairFinding<T>> CompletePairs<T>(FindingComparison<T> comparison)
        where T : notnull
        => comparison switch
        {
            FindingComparison<T>.Complete complete => complete.Pairs,
            FindingComparison<T>.Failed => throw new InvalidOperationException("Finding comparison did not complete."),
        };

    static bool MatchesMemberPair(
        PairFinding<ApiMemberHandle> pair,
        ResolvedDiffMemberTargets targets)
    {
        var oldHandle = OldSide(pair)?.Payload;
        var newHandle = NewSide(pair)?.Payload;
        var typeName = newHandle?.TypeFullName ?? oldHandle?.TypeFullName;
        return typeName is not null
            && targets.TypeNames.Contains(typeName)
            && (MatchesHandle(oldHandle, targets.MemberIdentities)
                || MatchesHandle(newHandle, targets.MemberIdentities));
    }

    static FindingTransitionRow ToTypeTransitionRow(
        PairFinding<ApiTypeHandle> pair,
        string fromVersion,
        string toVersion)
        => new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            TypeTarget(pair),
            fromVersion,
            toVersion,
            OldSide(pair) is null ? "absent" : "present",
            NewSide(pair) is null ? "absent" : "present",
            pair.Detail);

    static FindingTransitionRow ToMemberTransitionRow(
        PairFinding<ApiMemberHandle> pair,
        string fromVersion,
        string toVersion)
        => new(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            MemberTarget(pair),
            fromVersion,
            toVersion,
            OldSide(pair) is null ? "absent" : "present",
            NewSide(pair) is null ? "absent" : "present",
            pair.Detail);

    static FindingTransitionRow ToAllocationTransitionRow(
        ResearchSubjectKey subject,
        PairFinding<AllocationOccurrence> pair,
        string fromVersion,
        string toVersion)
    {
        var oldFinding = OldSide(pair);
        var newFinding = NewSide(pair);
        return new FindingTransitionRow(
            $"PairFinding.{pair.Kind}",
            pair.Descriptor.Id,
            AllocationTarget(subject, newFinding ?? oldFinding!),
            fromVersion,
            toVersion,
            oldFinding is null ? "absent" : "present",
            newFinding is null ? "absent" : "present",
            pair.Detail ?? newFinding?.Detail ?? oldFinding?.Detail);
    }

    static string AllocationTarget(
        ResearchSubjectKey subject,
        Finding<AllocationOccurrence> finding)
    {
        var occurrence = finding.Payload;
        var allocatedType = occurrence.AllocatedType?.ToQualifiedDisplayString()
            ?? occurrence.RuntimeAllocationType
            ?? occurrence.Detail
            ?? "?";
        return $"{subject.Display} :: {occurrence.Source}/{occurrence.Kind} {allocatedType}";
    }

    static string TypeTarget(PairFinding<ApiTypeHandle> pair)
        => (NewSide(pair) ?? OldSide(pair))!.Payload.TypeFullName;

    static string MemberTarget(PairFinding<ApiMemberHandle> pair)
    {
        var handle = (NewSide(pair) ?? OldSide(pair))!.Payload;
        return $"{handle.TypeFullName}.{handle.StableSelector ?? handle.Identity}";
    }

    static Finding<T>? OldSide<T>(PairFinding<T> pair)
        where T : notnull
        => pair switch
        {
            PairFinding<T>.Added => null,
            PairFinding<T>.Removed removed => removed.Old,
            PairFinding<T>.Present present => present.Old,
            PairFinding<T>.Changed changed => changed.Old,
        };

    static Finding<T>? NewSide<T>(PairFinding<T> pair)
        where T : notnull
        => pair switch
        {
            PairFinding<T>.Added added => added.New,
            PairFinding<T>.Removed => null,
            PairFinding<T>.Present present => present.New,
            PairFinding<T>.Changed changed => changed.New,
        };

    internal static ApiDiff FilterApiDiffByMemberTargets(ApiDiff diff, ApiSurface fromSurface, ApiSurface toSurface, DiffOptions options)
    {
        if (options.MemberFilter.Count == 0)
            return diff;

        var targets = ResolveMemberTargetIdentities(fromSurface, toSurface, options.MemberFilter, options.TypeFilter);
        List<TypeDiff> filtered = [];
        foreach (var typeDiff in diff.TypeDiffs)
        {
            var changes = typeDiff.Changes
                .Where(change => MatchesMemberTarget(typeDiff.TypeFullName, change, targets))
                .ToList();
            if (changes.Count > 0)
                filtered.Add(new TypeDiff(typeDiff.TypeFullName, changes));
        }

        return new ApiDiff
        {
            TypeDiffs = filtered,
            TotalBreaking = filtered.Sum(typeDiff => typeDiff.BreakingCount),
            TotalAdditive = filtered.Sum(typeDiff => typeDiff.AdditiveCount),
            TotalPotentiallyBreaking = filtered.Sum(typeDiff => typeDiff.PotentiallyBreakingCount)
        };
    }

    sealed record ResolvedDiffMemberTargets(
        HashSet<string> MemberIdentities,
        HashSet<string> TypeNames);

    static bool MatchesMemberTarget(string typeFullName, ApiChange change, ResolvedDiffMemberTargets targets)
        => IsMemberChange(change.Kind)
            ? MatchesHandle(change.Subject?.OldMember, targets.MemberIdentities)
              || MatchesHandle(change.Subject?.NewMember, targets.MemberIdentities)
            : IsWholeTypeChange(change.Kind) && targets.TypeNames.Contains(typeFullName);

    static bool MatchesHandle(ApiMemberHandle? handle, IReadOnlySet<string> targetIdentities)
        => handle is not null
           && ((handle.StableSelector is { } stable && targetIdentities.Contains(stable))
               || (handle.CanonicalSignature is { } canonical && targetIdentities.Contains(canonical))
               || targetIdentities.Contains(handle.Identity));

    static ResolvedDiffMemberTargets ResolveMemberTargetIdentities(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> memberTargets,
        IReadOnlyCollection<string> typeFilters,
        bool requireBodyTargets = false,
        string bodySectionName = "Analysis Diff")
    {
        HashSet<string> identities = new(StringComparer.Ordinal);
        HashSet<string> typeNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (var rawTarget in memberTargets)
        {
            var parsed = ParseDiffMemberTarget(rawTarget, fromSurface, toSurface, typeFilters);
            var found = false;
            var bodyFound = false;
            MemberTargetDiagnostic? diagnostic = null;
            MemberTargetDiagnostic? nonFatalDiagnostic = null;
            var oldType = FindExactType(fromSurface, parsed.TypeName);
            var newType = FindExactType(toSurface, parsed.TypeName);

            if (oldType is not null)
            {
                var oldResult = AddResolvedIdentities(oldType, parsed.Selector, identities);
                found |= oldResult.Found;
                bodyFound |= oldResult.BodyFound;
                if (oldResult.Diagnostic is { } oldDiagnostic)
                {
                    if (IsFatalTargetDiagnostic(oldDiagnostic.Kind))
                        diagnostic ??= oldDiagnostic;
                    else
                        nonFatalDiagnostic ??= oldDiagnostic;
                }
                if (oldResult.Found)
                    typeNames.Add(oldType.FullName);
            }
            if (newType is not null)
            {
                var newResult = AddResolvedIdentities(newType, parsed.Selector, identities);
                found |= newResult.Found;
                bodyFound |= newResult.BodyFound;
                if (newResult.Diagnostic is { } newDiagnostic)
                {
                    if (IsFatalTargetDiagnostic(newDiagnostic.Kind))
                        diagnostic ??= newDiagnostic;
                    else
                        nonFatalDiagnostic ??= newDiagnostic;
                }
                if (newResult.Found)
                    typeNames.Add(newType.FullName);
            }

            if (diagnostic is not null)
                throw new InvalidOperationException(diagnostic.Message);
            if (!found)
                throw new InvalidOperationException(nonFatalDiagnostic?.Message ?? $"Member target '{rawTarget}' did not resolve in either diff input.");
            if (requireBodyTargets && !bodyFound)
                throw new InvalidOperationException($"{bodySectionName} --member requires a method-like target; '{rawTarget}' resolved to a member with no method body.");
        }

        return new ResolvedDiffMemberTargets(identities, typeNames);
    }

    static (bool Found, bool BodyFound, MemberTargetDiagnostic? Diagnostic) AddResolvedIdentities(ApiType type, MemberTargetSelector selector, HashSet<string> identities)
    {
        var resolution = MemberTargetResolver.Resolve(type, selector);
        if (!resolution.Found)
            return (false, false, resolution.Diagnostic);

        identities.Add(resolution.Target!.Anchor.StableSelector);
        identities.Add(resolution.Target.Anchor.CanonicalSignature);
        var bodyFound = AddResearchBodyIdentity(resolution.Target, identities);
        return (true, bodyFound, null);
    }

    internal static bool AddResearchBodyIdentity(ResolvedMemberTarget target, HashSet<string> identities)
        => ResearchMemberIdentity.TryAddTargetIdentity(target, identities);

    static bool IsFatalTargetDiagnostic(MemberTargetDiagnosticKind kind)
        => kind is MemberTargetDiagnosticKind.AmbiguousMember
            or MemberTargetDiagnosticKind.DigestAmbiguous
            or MemberTargetDiagnosticKind.ConflictingSelectors;

    sealed record ParsedDiffMemberTarget(string TypeName, MemberTargetSelector Selector);

    static ParsedDiffMemberTarget ParseDiffMemberTarget(
        string rawTarget,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> typeFilters)
    {
        if (TrySplitTypeQualifiedMemberTarget(rawTarget, fromSurface, toSurface, out var typeName, out var memberSelector))
            return new ParsedDiffMemberTarget(typeName, MemberTargetSelector.Parse(memberSelector));

        var typeContext = ResolveTypeContext(fromSurface, toSurface, typeFilters, out var contextError);
        if (contextError is { Length: > 0 })
            throw new InvalidOperationException(contextError);
        if (typeContext is null)
            throw new InvalidOperationException($"--member '{rawTarget}' requires exactly one --type filter or a type-qualified selector.");

        return new ParsedDiffMemberTarget(typeContext, MemberTargetSelector.Parse(rawTarget));
    }

    static bool TrySplitTypeQualifiedMemberTarget(
        string rawTarget,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        out string typeName,
        out string memberSelector)
    {
        foreach (var marker in (ReadOnlySpan<string>)[".operator:", ".explicit:", ".extension:"])
        {
            var markerIndex = rawTarget.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0)
            {
                var candidate = rawTarget[..markerIndex];
                if (TryFindSingleType(fromSurface, toSurface, candidate, out typeName, out _))
                {
                    memberSelector = rawTarget[(markerIndex + 1)..];
                    return true;
                }
            }
        }

        foreach (var dot in TopLevelDotPositionsFromRight(rawTarget))
        {
            var candidate = rawTarget[..dot];
            if (TryFindSingleType(fromSurface, toSurface, candidate, out typeName, out _))
            {
                memberSelector = rawTarget[(dot + 1)..];
                return true;
            }
        }

        typeName = "";
        memberSelector = rawTarget;
        return false;
    }

    static IEnumerable<int> TopLevelDotPositionsFromRight(string value)
    {
        var depth = 0;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var ch = value[i];
            if (ch == '>')
                depth++;
            else if (ch == '<')
                depth--;
            else if (ch == '.' && depth == 0)
                yield return i;
        }
    }

    static string? ResolveTypeContext(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyCollection<string> typeFilters,
        out string? error)
    {
        error = null;
        if (typeFilters.Count != 1)
            return null;

        var query = typeFilters.First();
        if (TryFindSingleType(fromSurface, toSurface, query, out var typeName, out error))
            return typeName;

        return null;
    }

    static bool TryFindSingleType(ApiSurface fromSurface, ApiSurface toSurface, string query, out string typeName, out string? error)
    {
        error = null;
        var matches = FindTypes(fromSurface, query)
            .Concat(FindTypes(toSurface, query))
            .Select(type => type.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (matches.Count == 0)
        {
            typeName = "";
            return false;
        }

        if (matches.Count > 1)
        {
            typeName = "";
            error = $"Type target '{query}' is ambiguous. Use one of: {string.Join(", ", matches)}.";
            return false;
        }

        typeName = matches[0];
        return true;
    }

    static IEnumerable<ApiType> FindTypes(ApiSurface surface, string query)
    {
        foreach (var type in surface.Types)
            if (TypeMatcher.MatchesTypeFilter(type.FullName, query))
                yield return type;
    }

    static ApiType? FindExactType(ApiSurface surface, string fullName)
        => surface.Types.FirstOrDefault(type => type.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));

    static bool IsMemberChange(ChangeKind kind)
        => kind is ChangeKind.MemberAdded or ChangeKind.MemberRemoved or ChangeKind.MemberSignatureChanged
            or ChangeKind.VirtualRemoved or ChangeKind.AbstractMemberAdded or ChangeKind.EnumValueChanged
            or ChangeKind.MemberAttributeAdded or ChangeKind.MemberAttributeRemoved;

    static bool IsWholeTypeChange(ChangeKind kind)
        => kind is ChangeKind.TypeAdded or ChangeKind.TypeRemoved;

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
    public HashSet<string> MemberFilter { get; init; } = [];
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
    public string? Finding { get; init; }
    public bool Legend { get; init; }
    public string[]? Discover { get; init; }
    public bool Tree { get; init; }
    public string[]? Select { get; init; }
    public HashSet<string>? IncludeSections { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public int? Rows { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public bool IsRawOutput => OneLine || Jsonl || NoHeader || NameOnly;
}
