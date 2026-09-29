using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.PerformanceOracles;
using DotnetInspector.Queries;
using ILInspector.Analysis.Classification;
using ILInspector.Metadata;
using ILInspector.Metadata.LegacyOracles;

// method-classification-scorecard async|pinvoke check <assembly>...
// method-classification-scorecard async|pinvoke time [--rounds N] [--budget-ms N] [--tsv <path>] <assembly>...
//   Old is the retired scanner (dated oracle) filtered as the CLI filtered it;
//   Planner is MethodClassificationQuery. Rows compare by method name in
//   traversal order, because the populations spell declaring types and
//   signatures differently.
if (args.Length == 0 || args[0] is not ("async" or "pinvoke"))
{
    Console.Error.WriteLine("usage: async|pinvoke " + ScorecardCommandLine.Usage);
    return 2;
}

bool async = args[0] == "async";
if (!ScorecardCommandLine.TryParse(args[1..], out ScorecardOptions? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
ScorecardColumn<PEReader, MethodTextRow> oracle = async
    ? MethodPopulation<AsyncSelection>.NLinqColumn(shape)
    : MethodPopulation<PInvokeSelection>.NLinqColumn(shape);
ScorecardColumn<PEReader, MethodTextRow>[] columns =
[
    Columns.Old(async, shape),
    async ? MethodPopulation<AsyncSelection>.LinqColumn(shape) : MethodPopulation<PInvokeSelection>.LinqColumn(shape),
    oracle,
    Columns.Planner(async, shape),
];

IReadOnlyList<ScorecardAsset<PEReader>> assets = MethodPopulation.LoadAssets(options!.Assets);
try
{
    ScorecardCheck check = Scorecard.Check(assets, oracle, columns, static row => row.Name, new NameComparer());
    foreach (ScorecardMismatch mismatch in check.Mismatches)
        Console.WriteLine($"mismatch\t{mismatch.Asset}\t{shape.Label(mismatch.Closing)}\t{mismatch.Column}\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    Console.WriteLine($"# {args[0]}: {check.Compared} compared, {check.Mismatches.Count} mismatches, {check.WindowFailures.Count} strict-window failures");
    if (!check.Agrees)
        return 1;
    foreach (string asset in check.WindowFailures)
        Console.WriteLine($"window-failed\t{asset}\t{shape.Label(ScorecardClosing.Window)}");
    if (options.Command == ScorecardCommand.Check)
        return 0;

    IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(assets, columns, options.Timing, progress => Console.Error.WriteLine(progress));
    if (options.TsvPath is { } tsvPath)
    {
        using StreamWriter tsv = File.CreateText(tsvPath);
        Scorecard.WriteTsv(cells, tsv);
    }

    Console.Write(Scorecard.Report(cells, oracle.Name, shape));
    return 0;
}
finally
{
    foreach (ScorecardAsset<PEReader> asset in assets)
        asset.Asset.Dispose();
}

/// <summary>
/// Public, non-P/Invoke async methods, by the analysis the analyzers apply:
/// the runtime-async flag, then an in-place, memoized match of each custom
/// attribute's type against the compiler async state-machine attributes. Only
/// the read machinery differs from the Planner's.
/// </summary>
struct AsyncSelection : IMethodSelection
{
    const MethodImplAttributes RuntimeAsyncFlag = (MethodImplAttributes)0x2000;

    public readonly bool IsSelected(MetadataReader reader, TypeDefinition type, MethodDefinition method) =>
        default(PublicMethodSelection).IsSelected(reader, type, method)
        && (method.Attributes & MethodAttributes.PinvokeImpl) == 0
        && ((method.ImplAttributes & RuntimeAsyncFlag) != 0 || InPlaceAsyncAttribute.Any(reader, method));
}

/// <summary>The gate's in-place attribute-type match, memoized per attribute type as the gate does.</summary>
static class InPlaceAsyncAttribute
{
    static readonly MetadataTypeNameTarget AsyncStateMachine = new(KnownAttributeNames.AsyncStateMachineAttribute);
    static readonly MetadataTypeNameTarget AsyncIteratorStateMachine = new(KnownAttributeNames.AsyncIteratorStateMachineAttribute);

    [ThreadStatic] static MetadataReader? s_reader;
    [ThreadStatic] static Dictionary<EntityHandle, bool>? s_types;

    public static bool Any(MetadataReader reader, MethodDefinition method)
    {
        if (!ReferenceEquals(s_reader, reader))
        {
            s_reader = reader;
            s_types = [];
        }

        foreach (CustomAttributeHandle handle in method.GetCustomAttributes())
        {
            EntityHandle constructor = reader.GetCustomAttribute(handle).Constructor;
            EntityHandle attributeType = constructor.Kind switch
            {
                HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
                HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
                _ => default,
            };
            if (attributeType.IsNil)
                continue;
            if (!s_types!.TryGetValue(attributeType, out bool matches))
            {
                matches = MetadataTypeNameMatch.Matches(reader, attributeType, AsyncStateMachine) == MetadataTypeNameMatchResult.Match
                    || MetadataTypeNameMatch.Matches(reader, attributeType, AsyncIteratorStateMachine) == MetadataTypeNameMatchResult.Match;
                s_types[attributeType] = matches;
            }

            if (matches)
                return true;
        }

        return false;
    }
}

/// <summary>Public P/Invoke methods.</summary>
struct PInvokeSelection : IMethodSelection
{
    public readonly bool IsSelected(MetadataReader reader, TypeDefinition type, MethodDefinition method) =>
        default(PublicMethodSelection).IsSelected(reader, type, method)
        && (method.Attributes & MethodAttributes.PinvokeImpl) != 0;
}

/// <summary>Rows agree when their method names agree, in order.</summary>
sealed class NameComparer : IEqualityComparer<MethodTextRow>
{
    public bool Equals(MethodTextRow x, MethodTextRow y) => string.Equals(x.Name, y.Name, StringComparison.Ordinal);

    public int GetHashCode(MethodTextRow row) => StringComparer.Ordinal.GetHashCode(row.Name);
}

static class Columns
{
    /// <summary>The retired path: classify every method into rows, then filter and close.</summary>
    public static ScorecardColumn<PEReader, MethodTextRow> Old(bool async, ScorecardShape shape) =>
        new("Old", (closing, pe) =>
        {
            List<MethodTextRow> rows = [.. LegacyMethodClassificationScanner.Scan(pe)
                .Where(m => async
                    ? m.Classification is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync
                    : m.Classification == MethodClassification.PInvoke)
                .Select(static m => new MethodTextRow(m.MethodName, m.DeclaringType, m.Signature))];
            return Close(closing, rows, shape);
        });

    /// <summary>
    /// The enablement: Exists and Count are closings; Head, Tail, and the
    /// window read the Rows closing, which the query does not push down.
    /// </summary>
    public static ScorecardColumn<PEReader, MethodTextRow> Planner(bool async, ScorecardShape shape)
    {
        MethodClassificationAnalyzer analyzer = async ? MethodClassificationAnalyzer.Async : MethodClassificationAnalyzer.PInvoke;
        return new("Planner", (closing, pe) =>
        {
            switch (closing)
            {
                case ScorecardClosing.Exists:
                {
                    ClassificationQuestion question = new(analyzer, ClassificationClosing.Exists);
                    var exists = (ClassificationAnswer.Exists)MethodClassificationQuery.Execute(pe, [question]).AnswerTo(question);
                    return ScorecardAnswer<MethodTextRow>.OfExists(exists.Value);
                }

                case ScorecardClosing.Count:
                {
                    ClassificationQuestion question = new(analyzer, ClassificationClosing.Count);
                    var count = (ClassificationAnswer.Count)MethodClassificationQuery.Execute(pe, [question]).AnswerTo(question);
                    return ScorecardAnswer<MethodTextRow>.OfCount(count.Value);
                }

                default:
                {
                    ClassificationQuestion question = new(analyzer, ClassificationClosing.Rows);
                    var rows = (ClassificationAnswer.Rows)MethodClassificationQuery.Execute(pe, [question]).AnswerTo(question);
                    List<MethodTextRow> text = [.. rows.Methods.Select(static row =>
                        new MethodTextRow(row.MethodName.ToString(), row.DeclaringType.ToString(), row.Signature.ToString()))];
                    return Close(closing, text, shape);
                }
            }
        });
    }

    static ScorecardAnswer<MethodTextRow> Close(ScorecardClosing closing, List<MethodTextRow> rows, ScorecardShape shape) =>
        closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<MethodTextRow>.OfExists(rows.Count > 0),
            ScorecardClosing.Count => ScorecardAnswer<MethodTextRow>.OfCount(rows.Count),
            ScorecardClosing.Head => ScorecardAnswer<MethodTextRow>.OfRows(rows.Take(shape.N).ToList()),
            ScorecardClosing.Tail => ScorecardAnswer<MethodTextRow>.OfRows(rows.TakeLast(shape.N).ToList()),
            ScorecardClosing.Rows => ScorecardAnswer<MethodTextRow>.OfRows(rows),
            ScorecardClosing.Window => rows.Count >= shape.WindowSkip + shape.WindowTake
                ? ScorecardAnswer<MethodTextRow>.OfRows(rows.GetRange(shape.WindowSkip, shape.WindowTake))
                : ScorecardAnswer<MethodTextRow>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };
}
