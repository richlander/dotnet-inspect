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

    static readonly string? BashExecutable = FindShell("bash");
    static readonly string? DashExecutable = FindShell("dash");
    static readonly bool HasBash = BashExecutable is not null;
    static readonly bool HasDash = DashExecutable is not null;

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
        Assert.Contains(
            $"{Path.GetFileName(scratch.Path)}/restore-iltools.sh",
            result.Stderr.Replace('\\', '/'));
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
            $"export CDPATH=\"{ToShellPath(scratch.Path)}\"",
            ReportPathOnExit,
            "source eng/activate-iltools.sh",
        ]);

        var result = RunBash(script, "/orig1", workingDirectory: scratch.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["/tmp/iltools-a", "/tmp/iltools-b", "/orig1"], result.PathEntries);
    }

    /// <summary>
    /// Interactive Git Bash activation needs MSYS-form paths so drive colons do
    /// not split PATH. Native consumers need Windows form instead, so the
    /// producer must honor the explicit mode rather than ambient $GITHUB_PATH.
    /// </summary>
    [Theory]
    [InlineData("", "/msys", "C:/native")]
    [InlineData("--native-paths", "C:/native", "/msys")]
    public void Restore_EmitsRequestedWindowsPathFormat(
        string arguments,
        string expectedPrefix,
        string rejectedPrefix)
    {
        Assert.SkipUnless(HasBash, SkipReason);

        using var stubs = new ScratchDirectory(producer: null);
        string version = Assert.Single(
            File.ReadLines(RestoreScript),
            line => line.StartsWith("ILTOOLS_VERSION=", StringComparison.Ordinal))
            ["ILTOOLS_VERSION=".Length..];
        string root = Directory.CreateDirectory(Path.Combine(stubs.Path, "repo")).FullName;
        string packages = Path.Combine(root, "artifacts", "iltools", "packages");

        foreach ((string package, string tool, string marker) in new[]
        {
            ("runtime.win-x64.microsoft.netcore.ilasm", "ilasm.exe", "Usage: ilasm"),
            ("runtime.win-x64.microsoft.netcore.ildasm", "ildasm.exe", "Usage: ildasm"),
        })
        {
            string directory = Directory.CreateDirectory(
                Path.Combine(packages, package, version, "runtimes", "win-x64", "native")).FullName;
            string toolPath = Path.Combine(directory, tool);
            File.WriteAllText(toolPath, $"#!/bin/sh\nprintf '%s\\n' '{marker}'\n");
            ScratchDirectory.MakeExecutablePublic(toolPath);
        }

        var info = new ProcessStartInfo(BashExecutable!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot,
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(
            $$"""
            git() {
                [ "$1 $2" = "rev-parse --show-toplevel" ] || return 2
                printf '%s\n' "$ILTOOLS_TEST_ROOT"
            }
            dotnet() {
                [ "$1" = "restore" ] || return 2
                return 0
            }
            cygpath() {
                case "$1" in
                    -w) printf 'C:/native%s\n' "$2" ;;
                    -u) printf '/msys%s\n' "$2" ;;
                    *) return 2 ;;
                esac
            }
            source "{{ToShellPath(RestoreScript)}}" --rid win-x64 {{arguments}}
            """);
        info.Environment["GITHUB_PATH"] = Path.Combine(stubs.Path, "github_path");
        info.Environment["ILTOOLS_TEST_ROOT"] = ToShellPath(root);

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"restore-iltools.sh exited {process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.DoesNotContain(rejectedPrefix, stdout);
        string[] paths = stdout.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(paths.Length == 2, $"Expected two emitted paths.\nstdout:\n{stdout}");
        Assert.All(paths, path => Assert.StartsWith(expectedPrefix, path));
        Assert.Contains(paths, path => path.Contains("ilasm", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, path => path.Contains("ildasm", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("error:", stderr, StringComparison.OrdinalIgnoreCase);
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
        Assert.SkipUnless(HasDash, "dash is not available on this machine");

        using var scratch = new ScratchDirectory(TwoDirectoryProducer);
        var result = Run(DashExecutable!, $". \"{scratch.ActivatePath}\"", initialPath: null);

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

    [Fact]
    public void SlowWorkflows_FailAfterOracleRestoreFailure()
    {
        string workflow = File.ReadAllText(
            Path.Combine(RepoRoot, ".github", "workflows", "deep-inspect.yml"));
        int jobStart = workflow.IndexOf(
            "\n  test:\n",
            StringComparison.Ordinal);
        Assert.True(jobStart >= 0);
        int nextJob = workflow.IndexOf(
            "\n  platform-test:",
            jobStart,
            StringComparison.Ordinal);
        Assert.True(jobStart < nextJob);
        string job = workflow[jobStart..nextJob];
        int stepsStart = job.IndexOf(
            "\n    steps:\n",
            StringComparison.Ordinal);
        Assert.True(stepsStart >= 0);
        string jobHeader = job[..stepsStart];
        Assert.DoesNotContain("continue-on-error:", jobHeader);

        int install = job.IndexOf(
            "- name: Install ilasm/ildasm",
            StringComparison.Ordinal);
        int cliTests = job.IndexOf(
            "- name: Run CLI tests (including slow integration)",
            StringComparison.Ordinal);
        int decompilerTests = job.IndexOf(
            "- name: Run decompiler tests",
            StringComparison.Ordinal);
        int roundTrip = job.IndexOf(
            "- name: Run IL round-trip tests (including slow sweep)",
            StringComparison.Ordinal);
        int nextInstallStep = job.IndexOf(
            "\n      - ",
            install + 1,
            StringComparison.Ordinal);
        int terminalCheck = job.IndexOf(
            "- name: Check ilasm/ildasm result",
            StringComparison.Ordinal);

        Assert.True(install >= 0);
        Assert.True(install < nextInstallStep);
        Assert.True(nextInstallStep <= cliTests);
        Assert.True(install < cliTests);
        Assert.True(install < decompilerTests);
        Assert.True(cliTests < roundTrip);
        Assert.True(decompilerTests < roundTrip);
        Assert.True(roundTrip < terminalCheck);

        string installStep = job[install..nextInstallStep];
        Assert.Equal(
            1,
            installStep.Split('\n')
                .Count(line => line.Trim() == "id: iltools"));
        Assert.Equal(
            1,
            job.Split('\n')
                .Count(line => line.Trim() == "id: iltools"));
        Assert.Equal(
            ["continue-on-error: true"],
            installStep.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(
                    "continue-on-error:",
                    StringComparison.Ordinal)));
        Assert.Contains(
            "run: eng/restore-iltools.sh --rid linux-x64 >> \"$GITHUB_PATH\"",
            installStep.Split('\n').Select(line => line.Trim()));
        Assert.DoesNotContain(
            installStep.Split('\n'),
            line => line.TrimStart().StartsWith("if:", StringComparison.Ordinal));

        string checkStep = job[terminalCheck..];
        Assert.Equal(
            ["if: steps.iltools.outcome != 'success'"],
            checkStep.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("if:", StringComparison.Ordinal)));
        Assert.Contains(
            "run: |",
            checkStep.Split('\n').Select(line => line.Trim()));
        Assert.Contains(
            "exit 1",
            checkStep.Split('\n').Select(line => line.Trim()));
        Assert.DoesNotContain("continue-on-error:", checkStep);
        Assert.DoesNotContain("\n      - ", checkStep);
    }

    [Fact]
    public void PlatformWorkflow_RestoresOraclesAndExposesGitBashBeforeCliTests()
    {
        string workflow = File.ReadAllText(
            Path.Combine(RepoRoot, ".github", "workflows", "deep-inspect.yml"));
        int jobStart = workflow.IndexOf(
            "\n  platform-test:\n",
            StringComparison.Ordinal);
        int nextJob = workflow.IndexOf(
            "\n  decompiler-corpus:",
            jobStart,
            StringComparison.Ordinal);
        Assert.True(jobStart >= 0);
        Assert.True(jobStart < nextJob);
        string job = workflow[jobStart..nextJob];
        int stepsStart = job.IndexOf(
            "\n    steps:\n",
            StringComparison.Ordinal);
        Assert.True(stepsStart >= 0);
        string jobHeader = job[..stepsStart];
        Assert.Contains("inputs.lane == 'test'", jobHeader);
        Assert.Contains("inputs.lane == 'platform-test'", jobHeader);
        Assert.Contains("inputs.lane == 'all'", jobHeader);
        Assert.Contains("- os: windows-latest\n            rid: win-x64", jobHeader);
        Assert.Contains("- os: macos-latest\n            rid: osx-arm64", jobHeader);
        Assert.Contains("- os: ubuntu-26.04\n            rid: linux-x64", jobHeader);
        Assert.Contains("timeout-minutes: 90", jobHeader);

        int install = job.IndexOf(
            "- name: Install ilasm/ildasm/mdv",
            StringComparison.Ordinal);
        int cliTests = job.IndexOf(
            "- name: Run CLI tests (all)",
            StringComparison.Ordinal);
        int terminalCheck = job.IndexOf(
            "- name: Check ilasm/ildasm/mdv result",
            StringComparison.Ordinal);
        Assert.True(install >= 0);
        Assert.True(install < cliTests);
        Assert.True(cliTests < terminalCheck);

        int nextInstallStep = job.IndexOf(
            "\n      - ",
            install + 1,
            StringComparison.Ordinal);
        string installStep = job[install..nextInstallStep];
        Assert.Contains("id: iltools", installStep);
        Assert.Contains("continue-on-error: true", installStep);
        Assert.Contains("timeout-minutes: 5", installStep);
        Assert.Contains("shell: bash", installStep);
        Assert.Contains("ILTOOLS_BASH=", installStep);
        Assert.Contains("if [ \"$RUNNER_OS\" = Windows ]; then", installStep);
        Assert.Contains(
            "eng/restore-iltools.sh --rid ${{ matrix.rid }} --mdv --native-paths >> \"$GITHUB_PATH\"",
            installStep);

        string checkStep = job[terminalCheck..];
        Assert.Contains(
            "if: ${{ !cancelled() && steps.build.outcome == 'success' && " +
            "steps.iltools.outcome != 'success' }}",
            checkStep);
        Assert.Contains("exit 1", checkStep);
        Assert.DoesNotContain("continue-on-error:", checkStep);
        Assert.DoesNotContain("\n      - ", checkStep);
    }

    [Fact]
    public void WindowsPrWorkflow_MapsEachSelectorToOneFocusedSuite()
    {
        string workflow = File.ReadAllText(
            Path.Combine(RepoRoot, ".github", "workflows", "windows-pr.yml"))
            .ReplaceLineEndings("\n");

        Assert.Contains("name: Windows PR Validation", workflow);
        Assert.Contains("default: queries", workflow);
        Assert.Contains("runs-on: windows-latest", workflow);
        Assert.Contains(
            "      target_ref:\n" +
            "        description: 'Branch, tag, SHA, or " +
            "refs/pull/<number>/head to validate'\n" +
            "        required: true\n" +
            "        type: string",
            workflow);
        Assert.Contains(
            "group: windows-pr-${{ inputs.target_ref }}-${{ inputs.suite }}",
            workflow);
        Assert.Contains(
            "      - uses: actions/checkout@v7\n" +
            "        with:\n" +
            "          ref: ${{ inputs.target_ref }}",
            workflow);
        Assert.Equal(
            1,
            workflow.Split(
                "uses: actions/checkout@v7",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("\n  schedule:", workflow);
        Assert.DoesNotContain("platform-test", workflow);
        Assert.Contains(
            "run: dotnet build dotnet-inspect.slnx -c Release",
            workflow);

        (string Suite, string Command)[] suites =
        [
            (
                "queries",
                "dotnet run --project " +
                "src/DotnetInspector.Queries.Tests -c Release"),
            (
                "cli",
                "dotnet run --project src/dotnet-inspect.Tests -c Release -- " +
                "--filter-not-trait \"Speed=Slow\""),
            (
                "services",
                "dotnet run --project " +
                "src/DotnetInspector.Services.Tests -c Release"),
            (
                "analysis",
                "dotnet run --project src/ILInspector.Analysis.Tests " +
                "-c Release -- --filter-not-trait \"Speed=Slow\""),
            (
                "decompiler",
                "dotnet run --project src/ILInspector.Decompiler.Tests " +
                "-c Release -- -trait- \"Speed=Slow\""),
            (
                "metadata",
                "dotnet run --project tests/ILInspector.Metadata.Tests " +
                "-c Release"),
        ];
        Assert.Contains(
            "        options:\n" +
            "          - queries\n" +
            "          - cli\n" +
            "          - services\n" +
            "          - analysis\n" +
            "          - decompiler\n" +
            "          - metadata\n\n" +
            "permissions:",
            workflow);

        foreach ((string suite, string command) in suites)
        {
            Assert.Contains($"          - {suite}", workflow);
            Assert.Contains(
                $"if: inputs.suite == '{suite}'\n        run: {command}",
                workflow);
        }

        foreach (string suite in new[]
        {
            "cli",
            "metadata",
        })
        {
            Assert.Equal(
                3,
                workflow.Split(
                    $"inputs.suite == '{suite}'",
                    StringSplitOptions.None).Length - 1);
        }

        foreach (string suite in new[]
        {
            "queries",
            "services",
            "analysis",
            "decompiler",
        })
        {
            Assert.Equal(
                1,
                workflow.Split(
                    $"inputs.suite == '{suite}'",
                    StringSplitOptions.None).Length - 1);
        }

        int checkout = workflow.IndexOf(
            "- uses: actions/checkout@v7",
            StringComparison.Ordinal);
        int build = workflow.IndexOf("- name: Build", StringComparison.Ordinal);
        int install = workflow.IndexOf(
            "- name: Install ilasm/ildasm/mdv",
            StringComparison.Ordinal);
        int firstSuite = workflow.IndexOf(
            "- name: Run query tests",
            StringComparison.Ordinal);
        int lastSuite = workflow.IndexOf(
            "- name: Run metadata tests",
            StringComparison.Ordinal);
        int terminalCheck = workflow.IndexOf(
            "- name: Check ilasm/ildasm/mdv result",
            StringComparison.Ordinal);
        Assert.True(checkout >= 0);
        Assert.True(build >= 0);
        Assert.True(checkout < build);
        Assert.True(build < install);
        Assert.True(install < firstSuite);
        Assert.True(firstSuite < lastSuite);
        Assert.True(lastSuite < terminalCheck);
        foreach (string oracleSuiteStep in new[]
        {
            "Run CLI tests",
            "Run metadata tests",
        })
        {
            int oracleSuite = workflow.IndexOf(
                $"- name: {oracleSuiteStep}",
                StringComparison.Ordinal);
            Assert.True(install < oracleSuite);
        }

        int nextStep = workflow.IndexOf(
            "\n      - ",
            install + 1,
            StringComparison.Ordinal);
        string installStep = workflow[install..nextStep];
        Assert.Equal(
            "- name: Install ilasm/ildasm/mdv\n" +
            "        id: iltools\n" +
            "        if: >-\n" +
            "          inputs.suite == 'cli' ||\n" +
            "          inputs.suite == 'metadata'\n" +
            "        continue-on-error: true\n" +
            "        timeout-minutes: 5\n" +
            "        shell: bash\n" +
            "        run: |\n" +
            "          printf 'ILTOOLS_BASH=%s\\n' " +
            "\"$(cygpath -w \"$(command -v bash)\")\" >> \"$GITHUB_ENV\"\n" +
            "          eng/restore-iltools.sh --rid win-x64 --mdv " +
            "--native-paths >> \"$GITHUB_PATH\"\n",
            installStep);
        Assert.Equal(
            1,
            workflow.Split(
                "eng/restore-iltools.sh --rid win-x64 --mdv --native-paths",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            workflow.Split(
                "continue-on-error:",
                StringSplitOptions.None).Length - 1);

        string checkStep = workflow[terminalCheck..].TrimEnd();
        Assert.Equal(
            "- name: Check ilasm/ildasm/mdv result\n" +
            "        if: >-\n" +
            "          !cancelled() &&\n" +
            "          steps.build.outcome == 'success' &&\n" +
            "          steps.iltools.outcome != 'success' &&\n" +
            "          (\n" +
            "            inputs.suite == 'cli' ||\n" +
            "            inputs.suite == 'metadata'\n" +
            "          )\n" +
            "        shell: bash\n" +
            "        run: |\n" +
            "          echo \"::error::Restoring ilasm/ildasm/mdv failed, " +
            "so oracle-backed tests skipped.\" >&2\n" +
            "          exit 1",
            checkStep);
    }

    [Fact]
    public void DeepInspectWorkflow_CertifiesDailyAndOnDemand()
    {
        string workflow = File.ReadAllText(
            Path.Combine(RepoRoot, ".github", "workflows", "deep-inspect.yml"));
        int certification = workflow.IndexOf(
            "\n  release-certification:\n",
            StringComparison.Ordinal);
        int nextJob = workflow.IndexOf(
            "\n  census:\n",
            certification,
            StringComparison.Ordinal);

        Assert.Contains("- cron: '0 6 * * *'", workflow);
        Assert.Contains(
            "group: deep-inspect-${{ github.ref }}-${{ github.event.schedule || inputs.lane }}",
            workflow);
        Assert.True(certification >= 0);
        Assert.True(certification < nextJob);
        string job = workflow[certification..nextJob];
        Assert.Contains("needs: [test, platform-test, decompiler-corpus]", job);
        Assert.Contains("inputs.lane == 'test'", job);
        Assert.Contains("inputs.lane == 'all'", job);
        Assert.Contains("TEST_RESULT: ${{ needs.test.result }}", job);
        Assert.Contains(
            "PLATFORM_RESULT: ${{ needs.platform-test.result }}",
            job);
        Assert.Contains("CORPUS_RESULT: ${{ needs.decompiler-corpus.result }}", job);
        Assert.Contains("[ \"$TEST_RESULT\" != success ] ||", job);
        Assert.Contains("[ \"$PLATFORM_RESULT\" != success ] ||", job);
        Assert.Contains("[ \"$CORPUS_RESULT\" != success ]; then", job);
    }

    [Fact]
    public void ReleaseCertificationValidator_SelfTest()
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot,
        };
        info.ArgumentList.Add("run");
        info.ArgumentList.Add(Path.Combine("eng", "validate-release-certification.cs"));
        info.ArgumentList.Add("--");
        info.ArgumentList.Add("--self-test");

        using var process = Process.Start(info)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"Certification validator self-test failed.\nstdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.Contains("self-test passed", stdout);
    }

    [Fact]
    public void ReleaseWorkflow_TagsResolvedCiCommit()
    {
        string workflow = File.ReadAllText(
            Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));
        int releaseStep = workflow.IndexOf(
            "- name: Create GitHub Release",
            StringComparison.Ordinal);

        Assert.True(releaseStep >= 0);
        Assert.Contains(
            "target_commitish: ${{ needs.resolve.outputs.sha }}",
            workflow[releaseStep..]);
    }

    [Fact]
    public void ReleaseWorkflow_RequiresFreshCertificationBeforePackageBuilds()
    {
        string workflow = File.ReadAllText(
            Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("certification_run_id:", workflow);
        Assert.Contains("allow_later_commit:", workflow);
        Assert.Contains("CERTIFICATION_RUN_ID: ${{ inputs.certification_run_id }}", workflow);
        Assert.Contains("TARGET_RUN_ID: ${{ inputs.run_id }}", workflow);
        Assert.Equal(
            2,
            workflow.Split('\n').Count(
                line => line.Contains("bash eng/validate-release-evidence.sh")));
        int publishStart = workflow.IndexOf("\n  publish:\n", StringComparison.Ordinal);
        Assert.True(publishStart >= 0);
        string publishJob = workflow[publishStart..];
        int revalidate = publishJob.IndexOf(
            "- name: Revalidate certification before publish",
            StringComparison.Ordinal);
        int login = publishJob.IndexOf(
            "- name: NuGet login",
            StringComparison.Ordinal);
        int push = publishJob.IndexOf(
            "- name: Publish packages",
            StringComparison.Ordinal);
        Assert.True(revalidate >= 0);
        Assert.True(revalidate < login);
        Assert.True(login < push);
        Assert.Contains(
            "RESOLVED_SHA: ${{ needs.resolve.outputs.sha }}",
            publishJob[revalidate..login]);
        Assert.Contains(
            "\"$RESOLVED_SHA\"",
            publishJob[revalidate..login]);
        Assert.Contains("\n  build-native:\n    needs: resolve\n", workflow);
        Assert.Contains("\n  build-portable:\n    needs: resolve\n", workflow);
        Assert.Contains(
            "\n  publish:\n    needs: [resolve, build-native, build-portable]\n",
            workflow);
        Assert.DoesNotContain("\n  deep-inspect-test:\n", workflow);
        Assert.DoesNotContain("\n  decompiler-corpus:\n", workflow);
        Assert.DoesNotContain("restore-iltools.sh", workflow);

        string evidenceScript = File.ReadAllText(
            Path.Combine(RepoRoot, "eng", "validate-release-evidence.sh"));
        Assert.Contains(
            "[[ ! \"$certification_run_id\" =~ ^[1-9][0-9]*$ ]]",
            evidenceScript);
        Assert.Contains(
            "[[ ! \"$target_run_id\" =~ ^[1-9][0-9]*$ ]]",
            evidenceScript);
        Assert.Contains("--target-jobs \"$target_jobs\"", evidenceScript);
        Assert.Contains("--max-age-hours \"$max_age_hours\"", evidenceScript);
        Assert.Contains(
            "[ \"$resolved_sha\" != \"$expected_sha\" ]",
            evidenceScript);
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
            string nativeActivatePath = System.IO.Path.Combine(Path, "activate-iltools.sh");
            ActivatePath = ToShellPath(nativeActivatePath);
            File.Copy(ActivateScript, nativeActivatePath);
            MakeExecutable(nativeActivatePath);

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
        Run(BashExecutable!, script, initialPath, workingDirectory);

    static ActivationResult Run(string shell, string script, string? initialPath, string? workingDirectory = null)
    {
        if (initialPath is not null)
            script = $"export PATH={ShellQuote(initialPath)}\n{script}";

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

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ActivationResult(process.ExitCode, stdout, stderr);
    }

    static string ToShellPath(string path) =>
        OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    static string ShellQuote(string value) =>
        $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";

    static string? FindShell(string name)
    {
        foreach (string candidate in ShellCandidates(name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (CanRun(candidate))
                return candidate;
        }

        return null;
    }

    static IEnumerable<string> ShellCandidates(string name)
    {
        if (name == "bash" &&
            Environment.GetEnvironmentVariable("ILTOOLS_BASH") is string configured &&
            configured.Length > 0)
        {
            yield return configured;
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (string root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs"),
            }.Where(root => root.Length > 0))
            {
                if (name == "bash")
                    yield return Path.Combine(root, "Git", "bin", "bash.exe");

                yield return Path.Combine(root, "Git", "usr", "bin", $"{name}.exe");
            }
        }

        yield return name;
    }

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
