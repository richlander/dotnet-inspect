using System.Diagnostics;

string repository = Environment.CurrentDirectory;
(string body, string[] outputs) = LoadDetectionBody(repository);

AssertAll(RunDetection(repository, body, "pull_request", "", outputs), "true");
AssertAll(RunDetection(repository, body, "push", "", outputs), "false");
AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        resolutionSucceeds: false),
    "true");

Dictionary<string, string> readme =
    RunDetection(repository, body, "pull_request", "README.md", outputs);
if (readme["code"] != "false" || readme["docs"] != "true")
{
    throw new InvalidOperationException(
        $"README.md canary did not discriminate: {FormatValues(readme)}");
}

Dictionary<string, string> source = RunDetection(
    repository,
    body,
    "pull_request",
    "src/dotnet-inspect/Program.cs",
    outputs);
if (source["code"] != "true")
{
    throw new InvalidOperationException(
        $"Source canary did not select code: {FormatValues(source)}");
}

string brokenGhInvocation = body.Replace(
    " --name-only)",
    ")",
    StringComparison.Ordinal);
if (brokenGhInvocation == body)
{
    throw new InvalidOperationException("Could not construct the gh invocation mutation.");
}

Dictionary<string, string> brokenGh = RunDetection(
    repository,
    brokenGhInvocation,
    "pull_request",
    "README.md",
    outputs);
if (brokenGh["code"] == "false" && brokenGh["docs"] == "true")
{
    throw new InvalidOperationException("The fake gh accepted a broken invocation.");
}

AssertDetectionFails(
    repository,
    $"false{Environment.NewLine}{body}",
    outputs);

Console.WriteLine("CI change detection fail-safe and path canaries passed.");

static (string Body, string[] Outputs) LoadDetectionBody(string repository)
{
    string workflow = Path.Combine(repository, ".github", "workflows", "ci.yml");
    string[] lines = File.ReadAllLines(workflow);

    int jobsLine = FindUniqueDirectLine(
        lines,
        0,
        lines.Length,
        0,
        "jobs:",
        "top-level jobs mapping");
    int jobsEnd = FindBlockEnd(lines, jobsLine, 0);
    int changesLine = FindUniqueDirectLine(
        lines,
        jobsLine + 1,
        jobsEnd,
        2,
        "changes:",
        "jobs.changes");
    int changesEnd = FindBlockEnd(lines, changesLine, 2);

    int outputsLine = FindUniqueDirectLine(
        lines,
        changesLine + 1,
        changesEnd,
        4,
        "outputs:",
        "jobs.changes.outputs");
    int outputsEnd = FindBlockEnd(lines, outputsLine, 4);
    List<string> declaredOutputs = [];
    for (int index = outputsLine + 1; index < outputsEnd; index++)
    {
        string line = lines[index];
        string trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed.StartsWith('#') ||
            CountIndent(line) != 6)
        {
            continue;
        }

        int separator = trimmed.IndexOf(':');
        if (separator <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid jobs.changes output on workflow line {index + 1}.");
        }

        string name = trimmed[..separator];
        string expectedBinding =
            name + ": ${{ steps.filter.outputs." + name + " }}";
        if (trimmed != expectedBinding)
        {
            throw new InvalidOperationException(
                $"Invalid jobs.changes.outputs.{name} binding.");
        }

        if (declaredOutputs.Contains(name, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Duplicate jobs.changes output: {name}.");
        }

        declaredOutputs.Add(name);
    }

    if (declaredOutputs.Count == 0)
    {
        throw new InvalidOperationException(
            "jobs.changes must declare at least one output.");
    }

    int stepsLine = FindUniqueDirectLine(
        lines,
        changesLine + 1,
        changesEnd,
        4,
        "steps:",
        "jobs.changes.steps");
    int stepsEnd = FindBlockEnd(lines, stepsLine, 4);
    int stepLine = FindUniqueDirectLine(
        lines,
        stepsLine + 1,
        stepsEnd,
        6,
        "- name: Detect changes",
        "jobs.changes Detect changes step");
    int stepEnd = FindBlockEnd(lines, stepLine, 6);
    FindUniqueDirectLine(
        lines,
        stepLine + 1,
        stepEnd,
        8,
        "id: filter",
        "Detect changes id");
    FindUniqueDirectLine(
        lines,
        stepLine + 1,
        stepEnd,
        8,
        "shell: bash",
        "Detect changes shell");
    RequireNoDirectKey(lines, stepLine + 1, stepEnd, 8, "if");
    RequireNoDirectKey(
        lines,
        stepLine + 1,
        stepEnd,
        8,
        "continue-on-error");
    int runLine = FindUniqueDirectLine(
        lines,
        stepLine + 1,
        stepEnd,
        8,
        "run: |",
        "Detect changes literal run block");

    int runIndent = CountIndent(lines[runLine]);
    int bodyIndent = runIndent + 2;
    List<string> body = [];
    for (int index = runLine + 1; index < lines.Length; index++)
    {
        string line = lines[index];
        if (line.Length == 0)
        {
            body.Add("");
            continue;
        }

        int indent = CountIndent(line);
        if (indent <= runIndent)
        {
            break;
        }

        if (indent < bodyIndent)
        {
            throw new InvalidOperationException(
                $"Invalid Detect changes indentation on workflow line {index + 1}.");
        }

        body.Add(line[bodyIndent..]);
    }

    if (body.Count == 0)
    {
        throw new InvalidOperationException("Detect changes has an empty run block.");
    }

    return (string.Join('\n', body), declaredOutputs.ToArray());
}

