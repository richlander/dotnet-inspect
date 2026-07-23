using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace DotnetInspector.Services.Tests;

public class LocalRepoSourceReadTests
{
    const string Source =
        "class Sample\n{\n    public int M()\n    {\n        return 1;\n    }\n}\n";

    const string RelativePath = "src/Sample.cs";

    // ---------- URL parsing (no git required) ----------

    [Fact]
    public void ParsesGitHubRawUrl_IntoShaAndPath()
    {
        bool ok = LocalRepoSourceAcquisition.TryParseGitHubRawUrl(
            "https://raw.githubusercontent.com/dotnet/runtime/0123456789abcdef0123456789abcdef01234567/src/libraries/Foo.cs",
            out string sha, out string path);

        Assert.True(ok);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", sha);
        Assert.Equal("src/libraries/Foo.cs", path);
    }

    [Fact]
    public void ParsesGitHubRawUrl_UnescapesPath()
    {
        bool ok = LocalRepoSourceAcquisition.TryParseGitHubRawUrl(
            "https://raw.githubusercontent.com/o/r/0123456789abcdef0123456789abcdef01234567/src/My%20File.cs",
            out _, out string path);

        Assert.True(ok);
        Assert.Equal("src/My File.cs", path);
    }

    [Theory]
    // Wrong host: never address a non-GitHub URL as a github raw blob.
    [InlineData("https://example.com/o/r/0123456789abcdef0123456789abcdef01234567/a.cs")]
    // Non-hex commit segment.
    [InlineData("https://raw.githubusercontent.com/o/r/not-a-sha/a.cs")]
    // Missing path segment.
    [InlineData("https://raw.githubusercontent.com/o/r/0123456789abcdef0123456789abcdef01234567")]
    // Not an absolute URL.
    [InlineData("raw.githubusercontent.com/o/r/0123456789abcdef0123456789abcdef01234567/a.cs")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsNonAddressableUrls(string? url)
    {
        Assert.False(LocalRepoSourceAcquisition.TryParseGitHubRawUrl(url, out _, out _));
    }

    // ---------- git blob read (requires git) ----------

    [Fact]
    public void ReadsBlob_WhenChecksumMatches()
    {
        RequireGit();
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var (repo, sha) = InitRepoWithFile(RelativePath, content);
        try
        {
            byte[]? result = LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                RawUrl(sha, RelativePath), "SHA256", SHA256.HashData(content), [repo]);

            Assert.NotNull(result);
            Assert.Equal(content, result);
        }
        finally
        {
            TryDeleteDirectory(repo);
        }
    }

    [Fact]
    public void ReturnsNull_WhenChecksumMismatches()
    {
        RequireGit();
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var (repo, sha) = InitRepoWithFile(RelativePath, content);
        try
        {
            // Bytes exist in the repo but do not match the recorded hash: refuse, so the caller
            // falls back to the network rather than surfacing an unverified blob.
            byte[]? result = LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                RawUrl(sha, RelativePath), "SHA256",
                SHA256.HashData(Encoding.UTF8.GetBytes(Source + "tampered")), [repo]);

            Assert.Null(result);
        }
        finally
        {
            TryDeleteDirectory(repo);
        }
    }

    [Fact]
    public void ReturnsNull_WhenCommitNotPresent()
    {
        RequireGit();
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var (repo, _) = InitRepoWithFile(RelativePath, content);
        try
        {
            byte[]? result = LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                RawUrl("0000000000000000000000000000000000000000", RelativePath),
                "SHA256", SHA256.HashData(content), [repo]);

            Assert.Null(result);
        }
        finally
        {
            TryDeleteDirectory(repo);
        }
    }

    [Fact]
    public void ReturnsNull_WhenPathNotPresent()
    {
        RequireGit();
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var (repo, sha) = InitRepoWithFile(RelativePath, content);
        try
        {
            byte[]? result = LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                RawUrl(sha, "src/Missing.cs"), "SHA256", SHA256.HashData(content), [repo]);

            Assert.Null(result);
        }
        finally
        {
            TryDeleteDirectory(repo);
        }
    }

    [Fact]
    public void ReturnsNull_WhenDirectoryIsNotAGitRepo()
    {
        RequireGit();
        byte[] content = Encoding.UTF8.GetBytes(Source);
        string dir = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-notrepo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            byte[]? result = LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                RawUrl("0123456789abcdef0123456789abcdef01234567", RelativePath),
                "SHA256", SHA256.HashData(content), [dir]);

            Assert.Null(result);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void ReadsBlob_FromSecondRepo_WhenFirstLacksCommit()
    {
        RequireGit();
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var (emptyRepo, _) = InitRepoWithFile("other/Unrelated.cs", Encoding.UTF8.GetBytes("// noise\n"));
        var (goodRepo, sha) = InitRepoWithFile(RelativePath, content);
        try
        {
            byte[]? result = LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                RawUrl(sha, RelativePath), "SHA256", SHA256.HashData(content),
                [emptyRepo, goodRepo]);

            Assert.NotNull(result);
            Assert.Equal(content, result);
        }
        finally
        {
            TryDeleteDirectory(emptyRepo);
            TryDeleteDirectory(goodRepo);
        }
    }

    [Fact]
    public void ReturnsNull_WhenNoReposProvided()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        byte[]? result = LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
            RawUrl("0123456789abcdef0123456789abcdef01234567", RelativePath),
            "SHA256", SHA256.HashData(content), []);

        Assert.Null(result);
    }

    // ---------- helpers ----------

    static string RawUrl(string sha, string path)
        => $"https://raw.githubusercontent.com/owner/repo/{sha}/{path}";

    static void RequireGit()
    {
        if (!GitAvailable())
            Assert.Skip("git is not available on PATH.");
    }

    static bool GitAvailable()
    {
        try
        {
            using Process? p = Process.Start(new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null)
                return false;
            p.WaitForExit(5000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    static (string Repo, string Sha) InitRepoWithFile(string relativePath, byte[] content)
    {
        string repo = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        Git(repo, "config", "user.email", "test@example.com");
        Git(repo, "config", "user.name", "Test");
        Git(repo, "config", "commit.gpgsign", "false");
        // Keep the stored blob byte-identical to what we write so the checksum is deterministic.
        Git(repo, "config", "core.autocrlf", "false");

        string full = Path.Combine(repo, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);

        Git(repo, "add", relativePath);
        Git(repo, "commit", "-q", "-m", "add source");
        string sha = GitCapture(repo, "rev-parse", "HEAD").Trim();
        return (repo, sha);
    }

    static void Git(string repo, params string[] args)
    {
        var (exit, _, stderr) = RunGit(repo, args);
        if (exit != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({exit}): {stderr}");
    }

    static string GitCapture(string repo, params string[] args)
    {
        var (exit, stdout, stderr) = RunGit(repo, args);
        if (exit != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({exit}): {stderr}");
        return stdout;
    }

    static (int Exit, string StdOut, string StdErr) RunGit(string repo, string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
        return (process.ExitCode, stdout, stderr);
    }

    static void TryDeleteDirectory(string path)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* best effort */ }
            }
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temp directory cleanup is best-effort.
        }
    }
}
