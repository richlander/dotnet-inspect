using ILInspector.DecompilerHarness;
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

    /// <summary>
    /// Every mode that dispatches before a gate must refuse rather than run instead of
    /// it — checked against the binary, once per mode, for both protected gates.
    ///
    /// <para>This was one representative mode per gate until review round ten, on the
    /// reasoning that which modes precede which is <c>AuthoredCorpusRatchetTests</c>'
    /// business. That reasoning was wrong, and a reviewer showed why: the dispatch-order
    /// pin over there compares mode <em>names</em>, while the harness pairs each name
    /// with a <c>Selected</c> expression. Replacing one such expression with
    /// <c>false</c> left the whole suite green, and
    /// <c>--fixture-source-inventory --benchmark-authored-corpus &lt;corpus&gt;</c>
    /// printed an inventory and exited 0 — the requested gate silently discarded, which
    /// is #3245 exactly. One mode was covered here, so one mode was safe; the other
    /// sixteen were not.</para>
    ///
    /// <para>The corpus path is absent on purpose. A refusal must win over a missing
    /// file, so "runs instead of" rather than "corpus file not found" is what proves the
    /// preemption fired.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(PreemptionCases))]
    public void Harness_RefusesEveryPreemptingModeAgainstEveryProtectedGate(
        string gate,
        string mode,
        string[] invocation)
    {
        var run = RunHarness([gate, "/does-not-exist.jsonl", .. invocation]);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            $"{mode} runs instead of {gate}",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every mode that precedes a gate, paired with each gate it precedes.
    ///
    /// <para>Derived from the dispatch order rather than hand-listed, so a mode added to
    /// the harness without a case here fails
    /// <see cref="Preemption_CoversEveryModeThatPrecedesAGate"/> rather than silently
    /// going ungated.</para>
    /// </summary>
    public static TheoryData<string, string, string[]> PreemptionCases()
    {
        var cases = new TheoryData<string, string, string[]>();
        string[] order = AuthoredCorpusRatchetTests.DispatchOrderFlags;

        foreach (string gate in AuthoredCorpusExitContract.ProtectedGates)
        {
            int gateIndex = Array.IndexOf(order, gate);
            Assert.True(gateIndex >= 0, $"{gate} is not in the dispatch order.");

            foreach ((string mode, string[] invocation) in PreemptingModeInvocations)
            {
                // Only a mode that dispatches *earlier* preempts this gate. The two gates
                // are themselves in the order, so pairing them the other way round would
                // assert a refusal naming the wrong flag.
                if (Array.IndexOf(order, mode) < gateIndex)
                    cases.Add(gate, mode, invocation);
            }
        }

        return cases;
    }

    /// <summary>
    /// The theory above must exercise every mode the harness dispatches, not a subset
    /// that happens to be listed here.
    /// </summary>
    [Fact]
    public void Preemption_CoversEveryModeThatPrecedesAGate()
    {
        Assert.Equal(
            AuthoredCorpusRatchetTests.DispatchOrderFlags,
            PreemptingModeInvocations.Select(mode => mode.Mode).Distinct().ToArray());
    }

    /// <summary>
    /// How to invoke each dispatch mode, in dispatch order, paired with the flag name the
    /// refusal will report.
    ///
    /// <para>The two are separate because a mode is not always selected by the flag it is
    /// named after. Most are bare flags. The ones carrying a value must be spelled with
    /// it, or the flag would swallow the gate that follows it as its own argument and the
    /// test would pass having never requested a gate.</para>
    ///
    /// <para><c>--fidelity-check</c> is selected by a two-term disjunction, so it appears
    /// twice — and the second case must <em>omit</em> <c>--fidelity-check</c>, or the
    /// first term alone would satisfy it and the second would never be the reason the
    /// mode was selected. Review round eleven found exactly that: both cases passed the
    /// flag, so deleting the second term left the suite green. This list pairs the
    /// isolating invocation with the flag the harness reports for it, which is why the
    /// two columns exist at all.</para>
    ///
    /// <para>Round twelve found the same shape in five more modes. A mode is selected by
    /// every flag that sets its variable, whether the extra term sits in the selector
    /// expression (<c>--assertion-scan</c>, <c>--validity-check</c>) or in the parser
    /// (<c>--fuzz-signatures</c>, <c>--return-address</c>, <c>--not-my-type</c>), and an
    /// alternate flag with no case here is an alternate way to discard a gate that no
    /// test would notice. Every flag that selects a preempting mode gets an isolating
    /// case, which is why several modes appear more than once.</para>
    /// </summary>
    static readonly (string Mode, string[] Invocation)[] PreemptingModeInvocations =
    [
        ("--fixture-source-inventory", ["--fixture-source-inventory"]),
        ("--history-card", ["--history-card"]),
        ("--generated-fixtures", ["--generated-fixtures"]),
        ("--fuzz-signatures", ["--fuzz-signatures"]),
        ("--fuzz-signatures", ["--fuzz-unguarded"]),
        ("--return-to-sender-catalog", ["--return-to-sender-catalog"]),
        ("--emit-inverse-ledger", ["--emit-inverse-ledger", UnusedOutputPath]),
        ("--assertion-scan", ["--assertion-scan"]),
        ("--assertion-scan", ["--emit-assertion-violations", UnusedOutputPath]),
        ("--assertion-scan", ["--diff-assertion-violations", UnusedOutputPath]),
        ("--validity-check", ["--validity-check"]),
        ("--validity-check", ["--emit-validity-defects", UnusedOutputPath]),
        ("--validity-check", ["--diff-validity-defects", UnusedOutputPath]),
        ("--validity-predicate-scan", ["--validity-predicate-scan"]),
        ("--fidelity-check", ["--fidelity-check"]),
        ("--fidelity-check", ["--fidelity-method-delta", "SomeType.SomeMethod"]),
        ("--return-to-sender", ["--return-to-sender"]),
        ("--return-address", ["--return-address"]),
        ("--return-address", ["--emit-return-address-snapshot", UnusedOutputPath]),
        ("--return-address", ["--diff-return-address-baseline", UnusedOutputPath]),
        ("--not-my-type", ["--not-my-type"]),
        ("--not-my-type", ["--emit-not-my-type-snapshot", UnusedOutputPath]),
        ("--not-my-type", ["--diff-not-my-type-baseline", UnusedOutputPath]),
        ("--enumerate-real-methods", ["--enumerate-real-methods"]),
        ("--harvest-authored-corpus", ["--harvest-authored-corpus", UnusedOutputPath]),
        ("--harvest-evil-corpus", ["--harvest-evil-corpus", UnusedOutputPath]),
        ("--benchmark-authored-corpus", ["--benchmark-authored-corpus", "/does-not-exist.jsonl"]),
        ("--verify-authored-corpus", ["--verify-authored-corpus", "/does-not-exist.jsonl"]),
    ];

    /// <summary>
    /// A path for flags that demand an output file. Every case using it must refuse
    /// before dispatch, so nothing is ever written here.
    /// </summary>
    const string UnusedOutputPath = "/does-not-exist/unused-output.json";

    /// <summary>
    /// A requested <c>--ratchet-baseline</c> must reach the benchmark.
    ///
    /// <para>Dropping it at the call site left the suite green in review round ten, and
    /// the harness then ran the benchmark against no baseline at all while the caller
    /// believed it had ratcheted — a gate reporting success having compared nothing,
    /// which is #3245 restated.</para>
    ///
    /// <para>A missing baseline beside a real corpus file is what makes the forwarding
    /// observable: the benchmark reads the baseline before it reads the corpus, so
    /// "ratchet baseline not found" can only be reached if the path arrived. Were the
    /// argument dropped, the run would proceed to the corpus and complain about that
    /// instead.</para>
    /// </summary>
    [Fact]
    public void Harness_ForwardsTheRatchetBaselineToTheBenchmark()
    {
        string corpus = Path.Combine(Path.GetTempPath(), $"ratchet-forwarding-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(corpus, string.Empty);

        try
        {
            var run = RunHarness(
                "--benchmark-authored-corpus", corpus,
                "--ratchet-baseline", "/does-not-exist/baseline.jsonl");

            Assert.Equal(1, run.ExitCode);
            Assert.Contains(
                "Ratchet baseline not found: /does-not-exist/baseline.jsonl",
                run.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(corpus);
        }
    }

    /// <summary>
    /// A requested <c>--integrity-only</c> must reach the benchmark, and an unrequested
    /// one must not be invented.
    ///
    /// <para>This is the scheduled lane's whole contract. Dropping the argument at the
    /// call site left the suite green in review round ten; the lane would then have
    /// selected the historical <c>invalid == 0</c> contract, which its corpus cannot
    /// satisfy, and gone red every week for a reason no one could read off the exit
    /// code — the failure #3245 is about, reintroduced by the fix for it.</para>
    ///
    /// <para>Both runs are asserted because the flag only changes which contract is
    /// <em>reported</em>: the exit codes here are identical, so an exit-code check would
    /// see nothing. The corpus is a single real harvested row, and the claim does not
    /// depend on how that row decompiles — corrupting its token, method, type, or
    /// checksum still reaches the verdict and still names the contract applied, so
    /// ordinary drift in <c>ILInspector.CSharp</c> cannot rot this into a false green.
    /// This asserts the plumbing, not the quality.</para>
    ///
    /// <para>One drift does end the run early: renaming the assembly the row was
    /// harvested from, after which nothing in the pool matches it and the harness
    /// measures nothing. That is asserted separately rather than left to surface as an
    /// unexplained absence of <c>[integrity-only]</c>, because the negative assertion
    /// below would otherwise be satisfied by a run that never got started — this PR's
    /// characteristic defect, in its own test.</para>
    /// </summary>
    [Fact]
    public void Harness_ForwardsIntegrityOnlyToTheBenchmark()
    {
        string corpus = Path.Combine(
            AuthoredCorpusRatchetTests.FindRepositoryRoot(),
            "tools", "DecompilerHarness", "corpus", "one-row-authored-corpus.jsonl");
        string assembly = typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location;

        var requested = RunHarness(assembly, "--benchmark-authored-corpus", corpus, "--integrity-only");
        AssertTheBenchmarkActuallyRan(requested);
        Assert.Contains("[integrity-only]", requested.Output, StringComparison.Ordinal);

        var notRequested = RunHarness(assembly, "--benchmark-authored-corpus", corpus);
        AssertTheBenchmarkActuallyRan(notRequested);
        Assert.DoesNotContain("[integrity-only]", notRequested.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The run reached a verdict, so an assertion about which contract it reported is
    /// about the contract and not about the run having died first.
    ///
    /// <para>The exit code is checked because this is the only place a protected gate
    /// runs to completion, which makes it the only test that can catch
    /// <see cref="AuthoredCorpusExitContract.GateExitedWithoutRunning"/> firing on a
    /// healthy run. That check reads a flag the gate sets at its own dispatch site;
    /// deleting the flag turns every real gate run into a failure, and until this
    /// assertion existed the suite stayed green while it did. A guard against a silent
    /// success is worth little if it can start rejecting the successes that are real.</para>
    /// </summary>
    static void AssertTheBenchmarkActuallyRan(HarnessRun run)
    {
        Assert.DoesNotContain("measured nothing", run.Output, StringComparison.Ordinal);
        Assert.Contains("targets evaluated", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "A protected gate was requested but never ran",
            run.Output,
            StringComparison.Ordinal);
        Assert.Equal(0, run.ExitCode);
    }

    /// <summary>
    /// The verify gate must dispatch to the drift check.
    ///
    /// <para>Inverting its dispatch condition left the suite green in review round ten:
    /// the flag was accepted, every refusal rule applied to it correctly, and then
    /// nothing ran it. Reaching the drift check's own complaint about an unparseable
    /// corpus is what proves it was entered.</para>
    /// </summary>
    [Fact]
    public void Harness_DispatchesTheStandaloneVerifyGate()
    {
        string corpus = Path.Combine(Path.GetTempPath(), $"verify-dispatch-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(corpus, string.Empty);

        try
        {
            var run = RunHarness("--verify-authored-corpus", corpus);

            Assert.Equal(1, run.ExitCode);
            Assert.Contains(
                $"Corpus is empty or unparseable: {corpus}",
                run.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(corpus);
        }
    }

    /// <summary>
    /// A mode that returns before a requested gate makes the harness fail rather than
    /// report a success it did not measure.
    ///
    /// <para>This is the round-twelve finding, and it is the only test that reaches the
    /// check. Every list-based defense in this area — the declared modes, the dispatch
    /// order, the refusal — was defeated at least once by something absent from the list
    /// doing the preempting, most recently by a mode hand-edited into the harness's
    /// <c>if</c>-cascade and left out of both lists. The check under test names nothing
    /// and asks only whether the requested gate ran, so this test injects the defect
    /// rather than one particular route to it.</para>
    ///
    /// <para>The injection is real harness code
    /// (<see cref="AuthoredCorpusExitContract.SimulatePreemptionVariable"/>) because no ordinary invocation
    /// reaches the check: it is live only when the harness is broken. Without this test
    /// the wiring in <c>Main</c> could be deleted and nothing would go red.</para>
    /// </summary>
    [Theory]
    [Trait("Area", "Corpus")]
    [InlineData("--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus")]
    public void Harness_FailsWhenARequestedGateNeverRan(string gate)
    {
        var run = RunHarness(
            (AuthoredCorpusExitContract.SimulatePreemptionVariable, "1"),
            gate,
            "/does-not-exist.jsonl");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            "A protected gate was requested but never ran",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same preempting mode is not an error when no gate was requested.
    ///
    /// <para>Without this, the test above would pass just as well against a harness that
    /// failed on the environment variable alone, and it would be asserting the injection
    /// rather than the property. A mode returning 0 is the harness working normally; only
    /// a mode returning 0 <em>while a gate was asked for</em> is the defect.</para>
    /// </summary>
    [Fact]
    [Trait("Area", "Corpus")]
    public void Harness_SucceedsWhenAModeReturnsFirstAndNoGateWasRequested()
    {
        var run = RunHarness((AuthoredCorpusExitContract.SimulatePreemptionVariable, "1"), "--history-card");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain(
            "A protected gate was requested but never ran",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every way the benchmark can decline to measure exits non-zero.
    ///
    /// <para>This is the gate for an assumption
    /// <see cref="AuthoredCorpusExitContract.GateExitedWithoutRunning"/> depends on and
    /// cannot check for itself. That check reads a flag set at the gate's dispatch site,
    /// so it learns that the gate was <em>entered</em>, not that it measured anything. The
    /// distinction is harmless only while every path that enters the benchmark and
    /// measures nothing exits non-zero, because the check deliberately ignores non-zero
    /// exits. A future early <c>return 0</c> would be a gate reporting success without
    /// measuring — #3245 again — and nothing else here would notice.</para>
    ///
    /// <para>Each case names the message that identifies the path, so a run that failed
    /// somewhere else cannot be mistaken for the path under test.</para>
    /// </summary>
    [Fact]
    [Trait("Area", "Corpus")]
    public void Benchmark_ExitsNonZeroOnEveryPathThatMeasuresNothing()
    {
        string empty = Path.Combine(Path.GetTempPath(), $"empty-corpus-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(empty, string.Empty);
        string realCorpus = Path.Combine(
            AuthoredCorpusRatchetTests.FindRepositoryRoot(),
            "tools", "DecompilerHarness", "corpus", "one-row-authored-corpus.jsonl");

        try
        {
            (string Message, string[] Invocation)[] cases =
            [
                ("Corpus file not found",
                    ["--benchmark-authored-corpus", "/does-not-exist.jsonl"]),
                ("Ratchet baseline not found",
                    ["--benchmark-authored-corpus", empty, "--ratchet-baseline", "/does-not-exist.json"]),
                ("Corpus is empty or unparseable",
                    ["--benchmark-authored-corpus", empty]),
                ("the run measured nothing",
                    [typeof(Xunit.FactAttribute).Assembly.Location, "--benchmark-authored-corpus", realCorpus]),
            ];

            foreach ((string message, string[] invocation) in cases)
            {
                var run = RunHarness(invocation);

                Assert.Contains(message, run.Output, StringComparison.Ordinal);
                Assert.NotEqual(0, run.ExitCode);
            }
        }
        finally
        {
            File.Delete(empty);
        }
    }

    sealed record HarnessRun(int ExitCode, string Output);

    static HarnessRun RunHarness(params string[] arguments)
        => RunHarness(environment: null, arguments);

    static HarnessRun RunHarness(
        (string Name, string Value)? environment,
        params string[] arguments)
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

        if (environment is { } variable)
            startInfo.Environment[variable.Name] = variable.Value;

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

        string output = standardOutput.GetAwaiter().GetResult()
            + standardError.GetAwaiter().GetResult();

        // A crash otherwise surfaces as an exit code nobody expected, and the reader has
        // to go find the process output to learn why. Every assertion below this point is
        // about what the harness decided, so a harness that never decided anything should
        // say so in its own words.
        if (output.Contains("Unhandled exception", StringComparison.Ordinal))
            Assert.Fail($"The harness crashed instead of deciding:{Environment.NewLine}{output}");

        return new HarnessRun(process.ExitCode, output);
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
