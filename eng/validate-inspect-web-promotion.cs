using System.Globalization;
using System.Text.Json;

const string StagingWorkflow = ".github/workflows/deploy-inspect-web.yml";
const string StagingJob = "Publish staging";
const string SiteArtifact = "inspect-web-site";

try
{
    if (args is ["--self-test"])
    {
        RunSelfTest();
        return;
    }

    Dictionary<string, string> options = ParseOptions(args);
    RunInfo run = ReadRun(Required(options, "--run"));
    JobInfo[] jobs = ReadJobs(Required(options, "--jobs"));
    ArtifactInfo[] artifacts = ReadArtifacts(Required(options, "--artifacts"));
    string repository = Required(options, "--repository");
    double maxAgeHours = double.Parse(
        Required(options, "--max-age-hours"),
        CultureInfo.InvariantCulture);
    string githubOutput = Required(options, "--github-output");

    ValidationResult result = Validate(
        run,
        jobs,
        artifacts,
        repository,
        TimeSpan.FromHours(maxAgeHours),
        DateTimeOffset.UtcNow);

    File.AppendAllText(
        githubOutput,
        $"sha={result.Sha}\n" +
        $"run_attempt={result.RunAttempt}\n" +
        $"artifact_id={result.ArtifactId}\n" +
        $"artifact_digest={result.ArtifactDigest}\n");

    Console.WriteLine(
        $"Validated staging run {run.Id} attempt {result.RunAttempt} for {result.Sha}; " +
        $"artifact {result.ArtifactId} has digest {result.ArtifactDigest}.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Inspect-web promotion validation failed: {ex.Message}");
    Environment.ExitCode = 1;
}

static ValidationResult Validate(
    RunInfo run,
    IReadOnlyList<JobInfo> jobs,
    IReadOnlyList<ArtifactInfo> artifacts,
    string repository,
    TimeSpan maxAge,
    DateTimeOffset now)
{
    if (run.WorkflowPath != StagingWorkflow)
    {
        throw new InvalidOperationException(
            $"Staging run uses {run.WorkflowPath}, not {StagingWorkflow}.");
    }
    if (run.Event != "push")
        throw new InvalidOperationException($"Staging run event {run.Event} is not push.");
    if (run.HeadBranch != "main")
        throw new InvalidOperationException($"Staging run targets {run.HeadBranch}, not main.");
    if (run.Repository != repository || run.HeadRepository != repository)
    {
        throw new InvalidOperationException(
            $"Staging run repository identity is {run.Repository}/{run.HeadRepository}, " +
            $"not {repository}.");
    }
    if (run.Status != "completed" || run.Conclusion != "success")
    {
        throw new InvalidOperationException(
            $"Staging run is {run.Status}/{run.Conclusion}, not completed/success.");
    }
    if (run.HeadSha.Length != 40 || run.HeadSha.Any(c => !Uri.IsHexDigit(c)))
        throw new InvalidOperationException($"Staging run has invalid head SHA {run.HeadSha}.");
    if (run.RunAttempt < 1)
        throw new InvalidOperationException($"Staging run has invalid attempt {run.RunAttempt}.");

    JobInfo job = RequireSingle(
        jobs,
        job => job.Name == StagingJob,
        $"'{StagingJob}' job");
    if (job.Status != "completed" || job.Conclusion != "success")
    {
        throw new InvalidOperationException(
            $"Job '{StagingJob}' is {job.Status}/{job.Conclusion}, not completed/success.");
    }
    RequireFresh(job.CompletedAt, maxAge, now, $"Job '{StagingJob}'");

    if (artifacts.Count != 1)
    {
        throw new InvalidOperationException(
            $"Staging run contains {artifacts.Count} artifacts; expected one.");
    }
    ArtifactInfo artifact = artifacts[0];
    if (artifact.Name != SiteArtifact)
    {
        throw new InvalidOperationException(
            $"Staging artifact is named {artifact.Name}, not {SiteArtifact}.");
    }
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

    return new(run.HeadSha, run.RunAttempt, artifact.Id, artifact.Digest);
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
            $"Staging run contains {matches.Length} {label}s; expected one.");
    }

    return matches[0];
}

