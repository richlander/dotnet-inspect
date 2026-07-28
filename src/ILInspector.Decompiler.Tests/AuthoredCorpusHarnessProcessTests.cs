using System.Diagnostics;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Black-box gates on the harness <em>binary</em> for the authored-corpus gate flags
/// (#3245).
///
/// <para>These exist because of a defect that five separate review rounds found five
/// separate instances of: <c>Program.cs</c> owns an entry point, so it cannot be linked
/// into this project, so every decision it makes is unreachable by an ordinary test. The
/// response each time was to move one more term of the rule into
/// <see cref="AuthoredCorpusExitContract"/> and — for what could not be moved — to pin
/// the call site by reading its source text. Both responses were wrong.</para>
///
/// <para>Moving terms only relocates the seam. Review round nine deleted a single
/// argument from the <c>JudgeGateFlags</c> call, so the harness passed <c>false</c> where
/// it should have passed <c>verifyAuthoredCorpus</c>; the contract function was still
/// perfectly tested, the whole suite stayed green, and
/// <c>--verify-authored-corpus &lt;missing&gt; --help</c> printed usage and exited 0 —
/// a gate reporting success having measured nothing, which is #3245 itself.</para>
///
/// <para>The source-text pin was worse than useless, being wrong in both directions at
/// once: a behavior-preserving comment inside the argument list <em>failed</em> it, while
/// a commented-out decoy call above the real one <em>satisfied</em> it with the live call
/// passing a single gate. A test that fails on a no-op edit and passes on a real
/// regression is not a weak gate, it is a misleading one, and it was deleted rather than
/// patched.</para>
///
/// <para>So these tests run the binary and read what it says. There is no seam left to
/// strand a rule behind: whatever <c>Program.cs</c> parses, forwards, orders, or decides
/// is inside the assertion. They check the <em>message</em> and not only the exit code,
/// because an earlier round shipped a regression that exited 1 for the wrong reason and
/// an exit-code-only check read it as correct.</para>
/// </summary>
[Trait("Area", "Corpus")]
public class AuthoredCorpusHarnessProcessTests
{
    /// <summary>
    /// Every mode that dispatches before a gate must refuse rather than run instead of
    /// it. One representative earlier mode per gate is enough here: which modes precede
    /// which is `AuthoredCorpusRatchetTests`' business, while this asserts the binary
    /// actually applies the rule to both gates.
    /// </summary>
    [Theory]
    [InlineData("--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus")]
    public void Harness_RefusesAGateThatAnEarlierModeWouldReplace(string gate)
    {
        var run = RunHarness(gate, "/does-not-exist.jsonl", "--history-card");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            $"--history-card runs instead of {gate}",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same, with the flags in the other order. Preemption is decided by dispatch
    /// order and not by the order the caller typed, so both spellings must refuse — and a
    /// reviewer's exploit used the mode-first spelling.
    /// </summary>
    [Theory]
    [InlineData("--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus")]
    public void Harness_RefusesRegardlessOfTheOrderTheFlagsWereTyped(string gate)
    {
        var run = RunHarness("--history-card", gate, "/does-not-exist.jsonl");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"runs instead of {gate}", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asking for usage while asking for a gate must refuse, not print usage and exit 0.
    ///
    /// <para>This is the exact combination whose rule was found stranded twice: once
    /// inline in <c>Program.cs</c>, and once as an argument the harness stopped
    /// forwarding. Both times the contract function was fully tested and both times the
    /// binary answered 0.</para>
    /// </summary>
    [Theory]
    [InlineData("--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus")]
    public void Harness_RefusesHelpWhenAGateWasAlsoRequested(string gate)
    {
        var run = RunHarness(gate, "/does-not-exist.jsonl", "--help");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("--help does not run a gate", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The modifiers refuse on their own, rather than being silently ignored by a run
    /// that then reports whatever it happened to do.
    /// </summary>
    [Theory]
    [InlineData(new[] { "--integrity-only" }, "--integrity-only applies to")]
    [InlineData(new[] { "--ratchet-baseline", "/does-not-exist.jsonl" }, "--ratchet-baseline applies to")]
    public void Harness_RefusesAModifierWithoutItsGate(string[] flags, string expected)
    {
        var run = RunHarness(flags);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(expected, run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Demanding a quality verdict and declining to judge quality are contradictory, and
    /// honouring either silently would make the exit code mean something the caller did
    /// not ask for.
    /// </summary>
    [Fact]
    public void Harness_RefusesTheContradictoryModifierPair()
    {
        var run = RunHarness(
            "--benchmark-authored-corpus", "/does-not-exist.jsonl",
            "--integrity-only",
            "--ratchet-baseline", "/does-not-exist.jsonl");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("are contradictory", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusals above must not fire on a gate nobody requested.
    ///
    /// <para>Round seven shipped exactly that: the preemption search ran unconditionally,
    /// so every mode in the harness refused for preempting a gate that had not been
    /// asked for. It exited 1 — the value an exit-code-only check expects from a refusal
    /// test — for entirely the wrong reason, and the scheduled lane could never have
    /// run.</para>
    /// </summary>
    [Fact]
    public void Harness_RunsAModeThatPreemptsNoRequestedGate()
    {
        var run = RunHarness("--history-card");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("runs instead of", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Usage on its own still prints usage and succeeds; the refusals above are about a
    /// gate being requested, not about <c>--help</c> being unwelcome.
    /// </summary>
    [Fact]
    public void Harness_PrintsUsageWhenNoGateWasRequested()
    {
        var run = RunHarness("--help");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("usage: decompiler-harness", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scheduled lane's own flag combination reaches the benchmark.
    ///
    /// <para>Named after the workflow rather than described abstractly, because the
    /// failure this guards against is a refusal that looks correct in isolation. The
    /// corpus path is deliberately absent: reaching "corpus file not found" proves the
    /// flags were accepted and dispatch got as far as the gate, which is the part that
    /// regressed. Pointing it at a real corpus would test the benchmark instead, which
    /// `AuthoredCorpusRatchetTests` already does in-process.</para>
    /// </summary>
    [Fact]
    public void Harness_LetsTheScheduledIntegrityLaneRun()
    {
        var run = RunHarness(
            "--benchmark-authored-corpus", "/does-not-exist.jsonl", "--integrity-only", "--json");

        Assert.DoesNotContain("runs instead of", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("does not run a gate", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("applies to", run.Output, StringComparison.Ordinal);
        Assert.Contains("Corpus file not found", run.Output, StringComparison.Ordinal);
    }

    sealed record HarnessRun(int ExitCode, string Output);

    static HarnessRun RunHarness(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // The harness resolves the committed run-history store relative to the
            // repository root, so run it from there rather than from the test host's
            // output directory.
            WorkingDirectory = AuthoredCorpusRatchetTests.FindRepositoryRoot(),
        };

        startInfo.ArgumentList.Add(HarnessBinary());
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;

        // Read both streams before waiting: the harness writes its refusals to stderr and
        // its reports to stdout, and a full pipe buffer on either would deadlock.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        Assert.True(
            process.WaitForExit(milliseconds: 120_000),
            "The harness did not exit within two minutes.");

        return new HarnessRun(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult());
    }

    /// <summary>
    /// The built harness under test.
    ///
    /// <para>A missing binary fails rather than skips. These tests are the only ones that
    /// can see <c>Program.cs</c>, so quietly skipping them would restore precisely the
    /// blind spot they exist to remove — and "skipped" reading as "fine" is the shape of
    /// defect this whole PR is about. The test project takes a
    /// <c>ReferenceOutputAssembly="false"</c> project reference on the harness so the
    /// binary is built, and rebuilt, whenever these tests are.</para>
    /// </summary>
    static string HarnessBinary()
    {
        string path = Path.Combine(
            AuthoredCorpusRatchetTests.FindRepositoryRoot(),
            "tools", "DecompilerHarness", "bin", "Release", "net11.0", "decompiler-harness.dll");

        Assert.True(
            File.Exists(path),
            $"The harness binary is missing: {path}. Build it with "
                + "`dotnet build tools/DecompilerHarness -c Release`.");

        return path;
    }
}
