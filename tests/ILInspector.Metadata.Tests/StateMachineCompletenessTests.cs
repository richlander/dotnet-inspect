using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Fixtures =
    ILInspector.Metadata.StateMachineFixtures.StateMachineFixtures;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Implementation evidence for C1, C3, and C6 in
/// <c>docs/design/state-machine-relationship-index.md#completeness</c>.
/// Structural async machines are discovered independently from raw metadata
/// and compared with the index's keyed classification.
/// </summary>
public sealed class StateMachineCompletenessTests
{
    const string CorpusVariable = "SM_COMPLETENESS_CORPUS";

    const string CliDirectoryPresentDetail =
        "the CLI directory is present but carries no metadata";

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
    /// C1/C6 evidence over deterministic own-build assemblies. The independent
    /// population must be non-empty and every member must resolve.
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
    /// C3 implementation evidence: one whole-module failure must replace every
    /// structural async machine's classification with the same rejection.
    /// </summary>
    [Fact]
    public void GlobalFailure_RejectsEveryStructuralAsyncStateMachine()
    {
        using FileStream stream =
            File.OpenRead(typeof(Fixtures).Assembly.Location);
        using var pe = new PEReader(
            stream,
            PEStreamOptions.PrefetchEntireImage);
        MetadataReader reader = pe.GetMetadataReader();
        StateMachineRelationshipIndex index =
            StateMachineRelationshipIndex.Create(
                reader,
                relationshipBudget: 1);
        var relationships =
            Assert.IsType<StateMachineRelationshipsResult.Rejected>(
                index.Relationships);
        int structural = 0;

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            if (!ImplementsAsyncStateMachine(reader, handle))
                continue;

            structural++;
            var rejected =
                Assert.IsType<StateMachineRelationshipResult.Rejected>(
                    index.GetByStateMachine(handle));
            Assert.Same(relationships.Failure, rejected.Failure);
        }

