#:property ManagePackageVersionsCentrally=false
#:package YamlDotNet@18.1.0

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        reportedChangedFileCount: "1",
        changedFileCountIsString: true),
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
    ("[" +
        "{\"status\":\"modified\",\"filename\":\"README.md\"}," +
        "{\"status\":\"modified\",\"filename\":\"README.md\"}" +
        "]", "2"),
    ("[{" +
        "\"status\":\"renamed\"," +
        "\"previous_filename\":\"README.md\"," +
        "\"filename\":\"README.md\"" +
        "}]", "1"),
    ("[" +
        "{\"status\":\"renamed\",\"previous_filename\":\"src/old.cs\"," +
        "\"filename\":\"src/new-a.cs\"}," +
        "{\"status\":\"renamed\",\"previous_filename\":\"src/old.cs\"," +
        "\"filename\":\"src/new-b.cs\"}" +
        "]", "2"),
    ("[{\"status\":\"modified\",\"filename\":\"/src/Program.cs\"}]", "1"),
    ("[{\"status\":\"modified\",\"filename\":\"src/\"}]", "1"),
    ("[{\"status\":\"modified\",\"filename\":\"src//Program.cs\"}]", "1"),
    ("[{\"status\":\"modified\",\"filename\":\"./src/Program.cs\"}]", "1"),
    ("[{\"status\":\"modified\",\"filename\":\"../src/Program.cs\"}]", "1"),
    ("[{\"status\":\"modified\",\"filename\":\"src/./Program.cs\"}]", "1"),
    ("[{\"status\":\"modified\",\"filename\":\"src/../Program.cs\"}]", "1"),
    ("[{\"status\":\"modified\",\"filename\":\"src/.\"}]", "1"),
    ("[{\"status\":\"modified\",\"filename\":\"src/..\"}]", "1"),
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
        truncateRecordStream: true),
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
        "push",
        "src/dotnet-inspect/Program.cs",
        outputs,
        truncatePushStream: true),
    "true");
AssertAll(
    RunDetection(
        repository,
        body,
        "push",
        "",
        outputs,
        emptyPushRecord: true),
    "true");
foreach (int decode in new[] { 1, 2, 3 })
{
    AssertAll(
        RunDetection(
            repository,
            body,
            "pull_request",
            "README.md",
            outputs,
            failDecodeAt: decode),
        "true");
}
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
if (source["code"] != "true" || source["web"] != "false")
{
    throw new InvalidOperationException(
        $"CLI source canary did not select only code: {FormatValues(source)}");
}

Dictionary<string, string> webDependency = RunDetection(
    repository,
    body,
    "pull_request",
    "src/DotnetInspector.Queries/AssemblyContextApiSurfaceQuery.cs",
    outputs);
if (webDependency["code"] != "true" || webDependency["web"] != "true")
{
    throw new InvalidOperationException(
        $"Web dependency canary did not select code and web: {FormatValues(webDependency)}");
}

Dictionary<string, string> sharedWebCompileInput = RunDetection(
    repository,
    body,
    "pull_request",
    "src/UnionPolyfill.cs",
    outputs);
if (sharedWebCompileInput["code"] != "true"
    || sharedWebCompileInput["web"] != "true")
{
    throw new InvalidOperationException(
        $"Shared web compile-input canary did not select code and web: "
        + FormatValues(sharedWebCompileInput));
}

Dictionary<string, string> web = RunDetection(
    repository,
    body,
    "pull_request",
    "prototypes/inspect-web/engine/BrowserInspectionEngine.cs",
    outputs);
if (web["code"] != "false" || web["web"] != "true")
{
    throw new InvalidOperationException(
        $"Web canary did not select only web: {FormatValues(web)}");
}
AssertRouting(source, selected: "shipped", notSelected: "csharpdiff");
AssertRouting(source, selected: "shipped", notSelected: "decompiler");

Dictionary<string, string> cliTests = RunDetection(
    repository,
    body,
    "pull_request",
    "src/dotnet-inspect.Tests/CommandExecutionTests.cs",
    outputs);
