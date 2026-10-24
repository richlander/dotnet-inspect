namespace Inspector.Findings;

/// <summary>The terminal event a census cursor ends in (<c>docs/design/finding-census.md#the-cursor</c>).</summary>
public enum FindingCursorTerminalKind
{
    /// <summary>Every atom of the row was yielded.</summary>
    Complete,

    /// <summary>The row has no census of this kind; no atom was yielded.</summary>
    Absent,

    /// <summary>The kind could not be read past <see cref="FindingCursorTerminal.Position"/>.</summary>
    Failed,
}

/// <summary>The stage at which a cursor failed.</summary>
public enum FindingCursorFailureStage
{
    /// <summary>The row's own identity could not be formed, before its presence was read.</summary>
    Identity,

    /// <summary>The row could not be read: decoded, resolved, sorted, or validated.</summary>
    Reading,
}

/// <summary>
/// The terminal event of a census cursor: the inspection outcome of Finding
/// nomenclature, with the stage and position a failure was reached at, which
/// Exists reads and draining lowers into the <see cref="InspectionError"/>'s reason.
/// </summary>
public sealed record FindingCursorTerminal
{
    FindingCursorTerminal(
        FindingCursorTerminalKind kind,
        FindingInspectionAbsenceKind? absenceKind,
        FindingCursorFailureStage? stage,
        int position,
        string? reason)
    {
        Kind = kind;
        AbsenceKind = absenceKind;
        Stage = stage;
        Position = position;
        Reason = reason;
    }

    public FindingCursorTerminalKind Kind { get; }

    /// <summary>The typed absence kind of an <see cref="FindingCursorTerminalKind.Absent"/> terminal.</summary>
    public FindingInspectionAbsenceKind? AbsenceKind { get; }

    /// <summary>The stage of a <see cref="FindingCursorTerminalKind.Failed"/> terminal.</summary>
    public FindingCursorFailureStage? Stage { get; }

    /// <summary>
    /// The kind's position the cursor stopped at: the row's extent for a
    /// complete census, the position the kind could not read past for a
    /// failure, and zero for an absence or an identity failure.
    /// </summary>
    public int Position { get; }

    /// <summary>The absence detail or the failure reason.</summary>
    public string? Reason { get; }

    /// <summary>Whether the row's identity could not be formed, which Exists reads before presence.</summary>
    public bool IsIdentityFailure => Kind == FindingCursorTerminalKind.Failed && Stage == FindingCursorFailureStage.Identity;

    public static FindingCursorTerminal Complete(int position)
        => new(FindingCursorTerminalKind.Complete, absenceKind: null, stage: null, position, reason: null);

    public static FindingCursorTerminal Absent(FindingInspectionAbsenceKind kind, string? detail)
        => new(FindingCursorTerminalKind.Absent, kind, stage: null, position: 0, detail);

    public static FindingCursorTerminal Failed(FindingCursorFailureStage stage, int position, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(FindingCursorTerminalKind.Failed, absenceKind: null, stage, position, reason);
    }
}

/// <summary>
/// A census cursor (<c>docs/design/finding-census.md#the-cursor</c>): a
/// one-sided producer with a position, created for one row of one kind over
/// one image, that yields the row's atoms in the kind's canonical order and
/// ends in exactly one terminal, after which it is never advanced.
/// </summary>
public interface IFindingCursor<TAtom>
    where TAtom : class
{
    /// <summary>Yields the next atom into <see cref="Current"/>, or ends the cursor and returns false with <see cref="Terminal"/> set.</summary>
    bool Advance();

    /// <summary>The atom the last successful <see cref="Advance"/> yielded.</summary>
    TAtom? Current { get; }

    /// <summary>The number of atoms yielded so far.</summary>
    int Position { get; }

    /// <summary>The terminal, once <see cref="Advance"/> has returned false.</summary>
    FindingCursorTerminal? Terminal { get; }
}

/// <summary>How two atoms at one position compare, as <see cref="FindingExists"/> reads them.</summary>
public enum FindingAtomEquality
{
    /// <summary>The canonical bytes differ: the position decides <see cref="FindingExistsDecision.Changed"/>.</summary>
    Unequal,

    /// <summary>The canonical bytes are equal and every target is decided.</summary>
    Equal,

    /// <summary>
    /// The canonical bytes are equal apart from a target undecided on either
    /// side, which is left out of the comparison; the position is pending.
    /// </summary>
    EqualPending,
}

