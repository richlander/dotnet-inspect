using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CiChangeDetection;

/// <summary>
/// Admits pre-merge CI only while the latest qualifying Deep Inspect test
/// evidence is fresh and green.
/// </summary>
public static class DeepInspectHealthApp
{
    private const int DefaultMaximumAgeHours = 36;

    /// <summary>
    /// Runs the Deep Inspect health admission check.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>Zero when admission succeeds; otherwise one.</returns>
    public static int Run(string[] args)
    {
        try
        {
            if (args is ["--self-test"])
            {
                DeepInspectHealthTestSuite.Run();
                Console.WriteLine("Deep Inspect health policy self-tests passed.");
                return 0;
            }

            int maximumAgeHours = ParseMaximumAge(args);
            string repository = RequireEnvironment("GITHUB_REPOSITORY");
            string token = RequireEnvironment("GITHUB_TOKEN");
            string apiUrl = Environment.GetEnvironmentVariable("GITHUB_API_URL")
                ?? "https://api.github.com";

            using HttpClient client = CreateClient(token);
            IReadOnlyList<DeepInspectHealthRun> runs =
                LoadRuns(client, apiUrl, repository);
            DeepInspectHealthDecision decision = DeepInspectHealthPolicy.Evaluate(
                runs,
                DateTimeOffset.UtcNow,
                TimeSpan.FromHours(maximumAgeHours));

            if (!decision.Admitted)
            {
                Console.Error.WriteLine(
                    $"::error::Deep Inspect health admission failed: {decision.Message}");
                return 1;
            }

            Console.WriteLine($"Deep Inspect health admission passed: {decision.Message}");
            return 0;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or HttpRequestException
                or OperationCanceledException
                or JsonException)
        {
            Console.Error.WriteLine(
                $"::error::Deep Inspect health admission failed: {exception.Message}");
            return 1;
        }
    }

    private static HttpClient CreateClient(string token)
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "dotnet-inspect-deep-inspect-health");
        client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static IReadOnlyList<DeepInspectHealthRun> LoadRuns(
        HttpClient client,
        string apiUrl,
        string repository)
    {
        string runsUrl =
            $"{apiUrl.TrimEnd('/')}/repos/{repository}/actions/workflows/"
            + "deep-inspect.yml/runs?branch=main&status=completed&per_page=100";
        using JsonDocument document = GetJson(client, runsUrl);
        JsonElement runElements = RequireArray(document.RootElement, "workflow_runs");
        List<DeepInspectRunSummary> summaries = [];
        foreach (JsonElement runElement in runElements.EnumerateArray())
        {
            string eventName = RequireString(runElement, "event");
            if (eventName is not ("schedule" or "workflow_dispatch"))
            {
                continue;
            }

            summaries.Add(new DeepInspectRunSummary(
                RequireInt64(runElement, "id"),
                eventName,
                RequireString(runElement, "head_branch"),
                RequireString(runElement, "head_sha"),
                RequireDateTimeOffset(runElement, "updated_at"),
                RequireString(runElement, "html_url")));
        }

        foreach (DeepInspectRunSummary summary in summaries
            .Where(run => run.Branch == "main")
            .OrderByDescending(run => run.CompletedAt))
        {
            string jobsUrl =
                $"{apiUrl.TrimEnd('/')}/repos/{repository}/actions/runs/"
                + $"{summary.Id}/"
                + "jobs?filter=latest&per_page=100";
            using JsonDocument jobsDocument = GetJson(client, jobsUrl);
            JsonElement jobElements = RequireArray(
                jobsDocument.RootElement,
                "jobs");
            int totalCount = RequireInt32(
                jobsDocument.RootElement,
                "total_count");
            if (totalCount != jobElements.GetArrayLength())
            {
                throw new InvalidOperationException(
                    $"run {summary.Id} returned an incomplete job page");
            }

            List<DeepInspectHealthJob> jobs = [];
            foreach (JsonElement jobElement in jobElements.EnumerateArray())
            {
                jobs.Add(new DeepInspectHealthJob(
                    RequireString(jobElement, "name"),
                    RequireNullableString(jobElement, "conclusion")));
            }

            DeepInspectHealthRun run = new(
                summary.Id,
                summary.Event,
                summary.Branch,
                summary.HeadSha,
                summary.CompletedAt,
                summary.Url,
                jobs);
            if (DeepInspectHealthPolicy.IsQualifyingAttempt(run))
            {
                return [run];
            }
        }

        return [];
    }

    private static JsonDocument GetJson(HttpClient client, string url)
    {
        using HttpResponseMessage response = client.GetAsync(url)
            .GetAwaiter()
            .GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub API request failed with HTTP {(int)response.StatusCode}");
        }

        using Stream stream = response.Content.ReadAsStream();
        return JsonDocument.Parse(stream);
    }

    private static int ParseMaximumAge(string[] args)
    {
        if (args.Length == 0)
        {
            return DefaultMaximumAgeHours;
        }

        if (args is ["--maximum-age-hours", string value]
            && int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int hours)
            && hours > 0)
        {
            return hours;
        }

        throw new InvalidOperationException(
            "Usage: dotnet run eng/check-deep-inspect-health.cs -- "
            + "[--maximum-age-hours <positive integer>]");
    }

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"required environment variable {name} is missing");

    private static JsonElement RequireArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"GitHub API response is missing array {name}");
        }

        return value;
    }

    private static string RequireString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { Length: > 0 } text)
        {
            throw new InvalidOperationException(
                $"GitHub API response is missing string {name}");
        }

        return text;
    }

    private static string? RequireNullableString(
        JsonElement element,
        string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidOperationException(
                $"GitHub API response is missing {name}");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new InvalidOperationException(
                $"GitHub API response carries invalid {name}"),
        };
    }

    private static long RequireInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || !value.TryGetInt64(out long result))
        {
            throw new InvalidOperationException(
                $"GitHub API response is missing integer {name}");
        }

        return result;
    }

    private static int RequireInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || !value.TryGetInt32(out int result))
        {
            throw new InvalidOperationException(
                $"GitHub API response is missing integer {name}");
        }

        return result;
    }

    private static DateTimeOffset RequireDateTimeOffset(
        JsonElement element,
        string name)
    {
        string value = RequireString(element, name);
        if (!DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset result))
        {
            throw new InvalidOperationException(
                $"GitHub API response carries invalid {name}");
        }

        return result;
    }
}

