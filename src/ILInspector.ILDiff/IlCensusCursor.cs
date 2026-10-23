using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

using Inspector.Findings;

namespace ILInspector.ILDiff;

/// <summary>How a census cursor ended.</summary>
public enum IlCensusTerminalKind
{
    /// <summary>Every atom of the body was yielded.</summary>
    Complete,

    /// <summary>The row has no census of this kind; no atom was yielded.</summary>
    Absent,

    /// <summary>The kind could not be read past <see cref="IlCensusTerminal.Position"/>.</summary>
    Failed,
}

/// <summary>The stage at which a cursor failed.</summary>
public enum IlCensusFailureStage
{
    /// <summary>The row's own identity could not be formed, before its presence was read.</summary>
    Identity,

    /// <summary>The body could not be read: decoded, resolved, or validated.</summary>
    Reading,
}

/// <summary>
/// The terminal event of an <see cref="IlCensusCursor"/>: the inspection outcome
/// of Finding nomenclature, with the stage and IL position a failure was reached
/// at, which draining lowers into the <see cref="InspectionError"/>'s reason.
/// </summary>
public sealed record IlCensusTerminal
{
    IlCensusTerminal(
        IlCensusTerminalKind kind,
        FindingInspectionAbsenceKind? absenceKind,
        IlCensusFailureStage? stage,
        int position,
        string? reason)
    {
        Kind = kind;
        AbsenceKind = absenceKind;
        Stage = stage;
        Position = position;
        Reason = reason;
    }

    public IlCensusTerminalKind Kind { get; }

    /// <summary>The typed absence kind of an <see cref="IlCensusTerminalKind.Absent"/> terminal.</summary>
    public FindingInspectionAbsenceKind? AbsenceKind { get; }

    /// <summary>The stage of an <see cref="IlCensusTerminalKind.Failed"/> terminal.</summary>
    public IlCensusFailureStage? Stage { get; }

    /// <summary>
    /// The IL offset the cursor stopped at: the body length for a complete
    /// census, the offset the kind could not read past for a failure, and zero
    /// for an absence or an identity failure.
    /// </summary>
    public int Position { get; }

    /// <summary>The absence detail or the failure reason.</summary>
    public string? Reason { get; }

    public static IlCensusTerminal Complete(int bodyLength)
        => new(IlCensusTerminalKind.Complete, absenceKind: null, stage: null, bodyLength, reason: null);

    public static IlCensusTerminal Absent(FindingInspectionAbsenceKind kind, string? detail)
        => new(IlCensusTerminalKind.Absent, kind, stage: null, position: 0, detail);

    public static IlCensusTerminal Failed(IlCensusFailureStage stage, int position, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(IlCensusTerminalKind.Failed, absenceKind: null, stage, position, reason);
    }
}

/// <summary>
/// The census cursor of the IL kind (<c>docs/design/finding-census.md#the-cursor</c>):
/// a one-sided producer over one method body that yields the body's canonical
/// operations one per <see cref="Advance"/>, each instruction and then each
/// exception clause in declaration order, under one named policy, and ends in
/// exactly one <see cref="IlCensusTerminal"/>. The atom at a position is the
/// operation <see cref="IlFindings.Inspect(MethodInstructions?, MetadataReader?, FindingSubject, IlBodyDiffNormalization)"/>
/// places at that ordinal, so <see cref="Drain"/> and <c>Inspect</c> are one
/// operation at two speeds, and <see cref="IlBodyDiff"/>'s canonicalization is
/// the same drain: there is one producer of canonical IL.
/// </summary>
/// <remarks>
/// Over raw IL the cursor runs a boundary pre-scan that reads instruction
/// lengths and resolves nothing, then decodes one instruction and resolves
/// one operand per advance, memoized by its <see cref="IlCensusSide"/>. An instruction the decoder
/// rejects ends the pre-scan at its offset: the cursor yields every atom
/// before that offset and ends <see cref="IlCensusTerminalKind.Failed"/> there,
/// and a branch or switch target at or past that offset is carried as
/// <see cref="CanonicalIlOperation.UndecidedTarget"/>. A target that starts no
/// instruction of the body, starts one that follows a prefix, or lies at or
/// past the body's end is undecided too, and a stream that completes with one
/// ends failed rather than complete, as the decoder rejects it. Exception
/// clauses are validated by the decoder's own rules once every instruction is
/// read, so a clause atom never carries a boundary the alignment cannot place.
/// Over an already decoded <see cref="MethodInstructions"/> the pre-scan is that
/// decode; an incomplete decode retains no prefix, so the cursor fails at
/// position zero with the decode's reason.
/// </remarks>
public sealed class IlCensusCursor
{
    readonly FindingSubject? _subject;
    readonly IlCensusSide? _side;
    readonly IlBodyDiffNormalization _policy;
    readonly IlBodyDiff.InstructionMap _map;

