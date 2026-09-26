using System.Globalization;
using System.Text.Json;

const string CandidateWorkflow = ".github/workflows/release-candidate.yml";
const string CandidateArtifact = "dotnet-inspect-release-candidate";
const string SourceJob = "Resolve ready source";
const string AssembleJob = "Assemble immutable candidate";
const string ReadyJob = "Candidate assets certified";
const string TargetCiJob = "ci-required";

string[] assetJobs =
[
    SourceJob,
    "Build package (win-x64)",
    "Build package (win-arm64)",
    "Build package (osx-arm64)",
    "Build package (linux-x64)",
    "Build package (linux-arm64)",
    "Build portable packages",
    "Build production site",
    AssembleJob,
];
string[] certificationJobs =
[
    "Deep Inspect candidate / Test lane",
    "Deep Inspect candidate / Platform test (win-x64)",
    "Deep Inspect candidate / Platform test (osx-arm64)",
    "Deep Inspect candidate / Platform test (linux-x64)",
    "Deep Inspect candidate / Decompiler corpus lane",
    "Deep Inspect candidate / Release certification",
    "Deep Inspect candidate / Inspect Web comprehensive",
    "Deep Inspect candidate / Census lane",
];

try
{
    if (args is ["--self-test"])
    {
        RunSelfTest(assetJobs, certificationJobs);
        return;
    }

    Dictionary<string, string> options = ParseOptions(args);
    RunInfo run = ReadRun(Required(options, "--run"));
    JobInfo[] jobs = ReadJobs(Required(options, "--jobs"));
    ArtifactInfo[] artifacts = ReadArtifacts(Required(options, "--artifacts"));
    CheckInfo[] checks = ReadChecks(Required(options, "--checks"));
    string repository = Required(options, "--repository");
    int expectedAttempt = int.Parse(
        Required(options, "--expected-attempt"),
        CultureInfo.InvariantCulture);
    bool acceptCertificationConcerns = ParseBoolean(
        Required(options, "--accept-certification-concerns"),
        "--accept-certification-concerns");
    string githubOutput = Required(options, "--github-output");

    ValidationResult result = Validate(
        run,
        jobs,
        artifacts,
        checks,
        repository,
        expectedAttempt,
        acceptCertificationConcerns,
        assetJobs,
        certificationJobs,
        DateTimeOffset.UtcNow);

    File.AppendAllText(
        githubOutput,
        $"sha={result.Sha}\n" +
        $"run_attempt={result.RunAttempt}\n" +
        $"artifact_id={result.ArtifactId}\n" +
        $"artifact_digest={result.ArtifactDigest}\n" +
        $"concerns_accepted={result.ConcernsAccepted.ToString().ToLowerInvariant()}\n");

    Console.WriteLine(
        $"Validated release candidate run {run.Id} attempt {result.RunAttempt} " +
        $"for {result.Sha}; artifact {result.ArtifactId} has digest " +
        $"{result.ArtifactDigest}.");
    Console.WriteLine($"Certification evidence: {result.OutcomeSummary}.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Release candidate validation failed: {ex.Message}");
    Environment.ExitCode = 1;
}

