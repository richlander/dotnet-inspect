using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using ILInspector.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// One shallow instruction retained by <see cref="InstructionSequence"/>.
/// </summary>
public readonly struct InstructionEntry
{
    internal InstructionEntry(
        int offset,
        ILOpCode opCode,
        int nextOffset)
    {
        Offset = offset;
        OpCode = opCode;
        NextOffset = nextOffset;
    }

    public int Offset { get; }

    public ILOpCode OpCode { get; }

    public int NextOffset { get; }

    public int Length => NextOffset - Offset;
}

/// <summary>
/// A demand-driven shallow instruction prefix shared by independent cursors
/// and indexed readers.
/// </summary>
/// <remarks>
/// The sequence is single-threaded. Access scans only through the furthest
/// requested instruction and retains offset, opcode, and extent for replay or
/// indexed access. Operand values and branch targets are decoded only by
/// <see cref="Resolve(int)"/>, unless the sequence was explicitly created with
/// resolved-detail retention. Unreached IL remains unvalidated. A reached
/// structural failure is retained and rethrown for later requests that need
/// more instructions.
/// </remarks>
public sealed class InstructionSequence
{
    readonly MethodBodyBlock? _body;
    readonly Action? _ensureBodyOwnerAlive;
    readonly ImmutableArray<byte> _il;
    readonly bool _retainResolvedDetail;
    readonly List<InstructionEntry> _prefix = [];
    byte[]? _resolutionIl;
    Dictionary<int, DecodedInstruction>? _resolved;
    int _nextOffset;
    bool _isComplete;
    ExceptionDispatchInfo? _failure;

