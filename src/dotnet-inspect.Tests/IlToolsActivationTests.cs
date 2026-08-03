using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DotnetInspector.Tests;

/// <summary>
/// The gate for <c>eng/activate-iltools.sh</c>.
/// </summary>
/// <remarks>
/// <para>
/// A child process cannot change its parent's PATH, so <c>eng/restore-iltools.sh</c>
/// can only print the directories it restored and leave the caller to assemble
/// them. That assembly used to live in a documentation snippet, where nothing
/// executed it: three consecutive review rounds found three separate silent
/// defects in it (a lost trailing newline gluing the last directory to the
/// first pre-existing PATH entry, an exit status masked outside
/// <c>set -e</c>, and whitespace-only output prepending an empty PATH element,
/// which means the current directory). Each failure left a plausible-looking
/// PATH with no oracles on it, so the suites that depend on those oracles
/// reported a green run that proved nothing.
/// </para>
/// <para>
/// These tests are what makes that assembly a verified property rather than a
/// carefully worded claim. Each one drives the real
/// <c>eng/activate-iltools.sh</c> through a stub producer and asserts on the
/// resulting PATH, so a regression in any of those failure modes fails here.
/// <see cref="Activate_WhenRestoreScriptIsMissing_ReportsThePathItLookedIn"/>
/// is the non-vacuity test: it fails if the file stops resolving
/// <c>restore-iltools.sh</c> next to itself, which is the wiring the rest of
/// the class stubs out.
/// </para>
/// </remarks>
public class IlToolsActivationTests
{
    static readonly string RepoRoot = FindRepoRoot();
    static readonly string ActivateScript = Path.Combine(RepoRoot, "eng", "activate-iltools.sh");
    static readonly string RestoreScript = Path.Combine(RepoRoot, "eng", "restore-iltools.sh");

    static readonly bool HasBash = CanRunBash();

    const string SkipReason = "bash is not available on this machine";

    // Reports the sourcing shell's PATH without swallowing a failure. Wrapping
    // the `source` in `|| true` instead would both hide its status and disable
    // the caller's errexit for the sourced file, which is the condition under
    // test in the errexit cases.
    const string ReportPathOnExit = """trap 'printf "PATH=%s\n" "$PATH"' EXIT""";

    // A stub that emits two directories the way restore-iltools.sh does.
    const string TwoDirectoryProducer = """
        #!/bin/sh
        echo "ARGS:[$*]" >&2
        printf '%s\n' /tmp/iltools-a /tmp/iltools-b
        """;

    [Fact]
    public void RepositoryShipsBothScripts()
    {
        Assert.True(File.Exists(ActivateScript), $"Expected {ActivateScript} to exist.");
        Assert.True(File.Exists(RestoreScript), $"Expected {RestoreScript} to exist.");
    }

