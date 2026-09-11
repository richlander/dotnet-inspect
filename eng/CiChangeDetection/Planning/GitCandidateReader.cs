using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CiChangeDetection.Planning;

/// <summary>
/// The planner's single Git boundary. It validates that both provenance
/// endpoints exist as commits and that the checked worktree is the candidate,
/// then acquires the changed-path evidence once as raw bytes.
/// </summary>
internal static class GitCandidateReader
{
    /// <summary>
    /// Validates provenance against the repository: both endpoints must
    /// resolve to themselves as commits, and <c>HEAD^{commit}</c> must equal
    /// the candidate so routing describes the tree the jobs check.
    /// </summary>
    /// <param name="repository">The repository root directory.</param>
    /// <param name="kind">The provenance kind.</param>
    /// <param name="baseObjectId">The base endpoint object ID.</param>
    /// <param name="candidateObjectId">The candidate endpoint object ID.</param>
    /// <returns>The validated provenance.</returns>
    internal static CandidateProvenance ResolveProvenance(
        string repository,
        PlanEventKind kind,
        string baseObjectId,
        string candidateObjectId)
    {
        RequireRepositoryRoot(repository);
        CandidateProvenance provenance = CandidateProvenance.Create(
            kind,
            baseObjectId,
            candidateObjectId);
        RequireCommit(repository, provenance.BaseObjectId, "base");
        RequireCommit(repository, provenance.CandidateObjectId, "candidate");

        string head = RevParse(repository, "HEAD^{commit}")
            ?? throw new PlanRefusalException(
                PlanRefusalCategory.EndpointUnresolved,
                "the checked worktree has no resolvable HEAD commit");
        if (!string.Equals(
            head,
            provenance.CandidateObjectId,
            StringComparison.Ordinal))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.CandidateMismatch,
                "the checked HEAD commit is not the candidate endpoint");
        }

        if (kind == PlanEventKind.PullRequestSyntheticCandidate)
        {
            string? firstParent = RevParse(
                repository,
                $"{provenance.CandidateObjectId}^1");
            if (!string.Equals(
                firstParent,
                provenance.BaseObjectId,
                StringComparison.Ordinal))
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.CandidateMismatch,
                    "the pull-request base endpoint is not the checked "
                    + "candidate's first parent");
            }
        }

        return provenance;
    }

    private static void RequireRepositoryRoot(string repository)
    {
        byte[] prefix;
        try
        {
            prefix = RunForBytes(
                repository,
                ["rev-parse", "--show-prefix"]);
        }
        catch (PlanRefusalException)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.Repository,
                "the repository directory is not a Git worktree root");
        }

        if (prefix.Any(value => value is not ((byte)'\r' or (byte)'\n')))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.Repository,
                "the repository directory is not the worktree root");
        }
    }

    /// <summary>
    /// Acquires the changed paths once, from the two endpoint trees, reading
    /// raw bytes rather than decoded text. Rename detection is disabled so
    /// both sides of a rename arrive as a deletion and an addition.
    /// </summary>
    /// <param name="repository">The repository root directory.</param>
    /// <param name="provenance">The validated provenance.</param>
    /// <returns>The acquired evidence.</returns>
    internal static ChangeEvidence ReadChanges(
        string repository,
        CandidateProvenance provenance)
    {
        byte[] stream = RunForBytes(
            repository,
            [
                "diff",
                "-O",
                OperatingSystem.IsWindows() ? "NUL" : "/dev/null",
                "--no-renames",
                "--name-status",
                "-z",
                provenance.BaseObjectId,
                provenance.CandidateObjectId,
                "--",
            ]);
        return ParseNameStatusStream(stream);
    }

    /// <summary>
    /// Parses a <c>--name-status -z</c> byte stream. The canonical record is
    /// exactly <c>status-byte NUL path-bytes NUL</c>; anything else, including
    /// a truncated final record or a multi-byte status token, is a refusal.
    /// </summary>
    /// <param name="stream">The raw diff bytes.</param>
    /// <returns>The parsed evidence.</returns>
    internal static ChangeEvidence ParseNameStatusStream(
        ReadOnlySpan<byte> stream)
    {
        List<ChangeRecord> records = [];
        int offset = 0;
        while (offset < stream.Length)
        {
            int statusEnd = stream[offset..].IndexOf((byte)0);
            if (statusEnd < 0)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidenceFraming,
                    "changed-path stream ends inside a status field");
            }

            if (statusEnd != 1)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidenceStatus,
                    "changed-path stream contains a non-canonical status");
            }

            ChangeStatus status =
                ChangeRecord.ParseStatusByte(stream[offset]);
            offset += statusEnd + 1;

            int pathEnd = stream[offset..].IndexOf((byte)0);
            if (pathEnd < 0)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidenceFraming,
                    "changed-path stream ends inside a path field");
            }

            records.Add(new ChangeRecord(
                status,
                stream.Slice(offset, pathEnd)));
            offset += pathEnd + 1;
        }

        return ChangeEvidence.Create(records);
    }

    private static void RequireCommit(
        string repository,
        string objectId,
        string role)
    {
        string? resolved = RevParse(repository, $"{objectId}^{{commit}}");
        if (resolved is null)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.EndpointUnresolved,
                $"the {role} endpoint does not exist in the repository");
        }

        if (!string.Equals(resolved, objectId, StringComparison.Ordinal))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.EndpointUnresolved,
                $"the {role} endpoint object is not a commit");
        }
    }

    private static string? RevParse(string repository, string revision)
    {
        byte[] output;
        try
        {
            output = RunForBytes(
                repository,
                ["rev-parse", "--verify", "--quiet", "--end-of-options", revision]);
        }
        catch (PlanRefusalException)
        {
            return null;
        }

        string text = Encoding.ASCII.GetString(output).Trim();
        return text.Length == 0 ? null : text;
    }

    private static byte[] RunForBytes(
        string repository,
        IReadOnlyList<string> arguments)
    {
        if (!Directory.Exists(repository))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.Repository,
                "the repository directory does not exist");
        }

        ProcessStartInfo startInfo = new("git")
        {
            UseShellExecute = false,
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Arguments are always passed as an argument vector: no shell is
        // involved, so a path or revision can never be reinterpreted.
        startInfo.ArgumentList.Add("--no-optional-locks");
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["GIT_ASKPASS"] = "";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";

        Process? started;
        try
        {
            started = Process.Start(startInfo);
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.EvidenceUnavailable,
                "could not start git");
        }

        using Process process = started
            ?? throw new PlanRefusalException(
                PlanRefusalCategory.EvidenceUnavailable,
                "could not start git");

        using MemoryStream buffer = new();
        Task outputTask =
            process.StandardOutput.BaseStream.CopyToAsync(buffer);
        Task<string> standardErrorTask =
            process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout =
            new(TimeSpan.FromSeconds(120));
        try
        {
            process.WaitForExitAsync(timeout.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            Drain(outputTask, standardErrorTask, TimeSpan.FromSeconds(5));
            throw new PlanRefusalException(
                PlanRefusalCategory.EvidenceUnavailable,
                "git did not complete within the acquisition budget");
        }

        Drain(outputTask, standardErrorTask, TimeSpan.FromSeconds(5));
        if (process.ExitCode != 0)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.EvidenceUnavailable,
                $"git exited with status {process.ExitCode}");
        }

        return buffer.ToArray();
    }

    private static void Drain(
        Task outputTask,
        Task<string> standardErrorTask,
        TimeSpan timeout)
    {
        try
        {
            Task.WhenAll(outputTask, standardErrorTask)
                .WaitAsync(timeout)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
            when (exception is IOException
                or InvalidOperationException
                or OperationCanceledException
                or TimeoutException)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.EvidenceUnavailable,
                "could not read git process output");
        }
    }
}
