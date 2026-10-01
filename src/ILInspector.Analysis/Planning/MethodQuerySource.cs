using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>The direct MethodDef breadth selected by a source plan.</summary>
public enum MethodDefinitionSourceBreadthKind
{
    /// <summary>Every MethodDef in metadata order.</summary>
    AllDefinitions,

    /// <summary>An exact normalized set of MethodDef handles.</summary>
    ExactMethods,

    /// <summary>Every MethodDef declared by an exact normalized set of TypeDefs.</summary>
    ExactTypes,
}

/// <summary>
/// The immutable direct MethodDef population one source request may read.
/// Exact coordinates are normalized into metadata order without duplicates.
/// </summary>
public sealed class MethodDefinitionSourceBreadth
{
    MethodDefinitionSourceBreadth(
        MethodDefinitionSourceBreadthKind kind,
        ImmutableArray<MethodDefinitionHandle> methods,
        ImmutableArray<TypeDefinitionHandle> types)
    {
        Kind = kind;
        Methods = methods;
        Types = types;
    }

    public static MethodDefinitionSourceBreadth AllDefinitions { get; } =
        new(
            MethodDefinitionSourceBreadthKind.AllDefinitions,
            [],
            []);

    public MethodDefinitionSourceBreadthKind Kind { get; }

    public ImmutableArray<MethodDefinitionHandle> Methods { get; }

    public ImmutableArray<TypeDefinitionHandle> Types { get; }

    public bool IsEmpty =>
        Kind switch
        {
            MethodDefinitionSourceBreadthKind.ExactMethods => Methods.IsEmpty,
            MethodDefinitionSourceBreadthKind.ExactTypes => Types.IsEmpty,
            _ => false,
        };

    public static MethodDefinitionSourceBreadth ExactMethods(
        params MethodDefinitionHandle[] methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        return CreateExactMethods(methods);
    }

    public static MethodDefinitionSourceBreadth ExactMethods(
        ImmutableArray<MethodDefinitionHandle> methods)
    {
        if (methods.IsDefault)
        {
            throw new ArgumentException(
                "Exact MethodDef handles must be initialized.",
                nameof(methods));
        }

        return CreateExactMethods(methods.AsSpan());
    }

    static MethodDefinitionSourceBreadth CreateExactMethods(
        ReadOnlySpan<MethodDefinitionHandle> methods)
    {
        var rows = new int[methods.Length];
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].IsNil)
            {
                throw new ArgumentException(
                    "An exact MethodDef handle cannot be nil.",
                    nameof(methods));
            }

            rows[i] = MetadataTokens.GetRowNumber(methods[i]);
        }

        Array.Sort(rows);
        var normalized =
            ImmutableArray.CreateBuilder<MethodDefinitionHandle>(rows.Length);
        int previous = 0;
        foreach (int row in rows)
        {
            if (row == previous)
                continue;
            normalized.Add(MetadataTokens.MethodDefinitionHandle(row));
            previous = row;
        }

        return new(
            MethodDefinitionSourceBreadthKind.ExactMethods,
            normalized.ToImmutable(),
            []);
    }

    public static MethodDefinitionSourceBreadth ExactTypes(
        params TypeDefinitionHandle[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return CreateExactTypes(types);
    }

    public static MethodDefinitionSourceBreadth ExactTypes(
        ImmutableArray<TypeDefinitionHandle> types)
    {
        if (types.IsDefault)
        {
            throw new ArgumentException(
                "Exact TypeDef handles must be initialized.",
                nameof(types));
        }

        return CreateExactTypes(types.AsSpan());
    }

    static MethodDefinitionSourceBreadth CreateExactTypes(
        ReadOnlySpan<TypeDefinitionHandle> types)
    {
        var rows = new int[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            if (types[i].IsNil)
            {
                throw new ArgumentException(
                    "An exact TypeDef handle cannot be nil.",
                    nameof(types));
            }

            rows[i] = MetadataTokens.GetRowNumber(types[i]);
        }

        Array.Sort(rows);
        var normalized =
            ImmutableArray.CreateBuilder<TypeDefinitionHandle>(rows.Length);
        int previous = 0;
        foreach (int row in rows)
        {
            if (row == previous)
                continue;
            normalized.Add(MetadataTokens.TypeDefinitionHandle(row));
            previous = row;
        }

        return new(
            MethodDefinitionSourceBreadthKind.ExactTypes,
            [],
            normalized.ToImmutable());
    }
}

/// <summary>Opaque identity for one owner-issued Method-definition source request.</summary>
public sealed class MethodDefinitionSourceRequestIdentity
{
    internal MethodDefinitionSourceRequestIdentity()
    {
    }
}

/// <summary>
/// One resource-free request to run a closed, single-producer
/// description over Method definitions.
/// </summary>
public sealed class MethodDefinitionSourceRequest<TResult>
{
    internal MethodDefinitionSourceRequest(
        WorkDescription work,
        ProducerDeclaration<TResult> producer,
        MethodDefinitionSourceBreadth breadth)
    {
        Work = work;
        Producer = producer;
        Identity = new();
        Breadth = breadth;
        Terminal = work.TerminalOf(producer);
        DeclaredLayers = MethodDefinitionExecution.FieldsRead(work);
    }

