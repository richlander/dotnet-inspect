using System.Globalization;
using System.Text.Json;

const string CertificationWorkflow = ".github/workflows/deep-inspect.yml";
const string TargetWorkflow = ".github/workflows/ci.yml";
const string CertificationJob = "Release certification";
const string TestJob = "Test lane";
const string CorpusJob = "Decompiler corpus lane";
const string TargetCiJob = "ci-required";

try
{
    if (args is ["--self-test"])
    {
        RunSelfTest();
        return;
    }

    Dictionary<string, string> options = ParseOptions(args);
    RunInfo certification = ReadRun(Required(options, "--certification-run"));
    JobInfo[] certificationJobs = ReadJobs(Required(options, "--certification-jobs"));
    RunInfo target = ReadRun(Required(options, "--target-run"));
    JobInfo[] targetJobs = ReadJobs(Required(options, "--target-jobs"));
    ComparisonInfo comparison = ReadComparison(Required(options, "--comparison"));
    bool allowLaterCommit = bool.Parse(Required(options, "--allow-later-commit"));
    double maxAgeHours = double.Parse(
        Required(options, "--max-age-hours"),
        CultureInfo.InvariantCulture);
    string githubOutput = Required(options, "--github-output");

    ValidationResult result = Validate(
        certification,
        certificationJobs,
        target,
        targetJobs,
        comparison,
        allowLaterCommit,
        TimeSpan.FromHours(maxAgeHours),
        DateTimeOffset.UtcNow);

    File.AppendAllText(
        githubOutput,
        $"sha={result.TargetSha}\n" +
        $"certified_sha={result.CertifiedSha}\n" +
        $"later_commit={result.IsLaterCommit.ToString().ToLowerInvariant()}\n");

    Console.WriteLine(
        result.IsLaterCommit
            ? $"Publishing later commit {result.TargetSha}; Deep Inspect certified ancestor {result.CertifiedSha}."
            : $"Publishing certified commit {result.TargetSha}.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Release certification validation failed: {ex.Message}");
    Environment.ExitCode = 1;
}

static ValidationResult Validate(
    RunInfo certification,
    IReadOnlyList<JobInfo> certificationJobs,
    RunInfo target,
    IReadOnlyList<JobInfo> targetJobs,
    ComparisonInfo comparison,
    bool allowLaterCommit,
    TimeSpan maxAge,
    DateTimeOffset now)
{
    RequireRun(
        certification,
        CertificationWorkflow,
        ["schedule", "workflow_dispatch"],
        "certification");
    RequireRun(target, TargetWorkflow, ["push"], "target CI");

    JobInfo certificationJob = RequireSuccessfulJob(certificationJobs, CertificationJob);
    JobInfo testJob = RequireSuccessfulJob(certificationJobs, TestJob);
    JobInfo corpusJob = RequireSuccessfulJob(certificationJobs, CorpusJob);
    JobInfo targetCiJob = RequireSuccessfulJob(targetJobs, TargetCiJob);

    RequireFresh(testJob, maxAge, now);
    RequireFresh(corpusJob, maxAge, now);
    if (certificationJob.CompletedAt < testJob.CompletedAt ||
        certificationJob.CompletedAt < corpusJob.CompletedAt)
    {
        throw new InvalidOperationException(
            "Release certification predates a slow validation job; rerun the complete test lane.");
    }
    if (targetCiJob.CompletedAt < target.UpdatedAt.AddMinutes(-5))
    {
        throw new InvalidOperationException(
            "Target ci-required completion predates the target workflow update.");
    }

    bool isLaterCommit = target.HeadSha != certification.HeadSha;
    string expectedStatus = isLaterCommit ? "ahead" : "identical";
    if (comparison.Status != expectedStatus || comparison.BaseCommitSha != certification.HeadSha)
    {
        throw new InvalidOperationException(
            $"Target commit must be the certified commit or its descendant; comparison was " +
            $"{comparison.Status} with base {comparison.BaseCommitSha}.");
    }

    if (isLaterCommit && !allowLaterCommit)
    {
        throw new InvalidOperationException(
            $"Target {target.HeadSha} is later than certified commit {certification.HeadSha}. " +
            "Review the intervening commits and explicitly enable allow_later_commit to publish it.");
    }

    return new ValidationResult(certification.HeadSha, target.HeadSha, isLaterCommit);
}

static JobInfo RequireSuccessfulJob(IReadOnlyList<JobInfo> jobs, string name)
{
    JobInfo[] matchingJobs = jobs.Where(job => job.Name == name).ToArray();
    if (matchingJobs.Length != 1)
    {
        throw new InvalidOperationException(
            $"Run contains {matchingJobs.Length} '{name}' jobs; expected one.");
    }

    JobInfo job = matchingJobs[0];
    if (job.Status != "completed" || job.Conclusion != "success")
    {
        throw new InvalidOperationException(
            $"Job '{name}' is {job.Status}/{job.Conclusion}, not completed/success.");
    }

    return job;
}

