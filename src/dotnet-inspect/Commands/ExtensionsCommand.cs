using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using ILInspector.Findings;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Finds extension methods for a target type across packages, assemblies, and platform frameworks.
/// </summary>
public class ExtensionsCommand
{
    public static async Task<int> ExecuteAsync(ExtensionsOptions options)
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

            var results = await ScanExtensionsAsync(options, context, logger, targetType);

            // Apply limit
            if (options.Limit.HasValue && results.Count > options.Limit.Value)
            {
                results = results.Take(options.Limit.Value).ToList();
            }

            // Collapse overloads into single entries
            results = CollapseOverloads(results);

            if (results.Count == 0)
                NamespacePrefixHints.WriteIfLikelyNamespacePrefix(targetType);

            // Output results
            // --count reduces the payload, so it is resolved before the format flags that
            // render it. Ordering these the other way lets --json answer a count request
            // with the full unprojected result set.
            if (options.Count)
            {
                if (!WriteCount(targetType, results, options))
                    return 1;
            }
            else if (options.JsonOutput)
            {
                WriteJsonOutput(results, options.CompactJson);
            }
            else if (options.Tabular || options.Tsv || options.Jsonl || options.NoHeader)
            {
                WriteTableOutput(targetType, results, options);
            }
            else
            {
                WriteMarkoutOutput(targetType, results, options.Verbosity, options.Rows);
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
        string targetType)
    {
        using var assemblySet = await AssemblySetResolver.CollectAsync(
            context.HttpClient,
            options.ToAssemblySetRequest("inspect-ext"),
            logger.Log);
        AssemblySetDiagnosticWriter.Write(assemblySet);

        List<ExtensionMethodResult> results = [];
        var censuses = new List<ExtensionAssemblyCensus>(
            assemblySet.Assemblies.Count);
        ImmutableArray<ExtensionReachableTypePath> reachableTypes = [];
        using var workspace = new AssemblySetInspectionWorkspace();
        if (options.Reachable)
        {
            workspace.RunGroup(
                assemblySet,
                (group, entries) =>
                {
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
                    AssemblyContextResult<
                        ImmutableArray<ExtensionMethodInfo>> extensionMethods =
                        queryResults.Get(
                            AssemblyContextExtensionMethodsQuery.Definition);
                    foreach (AssemblyContextEntry<
                        ImmutableArray<ExtensionMethodInfo>> entry
                        in extensionMethods.Assemblies)
                    {
                        censuses.Add(
                            CreateExtensionCensus(
                                entries.EntryFor(entry.Subject),
                                entry));
                    }

                    AssemblyContextExtensionReachabilityResult reachability =
                        queryResults.Get(
                            AssemblyContextExtensionReachabilityQuery.Definition);
                    WriteReachabilityFailures(reachability, entries);
                    reachableTypes = reachability.ReachableTypes;
                },
                (assembly, failure) =>
                    censuses.Add(
                        FailedExtensionCensus(
                            assembly,
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
                    censuses.Add(CreateExtensionCensus(assembly, entry)),
                (assembly, failure) =>
                    censuses.Add(
                        FailedExtensionCensus(
                            assembly,
                            failure)));
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

    internal static void WriteReachabilityFailures(
        AssemblyContextExtensionReachabilityResult reachability,
        AssemblyContextEntryMap entries)
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
                        + $"{entries.EntryFor(entry.Subject).Path}: "
                        + rejected.Failure.Detail);
                    break;
                case AssemblyContextEntry<
                    ImmutableArray<
                        ExtensionReachabilityType>>.Failed failed:
                    CommandError.WriteWarning(
                        $"Extension reachability inspection failed for "
                        + $"{entries.EntryFor(entry.Subject).Path}: "
                        + failed.Error.Message);
                    break;
            }
        }
    }

    private static ExtensionAssemblyCensus CreateExtensionCensus(
        AssemblySetEntry assembly,
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
        AssemblySetEntry assembly,
        IReadOnlyList<ExtensionMethodInfo> members)
        => new(
            assembly,
            members,
            MetadataFindings.InspectExtensionMembers(
                members,
                ExtensionSubject(assembly.Path)));

    private static ExtensionAssemblyCensus FailedExtensionCensus(
        AssemblySetEntry assembly,
        string reason)
        => new(
            assembly,
            [],
            new FindingInspection<ExtensionMemberObservation>.Failed(
                new InspectionError(
                    ExtensionSubject(assembly.Path),
                    MetadataFindings.ExtensionMemberDescriptor,
                    reason)));

    private static FindingSubject ExtensionSubject(string path)
        => new(
            Path.GetFullPath(path),
            Path.GetFileName(path));

    internal sealed record ExtensionAssemblyCensus(
        AssemblySetEntry Assembly,
        IReadOnlyList<ExtensionMethodInfo> Members,
        FindingInspection<ExtensionMemberObservation> Inspection);

    internal static ExtensionAssemblyCensus InspectExtensionAssembly(
        AssemblySetEntry assembly,
        bool includeAll)
    {
        try
        {
            using var session = AssemblyInspectionSession.Open(assembly.Path);
            var members = session.ExtensionMethods(includeAll).ToList();
            return AvailableExtensionCensus(assembly, members);
        }
        catch (Exception ex)
        {
            return FailedExtensionCensus(assembly, ex.Message);
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
                    $"Extension member census for {census.Assembly.Path} does not correspond to its scanner inventory.");
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
                Assembly = Path.GetFileNameWithoutExtension(census.Assembly.Path),
                Signature = member.Signature,
                Kind = observation.Kind == ExtensionMemberKind.Method ? "method" : "property",
                Source = census.Assembly.Source,
                SourceVersion = census.Assembly.Version,
                ReachablePath = reachablePath,
                ReachableFromType = reachableFromType,
            });
        }

        return results;
    }

    private static void WriteJsonOutput(List<ExtensionMethodResult> results, bool compact)
    {
        var jsonResults = results.Select(ExtensionMethodJsonResult.From).ToList();
        JsonOutputHelper.Write(jsonResults, ExtensionsJsonContext.Default.ListExtensionMethodJsonResult,
            ExtensionsCompactJsonContext.Default.ListExtensionMethodJsonResult, compact);
    }

    private static bool WriteCount(
        string targetType,
        List<ExtensionMethodResult> results,
        ExtensionsOptions options)
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
            options.Rows);
    }

    private static void WriteMarkoutOutput(string targetType, List<ExtensionMethodResult> results, Verbosity verbosity, RowWindow? rows)
    {
        var view = ExtensionsOutputFormatter.BuildView(targetType, results, verbosity);
        OutputFormatter.WriteWindowedMarkdown(Console.Out, rows,
            opts => MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts));
    }

    private static void WriteTableOutput(string targetType, List<ExtensionMethodResult> results, ExtensionsOptions options)
    {
        var view = ExtensionsOutputFormatter.BuildView(targetType, results, options.Verbosity);
        OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
            options.Columns, options.Fields,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
            options.Rows);
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
}
