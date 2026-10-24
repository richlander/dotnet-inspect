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
    public void Presence_IsDecidedBeforeReading()
    {
        IlCensusCursor absent() => IlCensusCursor.Create((MethodInstructions?)null, reader: null, Subject, IlBodyDiffPolicy.Body);
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Unchanged, 0, 0), IlFindings.Exists(absent(), absent()));
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 0, 1), IlFindings.Exists(absent(), Raw([Nop, Ret])));
        Assert.Equal(new FindingExistsResult(FindingExistsDecision.Changed, 1, 0), IlFindings.Exists(Raw([Nop, Ret]), absent()));
        // Undecodable on one side and absent on the other is a difference, not undecided.
        Assert.Equal(FindingExistsDecision.Changed, IlFindings.Exists(absent(), Raw([Undefined])).Decision);
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
        Assert.Equal(FindingExistsDecision.Unchanged, Decide(Scripted(noBody), Scripted(noBody)).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(noBody), Scripted(noRow)).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(noBody), Scripted(readingFailed)).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(noBody), Scripted(complete)).Decision);
        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(readingFailed, "a"), Scripted(complete, "a")).Decision);
        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(readingFailed), Scripted(readingFailed)).Decision);
        Assert.Equal(FindingExistsDecision.Unchanged, Decide(Scripted(complete, "a", "b"), Scripted(complete, "a", "b")).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(complete, "a", "b"), Scripted(complete, "a", "c")).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(complete, "a"), Scripted(complete, "a", "b")).Decision);
        // Two complete censuses with a pending position cannot arise from a cursor; read as undecided, never equal.
        Assert.Equal(FindingExistsDecision.Indeterminate, Decide(Scripted(complete, "a?"), Scripted(complete, "a")).Decision);
        Assert.Equal(FindingExistsDecision.Changed, Decide(Scripted(complete, "a?", "b"), Scripted(complete, "a", "c")).Decision);
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

    // A trailing '?' marks an atom whose target is undecided: equal otherwise, the position pends.
    readonly struct ScriptedEquality : IFindingAtomEquality<string>
    {
        public FindingAtomEquality Compare(string oldAtom, string newAtom)
        {
            bool pending = oldAtom.EndsWith('?') || newAtom.EndsWith('?');
            if (oldAtom.TrimEnd('?') != newAtom.TrimEnd('?'))
                return FindingAtomEquality.Unequal;
            return pending ? FindingAtomEquality.EqualPending : FindingAtomEquality.Equal;
        }
    }

    sealed class ScriptedCursor(string[] atoms, FindingCursorTerminal terminal) : IFindingCursor<string>
    {
        public string? Current { get; private set; }

        public int Position { get; private set; }

        public FindingCursorTerminal? Terminal { get; private set; }

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