    [Fact]
    public void Activate_PrependsEmittedDirectoriesInOrder()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(TwoDirectoryProducer, initialPath: "/orig1:/orig2");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-a", "/tmp/iltools-b", "/orig1", "/orig2"], result.PathEntries);
    }

    [Fact]
    public void Activate_ForwardsItsArgumentsToTheRestoreScript()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(TwoDirectoryProducer, initialPath: "/orig1", arguments: "--rid linux-x64 --mdv");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ARGS:[--rid linux-x64 --mdv]", result.Stderr);
    }

    /// <summary>
    /// The trailing-newline defect: joining without one glues the last emitted
    /// directory to the first pre-existing PATH entry, corrupting both.
    /// </summary>
    [Fact]
    public void Activate_KeepsTheLastEmittedDirectorySeparateFromExistingEntries()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        // This producer deliberately omits the final newline, so the defect
        // cannot be masked by the producer being well behaved.
        var result = Activate(
            """
            #!/bin/sh
            printf '%s\n%s' /tmp/iltools-a /tmp/iltools-b
            """,
            initialPath: "/orig1");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-a", "/tmp/iltools-b", "/orig1"], result.PathEntries);
    }

    /// <summary>
    /// The empty-element defect: an empty PATH entry means the current
    /// directory, so it is a correctness and safety hazard, not cosmetic.
    /// </summary>
    [Fact]
    public void Activate_WhenPathWasEmpty_DoesNotLeaveAnEmptyElement()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(TwoDirectoryProducer, initialPath: "");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-a", "/tmp/iltools-b"], result.PathEntries);
        Assert.All(result.PathEntries, entry => Assert.NotEqual("", entry));
    }

    [Fact]
    public void Activate_SourcedTwice_DoesNotDuplicateEntries()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(TwoDirectoryProducer, initialPath: "/orig1", sourceCount: 2);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-a", "/tmp/iltools-b", "/orig1"], result.PathEntries);
    }

    /// <summary>
    /// The status-masking defect, in the shell where it actually bites: without
    /// <c>set -e</c> nothing else notices a failed restore, so the file has to
    /// notice it itself.
    /// </summary>
    [Fact]
    public void Activate_WhenRestoreFails_ReportsItsStatusAndLeavesPathUnchanged()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(
            """
            #!/bin/sh
            echo "restore blew up" >&2
            exit 3
            """,
            initialPath: "/orig1:/orig2");

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(["/orig1", "/orig2"], result.PathEntries);
        Assert.Contains("PATH left unchanged", result.Stderr);
    }

    [Fact]
    public void Activate_WhenRestoreFails_UnderErrexit_StillReportsBeforeUnwinding()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(
            """
            #!/bin/sh
            exit 3
            """,
            initialPath: "/orig1",
            callerOptions: "set -euo pipefail");

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(["/orig1"], result.PathEntries);
        Assert.Contains("PATH left unchanged", result.Stderr);
    }

    [Fact]
    public void Activate_UnderErrexitAndNounset_SucceedsOnTheHappyPath()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(TwoDirectoryProducer, initialPath: "/orig1", callerOptions: "set -euo pipefail");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-a", "/tmp/iltools-b", "/orig1"], result.PathEntries);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"   \n\t\n\n")]
    public void Activate_WhenRestorePrintsNoUsableDirectories_FailsAndLeavesPathUnchanged(string output)
    {
        Assert.SkipUnless(HasBash, SkipReason);

        // `output` is a printf *format*, so its escapes are interpreted by the
        // stub rather than by C#.
        var result = Activate(
            $"""
            #!/bin/sh
            printf '{output}'
            """,
            initialPath: "/orig1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(["/orig1"], result.PathEntries);
        Assert.Contains("PATH left unchanged", result.Stderr);
    }

    /// <summary>
    /// Directories are compared and joined as literal strings. A repository
    /// checked out under a path containing a glob metacharacter must not have
    /// its PATH entries treated as patterns.
    /// </summary>
    [Fact]
    public void Activate_TreatsGlobMetacharactersInDirectoriesLiterally()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(
            """
            #!/bin/sh
            printf '%s\n' '/tmp/we[i]rd*dir' '/tmp/plain'
            """,
            initialPath: "/orig1");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/we[i]rd*dir", "/tmp/plain", "/orig1"], result.PathEntries);
    }

    /// <summary>
    /// The file's whole job is repairing PATH, so it must not need a working
    /// PATH to run: it uses shell builtins rather than dirname(1) and tr(1).
    /// </summary>
    [Fact]
    public void Activate_WithAnUnusablePath_StillWorks()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(
            """
            #!/bin/sh
            printf '%s\n' /tmp/iltools-a
            """,
            initialPath: "/nonexistent-directory");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-a", "/nonexistent-directory"], result.PathEntries);
    }

    /// <summary>
    /// Executing rather than sourcing the file cannot work, so it must say so
    /// rather than exit 0 having changed nothing.
    /// </summary>
    [Fact]
    public void Activate_WhenExecutedInsteadOfSourced_FailsWithGuidance()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        using var scratch = new ScratchDirectory(TwoDirectoryProducer);
        var result = RunBash($"\"{scratch.ActivatePath}\"", initialPath: null);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must be sourced, not executed", result.Stderr);
    }

    /// <summary>
    /// Non-vacuity: the other tests stub <c>restore-iltools.sh</c>, so they
    /// would keep passing if the file stopped looking for it next to itself.
    /// This one fails when that wiring dies.
    /// </summary>
    [Fact]
    public void Activate_WhenRestoreScriptIsMissing_ReportsThePathItLookedIn()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        using var scratch = new ScratchDirectory(producer: null);
        var result = RunBash($"{ReportPathOnExit}\nsource \"{scratch.ActivatePath}\"", initialPath: "/orig1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(Path.Combine(scratch.Path, "restore-iltools.sh"), result.Stderr);
        Assert.Equal(["/orig1"], result.PathEntries);
    }

    /// <summary>
    /// Keeps the documentation pointing at the tested artifact. The defect
    /// class these tests close was a hand-written PATH incantation in a doc
    /// snippet, and it comes straight back if one is reintroduced.
    /// </summary>
    [Fact]
    public void AgentsMarkdown_DelegatesIlToolsPathAssemblyToTheScript()
    {
        var agents = File.ReadAllText(Path.Combine(RepoRoot, "AGENTS.md"));

        Assert.Contains("source eng/activate-iltools.sh", agents);

        foreach (var block in FencedBashBlocks(agents).Where(b => b.Contains("iltools")))
        {
            Assert.False(
                block.Contains("export PATH") || block.Contains("tr '\\n'"),
                $"AGENTS.md assembles PATH by hand instead of sourcing eng/activate-iltools.sh:\n{block}");
        }
    }

    static IEnumerable<string> FencedBashBlocks(string markdown)
    {
        var lines = markdown.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() is not "```bash" && lines[i].TrimEnd() is not "```sh")
                continue;

            int end = i + 1;
            while (end < lines.Length && lines[end].TrimEnd() != "```")
                end++;

            yield return string.Join('\n', lines[(i + 1)..Math.Min(end, lines.Length)]);
            i = end;
        }
    }

    // --- harness ---

    sealed record ActivationResult(int ExitCode, string Stdout, string Stderr)
    {
        // The script reports the sourcing shell's PATH on a marker line, so the
        // test observes that shell rather than this process's environment.
        public string[] PathEntries
        {
            get
            {
                var line = Stdout.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("PATH=", StringComparison.Ordinal));

                if (line is null)
                    return [];

                var path = line["PATH=".Length..].TrimEnd('\r');
                return path.Length == 0 ? [] : path.Split(':');
            }
        }
    }

    /// <summary>
    /// A directory holding a copy of the real activate script next to a stub
    /// <c>restore-iltools.sh</c>, so the script under test is the shipped one
    /// while its producer is controlled.
    /// </summary>
    sealed class ScratchDirectory : IDisposable
    {
        public string Path { get; }
        public string ActivatePath { get; }

        public ScratchDirectory(string? producer)
        {
            Path = Directory.CreateTempSubdirectory("iltools-activate-").FullName;
            ActivatePath = System.IO.Path.Combine(Path, "activate-iltools.sh");
            File.Copy(ActivateScript, ActivatePath);
            MakeExecutable(ActivatePath);

            if (producer is not null)
            {
                var stub = System.IO.Path.Combine(Path, "restore-iltools.sh");
                File.WriteAllText(stub, producer + "\n");
                MakeExecutable(stub);
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory is not worth failing a test over.
            }
        }

        static void MakeExecutable(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    static ActivationResult Activate(
        string producer,
        string initialPath,
        string arguments = "",
        int sourceCount = 1,
        string callerOptions = "")
    {
        using var scratch = new ScratchDirectory(producer);

        // The EXIT trap reports PATH without swallowing the failure.
        var lines = new List<string>();
        if (callerOptions.Length > 0)
            lines.Add(callerOptions);
        lines.Add(ReportPathOnExit);
        for (int i = 0; i < sourceCount; i++)
            lines.Add($"source \"{scratch.ActivatePath}\" {arguments}");

        return RunBash(string.Join('\n', lines), initialPath);
    }

    static ActivationResult RunBash(string script, string? initialPath)
    {
        var info = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(script);

        if (initialPath is not null)
            info.Environment["PATH"] = initialPath;

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ActivationResult(process.ExitCode, stdout, stderr);
    }

    static bool CanRunBash()
    {
        try
        {
            var info = new ProcessStartInfo("bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("exit 0");

            using var process = Process.Start(info)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "dotnet-inspect.slnx")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not locate the repository root (dotnet-inspect.slnx) from the test output directory.");
        return dir!.FullName;
    }
}