static void RequireFresh(
    DateTimeOffset completedAt,
    TimeSpan maxAge,
    DateTimeOffset now,
    string label)
{
    TimeSpan age = now - completedAt;
    if (age < TimeSpan.FromMinutes(-5))
        throw new InvalidOperationException($"{label} completion time is in the future.");
    if (age > maxAge)
    {
        throw new InvalidOperationException(
            $"{label} is {age.TotalHours:F1} hours old; " +
            $"maximum age is {maxAge.TotalHours:F1} hours.");
    }
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
        RequiredString(root.GetProperty("head_repository"), "full_name"),
        DateTimeOffset.Parse(
            RequiredString(root, "updated_at"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal));
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
            DateTimeOffset.Parse(
                RequiredString(job, "completed_at"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal)))
        .ToArray();

    if (jobs.Length != totalCount)
    {
        throw new InvalidOperationException(
            $"Staging jobs response is incomplete: received {jobs.Length} of {totalCount} jobs.");
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
            $"Staging artifacts response is incomplete: " +
            $"received {artifacts.Length} of {totalCount} artifacts.");
    }

    return artifacts;
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

static void RunSelfTest()
{
    DateTimeOffset now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    const string repository = "richlander/dotnet-inspect";
    const string sha = "1111111111111111111111111111111111111111";
    const string digest =
        "sha256:2222222222222222222222222222222222222222222222222222222222222222";
    RunInfo run = new(
        101,
        StagingWorkflow,
        "push",
        "main",
        sha,
        "completed",
        "success",
        1,
        repository,
        repository,
        now.AddMinutes(-5));
    JobInfo[] jobs =
    [
        new(StagingJob, "completed", "success", now.AddMinutes(-5)),
    ];
    ArtifactInfo[] artifacts =
    [
        new(202, SiteArtifact, 100, false, now.AddDays(30), digest, run.Id),
    ];

    ValidationResult result = Validate(
        run,
        jobs,
        artifacts,
        repository,
        TimeSpan.FromDays(30),
        now);
    Assert(result.Sha == sha, "Validated SHA should be preserved.");
    Assert(result.ArtifactId == 202, "Validated artifact ID should be preserved.");

    ExpectFailure(
        () => Validate(
            run with { WorkflowPath = ".github/workflows/ci.yml" },
            jobs,
            artifacts,
            repository,
            TimeSpan.FromDays(30),
            now),
        "not .github/workflows/deploy-inspect-web.yml");
    ExpectFailure(
        () => Validate(
            run with { Event = "workflow_dispatch" },
            jobs,
            artifacts,
            repository,
            TimeSpan.FromDays(30),
            now),
        "not push");
    ExpectFailure(
        () => Validate(
            run with { HeadRepository = "other/repository" },
            jobs,
            artifacts,
            repository,
            TimeSpan.FromDays(30),
            now),
        "repository identity");
    ExpectFailure(
        () => Validate(
            run,
            [new(StagingJob, "completed", "failure", now.AddMinutes(-5))],
            artifacts,
            repository,
            TimeSpan.FromDays(30),
            now),
        "not completed/success");
    ExpectFailure(
        () => Validate(
            run,
            jobs,
            [],
            repository,
            TimeSpan.FromDays(30),
            now),
        "contains 0 artifacts");
    ExpectFailure(
        () => Validate(
            run,
            jobs,
            [artifacts[0], artifacts[0] with { Id = 203 }],
            repository,
            TimeSpan.FromDays(30),
            now),
        "contains 2 artifacts");
    ExpectFailure(
        () => Validate(
            run,
            jobs,
            [artifacts[0], artifacts[0] with { Id = 203, Name = "other-artifact" }],
            repository,
            TimeSpan.FromDays(30),
            now),
        "contains 2 artifacts");
    ExpectFailure(
        () => Validate(
            run,
            jobs,
            [artifacts[0] with { Name = "other-artifact" }],
            repository,
            TimeSpan.FromDays(30),
            now),
        "not inspect-web-site");
    ExpectFailure(
        () => Validate(
            run,
            jobs,
            [artifacts[0] with { Expired = true }],
            repository,
            TimeSpan.FromDays(30),
            now),
        "expired");
    ExpectFailure(
        () => Validate(
            run,
            jobs,
            [artifacts[0] with { WorkflowRunId = 999 }],
            repository,
            TimeSpan.FromDays(30),
            now),
        "not 101");
    ExpectFailure(
        () => Validate(
            run,
            [jobs[0] with { CompletedAt = now.AddDays(-31) }],
            artifacts,
            repository,
            TimeSpan.FromDays(30),
            now),
        "maximum age");

    string scratch = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspect-web-promotion-{Guid.NewGuid():N}");
    Directory.CreateDirectory(scratch);
    try
    {
        string runPath = Path.Combine(scratch, "run.json");
        string jobsPath = Path.Combine(scratch, "jobs.json");
        string artifactsPath = Path.Combine(scratch, "artifacts.json");
        File.WriteAllText(
            runPath,
            """
            {"id":101,"path":"$WORKFLOW$","event":"push","head_branch":"main","head_sha":"$SHA$","status":"completed","conclusion":"success","run_attempt":1,"repository":{"full_name":"$REPOSITORY$"},"head_repository":{"full_name":"$REPOSITORY$"},"updated_at":"$UPDATED_AT$"}
            """
            .Replace("$WORKFLOW$", StagingWorkflow, StringComparison.Ordinal)
            .Replace("$SHA$", sha, StringComparison.Ordinal)
            .Replace("$REPOSITORY$", repository, StringComparison.Ordinal)
            .Replace(
                "$UPDATED_AT$",
                now.ToString("O", CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
        File.WriteAllText(
            jobsPath,
            """
            {"total_count":1,"jobs":[{"name":"$JOB$","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"}]}
            """
            .Replace("$JOB$", StagingJob, StringComparison.Ordinal)
            .Replace(
                "$COMPLETED_AT$",
                now.ToString("O", CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
        File.WriteAllText(
            artifactsPath,
            """
            {"total_count":1,"artifacts":[{"id":202,"name":"$ARTIFACT$","size_in_bytes":100,"expired":false,"expires_at":"$EXPIRES_AT$","digest":"$DIGEST$","workflow_run":{"id":101}}]}
            """
            .Replace("$ARTIFACT$", SiteArtifact, StringComparison.Ordinal)
            .Replace(
                "$EXPIRES_AT$",
                now.AddDays(30).ToString("O", CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            .Replace("$DIGEST$", digest, StringComparison.Ordinal));

        ValidationResult parsed = Validate(
            ReadRun(runPath),
            ReadJobs(jobsPath),
            ReadArtifacts(artifactsPath),
            repository,
            TimeSpan.FromDays(30),
            now);
        Assert(parsed.ArtifactDigest == digest, "API parsing should preserve artifact digest.");
    }
    finally
    {
        Directory.Delete(scratch, recursive: true);
    }

    Console.WriteLine("Inspect-web promotion validator self-test passed.");
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
    string HeadRepository,
    DateTimeOffset UpdatedAt);

internal sealed record JobInfo(
    string Name,
    string Status,
    string Conclusion,
    DateTimeOffset CompletedAt);

internal sealed record ArtifactInfo(
    long Id,
    string Name,
    long SizeInBytes,
    bool Expired,
    DateTimeOffset ExpiresAt,
    string Digest,
    long WorkflowRunId);

internal sealed record ValidationResult(
    string Sha,
    int RunAttempt,
    long ArtifactId,
    string ArtifactDigest);
