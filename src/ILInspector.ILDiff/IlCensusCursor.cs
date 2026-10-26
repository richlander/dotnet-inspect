using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

using Inspector.Findings;

namespace ILInspector.ILDiff;

/// <summary>
/// The census cursor of the IL kind (<c>docs/design/finding-census.md#the-cursor</c>):
/// a one-sided producer over one method body that yields the body's canonical
/// operations one per <see cref="Advance"/>, each instruction and then each
/// exception clause in declaration order, under one named policy, and ends in
/// exactly one <see cref="FindingCursorTerminal"/>. The atom at a position is the
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
/// before that offset and ends <see cref="FindingCursorTerminalKind.Failed"/> there,
/// and a branch or switch target at or past that offset is carried as
/// <see cref="CanonicalIlOperation.UndecidedTarget"/>. A target that starts no
/// instruction of the body, starts one that follows a prefix, or lies at or
/// past the body's end is undecided too, and a stream that completes with one
/// ends failed rather than complete, as the decoder rejects it. Exception
/// clauses are validated by the decoder's own rules once every instruction is
/// read, through its exception-flow topology over the cursor's instruction
/// map, without the decoder's blocks or any retained instruction, so a clause
/// atom never carries a boundary the alignment cannot place; a body that
/// declares no exception regions has no clause table.
/// Over an already decoded <see cref="MethodInstructions"/> the pre-scan is that
/// decode; an incomplete decode retains no prefix, so the cursor fails at
/// position zero with the decode's reason.
/// </remarks>
public sealed class IlCensusCursor : IFindingCursor<CanonicalIlOperation>
{
    readonly FindingSubject? _subject;
    readonly IlCensusSide? _side;
    readonly IlBodyDiffNormalization _policy;
    readonly IlBodyDiff.InstructionMap _map;

    // Raw source: the IL, its regions, and the pre-scan fault; the clause
    // stage validates regions against the instruction map.
    readonly byte[]? _il;
    readonly IReadOnlyCollection<ExceptionRegion>? _rawRegions;
    readonly int _faultOffset;
    readonly string? _faultReason;
    int _nextOffset;

    // Decoded source: the instructions and validated regions of the decode.
    readonly ImmutableArray<DecodedInstruction> _decoded;

    ImmutableArray<ExceptionRegionModel> _clauses;
    bool _clausesReady;
    int _clauseIndex;
    int _firstUndecidedOffset = -1;
    int _position;
    CanonicalIlOperation? _current;
    FindingCursorTerminal? _terminal;

    IlCensusCursor(FindingSubject? subject, IlBodyDiffNormalization policy, FindingCursorTerminal terminal)
    {
        _subject = subject;
        _policy = policy;
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
    }

    /// <summary>The policy the cursor's atoms are read under.</summary>
    public IlBodyDiffNormalization Policy => _policy;

    /// <summary>The atom the last successful <see cref="Advance"/> yielded.</summary>
    public CanonicalIlOperation? Current => _current;

    /// <summary>The ordinal the next atom takes: the number of atoms yielded so far.</summary>
    public int Position => _position;

    /// <summary>The terminal, once <see cref="Advance"/> has returned false.</summary>
    public FindingCursorTerminal? Terminal => _terminal;

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
            return new IlCensusCursor(subject, policy, FindingCursorTerminal.Absent(FindingInspectionAbsenceKind.NoApplicableInput, "Method has no IL body."));
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
            return new IlCensusCursor(subject, side.Policy, FindingCursorTerminal.Absent(FindingInspectionAbsenceKind.NoApplicableInput, "Method has no IL body."));
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
        ArgumentNullException.ThrowIfNull(subject);
        return OverMethod(side, pe, method, subject);
    }

    /// <summary>
    /// A cursor over a method definition for a reader that never drains it,
    /// such as Exists, which needs no subject; <see cref="Drain"/> refuses it.
    /// </summary>
    public static IlCensusCursor Create(IlCensusSide side, PEReader pe, MethodDefinitionHandle method)
        => OverMethod(side, pe, method, subject: null);

    static IlCensusCursor OverMethod(
        IlCensusSide side,
        PEReader pe,
        MethodDefinitionHandle method,
        FindingSubject? subject)
    {
        ArgumentNullException.ThrowIfNull(pe);
        ArgumentNullException.ThrowIfNull(side);
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
            return new IlCensusCursor(subject, side.Policy, FindingCursorTerminal.Failed(FindingCursorFailureStage.Identity, position: 0, ex.Message));
        }
        if (rva == 0)
            return new IlCensusCursor(subject, side.Policy, FindingCursorTerminal.Absent(FindingInspectionAbsenceKind.NoApplicableInput, "Method has no IL body."));

        MethodBodyBlock methodBody;
        try
        {
            methodBody = pe.GetMethodBody(rva);
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            return new IlCensusCursor(subject, side.Policy, FindingCursorTerminal.Failed(FindingCursorFailureStage.Reading, position: 0, ex.Message));
        }

        return new IlCensusCursor(subject, side, side.Policy, methodBody.GetILBytes() ?? [], methodBody.ExceptionRegions);
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
                policy,
                FindingCursorTerminal.Failed(FindingCursorFailureStage.Reading, position: 0, body.Blocks.IncompleteReason ?? "body decode failed"));
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
            return Yield(operation);
        }

        if (!_clausesReady)
        {
            // The stream is read to its end: the pre-scan's fault, then a
            // target that starts no admitted instruction or leaves the body,
            // which the decoder rejects before any clause and the cursor
            // reports in its own words, then the clause table by the
            // decoder's rules, whose reason is the one an incomplete decode
            // reports.
            if (_faultOffset >= 0)
                return Fail(_faultOffset, _faultReason ?? "body decode failed");
            if (_firstUndecidedOffset >= 0)
                return Fail(_firstUndecidedOffset, $"Branch target of the instruction at IL_{_firstUndecidedOffset:X4} does not align with an instruction.");
            if (!TryValidateClauses(out string? clauseFailure))
                return Fail(_map.IlLength, clauseFailure);
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
        _terminal = FindingCursorTerminal.Complete(_map.IlLength);
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

        FindingCursorTerminal terminal = _terminal!;
        return terminal.Kind switch
        {
            FindingCursorTerminalKind.Complete => new FindingInspection<CanonicalIlOperation>.Complete(atoms.ToImmutable()),
            FindingCursorTerminalKind.Absent => new FindingInspection<CanonicalIlOperation>.Absent(terminal.AbsenceKind!.Value, terminal.Reason),
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
        if (_rawRegions!.Count == 0)
        {
            // No exception regions: no clause table to validate and no clause
            // atoms to yield.
            _clauses = [];
            failure = null;
            return true;
        }

        try
        {
            // The decoder's clause-table rules are its exception-flow
            // topology's region rules; its blocks, its edges, and the
            // exception-flow facts' transfer validation, which the block graph
            // never consulted, are not needed to apply them, and the map
            // answers the boundaries its instruction list would.
            ExceptionClauseTable table = ExceptionFlowTopology.CreateTable(_map.IlLength, _map, _rawRegions);
            if (table.IncompleteReason is not null)
            {
                failure = table.IncompleteReason;
                return false;
            }

            _clauses = table.Models;
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
        _terminal = FindingCursorTerminal.Failed(FindingCursorFailureStage.Reading, position, reason);
        return false;
    }

    static bool IsMalformed(Exception ex)
        => ex is BadImageFormatException
            or InvalidProgramException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException;
}