    /// <summary>Creates the reference Method-source request without reading a subject.</summary>
    public static MethodDefinitionSourceRequest<TResult> Create(
        WorkDescription work,
        ProducerDeclaration<TResult> producer,
        MethodDefinitionSourceBreadth? breadth = null) =>
        MethodQuerySource.Plan(
            work,
            producer,
            breadth ?? MethodDefinitionSourceBreadth.AllDefinitions);

    /// <summary>This request's exact identity.</summary>
    public MethodDefinitionSourceRequestIdentity Identity { get; }

    /// <summary>The direct physical population selected by this request.</summary>
    public MethodDefinitionSourceBreadth Breadth { get; }

    /// <summary>The focused producer result this request publishes.</summary>
    public ProducerDeclaration<TResult> Producer { get; }

    /// <summary>The closing applied to the focused producer.</summary>
    public ProducerTerminal Terminal { get; }

    /// <summary>The source layers declared by the closed work description.</summary>
    public MethodDefinitionLayers DeclaredLayers { get; }

    internal WorkDescription Work { get; }
}

/// <summary>How the Method source settled one accepted request.</summary>
public enum MethodDefinitionSourceCompletion
{
    /// <summary>The source exhausted the selected Method-definition population.</summary>
    Exhausted,

    /// <summary>The request's terminal settled before source exhaustion.</summary>
    Satisfied,

    /// <summary>A producer failure prevented the source request from settling.</summary>
    ProducerFailed,

    /// <summary>A critical producer work bound aborted the source request.</summary>
    Aborted,
}

/// <summary>One inclusive contiguous range of MethodDef identities.</summary>
public readonly record struct MethodDefinitionHandleRange
{
    internal MethodDefinitionHandleRange(
        MethodDefinitionHandle first,
        MethodDefinitionHandle last)
    {
        First = first;
        Last = last;
    }

    public MethodDefinitionHandle First { get; }

    public MethodDefinitionHandle Last { get; }

    public int Count =>
        MetadataTokens.GetRowNumber(Last)
        - MetadataTokens.GetRowNumber(First)
        + 1;

    public bool Contains(MethodDefinitionHandle handle)
    {
        if (handle.IsNil)
            return false;

        int row = MetadataTokens.GetRowNumber(handle);
        return row >= MetadataTokens.GetRowNumber(First)
            && row <= MetadataTokens.GetRowNumber(Last);
    }
}

/// <summary>Exact detached MethodDef coverage compacted into row ranges.</summary>
public sealed class MethodDefinitionHandleCoverage
{
    internal MethodDefinitionHandleCoverage(
        int count,
        ImmutableArray<MethodDefinitionHandleRange> ranges)
    {
        Count = count;
        Ranges = ranges;
    }

    public static MethodDefinitionHandleCoverage Empty { get; } =
        new(0, []);

    public int Count { get; }

    public ImmutableArray<MethodDefinitionHandleRange> Ranges { get; }

    public bool Contains(MethodDefinitionHandle handle)
    {
        foreach (MethodDefinitionHandleRange range in Ranges)
        {
            if (range.Contains(handle))
                return true;
        }

        return false;
    }
}

/// <summary>Exact source work performed for one Method query request.</summary>
public sealed record MethodDefinitionSourceCoverage(
    MethodDefinitionHandleCoverage DefinitionsExamined,
    MethodDefinitionHandleCoverage MethodsSelected,
    MethodDefinitionHandleCoverage BodiesAcquired);

/// <summary>Detached evidence of the Method-source work actually performed.</summary>
public sealed record MethodDefinitionSourceReceipt(
    MethodDefinitionSourceRequestIdentity Request,
    AssemblyInspectionSubjectIdentity Subject,
    MethodDefinitionSourceBreadth Breadth,
    ProducerTerminal Terminal,
    MethodDefinitionLayers DeclaredLayers,
    MethodDefinitionSourceCompletion Completion,
    MethodDefinitionSourceCoverage Coverage,
    int ModuleLookups)
{
    public int DefinitionsVisited => Coverage.MethodsSelected.Count;

    public int BodiesAcquired => Coverage.BodiesAcquired.Count;
}

internal readonly struct MethodQuerySourceExecution<TResult>
{
    internal MethodQuerySourceExecution(
        MethodDefinitionSourceReceipt receipt,
        WorkReceipt workReceipt,
        ProducerResult<TResult> result)
    {
        Receipt = receipt;
        WorkReceipt = workReceipt;
        Result = result;
    }

    internal MethodDefinitionSourceReceipt Receipt { get; }

    internal WorkReceipt WorkReceipt { get; }

    internal ProducerResult<TResult> Result { get; }
}

