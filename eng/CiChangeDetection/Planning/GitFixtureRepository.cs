using System.Diagnostics;
using System.Text;

namespace CiChangeDetection.Planning;

/// <summary>
/// A real temporary Git repository. The planner fixtures run against actual
/// Git output rather than a synthesized stream, so path bytes, statuses, and
/// framing are the ones the production reader will see in CI.
/// </summary>
internal sealed class GitFixtureRepository : IDisposable
{
    private GitFixtureRepository(string root) => Root = root;

    /// <summary>
    /// Gets the repository root directory.
    /// </summary>
    internal string Root { get; }

    /// <summary>
    /// Creates an initialized repository under the scratch root.
    /// </summary>
    /// <param name="scratchRoot">The scratch directory.</param>
    /// <returns>The initialized fixture repository.</returns>
    internal static GitFixtureRepository Create(string scratchRoot)
    {
        string root = Path.Combine(scratchRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        GitFixtureRepository repository = new(root);
        repository.Git("init", "--quiet", "--initial-branch", "main");
        repository.Git("config", "user.email", "fixture@example.invalid");
        repository.Git("config", "user.name", "Fixture");
        repository.Git("config", "commit.gpgsign", "false");
        repository.Git("config", "core.autocrlf", "false");
        return repository;
    }

    /// <summary>
    /// Writes a file, creating any parent directories.
    /// </summary>
    /// <param name="relativePath">The repository-relative path.</param>
    /// <param name="content">The file content.</param>
    internal void Write(string relativePath, string content)
    {
        string full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>
    /// Deletes a file from the worktree.
    /// </summary>
    /// <param name="relativePath">The repository-relative path.</param>
    internal void Remove(string relativePath) =>
        File.Delete(Path.Combine(Root, relativePath));

    /// <summary>
    /// Replaces a regular file with a symbolic link so Git reports a type
    /// change rather than a modification.
    /// </summary>
    /// <param name="relativePath">The repository-relative path.</param>
    /// <param name="target">The link target.</param>
    internal void ReplaceWithSymbolicLink(string relativePath, string target)
    {
        string full = Path.Combine(Root, relativePath);
        File.Delete(full);
        File.CreateSymbolicLink(full, target);
    }

    /// <summary>
    /// Stages every change and commits it.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <returns>The resulting commit object ID.</returns>
    internal string CommitAll(string message)
    {
        Git("add", "--all", "--");
        Git("commit", "--quiet", "--allow-empty", "-m", message);
        return Head();
    }

    /// <summary>
    /// Gets the checked commit object ID.
    /// </summary>
    /// <returns>The head commit object ID.</returns>
    internal string Head() =>
        Git("rev-parse", "--verify", "HEAD^{commit}").Trim();

    /// <summary>
    /// Checks out an existing commit as a detached head.
    /// </summary>
    /// <param name="objectId">The commit to check out.</param>
    internal void CheckoutDetached(string objectId) =>
        Git("checkout", "--quiet", "--detach", objectId);

    /// <summary>
    /// Runs a Git command in the fixture repository.
    /// </summary>
    /// <param name="arguments">The argument vector.</param>
    /// <returns>The command's standard output.</returns>
    internal string Git(params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git")
        {
            UseShellExecute = false,
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["HOME"] = Root;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 60_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} did not complete.");
        }

        string output = outputTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited "
                + $"{process.ExitCode}: "
                + errorTask.GetAwaiter().GetResult());
        }

        return output;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Renders the acquired evidence as ordered <c>status:path</c> text for
    /// assertions. Paths are decoded only for the assertion message.
    /// </summary>
    /// <param name="evidence">The acquired evidence.</param>
    /// <returns>The rendered record list.</returns>
    internal static string Render(ChangeEvidence evidence) =>
        string.Join(
            ", ",
            evidence.Records.Select(record =>
                $"{(char)ChangeRecord.StatusByte(record.Status)}:"
                + Encoding.UTF8.GetString(record.Path)));
}
