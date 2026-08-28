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
    /// The property that held across every real assembly measured to date: a
    /// state machine is never claimed by an attribute and then refused. Sweeping
    /// 23,829 assemblies (NuGet cache plus shared framework) produced 145,413
    /// claims and 145,413 authenticated relationships, so any
    /// <c>Rejected</c> here is a genuine finding rather than corpus noise.
    ///
    /// <c>Absent</c> is reported but deliberately not asserted: reference
    /// assemblies retain the nested machine type while stripping the private
    /// kickoff, and F# emits no state-machine attribute at all. Both are
    /// legitimate, so asserting on them would make this test track the shape of
    /// whatever directory it was pointed at.
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

                if (IsAssemblyExtension(Path.GetExtension(entry.AsSpan())))
                {
                    yield return entry;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    // An ordinary file that is not named like an assembly. This
                    // is the only entry the sweep skips without accounting for
                    // it, and it is the only one it can skip safely.
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
                    // Not a directory at all: a dangling link, or a link to a
                    // file that is not named like an assembly. Nothing is hidden.
                    walkable = false;
                }

                if (walkable && visited.Add(DirectoryIdentity(entry)))
                {
                    pending.Push(entry);
                }
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
    /// Whether a corpus file's extension marks it as an assembly to measure.
    ///
    /// The filtering is explicit rather than delegated to a
    /// <c>Directory.GetFiles(dir, "*.dll")</c> pattern, because that pattern's
    /// case sensitivity follows the platform: on Linux it silently skips
    /// <c>*.DLL</c>. Both round-3 reviewers demonstrated the same hole
    /// independently -- a corpus holding a valid <c>good.dll</c> beside a
    /// corrupt <c>BROKEN.DLL</c> swept green, and renaming the second file to
    /// lowercase turned the same corpus red. A completeness gate cannot decide
    /// what it covers by way of a platform default, so the comparison is written
    /// out where it can be read.
    ///
    /// <c>.exe</c> is included for the same reason: a managed executable is an
    /// assembly, and skipping one is the same silent omission in a different
    /// spelling.
    /// </summary>
    static bool IsAssemblyExtension(ReadOnlySpan<char> extension) =>
        extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);

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
    /// Enumeration must not decide what the corpus covers by way of a platform
    /// default. <c>Directory.GetFiles(dir, "*.dll")</c> matches case-sensitively
    /// on Linux, so a corpus holding a corrupt <c>BROKEN.DLL</c> swept green
    /// while that file was never opened — the same silent omission an
    /// unreadable directory used to cause, in a different spelling.
    /// </summary>
    [Fact]
    public void EnumerateCandidates_MatchesExtensionCaseInsensitively()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"sm-case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            string nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);

            foreach (string name in new[] { "lower.dll", "UPPER.DLL", "app.exe" })
            {
                File.WriteAllBytes(Path.Combine(root, name), []);
            }

            foreach (string name in new[] { "Mixed.Dll", "APP.EXE", "notes.txt" })
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
                new[] { "APP.EXE", "Mixed.Dll", "UPPER.DLL", "app.exe", "lower.dll" },
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
    /// assembly, so every early-exit path in <c>ReadManagedClaim</c> is reached
    /// by some length in the range. The theory above is kept even though this
    /// subsumes it: it names which structure each interesting length sits at, so
    /// a failure there says what broke, while a failure here says only where.
    /// </summary>
    [Fact]
    public void TryMeasure_NoPrefixOfAManagedAssemblyIsEverNotManaged()
    {
        byte[] image = File.ReadAllBytes(
            typeof(StateMachineCompletenessTests).Assembly.Location);

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        int bound = Math.Min(image.Length - 1, peOffset + 512);

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
    /// space; this enumerates it, so no judgement is exercised about which
    /// offsets are worth trying.
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

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        int bound = Math.Min(image.Length - 1, peOffset + 512);

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
            ManagedClaim claim = ReadManagedClaim(stream, ref detail);
            if (claim == ManagedClaim.Unreadable)
            {
                return CorpusOutcome.Inaccessible;
            }

            // A file that claims to be managed and will not decode is a decode
            // failure at every seam below, and must fail the sweep. A file whose
            // headers could not be classified and then will not decode is not a
            // skip either: nothing established that it was unmanaged, so calling
            // it NotManaged would assert something no one measured.
            CorpusOutcome undecodable = claim switch
            {
                ManagedClaim.Yes => CorpusOutcome.DecodeFailed,
                ManagedClaim.Indeterminate => CorpusOutcome.Unclassifiable,
                _ => CorpusOutcome.NotManaged,
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
                    detail ??= "the CLI directory is present but carries no metadata";
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
    static ManagedClaim ReadManagedClaim(Stream stream, ref string? detail)
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
                return ManagedClaim.No;
            }

            if (read >= 2 && dos[1] != (byte)'Z')
            {
                return ManagedClaim.No;
            }

            if (read < 2)
            {
                return ManagedClaim.Indeterminate;
            }

            if (read < dos.Length)
            {
                return ManagedClaim.Indeterminate;
            }

            int peOffset = BinaryPrimitives.ReadInt32LittleEndian(dos[0x3C..]);
            if (peOffset < 0 || peOffset > stream.Length - 24)
            {
                return ManagedClaim.Indeterminate;
            }

            stream.Position = peOffset;

            // PE signature (4) followed by the COFF header (20).
            Span<byte> coff = stackalloc byte[24];
            if (stream.ReadAtLeast(coff, coff.Length, throwOnEndOfStream: false) < coff.Length
                || coff[0] != (byte)'P'
                || coff[1] != (byte)'E'
                || coff[2] != 0
                || coff[3] != 0)
            {
                return ManagedClaim.Indeterminate;
            }

            // A real optional header is at most a few hundred bytes. A wild value
            // means this is not a PE worth believing, not something to allocate for.
            int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(coff[^4..]);
            if (optionalSize is < 2 or > 1024)
            {
                return ManagedClaim.Indeterminate;
            }

            byte[] optional = new byte[optionalSize];
            if (stream.ReadAtLeast(optional, optionalSize, throwOnEndOfStream: false) < optionalSize)
            {
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
                return ManagedClaim.Indeterminate;
            }

            int cli = directories + (CliDirectoryIndex * 8);
            if (optionalSize < cli + 8)
            {
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
            return BinaryPrimitives.ReadUInt32LittleEndian(entry) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]) != 0
                ? ManagedClaim.Yes
                : ManagedClaim.No;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException)
        {
            detail = ex.Message;
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