static void RequireFresh(JobInfo job, TimeSpan maxAge, DateTimeOffset now)
{
    TimeSpan age = now - job.CompletedAt;
    if (age < TimeSpan.FromMinutes(-5))
        throw new InvalidOperationException($"Job '{job.Name}' completion time is in the future.");
    if (age > maxAge)
    {
        throw new InvalidOperationException(
            $"Job '{job.Name}' is {age.TotalHours:F1} hours old; " +
            $"maximum age is {maxAge.TotalHours:F1} hours.");
    }
}

static void RequireRun(
    RunInfo run,
    string expectedWorkflow,
    IReadOnlyCollection<string> allowedEvents,
    string label)
{
    if (run.WorkflowPath != expectedWorkflow)
        throw new InvalidOperationException($"{label} run uses {run.WorkflowPath}, not {expectedWorkflow}.");
    if (!allowedEvents.Contains(run.Event))
        throw new InvalidOperationException($"{label} run event {run.Event} is not allowed.");
    if (run.HeadBranch != "main")
        throw new InvalidOperationException($"{label} run targets branch {run.HeadBranch}, not main.");
    if (run.Status != "completed" || run.Conclusion != "success")
    {
        throw new InvalidOperationException(
            $"{label} run is {run.Status}/{run.Conclusion}, not completed/success.");
    }
    if (run.HeadSha.Length != 40 || run.HeadSha.Any(c => !Uri.IsHexDigit(c)))
        throw new InvalidOperationException($"{label} run has invalid head SHA {run.HeadSha}.");
}

static RunInfo ReadRun(string path)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    return new RunInfo(
        RequiredString(root, "path"),
        RequiredString(root, "event"),
        RequiredString(root, "head_branch"),
        RequiredString(root, "head_sha"),
        RequiredString(root, "status"),
        RequiredString(root, "conclusion"),
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
            $"Certification jobs response is incomplete: received {jobs.Length} of {totalCount} jobs.");
    }

    return jobs;
}

