using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Fixtures =
    ILInspector.Metadata.StateMachineFixtures.StateMachineFixtures;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Structural completeness cross-check for
/// <see cref="StateMachineRelationshipIndex"/>.
///
/// Every other test of the index starts where the index starts: from a
/// compiler-emitted state-machine attribute. That can only ever show the index
/// agrees with its own premise. It cannot show whether a state machine exists
/// that the attribute path never reaches.
///
/// This file supplies the independent signal. A compiler async state machine is
/// a TypeDef implementing <c>IAsyncStateMachine</c>, and the index never
/// consults that interface to <em>discover</em> a machine, so the two signals
/// are genuinely independent and a disagreement is real evidence rather than a
/// restatement.
///
/// Classification is exact rather than heuristic:
/// <see cref="StateMachineRelationshipIndex.GetByStateMachine"/> answers
/// <c>Resolved</c> (authenticated), <c>Rejected</c> (a claim exists but failed
/// authentication), or <c>Absent</c> (nothing claimed this type). Only
/// <c>Rejected</c> indicates the index declined evidence it was actually
/// offered; <c>Absent</c> is the ordinary shape for a reference assembly whose
/// kickoff was stripped, or for a non-Roslyn compiler such as F#, which uses
/// resumable code and emits no state-machine attribute.
///
/// That reading of <c>Rejected</c> holds per type, not per assembly. If the
/// index fails to build at all, every structural TypeDef comes back
/// <c>Rejected</c> whether or not it ever carried a claim, so a whole-assembly
/// rejection says the index failed rather than that this many claims were
/// refused.
///
/// The report does not separate those two. It keeps <c>Failure.Kind</c> only,
/// and the kinds overlap — <c>Malformed</c> and <c>BudgetExceeded</c> arise
/// both ways — so "refused authentication" in a sweep message means "the index
/// did not authenticate this type", not necessarily that a claim was offered
/// and declined. Round 15 caught this paragraph asserting a distinction nothing
/// makes. Read a whole-assembly rejection as the index having failed, and the
/// shape of the failure as the thing to go look at; #4833 tracks the failure
/// contract that would let the report say which it was.
/// </summary>
public sealed class StateMachineCompletenessTests
{
    /// <summary>
    /// Names the environment variable that supplies a corpus directory for
    /// <see cref="Corpus_NoStructuralStateMachineIsClaimedThenRejected"/>. The
    /// sweep is opt-in so ordinary CI runs pay nothing for it.
    /// </summary>
    const string CorpusVariable = "SM_COMPLETENESS_CORPUS";

    /// <summary>
    /// The detail reported when SRM finds no metadata behind a CLI directory
    /// the oracle positively claimed.
    /// </summary>
    const string CliDirectoryPresentDetail =
        "the CLI directory is present but carries no metadata";

    /// <summary>
    /// The detail reported when SRM finds no metadata, worded to match what the
    /// oracle actually established rather than assuming the CLI directory is
    /// present. Round 9 caught this message asserting presence on the
    /// indeterminate path, which sends a maintainer looking for a CLI directory
    /// nothing claimed to exist.
    /// </summary>
    static string NoMetadataDetail(ManagedClaim claim, ClaimExitSite exit) =>
        claim switch
        {
            ManagedClaim.Yes => CliDirectoryPresentDetail,
            ManagedClaim.No =>
                "the CLI directory is absent and SRM reports no metadata",
            _ => $"the header oracle could not decide whether a CLI directory "
                + $"is present ({exit}), and SRM reports no metadata",
        };

    /// <summary>
    /// The gate for the completeness property on assemblies this repository
    /// builds. Both specimens are compiler-async, are produced by the build
    /// under test, and are therefore deterministic — no corpus, no network, and
    /// no environment dependence.
    ///
    /// This test is non-vacuous by construction: it asserts that the structural
    /// scan actually found state machines before asserting anything about how
    /// they classified, so deleting the fixtures fails the test rather than
    /// silently passing it.
    /// </summary>
    [Theory]
    [InlineData(typeof(Fixtures))]
    [InlineData(typeof(StateMachineCompletenessTests))]
    public void OwnBuildOutputs_EveryStructuralAsyncStateMachineIsAuthenticated(
        Type specimen)
    {
        CompletenessReport report = Measure(specimen.Assembly.Location);

        Assert.NotEqual(0, report.Structural);
        Assert.Equal(0, report.Rejected);
        Assert.Equal(0, report.Absent);
        Assert.Equal(report.Structural, report.Resolved);
    }

    /// <summary>
    /// The same property, held over every file in this test's own output
    /// directory rather than two hand-picked assemblies.
    ///
    /// Round 11 is why this exists. The two specimens above are the only place
    /// <c>Absent</c> is asserted at all: the corpus sweep records it and
    /// deliberately does not fail on it, because reference assemblies and F#
    /// output make it legitimate there. A reviewer mutated
    /// StateMachineRelationshipIndex to answer Absent above 1,000 type rows and
    /// found it survived everything -- the corpus gate tolerates Absent by
    /// design, and both specimens are small enough that the mutation never
    /// fired. The Absent direction was being held by two assemblies that happen
    /// to sit at one end of the size range.
    ///
    /// The set here is derived from the build rather than listed, so it widens
    /// when dependencies do. Today it is 35 assemblies carrying 464 structural
    /// machines, two of which are the specimens themselves: this directory is
    /// the test's own output, so ILInspector.Metadata.Tests and the fixtures
    /// assembly sit in it and contribute 20 of those machines.
    ///
    /// Keeping it from quietly shrinking took three connected checks, and round
    /// 12 found each one individually satisfiable. The structural count must
    /// include machines found outside the two specimens, or emptying every
    /// other report still passes. The width must be measured only over
    /// neighbours that carry machines and are not specimens, or an unrelated
    /// wide assembly with nothing in it answers on their behalf. And a skip
    /// must be cross-checked against SRM, or routing all but one neighbour to
    /// "not managed" leaves 463 of 464 machines unexamined under assertions
    /// that only need one to survive.
    ///
    /// What the width assertion pins is image size, and only that. It is not a
    /// coverage statement: the widest neighbour carries 1 machine, while
    /// Microsoft.Testing.Platform, below the round-11 threshold at 868 rows,
    /// carries 193 of the 464.
    ///
    /// Its margin is worth stating plainly, because an earlier revision of this
    /// comment called two neighbours "an order of magnitude larger" than the
    /// specimens without measuring either. They are not. The larger specimen has
    /// 761 type rows; the widest neighbours are Microsoft.CodeAnalysis.CSharp at
    /// 2,829 and Microsoft.CodeAnalysis at 2,381, and the next one down is 887 --
    /// within 17% of the specimen. So only two assemblies, both Roslyn, carry
    /// this gate past the 1,000-row threshold the round-11 mutation used.
    ///
    /// That is the residual: if those dependencies went away, the reach
    /// assertion would still pass on a neighbour a few rows wider than the
    /// specimen while the size sensitivity that motivated this test quietly
    /// left. The assertion pins direction, not margin, and no principled
    /// threshold is available to pin margin -- the mutation's 1,000 was
    /// arbitrary, and a constant here would only encode one reviewer's guess.
    /// Recorded rather than papered over.
    ///
    /// A neighbour reporting Absent is not automatically a product defect -- a
    /// reference assembly retains the nested machine while stripping the
    /// kickoff, and F# emits no attribute. If one appears here, the answer is to
    /// exclude that neighbour and say why, not to weaken the assertion.
    /// </summary>
    [Fact]
    public void Neighbours_EveryStructuralAsyncStateMachineIsAuthenticated()
    {
        string directory = Path.GetDirectoryName(
            typeof(StateMachineCompletenessTests).Assembly.Location)!;

        var offenders = new List<string>();
        int examined = 0;
        int structural = 0;
        int beyondSpecimens = 0;
        int notManaged = 0;
        int widest = 0;

        foreach (string path in
            Directory.EnumerateFiles(directory).OrderBy(p => p))
        {
            switch (TryMeasure(path, out CompletenessReport report, out string? detail))
            {
                case CorpusOutcome.Measured:
                    break;

                // A native dependency is legitimate here and carries no claim,
                // but it is counted and cross-checked rather than dropped.
                // Round 12 found this branch swallowing the outcome outright,
                // which let a mutation route every neighbour but one here and
                // still pass: 463 of 464 machines went unexamined under an
                // assertion set that only required one to survive. SRM is the
                // independent answer to "was that really not an assembly", the
                // same oracle this file cross-checks everywhere else, so a
                // disagreement is reported instead of trusted.
                case CorpusOutcome.NotManaged:
                    notManaged++;
                    if (HasMetadataAccordingToSrm(path))
                    {
                        offenders.Add(
                            $"{Path.GetFileName(path)}: skipped as not managed, "
                                + "but SRM reads metadata in it");
                    }

                    continue;

                default:
                    offenders.Add($"{Path.GetFileName(path)}: {detail}");
                    continue;
            }

            examined++;
            structural += report.Structural;

            // Width is measured only over neighbours that actually carry
            // machines, and only over ones that are not the specimens. Round 12
            // showed the two assertions were independent: an unrelated wide
            // assembly with no state machines satisfied the width assertion
            // while every real neighbour report was emptied, so the gate could
            // shrink back to the two specimens and still pass.
            if (report.Structural != 0 && !IsSpecimen(path))
            {
                beyondSpecimens += report.Structural;
                widest = Math.Max(widest, TypeRowCount(path));
            }

            if (report.Absent != 0 || report.Rejected != 0)
            {
                offenders.Add(
                    $"{Path.GetFileName(path)}: {report.Structural} structural, "
                        + $"{report.Resolved} resolved, {report.Absent} absent, "
                        + $"{report.Rejected} rejected");
            }
        }

        int specimen = Math.Max(
            TypeRowCount(typeof(Fixtures).Assembly.Location),
            TypeRowCount(typeof(StateMachineCompletenessTests).Assembly.Location));

        Assert.NotEqual(0, examined);
        Assert.True(
            structural != 0,
            $"{examined} assemblies were measured and none carried a structural "
                + "state machine, so this gate proves nothing.");
        Assert.True(
            beyondSpecimens != 0,
            $"{examined} assemblies were measured and the only structural state "
                + "machines found were in the two specimens, so this gate adds "
                + "nothing to OwnBuildOutputs_EveryStructuralAsyncStateMachineIsAuthenticated.");
        Assert.True(
            widest > specimen,
            $"the widest machine-bearing neighbour has {widest} type rows and the "
                + $"larger specimen has {specimen}, so this gate no longer reaches "
                + "past the two hand-picked assemblies that missed the round-11 "
                + "mutation.");
        Assert.True(
            offenders.Count == 0,
            $"""
            {offenders.Count} of {examined} measured assemblies in this test's own
            output directory did not authenticate every structural state machine
            they carry ({notManaged} further file(s) were not managed assemblies).

            {Truncated(offenders)}
            """);
    }

