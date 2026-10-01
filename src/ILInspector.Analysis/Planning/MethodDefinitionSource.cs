using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Planning;

/// <summary>Opaque identity for one owner-issued Method-definition source request.</summary>
public sealed class MethodDefinitionSourceRequestIdentity
{
    internal MethodDefinitionSourceRequestIdentity()
    {
    }
}

/// <summary>
/// One resource-free request to run a closed, single-producer description
/// over Method definitions.
/// </summary>
public sealed class MethodDefinitionSourceRequest<TResult>
{
    MethodDefinitionSourceRequest(
        WorkDescription work,
        ProducerDeclaration<TResult> producer)
    {
        Work = work;
        Producer = producer;
        Identity = new();
        Terminal = work.TerminalOf(producer);
        DeclaredLayers = MethodDefinitionExecution.FieldsRead(work);
    }

    /// <summary>Creates the reference Method-source request without reading a subject.</summary>
    public static MethodDefinitionSourceRequest<TResult> Create(
        WorkDescription work,
        ProducerDeclaration<TResult> producer)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(producer);
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

        return new(work, producer);
    }

    /// <summary>This request's exact identity.</summary>
    public MethodDefinitionSourceRequestIdentity Identity { get; }

    /// <summary>The focused producer result this request publishes.</summary>
    public ProducerDeclaration<TResult> Producer { get; }

    /// <summary>The closing applied to the focused producer.</summary>
    public ProducerTerminal Terminal { get; }

    /// <summary>The source layers declared by the closed work description.</summary>
    public MethodDefinitionLayers DeclaredLayers { get; }

    internal WorkDescription Work { get; }
}

/// <summary>An inclusive range of MethodDef rows in one admitted module.</summary>
public readonly record struct MethodDefinitionRowRange
{
    public MethodDefinitionRowRange(int firstRow, int lastRow)
    {
        if (firstRow <= 0)
            throw new ArgumentOutOfRangeException(nameof(firstRow));
        if (lastRow < firstRow)
            throw new ArgumentOutOfRangeException(nameof(lastRow));

        FirstRow = firstRow;
        LastRow = lastRow;
    }

    public int FirstRow { get; }

    public int LastRow { get; }

    public int Count => checked(LastRow - FirstRow + 1);

    public int FirstMetadataToken =>
        MetadataTokens.GetToken(
            MetadataTokens.MethodDefinitionHandle(FirstRow));

    public int LastMetadataToken =>
        MetadataTokens.GetToken(
            MetadataTokens.MethodDefinitionHandle(LastRow));
}

/// <summary>
/// Detached exact coverage of MethodDef rows, compacted into sorted ranges.
/// </summary>
public sealed class MethodDefinitionCoverage
{
    internal MethodDefinitionCoverage(
        ImmutableArray<MethodDefinitionRowRange> ranges,
        int count)
    {
        Ranges = ranges;
        Count = count;
    }

    public static MethodDefinitionCoverage Empty { get; } =
        new([], 0);

    public ImmutableArray<MethodDefinitionRowRange> Ranges { get; }

    public int Count { get; }

    public bool ContainsMetadataToken(int token)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind != HandleKind.MethodDefinition)
            return false;

        int row = MetadataTokens.GetRowNumber(
            (MethodDefinitionHandle)handle);
        foreach (MethodDefinitionRowRange range in Ranges)
        {
            if (row < range.FirstRow)
                return false;
            if (row <= range.LastRow)
                return true;
        }

        return false;
    }

    internal bool HasSameCoverage(
        MethodDefinitionCoverage other) =>
        Count == other.Count
        && Ranges.AsSpan().SequenceEqual(
            other.Ranges.AsSpan());
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

    /// <summary>Required source work could not complete exactly.</summary>
    SourceIncomplete,

    /// <summary>A critical producer or source work bound aborted the request.</summary>
    Aborted,

    /// <summary>The owning operation cancelled before the next Method unit.</summary>
    Cancelled,
}