/// <summary>A kind's canonical-bytes equality of two atoms, with the undecided-target exclusion.</summary>
public interface IFindingAtomEquality<TAtom>
    where TAtom : class
{
    FindingAtomEquality Compare(TAtom oldAtom, TAtom newAtom);
}

/// <summary>The Exists decision for one row on one kind.</summary>
public enum FindingExistsDecision
{
    Unchanged,
    Changed,
    Indeterminate,
}

/// <summary>
/// One Exists decision with the atoms each cursor yielded before it, which the
/// laziness gate asserts: one per side for a pair that differs at its first
/// atom, the census length for an unchanged pair.
/// </summary>
public readonly record struct FindingExistsResult(FindingExistsDecision Decision, int OldAtoms, int NewAtoms);

/// <summary>
/// Exists over two cursors (<c>docs/design/finding-census.md#exists</c>):
/// lockstep that reads one paired row on one kind and stops at the first
/// event that decides, identity first, then presence, then reading. It builds
/// nothing: no census is sealed, no key string formed, no matcher run.
/// </summary>
public static class FindingExists
{
    /// <summary>
    /// Decides one row. Both cursors are advanced together; the decision is
    /// read from the first position at which either ends or the atoms differ,
    /// and neither cursor is advanced after it.
    /// </summary>
    public static FindingExistsResult Decide<TAtom, TEquality>(IFindingCursor<TAtom> oldCursor, IFindingCursor<TAtom> newCursor, TEquality equality)
        where TAtom : class
        where TEquality : struct, IFindingAtomEquality<TAtom>
    {
        ArgumentNullException.ThrowIfNull(oldCursor);
        ArgumentNullException.ThrowIfNull(newCursor);
        bool pending = false;
        while (true)
        {
            bool oldMore = oldCursor.Advance();
            bool newMore = newCursor.Advance();
            if (oldMore && newMore)
            {
                switch (equality.Compare(oldCursor.Current!, newCursor.Current!))
                {
                    case FindingAtomEquality.Unequal:
                        return Result(FindingExistsDecision.Changed, oldCursor, newCursor);
                    case FindingAtomEquality.EqualPending:
                        pending = true;
                        break;
                }
                continue;
            }

            FindingCursorTerminal? oldEnd = oldMore ? null : oldCursor.Terminal;
            FindingCursorTerminal? newEnd = newMore ? null : newCursor.Terminal;
            return Result(Decide(oldEnd, newEnd, pending), oldCursor, newCursor);
        }
    }

    // The Exists table, read top down: identity, then presence, then reading.
    // A null terminal is an atom at this position.
    static FindingExistsDecision Decide(FindingCursorTerminal? oldEnd, FindingCursorTerminal? newEnd, bool pending)
    {
        if (oldEnd is { IsIdentityFailure: true } || newEnd is { IsIdentityFailure: true })
            return FindingExistsDecision.Indeterminate;

        bool oldAbsent = oldEnd?.Kind == FindingCursorTerminalKind.Absent;
        bool newAbsent = newEnd?.Kind == FindingCursorTerminalKind.Absent;
        if (oldAbsent && newAbsent)
            return oldEnd!.AbsenceKind == newEnd!.AbsenceKind ? FindingExistsDecision.Unchanged : FindingExistsDecision.Changed;
        if (oldAbsent || newAbsent)
            return FindingExistsDecision.Changed;

        if (oldEnd?.Kind == FindingCursorTerminalKind.Failed || newEnd?.Kind == FindingCursorTerminalKind.Failed)
            return FindingExistsDecision.Indeterminate;

        if (oldEnd is not null && newEnd is not null)
        {
            // Both complete. An undecided target occurs only on a side that
            // ends failed, so a pending position with two complete censuses
            // is unreachable by construction; it is read as undecided, never
            // as equal.
            return pending ? FindingExistsDecision.Indeterminate : FindingExistsDecision.Unchanged;
        }

        // One complete, the other with an atom at this position.
        return FindingExistsDecision.Changed;
    }

    static FindingExistsResult Result<TAtom>(FindingExistsDecision decision, IFindingCursor<TAtom> oldCursor, IFindingCursor<TAtom> newCursor)
        where TAtom : class
        => new(decision, oldCursor.Position, newCursor.Position);
}
