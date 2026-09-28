using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.PerformanceOracles;
using DotnetInspector.Queries;
using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using InertText;
using NLinq;

// analyzer-audit <pinvoke|async|pointer> check|time [--rounds N] [--budget-ms N] [--tsv <path>] <dll>...
//   The equivalent performance scorecard: Exists, Count, and Rows (the closings
//   the Planner has), every column producing the Planner's row shape.
// analyzer-audit <pinvoke|async|pointer> loop <closing> <column> <seconds> <dll>
//   Runs one cell in a loop, for sampling.
// analyzer-audit units <dll>...
//   Methods per assembly, and methods in gate scope, for fixed/per-unit fits.
if (args.Length >= 1 && args[0] == "units")
{
    foreach (string path in args[1..])
    {
        using var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
        MetadataReader reader = pe.GetMetadataReader();
        int all = 0, scoped = 0, inTypes = 0;
        foreach (TypeDefinitionHandle t in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(t);
            bool typeIn = Gate.TypeInScope(reader, type);
            foreach (MethodDefinitionHandle m in type.GetMethods())
            {
                all++;
                if (!typeIn)
                    continue;
                inTypes++;
                if (Gate.MethodInScope(reader, reader.GetMethodDefinition(m)))
                    scoped++;
            }
        }

        Console.WriteLine($"{Path.GetFileNameWithoutExtension(path)}\t{all}\t{inTypes}\t{scoped}");
    }

    return 0;
}

if (args.Length < 2 || args[0] is not ("pinvoke" or "async" or "pointer"))
{
    Console.Error.WriteLine("usage: analyzer-audit pinvoke|async|pointer check|time|loop ...");
    return 2;
}

Analyzer analyzer = args[0] switch
{
    "pinvoke" => Analyzer.PInvoke,
    "async" => Analyzer.Async,
    _ => Analyzer.Pointer,
};
ScorecardClosing[] closings = [ScorecardClosing.Exists, ScorecardClosing.Count, ScorecardClosing.Rows];
var columns = Columns.For(analyzer);

if (args[1] == "loop")
{
    ScorecardClosing closing = Enum.Parse<ScorecardClosing>(args[2], ignoreCase: true);
    ScorecardColumn<PEReader, AuditRow> column = columns.Single(c => c.Name == args[3]);
    double seconds = double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
    using var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(args[5])));
    long calls = 0;
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed.TotalSeconds < seconds)
    {
        _ = column.Answer(closing, pe);
        calls++;
    }

    Console.WriteLine($"{calls} calls, {clock.Elapsed.TotalMicroseconds / calls:0.0} µs/call");
    return 0;
}

