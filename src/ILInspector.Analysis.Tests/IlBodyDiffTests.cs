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
        Assert.Empty(diff.Rows);
    }

    [Fact]
    public void Compare_InsertedInstruction_AlignsUnchangedSuffix()
    {
        var left = MethodInstructions.Decode([0x00, 0x2a], 2, []);
        var right = MethodInstructions.Decode([0x00, 0x00, 0x2a], 3, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.False(diff.IsExact);
        var added = Assert.Single(diff.Rows, row => row.Kind == IlDiffKind.Add);
        Assert.Equal(0x0001, added.Operation.Offset);
        Assert.Equal("nop", added.Operation.OpcodeFamily);
        Assert.DoesNotContain(diff.Rows, row => row.Kind == IlDiffKind.Remove);
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
        Assert.Single(diff.Rows);
        Assert.Equal(IlDiffKind.Add, diff.Rows[0].Kind);
        Assert.Equal("nop", diff.Rows[0].Operation.OpcodeFamily);
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

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal(0, removed.Operation.Offset);
                Assert.Equal("br", removed.Operation.OpcodeFamily);
                Assert.Equal("IL_0005", removed.Operation.Operand?.Value);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal(0, added.Operation.Offset);
                Assert.Equal("br", added.Operation.OpcodeFamily);
                Assert.Equal("IL_0003", added.Operation.Operand?.Value);
            });
    }

    [Fact]
    public void Compare_RetargetedBranchAndRemovedInstruction_ReportsInProgramOrder()
    {
        var left = MethodInstructions.Decode([0x2b, 0x03, 0x00, 0x16, 0x2a, 0x17, 0x2a], 7, []);
        var right = MethodInstructions.Decode([0x2b, 0x00, 0x16, 0x2a, 0x17, 0x2a], 6, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.Collection(
            diff.Rows,
            removedBranch =>
            {
                Assert.Equal(IlDiffKind.Remove, removedBranch.Kind);
                Assert.Equal(0, removedBranch.Operation.Offset);
                Assert.Equal("br", removedBranch.Operation.OpcodeFamily);
                Assert.Equal("IL_0005", removedBranch.Operation.Operand?.Value);
            },
            addedBranch =>
            {
                Assert.Equal(IlDiffKind.Add, addedBranch.Kind);
                Assert.Equal(0, addedBranch.Operation.Offset);
                Assert.Equal("br", addedBranch.Operation.OpcodeFamily);
                Assert.Equal("IL_0002", addedBranch.Operation.Operand?.Value);
            },
            removedNop =>
            {
                Assert.Equal(IlDiffKind.Remove, removedNop.Kind);
                Assert.Equal(2, removedNop.Operation.Offset);
                Assert.Equal("nop", removedNop.Operation.OpcodeFamily);
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
