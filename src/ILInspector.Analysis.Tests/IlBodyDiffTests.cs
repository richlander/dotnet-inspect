using ILInspector.Instructions;
using System.Reflection.Metadata;

namespace ILInspector.Analysis.Tests;

public class IlBodyDiffTests
{
    [Fact]
    public void Compare_ExactBodies_HasNoDifferences()
    {
        var left = MethodInstructions.Decode([0x00, 0x2a], 2, []);
        var right = MethodInstructions.Decode([0x00, 0x2a], 2, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.True(diff.IsExact);
        Assert.Null(diff.Failure);
        Assert.Empty(diff.Differences);
    }

    [Fact]
    public void Compare_InsertedInstruction_AlignsUnchangedSuffix()
    {
        var left = MethodInstructions.Decode([0x00, 0x2a], 2, []);
        var right = MethodInstructions.Decode([0x00, 0x00, 0x2a], 3, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.False(diff.IsExact);
        var added = Assert.Single(diff.Differences, row => row.Kind == IlBodyDiffChangeKind.Added);
        Assert.Null(added.OldOpCode);
        Assert.Equal(ILOpCode.Nop, added.NewOpCode);
        Assert.DoesNotContain(diff.Differences, row => row.Kind == IlBodyDiffChangeKind.Changed);
    }

    [Fact]
    public void Compare_BranchTargetOffsetShift_DoesNotMarkBranchChanged()
    {
        // br.s targets the final ret in both bodies. The inserted nop shifts the
        // absolute target from IL_0003 to IL_0004, but the branch operation itself
        // is unchanged for this first substrate.
        var left = MethodInstructions.Decode([0x2b, 0x01, 0x00, 0x2a], 4, []);
        var right = MethodInstructions.Decode([0x2b, 0x02, 0x00, 0x00, 0x2a], 5, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.False(diff.IsExact);
        Assert.Single(diff.Differences);
        Assert.Equal(IlBodyDiffChangeKind.Added, diff.Differences[0].Kind);
        Assert.Equal(ILOpCode.Nop, diff.Differences[0].NewOpCode);
    }

    [Fact]
    public void Compare_BranchTargetRetarget_ReportsChangedBranch()
    {
        // Same opcode sequence, but the first branch retargets from IL_0005 to
        // IL_0003. LCS can align the branch instructions, then target validation
        // must still report the control-flow change.
        var left = MethodInstructions.Decode([0x2b, 0x03, 0x00, 0x2a, 0x00, 0x2a], 6, []);
        var right = MethodInstructions.Decode([0x2b, 0x01, 0x00, 0x2a, 0x00, 0x2a], 6, []);

        var diff = IlBodyDiff.Compare(left, right);

        var changed = Assert.Single(diff.Differences);
        Assert.Equal(IlBodyDiffChangeKind.Changed, changed.Kind);
        Assert.Equal(ILOpCode.Br_s, changed.OldOpCode);
        Assert.Equal(ILOpCode.Br_s, changed.NewOpCode);
    }

    [Fact]
    public void Compare_RetargetedBranchAndRemovedInstruction_ReportsInProgramOrder()
    {
        var left = MethodInstructions.Decode([0x2b, 0x03, 0x00, 0x16, 0x2a, 0x17, 0x2a], 7, []);
        var right = MethodInstructions.Decode([0x2b, 0x00, 0x16, 0x2a, 0x17, 0x2a], 6, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.Collection(
            diff.Differences,
            changed =>
            {
                Assert.Equal(IlBodyDiffChangeKind.Changed, changed.Kind);
                Assert.Equal(0, changed.OldOffset);
                Assert.Equal(0, changed.NewOffset);
            },
            removed =>
            {
                Assert.Equal(IlBodyDiffChangeKind.Removed, removed.Kind);
                Assert.Equal(2, removed.OldOffset);
            });
    }

    [Fact]
    public void Compare_MalformedBody_ReportsFailure()
    {
        var malformed = MethodInstructions.Decode([0xfe], 1, []);
        var valid = MethodInstructions.Decode([0x2a], 1, []);

        var diff = IlBodyDiff.Compare(malformed, valid);

        Assert.False(diff.IsExact);
        Assert.NotNull(diff.Failure);
    }
}
