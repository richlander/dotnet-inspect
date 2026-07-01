using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ILInspector.Analysis;
using Microsoft.Diagnostics.Tracing.Etlx;

var root = new RootCommand("runfaster - correlate dotnet-* diagnostic artifacts with static .NET library allocation evidence");
root.Subcommands.Add(CreateCorrelateCommand());
root.SetAction(parseResult =>
{
    if (parseResult.Tokens.Count == 0)
    {
        Console.WriteLine("runfaster 0.1.0 - prototype runtime/static allocation correlator");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  runfaster correlate --library <path.dll> --trace <trace.nettrace>");
        Console.WriteLine("  runfaster correlate --library <path.dll> --log <dotnet-stack.txt> --jsonl");
        Console.WriteLine();
        Console.WriteLine("Inputs:");
        Console.WriteLine("  dotnet-trace, dotnet-gcdump, dotnet-dump, dotnet-stack, dotnet-counters text/log artifacts");
        Console.WriteLine("  .NET libraries analyzed with the same static IL substrate used by dotnet-inspect");
        return 0;
    }

    Console.Error.WriteLine("Error: unknown command. Use 'runfaster correlate --help'.");
    return 1;
});

return await root.Parse(args).InvokeAsync();

static Command CreateCorrelateCommand()
{
    var command = new Command("correlate", "Correlate diagnostic artifacts with allocation candidates from .NET libraries");

    var libraryOption = new Option<string[]>("--library")
    {
        Description = ".NET library/assembly path to inspect statically",
        Arity = ArgumentArity.ZeroOrMore,
        AllowMultipleArgumentsPerToken = true
    };
    libraryOption.Aliases.Add("--assembly");

    var triageOption = new Option<string[]>("--triage")
    {
        Description = "dotnet-inspect Performance Triage JSONL rows to include as static candidates",
        Arity = ArgumentArity.ZeroOrMore,
        AllowMultipleArgumentsPerToken = true
    };

    var traceOption = CreateInputOption("--trace", "dotnet-trace artifact (.nettrace, Speedscope JSON, Chromium JSON, or textual report)");
    var gcdumpOption = CreateInputOption("--gcdump", "dotnet-gcdump artifact");
    var dumpOption = CreateInputOption("--dump", "dotnet-dump artifact or analysis log");
    var stackOption = CreateInputOption("--stack", "dotnet-stack output");
    var countersOption = CreateInputOption("--counters", "dotnet-counters output");
    var logOption = CreateInputOption("--log", "Additional diagnostic text log");
    var inputOption = CreateInputOption("--input", "Diagnostic artifact with inferred source kind");

    var topOption = new Option<int>("--top")
    {
        Description = "Maximum correlated rows to render",
        DefaultValueFactory = _ => 25
    };
    topOption.Aliases.Add("-n");

    var maxLogLinesOption = new Option<int>("--max-log-lines")
    {
        Description = "Maximum text lines to scan per diagnostic input",
        DefaultValueFactory = _ => 200_000
    };

    var confirmedOnlyOption = new Option<bool>("--confirmed-only")
    {
        Description = "Show only static rows observed in runtime diagnostics"
    };

    var tableOption = new Option<bool>("--table") { Description = "Render candidate rows as an aligned table" };
    var tsvOption = new Option<bool>("--tsv") { Description = "Render candidate rows as TSV" };
    var jsonlOption = new Option<bool>("--jsonl") { Description = "Render candidate rows as JSON Lines" };
    var jsonOption = new Option<bool>("--json") { Description = "Render the full result as JSON" };
    var markdownOption = new Option<bool>("--markdown") { Description = "Render the full result as Markdown (default)" };
    var plainTextOption = new Option<bool>("--plaintext") { Description = "Render the full result as plain text" };

    command.Options.Add(libraryOption);
    command.Options.Add(triageOption);
    command.Options.Add(traceOption);
    command.Options.Add(gcdumpOption);
    command.Options.Add(dumpOption);
    command.Options.Add(stackOption);
    command.Options.Add(countersOption);
    command.Options.Add(logOption);
    command.Options.Add(inputOption);
    command.Options.Add(topOption);
    command.Options.Add(maxLogLinesOption);
    command.Options.Add(confirmedOnlyOption);
    command.Options.Add(tableOption);
    command.Options.Add(tsvOption);
    command.Options.Add(jsonlOption);
    command.Options.Add(jsonOption);
    command.Options.Add(markdownOption);
    command.Options.Add(plainTextOption);

    command.SetAction(parseResult =>
    {
        var options = new CorrelateOptions(
            Libraries: GetValues(parseResult, libraryOption),
            TriageFiles: GetValues(parseResult, triageOption),
            Inputs:
            [
                .. GetInputs(parseResult, traceOption, "trace"),
                .. GetInputs(parseResult, gcdumpOption, "gcdump"),
                .. GetInputs(parseResult, dumpOption, "dump"),
                .. GetInputs(parseResult, stackOption, "stack"),
                .. GetInputs(parseResult, countersOption, "counters"),
                .. GetInputs(parseResult, logOption, "log"),
                .. GetInputs(parseResult, inputOption, "diagnostic"),
            ],
            Format: ResolveFormat(
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(jsonlOption),
                parseResult.GetValue(tsvOption),
                parseResult.GetValue(tableOption),
                parseResult.GetValue(plainTextOption),
                parseResult.GetValue(markdownOption)),
            Top: parseResult.GetValue(topOption),
            MaxLogLines: parseResult.GetValue(maxLogLinesOption),
            ConfirmedOnly: parseResult.GetValue(confirmedOnlyOption));

        return ExecuteCorrelate(options);
    });

    return command;
}

static Option<string[]> CreateInputOption(string name, string description) => new(name)
{
    Description = description,
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = true
};

static string[] GetValues(ParseResult parseResult, Option<string[]> option)
    => parseResult.GetValue(option) ?? [];

static IEnumerable<DiagnosticInput> GetInputs(ParseResult parseResult, Option<string[]> option, string kind)
{
    foreach (var value in GetValues(parseResult, option))
        yield return new DiagnosticInput(kind, value);
}

static OutputFormat ResolveFormat(bool json, bool jsonl, bool tsv, bool table, bool plainText, bool markdown)
{
    if (json)
        return OutputFormat.Json;
    if (jsonl)
        return OutputFormat.Jsonl;
    if (tsv)
        return OutputFormat.Tsv;
    if (table)
        return OutputFormat.Table;
    if (plainText)
        return OutputFormat.PlainText;
    if (markdown)
        return OutputFormat.Markdown;
    return OutputFormat.Markdown;
}

static int ExecuteCorrelate(CorrelateOptions options)
{
    if (options.Libraries.Count == 0 && options.TriageFiles.Count == 0)
    {
        Console.Error.WriteLine("Error: provide at least one --library or --triage input.");
        return 1;
    }

    if (options.Inputs.Count == 0)
    {
        Console.Error.WriteLine("Error: provide at least one diagnostic input (--trace, --gcdump, --dump, --stack, --counters, --log, or --input).");
        return 1;
    }

    if (options.Top <= 0)
    {
        Console.Error.WriteLine("Error: --top must be greater than zero.");
        return 1;
    }

    if (options.MaxLogLines <= 0)
    {
        Console.Error.WriteLine("Error: --max-log-lines must be greater than zero.");
        return 1;
    }

    var result = new CorrelationResult();

    foreach (var library in options.Libraries)
    {
        if (!TryLoadLibrary(library, result, out var exitCode))
            return exitCode;
    }

    foreach (var triageFile in options.TriageFiles)
    {
        if (!TryLoadTriageFile(triageFile, result, out var exitCode))
            return exitCode;
    }

    var lookup = CandidateLookup.Create(result.Candidates);
    foreach (var input in options.Inputs)
    {
        if (!TryCorrelateInput(input, result, lookup, options.MaxLogLines, out var exitCode))
            return exitCode;
    }

    Render(result, options);
    return 0;
}

