#:property ManagePackageVersionsCentrally=false
#:package YamlDotNet@18.1.0

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
foreach ((string json, string count) in new[]
{
    ("[null]", "1"),
    ("[{\"status\":\"modified\"}]", "1"),
    ("[{\"status\":\"modified\",\"filename\":[\"src/a.cs\"]}]", "1"),
    ("[" +
        "{\"status\":\"modified\",\"filename\":\"README.md\"}," +
        "{\"status\":\"modified\",\"filename\":\"\"}" +
        "]", "2"),
    ("[{\"filename\":\"src/missing-status.cs\"}]", "1"),
    ("[{\"status\":\"\",\"filename\":\"src/empty-status.cs\"}]", "1"),
    ("[{\"status\":\"renamed\",\"filename\":\"Directory.Build.props.moved\"}]",
        "1"),
    ("[{" +
        "\"status\":\"renamed\"," +
        "\"previous_filename\":\"\"," +
        "\"filename\":\"src/new.cs\"" +
        "}]", "1"),
    ("[{" +
        "\"status\":\"modified\"," +
        "\"previous_filename\":1," +
        "\"filename\":\"README.md\"" +
        "}]", "1"),
    ("[{" +
        "\"status\":\"modified\"," +
        "\"previous_filename\":\"\"," +
        "\"filename\":\"README.md\"" +
        "}]", "1"),
})
{
    Dictionary<string, string> malformed = RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        reportedChangedFileCount: count,
        malformedFileRecordJson: json);
    AssertAll(malformed, "true");
}
AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        objectShapedFilePage: true),
    "true");
AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        nulFileRecord: true),
    "true");
AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        nulPreviousFileRecord: true),
    "true");
AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        failDecodeAt: 2),
    "true");
AssertAll(
    RunDetection(
        repository,
        body,
        "push",
        "src/dotnet-inspect/Program.cs",
        outputs,
        failDecodeAt: 1),
    "true");

Dictionary<string, string> readme =
    RunDetection(repository, body, "pull_request", "README.md", outputs);
if (readme["code"] != "false" || readme["docs"] != "true")
{
    throw new InvalidOperationException(
        $"README.md canary did not discriminate: {FormatValues(readme)}");
}

foreach (string status in new[]
{
    "added",
    "removed",
    "modified",
    "copied",
    "changed",
    "unchanged",
})
{
    Dictionary<string, string> statusResult = RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        fileStatus: status);
    if (statusResult["code"] != "false" ||
        statusResult["docs"] != "true")
    {
        throw new InvalidOperationException(
            $"{status} file-record canary did not discriminate: " +
            FormatValues(statusResult));
    }
}

AssertAll(
    RunDetection(
        repository,
        body,
        "pull_request",
        "README.md",
        outputs,
        fileStatus: "future"),
    "true");

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
AssertRouting(source, selected: "shipped", notSelected: "csharpdiff");

Dictionary<string, string> csharpDiff = RunDetection(
    repository,
    body,
    "pull_request",
    "tools/CSharpDiffHarness/Program.cs",
    outputs);
AssertRouting(csharpDiff, selected: "csharpdiff", notSelected: "code");

Dictionary<string, string> decompiler = RunDetection(
    repository,
    body,
    "pull_request",
    "eng/check-decompiler-gate.cs",
    outputs);
AssertRouting(decompiler, selected: "decompiler", notSelected: "code");

Dictionary<string, string> ilDiff = RunDetection(
    repository,
    body,
    "pull_request",
    "tools/IlDiffHarness/Program.cs",
    outputs);
AssertRouting(ilDiff, selected: "ildiff", notSelected: "code");

Dictionary<string, string> ilRoundtrip = RunDetection(
    repository,
    body,
    "pull_request",
    "eng/restore-ilassembler.sh",
    outputs);
AssertRouting(ilRoundtrip, selected: "ilroundtrip", notSelected: "docs");
if (ilRoundtrip["code"] != "true")
{
    throw new InvalidOperationException(
        $"IL round-trip canary did not start its containing test job: " +
        FormatValues(ilRoundtrip));
}

Dictionary<string, string> packaging = RunDetection(
    repository,
    body,
    "pull_request",
    "src/dotnet-inspect/dotnet-inspect.csproj",
    outputs);
AssertRouting(packaging, selected: "packaging", notSelected: "docs");

Dictionary<string, string> skill = RunDetection(
    repository,
    body,
    "pull_request",
    "skills/new-skill/SKILL.md",
    outputs);
