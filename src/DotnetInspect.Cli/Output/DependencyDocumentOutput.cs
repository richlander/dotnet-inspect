using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using InertText;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Output;

internal sealed record DependencyRootCompletionJson(
    int Occurrence, string Admission, string Traversal, bool GraphAvailable);
internal sealed record DependencySummaryJson(
    string RootSet, string Traversal, int? RequestedDepth, int Failures,
    DependencyRootCompletionJson[] Roots,
    DependencyEvidenceSummaryJson Evidence);
internal sealed record DependencyRootJson(
    int Occurrence, string Kind, string Locator,
    DependencyGraphJsonNodeIdentity? Identity,
    string Admission, string Traversal,
    DependencyEvidenceRootJson? Evidence);
internal sealed record DependencyFailureJson(
    int[] Roots, string Phase, string Reason, string Message,
    DependencyEvidenceFailureJson? Evidence = null,
    DependencyEvidenceDeclarationIdentityJson? Declaration = null,
    int? ProjectionIndex = null, int? NodeIndex = null, int? Limit = null,
    string? CandidateOutcome = null, string? CandidateReason = null,
    string? DiscoveryState = null, int? CandidateObservations = null,
    string? RestoredReason = null, int? Occurrences = null,
    string? MetadataDetail = null, string? MetadataRootReason = null,
    PackageManifestFailureReason? ManifestReason = null,
    int? ManifestLine = null, int? ManifestColumn = null,
    DependencyGraphJsonNodeIdentity? SourceIdentity = null,
    DependencyGraphJsonNodeIdentity? TargetIdentity = null,
    DependencyGraphJsonEvidenceIdentity? EvidenceIdentity = null,
    DependencyEvidencePackageCoordinateJson? PackageCoordinate = null);
internal sealed record DependencyDocumentJson(
    DependencySummaryJson Summary,
    DependencyGraphJsonDocument? DependencyGraph,
    List<DependencyRootJson>? Roots,
    List<DependencyEvidenceDependencyJson>? Dependencies,
    List<DependencyEvidenceRestoredEdgeJson>? RestoredEdges,
    List<DependencyFailureJson>? Failures,
    List<DependencyEvidenceGroupJson>? DependencyGroups,
    List<DependencyEvidenceRestoredPackageJson>? RestoredPackages);

[JsonSourceGenerationOptions(
    WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DependencyDocumentJson))]
internal partial class DependencyDocumentJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DependencyDocumentJson))]
internal partial class DependencyDocumentCompactJsonContext : JsonSerializerContext;

internal static class DependencyDocumentOutput
{
    internal static readonly string[] RootColumns =
        ["Occurrence", "Kind", "Locator", "Identity", "Admission", "Declarations", "Traversal"];
    internal static readonly string[] FailureColumns = ["Roots", "Phase", "Reason", "Message"];