AssertRouting(cliTests, selected: "code", notSelected: "decompiler");

Dictionary<string, string> decompilerSubstrate = RunDetection(
    repository,
    body,
    "pull_request",
    "src/ILInspector.MetadataPrimitives/TypeName.cs",
    outputs);
AssertRouting(
    decompilerSubstrate,
    selected: "decompiler",
    notSelected: "packaging");

Dictionary<string, string> decompilerFixture = RunDetection(
    repository,
    body,
    "pull_request",
    "src/ILInspector.Decompiler.Fixtures.ClassicAsync/Fixture.cs",
    outputs);
AssertRouting(
    decompilerFixture,
    selected: "decompiler",
    notSelected: "packaging");

Dictionary<string, string> missingDecompilerSkipList = RunDetection(
    repository,
    body.Replace(
        "eng/decompiler-gate-skip-projects.txt",
        "eng/missing-decompiler-gate-skip-projects.txt",
        StringComparison.Ordinal),
    "pull_request",
    "src/dotnet-inspect/Program.cs",
    outputs);
if (missingDecompilerSkipList["decompiler"] != "true")
{
    throw new InvalidOperationException(
        "Missing decompiler project skip list did not fail safe: " +
        FormatValues(missingDecompilerSkipList));
}

Dictionary<string, string> multipleFiles = RunDetection(
    repository,
    body,
    "pull_request",
    "src/dotnet-inspect/Program.cs\nREADME.md",
    outputs);
if (multipleFiles["code"] != "true" ||
    multipleFiles["docs"] != "true" ||
    multipleFiles["packaging"] != "false")
{
    throw new InvalidOperationException(
        $"Distinct multi-file canary did not discriminate: " +
        FormatValues(multipleFiles));
}

Dictionary<string, string> platformTypeRouting = RunDetection(
    repository,
    body,
    "pull_request",
    """
    docs/workflows/getting-started/type-and-member-addressability.md
    src/dotnet-inspect.Tests/CommandExecutionTests.cs
    src/dotnet-inspect/CommandLine/Commands/RouterCommandDefinition.cs
    """,
    outputs);
if (platformTypeRouting["code"] != "true"
    || platformTypeRouting["docs"] != "true"
    || platformTypeRouting["decompiler"] != "false")
{
    throw new InvalidOperationException(
        "Platform type routing canary selected the wrong lanes: " +
        FormatValues(platformTypeRouting));
}

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

Dictionary<string, string> workflow = RunDetection(
    repository,
    body,
    "pull_request",
    ".github/workflows/ci.yml",
    outputs);
if (workflow["code"] != "true" || workflow["skills"] != "true")
{
    throw new InvalidOperationException(
        $"Workflow canary did not select code and skills: {FormatValues(workflow)}");
}

Dictionary<string, string> skill = RunDetection(
    repository,
    body,
    "pull_request",
    "skills/new-skill/SKILL.md",
    outputs);
if (skill["code"] != "false"
    || skill["docs"] != "true"
    || skill["skills"] != "true")
{
    throw new InvalidOperationException(
        $"Skill canary did not select only docs and skills: {FormatValues(skill)}");
}

Dictionary<string, string> skillSupportDoc = RunDetection(
    repository,
    body,
    "pull_request",
    "skills/workflow-scenarios/validating-workflows.md",
    outputs);
if (skillSupportDoc["code"] != "false"
    || skillSupportDoc["docs"] != "true"
    || skillSupportDoc["skills"] != "false")
{
    throw new InvalidOperationException(
        $"Skill support document canary selected the wrong lanes: " +
        FormatValues(skillSupportDoc));
}

Dictionary<string, string> nestedSkillSupportDoc = RunDetection(
    repository,
    body,
    "pull_request",
    "skills/workflow-scenarios/examples/SKILL.md",
    outputs);