static ValidationResult Validate(
    RunInfo run,
    IReadOnlyList<JobInfo> jobs,
    IReadOnlyList<ArtifactInfo> artifacts,
    IReadOnlyList<CheckInfo> checks,
    string repository,
    int expectedAttempt,
    bool acceptCertificationConcerns,
    IReadOnlyList<string> assetJobNames,
    IReadOnlyList<string> certificationJobNames,
    DateTimeOffset now)
{
    if (run.WorkflowPath != CandidateWorkflow)
    {
        throw new InvalidOperationException(
            $"Candidate run uses {run.WorkflowPath}, not {CandidateWorkflow}.");
    }
    if (run.Event != "schedule" && run.Event != "workflow_dispatch")
    {
        throw new InvalidOperationException(
            $"Candidate run event {run.Event} is not schedule or workflow_dispatch.");
    }
    if (run.HeadBranch != "main")
        throw new InvalidOperationException($"Candidate run targets {run.HeadBranch}, not main.");
    if (run.Repository != repository || run.HeadRepository != repository)
    {
        throw new InvalidOperationException(
            $"Candidate run repository identity is {run.Repository}/{run.HeadRepository}, " +
            $"not {repository}.");
    }
    if (run.Status != "completed" ||
        (run.Conclusion != "success" && run.Conclusion != "failure"))
    {
        throw new InvalidOperationException(
            $"Candidate run is {run.Status}/{run.Conclusion}; expected a completed success or failure.");
    }
    if (run.HeadSha.Length != 40 || run.HeadSha.Any(c => !Uri.IsHexDigit(c)))
        throw new InvalidOperationException($"Candidate run has invalid head SHA {run.HeadSha}.");
    if (expectedAttempt < 1 || run.RunAttempt != expectedAttempt)
    {
        throw new InvalidOperationException(
            $"Candidate run attempt is {run.RunAttempt}, not selected attempt {expectedAttempt}.");
    }

    var requiredJobs = new Dictionary<string, JobInfo>(StringComparer.Ordinal);
    foreach (string name in assetJobNames.Concat(certificationJobNames).Append(ReadyJob))
    {
        JobInfo job = RequireSingle(
            jobs,
            candidate => candidate.Name == name,
            $"'{name}' job");
        if (job.RunAttempt != run.RunAttempt)
        {
            throw new InvalidOperationException(
                $"Job '{name}' belongs to attempt {job.RunAttempt}, not candidate attempt " +
                $"{run.RunAttempt}; rerun the complete candidate workflow.");
        }
        requiredJobs.Add(name, job);
    }

    foreach (string name in assetJobNames)
        RequireSuccessfulJob(requiredJobs[name]);

    JobInfo assemble = requiredJobs[AssembleJob];
    var certificationOutcomes = new List<string>();
    foreach (string name in certificationJobNames)
    {
        JobInfo job = requiredJobs[name];
        if (job.Status != "completed" ||
            (job.Conclusion != "success" && job.Conclusion != "failure"))
        {
            throw new InvalidOperationException(
                $"Certification job '{name}' is {job.Status}/{job.Conclusion}; " +
                "expected a completed success or failure.");
        }
        if (job.CompletedAt < assemble.CompletedAt)
        {
            throw new InvalidOperationException(
                $"Certification job '{name}' completed before candidate assembly.");
        }
        certificationOutcomes.Add($"{name}={job.Conclusion}");
    }

    string[] concerns = certificationJobNames
        .Where(name => requiredJobs[name].Conclusion != "success")
        .Select(name => $"{name}={requiredJobs[name].Conclusion}")
        .ToArray();
    JobInfo ready = requiredJobs[ReadyJob];
    if (ready.Status != "completed")
    {
        throw new InvalidOperationException(
            $"Candidate readiness job is {ready.Status}/{ready.Conclusion}, not completed.");
    }

    if (concerns.Length == 0)
    {
        if (acceptCertificationConcerns)
        {
            throw new InvalidOperationException(
                "Candidate certification is green; concern acceptance must remain disabled.");
        }
        if (run.Conclusion != "success" || ready.Conclusion != "success")
        {
            throw new InvalidOperationException(
                $"Green candidate is {run.Conclusion} with readiness {ready.Conclusion}; " +
                "both must succeed.");
        }
    }
    else
    {
        if (!acceptCertificationConcerns)
        {
            throw new InvalidOperationException(
                $"Candidate certification has concerns ({string.Join(", ", concerns)}). " +
                "Review each outcome and explicitly enable accept_certification_concerns.");
        }
        if (run.Conclusion != "failure" || ready.Conclusion != "failure")
        {
            throw new InvalidOperationException(
                $"Concern-bearing candidate is {run.Conclusion} with readiness " +
                $"{ready.Conclusion}; expected failure/failure.");
        }
    }

    CheckInfo targetCi = checks
        .Where(check => check.Name == TargetCiJob)
        .OrderBy(check => check.Id)
        .LastOrDefault()
        ?? throw new InvalidOperationException(
            $"Candidate SHA has no '{TargetCiJob}' check.");
    if (targetCi.Status != "completed" || targetCi.Conclusion != "success")
    {
        throw new InvalidOperationException(
            $"Candidate SHA '{TargetCiJob}' is {targetCi.Status}/{targetCi.Conclusion}, " +
            "not completed/success.");
    }

    ArtifactInfo artifact = RequireSingle(
        artifacts,
        candidate => candidate.Name == CandidateArtifact,
        $"'{CandidateArtifact}' artifact");
    if (artifact.WorkflowRunId != run.Id)
    {
        throw new InvalidOperationException(
            $"Artifact {artifact.Id} belongs to run {artifact.WorkflowRunId}, not {run.Id}.");
    }
    if (artifact.Expired || artifact.ExpiresAt <= now)
        throw new InvalidOperationException($"Artifact {artifact.Id} is expired.");
    if (artifact.SizeInBytes < 1)
        throw new InvalidOperationException($"Artifact {artifact.Id} is empty.");
    if (!IsSha256Digest(artifact.Digest))
    {
        throw new InvalidOperationException(
            $"Artifact {artifact.Id} has invalid digest {artifact.Digest}.");
    }

    return new(
        run.HeadSha,
        run.RunAttempt,
        artifact.Id,
        artifact.Digest,
        concerns.Length > 0,
        string.Join(", ", certificationOutcomes));
}