    internal static bool Write(
        DependencyDocument document, DependsOptions options, HashSet<string> sections)
    {
        List<DependencyFailureJson> failures = FailureRows(document);
        if (options.Count)
        {
            var counts = new CountProjection();
            string[] ordered = [.. DependencySections.Order.Where(sections.Contains)];
            foreach (string section in ordered)
            {
                bool exact = section switch
                {
                    DependencySections.Graph => document.Traversal is
                        (DependencyCompletion.Complete or DependencyCompletion.DepthBounded or DependencyCompletion.SourceBounded)
                        && document.Evidence.Summary.PackagePrefix is null,
                    DependencyEvidenceSections.Roots => document.Evidence.Summary.PackagePrefix is null,
                    DependencyEvidenceSections.Failures => true,
                    _ => DependencyEvidenceCommand.IsExactRowSet(document.Evidence, section),
                };
                if (!exact)
                {
                    CommandError.Write($"--count cannot report an exact '{section}' count: the requested evidence is incomplete or the root population is bounded.");
                    return false;
                }
                int count = section switch
                {
                    DependencySections.Graph => document.Graph.Edges.Length,
                    DependencyEvidenceSections.Roots => document.Roots.Length,
                    DependencyEvidenceSections.Failures => failures.Count,
                    _ => DependencyEvidenceSections.CountRows(document.Evidence, section),
                };
                if (options.Rows is { } window)
                {
                    (int start, int end) = window.Resolve(count);
                    count = end - start;
                }
                counts.SetRows(section, count);
            }
            CountOutput.Write(counts, ordered.Length > 1 ? ordered : null,
                options.Format, options.NoHeader);
            return true;
        }
        IReadOnlyList<DependencyGraphEdgeRow> edges =
            DependencyGraphOutputAdapter.EdgeRows(document.Graph, options.Rows);
        if (options.Tree || options.Format == OutputFormat.Mermaid)
        {
            DependencyGraphOutputAdapter.Write(document.Graph, edges, options.Format,
                options.Tree, options.EmbeddedMermaid, options.NoHeader, options.CompactJson);
            return true;
        }

        DependencyEvidenceDocument evidence = DependencyEvidenceDocument.Create(
            document.Evidence, sections, options.Rows);
        DependencyEvidenceSourceTokens tokens = DependencyEvidenceSourceTokens.Create(document.Evidence);
        var summary = new DependencySummaryJson(
            document.Roots.Any(root => root.Admission != DependencyCompletion.Complete)
                || document.Evidence.Summary.RootSetCompletion != PackageDependencyEvidenceRootSetCompletion.Complete
                ? "Incomplete" : "Complete",
            document.Traversal.ToString(), document.RequestedDepth, failures.Count,
            [.. document.Roots.Select(root => new DependencyRootCompletionJson(
                root.Index, root.Admission.ToString(), root.Traversal.ToString(),
                root.NodeId is not null && root.Traversal != DependencyCompletion.NotRequested))],
            evidence.Summary);
        bool projected = options.Columns is { Length: > 0 } || options.Fields is { Length: > 0 };
        if (options.Format == OutputFormat.Jsonl && !projected && sections.SetEquals([DependencySections.Graph]))
        {
            DependencyGraphOutputAdapter.Write(document.Graph, edges, OutputFormat.Jsonl,
                tree: false, embeddedMermaid: false, options.NoHeader, options.CompactJson);
            return true;
        }
        if (options.JsonOutput && !projected)
        {
            var json = new DependencyDocumentJson(summary,
                sections.Contains(DependencySections.Graph)
                    ? DependencyGraphOutputAdapter.ToJson(document.Graph, edges, document.PackageTraversal, tokens,
                        document.PackageRootOccurrences, document.RestoredTraversals) : null,
                sections.Contains(DependencyEvidenceSections.Roots)
                    ? [.. RowWindow.Apply(options.Rows, document.Roots).Select(root => new DependencyRootJson(
                        root.Index, root.Kind.ToString(), root.Locator.ToString(),
                        root.NodeId is { } id ? DependencyGraphOutputAdapter.JsonIdentity(document.Graph.Nodes[id].Identity) : null,
                        root.Admission.ToString(), root.Traversal.ToString(),
                        document.Evidence.Roots.FirstOrDefault(row => row.RootIndex == root.Index) is { } row
                            ? DependencyEvidenceRootJson.Create(row, tokens) : null))] : null,
                evidence.Dependencies, evidence.RestoredEdges,
                sections.Contains(DependencyEvidenceSections.Failures)
                    ? [.. RowWindow.Apply(options.Rows, failures)] : null,
                evidence.DependencyGroups, evidence.RestoredPackages);
            Console.WriteLine(JsonSerializer.Serialize(json, options.CompactJson
                ? DependencyDocumentCompactJsonContext.Default.DependencyDocumentJson
                : DependencyDocumentJsonContext.Default.DependencyDocumentJson));
            return true;
        }

        if (options.JsonOutput)
        {
            OutputFormatter.WriteProjectedJson(Console.Out, options.Columns, options.Fields,
                (output, formatter, writerOptions) =>
                {
                    WriteSummary(output, formatter, summary);
                    WriteSections(output, formatter, writerOptions, graphTable: true);
                }, !options.CompactJson);
        }
        else if (options.EvidenceOptions.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader,
                options.Format == OutputFormat.Tsv, options.Format == OutputFormat.Jsonl,
                options.Columns, options.Fields,
                (output, formatter, writerOptions) =>
                    WriteSections(output, formatter, writerOptions, graphTable: true));
        }
        else
        {
            IMarkoutFormatter formatter = options.Format == OutputFormat.PlainText
                ? new PlainTextFormatter()
                : new MarkdownFormatter(options.EmbeddedMermaid ? MarkdownGraphMode.Mermaid : MarkdownGraphMode.EdgeTable);
            WriteSummary(Console.Out, formatter, summary);
            MarkoutWriterOptions writerOptions = OutputFormatter.CreateWindowedOptions(
                rows: null, options.Columns, options.Fields);
            WriteSections(Console.Out, formatter, writerOptions, graphTable: projected);
        }
        return true;