/// <summary>Detached evidence of the Method-source work actually performed.</summary>
public sealed class MethodDefinitionSourceReceipt
{
    internal MethodDefinitionSourceReceipt(
        MethodDefinitionSourceRequestIdentity request,
        ProducerTerminal terminal,
        MethodDefinitionLayers declaredLayers,
        MethodDefinitionSourceCompletion completion,
        MethodDefinitionCoverage definitionsExamined,
        MethodDefinitionCoverage methodsSelected,
        MethodDefinitionCoverage bodiesAttempted,
        MethodDefinitionCoverage bodiesAcquired,
        MethodDefinitionCoverage moduleLookupMethods)
    {
        Request = request;
        Terminal = terminal;
        DeclaredLayers = declaredLayers;
        Completion = completion;
        DefinitionsExamined = definitionsExamined;
        MethodsSelected = methodsSelected;
        BodiesAttempted = bodiesAttempted;
        TerminalBodiesAcquired = bodiesAcquired;
        ModuleLookupMethods = moduleLookupMethods;
    }

    public MethodDefinitionSourceRequestIdentity Request { get; }

    public ProducerTerminal Terminal { get; }

    public MethodDefinitionLayers DeclaredLayers { get; }

    public MethodDefinitionSourceCompletion Completion { get; }

    public MethodDefinitionCoverage DefinitionsExamined { get; }

    public MethodDefinitionCoverage MethodsSelected { get; }

    public MethodDefinitionCoverage BodiesAttempted { get; }

    public MethodDefinitionCoverage TerminalBodiesAcquired { get; }

    public MethodDefinitionCoverage ModuleLookupMethods { get; }

    public int DefinitionsVisited => DefinitionsExamined.Count;

    public int BodiesAcquired => TerminalBodiesAcquired.Count;

    public int ModuleLookups => ModuleLookupMethods.Count;
}

/// <summary>
/// Detached publication of one owner-issued Method source execution.
/// </summary>
public sealed class MethodDefinitionSourceExecution<TResult>
{
    readonly ProducerResult<TResult> _result;

    internal MethodDefinitionSourceExecution(
        MethodDefinitionSourceRequest<TResult> request,
        MethodDefinitionSourceReceipt receipt,
        WorkReceipt workReceipt,
        ProducerResult<TResult> result)
    {
        Request = request;
        Receipt = receipt;
        WorkReceipt = workReceipt;
        _result = result;
    }

    public MethodDefinitionSourceRequest<TResult> Request { get; }

    public MethodDefinitionSourceReceipt Receipt { get; }

    public WorkReceipt WorkReceipt { get; }

    public ProducerResult<TResult> ResultOf(
        ProducerDeclaration<TResult> producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (!ReferenceEquals(Request.Producer, producer))
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' is not the focused result "
                + "of this Method source request.");
        }

        return _result;
    }
}

/// <summary>
/// Stateless serial reference source for owner-issued Method-definition work.
/// </summary>
public sealed class MethodDefinitionSource
{
    MethodDefinitionSource()
    {
    }

    public static MethodDefinitionSource Instance { get; } = new();

    public MethodDefinitionSourceExecution<TResult> Execute<TResult>(
        MethodDefinitionSourceRequest<TResult> request,
        string sourceName,
        PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);

        var work = new MethodDefinitionSourceWorkRecorder();
        MethodDefinitionExecution execution =
            MethodDefinitionExecution.Execute(
                request.Work,
                sourceName,
                peReader,
                work);
        ProducerResult<TResult> result =
            execution.ResultOf(request.Producer);
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
        MethodDefinitionSourceWorkSnapshot observed = work.Snapshot();
        var receipt = new MethodDefinitionSourceReceipt(
            request.Identity,
            request.Terminal,
            request.DeclaredLayers,
            completion,
            observed.DefinitionsExamined,
            observed.MethodsSelected,
            observed.BodiesAttempted,
            observed.BodiesAcquired,
            observed.ModuleLookupMethods);
        return new(
            request,
            receipt,
            execution.Receipt,
            result);
    }
}

internal sealed class MethodDefinitionSourceWorkRecorder
{
    MethodDefinitionCoverageBuilder _definitionsExamined;
    MethodDefinitionCoverageBuilder _methodsSelected;
    MethodDefinitionCoverageBuilder _bodiesAttempted;
    MethodDefinitionCoverageBuilder _bodiesAcquired;
    MethodDefinitionCoverageBuilder _moduleLookupMethods;

    public void DefinitionExamined(MethodDefinitionHandle handle) =>
        _definitionsExamined.Add(handle);