internal static class MethodQuerySource
{
    internal static MethodDefinitionSourceRequest<TResult> Plan<TResult>(
        WorkDescription work,
        ProducerDeclaration<TResult> producer,
        MethodDefinitionSourceBreadth breadth)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(breadth);
        if (!work.Contains(producer) || !work.WasRequested(producer))
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' is not a requested producer "
                + "in this work description.");
        }

        if (work.Producers.Length != 1
            || !ReferenceEquals(work.Producers[0], producer))
        {
            throw new ProducerContractException(
                "The reference Method source accepts exactly one planned "
                + "producer until QuerySpace request collapse lands.");
        }

        foreach (ProducerDeclaration planned in work.Producers)
        {
            if (planned is not IMethodDefinitionProducer)
            {
                throw new ProducerContractException(
                    $"Producer '{planned.Identity}' is not a "
                    + "method-definition producer.");
            }
        }

        return new(work, producer, breadth);
    }

    internal static MethodQuerySourceExecution<TResult> Execute<TResult>(
        MethodDefinitionSourceRequest<TResult> request,
        AssemblyInspectionSubjectIdentity subject,
        string sourceName,
        PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);

        MethodDefinitionExecution interim =
            MethodDefinitionExecution.Execute(
                request.Work,
                sourceName,
                peReader,
                request.Breadth);
        ProducerResult<TResult> result =
            interim.ResultOf(request.Producer);
        WorkReceipt workReceipt = interim.Receipt;
        ProducerParticipation participation =
            workReceipt.For(request.Producer);

        int moduleLookups = 0;
        foreach (ProducerLayerParticipation layer in participation.Layers)
        {
            if (string.Equals(
                    layer.Layer,
                    nameof(MethodDefinitionLayers.ModuleLookup),
                    StringComparison.Ordinal))
            {
                moduleLookups = layer.Acquired;
            }
        }

        MethodDefinitionSourceCompletion completion = result.Outcome switch
        {
            ProducerOutcome.Complete =>
                MethodDefinitionSourceCompletion.Exhausted,
            ProducerOutcome.Stopped =>
                MethodDefinitionSourceCompletion.Satisfied,
            ProducerOutcome.Failed or ProducerOutcome.PrerequisiteFailed =>
                MethodDefinitionSourceCompletion.ProducerFailed,
            ProducerOutcome.Aborted =>
                MethodDefinitionSourceCompletion.Aborted,
            _ => throw new ProducerContractException(
                $"Unknown producer outcome '{result.Outcome}'."),
        };
        var receipt = new MethodDefinitionSourceReceipt(
            request.Identity,
            subject,
            request.Breadth,
            request.Terminal,
            request.DeclaredLayers,
            completion,
            interim.SourceCoverage,
            moduleLookups);
        return new(receipt, workReceipt, result);
    }
}

internal sealed class MethodDefinitionSourceCoverageBuilder
{
    readonly bool _enabled;
    readonly MethodDefinitionHandleCoverageBuilder _definitionsExamined = new();
    readonly MethodDefinitionHandleCoverageBuilder _methodsSelected = new();
    readonly MethodDefinitionHandleCoverageBuilder _bodiesAcquired = new();

    public MethodDefinitionSourceCoverageBuilder(bool enabled) =>
        _enabled = enabled;

    public void RecordDefinitionExamined(MethodDefinitionHandle handle)
    {
        if (_enabled)
            _definitionsExamined.Add(handle);
    }

    public void RecordMethodSelected(MethodDefinitionHandle handle)
    {
        if (_enabled)
            _methodsSelected.Add(handle);
    }

    public void RecordBodyAcquired(MethodDefinitionHandle handle)
    {
        if (_enabled)
            _bodiesAcquired.Add(handle);
    }

    public MethodDefinitionSourceCoverage Build() =>
        new(
            _definitionsExamined.Build(),
            _methodsSelected.Build(),
            _bodiesAcquired.Build());
}

internal sealed class MethodDefinitionHandleCoverageBuilder
{
    List<MethodDefinitionHandleRange>? _completedRanges;
    int _firstRow;
    int _lastRow;
    int _count;

    public void Add(MethodDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (_count == 0)
        {
            _firstRow = row;
            _lastRow = row;
            _count = 1;
            return;
        }

        if (row == _lastRow)
            return;
        if (row < _lastRow)
        {
            throw new InvalidOperationException(
                "Method source coverage must be recorded in metadata order.");
        }

        if (row == _lastRow + 1)
        {
            _lastRow = row;
            _count++;
            return;
        }

        (_completedRanges ??= []).Add(CurrentRange());
        _firstRow = row;
        _lastRow = row;
        _count++;
    }

    public MethodDefinitionHandleCoverage Build()
    {
        if (_count == 0)
            return MethodDefinitionHandleCoverage.Empty;

        MethodDefinitionHandleRange current = CurrentRange();
        if (_completedRanges is null)
        {
            return new MethodDefinitionHandleCoverage(
                _count,
                [current]);
        }

        var ranges =
            ImmutableArray.CreateBuilder<MethodDefinitionHandleRange>(
                _completedRanges.Count + 1);
        ranges.AddRange(_completedRanges);
        ranges.Add(current);
        return new MethodDefinitionHandleCoverage(
            _count,
            ranges.ToImmutable());
    }

    MethodDefinitionHandleRange CurrentRange() =>
        new(
            MetadataTokens.MethodDefinitionHandle(_firstRow),
            MetadataTokens.MethodDefinitionHandle(_lastRow));
}