static void RequireSuccessfulJob(JobInfo job)
{
    if (job.Status != "completed" || job.Conclusion != "success")
    {
        throw new InvalidOperationException(
            $"Job '{job.Name}' is {job.Status}/{job.Conclusion}, not completed/success.");
    }
}

static T RequireSingle<T>(
    IReadOnlyList<T> values,
    Func<T, bool> predicate,
    string label)
{
    T[] matches = values.Where(predicate).ToArray();
    if (matches.Length != 1)
    {
        throw new InvalidOperationException(
            $"Candidate run contains {matches.Length} {label}s; expected one.");
    }

    return matches[0];
}

static bool IsSha256Digest(string digest) =>
    digest.StartsWith("sha256:", StringComparison.Ordinal) &&
    digest.Length == 71 &&
    digest.AsSpan(7).ToString().All(Uri.IsHexDigit);

static RunInfo ReadRun(string path)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    return new(
        RequiredInt64(root, "id"),
        RequiredString(root, "path"),
        RequiredString(root, "event"),
        RequiredString(root, "head_branch"),
        RequiredString(root, "head_sha"),
        RequiredString(root, "status"),
        RequiredString(root, "conclusion"),
        RequiredInt32(root, "run_attempt"),
        RequiredString(root.GetProperty("repository"), "full_name"),
        RequiredString(root.GetProperty("head_repository"), "full_name"));
}

static JobInfo[] ReadJobs(string path)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    int totalCount = root.GetProperty("total_count").GetInt32();
    JobInfo[] jobs = root.GetProperty("jobs")
        .EnumerateArray()
        .Select(job => new JobInfo(
            RequiredString(job, "name"),
            RequiredString(job, "status"),
            RequiredString(job, "conclusion"),
            RequiredInt32(job, "run_attempt"),
            DateTimeOffset.Parse(
                RequiredString(job, "completed_at"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal)))
        .ToArray();

    if (jobs.Length != totalCount)
    {
        throw new InvalidOperationException(
            $"Candidate jobs response is incomplete: received {jobs.Length} of {totalCount} jobs.");
    }

    return jobs;
}

