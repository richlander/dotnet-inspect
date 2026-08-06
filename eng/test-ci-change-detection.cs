#:property ManagePackageVersionsCentrally=false
#:package YamlDotNet@18.1.0

using System.Diagnostics;
using YamlDotNet.RepresentationModel;

string repository = Environment.CurrentDirectory;
(string body, string[] outputs) = LoadDetectionBody(repository);

AssertAll(RunDetection(repository, body, "pull_request", "", outputs), "true");
AssertAll(RunDetection(repository, body, "push", "", outputs), "false");
AssertAll(
    RunDetection(
        repository,
        body,
        "push",
        "README.md",
        outputs,
        resolutionSucceeds: false),
    "true");
AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        reportedChangedFileCount: "2"),
    "true");
AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        reportedChangedFileCount:
            "999999999999999999999999999999999999"),
    "true");
AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        resolutionSucceeds: false),
    "true");
AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        malformedFileRecord: true),
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

Dictionary<string, string> pushedSource = RunDetection(
    repository,
    body,
    "push",
    "src/dotnet-inspect/Program.cs",
    outputs);
if (pushedSource["code"] != "true")
{
    throw new InvalidOperationException(
        $"Pushed source canary did not select code: {FormatValues(pushedSource)}");
}

Dictionary<string, string> unicodeSource = RunDetection(
    repository,
    body,
    "pull_request",
    "src/dotnet-inspect/\u00E9.cs",
    outputs);
if (unicodeSource["code"] != "true")
{
    throw new InvalidOperationException(
        $"Unicode source canary did not select code: {FormatValues(unicodeSource)}");
}

Dictionary<string, string> renamedBuildInput = RunDetection(
    repository,
    body,
    "pull_request",
    "notes/renamed.txt",
    outputs,
    previousFiles: "Directory.Build.props");
if (renamedBuildInput["code"] != "true")
{
    throw new InvalidOperationException(
        $"Renamed build-input canary did not select code: " +
        FormatValues(renamedBuildInput));
}