    /// <summary>
    /// Type rows in an image, used to show this gate reaches past the two
    /// hand-picked specimens rather than asserting a bare number.
    /// </summary>
    /// <summary>
    /// The two assemblies OwnBuildOutputs already covers, excluded from the
    /// neighbour gate's own non-vacuity counts so that gate cannot pass on
    /// evidence the other one already supplies.
    /// </summary>
    static bool IsSpecimen(string path) =>
        string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(typeof(Fixtures).Assembly.Location),
            StringComparison.Ordinal)
        || string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(
                typeof(StateMachineCompletenessTests).Assembly.Location),
            StringComparison.Ordinal);

    /// <summary>
    /// The same property on the core library, which is the only assembly in
    /// reach that declares the state-machine attributes in the module that uses
    /// them.
    ///
    /// Round 12 is why this exists. Everywhere else, an async method's
    /// attribute constructor is a MemberReference into another assembly, so the
    /// index's MethodDefinition branch -- the same-module case -- was never
    /// exercised end to end by any gate. A reviewer made that branch
    /// unreachable and System.Private.CoreLib went from 71 resolved to 71
    /// absent while the entire suite stayed green, including the runtime
    /// corpus: the corpus sweep tolerates Absent by design, and the neighbour
    /// gate only sees assemblies copied beside the test, all of which reference
    /// their attributes.
    ///
    /// So this is not a widening for its own sake. It covers a distinct
    /// metadata encoding that nothing else here could reach, and it is the
    /// third and last place the Absent direction is held.
    /// </summary>
    [Fact]
    public void CoreLibrary_EveryStructuralAsyncStateMachineIsAuthenticated()
    {
        string path = typeof(object).Assembly.Location;

        Assert.True(
            TryMeasure(path, out CompletenessReport report, out string? detail)
                == CorpusOutcome.Measured,
            $"the core library at {path} could not be measured: {detail}");

        Assert.True(
            report.Structural != 0,
            "the core library carried no structural state machine, so this gate "
                + "proves nothing about the same-module constructor path.");
        Assert.Equal(0, report.Absent);
        Assert.Equal(0, report.Rejected);
        Assert.Equal(report.Structural, report.Resolved);
    }

    /// <summary>
    /// SRM's own answer to whether a file carries metadata, used to cross-check
    /// the hand-rolled claim reader's decision to skip a neighbour. Independent
    /// of that reader by construction: it shares no code with it.
    /// </summary>
    static bool HasMetadataAccordingToSrm(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.HasMetadata;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or IOException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static int TypeRowCount(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return reader.GetMetadataReader().TypeDefinitions.Count;
    }

    /// <summary>
    /// The property that held across every real assembly measured to date: a
    /// state machine is never claimed by an attribute and then refused. Sweeping
    /// 23,829 assemblies (NuGet cache plus shared framework) produced 145,413
    /// claims and 145,413 authenticated relationships.
    ///
    /// That sweep contained no trimmed output, and the qualifier matters:
    /// trimming is a known cause of refusal, because ILLink removes
    /// SetStateMachine, which both ClassicAsync and AsyncIterator require. So a
    /// Rejected here is a genuine finding for an untrimmed corpus, and expected
    /// noise for a trimmed one — the failure message below says which, because
    /// an earlier revision of this comment claimed the property held over every
    /// real assembly while the message underneath it named the exception.
    ///
    /// <c>Absent</c> is reported but deliberately not asserted: reference
    /// assemblies retain the nested machine type while stripping the private
    /// kickoff, and F# emits no state-machine attribute at all. Both are
    /// legitimate, so asserting on them would make this test track the shape of
    /// whatever directory it was pointed at. That tolerance is exactly why the
    /// Absent direction is held elsewhere, over a build-derived set, by
    /// Neighbours_EveryStructuralAsyncStateMachineIsAuthenticated: a product
    /// that simply stopped making claims would pass this test.
    /// </summary>
    [Fact]
    public void Corpus_NoStructuralStateMachineIsClaimedThenRejected()
    {
        string? root = Environment.GetEnvironmentVariable(CorpusVariable);
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(root),
            $"Set {CorpusVariable} to a directory to run the corpus sweep.");

        // Once the variable is supplied the sweep is opted in, so a path that
        // does not resolve is a configuration error rather than a reason to
        // skip. Skipping here would turn a typo into a green corpus gate.
        Assert.True(
            Directory.Exists(root),
            $"{CorpusVariable} is set to '{root}', which is not a directory. "
                + "Unset it to skip the corpus sweep.");

        string? problems = SweepProblems(root!, out string surveyed);
        Assert.True(problems is null, $"{surveyed}\n\n{problems}");
    }

    /// <summary>
    /// Runs the sweep and returns its assembled problem text, or
    /// <see langword="null"/> when the corpus is clean.
    ///
    /// Split out of the test so a synthetic corpus can drive the same code. The
    /// sweep above is opt-in and skipped by default, so nothing in CI executed
    /// it at all; the sweep tests below are the only automated evidence that it
    /// fails when it should, and they exercise this method rather than
    /// re-deriving its decisions.
    /// </summary>
    static string? SweepProblems(string root, out string surveyed)
    {
        var totals = new CompletenessReport();
        var offenders = new List<string>();
        var undecodable = new List<string>();
        var unclassifiable = new List<string>();
        var inaccessible = new List<string>();
        int assemblies = 0;
        int notManaged = 0;

        foreach (string path in EnumerateCandidates(root, inaccessible))
        {
            switch (TryMeasure(path, out CompletenessReport report, out string? detail))
            {
                case CorpusOutcome.Measured:
                    break;

                // Not a managed assembly at all: a native DLL, or a file that is
                // not a PE. Counted so an empty sweep cannot masquerade as a
                // clean one, but not a failure — it carries no claim to check.
                case CorpusOutcome.NotManaged:
                    notManaged++;
                    continue;

                // Environmental, not evidence about the index. Reported rather
                // than dropped so a systematically unreadable corpus is visible.
                case CorpusOutcome.Inaccessible:
                    inaccessible.Add($"{Path.GetFileName(path)}: {detail}");
                    continue;

                // A managed assembly whose metadata would not decode. This is a
                // real failure: a candidate existed and could not be evaluated.
                case CorpusOutcome.DecodeFailed:
                    undecodable.Add($"{Path.GetFileName(path)}: {detail}");
                    continue;

                // A PE that could not be classified at all. Kept separate from
                // the decode failures above because it makes a weaker claim --
                // this file might never have been managed -- and separate from
                // NotManaged because nothing established that it wasn't.
                default:
                    unclassifiable.Add($"{Path.GetFileName(path)}: {detail}");
                    continue;
            }

            assemblies++;
            totals.Add(report);

            if (report.Rejected != 0)
            {
                offenders.Add(
                    $"{Path.GetFileName(path)}: {report.Rejected} rejected "
                        + $"[{string.Join(", ", report.RejectionKinds)}]");
            }
        }

        surveyed =
            $"{assemblies} managed assemblies measured, {notManaged} "
                + $"non-managed skipped, {unclassifiable.Count} unclassifiable, "
                + $"{inaccessible.Count} unreadable.";

        // Every problem is collected before anything is asserted. Asserting them
        // one at a time lets the first failure hide the rest, so a corpus with
        // one unreadable directory and four hundred rejections would report only
        // the directory.
        var problems = new List<string>();

        if (inaccessible.Count != 0)
        {
            problems.Add(
                $"""
                {inaccessible.Count} corpus entr{(inaccessible.Count == 1 ? "y" : "ies")} could not be read, so the
                sweep did not cover the whole corpus and cannot claim the property
                over it. An unreadable directory is a hole in a completeness gate,
                not a detail to skip past: the assemblies it hides are exactly the
                ones nothing was proven about.

                {Truncated(inaccessible)}
                """);
        }

        if (undecodable.Count != 0)
        {
            problems.Add(
                $"""
                {undecodable.Count} managed assembl{(undecodable.Count == 1 ? "y" : "ies")} failed to decode, so
                {(undecodable.Count == 1 ? "its" : "their")} state machines could not be evaluated at all. The file
                carried a CLI header, so it claims to be managed; failing to read
                it is a decode failure rather than a reason to skip it.

                {Truncated(undecodable)}
                """);
        }

        if (unclassifiable.Count != 0)
        {
            problems.Add(
                $"""
                {unclassifiable.Count} PE file{(unclassifiable.Count == 1 ? "" : "s")} could not be classified, so
                the sweep cannot say whether {(unclassifiable.Count == 1 ? "it was" : "they were")} managed. Damaged
                or truncated headers stop the question being decidable before SRM
                is even reached. Reporting {(unclassifiable.Count == 1 ? "it" : "them")} as non-managed would assert
                something nothing measured, which is how a completeness gate goes
                green over files it never covered.

                {Truncated(unclassifiable)}
                """);
        }

        if (assemblies == 0)
        {
            problems.Add(
                "No managed assembly was measured, so this sweep proves nothing. "
                    + "If decode failures are also reported above, they are why.");
        }
        else if (totals.Structural == 0)
        {
            problems.Add(
                "No structural state machines were found, so this sweep proves "
                    + "nothing about the property it claims to gate.");
        }

        if (totals.Rejected != 0)
        {
            problems.Add(
                $"""
                {totals.Rejected} structural state machine(s) across
                {offenders.Count} assembl{(offenders.Count == 1 ? "y" : "ies")} were refused authentication.
                The sweep measured {assemblies} assemblies in total
                ({totals.Structural} structural, {totals.Resolved} resolved,
                {totals.Absent} absent).

                A refusal has two possible causes, distinguished by the failure kinds
                listed below. Either an attribute claimed the type and the claim
                failed its role requirements, or the module failed to index at all,
                in which case every structural machine in it reports Rejected
                regardless of whether anything claimed it (see #4833).

                A known cause of the first is trimming: ILLink removes
                SetStateMachine, which both ClassicAsync and AsyncIterator require,
                so every async claim in a trimmed assembly is refused (see #4827).
                If this corpus contains trimmed output, that is expected rather than
                a regression.

                {Truncated(offenders)}
                """);
        }

        return problems.Count == 0 ? null : string.Join("\n\n", problems);
    }

    /// <summary>How a specimen is damaged before the sweep sees it.</summary>
    public enum DamageKind
    {
        /// <summary>
        /// The metadata signature is destroyed, so the CLI header still claims
        /// the file is managed but nothing behind it will decode.
        /// </summary>
        MetadataSignature,

        /// <summary>
        /// The image stops after its headers, so the claim survives and the
        /// body it points at does not.
        /// </summary>
        Truncated,

        /// <summary>
        /// The image stops before the headers can be read at all, so whether it
        /// was ever managed is undecidable. Round 13 found this direction
        /// untested: routing <c>Unclassifiable</c> to <c>NotManaged</c> made an
        /// undecidable file a silent skip and the whole suite stayed green.
        /// </summary>
        HeaderTruncated,
    }

    /// <summary>
    /// The sweep must fail when a file it should have measured cannot be
    /// measured, instead of dropping it from the population and reporting the
    /// remainder as clean. That is the whole promise of the outcome accounting
    /// in <see cref="CorpusOutcome"/>, and nothing in CI checked it: the sweep
    /// is opt-in and skipped by default, so it never ran.
    ///
    /// Twenty-one unit tests used to pin the branches underneath this instead.
    /// They were admitted round by round on mutations that survived, which
    /// AGENTS.md excludes as an admission rule, and they hardened the header
    /// reader against corruption no corpus of build output or shared framework
    /// will contain. This drives the real entry point instead.
    ///
    /// The two truncation specimens differ in how much header they keep.
    /// <c>Truncated</c> retains the PE headers, so it still claims to be
    /// managed and fails later, at the metadata it points at. One byte of
    /// <c>HeaderTruncated</c> cannot answer the question either way, so it is
    /// undecidable rather than a claim. Which problem category the sweep files
    /// each under is deliberately not asserted: that is a branch detail, and
    /// pinning it is how the previous apparatus grew. The property is that the
    /// sweep does not come
    /// back clean and names the file.
    ///
    /// An undamaged assembly sits alongside the damaged one on purpose. Without
    /// it a corpus of one file fails for an unrelated reason — nothing was
    /// measured at all — and that incidental failure would mask a laundered
    /// skip. With it, the sweep has something to report success about, so the
    /// only thing keeping it red is the damaged file being accounted for.
    /// </summary>
    [Theory]
    [InlineData(DamageKind.MetadataSignature)]
    [InlineData(DamageKind.Truncated)]
    [InlineData(DamageKind.HeaderTruncated)]
    public void Sweep_DamagedAssembly_IsNeverSilentlyDropped(DamageKind damage)
    {
        string corpus = NewCorpusDirectory();
        try
        {
            string specimen = typeof(Fixtures).Assembly.Location;
            File.Copy(specimen, Path.Combine(corpus, "good.dll"));
            File.WriteAllBytes(
                Path.Combine(corpus, "damaged.dll"),
                Damage(File.ReadAllBytes(specimen), damage));

            string? problems = SweepProblems(corpus, out string surveyed);

            Assert.False(
                problems is null,
                $"A {damage} assembly left the population without failing the "
                    + $"sweep, which reported the rest as clean. {surveyed}");
            Assert.Contains("damaged.dll", problems);
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
        }
    }

    /// <summary>
    /// The negative half of the test above, and the reason that one is evidence
    /// rather than a sweep that fails on everything. A file that genuinely is
    /// not a managed assembly is skipped without failing the sweep — but it is
    /// counted and reported, because an uncounted skip is the silent shrink the
    /// accounting exists to prevent.
    /// </summary>
    [Fact]
    public void Sweep_NonManagedFile_IsSkippedButCounted()
    {
        string corpus = NewCorpusDirectory();
        try
        {
            File.Copy(
                typeof(Fixtures).Assembly.Location,
                Path.Combine(corpus, "good.dll"));
            File.WriteAllText(
                Path.Combine(corpus, "notes.txt"), "not a portable executable");

            string? problems = SweepProblems(corpus, out string surveyed);

            Assert.True(problems is null, $"{surveyed}\n\n{problems}");
            Assert.Contains("1 non-managed skipped", surveyed);
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
        }
    }

    /// <summary>
    /// The sweep's own named property: a state machine is never claimed and then
    /// refused. Round 13 found nothing held it. Deleting the rejection report
    /// entirely — <c>totals.Rejected != 0</c> mutated to <c>&lt; 0</c> — left the
    /// full suite green, so the sweep no longer gated the thing it is named
    /// after.
    ///
    /// The specimen renames <c>SetStateMachine</c> in the metadata string heap,
    /// one byte, same length, so every heap offset still lands where it did. The
    /// claim survives and the role lookup finds nothing, which is the shape
    /// trimming produces (#4827: ILLink removes SetStateMachine, which both
    /// ClassicAsync and AsyncIterator require) without needing a trimmer in CI.
    /// Measured on the fixture assembly: 9 structural, 9 resolved becomes 9
    /// structural, 9 rejected.
    ///
    /// This is the one damage that leaves a decodable assembly, so it is the
    /// only one that exercises the rejection path rather than an outcome branch.
    /// </summary>
    [Fact]
    public void Sweep_RejectedStateMachine_FailsTheSweep()
    {
        string corpus = NewCorpusDirectory();
        try
        {
            byte[] image = File.ReadAllBytes(typeof(Fixtures).Assembly.Location);
            File.WriteAllBytes(
                Path.Combine(corpus, "unauthenticatable.dll"),
                Unauthenticatable(image));

            string? problems = SweepProblems(corpus, out string surveyed);

            Assert.False(
                problems is null,
                "A claimed state machine was refused authentication and the "
                    + $"sweep still reported the corpus clean. {surveyed}");
            Assert.Contains("refused authentication", problems);
            Assert.Contains("unauthenticatable.dll", problems);
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
        }
    }

    /// <summary>
    /// A damaged assembly is found whatever it is named.
    ///
    /// Round 10 removed an extension filter from the sweep and round 12 removed
    /// one that had grown back in the neighbour gate; round 13 found that
    /// nothing stopped a third from appearing, because every synthetic specimen
    /// was called <c>.dll</c>. Restricting enumeration to <c>.exe</c> left the
    /// whole suite green. Round 14 found the first fix half-done: a specimen
    /// named <c>broken.bin</c> gates a filter on the extension's spelling but
    /// not one on its presence, and <c>!Path.HasExtension(entry)</c> still swept
    /// green. Managed code ships as <c>.exe</c>, as <c>.bin</c> inside packed
    /// layouts, and on Linux as a file with no extension at all, so both names
    /// are specimens here.
    /// </summary>
    [Theory]
    [InlineData("broken.bin")]
    [InlineData("broken")]
    public void Sweep_DamagedAssembly_IsFoundWhateverItIsNamed(string name)
    {
        string corpus = NewCorpusDirectory();
        try
        {
            string specimen = typeof(Fixtures).Assembly.Location;
            File.Copy(specimen, Path.Combine(corpus, "good.dll"));
            File.WriteAllBytes(
                Path.Combine(corpus, name),
                Damage(File.ReadAllBytes(specimen), DamageKind.Truncated));

            string? problems = SweepProblems(corpus, out string surveyed);

            Assert.False(
                problems is null,
                $"A damaged assembly named {name} was never enumerated, so the "
                    + $"sweep reported the corpus clean. {surveyed}");
            Assert.Contains(name, problems);
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
        }
    }

    /// <summary>
    /// Enumeration terminates on a directory that contains itself, and still
    /// reaches files nested beneath the corpus root — including files reachable
    /// only through a directory link.
    ///
    /// The termination half keeps its place while the rest of the enumeration
    /// tests go, because a hang is the failure mode this harness has actually
    /// produced: an unattended FIFO in a corpus blocks the sweep on open, and a
    /// test that never returns reports nothing at all. A cycle is the cheap,
    /// portable version of that risk, and the test proves termination by
    /// returning.
    ///
    /// Round 15 found the traversal half missing. Every other corpus in this
    /// file is flat, so deleting the directory push from
    /// <see cref="EnumerateCandidates"/> outright — the sweep then measuring
    /// only the root, which is most of what it is for — left the whole suite
    /// green. Round 16 found that fix half-done in turn: the specimen sat in an
    /// ordinary subdirectory, so skipping every directory carrying
    /// <see cref="FileAttributes.ReparsePoint"/> still swept green, and an
    /// assembly reachable only through a link could vanish under an accounting
    /// claim that says every assembly beneath the root is measured.
    ///
    /// So there are two specimens and two links. <c>nested/</c> is an ordinary
    /// directory and gates plain recursion. <c>linked/</c> points at a directory
    /// outside the corpus, which is the only route to the specimen inside it.
    /// <c>loop/</c> points back at the root and gates nothing but termination —
    /// the walk admits a directory by identity, so the loop is recognised as the
    /// root already visited while <c>linked/</c> is a genuinely new directory
    /// and is walked.
    /// </summary>
    [Fact]
    public void Sweep_DirectoryCycle_TerminatesAndReachesNestedFiles()
    {
        string corpus = NewCorpusDirectory();
        string offRoot = NewCorpusDirectory();
        try
        {
            string specimen = typeof(Fixtures).Assembly.Location;
            byte[] damaged = Damage(File.ReadAllBytes(specimen), DamageKind.Truncated);
            File.Copy(specimen, Path.Combine(corpus, "good.dll"));

            string nested = Path.Combine(corpus, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(nested, "buried.dll"), damaged);

            File.WriteAllBytes(Path.Combine(offRoot, "behindlink.dll"), damaged);

            try
            {
                Directory.CreateSymbolicLink(
                    Path.Combine(corpus, "loop"), corpus);
                Directory.CreateSymbolicLink(
                    Path.Combine(corpus, "linked"), offRoot);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException)
            {
                Assert.Skip($"Symbolic links are unavailable here: {ex.Message}");
            }

            string? problems = SweepProblems(corpus, out string surveyed);

            Assert.False(
                problems is null,
                "Two damaged assemblies below the corpus root were never "
                    + $"enumerated, so the sweep reported it clean. {surveyed}");
            Assert.Contains("buried.dll", problems);
            Assert.Contains("behindlink.dll", problems);

            // The cycle has to be recognised, not merely survived. Round 16
            // showed that deleting the identity check altogether still passed
            // both assertions above: the walk descends the loop until the
            // operating system refuses at its symlink depth limit,
            // GetFileSystemEntries throws, and the directory is recorded as
            // unreadable -- which lands in `problems` and satisfies a test
            // asking only that `problems` is non-null. Forty-one nested copies
            // of the specimen kept the name assertions true as well.
            //
            // Both halves of that are now assertions. A corpus whose only
            // peculiarity is a link back to its own root has nothing unreadable
            // in it, and it holds exactly one undamaged assembly however many
            // times the walk passes the root.
            Assert.Contains("0 unreadable", surveyed);
            Assert.Contains("1 managed assemblies measured", surveyed);
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
            Directory.Delete(offRoot, recursive: true);
        }
    }

    static byte[] Damage(byte[] image, DamageKind kind)
    {
        if (kind == DamageKind.HeaderTruncated)
        {
            // Not enough to answer whether a CLI directory exists, so the file
            // is undecidable rather than positively unmanaged.
            return image[..1];
        }

        if (kind == DamageKind.Truncated)
        {
            // Enough to carry the DOS stub, PE signature, COFF header and
            // optional header, so the CLI directory the oracle reads survives
            // and only the body it points at is missing.
            Assert.True(image.Length > 2048, "The specimen is too small to truncate.");
            return image[..2048];
        }

        byte[] copy = (byte[])image.Clone();
        int signature = copy.AsSpan().IndexOf("BSJB"u8);
        Assert.True(signature >= 0, "The specimen carries no metadata signature.");
        copy[signature] ^= 0xFF;
        return copy;
    }

    /// <summary>
    /// Renames <c>SetStateMachine</c> in the metadata string heap, in place and
    /// to the same length, so the assembly still decodes and every claim it
    /// carries still points where it did — but the role lookup finds no method
    /// of that name and refuses to authenticate.
    /// </summary>
    static byte[] Unauthenticatable(byte[] image)
    {
        byte[] copy = (byte[])image.Clone();
        ReadOnlySpan<byte> role = "SetStateMachine"u8;
        int at = copy.AsSpan().IndexOf(role);
        Assert.True(
            at >= 0,
            "The specimen carries no SetStateMachine to rename, so it cannot "
                + "produce a refused claim.");

        // The heap deduplicates strings, so one edit reaches every reference.
        copy[at + role.Length - 1] = (byte)'Z';
        return copy;
    }

    static string NewCorpusDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"sm-sweep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Renders at most 25 entries and says so when it drops any. An undisclosed
    /// truncation reads as a complete list, which understates the problem.
    /// </summary>
    static string Truncated(List<string> entries)
    {
        const int Limit = 25;
        string body = string.Join("\n  ", entries.Take(Limit));
        return entries.Count <= Limit
            ? $"  {body}"
            : $"  {body}\n  ... and {entries.Count - Limit} more "
                + $"(showing {Limit} of {entries.Count}).";
    }

    /// <summary>
    /// Yields every assembly beneath <paramref name="root"/>, recording anything
    /// it could not read into <paramref name="inaccessible"/>.
    ///
    /// The obvious spelling — <see cref="Directory.EnumerateFiles(string, string, EnumerationOptions)"/>
    /// with <c>IgnoreInaccessible</c> — is wrong for a gate. It keeps one
    /// unreadable subdirectory from aborting the sweep, which is why it was
    /// reached for, but it does so by omitting that subtree silently. A corpus
    /// whose interesting assemblies sit under a directory the test cannot read
    /// then sweeps green while proving nothing about them. Walking explicitly
    /// keeps the sweep robust and the hole visible.
    ///
    /// No test in this file enforces that: an unreadable directory needs
    /// permissions a test cannot portably arrange, and round 13 cut the fixture
    /// seam that faked one. Treat the recording as a design reason for the walk,
    /// not a verified property. #4833 tracks the failure contract that would
    /// give it a home.
    ///
    /// The walk reads each directory once with
    /// <see cref="Directory.GetFileSystemEntries(string)"/> and classifies every
    /// entry itself, rather than taking a <c>GetDirectories</c>/<c>GetFiles</c>
    /// split as the classification. Those two calls do not partition a
    /// directory: a symbolic link whose target sits under a search-denied parent
    /// is absent from the first and present in the second, so the split
    /// presented an unreadable subtree as a file and then dropped it for having
    /// no assembly extension. Anything that is neither a readable directory nor
    /// a candidate file is now recorded rather than assumed uninteresting.
    ///
    /// A <c>visited</c> set keyed on identity, resolved through the final link
    /// target, terminates cycles. A link pointing at its own ancestor otherwise
    /// walks until the operating system's <c>ELOOP</c> limit stops it — measured
    /// at 204 wasted directory reads — and then reports the resulting
    /// <see cref="IOException"/> as a hole, which is a true statement about a
    /// corpus with no hole in it.
    /// </summary>
    static IEnumerable<string> EnumerateCandidates(
        string root,
        List<string> inaccessible)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        pending.Push(root);
        visited.Add(DirectoryIdentity(root));

        while (pending.Count != 0)
        {
            string directory = pending.Pop();

            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (Exception ex)
                when (ex is UnauthorizedAccessException or IOException)
            {
                inaccessible.Add($"{directory}{Path.DirectorySeparatorChar} ({ex.Message})");
                continue;
            }

            foreach (string entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception ex)
                    when (ex is UnauthorizedAccessException or IOException)
                {
                    inaccessible.Add($"{entry} ({ex.Message})");
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    // Identity, not path, so that a link pointing back at an
                    // ancestor is visited once instead of walked until the
                    // operating system's symlink depth limit stops it.
                    if (visited.Add(DirectoryIdentity(entry)))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    // An ordinary file. Its extension is not consulted: round 10
                    // found the old .dll/.exe filter dropping a damaged managed
                    // assembly named broken.bin without a word, while the same
                    // bytes named broken.dll failed the sweep. Nothing about the
                    // CLI header lives in the file name, and the whole design of
                    // this gate is that one hand-written oracle decides what is
                    // managed. Filtering by name put a second, weaker decider in
                    // front of it -- one that answers "not an assembly" for a
                    // file it never opened.
                    //
                    // Everything reaches TryMeasure instead, so a JSON file or a
                    // PDB is answered No by the oracle and counted as NotManaged
                    // rather than vanishing. That costs a header read per file
                    // and buys the population back. It also subsumes the round 3
                    // fix, which had replaced a case-sensitive "*.dll" glob with
                    // an explicit case-insensitive comparison after both seats
                    // showed a corrupt BROKEN.DLL sweeping green on Linux: the
                    // spelling of a name cannot matter to a filter that is gone.
                    yield return entry;
                    continue;
                }

                // A link that did not present itself as a directory. It may still
                // be one: a link whose target sits under a directory without
                // search permission is reported by GetFileSystemEntries but is
                // missing the Directory attribute, so treating it as an ordinary
                // file would drop an entire subtree in silence -- the same
                // false-clean sweep an ignored unreadable directory used to
                // cause. Ask directly rather than assume.
                bool walkable;
                try
                {
                    Directory.GetFileSystemEntries(entry);
                    walkable = true;
                }
                catch (UnauthorizedAccessException ex)
                {
                    inaccessible.Add(
                        $"{entry} (link to an unreadable target: {ex.Message})");
                    continue;
                }
                catch (Exception ex) when (ex is IOException)
                {
                    // Not a directory at all: a dangling link, or a link to an
                    // ordinary file.
                    walkable = false;
                }

                if (walkable)
                {
                    if (visited.Add(DirectoryIdentity(entry)))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                // A link to something that is not a directory is a candidate
                // like any other file. Round 10 caught this dropping such links
                // in silence: it used to be masked because the old extension
                // filter ran before this branch and yielded a link named
                // something.dll first, so removing that filter turned a masked
                // hole into a live one for symlinked assemblies. Let the oracle
                // decide here too -- a dangling link fails to open and is
                // recorded as Inaccessible, which is accounted rather than
                // silent.
                yield return entry;
            }
        }
    }

    /// <summary>
    /// A directory's identity for cycle detection: the final target of any link
    /// chain, falling back to the full path when the entry is not a link or the
    /// target cannot be resolved.
    /// </summary>
    static string DirectoryIdentity(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                    .ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? Path.GetFullPath(path);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException)
        {
            return Path.GetFullPath(path);
        }
    }

    /// <summary>
    /// Why a corpus entry did or did not contribute a measurement. Every file
    /// the sweep opens lands in exactly one of these, so nothing is silently
    /// dropped.
    ///
    /// "Opens" is the honest boundary. A non-regular file named <c>*.dll</c> --
    /// a FIFO or a device node -- can block indefinitely inside
    /// <see cref="File.OpenRead"/> and so reach no outcome at all. .NET exposes
    /// no portable way to detect one beforehand: <c>File.GetAttributes</c>
    /// reports <c>Normal</c> for a FIFO exactly as it does for a regular file,
    /// <c>LinkTarget</c> is null, <c>Length</c> is 0, <c>GetUnixFileMode</c>
    /// returns permission bits only, and there is no non-blocking open. This is
    /// recorded as a known limitation rather than worked around, because the
    /// corpus root is chosen by the developer running the sweep and is not a
    /// hostile input.
    /// </summary>
    enum CorpusOutcome
    {
        /// <summary>Measured normally.</summary>
        Measured,

        /// <summary>Not a managed assembly: a native PE, or not a PE at all.</summary>
        NotManaged,

        /// <summary>Could not be opened or read: permissions, locking, or I/O.</summary>
        Inaccessible,

        /// <summary>A managed assembly whose metadata would not decode.</summary>
        DecodeFailed,

        /// <summary>
        /// A PE that could be opened but not classified: its headers are
        /// damaged before the point where "managed" is decidable, and SRM will
        /// not decode it either. Whether it was a managed assembly is unknown,
        /// so it is a hole in coverage rather than a file to skip.
        /// </summary>
        Unclassifiable,
    }

    /// <summary>
    /// Corpus-tolerant wrapper over <see cref="Measure"/>. It separates
    /// environmental problems, which say nothing about the index, from genuine
    /// metadata decode failures, which do. Callers must account for every
    /// outcome rather than swallowing any of them.
    /// </summary>
    static CorpusOutcome TryMeasure(
        string assemblyPath,
        out CompletenessReport report,
        out string? detail)
    {
        report = new CompletenessReport();
        detail = null;

        FileStream stream;
        try
        {
            stream = File.OpenRead(assemblyPath);
        }
        catch (Exception ex)
            when (ex is UnauthorizedAccessException or IOException)
        {
            detail = ex.Message;
            return CorpusOutcome.Inaccessible;
        }

        using (stream)
        {
            // Whether the file claims to be managed is decided here, before SRM
            // is consulted, and deliberately without using SRM.
            //
            // Round 4 showed why. Zeroing only the CLI header's directory *size*
            // makes SRM reject the PE headers wholesale: `HasMetadata`,
            // `PEHeaders.PEHeader.CorHeaderTableDirectory`, and
            // `GetMetadataReader` all throw BadImageFormatException, exactly as
            // they do for a file that is not a PE at all. Asking SRM to
            // distinguish "not managed" from "managed but damaged" therefore
            // cannot work: once it rejects the headers it will not tell us
            // whether a CLI directory was claimed, so damage was laundered into
            // NotManaged and skipped in silence.
            //
            // The independent read below answers only that one question, which
            // also makes it a genuine oracle rather than a restatement of the
            // thing under test.
            ManagedClaim claim = ReadManagedClaim(stream, ref detail, out ClaimExitSite claimExit);
            if (claim == ManagedClaim.Unreadable)
            {
                return CorpusOutcome.Inaccessible;
            }

            // A file that claims to be managed and will not decode is a decode
            // failure at every seam below, and must fail the sweep. A file whose
            // headers could not be classified and then will not decode is not a
            // skip either: nothing established that it was unmanaged, so calling
            // it NotManaged would assert something no one measured.
            //
            // NotManaged is matched explicitly rather than left as the default
            // arm. It is the laundering answer -- the one that removes a file
            // from the population while the sweep still reports success -- and
            // round 10 noted that as a fall-through it would silently adopt any
            // ManagedClaim member added later. Unclassifiable is the safe
            // direction for an answer nobody has considered yet, so a new member
            // lands there and is visible rather than skipped.
            CorpusOutcome undecodable = claim switch
            {
                ManagedClaim.Yes => CorpusOutcome.DecodeFailed,
                ManagedClaim.No => CorpusOutcome.NotManaged,
                _ => CorpusOutcome.Unclassifiable,
            };

            stream.Position = 0;

            PEReader pe;
            try
            {
                pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
            }
            catch (BadImageFormatException ex)
            {
                detail = $"{ex.GetType().Name}: {ex.Message}";
                return undecodable;
            }
            catch (Exception ex)
                when (ex is UnauthorizedAccessException or IOException)
            {
                detail = ex.Message;
                return CorpusOutcome.Inaccessible;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Same reasoning as the traverse catch below, applied at the
                // site that reaches it first. 400 randomized header corruptions
                // and six adversarial header values produced only
                // BadImageFormatException here, so this is not chasing an
                // observed escape -- it is making CorpusOutcome's documented
                // "every file reaches exactly one outcome" true by construction
                // rather than true by luck. Round 2 showed what the alternative
                // costs: an OverflowException escaped TryMeasure and aborted the
                // whole sweep, which proves nothing and reads like an
                // infrastructure fault rather than a corpus finding.
                detail = $"{ex.GetType().Name}: {ex.Message}";
                return undecodable;
            }

            using (pe)
            {
                bool hasMetadata;
                try
                {
                    hasMetadata = pe.HasMetadata;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Defer to the oracle. SRM is lazy, so an ordinary text file
                    // in a corpus reaches here rather than the constructor:
                    // HasMetadata throws "Unknown file format" and answers
                    // nothing. The oracle already read the headers and did, so
                    // `undecodable` carries its answer -- NotManaged for a file
                    // that never claimed to be managed, DecodeFailed for one
                    // that did, Unclassifiable when the oracle could not tell
                    // either.
                    //
                    // Round 15 proposed returning Unclassifiable unconditionally
                    // here, on the reasoning that SRM throwing means damage
                    // rather than agreement with a No. Tried it:
                    // Sweep_NonManagedFile_IsSkippedButCounted went red, because
                    // notes.txt takes exactly this path and is not damaged. A
                    // corpus of build output is full of such files and the sweep
                    // has to skip them without failing.
                    detail = $"{ex.GetType().Name}: {ex.Message}";
                    return undecodable;
                }

                if (!hasMetadata)
                {
                    // The wording has to follow the claim, not assume it. When
                    // the oracle answered Yes this really is a file with a CLI
                    // directory and no metadata behind it. When the oracle
                    // answered Indeterminate -- an implausible optional header
                    // size, say, on a perfectly ordinary native binary -- it
                    // never established that a CLI directory is present, and
                    // Round 9 caught this message asserting one anyway. That
                    // outcome is Unclassifiable and fails the sweep, so the
                    // detail is what a maintainer reads first; a confident
                    // false statement there sends them looking for a CLI
                    // directory that was never claimed to exist.
                    detail ??= NoMetadataDetail(claim, claimExit);
                    return undecodable;
                }

                try
                {
                    report = Measure(pe.GetMetadataReader());
                    return CorpusOutcome.Measured;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Deliberately broad, and safe to be broad precisely because
                    // DecodeFailed fails the sweep: this surfaces the failure
                    // instead of masking it, and records the exception type so an
                    // unexpected one is identifiable from the assertion message.
                    //
                    // Randomized corruption of a real assembly produced an
                    // OverflowException from GetMetadataReader. A
                    // BadImageFormatException-only catch let that escape
                    // TryMeasure entirely and abort the sweep, reaching no
                    // outcome at all -- contrary to what this type documents.
                    detail = $"{ex.GetType().Name}: {ex.Message}";
                    return CorpusOutcome.DecodeFailed;
                }
            }
        }
    }

    /// <summary>Whether a file's PE headers claim it carries a CLI image.</summary>
    enum ManagedClaim
    {
        /// <summary>
        /// Positively not managed: not a PE at all, or a PE whose headers were
        /// read successfully and carry an empty CLI directory.
        /// </summary>
        No,

        /// <summary>A non-empty CLI data directory: the file claims to be managed.</summary>
        Yes,

        /// <summary>
        /// A PE whose headers could not be read far enough to answer the
        /// question -- truncated, or malformed before the CLI directory.
        /// </summary>
        Indeterminate,

        /// <summary>The headers could not be read for environmental reasons.</summary>
        Unreadable,
    }

    /// <summary>
    /// Every way out of <c>ReadManagedClaim</c>, one member per outcome.
    ///
    /// The enum survives the removal of the coverage test that used to
    /// enumerate it, because <see cref="NoMetadataDetail"/> still names the exit
    /// site in the message a maintainer reads when a sweep fails. "Could not
    /// decide" is not actionable; "could not decide because the optional header
    /// was too short for directory 14" is.
    ///
    /// A member must correspond to an outcome, not to a place in the source.
    /// An earlier revision assigned one member and then branched to two
    /// different answers, which let the common answer report the site reached
    /// while the other was never exercised. Assign the site inside each branch,
    /// after the answer is decided, so that "reached" and "answered this way"
    /// cannot come apart.
    ///
    /// That is a construction rule for whoever edits this method, not a checked
    /// property. Round 13 removed the coverage test that enumerated the sites,
    /// and no test asserts on the message they appear in, so a member that
    /// stopped corresponding to an outcome would not fail anything. The member
    /// notes below likewise record why each site exists and what an earlier
    /// round found; read them as history, not as claims something enforces.
    /// </summary>
    enum ClaimExitSite
    {
        /// <summary>The first signature byte is present and is not <c>M</c>.</summary>
        SignatureFirstByteWrong,

        /// <summary>The second signature byte is present and is not <c>Z</c>.</summary>
        SignatureSecondByteWrong,

        /// <summary>The stream ends inside the two-byte signature.</summary>
        SignatureIncomplete,

        /// <summary>The stream ends inside the DOS header.</summary>
        DosHeaderIncomplete,

        /// <summary>The PE offset does not address a COFF header in the stream.</summary>
        PeOffsetOutOfRange,

        /// <summary>
        /// The four bytes at the PE offset are not the "PE\0\0" signature.
        ///
        /// This does not cover a short read of the COFF header, because
        /// <c>PeOffsetOutOfRange</c> above has already established that at
        /// least 24 bytes remain from the PE offset, so the read cannot come up
        /// short. Round 9 found a truncation disjunct here that shared this
        /// site: it could never fire, and the site read as covered because the
        /// corruption sweep reaches the signature comparison beside it. That is
        /// the Round 7 rule again -- a site must name an outcome, not a place --
        /// so the unreachable disjunct was removed rather than left to be
        /// counted as tested. The optional header read below keeps its own
        /// length check because nothing has established its length first.
        /// </summary>
        PeSignatureWrong,

        /// <summary>The declared optional header size is not a believable size.</summary>
        OptionalHeaderSizeImplausible,

        /// <summary>The stream ends inside the optional header.</summary>
        OptionalHeaderIncomplete,

        /// <summary>The optional header magic is neither PE32 nor PE32+.</summary>
        OptionalHeaderMagicUnrecognised,

        /// <summary>The optional header stops before the CLI directory.</summary>
        CliDirectoryBeyondOptionalHeader,

        /// <summary>The stream could not be read at all.</summary>
        StreamUnreadable,

        /// <summary>
        /// The CLI directory was read and at least one field was non-zero, so
        /// the file claims to be managed.
        /// </summary>
        CliDirectoryPresent,

        /// <summary>
        /// The CLI directory was read and both fields were zero, so the file
        /// makes no managed claim and the sweep may skip it. This is the
        /// laundering direction: a wrong answer here removes a file from the
        /// population while still reporting success, so it gets its own site
        /// rather than sharing one with <see cref="CliDirectoryPresent"/>.
        /// </summary>
        CliDirectoryAbsent,
    }

    static ManagedClaim ReadManagedClaim(Stream stream, ref string? detail) =>
        ReadManagedClaim(stream, ref detail, out _);

    /// <summary>
    /// The exit-path-reporting overload. <paramref name="exitSite"/> is an
    /// <c>out</c> parameter rather than a field or a return flag on purpose:
    /// definite assignment means the compiler refuses to build any path out of
    /// this method that does not name itself. The C# compiler is the gate on
    /// that half, and it is the whole gate: a new early return must pick a
    /// site, but nothing here checks that the site it picks is the right one,
    /// or that any given site is ever reached.
    /// </summary>
    static ManagedClaim ReadManagedClaim(
        Stream stream,
        ref string? detail,
        out ClaimExitSite exitSite)
    {
        try
        {
            stream.Position = 0;

            Span<byte> dos = stackalloc byte[64];
            int read = stream.ReadAtLeast(dos, dos.Length, throwOnEndOfStream: false);

            // Two different answers hide in a short read, and collapsing them is
            // the defect this whole enum exists to prevent. A file that does not
            // begin "MZ" is positively not a PE and is safe to skip in silence.
            // A file that does begin "MZ" but ends before its DOS header does is
            // a truncated PE: it may well have been a managed assembly, and
            // nothing here can tell. That is a coverage hole, not a
            // classification.
            //
            // So compare only the bytes that are actually present. A byte that
            // is present and wrong is a positive answer; a byte that is missing
            // is not an answer at all. Both rounds of this check are written per
            // byte for that reason: a lone "M" at end of stream was "MZ" until
            // something truncated it, and an empty file records nothing
            // whatsoever about what it used to be.
            if (read >= 1 && dos[0] != (byte)'M')
            {
                exitSite = ClaimExitSite.SignatureFirstByteWrong;
                return ManagedClaim.No;
            }

            if (read >= 2 && dos[1] != (byte)'Z')
            {
                exitSite = ClaimExitSite.SignatureSecondByteWrong;
                return ManagedClaim.No;
            }

            if (read < 2)
            {
                exitSite = ClaimExitSite.SignatureIncomplete;
                return ManagedClaim.Indeterminate;
            }

            if (read < dos.Length)
            {
                exitSite = ClaimExitSite.DosHeaderIncomplete;
                return ManagedClaim.Indeterminate;
            }

            int peOffset = BinaryPrimitives.ReadInt32LittleEndian(dos[0x3C..]);
            if (peOffset < 0 || peOffset > stream.Length - 24)
            {
                exitSite = ClaimExitSite.PeOffsetOutOfRange;
                return ManagedClaim.Indeterminate;
            }

            stream.Position = peOffset;

            // PE signature (4) followed by the COFF header (20). The guard above
            // proved that 24 bytes remain, so this read cannot come up short and
            // carries no length check; see ClaimExitSite.PeSignatureWrong.
            Span<byte> coff = stackalloc byte[24];
            stream.ReadAtLeast(coff, coff.Length, throwOnEndOfStream: false);
            if (coff[0] != (byte)'P'
                || coff[1] != (byte)'E'
                || coff[2] != 0
                || coff[3] != 0)
            {
                exitSite = ClaimExitSite.PeSignatureWrong;
                return ManagedClaim.Indeterminate;
            }

            // A real optional header is at most a few hundred bytes. A wild value
            // means this is not a PE worth believing, not something to allocate for.
            int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(coff[^4..]);
            if (optionalSize is < 2 or > 1024)
            {
                exitSite = ClaimExitSite.OptionalHeaderSizeImplausible;
                return ManagedClaim.Indeterminate;
            }

            byte[] optional = new byte[optionalSize];
            if (stream.ReadAtLeast(optional, optionalSize, throwOnEndOfStream: false) < optionalSize)
            {
                exitSite = ClaimExitSite.OptionalHeaderIncomplete;
                return ManagedClaim.Indeterminate;
            }

            // The CLI directory is the fifteenth of the optional header's data
            // directories, which begin after the standard fields: 96 bytes for
            // PE32, 112 for PE32+.
            const int CliDirectoryIndex = 14;
            int directories = BinaryPrimitives.ReadUInt16LittleEndian(optional) switch
            {
                0x10B => 96,
                0x20B => 112,
                _ => -1,
            };

            if (directories < 0)
            {
                exitSite = ClaimExitSite.OptionalHeaderMagicUnrecognised;
                return ManagedClaim.Indeterminate;
            }

            int cli = directories + (CliDirectoryIndex * 8);
            if (optionalSize < cli + 8)
            {
                exitSite = ClaimExitSite.CliDirectoryBeyondOptionalHeader;
                return ManagedClaim.Indeterminate;
            }

            // NumberOfRvaAndSizes is deliberately not consulted. The PE spec
            // says a count below fifteen leaves directory 14 undeclared, so
            // honouring it looks like the more correct read -- but SRM does not
            // honour it. Measured on a specimen with the count shortened to 14
            // and the directory bytes retained: SRM reports
            // NumberOfRvaAndSizes = 14, then returns that directory anyway and
            // answers HasMetadata = true. This oracle exists to classify SRM's
            // failures, so it has to agree with SRM about what SRM will try to
            // read. A count check here would answer No for a file SRM calls
            // managed, which is the laundering direction, reintroduced through
            // the front door.
            //
            // That measurement stands, but no test now pins it: the check that
            // did was one of the branch-level tests removed as disproportionate
            // under AGENTS.md, and a corpus of real build output contains no
            // file with a shortened count to notice a regression. Treat this
            // paragraph as a recorded measurement, not an enforced property.
            //
            // Either field being non-zero is a claim. Reading only the RVA would
            // miss a file whose RVA is zeroed but whose size survives, which is
            // the same hole that reading only the size would leave in the other
            // direction. A file that is genuinely not managed has both fields
            // zero, so requiring both before skipping is the direction that
            // cannot hide damage.
            //
            // Nothing gates that either. Rounds 13 and 14 both reproduced
            // `||` -> `&&` surviving the suite, and both agreed the gate would
            // be disproportionate: reaching the difference needs a PE with a
            // zeroed RVA and a surviving non-zero Size, which no compiler,
            // linker, or trimmer emits. Recorded, not enforced.
            ReadOnlySpan<byte> entry = optional.AsSpan(cli, 8);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]) != 0)
            {
                exitSite = ClaimExitSite.CliDirectoryPresent;
                return ManagedClaim.Yes;
            }

            exitSite = ClaimExitSite.CliDirectoryAbsent;
            return ManagedClaim.No;
        }
        catch (Exception ex)
            when (ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            detail = ex.Message;
            exitSite = ClaimExitSite.StreamUnreadable;
            return ManagedClaim.Unreadable;
        }
    }

    /// <summary>
    /// Measures one assembly by path. Used for assemblies this repository
    /// builds, where any failure to open or decode is a genuine bug rather than
    /// corpus noise, so nothing is caught here. The corpus sweep uses
    /// <see cref="TryMeasure"/> instead.
    /// </summary>
    static CompletenessReport Measure(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);

        Assert.True(
            pe.HasMetadata,
            $"'{assemblyPath}' has no managed metadata, so it cannot be one of "
                + "this repository's own build outputs.");

        return Measure(pe.GetMetadataReader());
    }

    /// <summary>
    /// Classifies every TypeDef that structurally implements
    /// <c>IAsyncStateMachine</c> against the index's own verdict for that type.
    /// </summary>
    static CompletenessReport Measure(MetadataReader reader)
    {
        var report = new CompletenessReport();
        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(reader);

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            if (!ImplementsAsyncStateMachine(reader, handle))
                continue;

            report.Structural++;
            switch (index.GetByStateMachine(handle))
            {
                case StateMachineRelationshipResult.Resolved:
                    report.Resolved++;
                    break;

                case StateMachineRelationshipResult.Rejected rejected:
                    report.Rejected++;
                    report.RejectionKinds.Add(rejected.Failure.Kind.ToString());
                    break;

                default:
                    report.Absent++;
                    break;
            }
        }

        return report;
    }

    /// <summary>
    /// Matches the interface by namespace and name only, ignoring which assembly
    /// it resolves to. That is deliberately more inclusive than any trust policy
    /// the product applies, so this ground truth cannot under-count.
    ///
    /// The one shape it does not read is a TypeSpecification, which round 11
    /// raised as a circularity risk: a machine reached only through a TypeSpec
    /// would never be counted structural, so the index would never be asked
    /// about it, and a defect in the product's own TypeSpec decoding would be
    /// invisible to this gate by construction. That is the right thing to worry
    /// about, and it is measured rather than argued. Across the shared framework
    /// and this test's dependencies -- 272 assemblies, 1,488 IAsyncStateMachine
    /// implementations -- every one arrives as a TypeReference (1,417) or a
    /// TypeDefinition (71) and none as a TypeSpecification, while those same
    /// assemblies carry 4,288 TypeSpec interface implementations for generic
    /// interfaces. The path is busy in general and empty for this interface,
    /// which follows from IAsyncStateMachine being non-generic: a TypeSpec
    /// encoding needs a generic instantiation or a custom modifier, and no C#
    /// or F# compiler emits one here.
    ///
    /// So this is a documented limit rather than an under-count: were a
    /// compiler to start emitting one, this detector would stop seeing those
    /// machines, and the gate would go quiet about them rather than fail.
    /// </summary>
    static bool ImplementsAsyncStateMachine(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        foreach (InterfaceImplementationHandle implementation in
            type.GetInterfaceImplementations())
        {
            EntityHandle @interface =
                reader.GetInterfaceImplementation(implementation).Interface;

            StringHandle namespaceHandle;
            StringHandle nameHandle;
            switch (@interface.Kind)
            {
                case HandleKind.TypeReference:
                    TypeReference reference =
                        reader.GetTypeReference((TypeReferenceHandle)@interface);
                    namespaceHandle = reference.Namespace;
                    nameHandle = reference.Name;
                    break;

                case HandleKind.TypeDefinition:
                    TypeDefinition definition =
                        reader.GetTypeDefinition((TypeDefinitionHandle)@interface);
                    namespaceHandle = definition.Namespace;
                    nameHandle = definition.Name;
                    break;

                default:
                    continue;
            }

            if (reader.StringComparer.Equals(nameHandle, "IAsyncStateMachine")
                && reader.StringComparer.Equals(
                    namespaceHandle,
                    "System.Runtime.CompilerServices"))
            {
                return true;
            }
        }

        return false;
    }

    sealed class CompletenessReport
    {
        public int Structural { get; set; }
        public int Resolved { get; set; }
        public int Rejected { get; set; }
        public int Absent { get; set; }
        public SortedSet<string> RejectionKinds { get; } = [];

        public void Add(CompletenessReport other)
        {
            Structural += other.Structural;
            Resolved += other.Resolved;
            Rejected += other.Rejected;
            Absent += other.Absent;
            RejectionKinds.UnionWith(other.RejectionKinds);
        }
    }
}
