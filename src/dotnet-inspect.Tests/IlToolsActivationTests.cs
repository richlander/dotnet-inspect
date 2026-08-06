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
    static readonly bool HasDash = CanRun("dash");

    const string SkipReason = "bash is not available on this machine";

    // Reports the sourcing shell's PATH without swallowing a failure. Wrapping
    // the `source` in `|| true` instead would both hide its status and disable
    // the caller's errexit for the sourced file, which is the condition under
    // test in the errexit cases.
    const string ReportPathOnExit = """trap 'printf "PATH=%s\n" "$PATH"' EXIT""";

    // %q renders the whole PATH as one shell-quoted token, so a newline inside
    // an entry survives the trip as a `\n` escape instead of ending the report
    // line. Plain %s cannot express the case these tests exist to pin.
    const string ReportQuotedPathOnExit = """trap 'printf "PATHQ=%q\n" "$PATH"' EXIT""";

    // A stub that emits two directories the way restore-iltools.sh does.
    // Reports the argument *count* as well as the values: a single empty
    // argument and no arguments at all are indistinguishable in "$*".
    const string TwoDirectoryProducer = """
        #!/bin/sh
        echo "ARGC:[$#] ARGS:[$*]" >&2
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
        Assert.Contains("ARGC:[3] ARGS:[--rid linux-x64 --mdv]", result.Stderr);
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
    /// Round 7: skipping a directory already on PATH is not the same as
    /// putting it first. A stale or broken copy earlier in PATH would keep
    /// shadowing the tool just restored, with success reported.
    /// </summary>
    [Fact]
    public void Activate_WhenARestoredDirectoryIsAlreadyOnPath_MovesItAheadOfTheRest()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(
            """
            #!/bin/sh
            printf '%s\n' /tmp/iltools-good
            """,
            initialPath: "/tmp/iltools-stale:/tmp/iltools-good:/orig1");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-good", "/tmp/iltools-stale", "/orig1"], result.PathEntries);
    }

    /// <summary>
    /// Reordering must not otherwise rewrite the caller's PATH. This file must
    /// not introduce an empty element, but it is not in the business of
    /// deleting one the caller already had.
    /// </summary>
    [Fact]
    public void Activate_PreservesUnrelatedEntriesVerbatimAndInOrder()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(TwoDirectoryProducer, initialPath: "/z:/tmp/iltools-b::/a");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-a", "/tmp/iltools-b", "/z", "", "/a"], result.PathEntries);
    }

    /// <summary>
    /// A directory containing a newline is legal and must survive verbatim. The
    /// colon-to-newline rewrite this file used to rely on tore such an entry in
    /// two and rejoined the halves with a colon; when the newline was trailing,
    /// the second half was empty -- an empty PATH element, which means the
    /// current directory. Activation reported success either way.
    /// </summary>
    [Fact]
    public void Activate_WhenAnExistingEntryContainsANewline_PreservesItAndAddsNoEmptyElement()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var quoted = ActivateReportingQuotedPath(
            TwoDirectoryProducer,
            initialPath: "/before:/tmp/has\nnewline:/after");

        Assert.Contains(@"\n", quoted);
        Assert.DoesNotContain("::", quoted);

        // The newline must still be inside a single entry, not promoted to a
        // separator: '/tmp/has' and 'newline' must not have become siblings.
        Assert.DoesNotContain("/tmp/has:", quoted);
    }

    /// <summary>
    /// A trailing newline is the variant that produced an empty element, so it
    /// gets its own case rather than riding on the interior one.
    /// </summary>
    [Fact]
    public void Activate_WhenAnExistingEntryEndsWithANewline_AddsNoEmptyElement()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var quoted = ActivateReportingQuotedPath(
            TwoDirectoryProducer,
            initialPath: "/tmp/trailing\n:/after");

        Assert.DoesNotContain("::", quoted);
        Assert.Contains(@"\n", quoted);
    }

    /// <summary>
    /// This file is meant to be sourced interactively, and CDPATH is an
    /// interactive setting. When `cd` finds the target through CDPATH it prints
    /// the resolved directory on stdout, which command substitution captures
    /// ahead of `pwd` -- leaving a two-line script directory that resolves to
    /// nothing, so activation fails on a machine where nothing is wrong.
    ///
    /// The script must be sourced by a *relative* path for this to bite: `cd`
    /// consults CDPATH only for a relative target, so an absolute `source`
    /// would exercise nothing. The layout mirrors the documented invocation --
    /// `source eng/activate-iltools.sh` from the repository root, with CDPATH
    /// naming a directory that also holds an `eng`.
    /// </summary>
    [Fact]
    public void Activate_WithCdpathSet_StillResolvesItsOwnDirectory()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        using var scratch = new ScratchDirectory(producer: null);

        var eng = Directory.CreateDirectory(Path.Combine(scratch.Path, "eng")).FullName;
        var activate = Path.Combine(eng, "activate-iltools.sh");
        File.Copy(ActivateScript, activate);
        ScratchDirectory.MakeExecutablePublic(activate);

        var stub = Path.Combine(eng, "restore-iltools.sh");
        File.WriteAllText(stub, TwoDirectoryProducer + "\n");
        ScratchDirectory.MakeExecutablePublic(stub);

        var script = string.Join('\n', [
            $"export CDPATH=\"{scratch.Path}\"",
            ReportPathOnExit,
            "source eng/activate-iltools.sh",
        ]);

        var result = RunBash(script, "/orig1", workingDirectory: scratch.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-a", "/tmp/iltools-b", "/orig1"], result.PathEntries);
    }

    /// <summary>
    /// The producer emits MSYS-form paths on Git Bash so drive colons cannot
    /// split a PATH. That form is unusable in $GITHUB_PATH, where a later pwsh
    /// step's .NET processes resolve it through Win32 -- the tools would go
    /// unfound and every oracle test would skip, green, which is the exact
    /// failure these scripts exist to prevent. No Windows lane exists today, so
    /// the producer must refuse rather than emit something silently inert.
    /// </summary>
    [Fact]
    public void Restore_WhenEmittingMsysPathsIntoGithubPath_RefusesInsteadOfSkippingQuietly()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        using var stubs = new ScratchDirectory(producer: null);

        // Stand in for Git Bash: the producer keys the MSYS rewrite on cygpath
        // being present, so a stub on PATH reaches the guard on any host.
        var cygpath = Path.Combine(stubs.Path, "cygpath");
        File.WriteAllText(cygpath, "#!/bin/sh\nshift; echo \"$1\"\n");
        ScratchDirectory.MakeExecutablePublic(cygpath);

        var info = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot,
        };
        info.ArgumentList.Add(RestoreScript);
        info.ArgumentList.Add("--rid");
        info.ArgumentList.Add("linux-x64");
        info.Environment["PATH"] = stubs.Path + ":" + Environment.GetEnvironmentVariable("PATH");
        info.Environment["GITHUB_PATH"] = Path.Combine(stubs.Path, "github_path");

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("GITHUB_PATH", stderr);
        Assert.Equal("", stdout.Trim());
    }

    /// <summary>
    /// `source file` with no arguments leaves the sourcing script's own
    /// positional parameters visible to the sourced file, and bash offers no
    /// way to tell those apart from real arguments. An empty argument is the
    /// documented escape hatch for a caller that has its own.
    /// </summary>
    [Fact]
    public void Activate_TreatsAnEmptyArgumentAsNoArguments()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(TwoDirectoryProducer, initialPath: "/orig1", arguments: "\"\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ARGC:[0]", result.Stderr);
        Assert.Equal(["/tmp/iltools-a", "/tmp/iltools-b", "/orig1"], result.PathEntries);
    }

    /// <summary>
    /// When parameters do leak through, the failure has to name the trap;
    /// "unknown argument '--fast'" from a script the caller never invoked is
    /// otherwise unattributable.
    /// </summary>
    [Fact]
    public void Activate_WhenAnArgumentIsRejected_NamesThePositionalParameterTrap()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(
            """
            #!/bin/sh
            echo "error: unknown argument '$1'." >&2
            exit 2
            """,
            initialPath: "/orig1",
            arguments: "--fast");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("positional parameters", result.Stderr);
        Assert.Equal(["/orig1"], result.PathEntries);
    }

    /// <summary>
    /// The non-bash guard has to survive being read by a POSIX shell. Array
    /// syntax is a substitution error in dash, which aborts before any guard
    /// can print anything useful.
    /// </summary>
    [Fact]
    public void Activate_SourcedFromAPosixShell_ExplainsItselfInsteadOfCrashing()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows(),
            "Git for Windows can provide dash, but a POSIX shell cannot source the Windows path to this script.");
        Assert.SkipUnless(HasDash, "dash is not available on this machine");

        using var scratch = new ScratchDirectory(TwoDirectoryProducer);
        var result = Run("dash", $". \"{scratch.ActivatePath}\"", initialPath: null);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("needs bash", result.Stderr);
        Assert.DoesNotContain("Bad substitution", result.Stderr);
    }

    /// <summary>
    /// Round 7: a nonblank line containing ':' passes the whitespace guard but
    /// splits into multiple PATH elements, and a colon-only line produces empty
    /// ones -- the current-directory hazard, reintroduced through the producer.
    /// </summary>
    [Theory]
    [InlineData(":")]
    [InlineData("/tmp/we:ird")]
    [InlineData("/tmp/ok:/tmp/smuggled")]
    public void Activate_WhenAProducedDirectoryContainsAColon_FailsAndLeavesPathUnchanged(string emitted)
    {
        Assert.SkipUnless(HasBash, SkipReason);

        var result = Activate(
            $"""
            #!/bin/sh
            printf '%s\n' '{emitted}'
            """,
            initialPath: "/orig1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(["/orig1"], result.PathEntries);
        Assert.Contains("PATH left unchanged", result.Stderr);
    }

    /// <summary>
    /// Round 7: a shell that already defines one of the helper names would
    /// otherwise have it silently destroyed -- or, if it is readonly, would run
    /// its own function in place of ours and report success having restored
    /// nothing.
    /// </summary>
    [Fact]
    public void Activate_WhenTheHelperNamesAreTaken_RefusesRatherThanRunningTheCallersFunction()
    {
        Assert.SkipUnless(HasBash, SkipReason);

        using var scratch = new ScratchDirectory(TwoDirectoryProducer);
        var result = RunBash(
            $$"""
            __iltools_activate() { printf 'CALLER FUNCTION RAN\n'; return 0; }
            readonly -f __iltools_activate
            {{ReportPathOnExit}}
            source "{{scratch.ActivatePath}}"
            """,
            initialPath: "/orig1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("CALLER FUNCTION RAN", result.Stdout);
        Assert.Contains("__iltools_", result.Stderr);
        Assert.Equal(["/orig1"], result.PathEntries);
    }

    /// <summary>
    /// Keeps the documentation pointing at the tested artifact. The defect
    /// class these tests close was a hand-written PATH incantation in a doc
    /// snippet, so the gate rejects any runnable snippet that invokes the
    /// producer directly -- `PATH="$(eng/restore-iltools.sh):$PATH"` never
    /// mentions `export` and would otherwise sail through.
    /// </summary>
    [Fact]
    public void AgentsMarkdown_DelegatesIlToolsPathAssemblyToTheScript()
    {
        var agents = File.ReadAllText(Path.Combine(RepoRoot, "AGENTS.md"));

        Assert.Contains("source eng/activate-iltools.sh", agents);

        foreach (var block in FencedBashBlocks(agents).Where(b => b.Contains("iltools")))
        {
            Assert.False(
                block.Contains("restore-iltools.sh"),
                $"AGENTS.md tells the reader to run the producer directly instead of sourcing\n" +
                $"eng/activate-iltools.sh, which puts the PATH assembly back outside the gate:\n{block}");

            Assert.False(
                block.Contains("PATH="),
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

        public static void MakeExecutablePublic(string path) => MakeExecutable(path);
    }

    /// <summary>
    /// Sources the script and returns the caller shell's PATH in shell-quoted
    /// form, so entries containing newlines stay observable.
    /// </summary>
    static string ActivateReportingQuotedPath(string producer, string initialPath)
    {
        using var scratch = new ScratchDirectory(producer);

        var script = string.Join('\n', [
            ReportQuotedPathOnExit,
            $"source \"{scratch.ActivatePath}\"",
        ]);

        var result = RunBash(script, initialPath);

        Assert.Equal(0, result.ExitCode);

        var line = result.Stdout.Split('\n')
            .FirstOrDefault(l => l.StartsWith("PATHQ=", StringComparison.Ordinal));

        Assert.NotNull(line);
        return line["PATHQ=".Length..];
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

    static ActivationResult RunBash(string script, string? initialPath, string? workingDirectory = null) =>
        Run("bash", script, initialPath, workingDirectory);

    static ActivationResult Run(string shell, string script, string? initialPath, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(shell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (workingDirectory is not null)
            info.WorkingDirectory = workingDirectory;
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

    static bool CanRunBash() => CanRun("bash");

    static bool CanRun(string shell)
    {
        try
        {
            var info = new ProcessStartInfo(shell)
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
