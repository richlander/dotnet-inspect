using System.Text.Json;
using DotnetInspector.BuildEvents;
using DotnetInspector.BuildEvents.Events;
using DotnetInspector.BuildEvents.Projections;
using DotnetInspector.Options;

namespace DotnetInspector.Commands;

public sealed record BuildOptions
{
    public required string Path { get; init; }
    public OutputFormat Format { get; init; }
    public string[]? Discover { get; init; }
    public string[]? Select { get; init; }
    public bool NoHeader { get; init; }
    public int? RowLimit { get; init; }
    public int? TailLimit { get; init; }
    public int? CardLimit { get; init; }
    public int? TailCardLimit { get; init; }
    public string? Code { get; init; }
    public string? Severity { get; init; }
    public string? Project { get; init; }
    public string? File { get; init; }
    public string? Diagnostic { get; init; }
    public string? Cluster { get; init; }
}

public static class BuildCommand
{
    public const string Name = "build";

    private const int DefaultDiagnosticTypeCount = 6;
    private const int DefaultRichDiagnosticCount = 1;

    private static readonly string[] s_sections =
    [
        "Summary",
        "DiagnosticTypes",
        "Diagnostics",
        "Errors",
        "Warnings",
        "Details",
        "Explain",
        "Projects",
        "Graph",
        "Targets",
        "Tasks"
    ];

    public static int Execute(BuildOptions options)
    {
        BuildEventLog log;
        try
        {
            BuildEventLogResolution resolution = BuildEventLogResolver.Resolve(options.Path);
            log = BuildEventLog.Load(resolution.Path, resolution.EventLogId);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Console.Error.WriteLine($"Error reading build event stream: {ex.Message}");
            return 1;
        }

        if (options.Discover is not null)
        {
            WriteDiscovery(options.Discover);
            return 0;
        }

        string section = ResolveSection(options.Select);
        return section switch
        {
            "Summary" => WriteRows(SummaryRows(log), options),
            "DiagnosticTypes" => WriteRows(DiagnosticTypeRows(log, options), options),
            "Diagnostics" => WriteRows(DiagnosticRows(log, options), options),
            "Errors" => WriteRows(FilteredDiagnosticRows(log, options, "error"), options),
            "Warnings" => WriteRows(FilteredDiagnosticRows(log, options, "warning"), options),
            "Details" => WriteDetails(log, options),
            "Explain" => WriteExplain(log, options),
            "Projects" => WriteRows(ProjectRows(log), options),
            "Graph" => WriteGraph(log, options),
            "Targets" => WriteRows(TargetRows(log), options),
            "Tasks" => WriteRows(TaskRows(log), options),
            _ => WriteUnknownSection(section),
        };
    }

    private static string ResolveSection(string[]? select)
    {
        if (select is null or { Length: 0 })
        {
            return "Summary";
        }

        string value = select[0];
        if (string.Equals(value, "All", StringComparison.OrdinalIgnoreCase))
        {
            return "Summary";
        }

        if (string.Equals(value, "Types", StringComparison.OrdinalIgnoreCase))
        {
            return "DiagnosticTypes";
        }

        string? match = s_sections.FirstOrDefault(section => string.Equals(section, value, StringComparison.OrdinalIgnoreCase));
        return match ?? value;
    }

    private static int WriteUnknownSection(string section)
    {
        Console.Error.WriteLine($"Error: Select value '{section}' not found.");
        Console.Error.WriteLine("Available sections:");
        foreach (string known in s_sections)
        {
            Console.Error.WriteLine($"  {known}");
        }

        return 1;
    }

