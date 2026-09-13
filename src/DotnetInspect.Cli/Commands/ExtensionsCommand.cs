using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using Inspector.Findings;
using ILInspector.Metadata;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Finds extension methods for a target type across packages, assemblies, and platform frameworks.
/// </summary>
public class ExtensionsCommand
{
    public static async Task<int> ExecuteAsync(
        ExtensionsOptions options,
        CancellationToken cancellationToken = default)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var targetType = options.TargetType;

        try
        {
            // Safety fallback — default to all platform frameworks
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            var results = await ScanExtensionsAsync(
                options,
                context,
                logger,
                targetType,
                cancellationToken);

            bool hasSemanticRowSelection =
                options.RowSelection?.Operations.Count > 0;
            if (hasSemanticRowSelection)
            {
                results = results
                    .OrderBy(r => r.ReachablePath ?? "", StringComparer.Ordinal)
                    .ThenBy(r => r.ExtensionClass, StringComparer.Ordinal)
                    .ThenBy(r => r.MethodName, StringComparer.Ordinal)
                    .ThenBy(r => r.Kind, StringComparer.Ordinal)
                    .ThenBy(r => r.Assembly, StringComparer.Ordinal)
                    .ThenBy(r => r.Source, StringComparer.Ordinal)
                    .ThenBy(r => r.SourceVersion, StringComparer.Ordinal)
                    .ThenBy(r => r.Signature, StringComparer.Ordinal)
                    .ToList();
            }

            // Apply the existing work limit before overload collapse.
            if (options.Limit.HasValue && results.Count > options.Limit.Value)
            {
                results = results.Take(options.Limit.Value).ToList();
            }

            // Collapse overloads into single entries
            results = CollapseOverloads(results);

            RowWindow? outputRows =
                hasSemanticRowSelection ? null : options.Rows;
            if (hasSemanticRowSelection
                && options.RowSelection is { } rowSelection)
            {
                RowsCohortResult<string, ExtensionMethodResult> selected =
                    RowsCohortExecutor.ApplyUnordered(
                        [
                            RowsCohortSequence<string, ExtensionMethodResult>.Create(
                                "Extensions",
                                results)
                        ],
                        rowSelection);
                if (!selected.IsSuccess)
                {
                    RowsCohortSemanticFailure<string> failure =
                        selected.Failure!;
                    CommandError.Write(
                        $"Extensions row selection stage "
                        + $"{failure.Failure.StageNumber} requires row "
                        + $"{failure.Failure.RequiredPosition}, but only "
                        + $"{failure.Failure.AvailableCount} extension rows "
                        + "are available.");
                    return 1;
                }

                results = selected.RowSets[0].Values.ToList();
            }

            if (results.Count == 0)
                NamespacePrefixHints.WriteIfLikelyNamespacePrefix(targetType);

            // Output results
            // --count reduces the payload, so it is resolved before the format flags that
            // render it. Ordering these the other way lets --json answer a count request
            // with the full unprojected result set.
            if (options.Count)
            {
                if (!WriteCount(targetType, results, options, outputRows))
                    return 1;
            }
            else if (options.JsonOutput)
            {
                if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                    return 1;

                WriteJsonOutput(results, options.CompactJson);
            }
            else if (options.Tabular || options.Tsv || options.Jsonl || options.NoHeader)
            {
                WriteTableOutput(targetType, results, options, outputRows);
            }
            else
            {
                WriteMarkoutOutput(targetType, results, options.Verbosity, outputRows);
            }

            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    private static async Task<List<ExtensionMethodResult>> ScanExtensionsAsync(
        ExtensionsOptions options,
        CommandContext context,
        VerboseLogger logger,
        string targetType,
        CancellationToken cancellationToken)
    {
        List<ExtensionMethodResult> results = [];
        var censuses = new List<ExtensionAssemblyCensus>();
        ImmutableArray<ExtensionReachableTypePath> reachableTypes = [];
        AssemblySetRequest request =
            options.ToAssemblySetRequest("inspect-ext");
        if (ConfiguredPackageSearchWorkspace.IsEligible(
                options.SourceSelection,
                request,
                options.Tfm))
        {
            await using ConfiguredPackageSearchWorkspace? configured =
                await ConfiguredPackageSearchWorkspace.OpenAsync(
                    context.HttpClient,
                    request,
                    options.Tfm!,
                    logger.Log,
                    cancellationToken);
            if (configured is not null)
            {
                ConfiguredPackageSearchQueryResult<
                    ExtensionSearchQueryResult>? execution =
                        await configured.QuerySurfaceAsync(
                            queryContext =>
                                ExecuteQueries(
                                    queryContext.Group,
                                    options,
                                    targetType),
                            cancellationToken);
                if (execution?.Sources is { } sources
                    && execution.Result is { } queryResult)
                {
                    foreach (AssemblyContextEntry<
                        ImmutableArray<ExtensionMethodInfo>> extensionMethods
                        in queryResult.ExtensionMethods.Assemblies)
                    {
                        censuses.Add(
                            CreateExtensionCensus(
                                sources.SourceFor(
                                    extensionMethods.Subject),
                                extensionMethods));
                    }
                    if (queryResult.Reachability is { } reachability)
                    {
                        WriteReachabilityFailures(
                            reachability,
                            sources.SourceFor);
                        reachableTypes = reachability.ReachableTypes;
                    }
                }
            }
        }
        else
        {
            using var assemblySet =
                await AssemblySetResolver.CollectAsync(
                    context.HttpClient,
                    request,
                    logger.Log);
            AssemblySetDiagnosticWriter.Write(assemblySet);

            using var workspace =
                new AssemblySetInspectionWorkspace();
            if (options.Reachable)
            {
                workspace.RunGroup(
                    assemblySet,
                    (group, entries) =>
                    {
                        ExtensionSearchQueryResult queryResult =
                            ExecuteQueries(group, options, targetType);
                        foreach (AssemblyContextEntry<
                            ImmutableArray<ExtensionMethodInfo>> entry
                            in queryResult.ExtensionMethods.Assemblies)
                        {
                            censuses.Add(
                                CreateExtensionCensus(
                                    SearchAssemblySource.FromAssemblySet(
                                        entries.EntryFor(entry.Subject)),
                                    entry));
                        }

                        WriteReachabilityFailures(
                            queryResult.Reachability!,
                            subject =>
                                SearchAssemblySource.FromAssemblySet(
                                    entries.EntryFor(subject)));
                        reachableTypes =
                            queryResult.Reachability!.ReachableTypes;
                    },
                    (assembly, failure) =>
                        censuses.Add(
                            FailedExtensionCensus(
                                SearchAssemblySource.FromAssemblySet(
                                    assembly),
                                failure)));
            }
            else
            {
                workspace.RunPerAssembly(
                    assemblySet,
                    AssemblyContextExtensionMethodsQuery.Definition,
                    group => AssemblyContextExtensionMethodsQuery.Execute(
                        group,
                        options.IncludeAll),
                    (assembly, entry) =>
                        censuses.Add(
                            CreateExtensionCensus(
                                SearchAssemblySource.FromAssemblySet(
                                    assembly),
                                entry)),
                    (assembly, failure) =>
                        censuses.Add(
                            FailedExtensionCensus(
                                SearchAssemblySource.FromAssemblySet(
                                    assembly),
                                failure)));
            }
        }

        var availableCensuses = new List<ExtensionAssemblyCensus>(censuses.Count);

        foreach (var census in censuses)
        {
            if (census.Inspection.Failure() is { } failure)
            {
                CommandError.WriteWarning(
                    $"Extension member inspection failed for {failure.Subject.Display}: {failure.Reason}");
                continue;
            }

            availableCensuses.Add(census);
            results.AddRange(ProjectExtensions(census, targetType));
        }

        foreach (ExtensionReachableTypePath reachable
            in reachableTypes)
        {
            foreach (var census in availableCensuses)
            {
                results.AddRange(ProjectExtensions(
                    census,
                    reachable.Type,
                    reachablePath: reachable.Path,
                    reachableFromType: reachable.Type));
            }
        }

        return results;
    }

    private static ExtensionSearchQueryResult ExecuteQueries(
        AssemblyContextGroup group,
        ExtensionsOptions options,
        string targetType)
    {
        if (!options.Reachable)
        {
            return new(
                AssemblyContextExtensionMethodsQuery.Execute(
                    group,
                    options.IncludeAll),
                Reachability: null);
        }

        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(
                    AssemblyContextExtensionMethodsQuery.Definition,
                    contextGroup =>
                        AssemblyContextExtensionMethodsQuery.Execute(
                            contextGroup,
                            options.IncludeAll))
                .Add(
                    AssemblyContextExtensionReachabilityQuery.Definition,
                    contextGroup =>
                        AssemblyContextExtensionReachabilityQuery.Execute(
                            contextGroup,
                            targetType,
                            options.Depth));
        InspectionQueryResults queryResults = registry.Run(
            [
                AssemblyContextExtensionMethodsQuery.Definition,
                AssemblyContextExtensionReachabilityQuery.Definition,
            ],
            group);
        return new(
            queryResults.Get(
                AssemblyContextExtensionMethodsQuery.Definition),
            queryResults.Get(
                AssemblyContextExtensionReachabilityQuery.Definition));
    }

    internal static void WriteReachabilityFailures(
        AssemblyContextExtensionReachabilityResult reachability,
        Func<AssemblyContextSubject, SearchAssemblySource> sourceFor)
    {
        foreach (AssemblyContextEntry<
            ImmutableArray<ExtensionReachabilityType>> entry
            in reachability.TypeInventories.Assemblies)
        {
            switch (entry)
            {
                case AssemblyContextEntry<
                    ImmutableArray<
                        ExtensionReachabilityType>>.Rejected rejected:
                    CommandError.WriteWarning(
                        $"Extension reachability inspection failed for "
                        + $"{sourceFor(entry.Subject).DiagnosticSubject}: "
                        + rejected.Failure.Detail);
                    break;
                case AssemblyContextEntry<
                    ImmutableArray<
                        ExtensionReachabilityType>>.Failed failed:
                    CommandError.WriteWarning(
                        $"Extension reachability inspection failed for "
                        + $"{sourceFor(entry.Subject).DiagnosticSubject}: "
                        + failed.Error.Message);
                    break;
            }
        }
    }

    internal static void WriteReachabilityFailures(
        AssemblyContextExtensionReachabilityResult reachability,
        AssemblyContextEntryMap entries) =>
        WriteReachabilityFailures(
            reachability,
            subject =>
                SearchAssemblySource.FromAssemblySet(
                    entries.EntryFor(subject)));

    private static ExtensionAssemblyCensus CreateExtensionCensus(
        SearchAssemblySource assembly,
        AssemblyContextEntry<ImmutableArray<ExtensionMethodInfo>> entry)
        => entry switch
        {
            AssemblyContextEntry<
                ImmutableArray<ExtensionMethodInfo>>.Available available =>
                AvailableExtensionCensus(assembly, available.Value),
            AssemblyContextEntry<
                ImmutableArray<ExtensionMethodInfo>>.Rejected rejected =>
                FailedExtensionCensus(
                    assembly,
                    rejected.Failure.Detail),
            AssemblyContextEntry<
                ImmutableArray<ExtensionMethodInfo>>.Failed failed =>
                FailedExtensionCensus(
                    assembly,
                    failed.Error.Message),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context extension result."),
        };

    private static ExtensionAssemblyCensus AvailableExtensionCensus(
        SearchAssemblySource assembly,
        IReadOnlyList<ExtensionMethodInfo> members)
        => new(
            assembly,
            members,
            MetadataFindings.InspectExtensionMembers(
                members,
                assembly.FindingSubject));

    private static ExtensionAssemblyCensus FailedExtensionCensus(
        SearchAssemblySource assembly,
        string reason)
        => new(
            assembly,
            [],
            new FindingInspection<ExtensionMemberObservation>.Failed(
                new InspectionError(
                    assembly.FindingSubject,
                    MetadataFindings.ExtensionMemberDescriptor,
                    reason)));

    internal sealed record ExtensionAssemblyCensus(
        SearchAssemblySource Assembly,
        IReadOnlyList<ExtensionMethodInfo> Members,
        FindingInspection<ExtensionMemberObservation> Inspection)
    {
        internal ExtensionAssemblyCensus(
            AssemblySetEntry assembly,
            IReadOnlyList<ExtensionMethodInfo> members,
            FindingInspection<ExtensionMemberObservation> inspection)
            : this(
                SearchAssemblySource.FromAssemblySet(assembly),
                members,
                inspection)
        {
        }
    }

    internal static ExtensionAssemblyCensus InspectExtensionAssembly(
        AssemblySetEntry assembly,
        bool includeAll)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(assembly.Path);
            var members = session.ExtensionMethods(includeAll).ToList();
            return AvailableExtensionCensus(
                SearchAssemblySource.FromAssemblySet(assembly),
                members);
        }
        catch (Exception ex)
        {
            return FailedExtensionCensus(
                SearchAssemblySource.FromAssemblySet(assembly),
                ex.Message);
        }
    }

    internal static List<ExtensionMethodResult> ProjectExtensions(
        ExtensionAssemblyCensus census,
        string targetType,
        string? reachablePath = null,
        string? reachableFromType = null)
    {
        var observationsByAnchor = census.Inspection.Findings()
            .ToLookup(
                static finding => finding.Payload.Anchor,
                static finding => finding.Payload);
        var normalizedTarget = FqnParser.NormalizeTypeName(targetType);
        List<ExtensionMethodResult> results = [];

        foreach (var member in census.Members)
        {
            var expectedKind = member.Kind == "method"
                ? ExtensionMemberKind.Method
                : ExtensionMemberKind.Property;
            var observation = member.Anchor is { } anchor
                ? observationsByAnchor[anchor].FirstOrDefault(candidate =>
                    candidate.Kind == expectedKind
                    && string.Equals(
                        candidate.ExtendedType,
                        member.CanonicalExtendedType,
                        StringComparison.Ordinal)
                    && string.Equals(
                        candidate.ReturnType,
                        member.ReturnType,
                        StringComparison.Ordinal)
                    && string.Equals(
                        candidate.Assembly,
                        member.Assembly,
                        StringComparison.Ordinal))
                : null;
            if (observation is null)
            {
                throw new InvalidOperationException(
                    $"Extension member census for "
                    + $"{census.Assembly.DiagnosticSubject} does not "
                    + "correspond to its scanner inventory.");
            }

            if (!TypeMatcher.Matches(
                    FqnParser.NormalizeTypeName(member.ExtendedType),
                    normalizedTarget))
            {
                continue;
            }

            results.Add(new ExtensionMethodResult
            {
                MethodName = member.MethodName,
                ExtensionClass = member.ExtensionClass,
                ExtendedType = member.ExtendedType,
                Assembly = census.Assembly.Library,
                Signature = member.Signature,
                Kind = observation.Kind == ExtensionMemberKind.Method ? "method" : "property",
                Source = census.Assembly.Source,
                SourceVersion = census.Assembly.SourceVersion,
                ReachablePath = reachablePath,
                ReachableFromType = reachableFromType,
            });
        }

        return results;
    }

    private static void WriteJsonOutput(List<ExtensionMethodResult> results, bool compact)
    {
        // The established public JSON contract is a typed array with numeric overload
        // counts and signatures. The output-shapes design records this compatibility
        // exception because the lowered Markout formatter intentionally emits section
        // objects with string cells.
        var jsonResults = results.Select(ExtensionMethodJsonResult.From).ToList();
        JsonOutputHelper.Write(jsonResults, ExtensionsJsonContext.Default.ListExtensionMethodJsonResult,
            ExtensionsCompactJsonContext.Default.ListExtensionMethodJsonResult, compact);
    }

    private static bool WriteCount(
        string targetType,
        List<ExtensionMethodResult> results,
        ExtensionsOptions options,
        RowWindow? rows)
    {
        var view = ExtensionsOutputFormatter.BuildView(
            targetType,
            results);
        return CountOutput.TryWriteProjected(
            view,
            SearchViewContext.Default,
            "Extensions",
            options.Columns,
            options.Fields,
            rows);
    }

    private static void WriteMarkoutOutput(string targetType, List<ExtensionMethodResult> results, Verbosity verbosity, RowWindow? rows)
    {
        var view = ExtensionsOutputFormatter.BuildView(targetType, results, verbosity);
        OutputFormatter.WriteWindowedMarkdown(Console.Out, rows,
            opts => MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts));
    }

    private static void WriteTableOutput(
        string targetType,
        List<ExtensionMethodResult> results,
        ExtensionsOptions options,
        RowWindow? rows)
    {
        var view = ExtensionsOutputFormatter.BuildView(targetType, results, options.Verbosity);
        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
            options.Columns, options.Fields,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
            rows);
    }

    /// <summary>
    /// Collapses method overloads into a single result with an overload count and signatures list.
    /// </summary>
    internal static List<ExtensionMethodResult> CollapseOverloads(List<ExtensionMethodResult> results)
    {
        return results
            .GroupBy(r => (r.MethodName, r.Kind, r.ExtensionClass, r.Assembly, r.Source, r.SourceVersion, r.ReachablePath, r.ReachableFromType))
            .Select(g =>
            {
                var first = g.First();
                var signatures = g.Select(r => r.Signature).Where(s => s != null).Distinct().Cast<string>().ToList();
                var count = signatures.Count > 0 ? signatures.Count : g.Count();
                return first with
                {
                    Overloads = count > 1 ? count : null,
                    Signatures = signatures.Count > 1 ? signatures : null,
                    Signature = signatures.Count == 1 ? signatures[0] : null
                };
            })
            .ToList();
    }

    private sealed record ExtensionSearchQueryResult(
        AssemblyContextResult<ImmutableArray<ExtensionMethodInfo>>
            ExtensionMethods,
        AssemblyContextExtensionReachabilityResult? Reachability);
}
