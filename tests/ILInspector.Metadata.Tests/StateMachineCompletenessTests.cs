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
        Assert.SkipWhen(
            !Directory.Exists(root),
            $"{CorpusVariable} is not a directory: {root}");

        var totals = new CompletenessReport();
        var offenders = new List<string>();
        int assemblies = 0;

        foreach (string path in
            Directory.EnumerateFiles(root!, "*.dll", SearchOption.AllDirectories))
        {
            CompletenessReport report;
            try
            {
                report = Measure(path);
            }
            catch (BadImageFormatException)
            {
                continue;
            }
            catch (IOException)
            {
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

        Assert.NotEqual(0, assemblies);
        Assert.NotEqual(0, totals.Structural);

        Assert.True(
            totals.Rejected == 0,
            $"""
            {totals.Rejected} structural state machine(s) were claimed by an
            attribute and then refused authentication, across {assemblies}
            assemblies ({totals.Structural} structural, {totals.Resolved}
            resolved, {totals.Absent} absent).

            A known cause is trimming: ILLink removes SetStateMachine, which both
            ClassicAsync and AsyncIterator require, so every async claim in a
            trimmed assembly is refused (see #4827). If this corpus contains
            trimmed output that is expected rather than a regression.

            Offenders:
              {string.Join("\n  ", offenders.Take(25))}
            """);
    }

    /// <summary>
    /// Classifies every TypeDef that structurally implements
    /// <c>IAsyncStateMachine</c> against the index's own verdict for that type.
    /// </summary>
    static CompletenessReport Measure(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
        var report = new CompletenessReport();
        if (!pe.HasMetadata)
            return report;

        MetadataReader reader = pe.GetMetadataReader();
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
