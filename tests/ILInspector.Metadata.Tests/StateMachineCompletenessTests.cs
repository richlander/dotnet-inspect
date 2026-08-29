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
    /// the oracle positively claimed. Shared with the test that gates it so the
    /// two cannot drift apart into a gate that passes by comparing against a
    /// sentence no longer emitted.
    /// </summary>
    const string CliDirectoryPresentDetail =
        "the CLI directory is present but carries no metadata";

    /// <summary>
    /// The detail reported when SRM finds no metadata, worded to match what the
    /// oracle actually established rather than assuming the CLI directory is
    /// present. Factored out so the gate can assert that a detail came from
    /// <em>this</em> branch: without that, a specimen that started reaching SRM's
    /// exception path instead would leave the gate green while measuring
    /// nothing, since an exception message also fails to claim presence.
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
    /// The same property, held over every assembly this test binds against
    /// rather than two hand-picked ones.
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
    /// when dependencies do and cannot silently shrink to the convenient cases.
    /// Today it is 35 assemblies carrying 464 structural machines, including
    /// Microsoft.CodeAnalysis and Microsoft.Testing.Platform, both an order of
    /// magnitude larger than either specimen. The assertion that some neighbour
    /// exceeds the larger specimen is what keeps that true: it is derived from
    /// the specimens rather than pinned to a number, so it stays meaningful if
    /// the specimens grow.
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
        int widest = 0;

        foreach (string path in
            Directory.EnumerateFiles(directory, "*.dll").OrderBy(p => p))
        {
            switch (TryMeasure(path, out CompletenessReport report, out string? detail))
            {
                case CorpusOutcome.Measured:
                    break;

                // A native dependency is legitimate here and carries no claim.
                case CorpusOutcome.NotManaged:
                    continue;

                default:
                    offenders.Add($"{Path.GetFileName(path)}: {detail}");
                    continue;
            }

            examined++;
            structural += report.Structural;
            widest = Math.Max(widest, TypeRowCount(path));

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
            widest > specimen,
            $"the widest neighbour has {widest} type rows and the larger specimen "
                + $"has {specimen}, so this gate no longer reaches past the two "
                + "hand-picked assemblies that missed the round-11 mutation.");
        Assert.True(
            offenders.Count == 0,
            $"""
            {offenders.Count} of {examined} assemblies beside this test did not
            authenticate every structural state machine they carry.

            {Truncated(offenders)}
            """);
    }

    /// <summary>
    /// Type rows in an image, used to show this gate reaches past the two
    /// hand-picked specimens rather than asserting a bare number.
    /// </summary>
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

        var totals = new CompletenessReport();
        var offenders = new List<string>();
        var undecodable = new List<string>();
        var unclassifiable = new List<string>();
        var inaccessible = new List<string>();
        int assemblies = 0;
        int notManaged = 0;

        foreach (string path in EnumerateCandidates(root!, inaccessible))
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

        string surveyed =
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

        Assert.True(
            problems.Count == 0,
            $"{surveyed}\n\n{string.Join("\n\n", problems)}");
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
    /// keeps the sweep robust and the hole visible, which is the property that
    /// actually matters here.
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
    /// A link the sweep cannot follow must be recorded, not skipped. When a
    /// link's target sits under a directory without search permission,
    /// <c>GetFileSystemEntries</c> reports the link but it carries no
    /// <c>Directory</c> attribute, so the old <c>GetDirectories</c>/<c>GetFiles</c>
    /// split showed it as a file; having no assembly extension, it was then
    /// dropped without a word, and every assembly behind it went unexamined
    /// while the sweep reported success.
    /// </summary>
    [Fact]
    public void EnumerateCandidates_LinkToUnreadableDirectory_IsRecorded()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string temp = Path.Combine(
            Path.GetTempPath(),
            $"sm-link-{Guid.NewGuid():N}");
        string corpus = Path.Combine(temp, "corpus");
        string restricted = Path.Combine(temp, "restricted");
        string hidden = Path.Combine(restricted, "hidden");
        Directory.CreateDirectory(corpus);
        Directory.CreateDirectory(hidden);

        try
        {
            File.WriteAllBytes(Path.Combine(corpus, "visible.dll"), []);
            File.WriteAllBytes(Path.Combine(hidden, "concealed.dll"), []);
            Directory.CreateSymbolicLink(Path.Combine(corpus, "link"), hidden);
            File.SetUnixFileMode(restricted, UnixFileMode.None);

            var inaccessible = new List<string>();
            string[] found = EnumerateCandidates(corpus, inaccessible)
                .Select(path => Path.GetFileName(path)!)
                .ToArray();

            Assert.Equal(new[] { "visible.dll" }, found);
            Assert.Single(inaccessible);
            Assert.Contains("link", inaccessible[0]);
        }
        finally
        {
            File.SetUnixFileMode(
                restricted,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// A link pointing back at an ancestor must be visited once. Without
    /// identity tracking the walk descends until the operating system's symlink
    /// depth limit stops it — 204 directory reads for a single link in
    /// measurement, ending in an <c>ELOOP</c> reported as an unreadable entry,
    /// which is a confusing failure for a corpus that is merely unusual.
    /// </summary>
    [Fact]
    public void EnumerateCandidates_SymlinkCycle_TerminatesAndReportsNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            $"sm-cycle-{Guid.NewGuid():N}");
        string inner = Path.Combine(root, "inner");
        Directory.CreateDirectory(inner);

        try
        {
            File.WriteAllBytes(Path.Combine(inner, "only.dll"), []);
            Directory.CreateSymbolicLink(Path.Combine(inner, "loop"), root);

            var inaccessible = new List<string>();
            string[] found = EnumerateCandidates(root, inaccessible).ToArray();

            Assert.Empty(inaccessible);
            Assert.Single(found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The sweep's population is decided by reading files, not by their names,
    /// and the walk reaches nested directories.
    ///
    /// Round 3 found <c>Directory.GetFiles(dir, "*.dll")</c> matching
    /// case-sensitively on Linux, so a corpus holding a corrupt
    /// <c>BROKEN.DLL</c> swept green while that file was never opened, and
    /// replaced the glob with an explicit case-insensitive comparison. Round 10
    /// found that the fix had been too narrow: a damaged managed assembly named
    /// <c>broken.bin</c> still left the corpus without a word, while the
    /// identical bytes named <c>broken.dll</c> failed the sweep.
    ///
    /// A name filter is a second decider standing in front of the oracle, and a
    /// weaker one, since it answers "not an assembly" for a file it never
    /// opened. So there is no filter now, and the case-sensitivity question
    /// disappears with it. Every ordinary file is offered to <c>TryMeasure</c>,
    /// which answers <c>NotManaged</c> for the ones that are not PEs and counts
    /// them, rather than letting them vanish.
    ///
    /// The odd names are the point: extensionless, dot-prefixed, and
    /// double-extension files are the ones a reintroduced filter would drop
    /// first.
    /// </summary>
    [Fact]
    public void EnumerateCandidates_OffersEveryOrdinaryFileWhateverItIsNamed()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"sm-case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            string nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);

            string[] top =
                ["lower.dll", "UPPER.DLL", "app.exe", "damaged.bin", "no-extension"];
            string[] inner = ["Mixed.Dll", "APP.EXE", "notes.txt", ".hidden", "a.tar.gz"];

            foreach (string name in top)
            {
                File.WriteAllBytes(Path.Combine(root, name), []);
            }

            foreach (string name in inner)
            {
                File.WriteAllBytes(Path.Combine(nested, name), []);
            }

            var inaccessible = new List<string>();
            string[] found = EnumerateCandidates(root, inaccessible)
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Empty(inaccessible);
            Assert.Equal(
                top.Concat(inner).Order(StringComparer.Ordinal).ToArray(),
                found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Zeroing either field of the CLI data directory must reach
    /// <c>DecodeFailed</c>, not <c>NotManaged</c>.
    ///
    /// This is a different seam from the metadata-signature case below: SRM
    /// rejects the PE headers outright, so <c>HasMetadata</c> throws exactly as
    /// it does for a file that is not a PE. SRM therefore cannot tell the two
    /// apart, which is why the harness reads the directory itself.
    ///
    /// Round 4 fixed the zeroed-size case and round 5 found the zeroed-RVA case
    /// still open, because the claim was read from the RVA alone. They are one
    /// defect wearing two hats, so they are one theory: a file that still
    /// carries either half of a CLI directory is claiming to be managed, and
    /// failing to decode it is a finding rather than a reason to skip it. A
    /// corpus holding either specimen beside one valid assembly swept green.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void TryMeasure_DamagedCliDirectory_ReportsDecodeFailed(int fieldOffset)
    {
        byte[] image = ZeroedCliDirectoryField(fieldOffset);

        string damaged = Path.Combine(
            Path.GetTempPath(),
            $"sm-cli-{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(damaged, image);

            Assert.Equal(
                CorpusOutcome.DecodeFailed,
                TryMeasure(damaged, out _, out string? detail));
            Assert.NotNull(detail);
        }
        finally
        {
            File.Delete(damaged);
        }
    }

    /// <summary>
    /// A truncated PE reaches <c>Unclassifiable</c>, never <c>NotManaged</c>.
    ///
    /// This is the fourth instance of one defect class, and the third at this
    /// seam. Rounds 2 through 5 each found a different way for the sweep to
    /// report success over files it never covered, and rounds 4 and 5 both found
    /// it here: first the CLI directory's size, then its RVA. Both were fixed by
    /// widening what counts as a managed claim, which treated the specimens
    /// rather than the property.
    ///
    /// The property is that an inability to classify must never present as a
    /// classification. <c>ReadManagedClaim</c> used to answer <c>No</c> for
    /// every shape it could not parse, so a managed assembly truncated anywhere
    /// before its CLI directory was reported non-managed and skipped in silence.
    /// Measured before the fix: of seven truncations of this very assembly, five
    /// were skipped silently, and a corpus holding all five beside one valid
    /// assembly swept green.
    ///
    /// The theory truncates at each structure the header read walks, so a future
    /// change that reintroduces a silent skip at any one of them fails here
    /// rather than in a corpus nobody runs.
    ///
    /// Lengths 0 and 1 are the boundary both round-6 reviewers found
    /// independently, and they are the reason the theory now starts at zero
    /// rather than at two. The signature comparison read a fixed two bytes, so a
    /// file holding a lone <c>M</c> at end of stream failed the comparison
    /// against a byte that was never there and was called positively non-PE.
    /// The two-byte case one row below it was already reported correctly, which
    /// is exactly how a boundary defect survives: the specimen that would have
    /// exposed it sat one byte outside the theory.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(40)]
    [InlineData(64)]
    [InlineData(200)]
    [InlineData(300)]
    public void TryMeasure_TruncatedPortableExecutable_IsUnclassifiable(int length)
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        Assert.Equal((byte)'M', image[0]);
        Assert.Equal((byte)'Z', image[1]);

        string truncated = Path.Combine(
            Path.GetTempPath(),
            $"sm-trunc-{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(truncated, image.AsSpan(0, length).ToArray());

            Assert.Equal(
                CorpusOutcome.Unclassifiable,
                TryMeasure(truncated, out _, out _));
        }
        finally
        {
            File.Delete(truncated);
        }
    }

    /// <summary>
    /// A file that does not begin "MZ" is positively not a PE, so it stays
    /// <c>NotManaged</c> and is skipped in silence.
    ///
    /// This is the negative control for the theory above. Without it, routing
    /// every unparseable shape to <c>Unclassifiable</c> would be indistinguishable
    /// from routing everything there, and a corpus containing ordinary non-PE
    /// files named <c>.dll</c> would fail for no reason.
    /// </summary>
    [Fact]
    public void TryMeasure_NotAPortableExecutable_StaysNotManaged()
    {
        string plain = Path.Combine(
            Path.GetTempPath(),
            $"sm-plain-{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(plain, "not a PE at all"u8.ToArray());

            Assert.Equal(
                CorpusOutcome.NotManaged,
                TryMeasure(plain, out _, out _));
        }
        finally
        {
            File.Delete(plain);
        }
    }

    /// <summary>
    /// A short file whose first byte is present and is not <c>M</c> stays
    /// <c>NotManaged</c>, however short it is.
    ///
    /// This is the negative control for the two boundary rows added to the
    /// theory above. Those rows require a one-byte file to be
    /// <c>Unclassifiable</c>; without this control, "treat one-byte files as
    /// unclassifiable" would satisfy them just as well as the actual rule,
    /// which is that a byte present and wrong is still a positive answer. A
    /// one-byte file holding <c>M</c> is unclassifiable and a one-byte file
    /// holding <c>X</c> is not, and only the per-byte comparison gets both.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { (byte)'X' })]
    [InlineData(new byte[] { (byte)'X', (byte)'Z' })]
    [InlineData(new byte[] { (byte)'M', (byte)'X' })]
    public void TryMeasure_ShortFileWithWrongSignature_StaysNotManaged(byte[] content)
    {
        string wrong = Path.Combine(
            Path.GetTempPath(),
            $"sm-wrong-{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(wrong, content);

            Assert.Equal(
                CorpusOutcome.NotManaged,
                TryMeasure(wrong, out _, out _));
        }
        finally
        {
            File.Delete(wrong);
        }
    }

    /// <summary>
    /// No prefix of a managed assembly, at any length, is ever reported
    /// <c>NotManaged</c>.
    ///
    /// This is the class-level statement of what six rounds kept finding one
    /// specimen at a time. Truncating a managed assembly cannot make it into a
    /// file that was positively never managed: every prefix of one is a shape
    /// that <em>was</em> a managed assembly until something cut it short, so the
    /// only honest answers are "still readable" or "cannot tell". Reporting
    /// <c>NotManaged</c> for any of them is the silent skip this gate exists to
    /// prevent.
    ///
    /// The theory above samples that property at seven lengths, and sampling is
    /// precisely how the round-6 defect survived: the theory started at length 2
    /// and the defect was at length 1. So this enumerates every length rather
    /// than choosing any, which removes the judgement call about which lengths
    /// are interesting -- a judgement that was wrong once and has no reason to
    /// be right next time.
    ///
    /// The bound covers the whole region the header read can touch on this
    /// assembly. That range reaches four of the reader's exit paths --
    /// SignatureIncomplete, DosHeaderIncomplete, PeOffsetOutOfRange and
    /// OptionalHeaderIncomplete -- which are the shapes truncation can actually
    /// produce, measured rather than assumed. An earlier revision of this
    /// comment claimed the range reached *every* early-exit path, which was
    /// false and was the same habit this file keeps having to correct: a
    /// coverage claim written by inspection. Whole-method coverage is the job of
    /// ReadManagedClaim_EnumeratedRange_ReachesEveryExitPath, which measures the
    /// union of all the inputs.
    ///
    /// The theory above is kept even though this subsumes it: it names which
    /// structure each interesting length sits at, so a failure there says what
    /// broke, while a failure here says only where.
    /// </summary>
    [Fact]
    public void TryMeasure_NoPrefixOfAManagedAssemblyIsEverNotManaged()
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        int bound = HeaderReadExtent(image);

        string prefix = Path.Combine(
            Path.GetTempPath(),
            $"sm-prefix-{Guid.NewGuid():N}.dll");

        List<int> skipped = [];

        try
        {
            for (int length = 0; length <= bound; length++)
            {
                File.WriteAllBytes(prefix, image.AsSpan(0, length).ToArray());

                if (TryMeasure(prefix, out _, out _) == CorpusOutcome.NotManaged)
                {
                    skipped.Add(length);
                }
            }
        }
        finally
        {
            File.Delete(prefix);
        }

        Assert.True(
            skipped.Count == 0,
            $"{skipped.Count} prefix lengths of a managed assembly were reported " +
            $"NotManaged and would be skipped in silence: " +
            $"{string.Join(", ", skipped.Take(20))}" +
            (skipped.Count > 20 ? ", ..." : string.Empty));
    }

    /// <summary>
    /// Corrupting any single byte of the PE header region leaves a file that is
    /// still reported as damaged rather than skipped -- except at the two
    /// signature bytes, where being skipped is the correct answer.
    ///
    /// This is the corruption counterpart to the prefix enumeration above, and
    /// it closes the other dimension the earlier rounds kept landing in. The
    /// 400-specimen random fuzz that motivated the broad catches samples this
    /// space; this enumerates the offsets, so no judgement is exercised about
    /// which of them are worth trying.
    ///
    /// It does sample the byte *values*, at 0x00, 0xFF and 0x7F, because this
    /// test writes a file per case and all 256 values across the region would
    /// be some 96,000 writes of a multi-hundred-kilobyte image. Round 9 was
    /// right that "enumerates" was too strong a word for that, and the gap is
    /// closed elsewhere rather than papered over: whether a file is skipped at
    /// all is decided solely by ReadManagedClaim's answer -- NotManaged is
    /// reached only through the precomputed outcome, and only when that answer
    /// is No -- and
    /// ReadManagedClaim_SilentSkipSites_AreExactlyTheKnownSet now enumerates
    /// every one of the 256 values at every offset in memory, where the same
    /// sweep costs a fraction of a second. This test's remaining job is the
    /// TryMeasure-level behaviour over a representative sample of values.
    ///
    /// The property is deliberately two-sided. Offsets 0 and 1 hold "MZ", and a
    /// file whose signature is positively wrong really is not a PE, so
    /// <c>NotManaged</c> is right there and the test asserts it. Every other
    /// offset in the region must not be skipped. A one-sided version would be
    /// satisfied by never skipping anything at all, which would make the gate
    /// fire on ordinary non-PE files; pinning both directions means the carve-out
    /// cannot quietly widen.
    /// </summary>
    [Fact]
    public void TryMeasure_SingleByteHeaderCorruption_IsNeverSilentlySkipped()
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        int bound = HeaderReadExtent(image);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"sm-corrupt-{Guid.NewGuid():N}.dll");

        List<string> skipped = [];
        List<string> unexpectedlyKept = [];

        byte[] copy = (byte[])image.Clone();

        try
        {
            foreach (byte value in (byte[])[0x00, 0xFF, 0x7F])
            {
                for (int offset = 0; offset <= bound; offset++)
                {
                    byte original = copy[offset];
                    copy[offset] = value;
                    File.WriteAllBytes(path, copy);
                    copy[offset] = original;

                    bool isSignatureByte = offset <= 1 && value != original;
                    bool wasSkipped =
                        TryMeasure(path, out _, out _) == CorpusOutcome.NotManaged;

                    if (isSignatureByte && !wasSkipped)
                    {
                        unexpectedlyKept.Add($"offset {offset}, value 0x{value:X2}");
                    }
                    else if (!isSignatureByte && wasSkipped)
                    {
                        skipped.Add($"offset {offset}, value 0x{value:X2}");
                    }
                }
            }
        }
        finally
        {
            File.Delete(path);
        }

        Assert.True(
            skipped.Count == 0,
            $"{skipped.Count} single-byte header corruptions were reported " +
            $"NotManaged and would be skipped in silence: " +
            $"{string.Join("; ", skipped.Take(20))}" +
            (skipped.Count > 20 ? "; ..." : string.Empty));

        Assert.True(
            unexpectedlyKept.Count == 0,
            $"{unexpectedlyKept.Count} corruptions of the MZ signature were not " +
            $"reported NotManaged, so the negative half of this property no " +
            $"longer holds: {string.Join("; ", unexpectedlyKept)}");
    }

    /// <summary>
    /// A file whose <c>NumberOfRvaAndSizes</c> does not reach the CLI directory
    /// still reaches <c>DecodeFailed</c>, because SRM reads that directory
    /// regardless of the declared count.
    ///
    /// A round-5 reviewer proposed the opposite: honour the count, and report
    /// this specimen <c>NotManaged</c>. Measuring SRM directly settled it the
    /// other way. On this exact file SRM reports
    /// <c>NumberOfRvaAndSizes = 14</c>, returns the stale directory anyway,
    /// answers <c>HasMetadata = true</c>, and only then fails with
    /// <c>Invalid COR20 header signature</c>. SRM calls the file managed, so a
    /// decode failure is the honest outcome and skipping it would be the
    /// laundering this gate exists to prevent.
    ///
    /// This test exists to keep that decision from being quietly reversed by
    /// the next reader who notices the missing count check and assumes it is an
    /// oversight. If SRM ever starts honouring the count, this test fails and
    /// the oracle should be changed to match it.
    /// </summary>
    [Fact]
    public void TryMeasure_ShortDirectoryCount_StillDecodeFailed()
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        int optional = peOffset + 4 + 20;
        int directories =
            BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(optional)) == 0x20B
                ? 112
                : 96;

        int metadata;
        using (var original = new PEReader(
            File.OpenRead(typeof(StateMachineCompletenessTests).Assembly.Location)))
        {
            metadata = original.PEHeaders.MetadataStartOffset;
        }

        // Shorten the count so directory 14 is undeclared, but leave its bytes.
        int count = optional + directories - 4;
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(count)) > 14);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(count), 14);
        Assert.NotEqual(
            0u,
            BinaryPrimitives.ReadUInt32LittleEndian(
                image.AsSpan(optional + directories + (14 * 8))));

        // Damage the metadata so the file cannot decode, which is what makes the
        // classification observable at all.
        new Random(7).NextBytes(image.AsSpan(metadata, 512));

        string stale = Path.Combine(
            Path.GetTempPath(),
            $"sm-nodir-{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(stale, image);

            using (var reader = new PEReader(File.OpenRead(stale)))
            {
                Assert.Equal(14, reader.PEHeaders.PEHeader!.NumberOfRvaAndSizes);
                Assert.NotEqual(
                    0,
                    reader.PEHeaders.PEHeader.CorHeaderTableDirectory
                        .RelativeVirtualAddress);
                Assert.True(
                    reader.HasMetadata,
                    "SRM ignores NumberOfRvaAndSizes; if this fails, the oracle "
                        + "should start honouring the count.");
            }

            Assert.Equal(
                CorpusOutcome.DecodeFailed,
                TryMeasure(stale, out _, out _));
        }
        finally
        {
            File.Delete(stale);
        }
    }

    /// <summary>
    /// A damaged managed assembly must reach <c>DecodeFailed</c>, not
    /// <c>NotManaged</c>. The two outcomes mean opposite things to the sweep:
    /// NotManaged is skipped in silence, DecodeFailed fails the run. Classifying
    /// damage as NotManaged therefore launders exactly the files this gate
    /// exists to notice, and nothing else here would catch it -- the corpus
    /// assertion is <c>Rejected == 0</c>, which an assembly that is never
    /// measured satisfies trivially.
    ///
    /// This is also the test that makes <c>DecodeFailed</c> a verified outcome
    /// rather than an asserted one.
    /// </summary>
    [Fact]
    public void TryMeasure_DamagedManagedAssembly_ReportsDecodeFailed()
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        int metadataStart;
        using (var probe = new PEReader(new MemoryStream(image)))
        {
            Assert.True(probe.HasMetadata);
            metadataStart = probe.PEHeaders.MetadataStartOffset;
        }

        // The metadata root signature, "BSJB". The CLI header still points at
        // this offset, so the file goes on claiming to be managed and only the
        // metadata itself is unreadable.
        image[metadataStart] ^= 0xFF;

        string damaged = Path.Combine(
            Path.GetTempPath(),
            $"sm-damaged-{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(damaged, image);

            CorpusOutcome outcome =
                TryMeasure(damaged, out _, out string? detail);

            Assert.Equal(CorpusOutcome.DecodeFailed, outcome);
            Assert.NotNull(detail);
        }
        finally
        {
            File.Delete(damaged);
        }
    }

    /// <summary>
    /// Every way a file can leave the population silently, measured rather than
    /// asserted.
    ///
    /// A <see cref="ManagedClaim.No"/> answer becomes NotManaged and is skipped
    /// without the sweep reporting anything, so the set of sites that can
    /// produce it is the set of blind spots. An earlier revision of the test
    /// below described the zeroed CLI directory as "the one way" that happens.
    /// That was wrong -- round 8 pointed out that corrupting either signature
    /// byte does it too -- and it was wrong in the way this PR keeps finding:
    /// a claim about coverage made by inspection instead of by measurement.
    ///
    /// So the set is collected from the enumerations instead of described. If a
    /// future change adds a fourth way to answer No, this fails and names it,
    /// and whoever adds it has to decide whether it is a defect or a limit
    /// worth pinning. Equality is asserted in both directions, so a site that
    /// stops being reachable fails here too rather than quietly leaving the set.
    ///
    /// The corruption sweep here enumerates all 256 byte values at every offset
    /// in the header read extent, not the three values the file-writing test
    /// samples. Round 9 pointed out that a branch keyed on some other value --
    /// answering No when a byte equals 0x42, say -- would slip past a sampled
    /// sweep while everything stayed green. In memory the full sweep is around
    /// 96,000 reads and costs a fraction of a second, so there was never a
    /// reason to sample it. This test carries the laundering-relevant claim, so
    /// it is the one that has to be exhaustive: a file is skipped if and only if
    /// this reader answers No, which makes the set below the complete account of
    /// how a file can vanish from the sweep in silence.
    ///
    /// That biconditional is about the *mechanism*, and it holds: NotManaged is
    /// produced only through the precomputed outcome, and only for a No. It is
    /// not a claim that a No is correct. Round 10 built a COFF-only image -- no
    /// MZ signature at all, carrying real metadata in a .cormeta section -- that
    /// SRM reads as managed while this reader answers No. See
    /// TryMeasure_ZeroedCliDirectory_IsSkippedAsTheKnownBlindSpot for what that
    /// costs and why it is accepted.
    /// </summary>
    [Fact]
    public void ReadManagedClaim_SilentSkipSites_AreExactlyTheKnownSet()
    {
        ClaimExitSite[] expected =
        [
            ClaimExitSite.SignatureFirstByteWrong,
            ClaimExitSite.SignatureSecondByteWrong,
            ClaimExitSite.CliDirectoryAbsent,
        ];

        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        int bound = HeaderReadExtent(image);

        HashSet<ClaimExitSite> skipping = [];
        string? detail = null;

        for (int length = 0; length <= bound; length++)
        {
            using MemoryStream prefix = new(image.AsSpan(0, length).ToArray());
            if (ReadManagedClaim(prefix, ref detail, out ClaimExitSite site)
                == ManagedClaim.No)
            {
                skipping.Add(site);
            }
        }

        byte[] copy = (byte[])image.Clone();

        for (int value = 0; value <= 0xFF; value++)
        {
            for (int offset = 0; offset <= bound; offset++)
            {
                byte original = copy[offset];
                copy[offset] = (byte)value;

                using (MemoryStream corrupted = new(copy))
                {
                    if (ReadManagedClaim(corrupted, ref detail, out ClaimExitSite site)
                        == ManagedClaim.No)
                    {
                        skipping.Add(site);
                    }
                }

                copy[offset] = original;
            }
        }

        foreach ((bool _, bool _, byte[] zeroed) in CliDirectoryZeroings(image))
        {
            using MemoryStream stream = new(zeroed, writable: false);
            if (ReadManagedClaim(stream, ref detail, out ClaimExitSite site)
                == ManagedClaim.No)
            {
                skipping.Add(site);
            }
        }

        foreach (int optionalSize in (int[])[2, 96, 176, 215])
        {
            using MemoryStream stream =
                new(ShortOptionalHeaderImage(optionalSize), writable: false);
            if (ReadManagedClaim(stream, ref detail, out ClaimExitSite site)
                == ManagedClaim.No)
            {
                skipping.Add(site);
            }
        }

        ClaimExitSite[] unexpected = [.. skipping.Except(expected)];
        ClaimExitSite[] missing = [.. expected.Except(skipping)];

        Assert.True(
            unexpected.Length == 0,
            $"{string.Join(", ", unexpected)} answered No, so a file can now "
                + "leave the sweep's population silently through a path that is "
                + "not pinned as a known blind spot.");

        Assert.True(
            missing.Length == 0,
            $"{string.Join(", ", missing)} is declared a silent-skip path but "
                + "no enumerated input reached it that way, so the declared set "
                + "no longer describes the reader.");
    }

    /// <summary>
    /// The last byte offset <c>ReadManagedClaim</c> can consult for this image.
    ///
    /// The reader touches exactly three regions: the 64-byte DOS header, the
    /// 24-byte PE signature and COFF header at <c>peOffset</c>, and the
    /// optional header of the declared size after that. The furthest byte it
    /// can reach is therefore derived from the image itself, not chosen.
    ///
    /// The enumerations used to stop at <c>peOffset + 512</c>, a number picked
    /// by hand and justified by the claim that the coverage test would go red
    /// if it were too small. Round 9 re-measured that and recorded the opposite:
    /// that coverage stayed green at a bound of 63, so the test did not
    /// constrain the number at all. Round 10 measured it again at this head and
    /// the round 9 result was wrong -- clamping this method to 63 fails coverage
    /// naming OptionalHeaderSizeImplausible, OptionalHeaderIncomplete and
    /// OptionalHeaderMagicUnrecognised, because those need corruption in the
    /// optional header at file offsets around 148 and 152, outside the range.
    /// The stale round 9 experiment had clamped only the real assembly and left
    /// the constructed short-header specimens at their own extents.
    ///
    /// So the coverage test does constrain the bound, and deriving the extent is
    /// what keeps every image in range rather than hoping one hand-picked number
    /// suits them all. Perturbing every byte the reader can read is a property of
    /// this image, checkable against the three reads above, and it moves
    /// automatically if those reads change.
    /// </summary>
    static int HeaderReadExtent(byte[] image)
    {
        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        int optionalSize =
            BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(peOffset + 20));

        int furthest = Math.Max(0x3F, peOffset + 24 + optionalSize - 1);
        return Math.Min(image.Length - 1, furthest);
    }

    /// <summary>
    /// A minimal PE whose optional header is declared too short to contain data
    /// directory 14, built deliberately rather than hoped for.
    ///
    /// Round 8 observed that <see cref="ClaimExitSite.CliDirectoryBeyondOptionalHeader"/>
    /// was reached only because this repository's own test assembly happens to
    /// declare a 224-byte optional header, so corrupting its low byte to 0x7F
    /// yields 127, which is short enough. That is coverage by accident: an
    /// assembly with a different size could leave the site unreached while the
    /// coverage test still passed. Constructing the shape directly makes the
    /// reach structural.
    /// </summary>
    static byte[] ShortOptionalHeaderImage(int optionalSize)
    {
        byte[] image = new byte[1024];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C), 128);
        image[128] = (byte)'P';
        image[129] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(128 + 20), (ushort)optionalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(128 + 24), 0x10B);
        return image;
    }

    /// <summary>
    /// The no-metadata detail never asserts more about the CLI directory than
    /// the oracle actually established.
    ///
    /// Round 9 found this message hardcoded to "the CLI directory is present
    /// but carries no metadata" on every path that reached it. That sentence is
    /// true only when the oracle answered Yes. When it answered Indeterminate --
    /// an implausible optional header size on an ordinary native binary, say --
    /// nothing had established that a CLI directory exists, and the outcome is
    /// Unclassifiable, which fails the sweep. The detail is the first thing a
    /// maintainer reads on that failure, so a confident false statement there
    /// sends them hunting for a directory nobody claimed to have seen. Measured
    /// before the fix, a 224-byte specimen whose oracle exit was
    /// CliDirectoryAbsent -- a positive finding of absence -- still reported the
    /// directory as present.
    ///
    /// The assertion is written as the general property and both arms are now
    /// exercised. Round 10 found the earlier version of this comment claiming no
    /// specimen could make SRM report no metadata while the oracle claims Yes,
    /// and that claim was false in the same way this file keeps being wrong: it
    /// was reached by trying one construction -- zeroing a real assembly's COR
    /// header metadata directory, which makes SRM throw and land in DecodeFailed
    /// -- and concluding from that single failure that none existed. A specimen
    /// already in this file falsifies it. Zeroing only the CLI directory's RVA
    /// while leaving its size non-zero leaves the oracle answering Yes, and SRM
    /// then reports no metadata without throwing, which is exactly the shape the
    /// Yes wording describes. That specimen is included below, so the Yes arm is
    /// covered rather than assumed unreachable.
    /// </summary>
    [Fact]
    public void TryMeasure_NoMetadataDetail_NeverOutrunsTheOracleClaim()
    {
        var specimens = new List<byte[]>();
        foreach (int optionalSize in new[] { 1, 96, 176, 215, 224, 1025 })
        {
            specimens.Add(ShortOptionalHeaderImage(optionalSize));
        }

        // The Yes arm: a real assembly whose CLI directory RVA is zeroed while
        // its size is left intact. The oracle still sees a directory claiming to
        // be managed, and SRM reports no metadata without throwing.
        specimens.Add(ZeroedCliDirectoryField(0));

        bool sawYes = false;

        foreach (byte[] image in specimens)
        {
            string? readerDetail = null;
            ManagedClaim claim;
            ClaimExitSite exit;
            using (var memory = new MemoryStream(image, writable: false))
            {
                claim = ReadManagedClaim(memory, ref readerDetail, out exit);
            }

            sawYes |= claim == ManagedClaim.Yes;

            string path = Path.Combine(
                Path.GetTempPath(),
                $"sm-detail-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image);

            try
            {
                TryMeasure(path, out _, out string? detail);

                Assert.NotNull(detail);

                // Pins that the detail came from the no-metadata branch at all.
                // The self-audit before round 10 found this test would have
                // stayed green if a specimen started reaching SRM's exception
                // path instead, because an exception message does not claim
                // presence either -- passing for a reason unrelated to what it
                // asserts.
                Assert.Equal(NoMetadataDetail(claim, exit), detail);

                Assert.Equal(
                    claim == ManagedClaim.Yes,
                    detail!.Contains(CliDirectoryPresentDetail, StringComparison.Ordinal));
            }
            finally
            {
                File.Delete(path);
            }
        }

        // Without this the suite could drift back to exercising only the false
        // arm -- which is the state round 10 found, described in the comment
        // above as if it were a property of the world rather than of the
        // specimen list.
        Assert.True(sawYes, "no specimen exercised the ManagedClaim.Yes arm");
    }

    /// <summary>
    /// A link to something that is not a directory is still a candidate.
    ///
    /// Round 10 found these dropped without a word. The hole predates that
    /// round but was masked: the old extension filter ran first and yielded a
    /// link named <c>something.dll</c> before this branch could drop it, so
    /// removing the filter in the same round turned a masked hole into a live
    /// one for exactly the files most likely to matter -- symlinked assemblies,
    /// which is how shared frameworks and many package layouts are assembled.
    ///
    /// A dangling link is included deliberately. It cannot be measured, but it
    /// must be <em>accounted</em>: it opens as Inaccessible rather than
    /// disappearing.
    /// </summary>
    [Fact]
    public void EnumerateCandidates_OffersLinksToOrdinaryFiles()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"sm-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            string target = Path.Combine(root, "target.dll");
            File.WriteAllBytes(
                target,
                File.ReadAllBytes(typeof(StateMachineCompletenessTests).Assembly.Location));

            File.CreateSymbolicLink(Path.Combine(root, "link.dll"), target);
            File.CreateSymbolicLink(Path.Combine(root, "link.bin"), target);
            File.CreateSymbolicLink(
                Path.Combine(root, "dangling.dll"),
                Path.Combine(root, "nothing-here"));

            var inaccessible = new List<string>();
            HashSet<string> found = EnumerateCandidates(root, inaccessible)
                .Select(path => Path.GetFileName(path)!)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Equal(
                new[] { "target.dll", "link.dll", "link.bin", "dangling.dll" }
                    .ToHashSet(StringComparer.Ordinal),
                found);

            // The links to a real assembly must measure like the assembly, and
            // the dangling one must be accounted rather than skipped.
            foreach (string name in new[] { "link.dll", "link.bin" })
            {
                Assert.Equal(
                    CorpusOutcome.Measured,
                    TryMeasure(Path.Combine(root, name), out _, out _));
            }

            Assert.Equal(
                CorpusOutcome.Inaccessible,
                TryMeasure(Path.Combine(root, "dangling.dll"), out _, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A FIFO that is already open cannot be measured, but it must not abort
    /// the sweep.
    ///
    /// <c>File.OpenRead</c> succeeds on a FIFO opened for reading, and the
    /// oracle's first act is to seek, which throws <c>NotSupportedException</c>
    /// on a non-seekable stream. Round 10 found that escaping <c>TryMeasure</c>
    /// entirely, so one such file ends the run before it reaches any outcome --
    /// the round 2 failure in a new place, where an escaping exception makes the
    /// sweep prove nothing and look like an infrastructure fault. Round 10's
    /// removal of the extension filter widened the exposure, since the file no
    /// longer has to be named like an assembly to be opened.
    ///
    /// Reported as Inaccessible, which is accounted and visible.
    ///
    /// The name says "opened" because that is the whole of what this proves,
    /// and round 11 caught the earlier name claiming more. The test holds a
    /// read-write handle so the oracle's own <c>File.OpenRead</c> returns; an
    /// <em>unattended</em> FIFO blocks in the open itself, and the sweep hangs
    /// rather than failing. Measured: a corpus containing one bare FIFO does not
    /// terminate.
    ///
    /// That is a known limit, not a fixed one. Avoiding it needs the file's type
    /// before opening, and .NET does not expose it -- a FIFO and an empty
    /// ordinary file report the same Attributes (Normal), the same
    /// UnixFileMode, and the same zero Length, so only a P/Invoke to stat could
    /// tell them apart. That is platform-specific machinery this harness should
    /// not carry for an entry no assembly corpus plausibly holds, and a hang is
    /// at least loud: unlike a silent skip, nobody mistakes it for a pass.
    /// </summary>
    [Fact]
    public void TryMeasure_OpenedNonSeekableFile_IsInaccessibleRatherThanFatal()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"sm-fifo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string fifo = Path.Combine(root, "pipe.dll");

        try
        {
            using (var mkfifo = System.Diagnostics.Process.Start("mkfifo", [fifo]))
            {
                Assert.NotNull(mkfifo);
                mkfifo!.WaitForExit();
                Assert.SkipWhen(
                    mkfifo.ExitCode != 0,
                    "mkfifo is unavailable, so the non-seekable case cannot be built.");
            }

            // Opening a FIFO write-only blocks until a reader arrives, and
            // read-only blocks until a writer does. Opening read-write does not
            // block on Linux, and keeping that handle open means the oracle's
            // own File.OpenRead returns immediately instead of waiting.
            using FileStream held = new(
                fifo, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

            Assert.Equal(
                CorpusOutcome.Inaccessible,
                TryMeasure(fifo, out _, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A copy of this test assembly with one field of its CLI directory zeroed:
    /// offset 0 is the RVA, offset 4 the size. Either half left standing is
    /// still a claim to be managed, which is the round 4 and round 5 lesson.
    /// </summary>
    static byte[] ZeroedCliDirectoryField(int fieldOffset)
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        int optional = peOffset + 4 + 20;
        int directories =
            BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(optional)) == 0x20B
                ? 112
                : 96;
        int field = optional + directories + (14 * 8) + fieldOffset;

        Assert.NotEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(field)));
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(field), 0);
        return image;
    }

    /// <summary>
    /// A PE that declares fewer than fifteen data directories is Unclassifiable,
    /// and that is deliberate.
    ///
    /// The PE specification allows it -- 96 standard bytes plus eight per
    /// directory, so ten directories give a 176-byte optional header, and
    /// directory 14 does not exist. Round 8 raised this as a false alarm on
    /// legitimate native content and proposed answering NotManaged instead.
    ///
    /// The observation is right and the prescription is not, for two measured
    /// reasons. First, incidence: across 90,960 real PE files in this machine's
    /// package and SDK trees, every one declares 224 or 240, and none is too
    /// short. The shape is legal but does not occur. Second, direction:
    /// NotManaged is the laundering answer, the one that removes a file from
    /// the population while the sweep still reports success, and it is where
    /// every finding in rounds 2 through 8 has lived. Answering it here would
    /// assert "this file is not a managed assembly" on the strength of a header
    /// this reader could not read to the end.
    ///
    /// So the conservative direction is kept and pinned instead of being left
    /// to chance. A visible false alarm on a shape with zero measured incidence
    /// costs an operator one look; a silent skip costs the gate its meaning.
    /// </summary>
    [Fact]
    public void TryMeasure_OptionalHeaderTooShortForCliDirectory_IsUnclassifiable()
    {
        byte[] image = ShortOptionalHeaderImage(176);

        using (MemoryStream stream = new(image, writable: false))
        {
            string? probeDetail = null;
            ManagedClaim claim =
                ReadManagedClaim(stream, ref probeDetail, out ClaimExitSite site);

            Assert.Equal(ManagedClaim.Indeterminate, claim);
            Assert.Equal(ClaimExitSite.CliDirectoryBeyondOptionalHeader, site);
        }

        string path = Path.Combine(
            Path.GetTempPath(), $"sm-shortopt-{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(path, image);

            Assert.Equal(
                CorpusOutcome.Unclassifiable,
                TryMeasure(path, out _, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A path that cannot be opened reaches Inaccessible.
    ///
    /// Round 8 pointed out that nothing exercised this outcome through
    /// <c>TryMeasure</c> itself: the I/O exit in <c>ReadManagedClaim</c> was
    /// covered by a throwing stream, but no test handed <c>TryMeasure</c> a path
    /// it could not open. A corpus sweep can genuinely meet one, because a file
    /// can be removed between enumeration and open.
    /// </summary>
    [Fact]
    public void TryMeasure_UnopenableFile_ReportsInaccessible()
    {
        string missing = Path.Combine(
            Path.GetTempPath(), $"sm-missing-{Guid.NewGuid():N}.dll");

        Assert.False(File.Exists(missing));

        Assert.Equal(
            CorpusOutcome.Inaccessible,
            TryMeasure(missing, out _, out string? detail));

        Assert.NotNull(detail);
    }

    /// <summary>
    /// The boundary of what this oracle can see, pinned deliberately.
    ///
    /// A managed assembly whose CLI directory has been zeroed in both fields is
    /// skipped as NotManaged. It is one of three ways a file that really was a
    /// managed assembly can leave the population without the sweep reporting
    /// anything; corrupting either signature byte does the same, and
    /// ReadManagedClaim_SilentSkipSites_AreExactlyTheKnownSet measures that the
    /// three are all of them.
    ///
    /// These three are all the same limitation seen at different fields: once
    /// the bytes this reader consults to recognise a managed PE are gone, the
    /// file looks like something that never was one. SRM cannot tell "not
    /// managed" from "managed but damaged" once it has rejected the headers
    /// either. Answering Unclassifiable instead would make every native DLL in
    /// every corpus unclassifiable, which would retire the gate. Reporting the
    /// gap belongs to the product's failure contract, not to a wider guess here.
    ///
    /// "Not fixable" would be too strong, and an earlier revision said it.
    /// Round 10 showed one of the three is fixable in principle by building a
    /// COFF-only image: no MZ signature at all, with the metadata blob reached
    /// through a .cormeta section instead of a CLI directory. Construction is
    /// mechanical -- lay a COFF file header with SizeOfOptionalHeader 0 and a
    /// section table over an assembly's DOS and PE headers, keeping every
    /// VirtualAddress and PointerToRawData, and add a .cormeta entry addressing
    /// the existing metadata. Built that way from ILInspector.Metadata.dll, SRM
    /// reports IsCoffOnly with 749 type definitions and 7,714 method
    /// definitions, while this reader answers No at the first signature byte.
    /// Recognising SRM's COFF-only path would close that.
    ///
    /// It is not closed here, on measured grounds rather than by assertion.
    /// Incidence is 0 across 86,374 real PE files, and COFF-only objects are a
    /// C++/CLI intermediate rather than anything a corpus of assemblies holds.
    /// Both halves of the exposure were measured on that specimen rather than
    /// reasoned about: intact, it is measured normally, because SRM succeeds
    /// and the No never reaches the precomputed outcome -- a corpus holding
    /// only that file passes, and the same corpus with no measurable assembly
    /// fails, so the pass is evidence it was counted. Truncated, it reports
    /// "1 non-managed skipped" and vanishes in silence. So the live exposure is
    /// a damaged COFF-only object, which narrows an already empty set.
    ///
    /// Against that, the oracle's value is that it is short enough to check by
    /// reading, and teaching it a second container format is the kind of growth
    /// that ends with it needing its own oracle. The limit is recorded instead.
    /// </summary>
    ///
    /// Round 7 is why this exists: the "absent" answer was unreachable by both
    /// enumerations and shared an exit site with "present", so nothing measured
    /// it and nothing described it. The test is two-sided so that a reader that
    /// skipped everything could not satisfy it.
    /// </summary>
    [Fact]
    public void TryMeasure_ZeroedCliDirectory_IsSkippedAsTheKnownBlindSpot()
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        int cliOffset = CliDirectoryFileOffset(image);

        string intact = Path.Combine(
            Path.GetTempPath(), $"sm-intact-{Guid.NewGuid():N}.dll");
        string zeroed = Path.Combine(
            Path.GetTempPath(), $"sm-zeroed-{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(intact, image);

            byte[] copy = (byte[])image.Clone();
            copy.AsSpan(cliOffset, 8).Clear();
            File.WriteAllBytes(zeroed, copy);

            Assert.Equal(
                CorpusOutcome.Measured,
                TryMeasure(intact, out _, out _));

            Assert.Equal(
                CorpusOutcome.NotManaged,
                TryMeasure(zeroed, out _, out _));
        }
        finally
        {
            File.Delete(intact);
            File.Delete(zeroed);
        }
    }

    /// <summary>
    /// The negative half of the same seam. Routing damage to DecodeFailed is
    /// only correct if files that are genuinely not managed assemblies still
    /// reach NotManaged; a gate that answered DecodeFailed for everything would
    /// pass the test above and fail every real corpus.
    /// </summary>
    [Fact]
    public void TryMeasure_NonPortableExecutable_ReportsNotManaged()
    {
        string garbage = Path.Combine(
            Path.GetTempPath(),
            $"sm-garbage-{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(garbage, "not a PE file, despite the extension"u8.ToArray());

            Assert.Equal(
                CorpusOutcome.NotManaged,
                TryMeasure(garbage, out _, out _));
        }
        finally
        {
            File.Delete(garbage);
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
    /// Reads the PE optional header's CLI data directory (index 14) directly and
    /// reports whether it is present, leaving <paramref name="stream"/> at an
    /// unspecified position.
    ///
    /// This is an independent oracle, not a reimplementation of anything the
    /// product owns: it exists precisely because SRM cannot answer this question
    /// once it has rejected a file's headers, and because using SRM to classify
    /// SRM's own failures would make the classification circular. It reads
    /// nothing beyond the directory count and the CLI directory's two fields,
    /// and distinguishes a shape it can positively reject from one it merely
    /// cannot read rather than guessing.
    ///
    /// The two directions of error are not symmetric, and the code is written
    /// for that asymmetry. A false <see cref="ManagedClaim.No"/> sends a damaged
    /// managed assembly to the skip bucket, which is a silent hole in a
    /// completeness gate. A false <see cref="ManagedClaim.Yes"/> only turns an
    /// undecodable non-assembly into a visible failure. So the claim is read
    /// generously — either CLI field non-zero — and the structural
    /// preconditions around it are read strictly.
    /// </summary>
    /// <summary>
    /// The enumerations above reach every exit path out of
    /// <c>ReadManagedClaim</c>.
    ///
    /// Every path names itself through an <c>out</c> parameter, definite
    /// assignment makes the compiler reject any path that does not, and this
    /// test requires the inputs to reach all of them. A path added later that
    /// nothing exercises fails here and says which one.
    ///
    /// This test also constrains the enumerations' bound, which round 9 denied.
    /// Round 9 recorded that coverage stayed green at a bound of 63 and
    /// concluded the bound was unmeasured; round 10 re-ran it and the recorded
    /// result was wrong, because that experiment had clamped only the real
    /// assembly. Clamping <see cref="HeaderReadExtent"/> itself to 63 fails here
    /// naming OptionalHeaderSizeImplausible, OptionalHeaderIncomplete and
    /// OptionalHeaderMagicUnrecognised. The bound is derived from the image
    /// rather than hand-picked, and this test is what would notice if the
    /// derivation stopped short.
    ///
    /// The I/O failure handler is the one site no file content can reach, so a
    /// stream that throws on read covers it directly rather than being excused
    /// as an exception to the rule. Coverage here is total, with no carve-out
    /// to keep honest.
    ///
    /// Round 7 found that total coverage was not the same as total coverage of
    /// the outcomes. Reading the CLI directory answers either "present" or
    /// "absent", and both answers shared one site, so the "present" answer that
    /// every ordinary managed file produces reported the site reached and the
    /// "absent" answer was never exercised at all. Neither enumeration could
    /// reach it: single-byte corruption cannot zero eight bytes, and any prefix
    /// long enough to contain the directory contains its real non-zero value.
    /// The site is split in two now, and the field-zeroing dimension below
    /// reaches the second one. The general rule this leaves is that a site must
    /// name an outcome, not a place in the source; a site assigned before a
    /// branch is reported reached by whichever branch runs first.
    /// </summary>
    [Fact]
    public void ReadManagedClaim_EnumeratedRange_ReachesEveryExitPath()
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        int bound = HeaderReadExtent(image);

        HashSet<ClaimExitSite> reached = [];
        string? detail = null;

        for (int length = 0; length <= bound; length++)
        {
            using MemoryStream prefix = new(image.AsSpan(0, length).ToArray());
            ReadManagedClaim(prefix, ref detail, out ClaimExitSite site);
            reached.Add(site);
        }

        byte[] copy = (byte[])image.Clone();

        for (int value = 0; value <= 0xFF; value++)
        {
            for (int offset = 0; offset <= bound; offset++)
            {
                byte original = copy[offset];
                copy[offset] = (byte)value;

                using MemoryStream corrupted = new(copy);
                ReadManagedClaim(corrupted, ref detail, out ClaimExitSite site);
                reached.Add(site);

                copy[offset] = original;
            }
        }

        foreach ((bool _, bool _, byte[] zeroed) in CliDirectoryZeroings(image))
        {
            using MemoryStream stream = new(zeroed, writable: false);
            ReadManagedClaim(stream, ref detail, out ClaimExitSite site);
            reached.Add(site);
        }

        // Constructed rather than hoped for. Round 8 found that
        // CliDirectoryBeyondOptionalHeader was reached only because this
        // assembly declares a 224-byte optional header, so a corruption to 0x7F
        // happened to land short of directory 14. An assembly with a different
        // size would have left the site unreached with this test still passing.
        foreach (int optionalSize in (int[])[2, 96, 176, 215])
        {
            using MemoryStream stream =
                new(ShortOptionalHeaderImage(optionalSize), writable: false);
            ReadManagedClaim(stream, ref detail, out ClaimExitSite site);
            reached.Add(site);
        }

        using (ThrowingStream throwing = new())
        {
            ReadManagedClaim(throwing, ref detail, out ClaimExitSite site);
            reached.Add(site);
        }

        ClaimExitSite[] all = Enum.GetValues<ClaimExitSite>();
        ClaimExitSite[] unreached = [.. all.Except(reached)];

        Assert.True(
            unreached.Length == 0,
            $"The enumerated range (bound {bound}) never reached exit " +
            $"{(unreached.Length == 1 ? "path" : "paths")} " +
            $"{string.Join(", ", unreached)} of {all.Length}, so the range does " +
            $"not exercise the whole method and a defect on an unreached path " +
            $"would not be found by either enumeration.");
    }

    /// <summary>
    /// A stream that fails the way an unreadable file fails, so the I/O handler
    /// in <c>ReadManagedClaim</c> can be reached without depending on file
    /// permissions, which vary by platform and do not constrain a root user.
    /// </summary>
    sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => 4096;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("simulated unreadable file");

        public override int Read(Span<byte> buffer) =>
            throw new IOException("simulated unreadable file");

        public override long Seek(long offset, SeekOrigin origin) => 0;

        public override void Flush()
        {
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Every way out of <c>ReadManagedClaim</c>, one member per outcome.
    ///
    /// These are names rather than numbers so the set of paths is derived from
    /// this declaration instead of being restated as a count that can drift
    /// away from it. Adding a member without wiring it to a path fails the
    /// coverage test as unreached, and wiring a path requires naming a member.
    ///
    /// A member must correspond to an outcome, not to a place in the source.
    /// An earlier revision assigned one member and then branched to two
    /// different answers, which let the common answer report the site reached
    /// while the other was never exercised -- the coverage test passed and
    /// proved less than it said. Assign the site inside each branch, after the
    /// answer is decided, so that "reached" and "answered this way" cannot come
    /// apart.
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
    /// this method that does not name itself. A future early return cannot be
    /// added silently and then go unenumerated, which is the failure mode that
    /// produced six rounds of findings.
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
            // the front door. TryMeasure_ShortDirectoryCount_StillDecodeFailed
            // pins that behaviour.
            //
            // Either field being non-zero is a claim. Reading only the RVA would
            // miss a file whose RVA is zeroed but whose size survives, which is
            // the same hole that reading only the size would leave in the other
            // direction. A file that is genuinely not managed has both fields
            // zero, so requiring both before skipping is the direction that
            // cannot hide damage.
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
    /// Enumerates every way the CLI directory's two fields can be zeroed and
    /// pins both the claim and the exit site for each.
    ///
    /// This is the branch that decides which files leave the population, so it
    /// is the laundering direction: a wrong answer here removes a file from the
    /// sweep while still reporting success. Round 7 found that neither the
    /// prefix nor the corruption enumeration could reach it. Single-byte
    /// corruption cannot zero eight bytes, and a prefix long enough to contain
    /// the directory contains the real non-zero value, so the "absent" outcome
    /// was unreachable by construction while sharing an exit site with
    /// "present" -- which meant the coverage test reported it exercised.
    ///
    /// The domain is the two fields being independently zeroed or not, so it is
    /// four cases and they are all here rather than sampled. Three of them are
    /// damage and must still claim managed: that is the asymmetry the reader
    /// above is written for, and a one-sided test would be satisfied by a
    /// reader that answered <c>Yes</c> unconditionally.
    /// </summary>
    [Fact]
    public void ReadManagedClaim_CliDirectoryFieldZeroing_LaundersNothing()
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        Assert.True(
            BinaryPrimitives.ReadUInt32LittleEndian(
                image.AsSpan(CliDirectoryFileOffset(image))) != 0,
            "The unmodified test assembly must have a non-zero CLI directory "
                + "RVA, or this test is zeroing something that was already zero.");

        List<string> wrong = [];

        foreach ((bool zeroRva, bool zeroSize, byte[] copy) in
            CliDirectoryZeroings(image))
        {
            bool bothZeroed = zeroRva && zeroSize;

            ManagedClaim expectedClaim =
                bothZeroed ? ManagedClaim.No : ManagedClaim.Yes;
            ClaimExitSite expectedSite = bothZeroed
                ? ClaimExitSite.CliDirectoryAbsent
                : ClaimExitSite.CliDirectoryPresent;

            using var stream = new MemoryStream(copy, writable: false);
            string? detail = null;
            ManagedClaim claim =
                ReadManagedClaim(stream, ref detail, out ClaimExitSite site);

            if (claim != expectedClaim || site != expectedSite)
            {
                wrong.Add(
                    $"rva zeroed={zeroRva}, size zeroed={zeroSize}: "
                        + $"expected {expectedClaim}/{expectedSite}, "
                        + $"got {claim}/{site}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "The CLI directory reader disagreed with the rule that only a file "
                + "with both fields zero makes no managed claim: "
                + string.Join("; ", wrong));
    }

    /// <summary>
    /// The file offset of the CLI data directory in a PE image.
    /// </summary>
    static int CliDirectoryFileOffset(byte[] image)
    {
        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        int optionalOffset = peOffset + 24;
        int directories = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(optionalOffset)) switch
        {
            0x10B => 96,
            0x20B => 112,
            _ => throw new InvalidOperationException(
                "The test assembly is neither PE32 nor PE32+, so this test "
                    + "cannot locate its CLI directory."),
        };

        return optionalOffset + directories + (14 * 8);
    }

    /// <summary>
    /// Every combination of the CLI directory's two fields being zeroed, which
    /// is the complete domain of that dimension in four cases.
    ///
    /// Both the coverage test and the laundering test enumerate this from here
    /// so that the dimension has one definition. If they each built their own,
    /// one could narrow without the other noticing, and the coverage test would
    /// keep reporting a path exercised that the property test no longer covered.
    /// </summary>
    static IEnumerable<(bool ZeroRva, bool ZeroSize, byte[] Image)>
        CliDirectoryZeroings(byte[] image)
    {
        int cliOffset = CliDirectoryFileOffset(image);

        foreach (bool zeroRva in (bool[])[false, true])
        {
            foreach (bool zeroSize in (bool[])[false, true])
            {
                byte[] copy = (byte[])image.Clone();

                if (zeroRva)
                {
                    copy.AsSpan(cliOffset, 4).Clear();
                }

                if (zeroSize)
                {
                    copy.AsSpan(cliOffset + 4, 4).Clear();
                }

                yield return (zeroRva, zeroSize, copy);
            }
        }
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
