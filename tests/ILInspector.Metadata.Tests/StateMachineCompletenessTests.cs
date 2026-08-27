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
                default:
                    undecodable.Add($"{Path.GetFileName(path)}: {detail}");
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
                + $"non-managed skipped, {inaccessible.Count} unreadable.";

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
    /// Yields every <c>*.dll</c> beneath <paramref name="root"/>, recording any
    /// directory it could not read into <paramref name="inaccessible"/>.
    ///
    /// The obvious spelling — <see cref="Directory.EnumerateFiles(string, string, EnumerationOptions)"/>
    /// with <c>IgnoreInaccessible</c> — is wrong for a gate. It keeps one
    /// unreadable subdirectory from aborting the sweep, which is why it was
    /// reached for, but it does so by omitting that subtree silently. A corpus
    /// whose interesting assemblies sit under a directory the test cannot read
    /// then sweeps green while proving nothing about them. Walking explicitly
    /// keeps the sweep robust and the hole visible, which is the property that
    /// actually matters here.
    /// </summary>
    static IEnumerable<string> EnumerateCandidates(
        string root,
        List<string> inaccessible)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count != 0)
        {
            string directory = pending.Pop();

            string[] subdirectories;
            string[] files;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (Exception ex)
                when (ex is UnauthorizedAccessException or IOException)
            {
                inaccessible.Add($"{directory}{Path.DirectorySeparatorChar} ({ex.Message})");
                continue;
            }

            foreach (string subdirectory in subdirectories)
            {
                pending.Push(subdirectory);
            }

            foreach (string file in files)
            {
                if (IsAssemblyExtension(Path.GetExtension(file.AsSpan())))
                {
                    yield return file;
                }
            }
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
            PEReader pe;
            try
            {
                pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
            }
            catch (BadImageFormatException)
            {
                return CorpusOutcome.NotManaged;
            }
            catch (Exception ex)
                when (ex is UnauthorizedAccessException or IOException)
            {
                detail = ex.Message;
                return CorpusOutcome.Inaccessible;
            }

            using (pe)
            {
                // The seam between "not managed" and "managed but undecodable"
                // is here, and it used to sit one step too late. HasMetadata
                // reads the PE headers and the CLI directory entry; if that
                // throws, or reports no CLI directory, the file is not a managed
                // assembly and skipping it is right. Once it reports true the
                // file claims to be managed, so any later failure is a decode
                // failure and must fail the sweep rather than be laundered into
                // NotManaged.
                //
                // Measured, not assumed: corrupting the "BSJB" metadata
                // signature of a real assembly leaves HasMetadata true and makes
                // GetMetadataReader throw "Invalid COR20 header signature". A
                // single catch spanning both calls classified that damaged
                // managed assembly as NotManaged and swept green.
                bool hasMetadata;
                try
                {
                    hasMetadata = pe.HasMetadata;
                }
                catch (BadImageFormatException)
                {
                    // PrefetchEntireImage defers format validation, so a file
                    // that is not a PE at all surfaces here rather than from the
                    // constructor. Real package caches contain such files.
                    return CorpusOutcome.NotManaged;
                }

                if (!hasMetadata)
                {
                    return CorpusOutcome.NotManaged;
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
