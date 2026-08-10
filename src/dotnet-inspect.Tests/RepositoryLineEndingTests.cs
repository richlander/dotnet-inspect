using System.Diagnostics;
using System.Text;

namespace DotnetInspector.Tests;

public sealed class RepositoryLineEndingTests
{
    /// <summary>
    /// Gate for the working-tree half of <c>.gitattributes</c>. Git normalizes
    /// content before comparison, so <c>git status</c> cannot reveal a stale
    /// Windows checkout whose tracked files remain CRLF after the LF policy was
    /// added. Git's own EOL inventory supplies both the working-tree ending and
    /// the effective attribute without guessing which files are text.
    /// </summary>
    [Fact]
    public async Task TrackedLfFilesHaveLfWorkingTreeEndings()
    {
        string root = FindRepositoryRoot();
        string inventory = await RunGitAsync(root, "ls-files", "--eol", "-z");
        string[] records = inventory.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(records);
        Assert.Contains(
            records,
            static record =>
                record.EndsWith(
                    "\tsrc/dotnet-inspect.Tests/RepositoryLineEndingTests.cs",
                    StringComparison.Ordinal)
                && HasLfPolicy(record));

        string[] offenders = FindUnexpectedWorkingTreeEndings(records);

        Assert.True(
            offenders.Length == 0,
            $$"""
            Tracked files declared eol=lf have CRLF or mixed working-tree endings:
            {{string.Join(Environment.NewLine, offenders)}}

            From the repository root on a clean working tree, refresh tracked files with:
            git rm --cached -r .
            git reset --hard HEAD
            """);
    }

    [Fact]
    public void LineEndingGateHonorsEffectiveGitAttributes()
    {
        string[] records =
        [
            "i/lf    w/lf    attr/text eol=lf\tgood.cs",
            "i/lf    w/crlf  attr/text eol=lf\tstale.cs",
            "i/lf    w/mixed attr/text eol=lf\tmixed.cs",
            "i/-text w/-text attr/-text\tfixture.dll",
            "i/lf    w/crlf  attr/text\tunconstrained.txt",
        ];

        Assert.Equal(
            ["mixed.cs", "stale.cs"],
            FindUnexpectedWorkingTreeEndings(records));
    }

    private static string[] FindUnexpectedWorkingTreeEndings(
        IEnumerable<string> records)
    {
        var offenders = new List<string>();
        foreach (string record in records)
        {
            int pathSeparator = record.IndexOf('\t');
            if (pathSeparator < 0)
                continue;

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

    private static async Task<string> RunGitAsync(
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
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        string output = await stdout;
        string error = await stderr;
        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {error}");
        return output;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }
}
