#:project ../src/DotnetInspector.Sections/DotnetInspector.Sections.csproj
#:property EnablePreviewFeatures=true

using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Inspector.Findings;
using Inspector.Text;
using QuerySpace.Composition;
using QuerySpace.Rows;

// Observational post-settlement probe. SourceView construction is intentionally
// outside the measured interval and is not correctness or acquisition evidence.
if (args.Length != 3
    || args[0] is not ("direct" or "overflow")
    || !int.TryParse(args[1], out int lineCount)
    || lineCount is < 1 or > 256
    || !int.TryParse(args[2], out int iterations)
    || iterations is < 1 or > 10)
{
    Console.Error.WriteLine(
        "Usage: dotnet run tools/SourceLineOperationBenchmark.cs -c Release -- "
        + "<direct|overflow> <lines> <iterations>");
    return 2;
}

string text = string.Join(
    '\n',
    Enumerable.Range(1, lineCount).Select(
        static value => $"line {value:D4} value"));
InspectionEnvelope<SourceView>[] inspections =
[
    .. Enumerable.Range(0, iterations).Select(
        _ => CreateInspection(text)),
];
ResolvedRowQueryPlan<SourceViewLine> plan = CreatePlan();

long checksum = 0;
long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
long started = Stopwatch.GetTimestamp();
foreach (InspectionEnvelope<SourceView> inspection in inspections)
{
    checksum += args[0] == "direct"
        ? ExecuteDirect(inspection)
        : ExecuteOverflow(inspection, plan);
}
long elapsed = Stopwatch.GetTimestamp() - started;
long allocated =
    GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
GC.KeepAlive(checksum);
Console.WriteLine(
    $"{elapsed}\t{Stopwatch.Frequency}\t{allocated}\t{checksum}");
return 0;

static long ExecuteDirect(
    InspectionEnvelope<SourceView> inspection) =>
    Consume(inspection.Content.Lines);

static long ExecuteOverflow(
    InspectionEnvelope<SourceView> inspection,
    ResolvedRowQueryPlan<SourceViewLine> plan)
{
    SourceViewLineOperationAdmission admission =
        SourceViewLineOperation.AdmitRows(
            inspection,
            plan);
    SourceViewLineOperation operation =
        admission.Operation
            ?? throw new InvalidOperationException(
                $"Plan declined: {admission.DeclineReason}.");
    if (!operation.TryPull(
            continuation: null,
            finalRowCredit: int.MaxValue,
            out SourceViewLineSegment? segment)
        || segment is null
        || !segment.IsComplete)
    {
        throw new InvalidOperationException(
            "Source operation did not complete.");
    }
    return Consume(segment.Rows);
}

static long Consume(IReadOnlyList<SourceViewLine> rows)
{
    long checksum = rows.Count;
    foreach (SourceViewLine row in rows)
    {
        checksum += row.Number;
        checksum += row.Start;
        checksum += row.Content.Length;
        checksum += (int)row.Terminator;
    }
    return checksum;
}

static InspectionEnvelope<SourceView> CreateInspection(
    string text)
{
    var binding = (SourceViewBinding)Activator.CreateInstance(
        typeof(SourceViewBinding),
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        [Guid.NewGuid()],
        culture: null)!;
    var origin =
        (SourceViewOrigin.DecompiledType)Activator.CreateInstance(
            typeof(SourceViewOrigin.DecompiledType),
            nonPublic: true)!;
    var identity = new SourceViewIdentity(
        new AssemblyReferenceIdentity(
            "SourceLineOperationBenchmark",
            new Version(1, 0),
            Culture: null,
            PublicKeyToken: null),
        AssemblyResolutionProvenance.Local(
            "Source line operation benchmark"));
    MetadataTypeDefinitionName type =
        MetadataTypeDefinitionName.Create(
            "Benchmark",
            ["Document"])
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                "The benchmark Type name is invalid.");
    var request = new SourceViewRequest.Type(
        new AssemblyTypeSourceRequest(type));
    var authoredAttempt =
        new SourceViewAuthoredAttemptEvidence.Type(
            PdbTypeSourceOutcome.PortablePdbUnavailable,
            PortablePdbAvailable: false,
            new FindingInspection<string>(
                new FindingInspection<string>.Complete([])),
            Document: null,
            ChecksumVerification: null);
    var document = new DecodedTextDocument(text);
    ConstructorInfo constructor =
        typeof(SourceView).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(SourceViewBinding),
                typeof(SourceViewOrigin),
                typeof(SourceViewLanguage),
                typeof(SourceViewIdentity),
                typeof(SourceViewRequest),
                typeof(SourceViewAuthoredAttemptEvidence),
                typeof(SourceViewTypeMappingEvidence),
                typeof(DecodedTextDocument),
            ],
            modifiers: null)
        ?? throw new InvalidOperationException(
            "SourceView constructor was not found.");
    var view = (SourceView)constructor.Invoke(
        [
            binding,
            origin,
            SourceViewLanguage.CSharp,
            identity,
            request,
            authoredAttempt,
            null,
            document,
        ]);
    return new(
        view,
        new InspectionShare.NonProjectable(
            "source-performance-probe",
            "Observational local performance probe."));
}

static ResolvedRowQueryPlan<SourceViewLine> CreatePlan()
{
    RowQueryResolutionResult<SourceViewLine> resolution =
        SourceViewLineVocabulary.Resolve(
            RowQueryIntent.Create(
                [],
                baselineOrder: null,
                RowSelectionIntent<
                    RowQueryOrderIntent>.Create([])));
    return resolution.Plan
        ?? throw new InvalidOperationException(
            "Source line plan did not resolve.");
}