static ArtifactInfo[] ReadArtifacts(string path)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    int totalCount = root.GetProperty("total_count").GetInt32();
    ArtifactInfo[] artifacts = root.GetProperty("artifacts")
        .EnumerateArray()
        .Select(artifact => new ArtifactInfo(
            RequiredInt64(artifact, "id"),
            RequiredString(artifact, "name"),
            RequiredInt64(artifact, "size_in_bytes"),
            artifact.GetProperty("expired").GetBoolean(),
            DateTimeOffset.Parse(
                RequiredString(artifact, "expires_at"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal),
            RequiredString(artifact, "digest"),
            RequiredInt64(artifact.GetProperty("workflow_run"), "id")))
        .ToArray();

    if (artifacts.Length != totalCount)
    {
        throw new InvalidOperationException(
            $"Candidate artifacts response is incomplete: " +
            $"received {artifacts.Length} of {totalCount} artifacts.");
    }

    return artifacts;
}

static CheckInfo[] ReadChecks(string path)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    int totalCount = root.GetProperty("total_count").GetInt32();
    CheckInfo[] checks = root.GetProperty("check_runs")
        .EnumerateArray()
        .Select(check => new CheckInfo(
            RequiredInt64(check, "id"),
            RequiredString(check, "name"),
            RequiredString(check, "status"),
            RequiredString(check, "conclusion")))
        .ToArray();

    if (checks.Length != totalCount)
    {
        throw new InvalidOperationException(
            $"Check-runs response is incomplete: received {checks.Length} of {totalCount} checks.");
    }

    return checks;
}

static string RequiredString(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out JsonElement value) ||
        value.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(value.GetString()))
    {
        throw new InvalidOperationException($"JSON property '{property}' is missing or invalid.");
    }

    return value.GetString()!;
}

static long RequiredInt64(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out JsonElement value) ||
        !value.TryGetInt64(out long result))
    {
        throw new InvalidOperationException($"JSON property '{property}' is missing or invalid.");
    }

    return result;
}

static int RequiredInt32(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out JsonElement value) ||
        !value.TryGetInt32(out int result))
    {
        throw new InvalidOperationException($"JSON property '{property}' is missing or invalid.");
    }

    return result;
}

static Dictionary<string, string> ParseOptions(string[] arguments)
{
    if (arguments.Length == 0 || arguments.Length % 2 != 0)
        throw new ArgumentException("Options must be supplied as --name value pairs.");

    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < arguments.Length; i += 2)
    {
        if (!arguments[i].StartsWith("--", StringComparison.Ordinal) ||
            !options.TryAdd(arguments[i], arguments[i + 1]))
        {
            throw new ArgumentException($"Invalid or duplicate option '{arguments[i]}'.");
        }
    }

    return options;
}

static string Required(IReadOnlyDictionary<string, string> options, string name) =>
    options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required option {name}.");

static bool ParseBoolean(string value, string name) =>
    value switch
    {
        "true" => true,
        "false" => false,
        _ => throw new ArgumentException($"{name} must be true or false."),
    };