    private static void WriteDiscovery(string[] discover)
    {
        if (discover.Length == 0)
        {
            WriteTable(
                s_sections.Select(section => new Dictionary<string, object?> { ["Name"] = section, ["Kind"] = "section" }).ToList(),
                noHeader: false);
            return;
        }

        string section = ResolveSection(discover);
        string[] columns = section switch
        {
            "Summary" => ["Kind", "Projects", "Failed", "Errors", "Warnings", "EventLogId"],
            "DiagnosticTypes" => ["Kind", "Severity", "Code", "Count"],
            "Diagnostics" => ["Severity", "Code", "Project", "File", "Line", "Column", "Message"],
            "Errors" => ["Code", "Project", "File", "Line", "Column", "Message"],
            "Warnings" => ["Code", "Project", "File", "Line", "Column", "Message"],
            "Details" => ["Id", "Digest", "Severity", "Code", "Project", "File", "Line", "Column", "Message"],
            "Explain" => ["Cluster", "Title", "Count", "Codes", "LikelyCause"],
            "Projects" => ["Project", "TargetFramework", "RuntimeIdentifier", "Errors", "Warnings", "Succeeded"],
            "Graph" => ["Mermaid"],
            "Targets" => ["Project", "Target", "Sequence"],
            "Tasks" => ["Project", "TargetId", "Task", "Sequence"],
            _ => [],
        };

        if (columns.Length == 0)
        {
            WriteUnknownSection(section);
            return;
        }

        WriteTable(columns.Select(column => new Dictionary<string, object?> { ["Name"] = column, ["Kind"] = "column" }).ToList(), noHeader: false);
    }

    private static int WriteGraph(BuildEventLog log, BuildOptions options)
    {
        string mermaid = BuildGraphWriter.WriteMermaid(BuildGraphModel.Create(log));
        if (options.Format is OutputFormat.Json)
        {
            WriteJsonObject(new Dictionary<string, object?> { ["mermaid"] = mermaid });
            Console.WriteLine();
        }
        else
        {
            Console.Write(mermaid);
        }

        return 0;
    }

    private static int WriteDetails(BuildEventLog log, BuildOptions options)
    {
        List<DiagnosticEvent> diagnostics = FilterDiagnostics(log.Diagnostics, options)
            .ToList();
        if (!string.IsNullOrEmpty(options.Diagnostic))
        {
            diagnostics = FilterByDiagnosticSelector(diagnostics, options.Diagnostic).ToList();
        }

        if (options.Format is OutputFormat.Json or OutputFormat.Jsonl or OutputFormat.Tsv)
        {
            return WriteRows(DetailIndexRows(diagnostics).ToList(), options);
        }

        int limit = Math.Max(0, options.CardLimit ?? options.RowLimit ?? DefaultRichDiagnosticCount);
        int? tailCardLimit = options.TailCardLimit ?? options.TailLimit;
        List<DiagnosticEvent> renderedDiagnostics = tailCardLimit is int tailLimit
            ? diagnostics.TakeLast(Math.Max(0, tailLimit)).ToList()
            : diagnostics.Take(limit).ToList();
        if (renderedDiagnostics.Count < diagnostics.Count)
        {
            Console.WriteLine($"Showing {renderedDiagnostics.Count} of {diagnostics.Count} diagnostic(s). Use --cards {diagnostics.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} to show all, --tail-cards N to show the end, or filter with --code.");
            Console.WriteLine();
        }

        foreach (DiagnosticEvent diagnostic in renderedDiagnostics)
        {
            WriteDiagnosticCard(diagnostic);
        }

        return 0;
    }

    private static int WriteExplain(BuildEventLog log, BuildOptions options)
    {
        List<DiagnosticEvent> diagnostics = FilterDiagnostics(log.Diagnostics, options).ToList();
        if (!string.IsNullOrEmpty(options.Diagnostic))
        {
            diagnostics = FilterByDiagnosticSelector(diagnostics, options.Diagnostic).ToList();
        }

        List<ExplainMatch> matches = FindExplainMatches(diagnostics, options.Cluster).ToList();

        if (options.Format is OutputFormat.Json or OutputFormat.Jsonl or OutputFormat.Tsv or OutputFormat.Table)
        {
            return WriteRows(ExplainRows(matches), options);
        }

        WriteExplainMarkdown(matches, diagnostics.Count);
        return 0;
    }

    private static int WriteRows(List<Dictionary<string, object?>> rows, BuildOptions options)
    {
        switch (options.Format)
        {
            case OutputFormat.Json:
                WriteJsonArray(rows);
                Console.WriteLine();
                break;

            case OutputFormat.Jsonl:
                foreach (Dictionary<string, object?> row in rows)
                {
                    WriteJsonObject(row);
                    Console.WriteLine();
                }

                break;

            case OutputFormat.Tsv:
                WriteTsv(rows, options.NoHeader);
                break;

            default:
                WriteTable(rows, options.NoHeader);
                break;
        }

        return 0;
    }

