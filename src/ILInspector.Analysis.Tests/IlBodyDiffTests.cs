using ILInspector.Instructions;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
    public void Compare_CompiledFixtureConstantValueChange_ReportsImmediateChange()
    {
        var left = DiffFixtureMethod("DiffFixtures.V1", "ConstantValue");
        var right = DiffFixtureMethod("DiffFixtures.V2", "ConstantValue");

        var diff = IlBodyDiff.Compare(left, right);

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal("ldc.i4", removed.Operation.OpcodeFamily);
                Assert.Equal("1", removed.Operation.Operand?.Value);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal("ldc.i4", added.Operation.OpcodeFamily);
                Assert.Equal("2", added.Operation.Operand?.Value);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureMultipleHunks_AssignsSeparateHunkIds()
    {
        var left = DiffFixtureMethod("DiffFixtures.V1", "MultipleHunks");
        var right = DiffFixtureMethod("DiffFixtures.V2", "MultipleHunks");

        var diff = IlBodyDiff.Compare(left, right);

        Assert.Collection(
            diff.Rows,
            firstRemoved =>
            {
                Assert.Equal(0, firstRemoved.HunkId);
                Assert.Equal(IlDiffKind.Remove, firstRemoved.Kind);
                Assert.Equal("ldc.i4", firstRemoved.Operation.OpcodeFamily);
                Assert.Equal("1", firstRemoved.Operation.Operand?.Value);
            },
            firstAdded =>
            {
                Assert.Equal(0, firstAdded.HunkId);
                Assert.Equal(IlDiffKind.Add, firstAdded.Kind);
                Assert.Equal("ldc.i4", firstAdded.Operation.OpcodeFamily);
                Assert.Equal("2", firstAdded.Operation.Operand?.Value);
            },
            secondRemoved =>
            {
                Assert.Equal(1, secondRemoved.HunkId);
                Assert.Equal(IlDiffKind.Remove, secondRemoved.Kind);
                Assert.Equal("ldc.i4", secondRemoved.Operation.OpcodeFamily);
                Assert.Equal("3", secondRemoved.Operation.Operand?.Value);
            },
            secondAdded =>
            {
                Assert.Equal(1, secondAdded.HunkId);
                Assert.Equal(IlDiffKind.Add, secondAdded.Kind);
                Assert.Equal("ldc.i4", secondAdded.Operation.OpcodeFamily);
                Assert.Equal("4", secondAdded.Operation.Operand?.Value);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureBranchTargetOffsetShift_DoesNotMarkBranchChanged()
    {
        var left = DiffFixtureMethod("DiffFixtures.V1", "BranchTargetOffsetShift");
        var right = DiffFixtureMethod("DiffFixtures.V2", "BranchTargetOffsetShift");

        var diff = IlBodyDiff.Compare(left, right);

        Assert.NotEmpty(diff.Rows);
        Assert.Contains(diff.Rows, row => row.Kind == IlDiffKind.Add && row.Operation.OpcodeFamily == "call");
        Assert.DoesNotContain(diff.Rows, IsBranchRow);
    }

    [Fact]
    public void Compare_CompiledFixtureBranchRetarget_ReportsChangedBranch()
    {
        var left = DiffFixtureMethod("DiffFixtures.V1", "BranchRetarget");
        var right = DiffFixtureMethod("DiffFixtures.V2", "BranchRetarget");

        var diff = IlBodyDiff.Compare(left, right);

        Assert.Contains(diff.Rows, row => row.Kind == IlDiffKind.Remove && IsBranchRow(row));
        Assert.Contains(diff.Rows, row => row.Kind == IlDiffKind.Add && IsBranchRow(row));
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

    static bool IsBranchRow(IlDiffRow row)
        => row.Operation.OpcodeFamily.StartsWith("br", StringComparison.Ordinal);

    static MethodInstructions DiffFixtureMethod(string project, string name)
    {
        using var stream = File.OpenRead(DiffFixturePath(project));
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();
        foreach (var handle in metadataReader.MethodDefinitions)
        {
            var method = metadataReader.GetMethodDefinition(handle);
            if (metadataReader.GetString(method.Name) == name)
                return MethodInstructions.Decode(peReader.GetMethodBody(method.RelativeVirtualAddress));
        }

        throw new InvalidOperationException($"Could not find method '{name}' in {project}.");
    }

    static string DiffFixturePath(string project)
    {
        var outputDirectory = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName, "..", "..", project, outputDirectory.Name, "DiffFixtureSample.dll"));
        Assert.True(File.Exists(path), $"Expected diff fixture assembly at {path}");
        return path;
    }
}
