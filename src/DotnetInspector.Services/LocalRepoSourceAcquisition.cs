using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace DotnetInspector.Services;

/// <summary>
/// Reads a single authored source file from a user-specified local git clone, keyed on the
/// SourceLink commit + repo-relative path and authenticated against the portable-PDB checksum.
///
/// This is the opt-in local-repository counterpart to <see cref="AuthoredSourceAcquisition"/>'s
/// remote SourceLink fetch. For a reproducible (published) build the PDB records a normalized,
/// non-local document path plus a raw.githubusercontent URL that encodes the commit SHA and the
/// repo-relative path. When the user names one or more local clones (<c>--repo</c>), we read the
/// committed blob at that exact SHA from the git object store — the committed object, not the
/// working tree — and accept it only when its bytes match the PDB checksum. A wrong repo, commit,
/// or path therefore self-rejects and the caller falls back to the network. The checksum is the
/// arbiter, so no repo-URL matching is required.
/// </summary>
public static partial class LocalRepoSourceAcquisition
{
    // The blob spec handed to git is "<sha>:<repo-relative-path>". The SHA is taken from the
    // (untrusted) PDB SourceLink URL, so require a plain hex object id before shelling out.
    [GeneratedRegex("^[0-9a-fA-F]{7,64}$")]
    private static partial Regex CommitShaRegex();

    private const int GitTimeoutMs = 15_000;

    // Upper bound on a blob we are willing to buffer; authored source files are small.
    private const long MaxBlobBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Attempts to read the authored source blob referenced by <paramref name="rawSourceUrl"/> from
    /// one of <paramref name="repositoryPaths"/>, returning the raw bytes only when they match the
    /// portable-PDB checksum. Returns <c>null</c> when the URL is not a parseable GitHub raw URL, no
    /// candidate repo has the commit/path, git is unavailable, or nothing matches the checksum.
    /// </summary>
    public static byte[]? TryReadVerifiedRepoBlob(
        string? rawSourceUrl,
        string? checksumAlgorithm,
        byte[]? checksum,
        IReadOnlyList<string> repositoryPaths)
    {
        if (repositoryPaths is null || repositoryPaths.Count == 0)
            return null;
        if (checksum is not { Length: > 0 } || string.IsNullOrEmpty(checksumAlgorithm))
            return null;
        if (!TryParseGitHubRawUrl(rawSourceUrl, out string sha, out string relativePath))
            return null;

        foreach (var repositoryPath in repositoryPaths)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath))
                continue;

            byte[]? bytes = TryReadBlob(repositoryPath, sha, relativePath);
            if (bytes is null)
                continue;

            if (AuthoredSourceAcquisition.VerifyChecksum(checksumAlgorithm, checksum, bytes)
                is SourceChecksumVerification.Exact or SourceChecksumVerification.LineEndingNormalized)
            {
                return bytes;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a <c>https://raw.githubusercontent.com/&lt;org&gt;/&lt;repo&gt;/&lt;sha&gt;/&lt;path&gt;</c>
    /// URL into the commit SHA and repo-relative path used to address a git blob. Rejects any other
    /// host, a non-hex SHA, and a path that is empty, contains a NUL, or would be read as an option.
    /// </summary>
    internal static bool TryParseGitHubRawUrl(string? url, out string sha, out string relativePath)
    {
        sha = string.Empty;
        relativePath = string.Empty;

        if (string.IsNullOrEmpty(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || !uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // /<org>/<repo>/<sha>/<relpath...>
        string[] parts = uri.AbsolutePath.TrimStart('/').Split('/', 4);
        if (parts.Length != 4)
            return false;

        string candidateSha = parts[2];
        if (!CommitShaRegex().IsMatch(candidateSha))
            return false;

        string candidatePath = Uri.UnescapeDataString(parts[3]);
        if (candidatePath.Length == 0
            || candidatePath.Contains('\0')
            || candidatePath.StartsWith('-'))
        {
            return false;
        }

        sha = candidateSha;
        relativePath = candidatePath;
        return true;
    }

    private static byte[]? TryReadBlob(string repositoryPath, string sha, string relativePath)
    {
        string fullRepo;
        try
        {
            if (!Path.IsPathFullyQualified(repositoryPath))
                return null;
            fullRepo = Path.GetFullPath(repositoryPath);
            if (!Directory.Exists(fullRepo))
                return null;
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = fullRepo,
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(fullRepo);
        startInfo.ArgumentList.Add("cat-file");
        startInfo.ArgumentList.Add("blob");
        startInfo.ArgumentList.Add($"{sha}:{relativePath}");
        // Never let git block on a credential prompt or take a repo lock for this read-only lookup.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // git is not installed / not on PATH.
            return null;
        }

        if (process is null)
            return null;

        using (process)
        {
            Task<byte[]?> stdoutTask = Task.Run(() => ReadCappedStdout(process.StandardOutput.BaseStream));
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(GitTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
                return null;
            }

            // Ensure the redirected streams are fully drained before inspecting the exit code.
            process.WaitForExit();

            byte[]? bytes;
            try { bytes = stdoutTask.GetAwaiter().GetResult(); }
            catch { return null; }
            try { _ = stderrTask.GetAwaiter().GetResult(); }
            catch { /* ignore stderr read failure */ }

            return process.ExitCode == 0 ? bytes : null;
        }
    }

    private static byte[]? ReadCappedStdout(Stream stream)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > MaxBlobBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
