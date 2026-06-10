using System.Text.Json;
using DotnetInspector.Options;

namespace DotnetInspector.Commands;

public sealed record BuildOptions
{
    public required string Path { get; init; }
    public OutputFormat Format { get; init; }
    public string[]? Discover { get; init; }
    public string[]? Select { get; init; }
    public bool NoHeader { get; init; }
}

public static class BuildCommand
{
    public const string Name = "build";

    private static readonly string[] s_sections = ["Projects", "Diagnostics", "Targets", "Tasks", "Graph"];

    public static int Execute(BuildOptions options)
    {
        if (!File.Exists(options.Path))
        {
            Console.Error.WriteLine($"Error: Build event stream '{options.Path}' not found.");
            return 1;
        }

        BuildEventLog log;
        try
        {
            log = BuildEventLog.Load(options.Path);
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
            "Projects" => WriteRows(ProjectRows(log), options),
            "Diagnostics" => WriteRows(DiagnosticRows(log), options),
            "Targets" => WriteRows(TargetRows(log), options),
            "Tasks" => WriteRows(TaskRows(log), options),
            "Graph" => WriteGraph(log, options),
            _ => WriteUnknownSection(section),
        };
    }

    private static string ResolveSection(string[]? select)
    {
        if (select is null or { Length: 0 })
        {
            return "Projects";
        }

        string value = select[0];
        if (string.Equals(value, "All", StringComparison.OrdinalIgnoreCase))
        {
            return "Projects";
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
            WriteTable([new Dictionary<string, object?> { ["Name"] = "Projects", ["Kind"] = "section" },
                new Dictionary<string, object?> { ["Name"] = "Diagnostics", ["Kind"] = "section" },
                new Dictionary<string, object?> { ["Name"] = "Targets", ["Kind"] = "section" },
                new Dictionary<string, object?> { ["Name"] = "Tasks", ["Kind"] = "section" },
                new Dictionary<string, object?> { ["Name"] = "Graph", ["Kind"] = "section" }], noHeader: false);
            return;
        }

        string section = ResolveSection(discover);
        string[] columns = section switch
        {
            "Projects" => ["Project", "TargetFramework", "RuntimeIdentifier", "Configuration", "ParentProject"],
            "Diagnostics" => ["Severity", "Code", "Project", "File", "Line", "Column", "Message"],
            "Targets" => ["Project", "Target", "Sequence"],
            "Tasks" => ["Project", "TargetId", "Task", "Sequence"],
            "Graph" => ["Mermaid"],
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
        string mermaid = BuildGraphWriter.WriteMermaid(log);
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

    private static List<Dictionary<string, object?>> ProjectRows(BuildEventLog log)
    {
        return log.Projects
            .Select(project => new Dictionary<string, object?>
            {
                ["Project"] = ShortPath(project.ProjectFile),
                ["TargetFramework"] = project.Dimensions.TargetFramework,
                ["RuntimeIdentifier"] = project.Dimensions.RuntimeIdentifier,
                ["Configuration"] = project.Dimensions.Configuration,
                ["ParentProject"] = log.ProjectByContext.TryGetValue(project.ParentContextKey, out ProjectEvent? parent)
                    ? ShortPath(parent.ProjectFile)
                    : null,
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> DiagnosticRows(BuildEventLog log)
    {
        return log.Diagnostics
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
            Console.WriteLine(string.Join('\t', columns.Select(column => ToCell(row[column]).Replace('\t', ' '))));
        }
    }

    private static string ShortPath(string? path)
    {
        return string.IsNullOrEmpty(path)
            ? string.Empty
            : Path.GetFileName(path);
    }

    private static string ToCell(object? value) => value?.ToString() ?? string.Empty;

    private static void WriteJsonArray(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        using Utf8JsonWriter writer = new(Console.OpenStandardOutput());
        writer.WriteStartArray();
        foreach (Dictionary<string, object?> row in rows)
        {
            WriteJsonObject(writer, row);
        }

        writer.WriteEndArray();
    }

    private static void WriteJsonObject(Dictionary<string, object?> row)
    {
        using Utf8JsonWriter writer = new(Console.OpenStandardOutput());
        WriteJsonObject(writer, row);
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
}

internal sealed class BuildEventLog
{
    public List<ProjectEvent> Projects { get; } = [];
    public List<DiagnosticEvent> Diagnostics { get; } = [];
    public List<TargetEvent> Targets { get; } = [];
    public List<TaskEvent> Tasks { get; } = [];
    public Dictionary<BuildContextKey, ProjectEvent> ProjectByContext { get; } = [];

    public static BuildEventLog Load(string path)
    {
        BuildEventLog log = new();

        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            string? kind = ReadString(root, "kind");
            long sequence = ReadInt64(root, "sequenceNumber");
            BuildEventContext context = BuildEventContext.Read(root.GetProperty("context"));
            JsonElement payload = root.GetProperty("payload");

            switch (kind)
            {
                case "project.started":
                    ProjectEvent project = new(
                        sequence,
                        context,
                        ReadString(payload, "projectFile"),
                        ProjectDimensions.Read(payload.GetProperty("dimensions")),
                        ReadBool(payload, "hasParentContext")
                            ? BuildEventContext.Read(payload.GetProperty("parentContext")).ToKey()
                            : default);
                    log.Projects.Add(project);
                    log.ProjectByContext[context.ToKey()] = project;
                    break;

                case "diagnostic":
                    log.Diagnostics.Add(new DiagnosticEvent(
                        sequence,
                        context,
                        ReadString(payload, "severity"),
                        ReadString(payload, "code"),
                        ReadString(payload, "file"),
                        ReadString(payload, "projectFile"),
                        ReadInt32(payload, "lineNumber"),
                        ReadInt32(payload, "columnNumber"),
                        ReadString(payload, "message")));
                    break;

                case "target.started":
                    log.Targets.Add(new TargetEvent(sequence, context, ReadString(payload, "targetName")));
                    break;

                case "task.started":
                    log.Tasks.Add(new TaskEvent(sequence, context, ReadString(payload, "projectFile"), ReadString(payload, "taskName")));
                    break;
            }
        }

        return log;
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int ReadInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt32(out int value)
            ? value
            : 0;

    private static long ReadInt64(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt64(out long value)
            ? value
            : 0;

    private static bool ReadBool(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.True;
}

internal sealed record ProjectEvent(
    long SequenceNumber,
    BuildEventContext Context,
    string? ProjectFile,
    ProjectDimensions Dimensions,
    BuildContextKey ParentContextKey);

internal sealed record DiagnosticEvent(
    long SequenceNumber,
    BuildEventContext Context,
    string? Severity,
    string? Code,
    string? File,
    string? ProjectFile,
    int LineNumber,
    int ColumnNumber,
    string? Message);

internal sealed record TargetEvent(long SequenceNumber, BuildEventContext Context, string? TargetName)
{
    public BuildContextKey ProjectContextKey => Context.ProjectKey;
}

internal sealed record TaskEvent(long SequenceNumber, BuildEventContext Context, string? ProjectFile, string? TaskName)
{
    public BuildContextKey ProjectContextKey => Context.ProjectKey;
}

internal sealed record ProjectDimensions(string? TargetFramework, string? RuntimeIdentifier, string? Configuration)
{
    public static ProjectDimensions Read(JsonElement element)
        => new(
            ReadString(element, "targetFramework"),
            ReadString(element, "runtimeIdentifier"),
            ReadString(element, "configuration"));

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

internal readonly record struct BuildEventContext(
    int SubmissionId,
    int ProjectInstanceId,
    int ProjectContextId,
    int TargetId,
    int TaskId)
{
    public BuildContextKey ToKey() => new(SubmissionId, ProjectInstanceId, ProjectContextId);

    public BuildContextKey ProjectKey => new(SubmissionId, ProjectInstanceId, ProjectContextId);

    public static BuildEventContext Read(JsonElement element)
        => new(
            ReadInt32(element, "submissionId"),
            ReadInt32(element, "projectInstanceId"),
            ReadInt32(element, "projectContextId"),
            ReadInt32(element, "targetId"),
            ReadInt32(element, "taskId"));

    private static int ReadInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt32(out int value)
            ? value
            : 0;
}

internal readonly record struct BuildContextKey(int SubmissionId, int ProjectInstanceId, int ProjectContextId);

internal static class BuildGraphWriter
{
    public static string WriteMermaid(BuildEventLog log)
    {
        List<Node> nodes = [];
        Dictionary<LogicalProjectKey, string> logicalNodeIds = [];
        Dictionary<BuildContextKey, string> executionNodeIds = [];
        List<(string From, string To)> edges = [];

        foreach (ProjectEvent project in log.Projects)
        {
            LogicalProjectKey logicalKey = new(project.ProjectFile, project.Dimensions.TargetFramework, project.Dimensions.RuntimeIdentifier, project.Dimensions.Configuration);
            if (!logicalNodeIds.TryGetValue(logicalKey, out string? nodeId))
            {
                nodeId = "p" + (nodes.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                logicalNodeIds[logicalKey] = nodeId;
                nodes.Add(new Node(nodeId, CreateLabel(project)));
            }

            executionNodeIds[project.Context.ToKey()] = nodeId;

            if (executionNodeIds.TryGetValue(project.ParentContextKey, out string? parentNodeId)
                && !string.Equals(parentNodeId, nodeId, StringComparison.Ordinal))
            {
                edges.Add((parentNodeId, nodeId));
            }
        }

        StringWriter writer = new();
        writer.WriteLine("flowchart TD");
        foreach (Node node in nodes)
        {
            writer.WriteLine($"  {node.Id}[\"{node.Label}\"]");
        }

        foreach ((string from, string to) in edges.Distinct())
        {
            writer.WriteLine($"  {from} --> {to}");
        }

        return writer.ToString();
    }

    private static string CreateLabel(ProjectEvent project)
    {
        string label = string.IsNullOrEmpty(project.ProjectFile)
            ? "(unknown project)"
            : Path.GetFileName(project.ProjectFile);
        List<string> dimensions = [];
        Add(dimensions, "TFM", project.Dimensions.TargetFramework);
        Add(dimensions, "RID", project.Dimensions.RuntimeIdentifier);
        Add(dimensions, "Config", project.Dimensions.Configuration);
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

    private readonly record struct LogicalProjectKey(string? ProjectFile, string? TargetFramework, string? RuntimeIdentifier, string? Configuration);

    private sealed record Node(string Id, string Label);
}