string brokenGhInvocation = body.Replace(
    "| @base64)'",
    ")'",
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
    using TextReader reader = File.OpenText(workflow);
    YamlStream yaml = [];
    yaml.Load(reader);
    if (yaml.Documents.Count != 1)
    {
        throw new InvalidOperationException(
            $"Expected one workflow document, found {yaml.Documents.Count}.");
    }

    YamlMappingNode root = RequireMapping(
        yaml.Documents[0].RootNode,
        "workflow root");
    RequireExactScalarValues(
        GetRequiredMapping(root, "env", "workflow"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_NOLOGO"] = "true",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
        },
        "workflow.env");
    RequireAbsent(root, "defaults", "workflow");
    YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "workflow");
    YamlMappingNode changes = GetRequiredMapping(jobs, "changes", "jobs");
    RequireAbsent(changes, "if", "jobs.changes");
    RequireAbsent(changes, "continue-on-error", "jobs.changes");
    RequireAbsent(changes, "defaults", "jobs.changes");
    RequireAbsent(changes, "env", "jobs.changes");

    YamlMappingNode outputMappings =
        GetRequiredMapping(changes, "outputs", "jobs.changes");
    List<string> declaredOutputs = [];
    foreach ((YamlNode keyNode, YamlNode valueNode) in outputMappings.Children)
    {
        string name = RequireScalar(keyNode, "jobs.changes output name");
        string binding = RequireScalar(
            valueNode,
            $"jobs.changes.outputs.{name} binding");
        string expectedBinding =
            "${{ steps.filter.outputs." + name + " }}";
        if (binding != expectedBinding)
        {
            throw new InvalidOperationException(
                $"Invalid jobs.changes.outputs.{name} binding.");
        }

        declaredOutputs.Add(name);
    }

    if (declaredOutputs.Count == 0)
    {
        throw new InvalidOperationException(
            "jobs.changes must declare at least one output.");
    }

    YamlSequenceNode steps = GetRequiredSequence(
        changes,
        "steps",
        "jobs.changes");
    if (steps.Children.Count < 2)
    {
        throw new InvalidOperationException(
            "jobs.changes must check out the repository before detection.");
    }

    YamlMappingNode checkoutStep = RequireMapping(
        steps.Children[0],
        "jobs.changes checkout step");
    RequireScalarValue(
        checkoutStep,
        "uses",
        "actions/checkout@v6",
        "jobs.changes checkout step");
    RequireScalarValue(
        GetRequiredMapping(
            checkoutStep,
            "with",
            "jobs.changes checkout step"),
        "fetch-depth",
        "0",
        "jobs.changes checkout step.with");

    List<(int Index, YamlMappingNode Step)> detectionSteps = [];
    List<(int Index, YamlMappingNode Step)> selfTestSteps = [];
    for (int index = 0; index < steps.Children.Count; index++)
    {
        YamlMappingNode step = RequireMapping(
            steps.Children[index],
            "jobs.changes step");
        if (GetOptionalScalar(step, "name") == "Detect changes")
        {
            detectionSteps.Add((index, step));
        }
        else if (GetOptionalScalar(step, "name") == "Self-test change detection")
        {
            selfTestSteps.Add((index, step));
        }
    }

    if (detectionSteps.Count != 1)
    {
        throw new InvalidOperationException(
            $"Expected one jobs.changes Detect changes step, " +
            $"found {detectionSteps.Count}.");
    }

    if (detectionSteps[0].Index != 1)
    {
        throw new InvalidOperationException(
            "Detect changes must run immediately after checkout.");
    }

    if (selfTestSteps.Count != 1 ||
        selfTestSteps[0].Index <= detectionSteps[0].Index)
    {
        throw new InvalidOperationException(
            "Self-test change detection must run once after Detect changes.");
    }

    YamlMappingNode selfTestStep = selfTestSteps[0].Step;
    RequireScalarValue(
        selfTestStep,
        "run",
        "dotnet run eng/test-ci-change-detection.cs",
        "Self-test change detection");
    RequireScalarValue(
        selfTestStep,
        "shell",
        "bash",
        "Self-test change detection");
    RequireExactScalarValues(
        GetRequiredMapping(
            selfTestStep,
            "env",
            "Self-test change detection"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BASH_ENV"] = "",
        },
        "Self-test change detection.env");
    RequireAbsent(selfTestStep, "if", "Self-test change detection");
    RequireAbsent(
        selfTestStep,
        "continue-on-error",
        "Self-test change detection");
    RequireAbsent(
        selfTestStep,
        "working-directory",
        "Self-test change detection");

    YamlMappingNode detectionStep = detectionSteps[0].Step;
    RequireScalarValue(detectionStep, "id", "filter", "Detect changes");
    RequireScalarValue(detectionStep, "shell", "bash", "Detect changes");
    RequireAbsent(detectionStep, "if", "Detect changes");
    RequireAbsent(detectionStep, "continue-on-error", "Detect changes");
    YamlMappingNode detectionEnvironment =
        GetRequiredMapping(detectionStep, "env", "Detect changes");
    RequireExactScalarValues(
        detectionEnvironment,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BASH_ENV"] = "",
            ["GH_TOKEN"] = "${{ github.token }}",
        },
        "Detect changes.env");
    string body = GetRequiredScalar(detectionStep, "run", "Detect changes");
    if (body.Length == 0)
    {
        throw new InvalidOperationException("Detect changes has an empty run block.");
    }

    return (body, declaredOutputs.ToArray());
}

static YamlMappingNode GetRequiredMapping(
    YamlMappingNode mapping,
    string key,
    string context) =>
    RequireMapping(GetRequiredNode(mapping, key, context), $"{context}.{key}");

static YamlSequenceNode GetRequiredSequence(
    YamlMappingNode mapping,
    string key,
    string context) =>
    GetRequiredNode(mapping, key, context) is YamlSequenceNode sequence
        ? sequence
        : throw new InvalidOperationException($"{context}.{key} must be a sequence.");

static string GetRequiredScalar(
    YamlMappingNode mapping,
    string key,
    string context) =>
    RequireScalar(GetRequiredNode(mapping, key, context), $"{context}.{key}");

