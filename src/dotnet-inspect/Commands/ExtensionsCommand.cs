using DotnetInspector.Packages;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using ILInspector.Findings;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
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
            if (options.JsonOutput)
            {
                WriteJsonOutput(results, options.CompactJson);
            }
            else if (options.Count)
            {
                WriteCount(results);
            }
            else if (options.OneLine || options.Tsv || options.Jsonl || options.NoHeader)
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
            Console.Error.WriteLine($"Error: {ex.Message}");
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
        var censuses = assemblySet.Assemblies
            .Select(assembly => InspectExtensionAssembly(assembly, options.IncludeAll))
            .ToList();
        var availableCensuses = new List<ExtensionAssemblyCensus>(censuses.Count);

        foreach (var census in censuses)
        {
            if (census.Inspection.Failure() is { } failure)
            {
                Console.Error.WriteLine(
                    $"Warning: Extension member inspection failed for {failure.Subject.Display}: {failure.Reason}");
                continue;
            }

            availableCensuses.Add(census);
            results.AddRange(ProjectExtensions(census, targetType));
        }

        if (!options.Reachable)
            return results;

        var assemblyPaths = assemblySet.Assemblies.Select(a => a.Path).ToList();
        var reachableTypes = ExtensionMethodScanner.FindReachableTypes(targetType, assemblyPaths, options.Depth);
        foreach (var (reachableType, path) in reachableTypes)
        {
            if (reachableType == targetType) continue;

            foreach (var census in availableCensuses)
            {
                results.AddRange(ProjectExtensions(
                    census,
                    reachableType,
                    reachablePath: path,
                    reachableFromType: reachableType));
            }
        }

        return results;
    }

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
            using var stream = File.OpenRead(assembly.Path);
            var members = ExtensionMethodScanner.FindAllExtensions(stream, includeAll).ToList();
            var inspection = MetadataFindings.InspectExtensionMembers(
                members,
                new FindingSubject(Path.GetFullPath(assembly.Path), Path.GetFileName(assembly.Path)));
            return new ExtensionAssemblyCensus(assembly, members, inspection);
        }
        catch (Exception ex)
        {
            return new ExtensionAssemblyCensus(
                assembly,
                [],
                new FindingInspection<ExtensionMemberObservation>.Failed(
                    new InspectionError(
                        new FindingSubject(
                            Path.GetFullPath(assembly.Path),
                            Path.GetFileName(assembly.Path)),
                        MetadataFindings.ExtensionMemberDescriptor,
                        ex.Message)));
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
        var normalizedTarget = TypeMatcher.Normalize(targetType);
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
                    TypeMatcher.Normalize(member.ExtendedType),
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
        JsonOutputHelper.Write(results, ExtensionsJsonContext.Default.ListExtensionMethodResult, ExtensionsCompactJsonContext.Default.ListExtensionMethodResult, compact);
    }

    private static void WriteCount(List<ExtensionMethodResult> results)
    {
        Console.WriteLine(results.Count);
    }

    private static void WriteMarkoutOutput(string targetType, List<ExtensionMethodResult> results, Verbosity verbosity, RowWindow? rows)
    {
        var view = ExtensionsOutputFormatter.BuildView(targetType, results, verbosity);
        OutputFormatter.WriteLimitedMarkdown(Console.Out,
            MarkoutSerializer.Serialize(view, SearchViewContext.Default), rows);
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

/// <summary>
/// Result of extension method search.
/// </summary>
public record class ExtensionMethodResult
{
    [JsonPropertyName("method")]
    public string MethodName { get; set; } = "";

    [JsonPropertyName("class")]
    public string ExtensionClass { get; set; } = "";

    [JsonPropertyName("extended_type")]
    public string ExtendedType { get; set; } = "";

    [JsonPropertyName("library")]
    public string Assembly { get; set; } = "";

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("signatures")]
    public List<string>? Signatures { get; set; }

    [JsonPropertyName("overloads")]
    public int? Overloads { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "method";

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }

    [JsonPropertyName("reachable_path")]
    public string? ReachablePath { get; set; }

    [JsonPropertyName("reachable_from_type")]
    public string? ReachableFromType { get; set; }
}