if (nestedSkillSupportDoc["code"] != "false"
    || nestedSkillSupportDoc["docs"] != "true"
    || nestedSkillSupportDoc["skills"] != "false")
{
    throw new InvalidOperationException(
        $"Nested skill support document canary selected the wrong lanes: " +
        FormatValues(nestedSkillSupportDoc));
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

int recordEncoding = body.IndexOf("| @json", StringComparison.Ordinal);
int recordBase64 = recordEncoding < 0
    ? -1
    : body.IndexOf("| @base64", recordEncoding, StringComparison.Ordinal);
if (recordBase64 < 0)
{
    throw new InvalidOperationException("Could not construct the gh invocation mutation.");
}
string brokenGhInvocation = body.Remove(
    recordBase64,
    "| @base64".Length);

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

    static void ValidateDecompilerProjectSkipList(string repository)
    {
        string manifestPath = Path.Combine(
            repository,
            "eng",
            "decompiler-gate-skip-projects.txt");
        string[] manifestLines = File.ReadAllLines(manifestPath);
        var actual = manifestLines.ToHashSet(StringComparer.Ordinal);
        if (actual.Count != manifestLines.Length
            || manifestLines.Any(line =>
                string.IsNullOrWhiteSpace(line)
                || line != line.Trim()
                || Path.IsPathRooted(line)
                || line.EndsWith('/')
                || line.Split('/').Any(part => part is "" or "." or "..")
                || !Directory.Exists(Path.Combine(repository, line))))
        {
            throw new InvalidOperationException(
                "eng/decompiler-gate-skip-projects.txt must contain unique, " +
                "existing, canonical repository-relative project directories.");
        }

        string graphPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-decompiler-graph-{Guid.NewGuid():N}.json");
        try
        {
            ProcessStartInfo startInfo = new("dotnet")
            {
                UseShellExecute = false,
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add(
                "src/ILInspector.Decompiler.Tests/ILInspector.Decompiler.Tests.csproj");
            startInfo.ArgumentList.Add("-t:GenerateRestoreGraphFile");
            startInfo.ArgumentList.Add($"-p:RestoreGraphOutputPath={graphPath}");
            startInfo.ArgumentList.Add("-nologo");
            startInfo.ArgumentList.Add("-v:q");

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start dotnet msbuild for the decompiler project graph.");
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            bool timedOut = !process.WaitForExit(milliseconds: 30_000);
            if (timedOut)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            if (timedOut || process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Could not evaluate the decompiler project graph.\n" +
                    $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
            }

            using JsonDocument graph = JsonDocument.Parse(
                File.ReadAllText(graphPath));
            var projectClosure = graph.RootElement
                .GetProperty("projects")
                .EnumerateObject()
                .Select(project =>
                {
                    string relative = Path.GetRelativePath(repository, project.Name);
                    if (Path.IsPathRooted(relative)
                        || relative == ".."
                        || relative.StartsWith(
                            $"..{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Decompiler graph project is outside the repository: {project.Name}");
                    }

                    return Path.GetDirectoryName(relative)!
                        .Replace(Path.DirectorySeparatorChar, '/');
                })
                .ToHashSet(StringComparer.Ordinal);

            string[] unsafeExemptions = actual
                .Intersect(projectClosure)
                .Order()
                .ToArray();
            if (unsafeExemptions.Length != 0)
            {
                throw new InvalidOperationException(
                    "eng/decompiler-gate-skip-projects.txt exempts projects in " +
                    "the evaluated ILInspector.Decompiler.Tests graph: [" +
                    string.Join(", ", unsafeExemptions) + "].");
            }
        }
        finally
        {
            File.Delete(graphPath);
        }
    }

    ValidateDecompilerProjectSkipList(repository);

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
    ValidateOutputConsumers(jobs);
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
        "web",
        "skills",
    ];
    if (!declaredOutputs.ToHashSet(StringComparer.Ordinal)
        .SetEquals(requiredOutputs))
    {
        throw new InvalidOperationException(
            $"jobs.changes must declare exactly: {string.Join(", ", requiredOutputs)}.");
    }

    YamlMappingNode inspectWeb =
        GetRequiredMapping(jobs, "inspect-web", "jobs");
    YamlSequenceNode inspectWebSteps = GetRequiredSequence(
        inspectWeb,
        "steps",
        "jobs.inspect-web");
    List<YamlMappingNode> webSdkSteps = [];
    foreach (YamlNode stepNode in inspectWebSteps.Children)
    {
        YamlMappingNode step = RequireMapping(
            stepNode,
            "jobs.inspect-web step");
        if (GetOptionalScalar(step, "uses") == "actions/setup-dotnet@v5")
            webSdkSteps.Add(step);
    }
    if (webSdkSteps.Count != 1)
    {
        throw new InvalidOperationException(
            $"Expected one inspect-web setup-dotnet step, found {webSdkSteps.Count}.");
    }
    YamlMappingNode webSdkWith = GetRequiredMapping(
        webSdkSteps[0],
        "with",
        "jobs.inspect-web setup-dotnet");
    RequireScalarValue(
        webSdkWith,
        "dotnet-version",
        "11.0.x",
        "jobs.inspect-web setup-dotnet.with");
    RequireScalarValue(
        webSdkWith,
        "dotnet-quality",
        "preview",
        "jobs.inspect-web setup-dotnet.with");

    YamlSequenceNode steps = GetRequiredSequence(
        changes,
        "steps",
        "jobs.changes");
    if (steps.Children.Count != 5)
    {
        throw new InvalidOperationException(
            "jobs.changes must contain exactly the four pinned prerequisites and self-test.");
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
        "AFD8804F209E05792867D7776A950AE6B0EA459F32F896782F0C6B794F5A4B76",
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
        selfTestSteps[0].Index != 4)
    {
        throw new InvalidOperationException(
            "Self-test change detection must run once after Detect changes.");
    }

    YamlMappingNode selfTestStep = selfTestSteps[0].Step;
    RequireExactKeys(
        selfTestStep,
        ["name", "shell", "run", "env"],
        "Self-test change detection");
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
    RequireAbsent(
        aggregate,
        "defaults",
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
    if (steps.Children.Count != 4)
    {
        throw new InvalidOperationException(
            "jobs.ci-required must contain checkout and exactly three enforcement steps.");
    }
    YamlMappingNode checkout = RequireMapping(
        steps.Children[0],
        "jobs.ci-required checkout step");
    RequireExactKeys(
        checkout,
        ["uses"],
        "jobs.ci-required checkout step");
    RequireScalarValue(
        checkout,
        "uses",
        "actions/checkout@v6",
        "jobs.ci-required checkout step");
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
    if (namedSteps[requiredStepNames[0]].Index != 1 ||
        namedSteps[requiredStepNames[1]].Index != 2 ||
        namedSteps[requiredStepNames[2]].Index != 3)
    {
        throw new InvalidOperationException(
            "jobs.ci-required enforcement steps are out of order.");
    }

    YamlMappingNode check = namedSteps[requiredStepNames[0]].Step;
    RequireExactKeys(
        check,
        ["name", "shell", "env", "run"],
        "ci-required structural check");
    RequireScalarValue(
        check,
        "shell",
        "bash",
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
        ["name", "shell", "run"],
        "ci-required result-filter self-test");
    RequireScalarValue(
        filterSelfTest,
        "shell",
        "bash",
        "ci-required result-filter self-test");
    RequireScalarSha256(
        filterSelfTest,
        "run",
        "7BE0D6B90EB8A915BB17ED8BC6B6DA3371197D69E5F98262D854330985E7E5BD",
        "ci-required result-filter self-test");

    YamlMappingNode resultCheck = namedSteps[requiredStepNames[2]].Step;
    RequireExactKeys(
        resultCheck,
        ["name", "shell", "env", "run"],
        "ci-required result check");
    RequireScalarValue(
        resultCheck,
        "shell",
        "bash",
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

static void ValidateOutputConsumers(YamlMappingNode jobs)
{
    var conditions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["markdownlint"] = "needs.changes.outputs.docs == 'true'",
        ["skill-gate"] =
            "needs.changes.outputs.skills == 'true' && " +
            "github.event_name == 'pull_request'",
        ["test"] =
            "needs.changes.outputs.code == 'true' && " +
            "github.event_name == 'pull_request'",
        ["test-windows"] =
            "needs.changes.outputs.code == 'true' && " +
            "github.event_name == 'pull_request'",
        ["build-net10"] =
            "needs.changes.outputs.shipped == 'true' && " +
            "github.event_name == 'pull_request'",
        ["decompiler-gates"] =
            "github.event_name == 'pull_request' && " +
            "needs.changes.outputs.decompiler == 'true'",
        ["csharp-diff-smoke"] =
            "github.event_name == 'pull_request' && " +
            "needs.changes.outputs.csharpdiff == 'true'",
        ["il-diff-smoke"] =
            "github.event_name == 'pull_request' && " +
            "needs.changes.outputs.ildiff == 'true'",
        ["pack"] =
            "github.event_name == 'pull_request' && " +
            "needs.changes.outputs.packaging == 'true'",
    };
    foreach ((string jobName, string condition) in conditions)
    {
        YamlMappingNode job = GetRequiredMapping(jobs, jobName, "jobs");
        RequireScalarValue(job, "needs", "changes", $"jobs.{jobName}");
        RequireScalarValue(job, "if", condition, $"jobs.{jobName}");
        RequireAbsent(job, "continue-on-error", $"jobs.{jobName}");
        RequireAbsent(job, "defaults", $"jobs.{jobName}");
    }
    ValidateConsumerStepGuards(jobs, conditions.Keys);

    YamlSequenceNode testSteps = GetRequiredSequence(
        GetRequiredMapping(jobs, "test", "jobs"),
        "steps",
        "jobs.test");
    var roundtripSteps = new Dictionary<string, YamlMappingNode>(
        StringComparer.Ordinal);
    foreach (YamlNode stepNode in testSteps.Children)
    {
        YamlMappingNode step = RequireMapping(stepNode, "jobs.test step");
        string? name = GetOptionalScalar(step, "name");
        if (name is "Restore vendored ILAssembler" or
            "Run IL round-trip tests (fast)")
        {
            if (!roundtripSteps.TryAdd(name, step))
            {
                throw new InvalidOperationException(
                    $"jobs.test contains duplicate step: {name}.");
            }
        }

    }

    string roundtripCondition =
        "matrix.rid == 'linux-x64' && " +
        "needs.changes.outputs.ilroundtrip == 'true'";
    foreach (string name in new[]
    {
        "Restore vendored ILAssembler",
        "Run IL round-trip tests (fast)",
    })
    {
        if (!roundtripSteps.TryGetValue(name, out YamlMappingNode? step))
        {
            throw new InvalidOperationException(
                $"jobs.test is missing step: {name}.");
        }
        RequireScalarValue(step, "if", roundtripCondition, $"jobs.test {name}");
        RequireAbsent(step, "continue-on-error", $"jobs.test {name}");
    }
}

static void ValidateConsumerStepGuards(
    YamlMappingNode jobs,
    IEnumerable<string> jobNames)
{
    var allowedIf = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["test/Upload PR decompiler corpus artifact"] = "always()",
        ["test/Check PR decompiler corpus result"] =
            "steps.decompiler_pr_corpus.outcome == 'failure'",
        ["test/Restore vendored ILAssembler"] =
            "matrix.rid == 'linux-x64' && " +
            "needs.changes.outputs.ilroundtrip == 'true'",
        ["test/Run IL round-trip tests (fast)"] =
            "matrix.rid == 'linux-x64' && " +
            "needs.changes.outputs.ilroundtrip == 'true'",
        ["test/Check ilasm/ildasm/mdv result"] =
            "steps.iltools.outcome == 'failure'",
        ["test-windows/Run CLI tests (all)"] =
            "${{ !cancelled() && steps.build.outcome == 'success' }}",
        ["test-windows/Run CSharpText tests"] =
            "${{ !cancelled() && steps.build.outcome == 'success' }}",
        ["test-windows/Run decompiler unit tests (fast)"] =
            "${{ !cancelled() && steps.build.outcome == 'success' }}",
        ["test-windows/Run NuGetFetch tests (offline)"] =
            "${{ !cancelled() && steps.build.outcome == 'success' }}",
        ["test-windows/Run metadata tests"] =
            "${{ !cancelled() && steps.build.outcome == 'success' }}",
        ["test-windows/Run services tests"] =
            "${{ !cancelled() && steps.build.outcome == 'success' }}",
        ["decompiler-gates/Upload gate report"] = "always()",
        ["csharp-diff-smoke/Upload C# Diff smoke artifact"] = "always()",
        ["il-diff-smoke/Upload IL Diff smoke artifact"] = "always()",
    };
    var allowedContinueOnError = new HashSet<string>(StringComparer.Ordinal)
    {
        "test/Run PR decompiler corpus sensor",
        "test/Install ilasm/ildasm/mdv",
        "decompiler-gates/Run decompiler gates",
    };
    var allowedShell = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["test/Run PR decompiler corpus sensor"] = "bash",
        ["test/Install ilasm/ildasm/mdv"] = "bash",
        ["csharp-diff-smoke/Run C# Diff baseline smoke"] = "bash",
        ["il-diff-smoke/Run IL Diff baseline smoke"] = "bash",
        ["skill-gate/Run embedded skill tests"] = "bash",
    };
    var allowedId = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["test/Run PR decompiler corpus sensor"] = "decompiler_pr_corpus",
        ["test/Install ilasm/ildasm/mdv"] = "iltools",
        ["test-windows/Build"] = "build",
        ["decompiler-gates/Run decompiler gates"] = "gates",
    };
    var seenIf = new HashSet<string>(StringComparer.Ordinal);
    var seenContinueOnError = new HashSet<string>(StringComparer.Ordinal);
    var seenShell = new HashSet<string>(StringComparer.Ordinal);
    var seenId = new HashSet<string>(StringComparer.Ordinal);

    foreach (string jobName in jobNames)
    {
        YamlSequenceNode steps = GetRequiredSequence(
            GetRequiredMapping(jobs, jobName, "jobs"),
            "steps",
            $"jobs.{jobName}");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (YamlNode stepNode in steps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                $"jobs.{jobName} step");
            string? identity = GetOptionalScalar(step, "name") ??
                GetOptionalScalar(step, "uses");
            if (identity is null || !identities.Add(identity))
            {
                throw new InvalidOperationException(
                    $"jobs.{jobName} steps must have unique names or uses.");
            }

            string key = $"{jobName}/{identity}";
            ValidateOptionalStepValue(
                step,
                "if",
                key,
                allowedIf,
                seenIf);
            ValidateOptionalStepValue(
                step,
                "shell",
                key,
                allowedShell,
                seenShell);
            ValidateOptionalStepValue(
                step,
                "id",
                key,
                allowedId,
                seenId);

            string? continueOnError =
                GetOptionalScalar(step, "continue-on-error");
            if (continueOnError is not null)
            {
                if (continueOnError != "true" ||
                    !allowedContinueOnError.Contains(key))
                {
                    throw new InvalidOperationException(
                        $"{key} has unapproved continue-on-error.");
                }
                seenContinueOnError.Add(key);
            }
            RequireAbsent(step, "working-directory", key);
        }
    }

    RequireSeenExactly(seenIf, allowedIf.Keys, "consumer step if conditions");
    RequireSeenExactly(
        seenContinueOnError,
        allowedContinueOnError,
        "consumer step continue-on-error");
    RequireSeenExactly(
        seenShell,
        allowedShell.Keys,
        "consumer step shell overrides");
    RequireSeenExactly(
        seenId,
        allowedId.Keys,
        "consumer step ids");
}

