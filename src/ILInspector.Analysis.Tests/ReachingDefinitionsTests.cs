using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

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
    public void Analyze_MalformedExceptionRegion_MarksResultIncomplete()
    {
        var result = ReachingDefinitions.Analyze([
            Op(ILOpCode.Ret),
        ], argumentSlotCount: 0, [default(ExceptionRegion)]);

        Assert.False(result.IsComplete);
        Assert.Contains("Exception try region", result.IncompleteReason);
    }

    [Fact]
    public void Analyze_CatchRegion_UseSeesDefinitionsFromTryRegion()
    {
        var result = AnalyzeFixture(nameof(TryCatchReadsLocal));

        Assert.True(result.IsComplete, result.IncompleteReason);
        AssertSomeUseSeesFirstAndLastSlotDefinitions(result);
    }

    [Fact]
    public void Analyze_FinallyRegion_UseSeesDefinitionsFromTryRegion()
    {
        var result = AnalyzeFixture(nameof(TryFinallyReadsLocal));

        Assert.True(result.IsComplete, result.IncompleteReason);
        AssertSomeUseSeesFirstAndLastSlotDefinitions(result);
    }

    [Fact]
    public void Analyze_FilterRegion_UseSeesDefinitionsFromTryRegion()
    {
        var result = AnalyzeFixture(nameof(TryFilterReadsLocal));

        Assert.True(result.IsComplete, result.IncompleteReason);
        AssertSomeUseSeesFirstAndLastSlotDefinitions(result);
    }

    [Fact]
    public void Analyze_NestedFinallyToCatch_UseSeesFinallyDefinition()
    {
        var result = AnalyzeFixture(nameof(NestedFinallyToCatchReadsLocal));

        Assert.True(result.IsComplete, result.IncompleteReason);
        AssertSomeUseSeesFirstAndLastSlotDefinitions(result);
    }

    static void AssertSomeUseSeesFirstAndLastSlotDefinitions(ReachingDefinitionsResult result)
    {
        var slotDefinitions = result.Definitions
            .Where(definition => !definition.IsArgument && definition.Slot == 0)
            .OrderBy(definition => definition.Offset)
            .ToArray();
        Assert.True(slotDefinitions.Length >= 2);
        var firstDefinition = slotDefinitions[0];
        var lastDefinition = slotDefinitions[^1];

        Assert.Contains(result.Uses, use =>
            !use.IsArgument
            && use.Slot == 0
            && use.ReachingDefinitions.Any(definition => definition.Id == firstDefinition.Id)
            && use.ReachingDefinitions.Any(definition => definition.Id == lastDefinition.Id));
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

    static ReachingDefinitionsResult AnalyzeFixture(string methodName)
    {
        using var stream = File.OpenRead(typeof(ReachingDefinitionsTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != nameof(ReachingDefinitionsTests))
                continue;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                return ReachingDefinitions.Analyze(peReader.GetMethodBody(method.RelativeVirtualAddress), argumentSlotCount: 0);
            }
        }
        throw new InvalidOperationException($"Fixture method {methodName} not found.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void MaybeThrow()
    {
    }

    static int s_sink;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Sink(int value) => s_sink = value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool Filter(int value)
    {
        Sink(value);
        return true;
    }

    static int TryCatchReadsLocal()
    {
        int value = 0;
        try
        {
            MaybeThrow();
            value = 1;
            MaybeThrow();
        }
        catch (InvalidOperationException)
        {
            return value;
        }
        return value;
    }

    static void TryFinallyReadsLocal()
    {
        int value = 0;
        try
        {
            MaybeThrow();
            value = 1;
            MaybeThrow();
        }
        finally
        {
            Sink(value);
        }
    }

    static int TryFilterReadsLocal()
    {
        int value = 0;
        try
        {
            MaybeThrow();
            value = 1;
            MaybeThrow();
        }
        catch (InvalidOperationException) when (Filter(value))
        {
            return value;
        }
        return value;
    }

    static int NestedFinallyToCatchReadsLocal()
    {
        int value = 0;
        try
        {
            try
            {
                value = 1;
                MaybeThrow();
            }
            finally
            {
                value = 2;
            }
        }
        catch
        {
            return value;
        }
        return value;
    }
}
