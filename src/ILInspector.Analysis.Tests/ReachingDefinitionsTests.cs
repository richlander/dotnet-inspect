using System.Reflection.Metadata;

using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

public class ReachingDefinitionsTests
{
    [Fact]
    public void Analyze_EntryArgumentUse_ReachesSyntheticArgumentDefinition()
    {
        var result = ReachingDefinitions.Analyze([
            Op(ILOpCode.Ldarg_0),
            Op(ILOpCode.Ret),
        ], argumentSlotCount: 1);

        var argUse = Assert.Single(result.Uses.Where(use => use.IsArgument && use.Slot == 0));
        var definition = Assert.Single(argUse.ReachingDefinitions);
        Assert.True(definition.IsArgument);
        Assert.Equal(0, definition.Slot);
        Assert.Equal(-1, definition.Offset);
    }

    [Fact]
    public void Analyze_BranchMerge_UseSeesDefinitionsFromBothPredecessors()
    {
        var result = ReachingDefinitions.Analyze([
            Op(ILOpCode.Ldarg_0),
            Op(ILOpCode.Brfalse_s), 0x04,
            Op(ILOpCode.Ldc_i4_1),
            Op(ILOpCode.Stloc_0),
            Op(ILOpCode.Br_s), 0x02,
            Op(ILOpCode.Ldc_i4_2),
            Op(ILOpCode.Stloc_0),
            Op(ILOpCode.Ldloc_0),
            Op(ILOpCode.Ret),
        ], argumentSlotCount: 1);

        var mergedUse = Assert.Single(result.Uses.Where(use => !use.IsArgument && use.Slot == 0));
        Assert.Equal([4, 8], mergedUse.ReachingDefinitions.Select(def => def.Offset).ToArray());
    }

    [Fact]
    public void Analyze_LoopBackEdge_UseSeesEntryAndLoopCarriedDefinitions()
    {
        var result = ReachingDefinitions.Analyze([
            Op(ILOpCode.Ldc_i4_0),
            Op(ILOpCode.Stloc_0),
            Op(ILOpCode.Ldc_i4_0),
            Op(ILOpCode.Stloc_1),
            Op(ILOpCode.Br_s), 0x08,
            Op(ILOpCode.Ldloc_0),
            Op(ILOpCode.Ldloc_1),
            Op(ILOpCode.Add),
            Op(ILOpCode.Stloc_0),
            Op(ILOpCode.Ldloc_1),
            Op(ILOpCode.Ldc_i4_1),
            Op(ILOpCode.Add),
            Op(ILOpCode.Stloc_1),
            Op(ILOpCode.Ldloc_1),
            Op(ILOpCode.Ldarg_0),
            Op(ILOpCode.Blt_s), unchecked((byte)-12),
            Op(ILOpCode.Ldloc_0),
            Op(ILOpCode.Ret),
        ], argumentSlotCount: 1);

        var loopUse = Assert.Single(result.Uses.Where(use => !use.IsArgument && use.Slot == 0 && use.Offset == 6));
        Assert.Equal([1, 9], loopUse.ReachingDefinitions.Select(def => def.Offset).ToArray());
    }

    [Fact]
    public void Analyze_SlotReuse_SeparatesUsesByReachingDefinition()
    {
        var result = ReachingDefinitions.Analyze([
            Op(ILOpCode.Ldc_i4_0),
            Op(ILOpCode.Stloc_0),
            Op(ILOpCode.Ldloc_0),
            Op(ILOpCode.Pop),
            Op(ILOpCode.Ldc_i4_1),
            Op(ILOpCode.Stloc_0),
            Op(ILOpCode.Ldloc_0),
            Op(ILOpCode.Ret),
        ], argumentSlotCount: 0);

        var firstUse = Assert.Single(result.Uses.Where(use => !use.IsArgument && use.Slot == 0 && use.Offset == 2));
        Assert.Equal([1], firstUse.ReachingDefinitions.Select(def => def.Offset).ToArray());
        var secondUse = Assert.Single(result.Uses.Where(use => !use.IsArgument && use.Slot == 0 && use.Offset == 6));
        Assert.Equal([5], secondUse.ReachingDefinitions.Select(def => def.Offset).ToArray());
    }

    [Fact]
    public void Analyze_MalformedHugeSwitch_ThrowsBeforeAllocatingTargetTable()
    {
        Assert.Throws<BadImageFormatException>(() => ReachingDefinitions.Analyze([
            Op(ILOpCode.Switch),
            0x01, 0x00, 0x00, 0x40,
            0x00, 0x00, 0x00, 0x00,
        ], argumentSlotCount: 0));
    }

    [Fact]
    public void Analyze_MalformedBranchIntoInstruction_Throws()
    {
        Assert.Throws<BadImageFormatException>(() => ReachingDefinitions.Analyze([
            Op(ILOpCode.Br_s), 0x01,
            Op(ILOpCode.Ldc_i4), 0x00, 0x00, 0x00, 0x00,
            Op(ILOpCode.Ret),
        ], argumentSlotCount: 0));
    }

    [Fact]
    public void Analyze_ExceptionRegions_MarksResultIncomplete()
    {
        var result = ReachingDefinitions.Analyze([
            Op(ILOpCode.Ret),
        ], argumentSlotCount: 0, [default(ExceptionRegion)]);

        Assert.False(result.IsComplete);
        Assert.Contains("Exception-handler", result.IncompleteReason);
    }

    [Fact]
    public void Analyze_LeaveRegion_MarksResultIncomplete()
    {
        var result = ReachingDefinitions.Analyze([
            Op(ILOpCode.Leave_s), 0x00,
            Op(ILOpCode.Ret),
        ], argumentSlotCount: 0);

        Assert.False(result.IsComplete);
        Assert.Contains("Region-leaving", result.IncompleteReason);
    }

    static byte Op(ILOpCode opcode) => checked((byte)opcode);
}
