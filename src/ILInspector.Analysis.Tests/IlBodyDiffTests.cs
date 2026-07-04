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
    public void Compare_BranchTargetInstructionChanged_DoesNotCascadeToBranch()
    {
        var left = MethodInstructions.Decode([0x2b, 0x00, 0x16, 0x2a], 4, []);
        var right = MethodInstructions.Decode([0x2b, 0x00, 0x17, 0x2a], 4, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal("ldc.i4", removed.Operation.OpcodeFamily);
                Assert.Equal("0", removed.Operation.Operand?.Value);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal("ldc.i4", added.Operation.OpcodeFamily);
                Assert.Equal("1", added.Operation.Operand?.Value);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureConstantValueChange_ReportsImmediateChange()
    {
        var diff = DiffFixtureDiff("ConstantValue");

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal("ldc.i4", removed.Operation.OpcodeFamily);
                Assert.Equal("1", removed.Operation.Operand?.Value);
                Assert.Equal("Removed IL operation 'ldc.i4 1'", removed.Message);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal("ldc.i4", added.Operation.OpcodeFamily);
                Assert.Equal("2", added.Operation.Operand?.Value);
                Assert.Equal("Added IL operation 'ldc.i4 2'", added.Message);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureStringTokenChange_ReportsStringOperandChange()
    {
        var diff = DiffFixtureDiff("StringToken");

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal("ldstr", removed.Operation.OpcodeFamily);
                Assert.Equal("string \"alpha\"", removed.Operation.Operand?.Value);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal("ldstr", added.Operation.OpcodeFamily);
                Assert.Equal("string \"beta\"", added.Operation.Operand?.Value);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureCallTokenChange_ReportsMemberOperandChange()
    {
        var diff = DiffFixtureDiff("CallToken");

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal("call", removed.Operation.OpcodeFamily);
                Assert.Contains("::Abs(", removed.Operation.Operand?.Value, StringComparison.Ordinal);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal("call", added.Operation.OpcodeFamily);
                Assert.Contains("::Sign(", added.Operation.Operand?.Value, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Compare_TokenOperandWithoutMetadata_FailsClosed()
    {
        var left = DiffFixtureMethod("DiffFixtures.V1", "StringToken");
        var right = DiffFixtureMethod("DiffFixtures.V2", "StringToken");

        var diff = IlBodyDiff.Compare(left, right);

        Assert.False(diff.IsExact);
        Assert.NotNull(diff.Failure);
        Assert.Contains("requires a MetadataReader-backed comparison", diff.Failure, StringComparison.Ordinal);
        Assert.Empty(diff.Rows);
    }

    [Fact]
    public void Compare_CompiledFixtureMultipleHunks_AssignsSeparateHunkIds()
    {
        var diff = DiffFixtureDiff("MultipleHunks");

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
        var diff = DiffFixtureDiff("BranchTargetOffsetShift");

        Assert.NotEmpty(diff.Rows);
        Assert.Contains(diff.Rows, row => row.Kind == IlDiffKind.Add && row.Operation.OpcodeFamily == "call");
        Assert.DoesNotContain(diff.Rows, IsBranchRow);
    }

    [Fact]
    public void Compare_CompiledFixtureBranchRetarget_ReportsChangedBranch()
    {
        var diff = DiffFixtureDiff("BranchRetarget");

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
                Assert.Equal("Removed IL branch 'br IL_0005'", removedBranch.Message);
            },
            addedBranch =>
            {
                Assert.Equal(IlDiffKind.Add, addedBranch.Kind);
                Assert.Equal(0, addedBranch.Operation.Offset);
                Assert.Equal("br", addedBranch.Operation.OpcodeFamily);
                Assert.Equal("IL_0002", addedBranch.Operation.Operand?.Value);
                Assert.Equal("Added IL branch 'br IL_0002'", addedBranch.Message);
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

    static IlBodyDiffResult DiffFixtureDiff(string name)
    {
        using var oldStream = File.OpenRead(DiffFixturePath("DiffFixtures.V1"));
        using var newStream = File.OpenRead(DiffFixturePath("DiffFixtures.V2"));
        using var oldPe = new PEReader(oldStream);
        using var newPe = new PEReader(newStream);
        var oldReader = oldPe.GetMetadataReader();
        var newReader = newPe.GetMetadataReader();
        return IlBodyDiff.Compare(
            oldReader,
            DiffFixtureMethodBody(oldPe, oldReader, name),
            newReader,
            DiffFixtureMethodBody(newPe, newReader, name));
    }

    static MethodInstructions DiffFixtureMethod(string project, string name)
    {
        using var stream = File.OpenRead(DiffFixturePath(project));
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();
        return MethodInstructions.Decode(DiffFixtureMethodBody(peReader, metadataReader, name));
    }

    static MethodBodyBlock DiffFixtureMethodBody(PEReader peReader, MetadataReader metadataReader, string name)
    {
        foreach (var handle in metadataReader.MethodDefinitions)
        {
            var method = metadataReader.GetMethodDefinition(handle);
            if (metadataReader.GetString(method.Name) == name)
                return peReader.GetMethodBody(method.RelativeVirtualAddress);
        }

        throw new InvalidOperationException($"Could not find method '{name}'.");
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
