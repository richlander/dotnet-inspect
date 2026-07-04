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
    public void Compare_AddedInstruction_ReportsAddedRow()
    {
        var left = MethodInstructions.Decode([0x00, 0x2a], 2, []);
        var right = MethodInstructions.Decode([0x00, 0x00, 0x2a], 3, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.False(diff.IsExact);
        var changed = Assert.Single(diff.Differences, row => row.Kind == IlBodyDiffChangeKind.Changed);
        Assert.Equal(ILOpCode.Ret, changed.OldOpCode);
        Assert.Equal(ILOpCode.Nop, changed.NewOpCode);
        var added = Assert.Single(diff.Differences, row => row.Kind == IlBodyDiffChangeKind.Added);
        Assert.Null(added.OldOpCode);
        Assert.Equal(ILOpCode.Ret, added.NewOpCode);
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