static ComparisonInfo ReadComparison(string path)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    return new ComparisonInfo(
        RequiredString(root, "status"),
        RequiredString(root.GetProperty("base_commit"), "sha"));
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
    DateTimeOffset now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    const string certifiedSha = "1111111111111111111111111111111111111111";
    const string laterSha = "2222222222222222222222222222222222222222";
    RunInfo certification = new(
        CertificationWorkflow,
        "schedule",
        "main",
        certifiedSha,
        "completed",
        "success",
        now.AddHours(-1));
    RunInfo exactTarget = new(
        TargetWorkflow,
        "push",
        "main",
        certifiedSha,
        "completed",
        "success",
        now);
    RunInfo laterTarget = exactTarget with { HeadSha = laterSha };
    JobInfo[] successfulJobs =
    [
        new(TestJob, "completed", "success", now.AddHours(-1)),
        new(CorpusJob, "completed", "success", now.AddMinutes(-50)),
        new(CertificationJob, "completed", "success", now.AddMinutes(-49)),
    ];
    JobInfo[] successfulTargetJobs =
    [
        new(TargetCiJob, "completed", "success", now.AddMinutes(1)),
    ];

    ValidationResult exact = Validate(
        certification,
        successfulJobs,
        exactTarget,
        successfulTargetJobs,
        new("identical", certifiedSha),
        false,
        TimeSpan.FromHours(36),
        now);
    Assert(!exact.IsLaterCommit, "Exact certified commit should pass without an override.");

    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs,
            laterTarget,
            successfulTargetJobs,
            new("ahead", certifiedSha),
            false,
            TimeSpan.FromHours(36),
            now),
        "explicitly enable allow_later_commit");

    ValidationResult later = Validate(
        certification,
        successfulJobs,
        laterTarget,
        successfulTargetJobs,
        new("ahead", certifiedSha),
        true,
        TimeSpan.FromHours(36),
        now);
    Assert(later.IsLaterCommit, "Explicitly accepted descendant should pass.");

    ExpectFailure(
        () => Validate(
            certification,
            [
                new(TestJob, "completed", "success", now.AddHours(-37)),
                new(CorpusJob, "completed", "success", now.AddMinutes(-50)),
                new(CertificationJob, "completed", "success", now.AddMinutes(-49)),
            ],
            exactTarget,
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            TimeSpan.FromHours(36),
            now),
        "maximum age");
    ExpectFailure(
        () => Validate(
            certification,
            [
                new(TestJob, "completed", "success", now.AddMinutes(-10)),
                new(CorpusJob, "completed", "success", now.AddMinutes(-9)),
                new(CertificationJob, "completed", "success", now.AddHours(-1)),
            ],
            exactTarget,
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            TimeSpan.FromHours(36),
            now),
        "predates a slow validation job");
    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs,
            laterTarget,
            successfulTargetJobs,
            new("diverged", "3333333333333333333333333333333333333333"),
            true,
            TimeSpan.FromHours(36),
            now),
        "certified commit or its descendant");
    ExpectFailure(
        () => Validate(
            certification,
            [
                new(TestJob, "completed", "failure", now.AddHours(-1)),
                new(CorpusJob, "completed", "success", now.AddMinutes(-50)),
                new(CertificationJob, "completed", "success", now.AddMinutes(-49)),
            ],
            exactTarget,
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            TimeSpan.FromHours(36),
            now),
        "not completed/success");
    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs,
            exactTarget with { WorkflowPath = ".github/workflows/release.yml" },
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            TimeSpan.FromHours(36),
            now),
        "not .github/workflows/ci.yml");
    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs,
            exactTarget,
            [new(TargetCiJob, "completed", "failure", now.AddMinutes(1))],
            new("identical", certifiedSha),
            false,
            TimeSpan.FromHours(36),
            now),
        "ci-required");

    string scratch = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspect-release-certification-{Guid.NewGuid():N}");
    Directory.CreateDirectory(scratch);
    try
    {
        string certificationRunPath = Path.Combine(scratch, "certification-run.json");
        string certificationJobsPath = Path.Combine(scratch, "certification-jobs.json");
        string targetRunPath = Path.Combine(scratch, "target-run.json");
        string comparisonPath = Path.Combine(scratch, "comparison.json");
        string completedAt = now.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);

        File.WriteAllText(
            certificationRunPath,
            """
            {"path":"$WORKFLOW$","event":"schedule","head_branch":"main","head_sha":"$SHA$","status":"completed","conclusion":"success","updated_at":"$UPDATED_AT$"}
            """
            .Replace("$WORKFLOW$", CertificationWorkflow, StringComparison.Ordinal)
            .Replace("$SHA$", certifiedSha, StringComparison.Ordinal)
            .Replace("$UPDATED_AT$", completedAt, StringComparison.Ordinal));
        File.WriteAllText(
            certificationJobsPath,
            """
            {"total_count":3,"jobs":[{"name":"Test lane","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"},{"name":"Decompiler corpus lane","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"},{"name":"Release certification","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"}]}
            """
            .Replace("$COMPLETED_AT$", completedAt, StringComparison.Ordinal));
        File.WriteAllText(
            targetRunPath,
            """
            {"path":"$WORKFLOW$","event":"push","head_branch":"main","head_sha":"$SHA$","status":"completed","conclusion":"success","updated_at":"$UPDATED_AT$"}
            """
            .Replace("$WORKFLOW$", TargetWorkflow, StringComparison.Ordinal)
            .Replace("$SHA$", laterSha, StringComparison.Ordinal)
            .Replace("$UPDATED_AT$", completedAt, StringComparison.Ordinal));
        string targetJobsPath = Path.Combine(scratch, "target-jobs.json");
        File.WriteAllText(
            targetJobsPath,
            """
            {"total_count":1,"jobs":[{"name":"ci-required","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"}]}
            """
            .Replace("$COMPLETED_AT$", completedAt, StringComparison.Ordinal));
        File.WriteAllText(
            comparisonPath,
            """
            {"status":"ahead","base_commit":{"sha":"$SHA$"}}
            """
            .Replace("$SHA$", certifiedSha, StringComparison.Ordinal));

        ValidationResult parsed = Validate(
            ReadRun(certificationRunPath),
            ReadJobs(certificationJobsPath),
            ReadRun(targetRunPath),
            ReadJobs(targetJobsPath),
            ReadComparison(comparisonPath),
            true,
            TimeSpan.FromHours(36),
            now);
        Assert(parsed.IsLaterCommit, "GitHub API response parsing should preserve the later commit.");
    }
    finally
    {
        Directory.Delete(scratch, recursive: true);
    }

    Console.WriteLine("Release certification validator self-test passed.");
}

static void ExpectFailure(Action action, string messageFragment)
{
    try
    {
        action();
    }
    catch (InvalidOperationException ex) when (
        ex.Message.Contains(messageFragment, StringComparison.Ordinal))
    {
        return;
    }

    throw new InvalidOperationException($"Expected failure containing '{messageFragment}'.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed record RunInfo(
    string WorkflowPath,
    string Event,
    string HeadBranch,
    string HeadSha,
    string Status,
    string Conclusion,
    DateTimeOffset UpdatedAt);

sealed record JobInfo(string Name, string Status, string Conclusion, DateTimeOffset CompletedAt);

sealed record ComparisonInfo(string Status, string BaseCommitSha);

sealed record ValidationResult(string CertifiedSha, string TargetSha, bool IsLaterCommit);
