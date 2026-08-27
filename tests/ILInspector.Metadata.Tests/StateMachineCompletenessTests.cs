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

        // IgnoreInaccessible keeps one unreadable subdirectory from aborting a
        // sweep of a shared or system-wide corpus.
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        foreach (string path in
            Directory.EnumerateFiles(root!, "*.dll", enumeration))
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

        Assert.True(
            undecodable.Count == 0,
            $"""
            {undecodable.Count} managed assembly/assemblies failed to decode, so
            their state machines could not be evaluated at all. That is a decode
            failure, not a clean sweep, and it is reported rather than skipped.

            {surveyed}

            Undecodable:
              {string.Join("\n  ", undecodable.Take(25))}
            """);

        Assert.True(assemblies != 0, $"No managed assemblies found. {surveyed}");
        Assert.True(
            totals.Structural != 0,
            $"No structural state machines found, so this sweep proves nothing. "
                + surveyed);

        Assert.True(
            totals.Rejected == 0,
            $"""
            {totals.Rejected} structural state machine(s) were refused
            authentication, across {assemblies} assemblies ({totals.Structural}
            structural, {totals.Resolved} resolved, {totals.Absent} absent).

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

            {surveyed}

            Offenders:
              {string.Join("\n  ", offenders.Take(25))}
            """);
    }

    /// <summary>
    /// Why a corpus entry did or did not contribute a measurement. Every file
    /// the sweep touches lands in exactly one of these, so nothing is silently
    /// dropped.
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
                MetadataReader reader;
                try
                {
                    if (!pe.HasMetadata)
                        return CorpusOutcome.NotManaged;

                    reader = pe.GetMetadataReader();
                }
                catch (BadImageFormatException)
                {
                    // PrefetchEntireImage defers format validation, so a file
                    // that is not a PE at all surfaces here rather than from the
                    // constructor. Not having readable headers means this is not
                    // a managed assembly, not that a managed assembly failed to
                    // decode. Real package caches contain such files.
                    return CorpusOutcome.NotManaged;
                }

                try
                {
                    report = Measure(reader);
                    return CorpusOutcome.Measured;
                }
                catch (BadImageFormatException ex)
                {
                    // A MetadataReader was obtained, so this genuinely is a
                    // managed assembly, and its metadata still failed to read.
                    // That is a decode failure and must not be silently skipped.
                    detail = ex.Message;
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