static bool TryLoadLibrary(string library, CorrelationResult result, out int exitCode)
{
    exitCode = 0;
    var path = FullPath(library);
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Error: library not found: {library}");
        exitCode = 1;
        return false;
    }

    try
    {
        var index = LibraryBodyIndex.Open(path);
        foreach (var group in index.GetAllocationOccurrences())
        {
            foreach (var occurrence in group.Value)
            {
                if (!occurrence.CountsAsHeapAllocation)
                    continue;

                var candidate = AllocationCandidate.FromOccurrence(result.Candidates.Count, path, occurrence);
                result.Candidates.Add(candidate);
            }
        }

        result.LibraryInputs.Add(new LibraryInput(Path: path, StaticCandidates: result.Candidates.Count(c => c.LibraryPath == path), Error: null));
        return true;
    }
    catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidDataException)
    {
        Console.Error.WriteLine($"Error: cannot inspect library '{library}': {ex.Message}");
        exitCode = 1;
        return false;
    }
}

static bool TryLoadTriageFile(string triageFile, CorrelationResult result, out int exitCode)
{
    exitCode = 0;
    var path = FullPath(triageFile);
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Error: triage file not found: {triageFile}");
        exitCode = 1;
        return false;
    }

    int rows = 0;
    try
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            rows++;
            using var document = JsonDocument.Parse(line);
            if (TryCreateCandidateFromJson(result.Candidates.Count, path, document.RootElement, out var candidate))
                result.Candidates.Add(candidate);
        }

        result.TriageInputs.Add(new TriageInput(path, rows));
        return true;
    }
    catch (Exception ex) when (ex is JsonException or IOException)
    {
        Console.Error.WriteLine($"Error: cannot read triage JSONL '{triageFile}': {ex.Message}");
        exitCode = 1;
        return false;
    }
}

static bool TryCreateCandidateFromJson(int id, string path, JsonElement element, out AllocationCandidate candidate)
{
    candidate = default!;
    string? method = GetJsonString(element, "Method", "method", "Member", "member", "Selector", "selector", "Stable", "stable");
    string? tokenText = GetJsonString(element, "MetadataToken", "metadataToken", "Token", "token", "MethodToken", "methodToken");
    string? offsetText = GetJsonString(element, "ILOffset", "ilOffset", "IL Offset", "il", "IL", "Offset", "offset");

    int token = TryParseFlexibleInt(tokenText, out var parsedToken) ? parsedToken : 0;
    int offset = TryParseFlexibleInt(offsetText, out var parsedOffset) ? parsedOffset : -1;

    if (method is null && token == 0)
        return false;

    string kind = GetJsonString(element, "Shape", "shape", "Kind", "kind", "AllocationKind", "allocationKind") ?? "triage";
    string? detail = GetJsonString(element, "Detail", "detail", "Evidence", "evidence", "Reason", "reason");
    bool inLoop = GetJsonBool(element, "InLoop", "inLoop", "Loop", "loop") ?? false;

    candidate = new AllocationCandidate(
        id,
        "triage",
        path,
        GetJsonString(element, "Assembly", "assembly") ?? "",
        null,
        token,
        offset,
        method ?? DisplayHelpers.FormatToken(token),
        DisplayHelpers.MethodKeyFromText(method),
        DisplayHelpers.MethodStackKeyFromText(method),
        kind,
        GetJsonString(element, "AllocatedType", "allocatedType", "Type", "type"),
        detail,
        inLoop,
        GetJsonString(element, "Frequency", "frequency"),
        GetJsonString(element, "Escape", "escape"),
        GetJsonString(element, "Confidence", "confidence"),
        GetJsonString(element, "Fix", "fix"),
        GetJsonString(element, "Root Reach", "root_reach", "rootReach"));
    return true;
}

static string? GetJsonString(JsonElement element, params string[] names)
{
    foreach (var name in names)
    {
        if (!element.TryGetProperty(name, out var value))
            continue;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    return null;
}

static bool? GetJsonBool(JsonElement element, params string[] names)
{
    foreach (var name in names)
    {
        if (!element.TryGetProperty(name, out var value))
            continue;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.String when string.Equals(value.GetString(), "loop", StringComparison.OrdinalIgnoreCase) => true,
            JsonValueKind.String when string.IsNullOrWhiteSpace(value.GetString()) => false,
            _ => null
        };
    }

    return null;
}

static bool TryCorrelateInput(
    DiagnosticInput input,
    CorrelationResult result,
    CandidateLookup lookup,
    int maxLogLines,
    out int exitCode)
{
    exitCode = 0;
    var path = FullPath(input.Path);
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Error: diagnostic input not found: {input.Path}");
        exitCode = 1;
        return false;
    }

    var summary = new DiagnosticInputSummary(input.Kind, path, new FileInfo(path).Length, IsTextLike(path), 0, 0, []);
    if (path.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
    {
        if (!TryCorrelateNetTrace(path, input.Kind, summary, lookup, result, out exitCode))
            return false;

        result.DiagnosticInputs.Add(summary);
        return true;
    }

    if (!summary.TextLike)
    {
        summary.Observations.Add("binary artifact accepted; no EventPipe/diagnostic reader is wired in this prototype");
        summary.Observations.Add("convert .nettrace to Speedscope/Chromium JSON or provide a textual report to exercise correlation");
        result.DiagnosticInputs.Add(summary);
        return true;
    }

    try
    {
        if (TryCorrelateSpeedscope(path, input.Kind, summary, lookup))
        {
            result.DiagnosticInputs.Add(summary);
            return true;
        }
        if (TryCorrelateChromiumTrace(path, input.Kind, summary, lookup))
        {
            result.DiagnosticInputs.Add(summary);
            return true;
        }
        if (TrySummarizeCounters(path, summary))
        {
            result.DiagnosticInputs.Add(summary);
            return true;
        }

        int lineCount = 0;
        int matchedRows = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineCount++;
            if (lineCount > maxLogLines)
            {
                summary.Observations.Add($"stopped at --max-log-lines {maxLogLines.ToString(CultureInfo.InvariantCulture)}");
                break;
            }

            matchedRows += CorrelateLine(line, input.Kind, lookup);
        }

        summary.LinesScanned = Math.Min(lineCount, maxLogLines);
        summary.MatchedRows = matchedRows;
        summary.Observations.Add(matchedRows == 0
            ? "no static allocation candidate matched this text input"
            : $"matched {matchedRows.ToString(CultureInfo.InvariantCulture)} static candidate row(s)");
        result.DiagnosticInputs.Add(summary);
        return true;
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error: cannot read diagnostic input '{input.Path}': {ex.Message}");
        exitCode = 1;
        return false;
    }
}

static bool TryCorrelateNetTrace(
    string path,
    string sourceKind,
    DiagnosticInputSummary summary,
    CandidateLookup lookup,
    CorrelationResult result,
    out int exitCode)
{
    exitCode = 0;
    string etlxPath = Path.Combine(Path.GetTempPath(), $"runfaster-{Path.GetFileNameWithoutExtension(path)}-{File.GetLastWriteTimeUtc(path).Ticks:x}.etlx");
    try
    {
        if (!File.Exists(etlxPath))
            TraceLog.CreateFromEventPipeDataFile(path, etlxPath, new TraceLogOptions());

        using var traceLog = new TraceLog(etlxPath);
        using var source = traceLog.Events.GetSource();
        int allocationTicks = 0;
        int matchedEvents = 0;
        long allocationBytes = 0;
        var matchedRows = new HashSet<int>();

        source.Clr.GCAllocationTick += data =>
        {
            allocationTicks++;
            var stack = traceLog.GetCallStackForEvent(data);
            if (stack is null)
                return;

            var matchedThisEvent = new HashSet<int>();
            var matchedMethodsThisEvent = new HashSet<string>(StringComparer.Ordinal);
            while (stack != null)
            {
                foreach (var candidate in lookup.FindByMethodText(stack.CodeAddress.FullMethodName))
                {
                    if (matchedMethodsThisEvent.Add(candidate.Method))
                        result.RecordMethodAllocation(candidate.Method, data.TypeName, data.AllocationAmount64);
                    MarkAllocationHit(candidate, matchedThisEvent, sourceKind, data.TypeName, data.AllocationAmount64);
                }
                stack = stack.Caller;
            }

            if (matchedThisEvent.Count == 0)
                return;

            matchedEvents++;
            allocationBytes += data.AllocationAmount64;
            foreach (var id in matchedThisEvent)
                matchedRows.Add(id);
        };

        source.Process();
        summary.MatchedRows = matchedRows.Count;
        summary.Observations.Add($"nettrace GC allocation ticks: {allocationTicks.ToString(CultureInfo.InvariantCulture)}");
        summary.Observations.Add(matchedEvents == 0
            ? "no allocation tick stack matched a static candidate"
            : $"matched {matchedEvents.ToString(CultureInfo.InvariantCulture)} allocation tick(s), sampled bytes {FormatBytes(allocationBytes)}");
        return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or InvalidOperationException or NotSupportedException)
    {
        Console.Error.WriteLine($"Error: cannot read nettrace '{path}': {ex.Message}");
        exitCode = 1;
        return false;
    }
}