internal sealed record DeepInspectHealthJob(
    string Name,
    string? Conclusion);

internal sealed record DeepInspectHealthRun(
    long Id,
    string Event,
    string Branch,
    string HeadSha,
    DateTimeOffset CompletedAt,
    string Url,
    IReadOnlyList<DeepInspectHealthJob> Jobs);

internal sealed record DeepInspectRunSummary(
    long Id,
    string Event,
    string Branch,
    string HeadSha,
    DateTimeOffset CompletedAt,
    string Url);

internal enum DeepInspectHealthOutcome
{
    Healthy,
    Missing,
    Stale,
    Future,
    Incomplete,
    NonGreen,
}

internal sealed record DeepInspectHealthDecision(
    bool Admitted,
    DeepInspectHealthOutcome Outcome,
    string Message);

internal static class DeepInspectHealthPolicy
{
    private const string TestJob = "Test lane";
    private const string DecompilerJob = "Decompiler test lane";
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

    internal static DeepInspectHealthDecision Evaluate(
        IEnumerable<DeepInspectHealthRun> runs,
        DateTimeOffset now,
        TimeSpan maximumAge)
    {
        DeepInspectHealthRun? selected = runs
            .Where(run => run.Event is "schedule" or "workflow_dispatch")
            .Where(run => run.Branch == "main")
            .OrderByDescending(run => run.CompletedAt)
            .FirstOrDefault(IsQualifyingAttempt);
        if (selected is null)
        {
            return new DeepInspectHealthDecision(
                false,
                DeepInspectHealthOutcome.Missing,
                "no completed main Deep Inspect run exercised the test lanes");
        }

        TimeSpan age = now - selected.CompletedAt;
        string prefix =
            $"run {selected.Url} at {ShortSha(selected.HeadSha)} "
            + $"completed {FormatAge(age)} ago";
        if (age < -MaximumClockSkew)
        {
            return new DeepInspectHealthDecision(
                false,
                DeepInspectHealthOutcome.Future,
                $"{prefix}; completion time is in the future");
        }

        if (age > maximumAge)
        {
            return new DeepInspectHealthDecision(
                false,
                DeepInspectHealthOutcome.Stale,
                $"{prefix}; maximum age is {maximumAge.TotalHours:0}h");
        }

        DeepInspectHealthJob? test = SingleJob(selected, TestJob);
        DeepInspectHealthJob? decompiler = SingleJob(selected, DecompilerJob);
        if (test is null || decompiler is null)
        {
            return new DeepInspectHealthDecision(
                false,
                DeepInspectHealthOutcome.Incomplete,
                $"{prefix}; required test jobs are missing or duplicated");
        }

        if (test.Conclusion != "success" || decompiler.Conclusion != "success")
        {
            return new DeepInspectHealthDecision(
                false,
                DeepInspectHealthOutcome.NonGreen,
                $"{prefix}; {TestJob}={test.Conclusion ?? "missing"}, "
                + $"{DecompilerJob}={decompiler.Conclusion ?? "missing"}");
        }

        return new DeepInspectHealthDecision(
            true,
            DeepInspectHealthOutcome.Healthy,
            $"{prefix}; both required test lanes succeeded");
    }

    internal static bool IsQualifyingAttempt(DeepInspectHealthRun run) =>
        SingleJob(run, TestJob) is { Conclusion: not "skipped" };

    private static DeepInspectHealthJob? SingleJob(
        DeepInspectHealthRun run,
        string name)
    {
        DeepInspectHealthJob[] matches =
            [.. run.Jobs.Where(job => job.Name == name)];
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string ShortSha(string sha) =>
        sha.Length > 12 ? sha[..12] : sha;

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            return $"-{Math.Abs(age.TotalMinutes):0}m";
        }

        return age.TotalHours >= 1
            ? $"{age.TotalHours:0.0}h"
            : $"{age.TotalMinutes:0}m";
    }
}