static string? GetOptionalScalar(YamlMappingNode mapping, string key) =>
    TryGetNode(mapping, key, out YamlNode node)
        ? RequireScalar(node, key)
        : null;

static YamlNode GetRequiredNode(
    YamlMappingNode mapping,
    string key,
    string context) =>
    TryGetNode(mapping, key, out YamlNode node)
        ? node
        : throw new InvalidOperationException($"Could not find {context}.{key}.");

static bool TryGetNode(
    YamlMappingNode mapping,
    string key,
    out YamlNode value)
{
    foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
    {
        if (keyNode is YamlScalarNode scalar && scalar.Value == key)
        {
            value = valueNode;
            return true;
        }
    }

    value = null!;
    return false;
}

static YamlMappingNode RequireMapping(YamlNode node, string context) =>
    node as YamlMappingNode
    ?? throw new InvalidOperationException($"{context} must be a mapping.");

static string RequireScalar(YamlNode node, string context) =>
    node is YamlScalarNode { Value: string value }
        ? value
        : throw new InvalidOperationException($"{context} must be a scalar.");

static void RequireScalarValue(
    YamlMappingNode mapping,
    string key,
    string expected,
    string context)
{
    string actual = GetRequiredScalar(mapping, key, context);
    if (actual != expected)
    {
        throw new InvalidOperationException(
            $"{context}.{key} must be {expected}, got {actual}.");
    }
}

static void RequireExactScalarValues(
    YamlMappingNode mapping,
    IReadOnlyDictionary<string, string> expected,
    string context)
{
    if (mapping.Children.Count != expected.Count)
    {
        throw new InvalidOperationException(
            $"{context} must declare exactly: {string.Join(", ", expected.Keys)}.");
    }

    foreach ((string key, string value) in expected)
    {
        RequireScalarValue(mapping, key, value, context);
    }
}

static void RequireAbsent(
    YamlMappingNode mapping,
    string key,
    string context)
{
    if (TryGetNode(mapping, key, out _))
    {
        throw new InvalidOperationException($"{context} must not declare {key}.");
    }
}

