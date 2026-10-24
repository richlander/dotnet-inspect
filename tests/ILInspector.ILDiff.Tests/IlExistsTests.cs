using System.Reflection.Metadata;

using ILInspector.ILDiff;
using Inspector.Findings;

namespace ILInspector.ILDiff.Tests;

/// <summary>
/// Exists over two IL cursors (<c>docs/design/finding-census.md#exists</c>):
/// the fault-order and laziness gates over raw IL, and the Exists table read
/// top down over scripted cursors, identity first, then presence, then reading.
/// </summary>
public sealed class IlExistsTests
{
    const byte Nop = 0x00, Ldc0 = 0x16, Ldc1 = 0x17, Pop = 0x26, Ret = 0x2A, BrS = 0x2B;
    const byte Undefined = 0x24;

    static readonly FindingSubject Subject = new("M", "M");

    [Fact]
    public void UnchangedBody_ReadsEveryAtomOnce()
    {
        FindingExistsResult result = Exists([Ldc0, Pop, Ret], [Ldc0, Pop, Ret]);
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Unchanged, 3, 3), result);
    }

    [Fact]
    public void DifferenceAtTheFirstAtom_AdvancesEachCursorOnce()
    {
        FindingExistsResult result = Exists([Ldc0, Pop, Ret], [Ldc1, Pop, Ret]);
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 1, 1), result);
    }

    [Fact]
    public void DifferenceBeforeAnUndefinedOpcode_IsChanged()
    {
        Assert.Equal(FindingExistsDecision.Changed, Exists([Ldc0, Undefined, Ret], [Ldc1, Undefined, Ret]).Decision);
    }

    [Fact]
    public void UndefinedOpcodeBeforeAnyDifference_IsIndeterminate_OnEitherSideAndOnBoth()
    {
        Assert.Equal(FindingExistsDecision.Indeterminate, Exists([Ldc0, Undefined, Ret], [Ldc0, Undefined, Ret]).Decision);
        Assert.Equal(FindingExistsDecision.Indeterminate, Exists([Ldc0, Undefined], [Ldc0, Pop, Ret]).Decision);
        Assert.Equal(FindingExistsDecision.Indeterminate, Exists([Ldc0, Pop, Ret], [Ldc0, Undefined]).Decision);
    }

    [Fact]
    public void OnlyUndecidedTargetsDifferBeforeTheFault_IsIndeterminate()
    {
        // br.s over one instruction: the old target is the ret; the new target
        // is the undefined opcode's offset, at the pre-scan's fault, so it is
        // undecided and the position pends; the fault then decides.
        FindingExistsResult result = Exists([BrS, 0x01, Nop, Ret], [BrS, 0x01, Nop, Undefined]);
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Indeterminate, 3, 2), result);
    }

    [Fact]
    public void DifferenceAfterAPendingPosition_IsChanged()
    {
        FindingExistsResult result = Exists([BrS, 0x01, Nop, Ret], [BrS, 0x01, Ldc0, Undefined]);
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 2, 2), result);
    }

    [Fact]
    public void RetargetedBranch_IsDecidedOnceBothStreamsComplete()
    {
        // br.s back to the nop on one side and to itself on the other: both
        // targets are decided, and the difference is decided at the end, as
        // the walk decides targets once the instruction map is complete.
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 3, 3), Exists([Nop, BrS, 0xFD, Ret], [Nop, BrS, 0xFE, Ret]));
        // The same retargeting followed by a fault is undecided, on either side and on both.
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Indeterminate, 3, 2), Exists([Nop, BrS, 0xFD, Ret], [Nop, BrS, 0xFE, Undefined]));
        Assert.Equal(FindingExistsDecision.Indeterminate, Exists([Nop, BrS, 0xFD, Undefined], [Nop, BrS, 0xFE, Ret]).Decision);
        Assert.Equal(FindingExistsDecision.Indeterminate, Exists([Nop, BrS, 0xFD, Undefined], [Nop, BrS, 0xFE, Undefined]).Decision);
        // A non-target difference after the retargeting decides at once.
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 3, 3), Exists([Nop, BrS, 0xFD, Ldc0, Ret], [Nop, BrS, 0xFE, Ldc1, Ret]));
    }

    [Fact]
    public void Presence_IsDecidedBeforeReading_WithoutDecodingAnAtom()
    {
        IlCensusCursor absent() => IlCensusCursor.Create((MethodInstructions?)null, reader: null, Subject, IlBodyDiffPolicy.Body);
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Unchanged, 0, 0), IlFindings.Exists(absent(), absent()));
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 0, 0), IlFindings.Exists(absent(), Raw([Nop, Ret])));
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 0, 0), IlFindings.Exists(Raw([Nop, Ret]), absent()));
        // Undecodable on one side and absent on the other is a difference, not undecided.
        Assert.Equal(FindingExistsDecision.Changed, IlFindings.Exists(absent(), Raw([Undefined])).Decision);
    }

    [Fact]
    public void CursorsUnderTwoPolicies_AreRefused()
    {
        IlCensusCursor none = IlCensusCursor.Create([Ret], Array.Empty<ExceptionRegion>(), reader: null, Subject);
        IlCensusCursor absentBody = IlCensusCursor.Create((MethodInstructions?)null, reader: null, Subject, IlBodyDiffPolicy.Body);
        Assert.Equal(IlBodyDiffPolicy.Body, absentBody.Policy);
        Assert.Throws<ArgumentException>(() => IlFindings.Exists(none, Raw([Ret])));
        Assert.Throws<ArgumentException>(() => IlFindings.Exists(none, absentBody));
    }

    [Fact]
    public void ACompleteCensusAgainstALongerOne_IsChangedAtTheEnd()
    {
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 1, 2), Exists([Nop], [Nop, Nop]));
    }

    [Fact]
    public void ExistsTable_IsReadIdentityThenPresenceThenReading()
    {
        var identityFailed = FindingCursorTerminal.Failed(FindingCursorFailureStage.Identity, 0, "malformed row");
        var readingFailed = FindingCursorTerminal.Failed(FindingCursorFailureStage.Reading, 7, "undefined opcode");
        var noBody = FindingCursorTerminal.Absent(FindingInspectionAbsenceKind.NoApplicableInput, null);
        var noRow = FindingCursorTerminal.Absent(FindingInspectionAbsenceKind.SubjectAbsent, null);
        var complete = FindingCursorTerminal.Complete(0);

        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(identityFailed), Scripted(noBody)).Decision);
        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(complete, "a"), Scripted(identityFailed)).Decision);
        // A terminal known before reading decides without an atom being read on either side.
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 0, 0), Decide(Scripted(noBody), Scripted(complete, "a")));
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Indeterminate, 0, 0), Decide(Scripted(complete, "a"), Scripted(readingFailed)));
        Assert.Equal(FindingExistsDecision.Unchanged, Decide(Scripted(noBody), Scripted(noBody)).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(noBody), Scripted(noRow)).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(noBody), Scripted(readingFailed)).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(noBody), Scripted(complete)).Decision);
        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(readingFailed, "a"), Scripted(complete, "a")).Decision);
        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(readingFailed), Scripted(readingFailed)).Decision);
        Assert.Equal(FindingExistsDecision.Unchanged, Decide(Scripted(complete, "a", "b"), Scripted(complete, "a", "b")).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(complete, "a", "b"), Scripted(complete, "a", "c")).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(complete, "a"), Scripted(complete, "a", "b")).Decision);
        // Two complete censuses with an undecided position cannot arise from a cursor; read as undecided, never equal.
        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(complete, "a?"), Scripted(complete, "a")).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(complete, "a?", "b"), Scripted(complete, "a", "c")).Decision);
        // A deferred difference is decided by two complete streams, and is undecided before a fault.
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 2, 2), Decide(Scripted(complete, "a!", "b"), Scripted(complete, "a", "b")));
        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(readingFailed, "a!", "b"), Scripted(complete, "a", "b")).Decision);
        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(complete, "a!", "b?"), Scripted(complete, "a", "b")).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(complete, "a!", "b"), Scripted(complete, "a", "c")).Decision);
    }

    static FindingExistsResult Exists(byte[] oldIl, byte[] newIl) => IlFindings.Exists(Raw(oldIl), Raw(newIl));

    static IlCensusCursor Raw(byte[] il)
        => IlCensusCursor.Create(il, Array.Empty<ExceptionRegion>(), reader: null, Subject, IlBodyDiffPolicy.Body);

    static FindingExistsResult Decide(ScriptedCursor oldCursor, ScriptedCursor newCursor)
    {
        FindingExistsResult result = FindingExists.Decide(oldCursor, newCursor, default(ScriptedEquality));
        Assert.False(oldCursor.AdvancedPastTerminal);
        Assert.False(newCursor.AdvancedPastTerminal);
        return result;
    }

    static ScriptedCursor Scripted(FindingCursorTerminal terminal, params string[] atoms) => new(atoms, terminal);

    // A trailing '?' marks an atom whose target is undecided, a trailing '!'
    // one whose decided target differs: equal otherwise, the position pends.
    readonly struct ScriptedEquality : IFindingAtomEquality<string>
    {
        public FindingAtomEquality Compare(string oldAtom, string newAtom)
        {
            bool undecided = oldAtom.EndsWith('?') || newAtom.EndsWith('?');
            bool deferred = oldAtom.EndsWith('!') || newAtom.EndsWith('!');
            if (oldAtom.TrimEnd('?', '!') != newAtom.TrimEnd('?', '!'))
                return FindingAtomEquality.Unequal;
            return undecided ? FindingAtomEquality.Undecided
                : deferred ? FindingAtomEquality.Deferred
                : FindingAtomEquality.Equal;
        }
    }

    // A cursor with no atoms is created at its terminal, as an absent, unreadable, or identity-failed row's cursor is.
    sealed class ScriptedCursor(string[] atoms, FindingCursorTerminal terminal) : IFindingCursor<string>
    {
        public string? Current { get; private set; }

        public int Position { get; private set; }

        public FindingCursorTerminal? Terminal { get; private set; } = atoms.Length == 0 ? terminal : null;

        public bool AdvancedPastTerminal { get; private set; }

        public bool Advance()
        {
            if (Terminal is not null)
            {
                AdvancedPastTerminal = true;
                return false;
            }
            if (Position < atoms.Length)
            {
                Current = atoms[Position++];
                return true;
            }
            Current = null;
            Terminal = terminal;
            return false;
        }
    }
}