    // Raw source: the IL, its regions, the pre-scan fault, and the instructions
    // decoded so far, which the clause stage validates regions against.
    readonly byte[]? _il;
    readonly IReadOnlyCollection<ExceptionRegion>? _rawRegions;
    readonly int _faultOffset;
    readonly string? _faultReason;
    readonly ImmutableArray<DecodedInstruction>.Builder? _collected;
    int _nextOffset;

    // Decoded source: the instructions and validated regions of the decode.
    readonly ImmutableArray<DecodedInstruction> _decoded;

    ImmutableArray<ExceptionRegionModel> _clauses;
    bool _clausesReady;
    int _clauseIndex;
    int _firstUndecidedOffset = -1;
    int _position;
    CanonicalIlOperation? _current;
    IlCensusTerminal? _terminal;

    IlCensusCursor(FindingSubject? subject, IlCensusTerminal terminal)
    {
        _subject = subject;
        _terminal = terminal;
        _map = IlBodyDiff.InstructionMap.Empty;
        _faultOffset = -1;
    }

    IlCensusCursor(
        FindingSubject? subject,
        IlCensusSide? side,
        IlBodyDiffNormalization policy,
        MethodInstructions body)
    {
        _subject = subject;
        _side = side;
        _policy = policy;
        _decoded = body.Instructions;
        _map = IlBodyDiff.InstructionMap.FromInstructions(body.Instructions);
        _clauses = body.Blocks.Regions;
        _clausesReady = true;
        _faultOffset = -1;
    }

    IlCensusCursor(
        FindingSubject? subject,
        IlCensusSide? side,
        IlBodyDiffNormalization policy,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        _subject = subject;
        _side = side;
        _policy = policy;
        _il = il;
        _rawRegions = exceptionRegions;
        _map = IlBodyDiff.InstructionMap.Scan(il, out _faultOffset, out _faultReason);
        _collected = ImmutableArray.CreateBuilder<DecodedInstruction>(_map.InstructionCount);
    }

    /// <summary>The policy the cursor's atoms are read under.</summary>
    public IlBodyDiffNormalization Policy => _policy;

    /// <summary>The atom the last successful <see cref="Advance"/> yielded.</summary>
    public CanonicalIlOperation? Current => _current;

    /// <summary>The ordinal the next atom takes: the number of atoms yielded so far.</summary>
    public int Position => _position;

    /// <summary>The terminal, once <see cref="Advance"/> has returned false.</summary>
    public IlCensusTerminal? Terminal => _terminal;

    /// <summary>
    /// A cursor over an already decoded body under <paramref name="policy"/>,
    /// with a side of its own for the operands it resolves. A null body is a
    /// method with no IL body, <see cref="FindingInspectionAbsenceKind.NoApplicableInput"/>.
    /// </summary>
    public static IlCensusCursor Create(
        MethodInstructions? body,
        MetadataReader? reader,
        FindingSubject subject,
        IlBodyDiffNormalization policy = IlBodyDiffNormalization.None)
    {
        ArgumentNullException.ThrowIfNull(subject);
        IlBodyDiff.RequireSingleSidePolicy(policy);
        if (body is null)
            return new IlCensusCursor(subject, IlCensusTerminal.Absent(FindingInspectionAbsenceKind.NoApplicableInput, "Method has no IL body."));
        return Create(body, Side(reader, policy), policy, subject);
    }

    /// <summary>
    /// A cursor over an already decoded body through its side, under the
    /// side's policy: the operands it resolves are the side's memoized keys.
    /// </summary>
    public static IlCensusCursor Create(IlCensusSide side, MethodInstructions? body, FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(subject);
        if (body is null)
            return new IlCensusCursor(subject, IlCensusTerminal.Absent(FindingInspectionAbsenceKind.NoApplicableInput, "Method has no IL body."));
        return Create(body, side, side.Policy, subject);
    }

