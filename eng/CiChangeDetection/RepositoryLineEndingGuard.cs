using System.Diagnostics;
using System.Text;

namespace CiChangeDetection;

/// <summary>
/// Validates the working-tree half of the repository's line-ending policy.
/// </summary>
internal static class RepositoryLineEndingGuard
{
    internal static void Validate(string repository)
    {
        string inventory = RunGit(repository, "ls-files", "--eol", "-z");
        string[] records =
            inventory.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (records.Length == 0)
        {
            throw new InvalidOperationException(
                "The repository line-ending inventory was empty.");
        }

        const string Canary =
            "\teng/test-ci-change-detection.cs";
        if (!records.Any(record =>
            record.EndsWith(Canary, StringComparison.Ordinal)
            && HasLfPolicy(record)))
        {
            throw new InvalidOperationException(
                "The repository line-ending inventory did not contain the "
                + "guard source with the expected LF policy.");
        }

        string[] offenders = FindUnexpectedWorkingTreeEndings(records);
        if (offenders.Length != 0)
        {
            throw new InvalidOperationException(
                "Tracked files declared eol=lf have CRLF or mixed "
                + "working-tree endings:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders)
                + Environment.NewLine
                + Environment.NewLine
                + "From the repository root on a clean working tree, "
                + "refresh tracked files with:"
                + Environment.NewLine
                + "git rm --cached -r ."
                + Environment.NewLine
                + "git reset --hard HEAD");
        }
    }

    internal static void AssertContract()
    {
        string[] records =
        [
            "i/lf    w/lf    attr/text eol=lf\tgood.cs",
            "i/lf    w/crlf  attr/text eol=lf\tstale.cs",
            "i/lf    w/mixed attr/text eol=lf\tmixed.cs",
            "i/-text w/-text attr/-text\tfixture.dll",
            "i/lf    w/crlf  attr/text\tunconstrained.txt",
        ];

        string[] actual = FindUnexpectedWorkingTreeEndings(records);
        string[] expected = ["mixed.cs", "stale.cs"];
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The repository line-ending guard no longer honors the "
                + "effective Git attributes.");
        }
    }

    private static string[] FindUnexpectedWorkingTreeEndings(
        IEnumerable<string> records)
    {
        var offenders = new List<string>();
        foreach (string record in records)
        {
            int pathSeparator = record.IndexOf('\t');
            if (pathSeparator < 0)
            {
                continue;
            }

            if (!HasLfPolicy(record)
                || !WorkingTreeHasUnexpectedEnding(record[..pathSeparator]))
            {
                continue;
            }

            offenders.Add(record[(pathSeparator + 1)..]);
        }

        return [.. offenders.Order(StringComparer.Ordinal)];
    }

    private static bool HasLfPolicy(string record)
    {
        int pathSeparator = record.IndexOf('\t');
        string metadata = pathSeparator < 0 ? record : record[..pathSeparator];
        return metadata
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("eol=lf", StringComparer.Ordinal);
    }

    private static bool WorkingTreeHasUnexpectedEnding(string metadata) =>
        metadata
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(static field => field is "w/crlf" or "w/mixed");

    private static string RunGit(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--no-optional-locks");
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["GIT_ASKPASS"] = "";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        try
        {
            process.WaitForExitAsync(timeout.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw new InvalidOperationException(
                "git ls-files --eol did not complete within 30 seconds.");
        }

        string output = stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git ls-files --eol failed with exit code "
                + $"{process.ExitCode}: {error}");
        }

        return output;
    }
}