static Dictionary<string, string> RunDetection(
    string repository,
    string body,
    string eventName,
    string files,
    IReadOnlyCollection<string> expectedOutputs,
    bool resolutionSucceeds = true)
{
    const string Before = "1111111111111111111111111111111111111111";
    const string Sha = "2222222222222222222222222222222222222222";
    string rendered = body
        .Replace("${{ github.event_name }}", eventName, StringComparison.Ordinal)
        .Replace(
            "${{ github.event.pull_request.number }}",
            "3704",
            StringComparison.Ordinal)
        .Replace("${{ github.event.before }}", Before, StringComparison.Ordinal)
        .Replace("${{ github.sha }}", Sha, StringComparison.Ordinal);

    string temporary = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspect-ci-change-{Guid.NewGuid():N}");
    Directory.CreateDirectory(temporary);
    try
    {
        string output = Path.Combine(temporary, "github-output");
        string binaries = Path.Combine(temporary, "bin");
        Directory.CreateDirectory(binaries);

        const string FakeGh = """
            #!/bin/sh
            if [ "$#" -ne 4 ] || [ "$1" != "pr" ] || [ "$2" != "diff" ] \
               || [ "$3" != "3704" ] || [ "$4" != "--name-only" ]; then
              echo "unexpected gh invocation: $*" >&2
              exit 64
            fi
            if [ "$RESOLUTION_SUCCEEDS" != "true" ]; then
              exit 1
            fi
            printf '%s' "$CHANGED_FILES"
            """;
        const string FakeGit = """
            #!/bin/sh
            if [ "$RESOLUTION_SUCCEEDS" != "true" ]; then
              exit 1
            fi
            if [ "$#" -eq 3 ] && [ "$1" = "cat-file" ] && [ "$2" = "-e" ] \
               && [ "$3" = "${EXPECTED_BEFORE}^{commit}" ]; then
              exit 0
            fi
            if [ "$#" -eq 4 ] && [ "$1" = "diff" ] && [ "$2" = "--name-only" ] \
               && [ "$3" = "$EXPECTED_BEFORE" ] && [ "$4" = "$EXPECTED_SHA" ]; then
              printf '%s' "$CHANGED_FILES"
              exit 0
            fi
            echo "unexpected git invocation: $*" >&2
            exit 64
            """;
        WriteExecutable(Path.Combine(binaries, "gh"), FakeGh);
        WriteExecutable(Path.Combine(binaries, "git"), FakeGit);

        ProcessStartInfo startInfo = new("bash")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = repository,
        };
        startInfo.ArgumentList.Add("--noprofile");
        startInfo.ArgumentList.Add("--norc");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("pipefail");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(rendered);
        startInfo.Environment["CHANGED_FILES"] = files;
        startInfo.Environment["EXPECTED_BEFORE"] = Before;
        startInfo.Environment["EXPECTED_SHA"] = Sha;
        startInfo.Environment["GITHUB_OUTPUT"] = output;
        startInfo.Environment["PATH"] =
            $"{binaries}{Path.PathSeparator}{startInfo.Environment["PATH"]}";
        startInfo.Environment["RESOLUTION_SUCCEEDS"] =
            resolutionSucceeds.ToString().ToLowerInvariant();

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Bash.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        bool timedOut = !process.WaitForExit(milliseconds: 30_000);
        if (timedOut)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        string standardOutput = standardOutputTask.GetAwaiter().GetResult();
        string standardError = standardErrorTask.GetAwaiter().GetResult();
        if (timedOut)
        {
            throw new InvalidOperationException(
                "Change detection did not exit within 30 seconds.\n" +
                $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Change detection exited {process.ExitCode}\n" +
                $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(output))
        {
            int separator = line.IndexOf('=');
            if (separator <= 0 ||
                !values.TryAdd(line[..separator], line[(separator + 1)..]))
            {
                throw new InvalidOperationException(
                    $"Invalid or duplicate workflow output: {line}");
            }
        }

        if (!values.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedOutputs))
        {
            throw new InvalidOperationException(
                $"Expected outputs [{string.Join(", ", expectedOutputs)}], " +
                $"got {FormatValues(values)}.");
        }

        return values;
    }
    finally
    {
        Directory.Delete(temporary, recursive: true);
    }
}