if (skill["code"] != "true" || skill["docs"] != "true")
{
    throw new InvalidOperationException(
        $"Skill canary did not select code and docs: {FormatValues(skill)}");
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
    "| @base64)",
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
    ValidateAggregateStructuralCheck(jobs);
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
    string[] requiredOutputs =
    [
        "code",
        "csharpdiff",
        "decompiler",
        "docs",
        "ildiff",
        "ilroundtrip",
        "packaging",
        "shipped",
    ];
    if (!declaredOutputs.ToHashSet(StringComparer.Ordinal)
        .SetEquals(requiredOutputs))
    {
        throw new InvalidOperationException(
            $"jobs.changes must declare exactly: {string.Join(", ", requiredOutputs)}.");
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
    RequireExactKeys(
        checkoutStep,
        ["uses", "with"],
        "jobs.changes checkout step");
    RequireScalarValue(
        checkoutStep,
        "uses",
        "actions/checkout@v6",
        "jobs.changes checkout step");
    RequireExactScalarValues(
        GetRequiredMapping(
            checkoutStep,
            "with",
            "jobs.changes checkout step"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fetch-depth"] = "0",
        },
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

    if (detectionSteps[0].Index != 3)
    {
        throw new InvalidOperationException(
            "Detect changes must run after checkout, .NET setup, and EVIL provenance validation.");
    }

    YamlMappingNode setupStep = RequireMapping(
        steps.Children[1],
        "jobs.changes .NET setup step");
    RequireExactKeys(
        setupStep,
        ["uses", "with"],
        "jobs.changes .NET setup step");
    RequireScalarValue(
        setupStep,
        "uses",
        "actions/setup-dotnet@v5",
        "jobs.changes .NET setup step");
    RequireExactScalarValues(
        GetRequiredMapping(
            setupStep,
            "with",
            "jobs.changes .NET setup step"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dotnet-version"] = "11.0.x",
        },
        "jobs.changes .NET setup step.with");

    YamlMappingNode provenanceStep = RequireMapping(
        steps.Children[2],
        "jobs.changes EVIL provenance step");
    RequireExactKeys(
        provenanceStep,
        ["name", "shell", "run"],
        "jobs.changes EVIL provenance step");
    RequireScalarValue(
        provenanceStep,
        "name",
        "Check EVIL history provenance",
        "jobs.changes EVIL provenance step");
    RequireScalarValue(
        provenanceStep,
        "shell",
        "bash",
        "jobs.changes EVIL provenance step");
    RequireScalarSha256(
        provenanceStep,
        "run",
        "F11C4A162CF21AC57E0BAAE405672EB0AA305896A34F293BE1C27248721B9B77",
        "jobs.changes EVIL provenance step");
    RequireAbsent(
        provenanceStep,
        "if",
        "jobs.changes EVIL provenance step");
    RequireAbsent(
        provenanceStep,
        "continue-on-error",
        "jobs.changes EVIL provenance step");
    RequireAbsent(
        provenanceStep,
        "working-directory",
        "jobs.changes EVIL provenance step");

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

static void ValidateAggregateStructuralCheck(YamlMappingNode jobs)
{
    YamlMappingNode aggregate = GetRequiredMapping(
        jobs,
        "ci-required",
        "jobs");
    RequireScalarValue(
        aggregate,
        "if",
        "always()",
        "jobs.ci-required");
    RequireAbsent(
        aggregate,
        "continue-on-error",
        "jobs.ci-required");
    YamlMappingNode aggregateEnvironment = GetRequiredMapping(
        aggregate,
        "env",
        "jobs.ci-required");
    RequireExactKeys(
        aggregateEnvironment,
        ["RESULT_FILTER"],
        "jobs.ci-required.env");
    RequireScalarSha256(
        aggregateEnvironment,
        "RESULT_FILTER",
        "D074F21341F3416A1D7FE48A0374CB69C59F52313ADF61FF732E930CFF0AEF29",
        "jobs.ci-required.env");
    YamlSequenceNode needs = GetRequiredSequence(
        aggregate,
        "needs",
        "jobs.ci-required");
    var actualNeeds = needs.Children
        .Select(node => RequireScalar(node, "jobs.ci-required need"))
        .ToHashSet(StringComparer.Ordinal);
    var expectedNeeds = jobs.Children.Keys
        .Select(node => RequireScalar(node, "job name"))
        .Where(name => name != "ci-required")
        .ToHashSet(StringComparer.Ordinal);
    if (!actualNeeds.SetEquals(expectedNeeds))
    {
        throw new InvalidOperationException(
            "jobs.ci-required.needs must contain every other job exactly once.");
    }
    YamlSequenceNode steps = GetRequiredSequence(
        aggregate,
        "steps",
        "jobs.ci-required");
    var namedSteps = new Dictionary<string, (int Index, YamlMappingNode Step)>(
        StringComparer.Ordinal);
    int stepIndex = 0;
    foreach (YamlNode stepNode in steps.Children)
    {
        YamlMappingNode step = RequireMapping(
            stepNode,
            "jobs.ci-required step");
        string? name = GetOptionalScalar(step, "name");
        if (name is not null &&
            !namedSteps.TryAdd(name, (stepIndex, step)))
        {
            throw new InvalidOperationException(
                $"jobs.ci-required contains duplicate step name: {name}.");
        }
        stepIndex++;
    }

    string[] requiredStepNames =
    [
        "Verify this gate depends on every other job",
        "Self-test the result filter",
        "Verify no required job failed or was cancelled",
    ];
    foreach (string name in requiredStepNames)
    {
        if (!namedSteps.ContainsKey(name))
        {
            throw new InvalidOperationException(
                $"jobs.ci-required is missing step: {name}.");
        }
    }
    if (!(namedSteps[requiredStepNames[0]].Index <
          namedSteps[requiredStepNames[1]].Index &&
          namedSteps[requiredStepNames[1]].Index <
          namedSteps[requiredStepNames[2]].Index))
    {
        throw new InvalidOperationException(
            "jobs.ci-required enforcement steps are out of order.");
    }

    YamlMappingNode check = namedSteps[requiredStepNames[0]].Step;
    RequireExactKeys(
        check,
        ["name", "env", "run"],
        "ci-required structural check");
    RequireExactScalarValues(
        GetRequiredMapping(
            check,
            "env",
            "ci-required structural check"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NEEDS"] = "${{ toJSON(needs) }}",
        },
        "ci-required structural check.env");
    RequireAbsent(check, "if", "ci-required structural check");
    RequireAbsent(
        check,
        "continue-on-error",
        "ci-required structural check");
    RequireScalarSha256(
        check,
        "run",
        "2DE85E60935EDA6C488053ACD405D02C4842F620AA0E0307E6E64166AF1A7DF8",
        "ci-required structural check");

    YamlMappingNode filterSelfTest = namedSteps[requiredStepNames[1]].Step;
    RequireExactKeys(
        filterSelfTest,
        ["name", "run"],
        "ci-required result-filter self-test");
    RequireScalarSha256(
        filterSelfTest,
        "run",
        "7BE0D6B90EB8A915BB17ED8BC6B6DA3371197D69E5F98262D854330985E7E5BD",
        "ci-required result-filter self-test");

    YamlMappingNode resultCheck = namedSteps[requiredStepNames[2]].Step;
    RequireExactKeys(
        resultCheck,
        ["name", "env", "run"],
        "ci-required result check");
    RequireExactScalarValues(
        GetRequiredMapping(
            resultCheck,
            "env",
            "ci-required result check"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NEEDS"] = "${{ toJSON(needs) }}",
        },
        "ci-required result check.env");
    RequireScalarSha256(
        resultCheck,
        "run",
        "8A91AD84EA333837F96705184446A0E7815286B01B21FA41C7998D6CCAFAC648",
        "ci-required result check");
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

static void RequireExactKeys(
    YamlMappingNode mapping,
    IReadOnlyCollection<string> expected,
    string context)
{
    var actual = mapping.Children.Keys
        .Select(key => ((YamlScalarNode)key).Value ?? "")
        .ToHashSet(StringComparer.Ordinal);
    if (!actual.SetEquals(expected))
    {
        throw new InvalidOperationException(
            $"{context} must declare exactly: {string.Join(", ", expected)}.");
    }
}

static void RequireScalarSha256(
    YamlMappingNode mapping,
    string key,
    string expected,
    string context)
{
    string actual = GetRequiredScalar(mapping, key, context);
    string hash = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
    if (hash != expected)
    {
        throw new InvalidOperationException(
            $"{context}.{key} SHA-256 must be {expected}, got {hash}.");
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
    string malformedFileRecordJson = "",
    bool objectShapedFilePage = false,
    bool nulFileRecord = false,
    bool nulPreviousFileRecord = false,
    string fileStatus = "modified",
    int failDecodeAt = 0)
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
              if [ -n "$REPORTED_CHANGED_FILE_COUNT" ]; then
              printf '%s\n' "$REPORTED_CHANGED_FILE_COUNT"
              elif [ -n "$MALFORMED_FILE_RECORD_JSON" ]; then
              printf '1\n'
              elif [ "$NUL_FILE_RECORD" = "true" ]; then
              printf '1\n'
              elif [ "$NUL_PREVIOUS_FILE_RECORD" = "true" ]; then
              printf '1\n'
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
               && [ "$4" = "--jq" ]; then
              records=$(
                if [ "$OBJECT_SHAPED_FILE_PAGE" = "true" ]; then
                  printf '%s\n' \
                    '{"a":{"status":"modified","filename":"README.md"}}'
                elif [ -n "$MALFORMED_FILE_RECORD_JSON" ]; then
                  printf '%s\n' "$MALFORMED_FILE_RECORD_JSON"
                elif [ "$NUL_FILE_RECORD" = "true" ]; then
                  printf '%s\n' \
                    '[{"status":"modified","filename":"s\u0000rc/Program.cs"}]'
                elif [ "$NUL_PREVIOUS_FILE_RECORD" = "true" ]; then
                  printf '%s\n' \
                    '[{"status":"modified","previous_filename":"sr\u0000c/Program.cs","filename":"notes/payload.bin"}]'
                elif [ -n "$PREVIOUS_FILES" ]; then
                  jq -cn \
                    --arg previous "$PREVIOUS_FILES" \
                    --arg filename "$CHANGED_FILES" \
                    '[{
                      status: "renamed",
                      previous_filename: $previous,
                      filename: $filename
                    }]'
                elif [ -n "$CHANGED_FILES" ]; then
                  printf '%s\n' "$CHANGED_FILES" |
                    jq -Rsc --arg status "$FILE_STATUS" '
                      split("\n")
                      | map(
                          select(length > 0)
                          | {status: $status, filename: .}
                        )
                    '
                else
                  printf '[]\n'
                fi
              ) || exit 1
              printf '%s\n' "$records" | jq -r "$5"
              exit $?
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
        if (failDecodeAt > 0)
        {
            const string FakeBase64 = """
                #!/bin/sh
                if [ "$1" = "--decode" ]; then
                  count=0
                  if [ -f "$BASE64_DECODE_COUNT" ]; then
                    IFS= read -r count < "$BASE64_DECODE_COUNT"
                  fi
                  count=$((count + 1))
                  printf '%s\n' "$count" > "$BASE64_DECODE_COUNT"
                  if [ "$FAIL_DECODE_AT" = "$count" ]; then
                    exit 1
                  fi
                fi
                exec /usr/bin/base64 "$@"
                """;
            WriteExecutable(Path.Combine(binaries, "base64"), FakeBase64);
        }

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
        startInfo.Environment["BASH_ENV"] = "";
        startInfo.Environment["CHANGED_FILES"] = files;
        startInfo.Environment["EXPECTED_BEFORE"] = Before;
        startInfo.Environment["EXPECTED_SHA"] = Sha;
        startInfo.Environment["FILE_STATUS"] = fileStatus;
        startInfo.Environment["GITHUB_OUTPUT"] = output;
        startInfo.Environment["BASE64_DECODE_COUNT"] =
            Path.Combine(temporary, "base64-decode-count");
        startInfo.Environment["FAIL_DECODE_AT"] =
            failDecodeAt.ToString();
        startInfo.Environment["MALFORMED_FILE_RECORD_JSON"] =
            malformedFileRecordJson;
        startInfo.Environment["OBJECT_SHAPED_FILE_PAGE"] =
            objectShapedFilePage.ToString().ToLowerInvariant();
        startInfo.Environment["NUL_FILE_RECORD"] =
            nulFileRecord.ToString().ToLowerInvariant();
        startInfo.Environment["NUL_PREVIOUS_FILE_RECORD"] =
            nulPreviousFileRecord.ToString().ToLowerInvariant();
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

static void AssertRouting(
    Dictionary<string, string> values,
    string selected,
    string notSelected)
{
    if (values[selected] != "true" || values[notSelected] != "false")
    {
        throw new InvalidOperationException(
            $"Expected {selected}=true and {notSelected}=false, got " +
            FormatValues(values));
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