static Dictionary<string, string> RunDetection(
    string repository,
    string body,
    string eventName,
    string files,
    IReadOnlyCollection<string> expectedOutputs,
    string previousFiles = "",
    string? reportedChangedFileCount = null,
    bool resolutionSucceeds = true,
    bool malformedFileRecord = false)
{
    const string Before = "1111111111111111111111111111111111111111";
    const string Sha = "2222222222222222222222222222222222222222";
    string rendered = body
        .Replace("${{ github.event_name }}", eventName, StringComparison.Ordinal)
        .Replace(
            "${{ github.event.pull_request.number }}",
            "3704",
            StringComparison.Ordinal)
        .Replace(
            "${{ github.repository }}",
            "richlander/dotnet-inspect",
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
        string standardOutputPath = Path.Combine(temporary, "stdout");
        string standardErrorPath = Path.Combine(temporary, "stderr");
        string binaries = Path.Combine(temporary, "bin");
        Directory.CreateDirectory(binaries);

        const string FakeGh = """
            #!/bin/sh
            if [ "$RESOLUTION_SUCCEEDS" != "true" ]; then
              exit 1
            fi
            if [ "$#" -eq 4 ] && [ "$1" = "api" ] \
               && [ "$2" = "repos/richlander/dotnet-inspect/pulls/3704" ] \
               && [ "$3" = "--jq" ] && [ "$4" = ".changed_files" ]; then
              if [ "$MALFORMED_FILE_RECORD" = "true" ]; then
              printf '2\n'
              elif [ -n "$REPORTED_CHANGED_FILE_COUNT" ]; then
              printf '%s\n' "$REPORTED_CHANGED_FILE_COUNT"
              elif [ -n "$PREVIOUS_FILES" ]; then
              printf '1\n'
              elif [ -z "$CHANGED_FILES" ]; then
              printf '0\n'
              else
              count=0
              while IFS= read -r file; do
                count=$((count + 1))
              done <<EOF
            $CHANGED_FILES
            EOF
              printf '%s\n' "$count"
              fi
              exit 0
            fi
            if [ "$#" -eq 5 ] && [ "$1" = "api" ] && [ "$2" = "--paginate" ] \
               && [ "$3" = "repos/richlander/dotnet-inspect/pulls/3704/files" ] \
               && [ "$4" = "--jq" ] \
               && [ "$5" = '.[] | select((.filename | type) == "string" and (.filename | length) > 0 and (.status == "added" or .status == "removed" or .status == "modified" or .status == "copied" or .status == "changed" or .status == "unchanged" or (.status == "renamed" and (.previous_filename | type) == "string" and (.previous_filename | length) > 0))) | ":file", ([.previous_filename?, .filename][] | select(. != null) | @base64)' ]; then
              if [ -n "$PREVIOUS_FILES" ]; then
              printf ':file\n'
              printf '%s' "$PREVIOUS_FILES" | base64 -w0
              printf '\n'
              printf '%s' "$CHANGED_FILES" | base64 -w0
              printf '\n'
              elif [ -n "$CHANGED_FILES" ]; then
              while IFS= read -r file; do
                printf ':file\n'
                printf '%s' "$file" | base64 -w0
                printf '\n'
              done <<EOF
            $CHANGED_FILES
            EOF
              fi
              exit 0
            fi
            echo "unexpected gh invocation: $*" >&2
            exit 64
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
            if [ "$#" -eq 6 ] && [ "$1" = "diff" ] && [ "$2" = "--no-renames" ] \
               && [ "$3" = "--name-only" ] && [ "$4" = "-z" ] \
               && [ "$5" = "$EXPECTED_BEFORE" ] && [ "$6" = "$EXPECTED_SHA" ]; then
              if [ -n "$CHANGED_FILES" ]; then
                while IFS= read -r file; do
                  printf '%s\0' "$file"
                done <<EOF
            $CHANGED_FILES
            EOF
              fi
              exit 0
            fi
            echo "unexpected git invocation: $*" >&2
            exit 64
            """;
        WriteExecutable(Path.Combine(binaries, "gh"), FakeGh);
        WriteExecutable(Path.Combine(binaries, "git"), FakeGit);

        ProcessStartInfo startInfo = new("bash")
        {
            UseShellExecute = false,
            WorkingDirectory = repository,
        };
        startInfo.ArgumentList.Add("--noprofile");
        startInfo.ArgumentList.Add("--norc");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            "exec bash --noprofile --norc -e -o pipefail -c " +
            "\"$1\" >\"$2\" 2>\"$3\"");
        startInfo.ArgumentList.Add("change-detection-wrapper");
        startInfo.ArgumentList.Add(rendered);
        startInfo.ArgumentList.Add(standardOutputPath);
        startInfo.ArgumentList.Add(standardErrorPath);
        startInfo.Environment["CHANGED_FILES"] = files;
        startInfo.Environment["EXPECTED_BEFORE"] = Before;
        startInfo.Environment["EXPECTED_SHA"] = Sha;
        startInfo.Environment["GITHUB_OUTPUT"] = output;
        startInfo.Environment["MALFORMED_FILE_RECORD"] =
            malformedFileRecord.ToString().ToLowerInvariant();
        startInfo.Environment["PATH"] =
            $"{binaries}{Path.PathSeparator}{startInfo.Environment["PATH"]}";
        startInfo.Environment["PREVIOUS_FILES"] = previousFiles;
        startInfo.Environment["REPORTED_CHANGED_FILE_COUNT"] =
            reportedChangedFileCount ?? "";
        startInfo.Environment["RESOLUTION_SUCCEEDS"] =
            resolutionSucceeds.ToString().ToLowerInvariant();

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Bash.");
        bool timedOut = !process.WaitForExit(milliseconds: 30_000);
        if (timedOut)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        string standardOutput = ReadIfExists(standardOutputPath);
        string standardError = ReadIfExists(standardErrorPath);
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

static string ReadIfExists(string path) =>
    File.Exists(path) ? File.ReadAllText(path) : "";

static void WriteExecutable(string path, string content)
{
    File.WriteAllText(path, content);
    File.SetUnixFileMode(
        path,
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute);
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