    private static List<Dictionary<string, object?>> SummaryRows(BuildEventLog log)
    {
        BuildSummary summary = BuildSummary.Create(log);
        return
        [
            new Dictionary<string, object?>
            {
                ["Kind"] = "summary",
                ["Projects"] = summary.Projects,
                ["Failed"] = summary.Failed,
                ["Errors"] = summary.Errors,
                ["Warnings"] = summary.Warnings,
                ["EventLogId"] = summary.EventLogId,
            }
        ];
    }

    private static List<Dictionary<string, object?>> DiagnosticTypeRows(BuildEventLog log, BuildOptions options)
    {
        int limit = options.RowLimit ?? DefaultDiagnosticTypeCount;
        return DiagnosticTypeSummary.Create(log, limit)
            .Select(summary => new Dictionary<string, object?>
            {
                ["Kind"] = "diagnostic-type",
                ["Severity"] = summary.Severity,
                ["Code"] = summary.Code,
                ["Count"] = summary.Count,
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> ProjectRows(BuildEventLog log)
    {
        Dictionary<BuildContextKey, ProjectDiagnosticCounts> diagnosticCounts = log.Diagnostics
            .GroupBy(static diagnostic => diagnostic.Context.ProjectKey)
            .ToDictionary(
                static group => group.Key,
                static group => new ProjectDiagnosticCounts(
                    group.Count(static diagnostic => IsSeverity(diagnostic, "error")),
                    group.Count(static diagnostic => IsSeverity(diagnostic, "warning"))));

        Dictionary<BuildContextKey, ProjectResultEvent> results = log.ProjectResults
            .GroupBy(static project => project.ProjectContextKey)
            .ToDictionary(static group => group.Key, static group => group.Last());

        return log.Projects
            .Select(project =>
            {
                diagnosticCounts.TryGetValue(project.Context.ProjectKey, out ProjectDiagnosticCounts counts);
                results.TryGetValue(project.Context.ProjectKey, out ProjectResultEvent? result);
                return new Dictionary<string, object?>
                {
                    ["Project"] = ShortPath(project.ProjectFile),
                    ["TargetFramework"] = project.Dimensions.TargetFramework,
                    ["RuntimeIdentifier"] = project.Dimensions.RuntimeIdentifier,
                    ["Errors"] = counts.Errors,
                    ["Warnings"] = counts.Warnings,
                    ["Succeeded"] = result is null ? null : result.Succeeded ? "true" : "false",
                };
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> DiagnosticRows(BuildEventLog log, BuildOptions options)
    {
        return FilterDiagnostics(log.Diagnostics, options)
            .Select(diagnostic => new Dictionary<string, object?>
            {
                ["Severity"] = diagnostic.Severity,
                ["Code"] = diagnostic.Code,
                ["Project"] = ShortPath(diagnostic.ProjectFile),
                ["File"] = diagnostic.File,
                ["Line"] = diagnostic.LineNumber,
                ["Column"] = diagnostic.ColumnNumber,
                ["Message"] = diagnostic.Message,
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> FilteredDiagnosticRows(BuildEventLog log, BuildOptions options, string severity)
    {
        return FilterDiagnostics(log.Diagnostics, options with { Severity = severity })
            .Select(diagnostic => new Dictionary<string, object?>
            {
                ["Code"] = diagnostic.Code,
                ["Project"] = ShortPath(diagnostic.ProjectFile),
                ["File"] = diagnostic.File,
                ["Line"] = diagnostic.LineNumber,
                ["Column"] = diagnostic.ColumnNumber,
                ["Message"] = diagnostic.Message,
            })
            .ToList();
    }

    private static IEnumerable<Dictionary<string, object?>> DetailIndexRows(IReadOnlyList<DiagnosticEvent> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            DiagnosticEvent diagnostic = diagnostics[i];
            string id = CreateDiagnosticSelector(diagnostic, i);
            yield return new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["Digest"] = CreateDiagnosticDigest(diagnostic),
                ["Severity"] = diagnostic.Severity,
                ["Code"] = diagnostic.Code,
                ["Project"] = ShortPath(diagnostic.ProjectFile),
                ["File"] = diagnostic.File,
                ["Line"] = diagnostic.LineNumber,
                ["Column"] = diagnostic.ColumnNumber,
                ["Message"] = diagnostic.Message,
            };
        }
    }

    private static void WriteDiagnosticCard(DiagnosticEvent diagnostic)
    {
        Console.Write(diagnostic.File);
        if (diagnostic.LineNumber > 0)
        {
            Console.Write('(');
            Console.Write(diagnostic.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (diagnostic.ColumnNumber > 0)
            {
                Console.Write(',');
                Console.Write(diagnostic.ColumnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            Console.Write(')');
        }

        Console.Write(": ");
        Console.Write(diagnostic.Severity);
        if (!string.IsNullOrEmpty(diagnostic.Code))
        {
            Console.Write(' ');
            Console.Write(diagnostic.Code);
        }

        Console.Write(": ");
        Console.WriteLine(diagnostic.Message);

        WriteSourceContext(diagnostic);
        Console.WriteLine();
    }

    private static void WriteSourceContext(DiagnosticEvent diagnostic)
    {
        if (string.IsNullOrEmpty(diagnostic.File) ||
            diagnostic.LineNumber <= 0 ||
            !File.Exists(diagnostic.File))
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(diagnostic.File);
        }
        catch (IOException)
        {
            return;
        }

        int errorIndex = diagnostic.LineNumber - 1;
        if (errorIndex < 0 || errorIndex >= lines.Length)
        {
            return;
        }

        int start = Math.Max(0, errorIndex - 2);
        int end = Math.Min(lines.Length - 1, errorIndex + 2);
        int width = (end + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

        for (int i = start; i <= end; i++)
        {
            string lineNumber = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width);
            Console.Write("  ");
            Console.Write(lineNumber);
            Console.Write(" | ");
            Console.WriteLine(lines[i]);

            if (i == errorIndex && diagnostic.ColumnNumber > 0)
            {
                Console.Write("  ");
                Console.Write(new string(' ', width));
                Console.Write(" | ");
                Console.Write(new string(' ', Math.Max(0, diagnostic.ColumnNumber - 1)));
                Console.WriteLine("^");
            }
        }
    }

    private static List<Dictionary<string, object?>> TargetRows(BuildEventLog log)
    {
        return log.Targets
            .Select(target => new Dictionary<string, object?>
            {
                ["Project"] = log.ProjectByContext.TryGetValue(target.ProjectContextKey, out ProjectEvent? project)
                    ? ShortPath(project.ProjectFile)
                    : null,
                ["Target"] = target.TargetName,
                ["Sequence"] = target.SequenceNumber,
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> TaskRows(BuildEventLog log)
    {
        return log.Tasks
            .Select(task => new Dictionary<string, object?>
            {
                ["Project"] = log.ProjectByContext.TryGetValue(task.ProjectContextKey, out ProjectEvent? project)
                    ? ShortPath(project.ProjectFile)
                    : ShortPath(task.ProjectFile),
                ["TargetId"] = task.Context.TargetId,
                ["Task"] = task.TaskName,
                ["Sequence"] = task.SequenceNumber,
            })
            .ToList();
    }

    private static void WriteTable(List<Dictionary<string, object?>> rows, bool noHeader)
    {
        if (rows.Count == 0)
        {
            return;
        }

        string[] columns = rows[0].Keys.ToArray();
        int[] widths = columns
            .Select(column => Math.Max(column.Length, rows.Max(row => ToCell(row[column]).Length)))
            .ToArray();

        if (!noHeader)
        {
            Console.WriteLine(string.Join("  ", columns.Select((column, i) => column.PadRight(widths[i]))));
            Console.WriteLine(string.Join("  ", widths.Select(width => new string('-', width))));
        }

        foreach (Dictionary<string, object?> row in rows)
        {
            Console.WriteLine(string.Join("  ", columns.Select((column, i) => ToCell(row[column]).PadRight(widths[i]))));
        }
    }

    private static void WriteTsv(List<Dictionary<string, object?>> rows, bool noHeader)
    {
        if (rows.Count == 0)
        {
            return;
        }

        string[] columns = rows[0].Keys.ToArray();
        if (!noHeader)
        {
            Console.WriteLine(string.Join('\t', columns));
        }

        foreach (Dictionary<string, object?> row in rows)
        {
            Console.WriteLine(string.Join('\t', columns.Select(column => ToCell(row[column]))));
        }
    }

    private static string ShortPath(string? path)
    {
        return string.IsNullOrEmpty(path)
            ? string.Empty
            : Path.GetFileName(path);
    }

    private static string ToCell(object? value)
        => (value?.ToString() ?? string.Empty)
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ');

    private static bool IsSeverity(DiagnosticEvent diagnostic, string severity)
        => string.Equals(diagnostic.Severity, severity, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<DiagnosticEvent> FilterDiagnostics(IEnumerable<DiagnosticEvent> diagnostics, BuildOptions options)
    {
        foreach (DiagnosticEvent diagnostic in diagnostics)
        {
            if (!string.IsNullOrEmpty(options.Severity) && !IsSeverity(diagnostic, options.Severity))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(options.Code) && !string.Equals(diagnostic.Code, options.Code, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ContainsPattern(diagnostic.ProjectFile, options.Project))
            {
                continue;
            }

            if (!ContainsPattern(diagnostic.File, options.File))
            {
                continue;
            }

            yield return diagnostic;
        }
    }

    private static bool ContainsPattern(string? value, string? pattern)
        => string.IsNullOrEmpty(pattern) ||
            (!string.IsNullOrEmpty(value) && value.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static string CreateDiagnosticSelector(DiagnosticEvent diagnostic, int index)
    {
        string prefix = IsSeverity(diagnostic, "warning") ? "W" : IsSeverity(diagnostic, "error") ? "E" : "D";
        return prefix + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateDiagnosticDigest(DiagnosticEvent diagnostic)
    {
        string identity = string.Join('\u001f',
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.ProjectFile,
            diagnostic.File,
            diagnostic.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            diagnostic.ColumnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            diagnostic.Message);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity));
        string suffix = Convert.ToHexString(hash, 0, 3).ToLowerInvariant();
        return string.IsNullOrEmpty(diagnostic.Code) ? suffix : diagnostic.Code + ":" + suffix;
    }

    private static readonly ExplainDefinition[] s_explainDefinitions =
    [
        new(
            Cluster: "toolchain-analyzer-crash",
            Title: "Toolchain or analyzer crash",
            Summary: "The build failed because a compiler, analyzer, or build task crashed rather than because user code produced normal diagnostics.",
            Codes: [],
            MessageTerms: ["Process terminated", "Assertion failed", "Unknown pattern kind", "Microsoft.CodeAnalysis", "AnalyzerExecutor", "DataFlowOperationVisitor"],
            LikelyCause: "An SDK/analyzer/toolchain component threw or asserted while processing the project. The event stream is preserving the emitted crash diagnostics, but this is usually not fixable by editing the reported target file.",
            FirstFixes:
            [
                "Confirm whether the failure reproduces with the same SDK/analyzer version.",
                "Try disabling analyzers or reducing analyzer severity only in an isolated diagnostic run, not as the product fix.",
                "If the task is warning cleanup, switch to a SDK/analyzer version that reaches the warning inventory before editing application code.",
                "File or link an SDK/analyzer issue with the stack trace and repro project."
            ],
            FollowUpCommands:
            [
                "dotnet-inspect build <log> -S Errors --tsv",
                "dotnet-inspect build <log> -S Details --markdown"
            ]),
        new(
            Cluster: "missing-abstract-members",
            Title: "Missing abstract member implementations",
            Summary: "A concrete type inherits abstract members but does not implement all required members.",
            Codes: ["CS0534"],
            MessageTerms: [],
            LikelyCause: "A class derives from an abstract base type or implements an abstract contract without providing every required override/member.",
            FirstFixes:
            [
                "Open the type named in the diagnostic and implement the missing abstract members.",
                "If the type is meant to stay incomplete, mark it abstract instead.",
                "Prefer minimal stubs only when the test/sample intentionally focuses on build correctness."
            ],
            FollowUpCommands:
            [
                "dotnet-inspect build <log> -S Errors --code CS0534 --tsv",
                "dotnet-inspect build <log> -S Details --code CS0534 --markdown"
            ]),
        new(
            Cluster: "generic-arity-mismatch",
            Title: "Generic type or method arity mismatch",
            Summary: "A generic type or method was used with the wrong number of type arguments.",
            Codes: ["CS0305"],
            MessageTerms: [],
            LikelyCause: "The source is using an older/non-generic shape or omitted required type arguments for a generic type or method.",
            FirstFixes:
            [
                "Inspect the declaration named in the diagnostic and count its generic parameters.",
                "Add the required type arguments when the target API is generic.",
                "Remove type arguments when the selected API is not generic, or switch to the intended generic type."
            ],
            FollowUpCommands:
            [
                "dotnet-inspect build <log> -S Errors --code CS0305 --tsv",
                "dotnet-inspect build <log> -S Details --code CS0305 --markdown"
            ]),
        new(
            Cluster: "async-task-misuse",
            Title: "Async task misuse",
            Summary: "Task-returning operations are being ignored, used as values, or mixed with synchronous code.",
            Codes: ["CS4014", "CS0029", "CS0019", "CS1929", "CS1503", "CS4016"],
            MessageTerms: [],
            LikelyCause: "An async method result is not awaited, a Task<T> is being used where T is expected, or a collection of tasks is being treated as completed values.",
            FirstFixes:
            [
                "Add await at the call site when the caller can become async.",
                "Propagate async outward instead of blocking when possible.",
                "Use Task.WhenAll before aggregating task results.",
                "If a method has no real async work, return Task.CompletedTask or Task.FromResult instead of marking it async."
            ],
            FollowUpCommands:
            [
                "dotnet-inspect build <log> -S Errors --code CS4014 --tsv",
                "dotnet-inspect build <log> -S Details --code CS4014 --markdown"
            ]),
        new(
            Cluster: "system-commandline-api-mismatch",
            Title: "System.CommandLine API shape mismatch",
            Summary: "The project appears to use one System.CommandLine API shape while referencing another version.",
            Codes: ["CS1061", "CS1729", "CS1739", "CS0103"],
            MessageTerms: ["Command", "RootCommand", "Option", "Argument", "Handler", "AddOption", "AddArgument", "getDefaultValue", "description"],
            LikelyCause: "The source likely targets older System.CommandLine APIs such as AddOption/AddArgument/Handler, but the referenced package exposes the newer API shape.",
            FirstFixes:
            [
                "Check the referenced System.CommandLine package version.",
                "Update command construction to the referenced API shape.",
                "Fix repeated API-shape errors together instead of one line at a time.",
                "After updating one command pattern, rebuild and inspect the remaining diagnostic types."
            ],
            FollowUpCommands:
            [
                "dotnet-inspect build <log> -S Errors --code CS1061 --tsv",
                "dotnet-inspect build <log> -S Details --code CS1061 --markdown"
            ]),
        new(
            Cluster: "syntax-errors",
            Title: "Syntax or parser errors",
            Summary: "The compiler cannot parse one or more source lines.",
            Codes: ["CS1002", "CS1003", "CS1013", "CS1026", "CS1513"],
            MessageTerms: [],
            LikelyCause: "The source contains invalid tokens, missing punctuation, malformed literals, or unbalanced delimiters.",
            FirstFixes:
            [
                "Fix the earliest syntax diagnostic first; later syntax diagnostics may be cascades.",
                "Use Details on the first diagnostic to inspect the exact source span.",
                "Rebuild after fixing the first parse error before changing many nearby lines."
            ],
            FollowUpCommands:
            [
                "dotnet-inspect build <log> -S Errors --tsv",
                "dotnet-inspect build <log> -S Details --markdown"
            ]),
        new(
            Cluster: "unresolved-symbol",
            Title: "Unresolved name or namespace",
            Summary: "A name, type, namespace, or member cannot be resolved from the current context.",
            Codes: ["CS0103", "CS0246", "CS1061"],
            MessageTerms: [],
            LikelyCause: "The code may be missing a using, reference, package, member rename, or local declaration.",
            FirstFixes:
            [
                "Use the diagnostic location to inspect the receiver or unresolved name.",
                "For CS1061, check whether the member is an extension method requiring a using or package reference.",
                "For CS0246, check namespace/package references before inventing a type.",
                "For CS0103, check for typos, renamed locals, or missing declarations."
            ],
            FollowUpCommands:
            [
                "dotnet-inspect build <log> -S Errors --code CS1061 --tsv",
                "dotnet-inspect build <log> -S Details --diagnostic E1 --markdown"
            ])
    ];

    private static IEnumerable<ExplainMatch> FindExplainMatches(IReadOnlyList<DiagnosticEvent> diagnostics, string? cluster)
    {
        bool[] assigned = new bool[diagnostics.Count];
        foreach (ExplainDefinition definition in s_explainDefinitions)
        {
            if (!string.IsNullOrEmpty(cluster) &&
                !string.Equals(definition.Cluster, cluster, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            List<DiagnosticEvent> matches = [];
            List<int> matchedIndexes = [];
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (assigned[i])
                {
                    continue;
                }

                if (MatchesExplainDefinition(definition, diagnostics[i]))
                {
                    matches.Add(diagnostics[i]);
                    matchedIndexes.Add(i);
                }
            }

            if (matches.Count > 0)
            {
                foreach (int index in matchedIndexes)
                {
                    assigned[index] = true;
                }

                yield return new ExplainMatch(definition, matches);
            }
        }
    }

    private static bool MatchesExplainDefinition(ExplainDefinition definition, DiagnosticEvent diagnostic)
    {
        if (definition.Codes.Length > 0 &&
            !definition.Codes.Contains(diagnostic.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (definition.MessageTerms.Length == 0)
        {
            return true;
        }

        string haystack = string.Join(' ', diagnostic.Message, diagnostic.ProjectFile, diagnostic.File);
        return definition.MessageTerms.Any(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Dictionary<string, object?>> ExplainRows(IReadOnlyList<ExplainMatch> matches)
    {
        return matches.Select(match => new Dictionary<string, object?>
            {
                ["Cluster"] = match.Definition.Cluster,
                ["Title"] = match.Definition.Title,
                ["Count"] = match.Diagnostics.Count,
                ["Codes"] = string.Join(",", match.Diagnostics
                    .Select(static diagnostic => diagnostic.Code)
                    .Where(static code => !string.IsNullOrEmpty(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)),
                ["LikelyCause"] = match.Definition.LikelyCause,
            })
            .ToList();
    }

    private static void WriteExplainMarkdown(IReadOnlyList<ExplainMatch> matches, int selectedDiagnosticCount)
    {
        Console.WriteLine("# Explain");
        Console.WriteLine();
        Console.WriteLine($"Selected diagnostics: {selectedDiagnosticCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Matched clusters: {matches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        Console.WriteLine();

        if (matches.Count == 0)
        {
            Console.WriteLine("No explanation is available for the selected diagnostics.");
            Console.WriteLine();
            Console.WriteLine("Use `Types` and `Details` to inspect the diagnostic rows and source context directly.");
            return;
        }

        foreach (ExplainMatch match in matches)
        {
            WriteExplainMatchMarkdown(match);
        }
    }

    private static void WriteExplainMatchMarkdown(ExplainMatch match)
    {
        ExplainDefinition definition = match.Definition;
        Console.WriteLine($"## {definition.Title}");
        Console.WriteLine();
        Console.WriteLine($"Cluster: `{definition.Cluster}`");
        Console.WriteLine();
        Console.WriteLine(definition.Summary);
        Console.WriteLine();
        Console.WriteLine("### Applies to");
        Console.WriteLine();
        Console.WriteLine("| Severity | Code | Count | Example |");
        Console.WriteLine("| --- | --- | ---: | --- |");
        foreach (var group in match.Diagnostics
            .GroupBy(static diagnostic => new { diagnostic.Severity, diagnostic.Code })
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key.Code, StringComparer.OrdinalIgnoreCase))
        {
            string example = group.First().Message ?? string.Empty;
            Console.WriteLine($"| {EscapeMarkdownCell(group.Key.Severity)} | {EscapeMarkdownCell(group.Key.Code ?? string.Empty)} | {group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture)} | {EscapeMarkdownCell(Truncate(example, 100))} |");
        }

        Console.WriteLine();
        Console.WriteLine("### Likely cause");
        Console.WriteLine();
        Console.WriteLine(definition.LikelyCause);
        Console.WriteLine();
        Console.WriteLine("### First fixes");
        Console.WriteLine();
        for (int i = 0; i < definition.FirstFixes.Length; i++)
        {
            Console.WriteLine($"{(i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}. {definition.FirstFixes[i]}");
        }

        Console.WriteLine();
        Console.WriteLine("### Useful follow-up");
        Console.WriteLine();
        foreach (string command in definition.FollowUpCommands)
        {
            Console.WriteLine($"- `{command}`");
        }

        Console.WriteLine();
    }

    private static IEnumerable<DiagnosticEvent> FilterByDiagnosticSelector(IReadOnlyList<DiagnosticEvent> diagnostics, string selectorOrDigest)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            DiagnosticEvent diagnostic = diagnostics[i];
            if (string.Equals(CreateDiagnosticSelector(diagnostic, i), selectorOrDigest, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CreateDiagnosticDigest(diagnostic), selectorOrDigest, StringComparison.OrdinalIgnoreCase))
            {
                yield return diagnostic;
            }
        }
    }

    private static string EscapeMarkdownCell(string value)
        => value.Replace('|', ' ');

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";

    private static void WriteJsonArray(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        Console.Write(WriteJson(writer =>
        {
            writer.WriteStartArray();
            foreach (Dictionary<string, object?> row in rows)
            {
                WriteJsonObject(writer, row);
            }

            writer.WriteEndArray();
        }));
    }

    private static void WriteJsonObject(Dictionary<string, object?> row)
    {
        Console.Write(WriteJson(writer => WriteJsonObject(writer, row)));
    }

    private static string WriteJson(Action<Utf8JsonWriter> write)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            write(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteJsonObject(Utf8JsonWriter writer, Dictionary<string, object?> row)
    {
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object?> item in row)
        {
            writer.WritePropertyName(ToCamelCase(item.Key));
            switch (item.Value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case int value:
                    writer.WriteNumberValue(value);
                    break;
                case long value:
                    writer.WriteNumberValue(value);
                    break;
                case bool value:
                    writer.WriteBooleanValue(value);
                    break;
                default:
                    writer.WriteStringValue(item.Value.ToString());
                    break;
            }
        }

        writer.WriteEndObject();
    }

    private static string ToCamelCase(string value)
    {
        return value.Length == 0 || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
    }

    private readonly record struct ProjectDiagnosticCounts(int Errors, int Warnings);

    private sealed record ExplainDefinition(
        string Cluster,
        string Title,
        string Summary,
        string[] Codes,
        string[] MessageTerms,
        string LikelyCause,
        string[] FirstFixes,
        string[] FollowUpCommands);

    private sealed record ExplainMatch(ExplainDefinition Definition, IReadOnlyList<DiagnosticEvent> Diagnostics);
}

internal static class BuildGraphWriter
{
    public static string WriteMermaid(BuildGraphModel graph)
    {
        StringWriter writer = new();
        writer.WriteLine("flowchart TD");
        foreach (BuildGraphNode node in graph.Nodes)
        {
            writer.WriteLine($"  {node.Id}[\"{CreateLabel(node)}\"]");
        }

        foreach (BuildGraphEdge edge in graph.Edges)
        {
            writer.WriteLine($"  {edge.From} --> {edge.To}");
        }

        return writer.ToString();
    }

    private static string CreateLabel(BuildGraphNode node)
    {
        string label = string.IsNullOrEmpty(node.ProjectFile)
            ? "(unknown project)"
            : Path.GetFileName(node.ProjectFile);
        List<string> dimensions = [];
        Add(dimensions, "TFM", node.TargetFramework);
        Add(dimensions, "RID", node.RuntimeIdentifier);
        Add(dimensions, "Config", node.Configuration);
        return dimensions.Count == 0
            ? Escape(label)
            : Escape(label + "<br/>" + string.Join("<br/>", dimensions));
    }

    private static void Add(List<string> dimensions, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dimensions.Add($"{name}: {value}");
        }
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