    InstructionSequence(
        MethodBodyBlock body,
        Action ensureOwnerAlive,
        bool retainResolvedDetail)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _ensureBodyOwnerAlive =
            ensureOwnerAlive
            ?? throw new ArgumentNullException(nameof(ensureOwnerAlive));
        _retainResolvedDetail = retainResolvedDetail;
    }

    InstructionSequence(
        ImmutableArray<byte> il,
        bool retainResolvedDetail)
    {
        if (il.IsDefault)
            throw new ArgumentException("The IL snapshot must be initialized.", nameof(il));
        _il = il;
        _retainResolvedDetail = retainResolvedDetail;
    }

    InstructionSequence(
        ReadOnlySpan<byte> il,
        bool retainResolvedDetail)
    {
        _il = ImmutableCollectionsMarshal.AsImmutableArray(il.ToArray());
        _retainResolvedDetail = retainResolvedDetail;
    }

    public InstructionSequence(ImmutableArray<byte> il)
        : this(il, retainResolvedDetail: false)
    {
    }

    public InstructionSequence(ReadOnlySpan<byte> il)
        : this(il, retainResolvedDetail: false)
    {
    }

    /// <summary>
    /// Creates a sequence that borrows one Method body and checks the owner's
    /// lifetime before every access to its retained image.
    /// </summary>
    public static InstructionSequence Borrow(
        MethodBodyBlock body,
        Action ensureOwnerAlive) =>
        new(body, ensureOwnerAlive, retainResolvedDetail: false);

    /// <summary>
    /// Creates a borrowed sequence that retains full decoded detail while its
    /// shared scan frontier advances.
    /// </summary>
    public static InstructionSequence BorrowResolved(
        MethodBodyBlock body,
        Action ensureOwnerAlive) =>
        new(body, ensureOwnerAlive, retainResolvedDetail: true);

    /// <summary>
    /// Creates a copied sequence that retains full decoded detail while its
    /// shared scan frontier advances.
    /// </summary>
    public static InstructionSequence CreateResolved(
        ImmutableArray<byte> il) =>
        new(il, retainResolvedDetail: true);

    /// <summary>
    /// Creates a copied sequence that retains full decoded detail while its
    /// shared scan frontier advances.
    /// </summary>
    public static InstructionSequence CreateResolved(
        ReadOnlySpan<byte> il) =>
        new(il, retainResolvedDetail: true);

    /// <summary>
    /// Whether access has reached and validated the end of the IL stream.
    /// Reading this property never advances scanning.
    /// </summary>
    public bool IsComplete => _isComplete;

    /// <summary>
    /// The number of shallow instructions retained so far. Reading this
    /// property never advances scanning.
    /// </summary>
    public int RetainedCount => _prefix.Count;

    /// <summary>Creates an independent cursor at the beginning of the sequence.</summary>
    public InstructionCursor GetCursor() => new(this);

    /// <summary>
    /// Gets one zero-based shallow instruction, scanning and retaining only as
    /// far as that instruction. Returns <see langword="false"/> when the
    /// stream ends before the requested index.
    /// </summary>
    public bool TryGet(
        int index,
        out InstructionEntry instruction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        EnsureScannedThrough(index);
        if ((uint)index < (uint)_prefix.Count)
        {
            instruction = _prefix[index];
            return true;
        }

        instruction = default;
        return false;
    }

    /// <summary>
    /// Gets one zero-based shallow instruction, scanning only as far as that
    /// instruction.
    /// </summary>
    public InstructionEntry this[int index] =>
        TryGet(index, out InstructionEntry instruction)
            ? instruction
            : throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "The instruction index is beyond the end of the IL stream.");

    /// <summary>
    /// Gets the shallow instruction beginning exactly at
    /// <paramref name="offset"/>, scanning only until that offset is covered.
    /// </summary>
    public bool TryGetAtOffset(
        int offset,
        out int index,
        out InstructionEntry instruction)
    {
        index = IndexAtOrAfter(offset);
        if ((uint)index < (uint)_prefix.Count
            && _prefix[index].Offset == offset)
        {
            instruction = _prefix[index];
            return true;
        }

        instruction = default;
        return false;
    }

    /// <summary>
    /// Gets the index of the first instruction beginning at or after
    /// <paramref name="offset"/>, extending the shared prefix only until that
    /// result is known.
    /// </summary>
    public int IndexAtOrAfter(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        int index = LowerBound(offset);
        if (index < _prefix.Count || _isComplete)
            return index;

        _failure?.Throw();
        while (TryScanNext(out InstructionEntry instruction))
        {
            _prefix.Add(instruction);
            if (instruction.Offset >= offset)
                return _prefix.Count - 1;
        }

        return _prefix.Count;
    }

    /// <summary>
    /// Resolves full operand and branch detail for one retained instruction.
    /// </summary>
    public DecodedInstruction Resolve(int index)
    {
        InstructionEntry entry = this[index];
        if (_resolved is not null
            && _resolved.TryGetValue(index, out DecodedInstruction? resolved))
        {
            return resolved;
        }

        ReadOnlySpan<byte> il = GetResolutionIl();
        int offset = entry.Offset;
        if (!InstructionDecoder.TryDecodeNext(
            il,
            ref offset,
            default,
            out resolved))
        {
            throw new InvalidOperationException(
                "A retained instruction could not be resolved.");
        }
        if (resolved.Offset != entry.Offset
            || resolved.OpCode != entry.OpCode
            || resolved.NextOffset != entry.NextOffset)
        {
            throw new InvalidOperationException(
                "Resolved instruction detail does not match the retained shallow entry.");
        }

        _resolved ??= [];
        _resolved.Add(index, resolved);
        return resolved;
    }

    /// <summary>
    /// Completes a resolved-detail sequence and promotes its one retained
    /// decode into canonical Layer 0 instructions correlated with
    /// <paramref name="body"/>.
    /// </summary>
    /// <remarks>
    /// This operation explicitly scans and validates any unreached suffix.
    /// The body evidence must describe the same IL bytes retained by this
    /// sequence.
    /// </remarks>
    public MethodInstructions Materialize(MethodBodyData body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!_retainResolvedDetail)
        {
            throw new InvalidOperationException(
                "Materialization requires a sequence created with resolved-detail retention.");
        }

        while (!_isComplete)
        {
            if (!TryScanNext(out InstructionEntry instruction))
                break;
            _prefix.Add(instruction);
        }
        _failure?.Throw();

        ReadOnlySpan<byte> il = GetResolutionIl();
        if (!il.SequenceEqual(body.IL.AsSpan()))
        {
            throw new ArgumentException(
                "The Metadata body evidence does not match the retained IL.",
                nameof(body));
        }

        var instructions =
            ImmutableArray.CreateBuilder<DecodedInstruction>(_prefix.Count);
        for (int i = 0; i < _prefix.Count; i++)
        {
            if (_resolved is null
                || !_resolved.TryGetValue(
                    i,
                    out DecodedInstruction? resolved))
            {
                throw new InvalidOperationException(
                    "Resolved instruction detail was not retained.");
            }
            instructions.Add(resolved);
        }

        return MethodInstructions.Create(
            body,
            instructions.MoveToImmutable());
    }

    internal bool TryMoveNext(
        ref int nextIndex,
        out InstructionEntry instruction)
    {
        if ((uint)nextIndex < (uint)_prefix.Count)
        {
            instruction = _prefix[nextIndex++];
            return true;
        }

        _failure?.Throw();
        if (_isComplete)
        {
            instruction = default;
            return false;
        }

        if (nextIndex != _prefix.Count)
        {
            throw new InvalidOperationException(
                "A cursor cannot advance beyond the shared scan frontier.");
        }

        if (!TryScanNext(out instruction))
            return false;

        _prefix.Add(instruction);
        nextIndex++;
        return true;
    }

    void EnsureScannedThrough(int index)
    {
        if ((uint)index < (uint)_prefix.Count)
            return;

        _failure?.Throw();
        while (_prefix.Count <= index && !_isComplete)
        {
            if (!TryScanNext(out InstructionEntry instruction))
                break;
            _prefix.Add(instruction);
        }
    }

    int LowerBound(int offset)
    {
        int lo = 0;
        int hi = _prefix.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_prefix[mid].Offset < offset)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    bool TryScanNext(out InstructionEntry instruction)
    {
        try
        {
            _ensureBodyOwnerAlive?.Invoke();
            bool hasInstruction;
            if (_retainResolvedDetail)
            {
                int index = _prefix.Count;
                if (InstructionDecoder.TryDecodeNext(
                    GetResolutionIl(),
                    ref _nextOffset,
                    default,
                    out DecodedInstruction? resolved))
                {
                    hasInstruction = true;
                    instruction = new(
                        resolved.Offset,
                        resolved.OpCode,
                        resolved.NextOffset);
                    _resolved ??= [];
                    _resolved.Add(index, resolved);
                }
                else
                {
                    hasInstruction = false;
                    instruction = default;
                }
            }
            else
            {
                hasInstruction = _body is not null
                    ? InstructionDecoder.TryReadNext(
                        _body,
                        ref _nextOffset,
                        out instruction)
                    : InstructionDecoder.TryReadNext(
                        GetIl(),
                        ref _nextOffset,
                        out instruction);
            }
            if (!hasInstruction)
                _isComplete = true;
            return hasInstruction;
        }
        catch (BadImageFormatException ex)
        {
            _failure = ExceptionDispatchInfo.Capture(ex);
            throw;
        }
    }

    ReadOnlySpan<byte> GetIl()
    {
        if (_body is null)
            return _il.AsSpan();

        _ensureBodyOwnerAlive!.Invoke();
        return _body.GetILBytes() ?? [];
    }

    ReadOnlySpan<byte> GetResolutionIl()
    {
        if (_body is null)
            return _il.AsSpan();

        _ensureBodyOwnerAlive!.Invoke();
        return _resolutionIl ??= _body.GetILBytes() ?? [];
    }
}

/// <summary>
/// One independently positioned forward cursor over an
/// <see cref="InstructionSequence"/>.
/// </summary>
public struct InstructionCursor
{
    readonly InstructionSequence _sequence;
    int _nextIndex;
    InstructionEntry _current;
    bool _hasCurrent;

    internal InstructionCursor(InstructionSequence sequence)
    {
        _sequence = sequence;
        _nextIndex = 0;
        _current = default;
        _hasCurrent = false;
    }

    /// <summary>The instruction selected by the most recent successful move.</summary>
    public readonly InstructionEntry Current =>
        _hasCurrent
            ? _current
            : throw new InvalidOperationException(
                "The cursor is not positioned on an instruction.");

    /// <summary>
    /// Advances to the next shallow instruction, extending the shared retained
    /// prefix only when this cursor reaches its frontier.
    /// </summary>
    public bool MoveNext()
    {
        if (_sequence.TryMoveNext(
            ref _nextIndex,
            out InstructionEntry instruction))
        {
            _current = instruction;
            _hasCurrent = true;
            return true;
        }

        _current = default;
        _hasCurrent = false;
        return false;
    }
}