if (!ScorecardCommandLine.TryParse(args[1..], out ScorecardOptions? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
ScorecardColumn<PEReader, AuditRow> oracle = columns.Single(c => c.Name == "NLinq");
IReadOnlyList<ScorecardAsset<PEReader>> assets = MethodPopulation.LoadAssets(options!.Assets);
try
{
    ScorecardCheck check = Scorecard.Check(assets, oracle, columns, r => r.ToString(), AuditRow.Content, closings);
    foreach (ScorecardMismatch mismatch in check.Mismatches)
        Console.WriteLine($"mismatch\t{mismatch.Asset}\t{shape.Label(mismatch.Closing)}\t{mismatch.Column}\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    Console.WriteLine($"# {args[0]}: {check.Compared} compared, {check.Mismatches.Count} mismatches");
    if (!check.Agrees || options.Command == ScorecardCommand.Check)
        return check.Agrees ? 0 : 1;

    IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(assets, columns, options.Timing, p => Console.Error.WriteLine(p), closings);
    if (options.TsvPath is { } tsv)
    {
        using StreamWriter writer = File.CreateText(tsv);
        Scorecard.WriteTsv(cells, writer);
    }

    Console.Write(Scorecard.Report(cells, oracle.Name, shape));
    return 0;
}
finally
{
    foreach (ScorecardAsset<PEReader> asset in assets)
        asset.Asset.Dispose();
}

enum Analyzer
{
    PInvoke,
    Async,
    Pointer,
}

/// <summary>
/// The compared content of a row: every field except the traversal ordinal
/// and token, which Old does not carry.
/// </summary>
sealed record AuditRow(string Name, string DeclaringType, string Namespace, string Signature, string? ReturnType, string? Selector, string? Module)
{
    public static IEqualityComparer<AuditRow> Content { get; } = EqualityComparer<AuditRow>.Default;

    public override string ToString() => $"{DeclaringType}::{Name}|{Signature}";
}

/// <summary>The gate's population: MethodClassificationScope, exactly.</summary>
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
/// Per-call state for the equivalent columns: the identity budgets the
/// Planner keeps per execution, and the lazily built state-machine index.
/// </summary>
sealed class CallState(MetadataReader reader)
{
    public readonly MetadataReader Reader = reader;
    public int IdentityDecodeFailures;
    public int IdentityWork = MetadataSafetyPolicy.MaxClassificationScanWorkChars;
    StateMachineRelationshipIndex? _index;

    public StateMachineRelationshipIndex Index => _index ??= StateMachineRelationshipIndex.Create(Reader);
}

/// <summary>The analyzers' predicates, the same tests the Planner applies after its gate.</summary>
static class Tests
{
    const MethodImplAttributes RuntimeAsyncFlag = (MethodImplAttributes)0x2000;

    public static MethodClassification? Classify(Analyzer analyzer, CallState state, MethodDefinitionHandle handle, MethodDefinition method)
    {
        bool pinvoke = Gate.IsPInvoke(method);
        switch (analyzer)
        {
            case Analyzer.PInvoke:
                return pinvoke ? MethodClassification.PInvoke : null;
            case Analyzer.Pointer:
                return !pinvoke && HasPointer(method) ? MethodClassification.Unsafe : null;
            default:
                if (pinvoke)
                    return null;
                if ((method.ImplAttributes & RuntimeAsyncFlag) != 0)
                    return MethodClassification.RuntimeAsync;
                return state.Index.GetByKickoff(handle) switch
                {
                    StateMachineRelationshipResult.Resolved
                    {
                        Relationship.Kind: StateMachineClaimKind.ClassicAsync or StateMachineClaimKind.AsyncIterator,
                    } => MethodClassification.StateMachineAsync,
                    StateMachineRelationshipResult.Rejected => throw new BadImageFormatException("The state-machine relationship was rejected."),
                    _ => null,
                };
        }
    }

    // The legacy async test the earlier scorecard's NLinq column used.
    public static MethodClassification? LegacyAsync(MetadataReader reader, MethodDefinition method) =>
        Gate.IsPInvoke(method) ? null : MethodClassificationScanner.ClassifyAsyncMethod(reader, method);

    static bool HasPointer(MethodDefinition method)
    {
        try
        {
            MethodSignature<bool> signature = method.DecodeSignature(PointerProvider.Instance, null);
            if (signature.ReturnType)
                return true;
            foreach (bool parameter in signature.ParameterTypes)
            {
                if (parameter)
                    return true;
            }

            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// The Planner's row, built the way its projection builds it: identity from
/// MethodRowProjection, every field wrapped in InertString. The Planner also
/// charges an identity budget and decodes identity strings through a second,
/// budgeted MetadataReader; those are measured separately.
/// </summary>
static class Rows
{
    public static ClassifiedMethodRow Project(
        CallState state,
        TypeDefinitionHandle typeHandle,
        TypeDefinition type,
        MethodDefinitionHandle handle,
        MethodDefinition method,
        MethodClassification classification,
        int ordinal)
    {
        MetadataReader reader = state.Reader;
        MethodAnchorInfo? anchor = MethodRowProjection.TryCreateMethodIdentity(
            reader, typeHandle, method, ref state.IdentityDecodeFailures, ref state.IdentityWork);
        string name = reader.GetString(method.Name);
        string signature = MethodRowProjection.FormatSignatureOrFallback(reader, type, method, name, anchor);
        string declaring = MethodRowProjection.FormatDeclaringTypeName(reader, typeHandle);
        string ns = reader.GetString(type.Namespace);
        InertString? module = classification == MethodClassification.PInvoke
            && MethodRowProjection.GetPInvokeModuleName(reader, handle) is { } m
                ? Inert(m)
                : null;
        return new ClassifiedMethodRow(
            MetadataTokens.GetToken(handle),
            ordinal,
            Inert(name),
            Inert(declaring),
            Inert(ns),
            Inert(signature),
            classification,
            module,
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

    public static AuditRow Audit(ClassifiedMethodRow row) =>
        new(row.MethodName.ToString(), row.DeclaringType.ToString(), row.Namespace.ToString(), row.Signature.ToString(),
            row.ReturnType?.ToString(), row.Anchor?.StableSelector.ToString(), row.ModuleName?.ToString());

    public static AuditRow Audit(ClassifiedMethodInfo row) =>
        new(row.MethodName, row.DeclaringType, row.Namespace ?? "", row.Signature,
            row.ReturnType, row.Anchor?.StableSelector, row.ModuleName);
}

/// <summary>A lazy view: rows are converted to AuditRow only when compared, never inside a timed call.</summary>
sealed class View<T>(IReadOnlyList<T> rows, Func<T, AuditRow> map) : IReadOnlyList<AuditRow>
{
    public AuditRow this[int index] => map(rows[index]);

    public int Count => rows.Count;

    public IEnumerator<AuditRow> GetEnumerator()
    {
        foreach (T row in rows)
            yield return map(row);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A method definition with the traversal ordinal the Planner assigns: its position among methods of in-scope types.</summary>
readonly record struct Unit(TypeDefinitionHandle TypeHandle, TypeDefinition Type, MethodDefinitionHandle Handle, MethodDefinition Method, int Ordinal);

static class Columns
{
    public static ScorecardColumn<PEReader, AuditRow>[] For(Analyzer analyzer)
    {
        var columns = new List<ScorecardColumn<PEReader, AuditRow>>
        {
            new("Old", (closing, pe) => Old(analyzer, closing, pe)),
            new("LINQ", (closing, pe) => Linq(analyzer, closing, pe)),
            new("NLinq", (closing, pe) => NLinqColumn(analyzer, closing, pe)),
            new("Planner", (closing, pe) => Planner(analyzer, closing, pe)),
            new("Planner exec", (closing, pe) => PlannerExec(analyzer, closing, pe)),
        };
        if (analyzer == Analyzer.Async)
            columns.Insert(3, new("NLinq legacy async", (closing, pe) => NLinqLegacyAsync(closing, pe)));
        return [.. columns];
    }

    static ScorecardAnswer<AuditRow> Old(Analyzer analyzer, ScorecardClosing closing, PEReader pe)
    {
        Func<MethodClassification, bool> keep = analyzer switch
        {
            Analyzer.PInvoke => c => c == MethodClassification.PInvoke,
            Analyzer.Pointer => c => c == MethodClassification.Unsafe,
            _ => c => c is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync,
        };
        List<ClassifiedMethodInfo> rows = MethodClassificationScanner.Scan(pe).Where(m => keep(m.Classification)).ToList();
        return closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<AuditRow>.OfExists(rows.Count != 0),
            ScorecardClosing.Count => ScorecardAnswer<AuditRow>.OfCount(rows.Count),
            _ => ScorecardAnswer<AuditRow>.OfRows(new View<ClassifiedMethodInfo>(rows, Rows.Audit)),
        };
    }

    static ScorecardAnswer<AuditRow> Linq(Analyzer analyzer, ScorecardClosing closing, PEReader pe)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var state = new CallState(reader);
        IEnumerable<(Unit Unit, MethodClassification Class)> selected = reader.TypeDefinitions
            .Select(t => (Handle: t, Type: reader.GetTypeDefinition(t)))
            .Where(t => Gate.TypeInScope(reader, t.Type))
            .SelectMany(t => t.Type.GetMethods().Select(m => (t.Handle, t.Type, Method: m)))
            .Select((m, i) => new Unit(m.Handle, m.Type, m.Method, reader.GetMethodDefinition(m.Method), i))
            .Where(u => Gate.MethodInScope(reader, u.Method))
            .Select(u => (Unit: u, Class: Tests.Classify(analyzer, state, u.Handle, u.Method)))
            .Where(u => u.Class is not null)
            .Select(u => (u.Unit, u.Class!.Value));
        return closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<AuditRow>.OfExists(selected.Any()),
            ScorecardClosing.Count => ScorecardAnswer<AuditRow>.OfCount(selected.Count()),
            _ => ScorecardAnswer<AuditRow>.OfRows(new View<ClassifiedMethodRow>(
                selected.Select(s => Rows.Project(state, s.Unit.TypeHandle, s.Unit.Type, s.Unit.Handle, s.Unit.Method, s.Class, s.Unit.Ordinal)).ToList(),
                Rows.Audit)),
        };
    }

    static ScorecardAnswer<AuditRow> NLinqColumn(Analyzer analyzer, ScorecardClosing closing, PEReader pe)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var state = new CallState(reader);
        var selected = new MethodDefinitionRows(reader).Where<MethodDefinitionRows, MethodDefinitionRow, Select>(new Select(analyzer, state));
        switch (closing)
        {
            case ScorecardClosing.Exists:
                return ScorecardAnswer<AuditRow>.OfExists(
                    new MethodDefinitionRows(reader).Any<MethodDefinitionRows, MethodDefinitionRow, Select>(new Select(analyzer, state)));
            case ScorecardClosing.Count:
                return ScorecardAnswer<AuditRow>.OfCount(
                    selected.CountFold<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow>());
            default:
                List<ClassifiedMethodRow> rows = selected
                    .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, MethodDefinitionRow, ClassifiedMethodRow, Project>(new Project(analyzer, state))
                    .ToList<Map<MethodDefinitionRow, ClassifiedMethodRow, Filter<MethodDefinitionRow, MethodDefinitionRows, Select>, Project>, ClassifiedMethodRow>();
                return ScorecardAnswer<AuditRow>.OfRows(new View<ClassifiedMethodRow>(rows, Rows.Audit));
        }
    }

    static ScorecardAnswer<AuditRow> NLinqLegacyAsync(ScorecardClosing closing, PEReader pe)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var state = new CallState(reader);
        var selected = new MethodDefinitionRows(reader).Where<MethodDefinitionRows, MethodDefinitionRow, LegacyAsyncSelect>(default);
        switch (closing)
        {
            case ScorecardClosing.Exists:
                return ScorecardAnswer<AuditRow>.OfExists(
                    new MethodDefinitionRows(reader).Any<MethodDefinitionRows, MethodDefinitionRow, LegacyAsyncSelect>(default));
            case ScorecardClosing.Count:
                return ScorecardAnswer<AuditRow>.OfCount(
                    selected.CountFold<Filter<MethodDefinitionRow, MethodDefinitionRows, LegacyAsyncSelect>, MethodDefinitionRow>());
            default:
                List<ClassifiedMethodRow> rows = selected
                    .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, LegacyAsyncSelect>, MethodDefinitionRow, ClassifiedMethodRow, LegacyAsyncProject>(new LegacyAsyncProject(state))
                    .ToList<Map<MethodDefinitionRow, ClassifiedMethodRow, Filter<MethodDefinitionRow, MethodDefinitionRows, LegacyAsyncSelect>, LegacyAsyncProject>, ClassifiedMethodRow>();
                return ScorecardAnswer<AuditRow>.OfRows(new View<ClassifiedMethodRow>(rows, Rows.Audit));
        }
    }

    static readonly Dictionary<(Analyzer, ScorecardClosing), WorkDescription> s_plans = [];

    static ProducerDeclaration<ClosedQueryResult<ClassifiedMethodRow>> ProducerFor(Analyzer analyzer) => analyzer switch
    {
        Analyzer.PInvoke => PInvokeAnalyzer.Instance,
        Analyzer.Async => AsyncAnalyzer.Instance,
        _ => PointerSignatureAnalyzer.Instance,
    };

    // The execution alone: planned once outside the timed region, no image
    // admission, no answer wrapping. Planner minus this is the query layer.
    static ScorecardAnswer<AuditRow> PlannerExec(Analyzer analyzer, ScorecardClosing closing, PEReader pe)
    {
        if (!s_plans.TryGetValue((analyzer, closing), out WorkDescription? plan))
        {
            ProducerTerminal terminal = closing switch
            {
                ScorecardClosing.Exists => ProducerTerminal.Exists,
                ScorecardClosing.Count => ProducerTerminal.Complete,
                _ => ProducerTerminal.Rows,
            };
            plan = ((ProducerPlanResult.Accepted)ProducerPlanner.Plan([new ProducerRequest(ProducerFor(analyzer), terminal)])).Description;
            s_plans[(analyzer, closing)] = plan;
        }

        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>> result =
            MethodDefinitionExecution.Execute(plan, "MethodClassification", pe).ResultOf(ProducerFor(analyzer));
        ClosedQueryResult<ClassifiedMethodRow> value = result.Value
            ?? throw new InvalidOperationException($"The execution did not complete: {result.Outcome}");
        return closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<AuditRow>.OfExists(value.Exists),
            ScorecardClosing.Count => ScorecardAnswer<AuditRow>.OfCount(value.Count),
            _ => ScorecardAnswer<AuditRow>.OfRows(new View<ClassifiedMethodRow>(value.Rows, Rows.Audit)),
        };
    }

    static ScorecardAnswer<AuditRow> Planner(Analyzer analyzer, ScorecardClosing closing, PEReader pe)
    {
        var question = new ClassificationQuestion(
            analyzer switch
            {
                Analyzer.PInvoke => MethodClassificationAnalyzer.PInvoke,
                Analyzer.Async => MethodClassificationAnalyzer.Async,
                _ => MethodClassificationAnalyzer.PointerSignature,
            },
            closing switch
            {
                ScorecardClosing.Exists => ClassificationClosing.Exists,
                ScorecardClosing.Count => ClassificationClosing.Count,
                _ => ClassificationClosing.Rows,
            },
            ClassifiedRowOrder.Metadata);
        ClassificationAnswer answer = MethodClassificationQuery.Execute(pe, [question]).AnswerTo(question);
        return answer switch
        {
            ClassificationAnswer.Exists exists => ScorecardAnswer<AuditRow>.OfExists(exists.Value),
            ClassificationAnswer.Count count => ScorecardAnswer<AuditRow>.OfCount(count.Value),
            ClassificationAnswer.Rows rows => ScorecardAnswer<AuditRow>.OfRows(new View<ClassifiedMethodRow>(rows.Methods, Rows.Audit)),
            _ => throw new InvalidOperationException($"The planner did not answer: {answer}"),
        };
    }

    // NLinq's selection over MethodDefinitionRows: the gate, then the test.
    // Rows carry the method's row number as their ordinal: the same int field
    // the Planner fills, excluded from the compared content.
    struct Select(Analyzer analyzer, CallState state) : IFunc<MethodDefinitionRow, bool>
    {
        public readonly bool Invoke(MethodDefinitionRow row) =>
            Gate.TypeInScope(row.Reader, row.Type)
            && Gate.MethodInScope(row.Reader, row.Method)
            && Tests.Classify(analyzer, state, row.MethodHandle, row.Method) is not null;
    }

    struct Project(Analyzer analyzer, CallState state) : IFunc<MethodDefinitionRow, ClassifiedMethodRow>
    {
        public readonly ClassifiedMethodRow Invoke(MethodDefinitionRow row) =>
            Rows.Project(state, row.TypeHandle, row.Type, row.MethodHandle, row.Method,
                analyzer switch
                {
                    // As the Planner's projections: P/Invoke and pointer rows
                    // carry a fixed class; the async projection classifies again.
                    Analyzer.PInvoke => MethodClassification.PInvoke,
                    Analyzer.Pointer => MethodClassification.Unsafe,
                    _ => Tests.Classify(analyzer, state, row.MethodHandle, row.Method)!.Value,
                },
                MetadataTokens.GetRowNumber(row.MethodHandle) - 1);
    }

    struct LegacyAsyncSelect : IFunc<MethodDefinitionRow, bool>
    {
        public readonly bool Invoke(MethodDefinitionRow row) =>
            Gate.TypeInScope(row.Reader, row.Type)
            && Gate.MethodInScope(row.Reader, row.Method)
            && Tests.LegacyAsync(row.Reader, row.Method) is not null;
    }

    struct LegacyAsyncProject(CallState state) : IFunc<MethodDefinitionRow, ClassifiedMethodRow>
    {
        public readonly ClassifiedMethodRow Invoke(MethodDefinitionRow row) =>
            Rows.Project(state, row.TypeHandle, row.Type, row.MethodHandle, row.Method,
                Tests.LegacyAsync(row.Reader, row.Method)!.Value,
                MetadataTokens.GetRowNumber(row.MethodHandle) - 1);
    }
}

sealed class PointerProvider : ISignatureTypeProvider<bool, object?>
{
    public static PointerProvider Instance { get; } = new();
    public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;
    public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;
    public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => false;
    public bool GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    public bool GetSZArrayType(bool elementType) => elementType;
    public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;
    public bool GetByReferenceType(bool elementType) => elementType;
    public bool GetPointerType(bool elementType) => true;
    public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments) => genericType || typeArguments.Any(a => a);
    public bool GetGenericTypeParameter(object? genericContext, int index) => false;
    public bool GetGenericMethodParameter(object? genericContext, int index) => false;
    public bool GetFunctionPointerType(MethodSignature<bool> signature) => true;
    public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => unmodifiedType;
    public bool GetPinnedType(bool elementType) => elementType;
}
