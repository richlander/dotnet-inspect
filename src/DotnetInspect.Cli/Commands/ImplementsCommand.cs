using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
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
/// Finds types that implement an interface or extend a base class.
/// </summary>
public class ImplementsCommand
{
    public static async Task<int> ExecuteAsync(
        ImplementsOptions options,
        CancellationToken cancellationToken = default)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var targetType = options.TargetType;

        try
        {
            // Discovery mode: -D/--discover lists schema
            if (options.Discover != null)
            {
                var schema = new DocumentSchema()
                    .Add("Implementers", "column", "Type", "Kind", "Relationship", "Library", "Source");
                return DiscoverOutput.Execute(options.Discover, schema,
                    tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl,
                    projection: options);
            }

            // Safety fallback — default to all platform frameworks
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            var results = new List<ImplementerResult>();
            AssemblySetRequest request =
                options.ToAssemblySetRequest("inspect-impl");
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
                        AssemblyContextResult<
                            ImmutableArray<TypeRelationship>>>? execution =
                            await configured.QuerySurfaceAsync(
                                queryContext =>
                                    AssemblyContextImplementersQuery.Execute(
                                        queryContext.Group,
                                        targetType,
                                        options.IncludeAll),
                                cancellationToken);
                    if (execution?.Sources is { } sources
                        && execution.Result is { } queryResult)
                    {
                        logger.Log(
                            $"Scanning {sources.AssemblyCount} "
                            + $"libraries for types implementing {targetType}");
                        foreach (AssemblyContextEntry<
                            ImmutableArray<TypeRelationship>> entry
                            in queryResult.Assemblies)
                        {
                            AddImplementers(
                                results,
                                sources.SourceFor(entry.Subject),
                                entry);
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
                logger.Log(
                    $"Scanning {assemblySet.Assemblies.Count} libraries "
                    + $"for types implementing {targetType}");

                using var workspace =
                    new AssemblySetInspectionWorkspace();
                workspace.RunPerAssembly(
                    assemblySet,
                    AssemblyContextImplementersQuery.Definition,
                    group => AssemblyContextImplementersQuery.Execute(
                        group,
                        targetType,
                        options.IncludeAll),
                    (assembly, entry) =>
                        AddImplementers(
                            results,
                            SearchAssemblySource.FromAssemblySet(assembly),
                            entry),
                    (assembly, failure) =>
                        CommandError.WriteWarning(
                            $"Error scanning {assembly.Path}: {failure}"));
            }

            // Deduplicate by type name + source (same type from multiple TFM folders)
            results = results
                .GroupBy(r => (r.TypeName, r.Source))
                .Select(g => g.First())
                .ToList();

            if (options.RowSelection is not null)
            {
                results = results
                    .OrderBy(r => r.TypeName, StringComparer.Ordinal)
                    .ThenBy(r => r.Source, StringComparer.Ordinal)
                    .ThenBy(r => r.SourceVersion, StringComparer.Ordinal)
                    .ToList();
            }

            // Apply limit
            if (options.Limit.HasValue && results.Count > options.Limit.Value)
            {
                results = results.Take(options.Limit.Value).ToList();
            }

            RowWindow? outputRows = options.RowSelection is null ? options.Rows : null;
            if (options.RowSelection is { } rowSelection)
            {
                RowsCohortResult<string, ImplementerResult> selected =
                    RowsCohortExecutor.ApplyUnordered(
                        [
                            RowsCohortSequence<string, ImplementerResult>.Create(
                                "Implementers",
                                results)
                        ],
                        rowSelection);
                if (!selected.IsSuccess)
                {
                    RowsCohortSemanticFailure<string> failure =
                        selected.Failure!;
                    CommandError.Write(
                        $"Implementers row selection stage "
                        + $"{failure.Failure.StageNumber} requires row "
                        + $"{failure.Failure.RequiredPosition}, but only "
                        + $"{failure.Failure.AvailableCount} implementer rows "
                        + "are available.");
                    return 1;
                }

                results = selected.RowSets[0].Values.ToList();
            }
            else if (outputRows is { } legacyRows)
            {
                results = RowWindow.Apply(legacyRows, results).ToList();
                outputRows = null;
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
            else
            {
                WriteMarkoutOutput(targetType, results, options.Tabular, options.Tsv, options.Jsonl, options.NoHeader, options.Columns, options.Fields, outputRows);
            }

            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    private static void AddImplementers(
        List<ImplementerResult> results,
        SearchAssemblySource assembly,
        AssemblyContextEntry<ImmutableArray<TypeRelationship>> entry)
    {
        switch (entry)
        {
            case AssemblyContextEntry<
                ImmutableArray<TypeRelationship>>.Available available:
                foreach (TypeRelationship relationship
                    in available.Value)
                {
                    results.Add(new ImplementerResult
                    {
                        TypeName = relationship.TypeName,
                        Namespace = relationship.Namespace,
                        Kind = relationship.Kind,
                        Relationship = relationship.RelationshipKind
                            .ToString()
                            .ToLowerInvariant(),
                        Assembly = assembly.Library,
                        Source = assembly.Source,
                        SourceVersion = assembly.SourceVersion,
                    });
                }
                break;
            case AssemblyContextEntry<
                ImmutableArray<TypeRelationship>>.Rejected rejected:
                CommandError.WriteWarning(
                    $"Error scanning {assembly.DiagnosticSubject}: "
                    + rejected.Failure.Detail);
                break;
            case AssemblyContextEntry<
                ImmutableArray<TypeRelationship>>.Failed failed:
                CommandError.WriteWarning(
                    $"Error scanning {assembly.DiagnosticSubject}: "
                    + failed.Error.Message);
                break;
        }
    }

    private static void WriteJsonOutput(List<ImplementerResult> results, bool compact)
    {
        var jsonResults = results.Select(ImplementerJsonResult.From).ToList();
        JsonOutputHelper.Write(jsonResults, ImplementsJsonContext.Default.ListImplementerJsonResult,
            ImplementsCompactJsonContext.Default.ListImplementerJsonResult, compact);
    }

    private static bool WriteCount(
        string targetType,
        List<ImplementerResult> results,
        ImplementsOptions options,
        RowWindow? rows)
    {
        var view = ImplementsOutputFormatter.BuildView(targetType, results);
        return CountOutput.TryWriteProjected(
            view,
            SearchViewContext.Default,
            "Implementers",
            options.Columns,
            options.Fields,
            rows);
    }

    private static void WriteMarkoutOutput(string targetType, List<ImplementerResult> results, bool tabular, bool tsv, bool jsonl, bool noHeader, string[]? columns, string[]? fields, RowWindow? rows)
    {
        var view = ImplementsOutputFormatter.BuildView(targetType, results);

        if (view.Rows == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        if (tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !noHeader, tsv, jsonl, columns, fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                rows);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, rows,
                opts => MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts));
        }
    }
}