static int FindUniqueDirectLine(
    string[] lines,
    int start,
    int end,
    int indent,
    string content,
    string description)
{
    int match = -1;
    for (int index = start; index < end; index++)
    {
        if (CountIndent(lines[index]) != indent ||
            lines[index].Trim() != content)
        {
            continue;
        }

        if (match >= 0)
        {
            throw new InvalidOperationException(
                $"Expected one {description}, found more than one.");
        }

        match = index;
    }

    if (match < 0)
    {
        throw new InvalidOperationException($"Could not find {description}.");
    }

    return match;
}

static int FindBlockEnd(string[] lines, int start, int indent)
{
    for (int index = start + 1; index < lines.Length; index++)
    {
        string trimmed = lines[index].TrimStart();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            continue;
        }

        if (CountIndent(lines[index]) <= indent)
        {
            return index;
        }
    }

    return lines.Length;
}

static void RequireNoDirectKey(
    string[] lines,
    int start,
    int end,
    int indent,
    string key)
{
    for (int index = start; index < end; index++)
    {
        if (CountIndent(lines[index]) == indent &&
            lines[index].TrimStart().StartsWith(
                key + ":",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Detect changes must not declare {key}.");
        }
    }
}

static void WriteExecutable(string path, string content)
{
    File.WriteAllText(path, content);
    File.SetUnixFileMode(
        path,
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute);
}

static int CountIndent(string line)
{
    int indent = 0;
    while (indent < line.Length && line[indent] == ' ')
    {
        indent++;
    }

    if (indent < line.Length && line[indent] == '\t')
    {
        throw new InvalidOperationException("Workflow indentation must use spaces.");
    }

    return indent;
}

static void AssertAll(Dictionary<string, string> values, string expected)
{
    if (values.Values.Any(value => value != expected))
    {
        throw new InvalidOperationException(
            $"Expected every output to be {expected}, got {FormatValues(values)}.");
    }
}

static void AssertDetectionFails(
    string repository,
    string body,
    IReadOnlyCollection<string> expectedOutputs)
{
    try
    {
        RunDetection(
            repository,
            body,
            "pull_request",
            "README.md",
            expectedOutputs);
    }
    catch (InvalidOperationException exception)
        when (exception.Message.StartsWith(
            "Change detection exited ",
            StringComparison.Ordinal))
    {
        return;
    }

    throw new InvalidOperationException(
        "The Actions-compatible shell did not stop after a failed command.");
}

static string FormatValues(Dictionary<string, string> values) =>
    $"[{string.Join(", ", values.Select(item => $"{item.Key}={item.Value}"))}]";