static void RunSelfTest(
    IReadOnlyList<string> assetJobNames,
    IReadOnlyList<string> certificationJobNames)
{
    DateTimeOffset now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    const string repository = "richlander/dotnet-inspect";
    const string sha = "1111111111111111111111111111111111111111";
    const string digest =
        "sha256:2222222222222222222222222222222222222222222222222222222222222222";
    RunInfo run = new(
        101,
        CandidateWorkflow,
        "schedule",
        "main",
        sha,
        "completed",
        "success",
        2,
        repository,
        repository);
    JobInfo[] jobs =
    [
        .. assetJobNames.Select(
            name => new JobInfo(name, "completed", "success", 2, now.AddMinutes(-20))),
        .. certificationJobNames.Select(
            name => new JobInfo(name, "completed", "success", 2, now.AddMinutes(-10))),
        new(ReadyJob, "completed", "success", 2, now.AddMinutes(-5)),
    ];
    ArtifactInfo[] artifacts =
    [
        new(202, CandidateArtifact, 100, false, now.AddDays(30), digest, run.Id),
    ];
    CheckInfo[] checks =
    [
        new(303, TargetCiJob, "completed", "success"),
    ];

    ValidationResult green = Validate(
        run,
        jobs,
        artifacts,
        checks,
        repository,
        2,
        false,
        assetJobNames,
        certificationJobNames,
        now);
    Assert(!green.ConcernsAccepted, "Green candidate should not accept concerns.");
    Assert(green.ArtifactId == 202, "Candidate artifact identity should be preserved.");

    string scratch = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspect-release-candidate-{Guid.NewGuid():N}");
    Directory.CreateDirectory(scratch);
    try
    {
        string runPath = Path.Combine(scratch, "run.json");
        string jobsPath = Path.Combine(scratch, "jobs.json");
        string artifactsPath = Path.Combine(scratch, "artifacts.json");
        string checksPath = Path.Combine(scratch, "checks.json");
        WriteJson(runPath, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", run.Id);
            writer.WriteString("path", run.WorkflowPath);
            writer.WriteString("event", run.Event);
            writer.WriteString("head_branch", run.HeadBranch);
            writer.WriteString("head_sha", run.HeadSha);
            writer.WriteString("status", run.Status);
            writer.WriteString("conclusion", run.Conclusion);
            writer.WriteNumber("run_attempt", run.RunAttempt);
            writer.WriteStartObject("repository");
            writer.WriteString("full_name", run.Repository);
            writer.WriteEndObject();
            writer.WriteStartObject("head_repository");
            writer.WriteString("full_name", run.HeadRepository);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
        WriteJson(jobsPath, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("total_count", jobs.Length);
            writer.WriteStartArray("jobs");
            foreach (JobInfo job in jobs)
            {
                writer.WriteStartObject();
                writer.WriteString("name", job.Name);
                writer.WriteString("status", job.Status);
                writer.WriteString("conclusion", job.Conclusion);
                writer.WriteNumber("run_attempt", job.RunAttempt);
                writer.WriteString("completed_at", job.CompletedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        WriteJson(artifactsPath, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("total_count", artifacts.Length);
            writer.WriteStartArray("artifacts");
            foreach (ArtifactInfo artifact in artifacts)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", artifact.Id);
                writer.WriteString("name", artifact.Name);
                writer.WriteNumber("size_in_bytes", artifact.SizeInBytes);
                writer.WriteBoolean("expired", artifact.Expired);
                writer.WriteString("expires_at", artifact.ExpiresAt);
                writer.WriteString("digest", artifact.Digest);
                writer.WriteStartObject("workflow_run");
                writer.WriteNumber("id", artifact.WorkflowRunId);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        WriteJson(checksPath, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("total_count", checks.Length);
            writer.WriteStartArray("check_runs");
            foreach (CheckInfo check in checks)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", check.Id);
                writer.WriteString("name", check.Name);
                writer.WriteString("status", check.Status);
                writer.WriteString("conclusion", check.Conclusion);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });

        ValidationResult parsed = Validate(
            ReadRun(runPath),
            ReadJobs(jobsPath),
            ReadArtifacts(artifactsPath),
            ReadChecks(checksPath),
            repository,
            2,
            false,
            assetJobNames,
            certificationJobNames,
            now);
        Assert(parsed.ArtifactDigest == digest, "API parsing should preserve artifact digest.");
    }
    finally
    {
        Directory.Delete(scratch, recursive: true);
    }

    ExpectFailure(
        () => Validate(
            run,
            jobs,
            artifacts,
            checks,
            repository,
            1,
            false,
            assetJobNames,
            certificationJobNames,
            now),
        "not selected attempt");
    ExpectFailure(
        () => Validate(
            run,
            jobs.Select(job => job.Name == SourceJob
                ? job with { RunAttempt = 1 }
                : job).ToArray(),
            artifacts,
            checks,
            repository,
            2,
            false,
            assetJobNames,
            certificationJobNames,
            now),
        "rerun the complete candidate workflow");
    ExpectFailure(
        () => Validate(
            run,
            jobs.Where(job => job.Name != "Build production site").ToArray(),
            artifacts,
            checks,
            repository,
            2,
            false,
            assetJobNames,
            certificationJobNames,
            now),
        "Build production site");
    ExpectFailure(
        () => Validate(
            run,
            jobs,
            artifacts,
            checks,
            repository,
            2,
            true,
            assetJobNames,
            certificationJobNames,
            now),
        "must remain disabled");

    string failedJob = certificationJobNames[0];
    JobInfo[] concernJobs = jobs
        .Select(job => job.Name == failedJob
            ? job with { Conclusion = "failure" }
            : job.Name == ReadyJob
                ? job with { Conclusion = "failure" }
                : job)
        .ToArray();
    ExpectFailure(
        () => Validate(
            run with { Conclusion = "failure" },
            concernJobs,
            artifacts,
            checks,
            repository,
            2,
            false,
            assetJobNames,
            certificationJobNames,
            now),
        "accept_certification_concerns");
    ValidationResult concern = Validate(
        run with { Conclusion = "failure" },
        concernJobs,
        artifacts,
        checks,
        repository,
        2,
        true,
        assetJobNames,
        certificationJobNames,
        now);
    Assert(concern.ConcernsAccepted, "Accepted concern should remain visible.");
    Assert(
        concern.OutcomeSummary.Contains($"{failedJob}=failure", StringComparison.Ordinal),
        "Concern summary should disclose the failed job.");
    ExpectFailure(
        () => Validate(
            run with { Conclusion = "failure" },
            concernJobs.Select(job => job.Name == failedJob
                ? job with { Conclusion = "cancelled" }
                : job).ToArray(),
            artifacts,
            checks,
            repository,
            2,
            true,
            assetJobNames,
            certificationJobNames,
            now),
        "completed success or failure");
    ExpectFailure(
        () => Validate(
            run,
            jobs,
            artifacts,
            [new(303, TargetCiJob, "completed", "failure")],
            repository,
            2,
            false,
            assetJobNames,
            certificationJobNames,
            now),
        TargetCiJob);
    ExpectFailure(
        () => Validate(
            run,
            jobs,
            [artifacts[0] with { Expired = true }],
            checks,
            repository,
            2,
            false,
            assetJobNames,
            certificationJobNames,
            now),
        "expired");

    Console.WriteLine("Release candidate validator self-test passed.");
}

static void ExpectFailure(Action action, string messageFragment)
{
    try
    {
        action();
    }
    catch (Exception ex) when (
        ex.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected failure containing '{messageFragment}'.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void WriteJson(string path, Action<Utf8JsonWriter> write)
{
    using FileStream stream = File.Create(path);
    using var writer = new Utf8JsonWriter(stream);
    write(writer);
}

internal sealed record RunInfo(
    long Id,
    string WorkflowPath,
    string Event,
    string HeadBranch,
    string HeadSha,
    string Status,
    string Conclusion,
    int RunAttempt,
    string Repository,
    string HeadRepository);

internal sealed record JobInfo(
    string Name,
    string Status,
    string Conclusion,
    int RunAttempt,
    DateTimeOffset CompletedAt);

internal sealed record ArtifactInfo(
    long Id,
    string Name,
    long SizeInBytes,
    bool Expired,
    DateTimeOffset ExpiresAt,
    string Digest,
    long WorkflowRunId);

internal sealed record CheckInfo(
    long Id,
    string Name,
    string Status,
    string Conclusion);

internal sealed record ValidationResult(
    string Sha,
    int RunAttempt,
    long ArtifactId,
    string ArtifactDigest,
    bool ConcernsAccepted,
    string OutcomeSummary);