static bool TryCorrelateSpeedscope(
    string path,
    string sourceKind,
    DiagnosticInputSummary summary,
    CandidateLookup lookup)
{
    if (!path.EndsWith(".speedscope.json", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".speedscope", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
    var root = document.RootElement;
    if (!root.TryGetProperty("shared", out var shared)
        || !shared.TryGetProperty("frames", out var framesElement)
        || !root.TryGetProperty("profiles", out var profilesElement))
    {
        return false;
    }

    var frames = framesElement.EnumerateArray()
        .Select(frame => frame.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "")
        .ToArray();

    int matchedRows = 0;
    int profileCount = 0;
    foreach (var profile in profilesElement.EnumerateArray())
    {
        profileCount++;
        var type = profile.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (string.Equals(type, "evented", StringComparison.OrdinalIgnoreCase))
            matchedRows += CorrelateEventedSpeedscopeProfile(profile, frames, sourceKind, lookup);
        else if (string.Equals(type, "sampled", StringComparison.OrdinalIgnoreCase))
            matchedRows += CorrelateSampledSpeedscopeProfile(profile, frames, sourceKind, lookup);
    }

    summary.MatchedRows = matchedRows;
    summary.Observations.Add(profileCount == 0
        ? "speedscope JSON contained no profiles"
        : $"speedscope profiles: {profileCount.ToString(CultureInfo.InvariantCulture)}");
    summary.Observations.Add(matchedRows == 0
        ? "no static allocation candidate matched speedscope frames"
        : $"matched {matchedRows.ToString(CultureInfo.InvariantCulture)} static candidate row(s) from speedscope frames");
    return true;
}

static bool TrySummarizeCounters(string path, DiagnosticInputSummary summary)
{
    if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        return false;

    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
    var root = document.RootElement;
    if (!root.TryGetProperty("Events", out var eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
        return false;

    var totalAllocated = new List<double>();
    var genCollections = new List<double>();
    foreach (var item in eventsElement.EnumerateArray())
    {
        if (!item.TryGetProperty("name", out var nameElement)
            || !item.TryGetProperty("value", out var valueElement)
            || !valueElement.TryGetDouble(out var value))
        {
            continue;
        }

        var name = nameElement.GetString();
        if (string.Equals(name, "dotnet.gc.heap.total_allocated (By / 1 sec)", StringComparison.Ordinal))
            totalAllocated.Add(value);
        else if (string.Equals(name, "dotnet.gc.collections ({collection} / 1 sec)", StringComparison.Ordinal))
            genCollections.Add(value);
    }

    if (totalAllocated.Count == 0 && genCollections.Count == 0)
        return false;

    summary.Observations.Add($"counter events: {eventsElement.GetArrayLength().ToString(CultureInfo.InvariantCulture)}");
    if (totalAllocated.Count > 0)
    {
        var average = totalAllocated.Average();
        var maximum = totalAllocated.Max();
        summary.Observations.Add($"avg allocated/sec {FormatBytes((long)average)}, max {FormatBytes((long)maximum)}");
    }
    if (genCollections.Count > 0)
        summary.Observations.Add($"max GC collections/sec {genCollections.Max().ToString("0.##", CultureInfo.InvariantCulture)}");
    return true;
}

static bool TryCorrelateChromiumTrace(
    string path,
    string sourceKind,
    DiagnosticInputSummary summary,
    CandidateLookup lookup)
{
    if (!path.EndsWith(".chromium.json", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".trace.json", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
    var root = document.RootElement;
    if (!root.TryGetProperty("stackFrames", out var stackFramesElement)
        || !root.TryGetProperty("traceEvents", out var traceEventsElement))
    {
        return false;
    }

    var frames = ReadChromiumFrames(stackFramesElement);
    var stack = new List<int>();
    var weightsByFrame = new Dictionary<int, double>();
    double previousAt = 0;
    bool havePreviousAt = false;

    foreach (var item in traceEventsElement.EnumerateArray())
    {
        if (!item.TryGetProperty("ts", out var tsElement)
            || !tsElement.TryGetDouble(out var at))
        {
            at = previousAt;
        }

        if (havePreviousAt && at > previousAt)
        {
            var delta = at - previousAt;
            foreach (var frameIndex in stack)
                weightsByFrame[frameIndex] = weightsByFrame.GetValueOrDefault(frameIndex) + delta;
        }

        havePreviousAt = true;
        previousAt = at;

        if (!item.TryGetProperty("ph", out var phaseElement)
            || !item.TryGetProperty("sf", out var frameElement))
        {
            continue;
        }

        var phase = phaseElement.GetString();
        int frame = TryReadFrameId(frameElement);
        if (frame < 0)
            continue;

        if (string.Equals(phase, "B", StringComparison.OrdinalIgnoreCase))
        {
            stack.Add(frame);
        }
        else if (string.Equals(phase, "E", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i] == frame)
                {
                    stack.RemoveRange(i, stack.Count - i);
                    break;
                }
            }
        }
    }

    int matchedRows = MarkFrameWeights(weightsByFrame, frames, sourceKind, lookup);
    summary.MatchedRows = matchedRows;
    summary.Observations.Add($"chromium trace events: {traceEventsElement.GetArrayLength().ToString(CultureInfo.InvariantCulture)}");
    summary.Observations.Add(matchedRows == 0
        ? "no static allocation candidate matched chromium frames"
        : $"matched {matchedRows.ToString(CultureInfo.InvariantCulture)} static candidate row(s) from chromium frames");
    return true;
}

static string[] ReadChromiumFrames(JsonElement stackFramesElement)
{
    var frames = new Dictionary<int, string>();
    foreach (var property in stackFramesElement.EnumerateObject())
    {
        if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            continue;

        frames[id] = property.Value.TryGetProperty("name", out var name)
            ? name.GetString() ?? ""
            : "";
    }

    if (frames.Count == 0)
        return [];

    var result = new string[frames.Keys.Max() + 1];
    foreach (var (id, name) in frames)
        result[id] = name;
    return result;
}

static int TryReadFrameId(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        return number;
    if (element.ValueKind == JsonValueKind.String
        && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var textNumber))
    {
        return textNumber;
    }
    return -1;
}

static int CorrelateEventedSpeedscopeProfile(
    JsonElement profile,
    string[] frames,
    string sourceKind,
    CandidateLookup lookup)
{
    if (!profile.TryGetProperty("events", out var eventsElement))
        return 0;

    var stack = new List<int>();
    var weightsByFrame = new Dictionary<int, double>();
    double previousAt = 0;
    bool havePreviousAt = false;

    foreach (var item in eventsElement.EnumerateArray())
    {
        double at = item.TryGetProperty("at", out var atElement) && atElement.TryGetDouble(out var parsedAt)
            ? parsedAt
            : previousAt;

        if (havePreviousAt && at > previousAt)
        {
            var delta = at - previousAt;
            foreach (var frameIndex in stack)
                weightsByFrame[frameIndex] = weightsByFrame.GetValueOrDefault(frameIndex) + delta;
        }

        havePreviousAt = true;
        previousAt = at;

        if (!item.TryGetProperty("type", out var typeElement)
            || !item.TryGetProperty("frame", out var frameElement)
            || !frameElement.TryGetInt32(out var frame))
        {
            continue;
        }

        var type = typeElement.GetString();
        if (string.Equals(type, "O", StringComparison.OrdinalIgnoreCase))
        {
            stack.Add(frame);
        }
        else if (string.Equals(type, "C", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i] == frame)
                {
                    stack.RemoveRange(i, stack.Count - i);
                    break;
                }
            }
        }
    }

    return MarkFrameWeights(weightsByFrame, frames, sourceKind, lookup);
}

static int CorrelateSampledSpeedscopeProfile(
    JsonElement profile,
    string[] frames,
    string sourceKind,
    CandidateLookup lookup)
{
    if (!profile.TryGetProperty("samples", out var samplesElement))
        return 0;

    double[]? weights = null;
    if (profile.TryGetProperty("weights", out var weightsElement))
        weights = weightsElement.EnumerateArray().Select(w => w.TryGetDouble(out var value) ? value : 1).ToArray();

    var weightsByFrame = new Dictionary<int, double>();
    int sampleIndex = 0;
    foreach (var sample in samplesElement.EnumerateArray())
    {
        var weight = weights is not null && sampleIndex < weights.Length ? weights[sampleIndex] : 1;
        sampleIndex++;
        foreach (var frameElement in sample.EnumerateArray())
        {
            if (!frameElement.TryGetInt32(out var frame))
                continue;
            weightsByFrame[frame] = weightsByFrame.GetValueOrDefault(frame) + weight;
        }
    }

    return MarkFrameWeights(weightsByFrame, frames, sourceKind, lookup);
}

static int MarkFrameWeights(
    IReadOnlyDictionary<int, double> weightsByFrame,
    string[] frames,
    string sourceKind,
    CandidateLookup lookup)
{
    int matchedRows = 0;
    foreach (var (frame, weight) in weightsByFrame.OrderByDescending(pair => pair.Value))
    {
        if (frame < 0 || frame >= frames.Length || weight <= 0)
            continue;

        var matchedIds = new HashSet<int>();
        foreach (var candidate in lookup.FindByMethodText(frames[frame]))
            MarkHit(candidate, matchedIds, sourceKind, bytes: null, exactOffset: false, weight);
        matchedRows += matchedIds.Count;
    }

    return matchedRows;
}

static int CorrelateLine(string line, string sourceKind, CandidateLookup lookup)
{
    if (string.IsNullOrWhiteSpace(line))
        return 0;

    var bytes = TryReadBytes(line);
    var matchedIds = new HashSet<int>();
    foreach (Match match in Patterns.TokenOffset().Matches(line))
    {
        if (!TryParseHexInt(match.Groups["token"].Value, out var token)
            || !TryParseHexInt(match.Groups["offset"].Value, out var offset))
            continue;

        foreach (var candidate in lookup.FindByTokenOffset(token, offset))
            MarkHit(candidate, matchedIds, sourceKind, bytes, exactOffset: true, weight: 1);
    }

    var methodMatches = lookup.FindByMethodText(line);
    if (methodMatches.Count > 0)
    {
        int? ilOffset = null;
        var ilMatch = Patterns.IlOffset().Match(line);
        if (ilMatch.Success && TryParseHexInt(ilMatch.Groups["offset"].Value, out var parsedOffset))
            ilOffset = parsedOffset;

        foreach (var candidate in methodMatches)
        {
            if (ilOffset is int offset && candidate.IlOffset >= 0 && candidate.IlOffset != offset)
                continue;

            MarkHit(candidate, matchedIds, sourceKind, bytes, exactOffset: ilOffset is not null && candidate.IlOffset == ilOffset, weight: 1);
        }
    }

    return matchedIds.Count;
}

static void MarkHit(AllocationCandidate candidate, HashSet<int> matchedIds, string sourceKind, long? bytes, bool exactOffset, double weight)
{
    if (!matchedIds.Add(candidate.Id))
        return;

    candidate.RuntimeHits++;
    candidate.RuntimeWeight += weight;
    if (bytes is long value)
        candidate.RuntimeBytes += value;
    candidate.ObservedSources.Add(sourceKind);
    candidate.ExactOffsetObserved |= exactOffset;
}

static void MarkAllocationHit(AllocationCandidate candidate, HashSet<int> matchedIds, string sourceKind, string? allocatedType, long sampledBytes)
{
    if (!matchedIds.Add(candidate.Id))
        return;

    candidate.AllocationHits++;
    candidate.AllocationBytes += sampledBytes;
    candidate.RuntimeWeight += sampledBytes;
    candidate.ObservedSources.Add(sourceKind);
    if (!string.IsNullOrWhiteSpace(allocatedType))
    {
        candidate.ObservedAllocatedTypes[allocatedType] = candidate.ObservedAllocatedTypes.GetValueOrDefault(allocatedType) + sampledBytes;
        if (candidate.MatchesAllocatedType(allocatedType))
        {
            candidate.ShapeMatched = true;
            candidate.ShapeAllocationHits++;
            candidate.ShapeAllocationBytes += sampledBytes;
        }
    }
}

static long? TryReadBytes(string line)
{
    var match = Patterns.Bytes().Match(line);
    if (!match.Success)
        return null;

    if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        return null;

    var unit = match.Groups["unit"].Value.ToLowerInvariant();
    double multiplier = unit switch
    {
        "b" or "byte" or "bytes" => 1,
        "kb" or "kib" => 1024,
        "mb" or "mib" => 1024 * 1024,
        "gb" or "gib" => 1024 * 1024 * 1024,
        _ => 1
    };

    return (long)Math.Round(value * multiplier);
}

static bool IsTextLike(string path)
{
    var extension = Path.GetExtension(path).ToLowerInvariant();
    if (extension is ".txt" or ".log" or ".json" or ".csv" or ".speedscope" or ".xml" or ".md")
        return true;
    if (extension is ".nettrace" or ".gcdump" or ".dmp" or ".dump")
        return false;

    Span<byte> buffer = stackalloc byte[512];
    using var stream = File.OpenRead(path);
    int read = stream.Read(buffer);
    if (read == 0)
        return true;

    int suspicious = 0;
    for (int i = 0; i < read; i++)
    {
        byte b = buffer[i];
        if (b == 0)
            return false;
        if (b < 0x09 || (b > 0x0D && b < 0x20))
            suspicious++;
    }

    return suspicious * 5 < read;
}

static void Render(CorrelationResult result, CorrelateOptions options)
{
    var candidates = SelectCandidates(result.Candidates, options).ToArray();
    switch (options.Format)
    {
        case OutputFormat.Json:
            RenderJson(result, candidates);
            break;
        case OutputFormat.Jsonl:
            foreach (var candidate in candidates)
                WriteCandidateJsonLine(candidate, indented: false);
            break;
        case OutputFormat.Tsv:
            RenderSeparated(candidates, "\t");
            break;
        case OutputFormat.Table:
            RenderTable(candidates, markdown: false);
            break;
        case OutputFormat.PlainText:
            RenderPlainText(result, candidates);
            break;
        default:
            RenderMarkdown(result, candidates);
            break;
    }
}

static IEnumerable<AllocationCandidate> SelectCandidates(IReadOnlyList<AllocationCandidate> candidates, CorrelateOptions options)
{
    return candidates
        .Where(c => !options.ConfirmedOnly || c.IsObserved)
        .OrderByDescending(c => c.IsObserved)
        .ThenByDescending(c => c.ShapeMatched)
        .ThenByDescending(c => c.ShapeAllocationBytes)
        .ThenByDescending(c => c.AllocationHits)
        .ThenByDescending(c => c.ExactOffsetObserved)
        .ThenByDescending(c => c.RuntimeWeight)
        .ThenByDescending(c => c.RuntimeHits)
        .ThenByDescending(c => c.RuntimeBytes)
        .ThenByDescending(c => c.InLoop)
        .ThenBy(c => c.Method, StringComparer.Ordinal)
        .Take(options.Top);
}

static void RenderMarkdown(CorrelationResult result, IReadOnlyList<AllocationCandidate> candidates)
{
    var observed = candidates.Where(c => c.IsObserved).ToArray();
    var cold = result.Candidates.Where(c => !c.IsObserved).ToArray();
    var counterObservations = result.DiagnosticInputs
        .Where(static i => string.Equals(i.Kind, "counters", StringComparison.OrdinalIgnoreCase))
        .SelectMany(static i => i.Observations)
        .Where(static o => o.Contains("allocated/sec", StringComparison.OrdinalIgnoreCase)
            || o.Contains("GC collections/sec", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var observedGroups = observed
        .GroupBy(c => c.Method, StringComparer.Ordinal)
        .Select(g => new
        {
            Method = g.Key,
            Weight = g.Max(c => c.RuntimeWeight),
            Hits = g.Sum(c => c.RuntimeHits),
            AllocationHits = g.Max(c => c.EffectiveAllocationHits),
            AllocationBytes = g.Max(c => c.EffectiveObservedBytes),
            Rows = g.Count(),
            Shapes = string.Join(", ", g.Select(c => c.AllocationKind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            Confidence = HighestConfidence(g.Select(c => c.Confidence)),
            RootReach = g.Select(c => c.RootReach).FirstOrDefault(static r => !string.IsNullOrWhiteSpace(r)),
            Offsets = string.Join(", ", g.Select(c => DisplayHelpers.FormatOffset(c.IlOffset)).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct(StringComparer.Ordinal).Take(12)),
            Evidence = g.Select(c => c.Detail).FirstOrDefault(static e => !string.IsNullOrWhiteSpace(e)),
            Fix = g.Select(c => c.Fix).FirstOrDefault(static f => !string.IsNullOrWhiteSpace(f)),
            TopTypes = result.MethodAllocations.TryGetValue(g.Key, out var methodAllocations)
                ? methodAllocations.TopTypes(4)
                : "",
            Ambiguity = g.Any(c => c.RowAmbiguous) ? "row-ambiguous" : "row-likely",
            Exact = g.Any(c => c.ExactOffsetObserved),
            ShapeMatched = g.Any(c => c.ShapeMatched),
            InLoop = g.Any(c => c.InLoop),
        })
        .OrderByDescending(g => g.Weight)
        .ThenByDescending(g => g.Hits)
        .ToArray();

    Console.WriteLine("# runfaster performance report");
    Console.WriteLine();
    Console.WriteLine($"Static candidates: {result.Candidates.Count.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Runtime-observed candidates: {result.Candidates.Count(c => c.IsObserved).ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine();

    Console.WriteLine("## Conclusion");
    Console.WriteLine();
    if (observedGroups.Length == 0)
    {
        Console.WriteLine("No static performance candidate was observed in the supplied runtime diagnostics. Treat the selected workload as a negative confirmation, not as proof the code is never hot.");
    }
    else
    {
        var top = observedGroups[0];
        if (top.AllocationHits > 0)
        {
            var match = top.ShapeMatched ? "matching-shape allocation events" : "allocation events";
            Console.WriteLine($"The hottest observed candidate is `{top.Method}`. Raw `.nettrace` shows {top.AllocationHits.ToString(CultureInfo.InvariantCulture)} {match} under this method ({FormatBytes(top.AllocationBytes)} sampled), and static IL triage found {top.Rows.ToString(CultureInfo.InvariantCulture)} `{top.Shapes}` row(s){(top.InLoop ? " inside loops" : "")}.");
        }
        else
        {
            Console.WriteLine($"The hottest observed candidate is `{top.Method}`. Runtime samples put this method on-stack with weight {top.Weight.ToString("0.##", CultureInfo.InvariantCulture)}, and static IL triage found {top.Rows.ToString(CultureInfo.InvariantCulture)} `{top.Shapes}` row(s){(top.InLoop ? " inside loops" : "")}.");
        }
        if (!string.IsNullOrWhiteSpace(top.Fix))
            Console.WriteLine($"Recommended first fix: {top.Fix}");
        if (counterObservations.Length > 0)
            Console.WriteLine($"Runtime context: {string.Join("; ", counterObservations)}.");
        Console.WriteLine();
        Console.WriteLine(top.Exact
            ? "The runtime artifact matched at exact token+IL offset for at least one row."
            : top.ShapeMatched
                ? top.Ambiguity == "row-ambiguous"
                    ? "The runtime artifact confirms matching allocation types under the method, but multiple same-shape static rows share that evidence. Treat the method/shape as hot and inspect the listed offsets before choosing the exact edit."
                    : "The runtime artifact confirms matching allocation types under the method, and there is a single same-shape static row. This is strong row-level prioritization evidence; inspect/decompile the listed offset before changing code."
                : "The runtime artifact confirms the method, not the exact IL offset. Use this as a prioritization signal; inspect/decompile the listed offsets before changing code.");
    }
    Console.WriteLine();

    if (observedGroups.Length > 0)
    {
        Console.WriteLine("## Confirmed paydirt");
        Console.WriteLine();
        Console.WriteLine("| Runtime Evidence | Method | Static Rows | Row Confidence | Shape | Confidence | Loop | IL Offsets | Top Allocated Types | Why it matters | Fix |");
        Console.WriteLine("| ---------------- | ------ | ----------: | -------------- | ----- | ---------- | ---- | ---------- | ------------------- | -------------- | --- |");
        foreach (var group in observedGroups.Take(10))
        {
            Console.WriteLine("| "
                + string.Join(" | ",
                [
                    group.AllocationBytes > 0 ? $"{group.AllocationHits.ToString(CultureInfo.InvariantCulture)} alloc ticks / {FormatBytes(group.AllocationBytes)}" : $"sample weight {group.Weight.ToString("0.##", CultureInfo.InvariantCulture)}",
                    $"`{Escape(group.Method)}`",
                    group.Rows.ToString(CultureInfo.InvariantCulture),
                    Escape(group.Ambiguity),
                    Escape(group.Shapes),
                    Escape(group.Confidence ?? ""),
                    group.InLoop ? "yes" : "",
                    Escape(group.Offsets),
                    Escape(group.TopTypes),
                    Escape(group.Evidence ?? ""),
                    Escape(group.Fix ?? ""),
                ])
                + " |");
        }
        Console.WriteLine();
    }

    Console.WriteLine("## Inputs");
    Console.WriteLine();
    Console.WriteLine("| Kind | Path | Format | Lines | Matches | Observations |");
    Console.WriteLine("| ---- | ---- | ------ | -----: | ------: | ------------ |");
    foreach (var input in result.DiagnosticInputs)
    {
        Console.WriteLine($"| {Escape(input.Kind)} | `{Escape(Path.GetFileName(input.Path))}` | {(input.TextLike ? "text" : "binary")} | {input.LinesScanned} | {input.MatchedRows} | {Escape(string.Join("; ", input.Observations))} |");
    }

    Console.WriteLine();
    Console.WriteLine("## Observed candidate rows");
    Console.WriteLine();
    RenderTable(observed.Length > 0 ? observed : candidates, markdown: true);
    if (cold.Length > 0)
    {
        Console.WriteLine();
        Console.WriteLine("## Not exercised by this workload");
        Console.WriteLine();
        Console.WriteLine($"{cold.Length.ToString(CultureInfo.InvariantCulture)} static candidate row(s) were not observed in the supplied runtime artifacts. That is useful negative evidence for this workload, not a global dismissal.");
        Console.WriteLine();
        Console.WriteLine("| Shape | Rows |");
        Console.WriteLine("| ----- | ---: |");
        foreach (var shape in cold.GroupBy(static c => c.AllocationKind, StringComparer.Ordinal)
                     .OrderByDescending(static g => g.Count())
                     .ThenBy(static g => g.Key, StringComparer.Ordinal)
                     .Take(8))
        {
            Console.WriteLine($"| {Escape(shape.Key)} | {shape.Count().ToString(CultureInfo.InvariantCulture)} |");
        }
    }
    Console.WriteLine();
    Console.WriteLine("## Reading this report");
    Console.WriteLine();
    Console.WriteLine("- Static rows are IL-visible candidates from dotnet-inspect Performance Triage.");
    Console.WriteLine("- Runtime weight comes from the supplied diagnostics artifact. Speedscope and Chromium reports are sampled/profile weights; text reports are presence matches.");
    Console.WriteLine("- `method-hot` means the runtime artifact observed the method. `allocation-hot` means raw allocation events occurred with the method on-stack. `shape-hot` means the allocated type matched the static allocation shape. `shape-hot-ambiguous` means multiple same-shape rows in one method share the dynamic evidence. `confirmed-hot` means an exact token+IL coordinate was observed.");
}

static void RenderPlainText(CorrelationResult result, IReadOnlyList<AllocationCandidate> candidates)
{
    Console.WriteLine("runfaster prototype");
    Console.WriteLine($"Static candidates: {result.Candidates.Count.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Runtime-observed candidates: {result.Candidates.Count(c => c.IsObserved).ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine();
    RenderTable(candidates, markdown: false);
}

static void RenderTable(IReadOnlyList<AllocationCandidate> candidates, bool markdown)
{
    string[][] rows =
    [
        ["Status", "Hits", "Alloc", "Weight", "Bytes", "Token+IL", "Method", "Kind", "Loop", "Source"],
        .. candidates.Select(c => new[]
        {
            c.Status,
            c.RuntimeHits.ToString(CultureInfo.InvariantCulture),
            c.EffectiveAllocationHits == 0 ? "" : c.EffectiveAllocationHits.ToString(CultureInfo.InvariantCulture),
            c.EffectiveRuntimeWeight == 0 ? "" : c.EffectiveRuntimeWeight.ToString("0.##", CultureInfo.InvariantCulture),
            c.EffectiveObservedBytes == 0 ? "" : FormatBytes(c.EffectiveObservedBytes),
            c.TokenAndOffset,
            c.Method,
            c.AllocationKind,
            c.InLoop ? "yes" : "",
            string.Join(",", c.ObservedSources.Order(StringComparer.Ordinal))
        })
    ];

    if (markdown)
    {
        Console.WriteLine("| " + string.Join(" | ", rows[0].Select(Escape)) + " |");
        Console.WriteLine("| ------ | ---: | ----: | -----: | ----: | -------- | ------ | ---- | ---- | ------ |");
        foreach (var row in rows.Skip(1))
            Console.WriteLine("| " + string.Join(" | ", row.Select(Escape)) + " |");
        return;
    }

    var widths = new int[rows[0].Length];
    foreach (var row in rows)
    {
        for (int i = 0; i < row.Length; i++)
            widths[i] = Math.Max(widths[i], row[i].Length);
    }

    foreach (var row in rows)
    {
        for (int i = 0; i < row.Length; i++)
        {
            if (i > 0)
                Console.Write("  ");
            Console.Write(row[i].PadRight(widths[i]));
        }
        Console.WriteLine();
    }
}

static void RenderSeparated(IReadOnlyList<AllocationCandidate> candidates, string separator)
{
    Console.WriteLine(string.Join(separator, ["status", "hits", "allocation_hits", "weight", "bytes", "token_il", "method", "kind", "in_loop", "source"]));
    foreach (var candidate in candidates)
    {
        Console.WriteLine(string.Join(separator,
        [
            candidate.Status,
            candidate.RuntimeHits.ToString(CultureInfo.InvariantCulture),
            candidate.EffectiveAllocationHits.ToString(CultureInfo.InvariantCulture),
            candidate.EffectiveRuntimeWeight.ToString(CultureInfo.InvariantCulture),
            candidate.EffectiveObservedBytes.ToString(CultureInfo.InvariantCulture),
            candidate.TokenAndOffset,
            candidate.Method,
            candidate.AllocationKind,
            candidate.InLoop ? "true" : "false",
            string.Join(",", candidate.ObservedSources.Order(StringComparer.Ordinal))
        ]));
    }
}

static void RenderJson(CorrelationResult result, IReadOnlyList<AllocationCandidate> candidates)
{
    using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });
    writer.WriteStartObject();
    writer.WriteNumber("staticCandidates", result.Candidates.Count);
    writer.WriteNumber("observedCandidates", result.Candidates.Count(c => c.IsObserved));
    writer.WritePropertyName("inputs");
    writer.WriteStartArray();
    foreach (var input in result.DiagnosticInputs)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", input.Kind);
        writer.WriteString("path", input.Path);
        writer.WriteBoolean("textLike", input.TextLike);
        writer.WriteNumber("bytes", input.Bytes);
        writer.WriteNumber("linesScanned", input.LinesScanned);
        writer.WriteNumber("matchedRows", input.MatchedRows);
        writer.WritePropertyName("observations");
        writer.WriteStartArray();
        foreach (var observation in input.Observations)
            writer.WriteStringValue(observation);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WritePropertyName("candidates");
    writer.WriteStartArray();
    foreach (var candidate in candidates)
        WriteCandidateJsonObject(writer, candidate);
    writer.WriteEndArray();
    writer.WriteEndObject();
}

static void WriteCandidateJsonLine(AllocationCandidate candidate, bool indented)
{
    using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = indented });
    WriteCandidateJsonObject(writer, candidate);
    writer.Flush();
    Console.WriteLine();
}

static void WriteCandidateJsonObject(Utf8JsonWriter writer, AllocationCandidate candidate)
{
    writer.WriteStartObject();
    writer.WriteString("status", candidate.Status);
    writer.WriteString("source", candidate.Source);
    writer.WriteString("library", candidate.LibraryPath);
    writer.WriteString("assembly", candidate.AssemblyName);
    writer.WriteString("method", candidate.Method);
    writer.WriteString("tokenIl", candidate.TokenAndOffset);
    writer.WriteNumber("methodToken", candidate.MethodToken);
    writer.WriteNumber("ilOffset", candidate.IlOffset);
    writer.WriteString("kind", candidate.AllocationKind);
    if (candidate.Confidence is not null)
        writer.WriteString("confidence", candidate.Confidence);
    if (candidate.RootReach is not null)
        writer.WriteString("rootReach", candidate.RootReach);
    if (candidate.AllocatedType is not null)
        writer.WriteString("allocatedType", candidate.AllocatedType);
    if (candidate.Detail is not null)
        writer.WriteString("evidence", candidate.Detail);
    if (candidate.Fix is not null)
        writer.WriteString("fix", candidate.Fix);
    writer.WriteBoolean("inLoop", candidate.InLoop);
    writer.WriteNumber("runtimeHits", candidate.RuntimeHits);
    writer.WriteNumber("runtimeWeight", candidate.RuntimeWeight);
    writer.WriteNumber("runtimeBytes", candidate.RuntimeBytes);
    writer.WriteNumber("allocationHits", candidate.AllocationHits);
    writer.WriteNumber("allocationBytes", candidate.AllocationBytes);
    writer.WriteNumber("shapeAllocationHits", candidate.ShapeAllocationHits);
    writer.WriteNumber("shapeAllocationBytes", candidate.ShapeAllocationBytes);
    writer.WriteBoolean("shapeMatched", candidate.ShapeMatched);
    writer.WriteBoolean("rowAmbiguous", candidate.RowAmbiguous);
    writer.WriteNumber("sameMethodShapeRows", candidate.SameMethodShapeRows);
    writer.WritePropertyName("observedAllocatedTypes");
    writer.WriteStartArray();
    foreach (var (type, bytes) in candidate.ObservedAllocatedTypes.OrderByDescending(kv => kv.Value).Take(10))
    {
        writer.WriteStartObject();
        writer.WriteString("type", type);
        writer.WriteNumber("bytes", bytes);
        writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteBoolean("exactOffsetObserved", candidate.ExactOffsetObserved);
    writer.WritePropertyName("observedSources");
    writer.WriteStartArray();
    foreach (var source in candidate.ObservedSources.Order(StringComparer.Ordinal))
        writer.WriteStringValue(source);
    writer.WriteEndArray();
    writer.WriteEndObject();
}

static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

static string FormatBytes(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB"];
    double value = bytes;
    int unit = 0;
    while (value >= 1024 && unit + 1 < units.Length)
    {
        value /= 1024;
        unit++;
    }

    return unit == 0
        ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
        : $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {units[unit]}";
}

static string? HighestConfidence(IEnumerable<string?> values)
{
    var confidence = values
        .Where(static v => !string.IsNullOrWhiteSpace(v))
        .Select(static v => v!)
        .ToArray();
    if (confidence.Any(static v => string.Equals(v, "high", StringComparison.OrdinalIgnoreCase)))
        return "high";
    if (confidence.Any(static v => string.Equals(v, "medium", StringComparison.OrdinalIgnoreCase)))
        return "medium";
    if (confidence.Any(static v => string.Equals(v, "low", StringComparison.OrdinalIgnoreCase)))
        return "low";
    return confidence.FirstOrDefault();
}

static bool TryParseFlexibleInt(string? value, out int result)
{
    result = 0;
    if (string.IsNullOrWhiteSpace(value))
        return false;

    value = value.Trim();
    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        return int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

    if (value.StartsWith("IL_", StringComparison.OrdinalIgnoreCase))
        return int.TryParse(value[3..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

    if (value.Any(c => (c is >= 'a' and <= 'f') || (c is >= 'A' and <= 'F')))
        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}

static bool TryParseHexInt(string value, out int result)
{
    value = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
    return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
}

static string FullPath(string path) => Path.GetFullPath(path);

sealed record CorrelateOptions(
    IReadOnlyList<string> Libraries,
    IReadOnlyList<string> TriageFiles,
    IReadOnlyList<DiagnosticInput> Inputs,
    OutputFormat Format,
    int Top,
    int MaxLogLines,
    bool ConfirmedOnly);

sealed record DiagnosticInput(string Kind, string Path);

sealed record LibraryInput(string Path, int StaticCandidates, string? Error);

sealed record TriageInput(string Path, int Rows);

sealed class DiagnosticInputSummary(
    string kind,
    string path,
    long bytes,
    bool textLike,
    int linesScanned,
    int matchedRows,
    List<string> observations)
{
    public string Kind { get; } = kind;
    public string Path { get; } = path;
    public long Bytes { get; } = bytes;
    public bool TextLike { get; } = textLike;
    public int LinesScanned { get; set; } = linesScanned;
    public int MatchedRows { get; set; } = matchedRows;
    public List<string> Observations { get; } = observations;
}

sealed class CorrelationResult
{
    public List<LibraryInput> LibraryInputs { get; } = [];
    public List<TriageInput> TriageInputs { get; } = [];
    public List<DiagnosticInputSummary> DiagnosticInputs { get; } = [];
    public List<AllocationCandidate> Candidates { get; } = [];
    public Dictionary<string, MethodAllocationSummary> MethodAllocations { get; } = new(StringComparer.Ordinal);

    public void RecordMethodAllocation(string method, string? allocatedType, long bytes)
    {
        if (string.IsNullOrWhiteSpace(allocatedType))
            allocatedType = "(unknown)";

        if (!MethodAllocations.TryGetValue(method, out var summary))
        {
            summary = new MethodAllocationSummary(method);
            MethodAllocations.Add(method, summary);
        }

        summary.Record(allocatedType, bytes);
    }
}

sealed class MethodAllocationSummary(string method)
{
    readonly Dictionary<string, (int Count, long Bytes)> _types = new(StringComparer.Ordinal);

    public string Method { get; } = method;
    public int Count { get; private set; }
    public long Bytes { get; private set; }

    public void Record(string allocatedType, long bytes)
    {
        Count++;
        Bytes += bytes;
        var current = _types.GetValueOrDefault(allocatedType);
        _types[allocatedType] = (current.Count + 1, current.Bytes + bytes);
    }

    public string TopTypes(int count)
        => string.Join("; ", _types
            .OrderByDescending(static kv => kv.Value.Bytes)
            .ThenBy(static kv => kv.Key, StringComparer.Ordinal)
            .Take(count)
            .Select(static kv => $"{kv.Key} {FormatBytesLocal(kv.Value.Bytes)}"));

    static string FormatBytesLocal(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit + 1 < units.Length)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}

sealed class AllocationCandidate(
    int id,
    string source,
    string libraryPath,
    string assemblyName,
    Guid? moduleVersionId,
    int methodToken,
    int ilOffset,
    string method,
    string methodKey,
    string methodStackKey,
    string allocationKind,
    string? allocatedType,
    string? detail,
    bool inLoop,
    string? frequency,
    string? escape,
    string? confidence,
    string? fix,
    string? rootReach)
{
    public int Id { get; } = id;
    public string Source { get; } = source;
    public string LibraryPath { get; } = libraryPath;
    public string AssemblyName { get; } = assemblyName;
    public Guid? ModuleVersionId { get; } = moduleVersionId;
    public int MethodToken { get; } = methodToken;
    public int IlOffset { get; } = ilOffset;
    public string Method { get; } = method;
    public string MethodKey { get; } = methodKey;
    public string MethodStackKey { get; } = methodStackKey;
    public string AllocationKind { get; } = allocationKind;
    public string? AllocatedType { get; } = allocatedType;
    public string? Detail { get; } = detail;
    public bool InLoop { get; } = inLoop;
    public string? Frequency { get; } = frequency;
    public string? Escape { get; } = escape;
    public string? Confidence { get; } = confidence;
    public string? Fix { get; } = fix;
    public string? RootReach { get; } = rootReach;
    public int RuntimeHits { get; set; }
    public double RuntimeWeight { get; set; }
    public long RuntimeBytes { get; set; }
    public int AllocationHits { get; set; }
    public long AllocationBytes { get; set; }
    public int ShapeAllocationHits { get; set; }
    public long ShapeAllocationBytes { get; set; }
    public bool ShapeMatched { get; set; }
    public bool ExactOffsetObserved { get; set; }
    public int SameMethodShapeRows { get; set; } = 1;
    public bool RowAmbiguous => ShapeMatched && SameMethodShapeRows > 1 && !ExactOffsetObserved;
    public HashSet<string> ObservedSources { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> ObservedAllocatedTypes { get; } = new(StringComparer.Ordinal);
    public long TotalObservedBytes => RuntimeBytes + AllocationBytes;
    public long EffectiveObservedBytes => ShapeAllocationBytes > 0 ? ShapeAllocationBytes : TotalObservedBytes;
    public double EffectiveRuntimeWeight => ShapeAllocationBytes > 0 ? ShapeAllocationBytes : RuntimeWeight;
    public int EffectiveAllocationHits => ShapeAllocationHits > 0 ? ShapeAllocationHits : AllocationHits;
    public bool IsObserved => RuntimeHits > 0 || AllocationHits > 0;
    public string TokenAndOffset => $"{DisplayHelpers.FormatToken(MethodToken)}+{DisplayHelpers.FormatOffset(IlOffset)}";
    public string Status => ShapeMatched
        ? RowAmbiguous ? "shape-hot-ambiguous" : "shape-hot"
        : AllocationHits > 0
            ? "allocation-hot"
            : RuntimeHits == 0
        ? "cold-for-this-workload"
        : ExactOffsetObserved ? "confirmed-hot" : "method-hot";

    public bool MatchesAllocatedType(string allocatedType)
    {
        if (AllocatedType is { Length: > 0 } staticType
            && TypeNamesEquivalent(staticType, allocatedType))
        {
            return true;
        }

        if (Detail is null)
            return false;

        var detail = Detail.Trim();
        if (detail.StartsWith("box ", StringComparison.OrdinalIgnoreCase))
            return TypeNamesEquivalent(detail[4..].Trim(), allocatedType);

        if (detail.StartsWith("newarr ", StringComparison.OrdinalIgnoreCase))
        {
            var element = detail[7..].Trim();
            return TypeNamesEquivalent($"{element}[]", allocatedType);
        }

        if (string.Equals(AllocationKind, "string-build-in-loop", StringComparison.OrdinalIgnoreCase))
            return string.Equals(allocatedType, "System.String", StringComparison.Ordinal)
                || string.Equals(allocatedType, "System.Text.StringBuilder", StringComparison.Ordinal)
                || string.Equals(allocatedType, "System.Char[]", StringComparison.Ordinal);

        if (AllocationKind.Contains("delegate", StringComparison.OrdinalIgnoreCase))
            return allocatedType.Contains("Func`", StringComparison.Ordinal)
                || allocatedType.Contains("Action`", StringComparison.Ordinal)
                || allocatedType.EndsWith("Delegate", StringComparison.Ordinal);

        return false;
    }

    static bool TypeNamesEquivalent(string left, string right)
    {
        left = NormalizeTypeName(left);
        right = NormalizeTypeName(right);
        return string.Equals(left, right, StringComparison.Ordinal)
            || string.Equals(LeafTypeName(left), LeafTypeName(right), StringComparison.Ordinal);
    }

    static string NormalizeTypeName(string value)
        => value.Replace("class ", "", StringComparison.Ordinal)
            .Replace("value class ", "", StringComparison.Ordinal)
            .Replace("valuetype ", "", StringComparison.Ordinal)
            .Trim();

    static string LeafTypeName(string value)
    {
        int generic = value.IndexOf('<');
        if (generic >= 0)
            value = value[..generic];
        int dot = value.LastIndexOf('.');
        return dot >= 0 ? value[(dot + 1)..] : value;
    }

    public static AllocationCandidate FromOccurrence(int id, string path, AllocationOccurrence occurrence) => new(
        id,
        "library",
        path,
        occurrence.Method.AssemblyName,
        occurrence.Method.ModuleVersionId,
        occurrence.Method.MetadataToken,
        occurrence.ILOffset,
        DisplayHelpers.MethodDisplay(occurrence.Method),
        DisplayHelpers.MethodKey(occurrence.Method),
        DisplayHelpers.MethodStackKey(occurrence.Method),
        occurrence.Kind.ToString(),
        occurrence.AllocatedType?.ToQualifiedDisplayString(),
        occurrence.Detail,
        occurrence.InLoop,
        occurrence.Frequency.ToString(),
        occurrence.Escape.ToString(),
        null,
        null,
        null);
}

sealed class CandidateLookup
{
    readonly Dictionary<(int Token, int Offset), List<AllocationCandidate>> _byTokenOffset;
    readonly List<(string Fragment, AllocationCandidate Candidate)> _methodFragments;

    CandidateLookup(
        Dictionary<(int Token, int Offset), List<AllocationCandidate>> byTokenOffset,
        List<(string Fragment, AllocationCandidate Candidate)> methodFragments)
    {
        _byTokenOffset = byTokenOffset;
        _methodFragments = methodFragments;
    }

    public static CandidateLookup Create(IReadOnlyList<AllocationCandidate> candidates)
    {
        var byTokenOffset = new Dictionary<(int Token, int Offset), List<AllocationCandidate>>();
        var fragments = new List<(string Fragment, AllocationCandidate Candidate)>();
        foreach (var group in candidates.GroupBy(static c => (c.Method, c.AllocationKind)))
        {
            int count = group.Count();
            foreach (var candidate in group)
                candidate.SameMethodShapeRows = count;
        }

        foreach (var candidate in candidates)
        {
            if (candidate.MethodToken != 0 && candidate.IlOffset >= 0)
            {
                var key = (candidate.MethodToken, candidate.IlOffset);
                if (!byTokenOffset.TryGetValue(key, out var list))
                {
                    list = [];
                    byTokenOffset.Add(key, list);
                }
                list.Add(candidate);
            }

            AddFragment(candidate.MethodKey, candidate);
            AddFragment(candidate.MethodStackKey, candidate);
            AddFragment(candidate.Method, candidate);
            AddMethodLeafFragment(candidate.MethodKey, candidate);
            AddMethodLeafFragment(candidate.MethodStackKey, candidate);
        }

        return new CandidateLookup(byTokenOffset, fragments);

        void AddFragment(string value, AllocationCandidate candidate)
        {
            if (value.Length >= 8)
                fragments.Add((value, candidate));
        }

        void AddMethodLeafFragment(string value, AllocationCandidate candidate)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var normalized = value.Replace("::", ".", StringComparison.Ordinal);
            int paren = normalized.IndexOf('(');
            if (paren >= 0)
                normalized = normalized[..paren];

            int methodDot = normalized.LastIndexOf('.');
            if (methodDot <= 0 || methodDot == normalized.Length - 1)
                return;

            var methodName = normalized[(methodDot + 1)..];
            var declaringType = normalized[..methodDot];
            int typeDot = declaringType.LastIndexOf('.');
            var typeName = typeDot >= 0 ? declaringType[(typeDot + 1)..] : declaringType;
            AddFragment($"{typeName}.{methodName}", candidate);
        }
    }

    public IReadOnlyList<AllocationCandidate> FindByTokenOffset(int token, int offset)
        => _byTokenOffset.TryGetValue((token, offset), out var candidates) ? candidates : [];

    public List<AllocationCandidate> FindByMethodText(string line)
    {
        var candidates = new List<AllocationCandidate>();
        foreach (var (fragment, candidate) in _methodFragments)
        {
            if (line.Contains(fragment, StringComparison.Ordinal))
                candidates.Add(candidate);
        }

        return candidates;
    }
}

enum OutputFormat
{
    Markdown,
    PlainText,
    Table,
    Tsv,
    Jsonl,
    Json
}

static class DisplayHelpers
{
    public static string FormatToken(int token) => token == 0 ? "" : $"0x{token:X8}";

    public static string FormatOffset(int offset) => offset < 0 ? "" : $"IL_{offset:X4}";

    public static string MethodDisplay(MethodIdentity method)
    {
        var parameters = string.Join(", ", method.ParameterTypes.Select(p => p.ToQualifiedDisplayString()));
        return $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({parameters})";
    }

    public static string MethodKey(MethodIdentity method) => $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}";

    public static string MethodKeyFromText(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return "";

        int paren = method.IndexOf('(');
        return paren > 0 ? method[..paren] : method;
    }

    public static string MethodStackKey(MethodIdentity method) => $"{method.DeclaringType.ToQualifiedDisplayString()}::{method.Name}";

    public static string MethodStackKeyFromText(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return "";

        var key = MethodKeyFromText(method);
        int lastDot = key.LastIndexOf('.');
        return lastDot < 0 ? key : $"{key[..lastDot]}::{key[(lastDot + 1)..]}";
    }
}

static partial class Patterns
{
    [GeneratedRegex(@"(?:0x)?(?<token>06[0-9a-fA-F]{6})\s*(?:\+|:|@|\s+IL_?)\s*(?:0x|IL_)?(?<offset>[0-9a-fA-F]{1,6})", RegexOptions.CultureInvariant)]
    public static partial Regex TokenOffset();

    [GeneratedRegex(@"\bIL[_ ]?(?:0x)?(?<offset>[0-9a-fA-F]{1,6})\b", RegexOptions.CultureInvariant)]
    public static partial Regex IlOffset();

    [GeneratedRegex(@"\b(?<value>\d+(?:\.\d+)?)\s*(?<unit>bytes?|b|kb|kib|mb|mib|gb|gib)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex Bytes();
}