static void ValidateOptionalStepValue(
    YamlMappingNode step,
    string property,
    string key,
    IReadOnlyDictionary<string, string> allowed,
    ISet<string> seen)
{
    string? value = GetOptionalScalar(step, property);
    if (value is null)
    {
        return;
    }
    if (!allowed.TryGetValue(key, out string? expected) ||
        value != expected)
    {
        throw new InvalidOperationException(
            $"{key}.{property} is not approved.");
    }
    seen.Add(key);
}

static void RequireSeenExactly(
    IReadOnlySet<string> actual,
    IEnumerable<string> expected,
    string context)
{
    if (!actual.SetEquals(expected))
    {
        throw new InvalidOperationException(
            $"{context} do not match the approved set.");
    }
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
    bool changedFileCountIsString = false,
    bool resolutionSucceeds = true,
    string malformedFileRecordJson = "",
    bool objectShapedFilePage = false,
    bool nulFileRecord = false,
    bool nulPreviousFileRecord = false,
    string fileStatus = "modified",
    int failDecodeAt = 0,
    bool truncateRecordStream = false,
    bool truncatePushStream = false,
    bool emptyPushRecord = false)
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
            COUNT_JQ='if ((.changed_files | type) == "number") and (.changed_files >= 0) and (.changed_files == (.changed_files | floor)) then (.changed_files | tostring) else error("invalid changed_files") end'
            if [ "$RESOLUTION_SUCCEEDS" != "true" ]; then
              exit 1
            fi
            if [ "$#" -eq 4 ] && [ "$1" = "api" ] \
               && [ "$2" = "repos/richlander/dotnet-inspect/pulls/3704" ] \
               && [ "$3" = "--jq" ] && [ "$4" = "$COUNT_JQ" ]; then
              if [ -n "$REPORTED_CHANGED_FILE_COUNT" ]; then
              count=$REPORTED_CHANGED_FILE_COUNT
              elif [ -n "$MALFORMED_FILE_RECORD_JSON" ]; then
              count=1
              elif [ "$NUL_FILE_RECORD" = "true" ]; then
              count=1
              elif [ "$NUL_PREVIOUS_FILE_RECORD" = "true" ]; then
              count=1
              elif [ -n "$PREVIOUS_FILES" ]; then
              count=1
              elif [ -z "$CHANGED_FILES" ]; then
              count=0
              else
              count=0
              while IFS= read -r file; do
                count=$((count + 1))
              done <<EOF
            $CHANGED_FILES
            EOF
              fi
              if [ "$CHANGED_FILE_COUNT_IS_STRING" = "true" ]; then
                jq -cn --arg count "$count" '{changed_files: $count}' |
                  jq -r "$4"
              else
                printf '{"changed_files":%s}\n' "$count" |
                  jq -r "$4"
              fi
              exit $?
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
                    '[{"status":"renamed","previous_filename":"sr\u0000c/Program.cs","filename":"notes/payload.bin"}]'
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
              rendered=$(printf '%s\n' "$records" | jq -r "$5") || exit 1
              if [ "$TRUNCATE_RECORD_STREAM" = "true" ]; then
                printf '%s' "${rendered%????}"
              else
                printf '%s\n' "$rendered"
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
              if [ "$EMPTY_PUSH_RECORD" = "true" ]; then
                printf '\0'
              elif [ -n "$CHANGED_FILES" ]; then
                if [ "$TRUNCATE_PUSH_STREAM" = "true" ]; then
                  printf '%s' "$CHANGED_FILES"
                else
                  while IFS= read -r file; do
                    printf '%s\0' "$file"
                  done <<EOF
            $CHANGED_FILES
            EOF
                fi
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
        startInfo.Environment["CHANGED_FILE_COUNT_IS_STRING"] =
            changedFileCountIsString.ToString().ToLowerInvariant();
        startInfo.Environment["EXPECTED_BEFORE"] = Before;
        startInfo.Environment["EXPECTED_SHA"] = Sha;
        startInfo.Environment["EMPTY_PUSH_RECORD"] =
            emptyPushRecord.ToString().ToLowerInvariant();
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
        startInfo.Environment["TRUNCATE_RECORD_STREAM"] =
            truncateRecordStream.ToString().ToLowerInvariant();
        startInfo.Environment["TRUNCATE_PUSH_STREAM"] =
            truncatePushStream.ToString().ToLowerInvariant();

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
