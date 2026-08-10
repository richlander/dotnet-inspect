using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Finds types that implement an interface or extend a base class.
/// </summary>
public class ImplementsCommand
{
    public static async Task<int> ExecuteAsync(ImplementsOptions options)
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

            using var assemblySet = await AssemblySetResolver.CollectAsync(
                context.HttpClient,
                options.ToAssemblySetRequest("inspect-impl"),
                logger.Log);
            AssemblySetDiagnosticWriter.Write(assemblySet);
            logger.Log($"Scanning {assemblySet.Assemblies.Count} libraries for types implementing {targetType}");

            var results = new List<ImplementerResult>();
            using var workspace = new AssemblySetInspectionWorkspace();
            workspace.RunPerAssembly(
                assemblySet,
                AssemblyContextImplementersQuery.Definition,
                group => AssemblyContextImplementersQuery.Execute(
                    group,
                    targetType,
                    options.IncludeAll),
                (assembly, entry) =>
                    AddImplementers(results, assembly, entry, logger),
                (assembly, failure) =>
                    logger.LogWarning(
                        $"Error scanning {assembly.Path}: {failure}"));

            // Deduplicate by type name + source (same type from multiple TFM folders)
            results = results
                .GroupBy(r => (r.TypeName, r.Source))
                .Select(g => g.First())
                .ToList();

            // Apply limit
            if (options.Limit.HasValue && results.Count > options.Limit.Value)
            {
                results = results.Take(options.Limit.Value).ToList();
            }

            if (results.Count == 0)
                NamespacePrefixHints.WriteIfLikelyNamespacePrefix(targetType);

            // Output results
            // --count reduces the payload, so it is resolved before the format flags that
            // render it. Ordering these the other way lets --json answer a count request
            // with the full unprojected result set.
            if (options.Count)
            {
                WriteCount(results);
            }
            else if (options.JsonOutput)
            {
                WriteJsonOutput(results, options.CompactJson);
            }
            else
            {
                WriteMarkoutOutput(targetType, results, options.Tabular, options.Tsv, options.Jsonl, options.NoHeader, options.Columns, options.Fields, options.Rows);
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
        AssemblySetEntry assembly,
        AssemblyContextEntry<ImmutableArray<TypeRelationship>> entry,
        VerboseLogger logger)
    {
        switch (entry)
        {
            case AssemblyContextEntry<
                ImmutableArray<TypeRelationship>>.Available available:
                string assemblyName =
                    Path.GetFileNameWithoutExtension(assembly.Path);
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
                        Assembly = assemblyName,
                        Source = assembly.Source,
                        SourceVersion = assembly.Version,
                    });
                }
                break;
            case AssemblyContextEntry<
                ImmutableArray<TypeRelationship>>.Rejected rejected:
                logger.LogWarning(
                    $"Error scanning {assembly.Path}: {rejected.Failure.Detail}");
                break;
            case AssemblyContextEntry<
                ImmutableArray<TypeRelationship>>.Failed failed:
                logger.LogWarning(
                    $"Error scanning {assembly.Path}: {failed.Error.Message}");
                break;
        }
    }

    private static void WriteJsonOutput(List<ImplementerResult> results, bool compact)
    {
        var jsonResults = results.Select(ImplementerJsonResult.From).ToList();
        JsonOutputHelper.Write(jsonResults, ImplementsJsonContext.Default.ListImplementerJsonResult,
            ImplementsCompactJsonContext.Default.ListImplementerJsonResult, compact);
    }

    private static void WriteCount(List<ImplementerResult> results)
    {
        CountOutput.WriteCount(results.Count);
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