        void WriteSections(TextWriter output, IMarkoutFormatter formatter,
            MarkoutWriterOptions writerOptions, bool graphTable)
        {
            var writer = MarkoutWriter.Create(output, formatter, writerOptions);
            foreach (string section in DependencySections.Order.Where(sections.Contains))
            {
                if (section == DependencyEvidenceSections.Failures && failures.Count == 0
                    && options.Select is not { Length: > 0 })
                    continue;
                if (section == DependencySections.Graph)
                {
                    writer.WriteSectionStart(2, section);
                    if (graphTable)
                        DependencyGraphOutputAdapter.WriteRows(writer, edges);
                    else if (options.EmbeddedMermaid)
                        writer.WriteGraph(DependencyGraphOutputAdapter.ToGraph(document.Graph, edges, true));
                    else
                    {
                        writer.Flush();
                        if (options.Format != OutputFormat.PlainText)
                            output.WriteLine("```text");
                        DependencyGraphOutputAdapter.WriteTree(output, document.Graph, edges);
                        if (options.Format != OutputFormat.PlainText)
                            output.WriteLine("```");
                    }
                    writer.WriteSectionEnd();
                    writer.Flush();
                }
                else if (section == DependencyEvidenceSections.Roots)
                {
                    writer.WriteSectionStart(2, section);
                    writer.WriteTable(RootColumns,
                        ["occurrence", "kind", "locator", "identity", "admission", "declarations", "traversal"],
                        [.. RowWindow.Apply(options.Rows, document.Roots).Select(root => new[]
                        {
                            root.Index.ToString(CultureInfo.InvariantCulture), root.Kind.ToString(), root.Locator.ToString(),
                            root.NodeId is { } id ? DependencyGraphOutputAdapter.IdentityText(document.Graph.Nodes[id].Identity).ToString() : "",
                            root.Admission.ToString(),
                            document.Evidence.Roots.FirstOrDefault(row => row.RootIndex == root.Index) is { } evidenceRoot
                                ? evidenceRoot.DeclarationState + (evidenceRoot.DeclarationCompletion is { } completion ? $"/{completion}" : "")
                                : "NotApplicable",
                            root.Traversal.ToString(),
                        })]);
                    writer.WriteSectionEnd();
                    writer.Flush();
                }
                else if (section == DependencyEvidenceSections.Failures)
                {
                    writer.WriteSectionStart(2, section);
                    writer.WriteTable(FailureColumns, ["roots", "phase", "reason", "message"],
                        [.. RowWindow.Apply(options.Rows, failures).Select(failure => new[]
                        {
                            string.Join(",", failure.Roots), failure.Phase, failure.Reason, failure.Message,
                        })]);
                    writer.WriteSectionEnd();
                    writer.Flush();
                }
                else
                {
                    var view = DependencyEvidenceCommand.BuildTableView(document.Evidence,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section }, options.Rows);
                    MarkoutSerializer.Serialize(view, output, formatter,
                        DependencyEvidenceViewContext.Default, writerOptions);
                }
            }
        }
    }

    internal static int WriteDiscovery(DependencyDocument document, DependsOptions options,
        SectionCatalog<DependencyEvidenceProjection> catalog)
    {
        var schema = DependencySections.CreateSchema();
        HashSet<string> effective = [DependencyEvidenceSections.Roots];
        if (document.Graph.Roots.Length > 0)
            effective.Add(DependencySections.Graph);
        if (!document.TraversalFailures.IsEmpty || !document.Evidence.Failures.IsEmpty)
            effective.Add(DependencyEvidenceSections.Failures);
        foreach (string section in DependencyEvidenceSections.SectionOrder)
        {
            if (DependencyEvidenceSections.CountRows(document.Evidence, section) > 0)
                effective.Add(section);
        }
        var filtered = new DocumentSchema();
        foreach (string section in DependencySections.Order.Where(effective.Contains))
            filtered.Add(section, "column", [.. schema.GetSection(section)!.Items.Select(item => item.Name)]);
        int result = DiscoverOutput.Execute(options.Discover, filtered,
            tree: options.Tree, json: options.JsonOutput,
            tsv: options.Format == OutputFormat.Tsv, jsonl: options.Format == OutputFormat.Jsonl,
            sectionCategories: catalog.SelectionCategoryMap, projection: options);
        return result == 0 && document.IsSuccessful ? 0 : 1;
    }

    private static void WriteSummary(TextWriter output, IMarkoutFormatter formatter, DependencySummaryJson summary)
    {
        var writer = MarkoutWriter.Create(output, formatter, new MarkoutWriterOptions());
        writer.WriteSectionStart(2, "Summary");
        writer.WriteFields(
            new MarkoutField("Root Set", summary.RootSet),
            new MarkoutField("Roots", summary.Roots.Length.ToString(CultureInfo.InvariantCulture)),
            new MarkoutField("Traversal", summary.Traversal),
            new MarkoutField("Requested Depth", summary.RequestedDepth?.ToString(CultureInfo.InvariantCulture) ?? "Unbounded"),
            new MarkoutField("Failures", summary.Failures.ToString(CultureInfo.InvariantCulture)),
            new MarkoutField("Root Outcomes", string.Join("; ", summary.Roots.Select(root =>
                $"{root.Occurrence}: {root.Admission}/{root.Traversal}"))),
            new MarkoutField("Declarations", $"{summary.Evidence.CompleteDeclarations} complete; {summary.Evidence.IncompleteDeclarations} incomplete; {summary.Evidence.UnavailableDeclarations} unavailable; {summary.Evidence.FailedDeclarations} failed"));
        if (summary.Evidence.PackagePrefix is { } prefix)
        {
            writer.WriteFields(
                new MarkoutField("Prefix", prefix.Prefix?.ToString() ?? ""),
                new MarkoutField("Prefix Source", prefix.Source?.ProducerDisplay?.ToString() ?? ""),
                new MarkoutField("Candidates", prefix.Candidates.ToString(CultureInfo.InvariantCulture)),
                new MarkoutField("Matches", prefix.Matches.ToString(CultureInfo.InvariantCulture)),
                new MarkoutField("Prefix Failures", prefix.Failures.ToString(CultureInfo.InvariantCulture)),
                new MarkoutField("Truncation", prefix.TruncationReason.ToString()));
        }
        writer.WriteSectionEnd();
        writer.Flush();
    }

    private static List<DependencyFailureJson> FailureRows(DependencyDocument document)
    {
        var tokens = DependencyEvidenceSourceTokens.Create(document.Evidence);
        var result = document.Evidence.Failures.Select(row => new DependencyFailureJson(
            row.RootIndex is { } root ? [root] : [], row.Phase.ToString(), row.Reason,
            row.Message.ToString(), DependencyEvidenceFailureJson.Create(row, tokens))).ToList();
        foreach (DependencyTraversalFailure failure in document.TraversalFailures)
        {
            var candidate = failure.PackageResolution?.Outcome;
            string? candidateReason = candidate switch
            {
                PackageDependencyTraversalCandidateResult.Failed
                { Failure: PackageDependencyTraversalCandidateFailure.AuthorizationDenied } => "AuthorizationDenied",
                PackageDependencyTraversalCandidateResult.Failed
                { Failure: PackageDependencyTraversalCandidateFailure.NoMatchingVersion } => "NoMatchingVersion",
                PackageDependencyTraversalCandidateResult.Incomplete
                { Evidence: PackageDependencyTraversalCandidateIncomplete.PinnedAuthorization } => "PinnedAuthorization",
                PackageDependencyTraversalCandidateResult.Incomplete
                { Evidence: PackageDependencyTraversalCandidateIncomplete.VersionDiscovery } => "VersionDiscovery",
                null => null,
                _ => throw new InvalidOperationException("Unknown failed candidate outcome."),
            };
            var discovery = (candidate as PackageDependencyTraversalCandidateResult.Incomplete)?.Evidence
                as PackageDependencyTraversalCandidateIncomplete.VersionDiscovery;
            var manifest = failure.PackageManifest?.Detail as PackageDependencyTraversalManifestFailureDetail.Identity;
            result.Add(new([.. failure.Roots], failure.Phase, failure.Reason, failure.Message.ToString(),
                Declaration: failure.PackageResolution is { } resolution
                    ? DependencyEvidenceDeclarationIdentityJson.Create(resolution.DeclarationIdentity)
                    : failure.PackageBudget is { } budget
                        ? DependencyEvidenceDeclarationIdentityJson.Create(budget.DeclarationIdentity) : null,
                ProjectionIndex: failure.PackageManifest?.ProjectionIndex
                    ?? failure.PackageResolution?.SourceProjectionIndex ?? failure.PackageBudget?.SourceProjectionIndex,
                NodeIndex: failure.PackageManifest?.NodeIndex,
                Limit: failure.PackageBudget?.Limit
                    ?? (failure.PackageManifest?.Detail as PackageDependencyTraversalManifestFailureDetail.ManifestProjectionBudgetExhausted)?.Limit,
                CandidateOutcome: candidate is null ? null
                    : candidate is PackageDependencyTraversalCandidateResult.Failed ? "Failed" : "Incomplete",
                CandidateReason: candidateReason,
                DiscoveryState: discovery?.State.ToString(),
                CandidateObservations: discovery?.CandidateObservationCount,
                RestoredReason: failure.RestoredGraph?.Reason.ToString(),
                Occurrences: failure.RestoredGraph?.Count,
                MetadataDetail: failure.MetadataFailure?.Detail is { } detail
                    ? new InertString(TextPolicy.Field, detail).ToString() : null,
                MetadataRootReason: failure.MetadataFailure?.MetadataRootReason?.ToString(),
                ManifestReason: manifest?.Failure.Reason,
                ManifestLine: manifest?.Failure.LineNumber,
                ManifestColumn: manifest?.Failure.LinePosition,
                SourceIdentity: failure.SourceIdentity is { } source ? DependencyGraphOutputAdapter.JsonIdentity(source) : null,
                TargetIdentity: failure.TargetIdentity is { } target ? DependencyGraphOutputAdapter.JsonIdentity(target) : null,
                EvidenceIdentity: DependencyGraphOutputAdapter.JsonEvidenceIdentity(failure.EvidenceIdentity),
                PackageCoordinate: failure.PackageManifest is { } package && document.PackageTraversal is { } traversal
                    ? DependencyEvidencePackageCoordinateJson.Create(traversal.Nodes[package.NodeIndex].Coordinate) : null));
        }
        return result;
    }
}