    /// <summary>A cursor over a method definition read from its image, with a side of its own.</summary>
    public static IlCensusCursor Create(
        PEReader pe,
        MetadataReader reader,
        MethodDefinitionHandle method,
        FindingSubject subject,
        IlBodyDiffNormalization policy = IlBodyDiffNormalization.None)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return Create(IlCensusSide.Create(reader, policy), pe, method, subject);
    }

    /// <summary>A cursor over a method definition read from its image, through the image's side.</summary>
    public static IlCensusCursor Create(
        IlCensusSide side,
        PEReader pe,
        MethodDefinitionHandle method,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(pe);
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(subject);
        if (method.IsNil)
            throw new ArgumentException("Method handle must not be nil.", nameof(method));
        MetadataReader reader = side.Reader;

        // The row's identity is its definition row; its presence is the RVA,
        // read before the body. A body the image cannot read after a non-zero
        // RVA is present and unreadable: a Reading failure, never an identity one.
        int rva;
        try
        {
            rva = reader.GetMethodDefinition(method).RelativeVirtualAddress;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            return new IlCensusCursor(subject, IlCensusTerminal.Failed(IlCensusFailureStage.Identity, position: 0, ex.Message));
        }
        if (rva == 0)
            return new IlCensusCursor(subject, IlCensusTerminal.Absent(FindingInspectionAbsenceKind.NoApplicableInput, "Method has no IL body."));

        MethodBodyBlock methodBody;
        try
        {
            methodBody = pe.GetMethodBody(rva);
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            return new IlCensusCursor(subject, IlCensusTerminal.Failed(IlCensusFailureStage.Reading, position: 0, ex.Message));
        }

        return Create(side, methodBody.GetILBytes() ?? [], methodBody.ExceptionRegions, subject);
    }

    /// <summary>A cursor over raw IL and its exception regions, with a side of its own.</summary>
    public static IlCensusCursor Create(
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        MetadataReader? reader,
        FindingSubject subject,
        IlBodyDiffNormalization policy = IlBodyDiffNormalization.None)
    {
        ArgumentNullException.ThrowIfNull(il);
        ArgumentNullException.ThrowIfNull(exceptionRegions);
        ArgumentNullException.ThrowIfNull(subject);
        IlBodyDiff.RequireSingleSidePolicy(policy);
        return new IlCensusCursor(subject, Side(reader, policy), policy, il, exceptionRegions);
    }

    /// <summary>A cursor over raw IL and its exception regions, through the image's side.</summary>
    public static IlCensusCursor Create(
        IlCensusSide side,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(il);
        ArgumentNullException.ThrowIfNull(exceptionRegions);
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(subject);
        return new IlCensusCursor(subject, side, side.Policy, il, exceptionRegions);
    }

    /// <summary>
    /// The producer <see cref="IlBodyDiff"/> canonicalizes through, with a
    /// side that may carry both sides' generated-ordinal correspondence.
    /// </summary>
    internal static IlCensusCursor Create(
        MethodInstructions body,
        IlCensusSide? side,
        IlBodyDiffNormalization policy,
        FindingSubject? subject = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.IsComplete)
        {
            return new IlCensusCursor(
                subject,
                IlCensusTerminal.Failed(IlCensusFailureStage.Reading, position: 0, body.Blocks.IncompleteReason ?? "body decode failed"));
        }

        return new IlCensusCursor(subject, side, policy, body);
    }

    static IlCensusSide? Side(MetadataReader? reader, IlBodyDiffNormalization policy)
        => reader is null ? null : IlCensusSide.Create(reader, policy);

    /// <summary>
    /// Yields the next atom into <see cref="Current"/>, or ends the cursor and
    /// returns false with <see cref="Terminal"/> set. Never advances past its
    /// terminal.
    /// </summary>
    public bool Advance()
    {
        if (_terminal is not null)
            return false;

        if (_position < _map.InstructionCount)
        {
            if (!TryNextInstruction(out DecodedInstruction? instruction, out string? decodeFailure, out int decodeOffset))
                return Fail(decodeOffset, decodeFailure);
            if (!IlBodyDiff.TryToOperation(instruction, _side, _policy, _map, out CanonicalIlOperation operation, out string? failure))
                return Fail(instruction.Offset, failure ?? "operand resolution failed");
            if (_firstUndecidedOffset < 0 && operation.TargetIndexes.Contains(CanonicalIlOperation.UndecidedTarget))
                _firstUndecidedOffset = operation.Offset;
            _collected?.Add(instruction);
            return Yield(operation);
        }

        if (!_clausesReady)
        {
            // The stream is read to its end: the pre-scan's fault, then the
            // decoder's own rules over the whole body (a target that starts no
            // admitted instruction or leaves the body, and the clause table),
            // whose reasons are the ones an incomplete decode reports.
            if (_faultOffset >= 0)
                return Fail(_faultOffset, _faultReason ?? "body decode failed");
            int undecided = _firstUndecidedOffset >= 0 ? _firstUndecidedOffset : _map.IlLength;
            if (!TryValidateClauses(out string? clauseFailure))
                return Fail(undecided, clauseFailure);
            if (_firstUndecidedOffset >= 0)
                return Fail(undecided, $"Branch target of the instruction at IL_{undecided:X4} does not align with an instruction.");
            _clausesReady = true;
        }

        if (_clauseIndex < _clauses.Length)
        {
            ExceptionRegionModel clause = _clauses[_clauseIndex];
            if (!IlBodyDiff.TryToClause(clause, _side, _map, out CanonicalIlOperation atom, out string? failure))
                return Fail(clause.TryStart, failure ?? "catch Type resolution failed");
            _clauseIndex++;
            return Yield(atom);
        }

        _current = null;
        _terminal = IlCensusTerminal.Complete(_map.IlLength);
        return false;
    }

    /// <summary>
    /// Drains the cursor into the kind's inspection: the atoms as
    /// <see cref="Finding{T}"/> with their matcher projection and ordinal, then
    /// the terminal lowered to <see cref="FindingInspection{T}"/>. This is
    /// <see cref="IlFindings.Inspect(MethodInstructions?, MetadataReader?, FindingSubject, IlBodyDiffNormalization)"/>.
    /// Sealing projects each operand's text as well as its key string: a
    /// sealed census is retained and may be shown after its side's reader is
    /// disposed, and a side formats each distinct operand once.
    /// </summary>
    public FindingInspection<CanonicalIlOperation> Drain()
    {
        if (_subject is null)
            throw new InvalidOperationException("Draining a cursor into an inspection requires its subject.");
        if (_position != 0)
            throw new InvalidOperationException("A cursor is drained from its first position.");

        var atoms = ImmutableArray.CreateBuilder<Finding<CanonicalIlOperation>>();
        while (Advance())
        {
            CanonicalIlOperation operation = _current!;
            operation.Operand?.Seal();
            atoms.Add(new Finding<CanonicalIlOperation>(
                _subject,
                IlFindings.OperationDescriptor,
                new FindingKey(IlFindings.GetIdentityKey(operation)),
                operation,
                Ordinal: atoms.Count));
        }

        IlCensusTerminal terminal = _terminal!;
        return terminal.Kind switch
        {
            IlCensusTerminalKind.Complete => new FindingInspection<CanonicalIlOperation>.Complete(atoms.ToImmutable()),
            IlCensusTerminalKind.Absent => new FindingInspection<CanonicalIlOperation>.Absent(terminal.AbsenceKind!.Value, terminal.Reason),
            _ => new FindingInspection<CanonicalIlOperation>.Failed(
                new InspectionError(_subject, IlFindings.InspectionDescriptor, terminal.Reason!)),
        };
    }

    bool TryNextInstruction(
        [NotNullWhen(true)] out DecodedInstruction? instruction,
        [NotNullWhen(false)] out string? failure,
        out int failureOffset)
    {
        if (_il is null)
        {
            instruction = _decoded[_position];
            failure = null;
            failureOffset = -1;
            return true;
        }

        int offset = _nextOffset;
        try
        {
            if (InstructionDecoder.TryDecodeNext(_il, ref _nextOffset, default, out instruction))
            {
                failure = null;
                failureOffset = -1;
                return true;
            }

            failure = "The decoder reached the end before the IL length.";
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            instruction = null;
            failure = ex.Message;
        }

        failureOffset = offset;
        return false;
    }

    bool TryValidateClauses([NotNullWhen(false)] out string? failure)
    {
        if (_il is null)
        {
            failure = null;
            return true;
        }

        try
        {
            BlockGraph graph = BlockGraph.Build(_map.IlLength, _collected!.ToImmutable(), _rawRegions!);
            if (!graph.IsComplete)
            {
                failure = graph.IncompleteReason ?? "body decode failed";
                return false;
            }

            _clauses = graph.Regions;
            failure = null;
            return true;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            failure = ex.Message;
            return false;
        }
    }

    bool Yield(CanonicalIlOperation operation)
    {
        _current = operation;
        _position++;
        return true;
    }

    bool Fail(int position, string reason)
    {
        _current = null;
        _terminal = IlCensusTerminal.Failed(IlCensusFailureStage.Reading, position, reason);
        return false;
    }

    static bool IsMalformed(Exception ex)
        => ex is BadImageFormatException
            or InvalidProgramException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException;
}
