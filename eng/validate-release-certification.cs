using System.Globalization;
using System.Text.Json;

const string CertificationWorkflow = ".github/workflows/deep-inspect.yml";
const string TargetWorkflow = ".github/workflows/ci.yml";
const string CertificationJob = "Release certification";
const string TestJob = "Test lane";
const string WindowsPlatformJob = "Platform test (win-x64)";
const string MacOSPlatformJob = "Platform test (osx-arm64)";
const string LinuxPlatformJob = "Platform test (linux-x64)";
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
    bool acceptFailedCertification = bool.Parse(
        Required(options, "--accept-failed-certification"));
    string githubOutput = Required(options, "--github-output");

    ValidationResult result = Validate(
        certification,
        certificationJobs,
        target,
        targetJobs,
        comparison,
        allowLaterCommit,
        acceptFailedCertification);

    File.AppendAllText(
        githubOutput,
        $"sha={result.TargetSha}\n" +
        $"certified_sha={result.CertifiedSha}\n" +
        $"later_commit={result.IsLaterCommit.ToString().ToLowerInvariant()}\n" +
        $"certification_failures_accepted={result.AcceptedFailures.ToString().ToLowerInvariant()}\n");

    Console.WriteLine(
        result.IsLaterCommit
            ? $"Publishing later commit {result.TargetSha}; Deep Inspect observed ancestor {result.CertifiedSha}."
            : $"Publishing observed commit {result.TargetSha}.");
    Console.WriteLine($"Deep Inspect evidence: {result.OutcomeSummary}.");
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
    bool acceptFailedCertification)
{
    RequireCertificationRun(
        certification,
        "certification");
    RequireSuccessfulRun(target, TargetWorkflow, ["push"], "target CI", requireMain: true);

    JobInfo certificationJob = RequireCompletedEvidenceJob(certificationJobs, CertificationJob);
    JobInfo testJob = RequireCompletedEvidenceJob(certificationJobs, TestJob);
    JobInfo windowsPlatformJob = RequireCompletedEvidenceJob(certificationJobs, WindowsPlatformJob);
    JobInfo macOSPlatformJob = RequireCompletedEvidenceJob(certificationJobs, MacOSPlatformJob);
    JobInfo linuxPlatformJob = RequireCompletedEvidenceJob(certificationJobs, LinuxPlatformJob);
    JobInfo corpusJob = RequireCompletedEvidenceJob(certificationJobs, CorpusJob);
    JobInfo targetCiJob = RequireSuccessfulJob(targetJobs, TargetCiJob);

    if (certificationJob.CompletedAt < testJob.CompletedAt ||
        certificationJob.CompletedAt < windowsPlatformJob.CompletedAt ||
        certificationJob.CompletedAt < macOSPlatformJob.CompletedAt ||
        certificationJob.CompletedAt < linuxPlatformJob.CompletedAt ||
        certificationJob.CompletedAt < corpusJob.CompletedAt)
    {
        throw new InvalidOperationException(
            "Release certification predates a required validation job; rerun the complete test lane.");
    }

    JobInfo[] evidenceJobs =
    [
        testJob,
        windowsPlatformJob,
        macOSPlatformJob,
        linuxPlatformJob,
        corpusJob,
        certificationJob,
    ];
    var failedEvidence = new List<string>();
    if (certification.Conclusion != "success")
        failedEvidence.Add($"workflow={certification.Conclusion}");
    failedEvidence.AddRange(
        evidenceJobs
            .Where(job => job.Conclusion != "success")
            .Select(job => $"{job.Name}={job.Conclusion}"));
    if (failedEvidence.Count > 0 && !acceptFailedCertification)
    {
        throw new InvalidOperationException(
            $"Deep Inspect evidence is not green ({string.Join(", ", failedEvidence)}). " +
            "Review these outcomes and explicitly enable accept_failed_certification to publish.");
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

    string outcomeSummary = string.Join(
        ", ",
        evidenceJobs.Select(job => $"{job.Name}={job.Conclusion}"));
    return new ValidationResult(
        certification.HeadSha,
        target.HeadSha,
        isLaterCommit,
        failedEvidence.Count > 0,
        outcomeSummary);
}

static JobInfo RequireCompletedEvidenceJob(IReadOnlyList<JobInfo> jobs, string name)
{
    JobInfo[] matchingJobs = jobs.Where(job => job.Name == name).ToArray();
    if (matchingJobs.Length != 1)
    {
        throw new InvalidOperationException(
            $"Run contains {matchingJobs.Length} '{name}' jobs; expected one.");
    }

    JobInfo job = matchingJobs[0];
    if (job.Status != "completed" || job.Conclusion == "skipped")
    {
        throw new InvalidOperationException(
            $"Required evidence job '{name}' is {job.Status}/{job.Conclusion}; " +
            "it must complete rather than be skipped.");
    }

    return job;
}

static JobInfo RequireSuccessfulJob(IReadOnlyList<JobInfo> jobs, string name)
{
    JobInfo job = RequireCompletedEvidenceJob(jobs, name);
    if (job.Conclusion != "success")
    {
        throw new InvalidOperationException(
            $"Job '{name}' is {job.Status}/{job.Conclusion}, not completed/success.");
    }

    return job;
}

static void RequireCertificationRun(RunInfo run, string label)
{
    RequireRunIdentity(
        run,
        CertificationWorkflow,
        ["schedule", "workflow_dispatch"],
        label);
    if (run.Event == "schedule" && run.HeadBranch != "main")
        throw new InvalidOperationException($"{label} scheduled run targets branch {run.HeadBranch}, not main.");
    if (run.Status != "completed" ||
        (run.Conclusion != "success" && run.Conclusion != "failure"))
    {
        throw new InvalidOperationException(
            $"{label} run is {run.Status}/{run.Conclusion}; expected a completed success or failure.");
    }
}

static void RequireSuccessfulRun(
    RunInfo run,
    string expectedWorkflow,
    IReadOnlyCollection<string> allowedEvents,
    string label,
    bool requireMain)
{
    RequireRunIdentity(run, expectedWorkflow, allowedEvents, label);
    if (requireMain && run.HeadBranch != "main")
        throw new InvalidOperationException($"{label} run targets branch {run.HeadBranch}, not main.");
    if (run.Status != "completed" || run.Conclusion != "success")
    {
        throw new InvalidOperationException(
            $"{label} run is {run.Status}/{run.Conclusion}, not completed/success.");
    }
}

static void RequireRunIdentity(
    RunInfo run,
    string expectedWorkflow,
    IReadOnlyCollection<string> allowedEvents,
    string label)
{
    if (run.WorkflowPath != expectedWorkflow)
        throw new InvalidOperationException($"{label} run uses {run.WorkflowPath}, not {expectedWorkflow}.");
    if (!allowedEvents.Contains(run.Event))
        throw new InvalidOperationException($"{label} run event {run.Event} is not allowed.");
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
        new(WindowsPlatformJob, "completed", "success", now.AddMinutes(-58)),
        new(MacOSPlatformJob, "completed", "success", now.AddMinutes(-57)),
        new(LinuxPlatformJob, "completed", "success", now.AddMinutes(-56)),
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
        false);
    Assert(!exact.IsLaterCommit, "Exact certified commit should pass without an override.");
    Assert(!exact.AcceptedFailures, "Green evidence should not report accepted failures.");

    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs,
            laterTarget,
            successfulTargetJobs,
            new("ahead", certifiedSha),
            false,
            false),
        "explicitly enable allow_later_commit");

    ValidationResult later = Validate(
        certification,
        successfulJobs,
        laterTarget,
        successfulTargetJobs,
        new("ahead", certifiedSha),
        true,
        false);
    Assert(later.IsLaterCommit, "Explicitly accepted descendant should pass.");

    ValidationResult oldEvidence = Validate(
        certification with { UpdatedAt = now.AddDays(-30) },
        successfulJobs
            .Select(job => job with { CompletedAt = job.CompletedAt.AddDays(-30) })
            .ToArray(),
        exactTarget,
        successfulTargetJobs,
        new("identical", certifiedSha),
        false,
        false);
    Assert(!oldEvidence.AcceptedFailures, "Age should not turn green evidence into a failure.");

    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs
                .Select(job => job.Name == LinuxPlatformJob
                    ? job with { CompletedAt = now.AddMinutes(-10) }
                    : job)
                .ToArray(),
            exactTarget,
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            false),
        "predates a required validation job");
    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs,
            laterTarget,
            successfulTargetJobs,
            new("diverged", "3333333333333333333333333333333333333333"),
            true,
            false),
        "certified commit or its descendant");

    JobInfo[] failedJobs = successfulJobs
        .Select(job => job.Name switch
        {
            TestJob => job with { Conclusion = "failure" },
            WindowsPlatformJob => job with { Conclusion = "cancelled" },
            _ => job,
        })
        .ToArray();
    ExpectFailure(
        () => Validate(
            certification with { Conclusion = "failure" },
            failedJobs,
            exactTarget,
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            false),
        "accept_failed_certification");

    ValidationResult acceptedFailures = Validate(
        certification with
        {
            Event = "workflow_dispatch",
            HeadBranch = "release/0.26.0-certification",
            Conclusion = "failure",
        },
        failedJobs,
        laterTarget,
        successfulTargetJobs,
        new("ahead", certifiedSha),
        true,
        true);
    Assert(acceptedFailures.AcceptedFailures, "Explicitly accepted failed evidence should be reported.");
    Assert(
        acceptedFailures.OutcomeSummary.Contains(
            $"{WindowsPlatformJob}=cancelled",
            StringComparison.Ordinal),
        "Accepted evidence should disclose each required job conclusion.");

    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs.Where(job => job.Name != WindowsPlatformJob).ToArray(),
            exactTarget,
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            false),
        WindowsPlatformJob);
    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs
                .Select(job => job.Name == MacOSPlatformJob
                    ? job with { Conclusion = "skipped" }
                    : job)
                .ToArray(),
            exactTarget,
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            true),
        "must complete rather than be skipped");
    ExpectFailure(
        () => Validate(
            certification,
            [.. successfulJobs, successfulJobs.Single(job => job.Name == LinuxPlatformJob)],
            exactTarget,
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            false),
        "expected one");
    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs,
            exactTarget with { WorkflowPath = ".github/workflows/release.yml" },
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            false),
        "not .github/workflows/ci.yml");
    ExpectFailure(
        () => Validate(
            certification,
            successfulJobs,
            exactTarget,
            [new(TargetCiJob, "completed", "failure", now.AddMinutes(1))],
            new("identical", certifiedSha),
            false,
            false),
        "ci-required");
    ExpectFailure(
        () => Validate(
            certification with { Conclusion = "cancelled" },
            failedJobs,
            exactTarget,
            successfulTargetJobs,
            new("identical", certifiedSha),
            false,
            true),
        "completed success or failure");

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
            {"total_count":6,"jobs":[{"name":"Test lane","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"},{"name":"Platform test (win-x64)","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"},{"name":"Platform test (osx-arm64)","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"},{"name":"Platform test (linux-x64)","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"},{"name":"Decompiler corpus lane","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"},{"name":"Release certification","status":"completed","conclusion":"success","completed_at":"$COMPLETED_AT$"}]}
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
            false);
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

sealed record ValidationResult(
    string CertifiedSha,
    string TargetSha,
    bool IsLaterCommit,
    bool AcceptedFailures,
    string OutcomeSummary);