        Assert.True(
            structural > 1,
            "The fixture must carry multiple structural async machines so this "
                + "gate can detect a partial whole-module rejection.");
        Assert.Equal(
            StateMachineRelationshipFailureKind.BudgetExceeded,
            relationships.Failure.Kind);
    }

    /// <summary>
    /// Broadens C1/C6 evidence to build-derived, machine-bearing neighbours.
    /// Non-vacuity checks keep the population wider than the two specimens and
    /// cross-check every skip against SRM.
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

                // A skipped neighbour is independently checked before leaving
                // the measured population.
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

            // Width evidence must come from a non-specimen that carries a
            // structural async machine.
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
                + "past the two hand-picked assemblies.");
        Assert.True(
            offenders.Count == 0,
            $"""
            {offenders.Count} file(s) in this test's own output directory could
            not be measured consistently or did not authenticate every structural
            state machine they carry. {examined} assemblies were measured and
            {notManaged} file(s) were classified as non-managed.

            {Truncated(offenders)}
            """);
    }

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
    /// C1/C6 evidence for the core library's same-module attribute encoding,
    /// which own-build consumers do not exercise.
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
    /// Cross-checks the independent header reader before a neighbour is skipped.
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
    /// Optional observational sweep over an external corpus. It reports
    /// rejected structural async machines and population holes but tolerates
    /// <c>Absent</c>, so it is not by itself a complete C1 or C3 gate.
    /// </summary>
    [Fact]
    public void Corpus_NoStructuralStateMachineIsClaimedThenRejected()
    {
        string? root = Environment.GetEnvironmentVariable(CorpusVariable);
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(root),
            $"Set {CorpusVariable} to a directory to run the corpus sweep.");

        // An explicitly configured but missing corpus is a failure, not a skip.
        Assert.True(
            Directory.Exists(root),
            $"{CorpusVariable} is set to '{root}', which is not a directory. "
                + "Unset it to skip the corpus sweep.");

        string? problems = SweepProblems(root!, out string surveyed);
        Assert.True(problems is null, $"{surveyed}\n\n{problems}");
    }

    /// <summary>
    /// Runs the production-shaped sweep for both the opt-in corpus gate and its
    /// deterministic controls, returning every accumulated problem.
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

                // Count genuine non-managed files without treating them as failures.
                case CorpusOutcome.NotManaged:
                    notManaged++;
                    continue;

                // Environmental holes remain visible in the report.
                case CorpusOutcome.Inaccessible:
                    inaccessible.Add($"{Path.GetFileName(path)}: {detail}");
                    continue;

                // A managed claim that cannot be decoded fails the sweep.
                case CorpusOutcome.DecodeFailed:
                    undecodable.Add($"{Path.GetFileName(path)}: {detail}");
                    continue;

                // Indeterminate input is neither a managed claim nor a safe skip.
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

        // Collect every failure class so one hole cannot hide the others.
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
                {unclassifiable.Count} corpus file{(unclassifiable.Count == 1 ? "" : "s")} could not be classified
                as managed or non-managed. The raw-header oracle could not decide,
                and SRM did not expose managed metadata that would settle the
                question. Reporting {(unclassifiable.Count == 1 ? "it" : "them")} as non-managed would assert
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

                A refusal has two cause classes, and the failure kinds listed
                below do not reliably distinguish them. Either a claim involving
                the type failed identity or role authentication, or the module
                failed to index at all, in which case every structural machine in
                it reports Rejected regardless of whether anything claimed it
                (see #4833).

                A known cause of the first is trimming a must-be-present role.
                ClassicAsync admits an absent SetStateMachine support role, but
                AsyncIterator still requires it. A trimmed async iterator may
                therefore be refused when that role is removed.

                {Truncated(offenders)}
                """);
        }

        return problems.Count == 0 ? null : string.Join("\n\n", problems);
    }

    /// <summary>How a specimen is damaged before the sweep sees it.</summary>
    public enum DamageKind
    {
        /// <summary>
        /// Preserves the CLI claim while making metadata undecodable.
        /// </summary>
        MetadataSignature,

        /// <summary>
        /// Preserves the headers while truncating their target.
        /// </summary>
        Truncated,

        /// <summary>
        /// Stops before managed status is decidable.
        /// </summary>
        HeaderTruncated,
    }

    /// <summary>
    /// Ensures managed claims and indeterminate files cannot leave the measured
    /// population silently. A valid neighbour prevents an incidental
    /// zero-measurement failure from masking the damaged file's accounting.
    /// </summary>
    [Theory]
    [InlineData(
        DamageKind.MetadataSignature,
        "1 managed assemblies measured, 0 non-managed skipped, "
            + "0 unclassifiable, 0 unreadable.")]
    [InlineData(
        DamageKind.Truncated,
        "1 managed assemblies measured, 0 non-managed skipped, "
            + "0 unclassifiable, 0 unreadable.")]
    [InlineData(
        DamageKind.HeaderTruncated,
        "1 managed assemblies measured, 0 non-managed skipped, "
            + "1 unclassifiable, 0 unreadable.")]
    public void Sweep_DamagedAssembly_IsNeverSilentlyDropped(
        DamageKind damage,
        string expectedSurvey)
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
            if (damage == DamageKind.HeaderTruncated)
            {
                Assert.Contains(
                    "1 corpus file could not be classified",
                    problems);
                Assert.Contains(
                    "as managed or non-managed.",
                    problems);
            }
            AssertSurvey(expectedSurvey, surveyed);
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
        }
    }

    /// <summary>
    /// Confirms a genuine non-managed file is a counted skip, not a failure.
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
            File.WriteAllBytes(Path.Combine(corpus, "_._"), []);

            string? problems = SweepProblems(corpus, out string surveyed);

            Assert.True(problems is null, $"{surveyed}\n\n{problems}");
            AssertSurvey(
                "1 managed assemblies measured, 2 non-managed skipped, "
                    + "0 unclassifiable, 0 unreadable.",
                surveyed);
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
        }
    }

    /// <summary>
    /// Makes every fixture claim fail its role check and proves the sweep rejects
    /// that decodable population. This is a per-claim negative control, not C3
    /// whole-module-failure evidence.
    /// </summary>
    [Fact]
    public void Sweep_RejectedStateMachine_FailsTheSweep()
    {
        string corpus = NewCorpusDirectory();
        try
        {
            byte[] image = File.ReadAllBytes(typeof(Fixtures).Assembly.Location);
            string damaged = Path.Combine(corpus, "unauthenticatable.dll");
            File.WriteAllBytes(damaged, Unauthenticatable(image));

            Assert.Equal(
                CorpusOutcome.Measured,
                TryMeasure(damaged, out CompletenessReport report, out _));
            Assert.NotEqual(0, report.Structural);
            Assert.Equal(0, report.Resolved);
            Assert.Equal(report.Structural, report.Rejected);
            Assert.Equal("Unresolved", Assert.Single(report.RejectionKinds));

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
    /// Confirms candidate discovery is independent of extension spelling or
    /// presence.
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
            AssertSurvey(
                "1 managed assemblies measured, 0 non-managed skipped, "
                    + "0 unclassifiable, 0 unreadable.",
                surveyed);
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
        }
    }

    /// <summary>
    /// Requires ordinary and linked subdirectories to be traversed while a link
    /// cycle terminates by directory identity.
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

            // Exact accounting distinguishes cycle recognition from eventual
            // operating-system refusal at a symlink-depth limit.
            AssertSurvey(
                "1 managed assemblies measured, 0 non-managed skipped, "
                    + "0 unclassifiable, 0 unreadable.",
                surveyed);
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
            Directory.Delete(offRoot, recursive: true);
        }
    }

    static void AssertSurvey(string expected, string surveyed) =>
        Assert.Equal(expected, surveyed);

    static byte[] Damage(byte[] image, DamageKind kind)
    {
        if (kind == DamageKind.HeaderTruncated)
        {
            // Managed status is undecidable this early in the header.
            return image[..1];
        }

        if (kind == DamageKind.Truncated)
        {
            // Preserve the CLI directory while removing its target.
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
    /// Renames <c>MoveNext</c> in place so claims remain decodable but fail
    /// required execution-role authentication.
    /// </summary>
    static byte[] Unauthenticatable(byte[] image)
    {
        byte[] copy = (byte[])image.Clone();
        ReadOnlySpan<byte> role = "MoveNext\0"u8;
        int at = copy.AsSpan().IndexOf(role);
        Assert.True(
            at >= 0,
            "The specimen carries no MoveNext to rename, so it cannot "
                + "produce a refused claim.");

        // The heap deduplicates strings, so one edit reaches every reference.
        copy[at + role.Length - 2] = (byte)'Z';
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
    /// Renders a bounded list with explicit omission accounting.
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
    /// Walks all names without extension filtering, records traversal failures,
    /// and follows each linked directory identity once. Recording an unreadable
    /// directory is not covered by a portable automated control.
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
                    // Identity prevents a link cycle from revisiting an ancestor.
                    if (visited.Add(DirectoryIdentity(entry)))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    // Names are not metadata evidence; the header oracle decides.
                    yield return entry;
                    continue;
                }

                // A reparse point without Directory may still target a directory.
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
                    // A dangling or file link is not traversable as a directory.
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

                // File and dangling links still receive an explicit outcome.
                yield return entry;
            }
        }
    }

    /// <summary>
    /// Uses a link's final target for cycle detection, or its full path when no
    /// target can be resolved.
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
    /// Explicit accounting for every corpus entry that opens. A developer-chosen
    /// corpus containing a FIFO can still block in <see cref="File.OpenRead"/>;
    /// that trusted-input limitation is not covered here.
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
        /// Header damage prevents either managed or non-managed classification.
        /// </summary>
        Unclassifiable,
    }

    /// <summary>
    /// Separates environmental, classification, and decode outcomes around
    /// <see cref="Measure"/> so callers cannot silently drop an entry.
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
            // The raw-header oracle decides whether a managed claim exists
            // before SRM, whose parse failures cannot answer that question.
            ManagedClaim claim = ReadManagedClaim(stream, ref detail, out ClaimExitSite claimExit);
            if (claim == ManagedClaim.Unreadable)
            {
                return CorpusOutcome.Inaccessible;
            }

            // Only a positive non-managed answer can become a clean skip;
            // indeterminate and future answers remain visible.
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
                // Preserve the oracle's classification for any nonfatal parse failure.
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
                    // SRM is lazy; its failure cannot override the raw-header
                    // oracle's managed, non-managed, or indeterminate answer.
                    detail = $"{ex.GetType().Name}: {ex.Message}";
                    return undecodable;
                }

                if (!hasMetadata)
                {
                    // Diagnostics distinguish a positive CLI claim from an
                    // indeterminate header.
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
                    // Every nonfatal metadata failure remains a visible decode failure.
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
        /// Positively non-managed or an empty CLI directory.
        /// </summary>
        No,

        /// <summary>A non-empty CLI data directory: the file claims to be managed.</summary>
        Yes,

        /// <summary>
        /// Headers end or become malformed before managed status is decidable.
        /// </summary>
        Indeterminate,

        /// <summary>The headers could not be read for environmental reasons.</summary>
        Unreadable,
    }

    /// <summary>
    /// Identifies the raw-header decision used in failure diagnostics. Definite
    /// assignment requires every return to name a site; correspondence between
    /// a site and its outcome is not independently gated.
    /// </summary>
    enum ClaimExitSite
    {
        /// <summary>An empty file makes no managed claim.</summary>
        EmptyFile,

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
        /// The four bytes at the validated PE offset are not "PE\0\0".
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
        /// Both CLI directory fields are zero, so the file makes no managed claim.
        /// </summary>
        CliDirectoryAbsent,
    }

    static ManagedClaim ReadManagedClaim(Stream stream, ref string? detail) =>
        ReadManagedClaim(stream, ref detail, out _);

    /// <summary>
    /// Reads only enough PE structure to decide whether a CLI directory is
    /// claimed. The <c>out</c> parameter makes every exit identify its decision
    /// site for diagnostics.
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

            // An empty file and a present wrong byte prove non-PE; a missing
            // second signature byte leaves managed status indeterminate.
            if (read == 0)
            {
                exitSite = ClaimExitSite.EmptyFile;
                return ManagedClaim.No;
            }

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

            // Match SRM's measured behavior by reading directory 14 even when
            // NumberOfRvaAndSizes is shorter. Either non-zero field is treated
            // as a claim. Neither edge is independently gated because normal
            // compiler, linker, and trimmer output does not emit those shapes.
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
    /// Measures a deterministic build output without corpus-tolerant catches.
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
    /// Discovers the interface by namespace and name without product trust
    /// policy. TypeReference and TypeDefinition encodings are covered;
    /// TypeSpecification remains an explicit, unverified detector limitation.
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
