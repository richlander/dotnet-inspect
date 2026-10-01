using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.PerformanceOracles;
using DotnetInspector.Queries;
using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.Metadata.LegacyOracles;
using InertText;
using NLinq;

// method-classification-scorecard async|pinvoke check <assembly>...
// method-classification-scorecard async|pinvoke time [--rounds N] [--budget-ms N] [--tsv <path>] <assembly>...
//
// Old is the retired scanner (dated oracle) filtered as the CLI filtered it.
// LINQ, NLinq, and Planner apply the identical analysis and produce the
// identical row: the analyzers' gate and tests, with the gate's per-execution
// attribute-match memos, and the Planner's projection (identity through
// MethodRowProjection, every field inert). Only the read machinery differs.
// The equivalent columns follow #8870's audit harness (tools/AnalyzerAudit on
// its scratch branch). Rows compare by method name in traversal order.
if (args.Length == 0 || args[0] is not ("async" or "pinvoke"))
{
    Console.Error.WriteLine("usage: async|pinvoke " + ScorecardCommandLine.Usage);
    return 2;
}

Population population = args[0] == "async" ? Population.Async : Population.PInvoke;
if (!ScorecardCommandLine.TryParse(args[1..], out ScorecardOptions? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
ScorecardColumn<PEReader, string>[] columns = Columns.For(population, shape);
ScorecardColumn<PEReader, string> oracle = columns.Single(static column => column.Name == "NLinq");

IReadOnlyList<ScorecardAsset<PEReader>> assets = MethodPopulation.LoadAssets(options!.Assets);
try
{
    ScorecardCheck check = Scorecard.Check(assets, oracle, columns, static name => name);
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

enum Population
{
    Async,
    PInvoke,
}

/// <summary>The gate's population: <c>MethodClassificationScope</c>, exactly.</summary>
static class Gate
{
    public static bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
        !reader.StringComparer.StartsWith(type.Name, "<");

    public static bool MethodInScope(MetadataReader reader, MethodDefinition method)
    {
        if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            return false;
        StringHandle name = method.Name;
        return !(reader.StringComparer.StartsWith(name, "get_")
            || reader.StringComparer.StartsWith(name, "set_")
            || reader.StringComparer.StartsWith(name, "add_")
            || reader.StringComparer.StartsWith(name, "remove_"));
    }

    public static bool IsPInvoke(MethodDefinition method) => (method.Attributes & MethodAttributes.PinvokeImpl) != 0;
}

/// <summary>
/// Per-call state: the identity budgets the Planner keeps per execution, and
/// the attribute-match memos the gate keeps per execution.
/// </summary>
sealed class CallState(MetadataReader reader)
{
    public readonly MetadataReader Reader = reader;
    public int IdentityDecodeFailures;
    public int IdentityWork = MetadataSafetyPolicy.MaxClassificationScanWorkChars;
    public readonly Dictionary<(EntityHandle, MetadataTypeNameTarget), bool> Constructors = [];
    public readonly Dictionary<(EntityHandle, MetadataTypeNameTarget), bool> Types = [];
}

/// <summary>
/// The analyzers' tests, as the Planner applies them after its gate: the
/// P/Invoke flag; the runtime-async flag; and the in-place attribute type
/// match through <see cref="MetadataTypeNameMatch"/>, one target at a time,
/// memoized per constructor and per attribute type.
/// </summary>
static class Tests
{
    const MethodImplAttributes RuntimeAsyncFlag = (MethodImplAttributes)0x2000;
    static readonly MetadataTypeNameTarget AsyncStateMachine = new(KnownAttributeNames.AsyncStateMachineAttribute);
    static readonly MetadataTypeNameTarget AsyncIteratorStateMachine = new(KnownAttributeNames.AsyncIteratorStateMachineAttribute);

    public static bool IsRuntimeAsync(MethodDefinition method) => (method.ImplAttributes & RuntimeAsyncFlag) != 0;

    public static MethodClassification? Classify(Population population, CallState state, MethodDefinition method)
    {
        if (population == Population.PInvoke)
            return Gate.IsPInvoke(method) ? MethodClassification.PInvoke : null;
        if (Gate.IsPInvoke(method))
            return null;
        if (IsRuntimeAsync(method))
            return MethodClassification.RuntimeAsync;
        return HasAttributeOfType(state, method, AsyncStateMachine) || HasAttributeOfType(state, method, AsyncIteratorStateMachine)
            ? MethodClassification.StateMachineAsync
            : null;
    }

    static bool HasAttributeOfType(CallState state, MethodDefinition method, MetadataTypeNameTarget target)
    {
        MetadataReader reader = state.Reader;
        foreach (CustomAttributeHandle handle in method.GetCustomAttributes())
        {
            EntityHandle constructor = reader.GetCustomAttribute(handle).Constructor;
            if (!state.Constructors.TryGetValue((constructor, target), out bool matches))
            {
                EntityHandle parent = constructor.Kind switch
                {
                    HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
                    HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
                    _ => default,
                };
                matches = !parent.IsNil && TypeMatches(state, parent, target);
                state.Constructors[(constructor, target)] = matches;
            }

            if (matches)
                return true;
        }

        return false;
    }

    static bool TypeMatches(CallState state, EntityHandle type, MetadataTypeNameTarget target)
    {
        if (state.Types.TryGetValue((type, target), out bool known))
            return known;
        bool matches = MetadataTypeNameMatch.Matches(state.Reader, type, target) switch
        {
            MetadataTypeNameMatchResult.Match => true,
            MetadataTypeNameMatchResult.NoMatch => false,
            _ => throw new BadImageFormatException("An attribute type's name could not be matched."),
        };
        state.Types[(type, target)] = matches;
        return matches;
    }
}

/// <summary>The Planner's row, built as its projection builds it.</summary>
static class Rows
{
    public static ClassifiedMethodRow Project(CallState state, MethodDefinitionRow row, MethodClassification classification)
    {
        MetadataReader reader = state.Reader;
        MethodAnchorInfo? anchor = MethodRowProjection.TryCreateMethodIdentity(
            reader, row.TypeHandle, row.Method, ref state.IdentityDecodeFailures, ref state.IdentityWork);
        string name = reader.GetString(row.Method.Name);
        string signature = MethodRowProjection.FormatSignatureOrFallback(reader, row.Type, row.Method, name, anchor);
        string declaring = MethodRowProjection.FormatDeclaringTypeName(reader, row.TypeHandle);
        string ns = reader.GetString(row.Type.Namespace);
        string? module = classification == MethodClassification.PInvoke
            ? MethodRowProjection.GetPInvokeModuleName(reader, row.MethodHandle)
            : null;
        return new ClassifiedMethodRow(
            MetadataTokens.GetToken(row.MethodHandle),
            MetadataTokens.GetRowNumber(row.MethodHandle) - 1,
            Inert(name),
            Inert(declaring),
            Inert(ns),
            Inert(signature),
            classification,
            module is null ? null : Inert(module),
            anchor is null
                ? null
                : new MethodRowAnchor(
                    anchor.Anchor,
                    Inert(anchor.Anchor.StableSelector),
                    Inert(anchor.Anchor.CanonicalSignature),
                    Inert(anchor.Anchor.TypeFullName),
                    Inert(anchor.Anchor.MemberName)),
            anchor is null ? null : Inert(anchor.ReturnType));
    }

    static InertString Inert(string text) => new(TextPolicy.Field, text);
}

/// <summary>A lazy view: rows become names only when compared, never inside a timed call.</summary>
sealed class View<T>(IReadOnlyList<T> rows, Func<T, string> name) : IReadOnlyList<string>
{
    public string this[int index] => name(rows[index]);

    public int Count => rows.Count;

    public IEnumerator<string> GetEnumerator()
    {
        foreach (T row in rows)
            yield return name(row);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

static class Columns
{
    public static ScorecardColumn<PEReader, string>[] For(Population population, ScorecardShape shape) =>
    [
        new("Old", (closing, pe) => Old(population, closing, pe, shape)),
        new("LINQ", (closing, pe) => Linq(population, closing, pe, shape)),
        new("NLinq", (closing, pe) => NLinqColumn(population, closing, pe, shape)),
        new("Planner", (closing, pe) => Planner(population, closing, pe, shape)),
    ];

    static ScorecardAnswer<string> Answer<T>(ScorecardClosing closing, List<T> rows, Func<T, string> name, ScorecardShape shape) =>
        closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<string>.OfExists(rows.Count > 0),
            ScorecardClosing.Count => ScorecardAnswer<string>.OfCount(rows.Count),
            ScorecardClosing.Head => ScorecardAnswer<string>.OfRows(new View<T>(rows.GetRange(0, Math.Min(shape.N, rows.Count)), name)),
            ScorecardClosing.Tail => ScorecardAnswer<string>.OfRows(new View<T>(rows.GetRange(Math.Max(0, rows.Count - shape.N), Math.Min(shape.N, rows.Count)), name)),
            ScorecardClosing.Rows => ScorecardAnswer<string>.OfRows(new View<T>(rows, name)),
            ScorecardClosing.Window => rows.Count >= shape.WindowSkip + shape.WindowTake
                ? ScorecardAnswer<string>.OfRows(new View<T>(rows.GetRange(shape.WindowSkip, shape.WindowTake), name))
                : ScorecardAnswer<string>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };

    /// <summary>The retired path: classify every method into rows, then filter and close.</summary>
    static ScorecardAnswer<string> Old(Population population, ScorecardClosing closing, PEReader pe, ScorecardShape shape)
    {
        List<ClassifiedMethodInfo> rows = [.. LegacyMethodClassificationScanner.Scan(pe)
            .Where(m => population == Population.Async
                ? m.Classification is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync
                : m.Classification == MethodClassification.PInvoke)];
        return Answer(closing, rows, static m => m.MethodName, shape);
    }

    /// <summary>
    /// An idiomatic streaming System.Linq pipeline over the same gate and
    /// tests. Row-returning closings project only the rows they consume;
    /// Exists and Count never construct the projection.
    /// </summary>
    static ScorecardAnswer<string> Linq(Population population, ScorecardClosing closing, PEReader pe, ScorecardShape shape)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var state = new CallState(reader);
        IEnumerable<MethodDefinitionRow> selected = reader.TypeDefinitions
            .Select(handle => (Handle: handle, Type: reader.GetTypeDefinition(handle)))
            .Where(type => Gate.TypeInScope(reader, type.Type))
            .SelectMany(type => type.Type.GetMethods().Select(method => new MethodDefinitionRow(reader, type.Handle, type.Type, method, reader.GetMethodDefinition(method))))
            .Where(row => Gate.MethodInScope(reader, row.Method))
            .Where(row => Tests.Classify(population, state, row.Method) is not null);
        return closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<string>.OfExists(selected.Any()),
            ScorecardClosing.Count => ScorecardAnswer<string>.OfCount(selected.Count()),
            ScorecardClosing.Head => ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(
                selected
                    .Take(shape.N)
                    .Select(row => Rows.Project(state, row, ClassOf(population, row.Method)))
                    .ToList(),
                Name)),
            ScorecardClosing.Tail => ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(
                selected
                    .TakeLast(shape.N)
                    .Select(row => Rows.Project(state, row, ClassOf(population, row.Method)))
                    .ToList(),
                Name)),
            ScorecardClosing.Rows => ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(
                selected
                    .Select(row => Rows.Project(state, row, ClassOf(population, row.Method)))
                    .ToList(),
                Name)),
            ScorecardClosing.Window => selected
                    .Skip(shape.WindowSkip)
                    .Take(shape.WindowTake)
                    .Select(row => Rows.Project(state, row, ClassOf(population, row.Method)))
                    .ToList() is { } window
                && window.Count == shape.WindowTake
                    ? ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(window, Name))
                    : ScorecardAnswer<string>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };
    }

    /// <summary>The NLinq oracle over the method-definition source: the gate, the test, the projection, then the closing.</summary>
    static ScorecardAnswer<string> NLinqColumn(Population population, ScorecardClosing closing, PEReader pe, ScorecardShape shape)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var state = new CallState(reader);
        var select = new Select(population, state);
        if (closing == ScorecardClosing.Exists)
        {
            return ScorecardAnswer<string>.OfExists(
                new MethodDefinitionRows(reader).Any<MethodDefinitionRows, MethodDefinitionRow, Select>(select));
        }

        var selected =
            new MethodDefinitionRows(reader)
                .Where<MethodDefinitionRows, MethodDefinitionRow, Select>(
                    select);
        if (closing == ScorecardClosing.Count)
        {
            return ScorecardAnswer<string>.OfCount(
                selected.CountFold<
                    Filter<MethodDefinitionRow, MethodDefinitionRows, Select>,
                    MethodDefinitionRow>());
        }

        var project = new Project(population, state);
        switch (closing)
        {
            case ScorecardClosing.Head:
            {
                var rows = selected
                    .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow, ClassifiedMethodRow, Project>(project)
                    .Take<Map<MethodDefinitionRow, ClassifiedMethodRow, Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, Project>, ClassifiedMethodRow>(shape.N);
                var list = new List<ClassifiedMethodRow>(shape.N);
                while (true)
                {
                    ClassifiedMethodRow row = rows.TryGetNext(out bool hasMore);
                    if (!hasMore)
                        return ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(list, Name));
                    list.Add(row);
                }
            }

            case ScorecardClosing.Tail:
            {
                List<MethodDefinitionRow> last = selected
                    .TakeLast<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow>(shape.N);
                var list = new List<ClassifiedMethodRow>(last.Count);
                foreach (MethodDefinitionRow row in last)
                    list.Add(project.Invoke(row));
                return ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(list, Name));
            }

            case ScorecardClosing.Rows:
                return ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(selected
                    .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow, ClassifiedMethodRow, Project>(project)
                    .ToList<Map<MethodDefinitionRow, ClassifiedMethodRow, Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, Project>, ClassifiedMethodRow>(), Name));

            case ScorecardClosing.Window:
                // Skip before Select, so skipped rows are never projected.
                return selected
                        .Skip<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow>(shape.WindowSkip)
                        .Select<SkipEnumerator<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow>, MethodDefinitionRow, ClassifiedMethodRow, Project>(project)
                        .TryTakeExactly<Map<MethodDefinitionRow, ClassifiedMethodRow, SkipEnumerator<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow>, Project>, ClassifiedMethodRow>(shape.WindowTake, out List<ClassifiedMethodRow> window)
                    ? ScorecardAnswer<string>.OfRows(new View<ClassifiedMethodRow>(window, Name))
                    : ScorecardAnswer<string>.OfWindowFailure();

            default:
                throw new ArgumentOutOfRangeException(nameof(closing));
        }
    }

    /// <summary>
    /// The enablement: Exists, Count, and Head are source closings. Tail and
    /// the strict window still read the Rows closing.
    /// </summary>
    static ScorecardAnswer<string> Planner(Population population, ScorecardClosing closing, PEReader pe, ScorecardShape shape)
    {
        MethodClassificationAnalyzer analyzer = population == Population.Async
            ? MethodClassificationAnalyzer.Async
            : MethodClassificationAnalyzer.PInvoke;
        ClassificationQuestion question = closing == ScorecardClosing.Head
            ? ClassificationQuestion.Head(analyzer, shape.N)
            : new(
                analyzer,
                closing switch
                {
                    ScorecardClosing.Exists => ClassificationClosing.Exists,
                    ScorecardClosing.Count => ClassificationClosing.Count,
                    _ => ClassificationClosing.Rows,
                });
        return MethodClassificationQuery.Execute(pe, [question]).AnswerTo(question) switch
        {
            ClassificationAnswer.Exists exists => ScorecardAnswer<string>.OfExists(exists.Value),
            ClassificationAnswer.Count count => ScorecardAnswer<string>.OfCount(count.Value),
            ClassificationAnswer.Rows rows => Answer(closing, [.. rows.Methods], Name, shape),
            ClassificationAnswer answer => throw new InvalidOperationException($"The planner did not answer: {answer}"),
        };
    }

    static string Name(ClassifiedMethodRow row) => row.MethodName.ToString();

    static MethodClassification ClassOf(Population population, MethodDefinition method) =>
        population == Population.PInvoke ? MethodClassification.PInvoke
        : Tests.IsRuntimeAsync(method) ? MethodClassification.RuntimeAsync
        : MethodClassification.StateMachineAsync;

    struct Select(Population population, CallState state) : IFunc<MethodDefinitionRow, bool>
    {
        public readonly bool Invoke(MethodDefinitionRow row) =>
            Gate.TypeInScope(row.Reader, row.Type)
            && Gate.MethodInScope(row.Reader, row.Method)
            && Tests.Classify(population, state, row.Method) is not null;
    }

    struct Project(Population population, CallState state) : IFunc<MethodDefinitionRow, ClassifiedMethodRow>
    {
        // As the Planner's projections: a selected row's class follows from
        // the population and the runtime flag.
        public readonly ClassifiedMethodRow Invoke(MethodDefinitionRow row) =>
            Rows.Project(state, row, ClassOf(population, row.Method));
    }
}