    public void MethodSelected(MethodDefinitionHandle handle) =>
        _methodsSelected.Add(handle);

    public void BodyAttempted(MethodDefinitionHandle handle) =>
        _bodiesAttempted.Add(handle);

    public void BodyAcquired(MethodDefinitionHandle handle) =>
        _bodiesAcquired.Add(handle);

    public void ModuleLookupUsed(MethodDefinitionHandle handle) =>
        _moduleLookupMethods.Add(handle);

    public MethodDefinitionSourceWorkSnapshot Snapshot() =>
        CreateSnapshot();

    MethodDefinitionSourceWorkSnapshot CreateSnapshot()
    {
        MethodDefinitionCoverage examined =
            _definitionsExamined.Build();
        MethodDefinitionCoverage selected =
            Reuse(_methodsSelected.Build(), examined);
        MethodDefinitionCoverage bodiesAttempted =
            _bodiesAttempted.Build();
        MethodDefinitionCoverage bodiesAcquired =
            Reuse(
                _bodiesAcquired.Build(),
                bodiesAttempted);
        MethodDefinitionCoverage lookupMethods =
            Reuse(
                _moduleLookupMethods.Build(),
                selected,
                examined);
        return new(
            examined,
            selected,
            bodiesAttempted,
            bodiesAcquired,
            lookupMethods);
    }

    static MethodDefinitionCoverage Reuse(
        MethodDefinitionCoverage coverage,
        MethodDefinitionCoverage candidate)
    {
        return coverage.HasSameCoverage(candidate)
            ? candidate
            : coverage;
    }

    static MethodDefinitionCoverage Reuse(
        MethodDefinitionCoverage coverage,
        MethodDefinitionCoverage first,
        MethodDefinitionCoverage second)
    {
        if (coverage.HasSameCoverage(first))
            return first;
        return coverage.HasSameCoverage(second)
            ? second
            : coverage;
    }
}

internal readonly record struct MethodDefinitionSourceWorkSnapshot(
    MethodDefinitionCoverage DefinitionsExamined,
    MethodDefinitionCoverage MethodsSelected,
    MethodDefinitionCoverage BodiesAttempted,
    MethodDefinitionCoverage BodiesAcquired,
    MethodDefinitionCoverage ModuleLookupMethods);

internal struct MethodDefinitionCoverageBuilder
{
    List<MethodDefinitionRowRange>? _ranges;
    HashSet<int>? _unorderedRows;
    int _count;
    int _lastRow;

    public void Add(MethodDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (_unorderedRows is not null)
        {
            _unorderedRows.Add(row);
            return;
        }

        if (row == _lastRow)
            return;
        if (row < _lastRow)
        {
            _unorderedRows = [];
            if (_ranges is not null)
            {
                foreach (MethodDefinitionRowRange range in _ranges)
                {
                    for (int existing = range.FirstRow;
                         existing <= range.LastRow;
                         existing++)
                    {
                        _unorderedRows.Add(existing);
                    }
                }
            }

            _unorderedRows.Add(row);
            _ranges = null;
            return;
        }

        _ranges ??= [];
        if (_ranges.Count != 0
            && row == _ranges[^1].LastRow + 1)
        {
            MethodDefinitionRowRange last = _ranges[^1];
            _ranges[^1] = new(last.FirstRow, row);
        }
        else
        {
            _ranges.Add(new(row, row));
        }

        _count++;
        _lastRow = row;
    }

    public MethodDefinitionCoverage Build()
    {
        if (_unorderedRows is null)
        {
            return _count == 0
                ? MethodDefinitionCoverage.Empty
                : new([.. _ranges!], _count);
        }

        int[] rows = [.. _unorderedRows];
        Array.Sort(rows);
        var ranges =
            ImmutableArray.CreateBuilder<MethodDefinitionRowRange>();
        int first = rows[0];
        int last = first;
        for (int i = 1; i < rows.Length; i++)
        {
            int row = rows[i];
            if (row == last + 1)
            {
                last = row;
                continue;
            }

            ranges.Add(new(first, last));
            first = row;
            last = row;
        }

        ranges.Add(new(first, last));
        return new(ranges.ToImmutable(), rows.Length);
    }
}
