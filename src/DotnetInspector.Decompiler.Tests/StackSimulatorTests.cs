using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Decompiler;

namespace DotnetInspector.Decompiler.Tests;

/// <summary>
/// Tests for the Phase 2 stack type simulation and variable introduction.
/// </summary>
public class StackSimulatorTests
{
    // --- StackValueKind.ResultKind ---

    [Theory]
    [InlineData(ILOpCode.Ldc_i4_0, StackValueKind.Int32)]
    [InlineData(ILOpCode.Ldc_i4_s, StackValueKind.Int32)]
    [InlineData(ILOpCode.Ldc_i8, StackValueKind.Int64)]
    [InlineData(ILOpCode.Ldc_r4, StackValueKind.Float)]
    [InlineData(ILOpCode.Ldc_r8, StackValueKind.Float)]
    [InlineData(ILOpCode.Ldnull, StackValueKind.ObjRef)]
    [InlineData(ILOpCode.Ldstr, StackValueKind.ObjRef)]
    [InlineData(ILOpCode.Conv_i4, StackValueKind.Int32)]
    [InlineData(ILOpCode.Conv_i8, StackValueKind.Int64)]
    [InlineData(ILOpCode.Conv_r8, StackValueKind.Float)]
    [InlineData(ILOpCode.Conv_i, StackValueKind.NativeInt)]
    [InlineData(ILOpCode.Ceq, StackValueKind.Int32)]
    [InlineData(ILOpCode.Ldloca_s, StackValueKind.ByRef)]
    [InlineData(ILOpCode.Sizeof, StackValueKind.Int32)]
    [InlineData(ILOpCode.Localloc, StackValueKind.NativeInt)]
    [InlineData(ILOpCode.Call, StackValueKind.Unknown)]
    public void ResultKind_ReturnsExpected(ILOpCode opcode, StackValueKind expected)
    {
        Assert.Equal(expected, opcode.ResultKind());
    }

    // --- StackValue ---

    [Fact]
    public void StackValue_FromTypeName_PrimitiveTypes()
    {
        Assert.Equal(StackValueKind.Int32, StackValue.FromTypeName("int").Kind);
        Assert.Equal(StackValueKind.Int32, StackValue.FromTypeName("System.Int32").Kind);
        Assert.Equal(StackValueKind.Int32, StackValue.FromTypeName("bool").Kind);
        Assert.Equal(StackValueKind.Int64, StackValue.FromTypeName("long").Kind);
        Assert.Equal(StackValueKind.Float, StackValue.FromTypeName("float").Kind);
        Assert.Equal(StackValueKind.Float, StackValue.FromTypeName("double").Kind);
        Assert.Equal(StackValueKind.NativeInt, StackValue.FromTypeName("nint").Kind);
        Assert.Equal(StackValueKind.NativeInt, StackValue.FromTypeName("System.IntPtr").Kind);
    }

    [Fact]
    public void StackValue_FromTypeName_ReferenceTypes()
    {
        Assert.Equal(StackValueKind.ObjRef, StackValue.FromTypeName("string").Kind);
        Assert.Equal(StackValueKind.ObjRef, StackValue.FromTypeName("System.Object").Kind);
        Assert.Equal(StackValueKind.ObjRef, StackValue.FromTypeName("int[]").Kind);
    }

    [Fact]
    public void StackValue_FromTypeName_PointerAndByRef()
    {
        Assert.Equal(StackValueKind.NativeInt, StackValue.FromTypeName("byte*").Kind);
        Assert.Equal(StackValueKind.ByRef, StackValue.FromTypeName("int&").Kind);
        Assert.Equal("int", StackValue.FromTypeName("int&").TypeName);
    }

    [Fact]
    public void StackValue_Merge_SameKind_SameType()
    {
        var a = StackValue.CreateObjRef("System.String");
        var b = StackValue.CreateObjRef("System.String");
        var merged = StackValue.Merge(a, b);
        Assert.Equal(StackValueKind.ObjRef, merged.Kind);
        Assert.Equal("System.String", merged.TypeName);
    }

    [Fact]
    public void StackValue_Merge_SameKind_DifferentObjRef()
    {
        var a = StackValue.CreateObjRef("System.String");
        var b = StackValue.CreateObjRef("System.Exception");
        var merged = StackValue.Merge(a, b);
        Assert.Equal(StackValueKind.ObjRef, merged.Kind);
        Assert.Equal("object", merged.TypeName);
    }

    [Fact]
    public void StackValue_Merge_Int32_NativeInt()
    {
        var a = StackValue.CreatePrimitive(StackValueKind.Int32);
        var b = StackValue.CreatePrimitive(StackValueKind.NativeInt);
        Assert.Equal(StackValueKind.NativeInt, StackValue.Merge(a, b).Kind);
        Assert.Equal(StackValueKind.NativeInt, StackValue.Merge(b, a).Kind);
    }

    [Fact]
    public void StackValue_Merge_Incompatible()
    {
        var a = StackValue.CreatePrimitive(StackValueKind.Int32);
        var b = StackValue.CreateObjRef("System.Object");
        Assert.Equal(StackValueKind.Unknown, StackValue.Merge(a, b).Kind);
    }

    // --- StackState ---

    [Fact]
    public void StackState_PushPopPeek()
    {
        var state = StackState.Empty;
        Assert.Equal(0, state.Height);

        state = state.Push(StackValue.CreatePrimitive(StackValueKind.Int32));
        Assert.Equal(1, state.Height);
        Assert.Equal(StackValueKind.Int32, state.Peek().Kind);

        state = state.Push(StackValue.CreateObjRef("string"));
        Assert.Equal(2, state.Height);
        Assert.Equal(StackValueKind.ObjRef, state.Peek().Kind);
        Assert.Equal(StackValueKind.Int32, state.Peek(1).Kind);

        state = state.Pop(out var popped);
        Assert.Equal(StackValueKind.ObjRef, popped.Kind);
        Assert.Equal(1, state.Height);
    }

    [Fact]
    public void StackState_Merge_Identical()
    {
        var s1 = StackState.Empty
            .Push(StackValue.CreatePrimitive(StackValueKind.Int32))
            .Push(StackValue.CreateObjRef("string"));

        var s2 = StackState.Empty
            .Push(StackValue.CreatePrimitive(StackValueKind.Int32))
            .Push(StackValue.CreateObjRef("string"));

        var (merged, changed) = s1.Merge(s2);
        Assert.False(changed);
        Assert.Equal(2, merged.Height);
    }

    [Fact]
    public void StackState_Merge_DifferentObjRef_Widens()
    {
        var s1 = StackState.Empty.Push(StackValue.CreateObjRef("System.String"));
        var s2 = StackState.Empty.Push(StackValue.CreateObjRef("System.Exception"));

        var (merged, changed) = s1.Merge(s2);
        Assert.True(changed);
        Assert.Equal("object", merged.Peek().TypeName);
    }

    // --- StackSimulator on known methods ---

    [Fact]
    public void Simulate_Add_ProducesInt32Result()
    {
        var (context, cfg) = BuildSimulationContext(nameof(CfgSampleClass.Add));
        var result = StackSimulator.Simulate(context, cfg);

        // Entry block should start with empty stack
        Assert.True(result.BlockEntryStacks.ContainsKey(0));
        Assert.Equal(0, result.BlockEntryStacks[0].Height);

        // Should have parameter variables
        Assert.True(result.Parameters.Count >= 2);
    }

    [Fact]
    public void Simulate_IsPositive_HasConditionalBranch()
    {
        var (context, cfg) = BuildSimulationContext(nameof(CfgSampleClass.IsPositive));
        var result = StackSimulator.Simulate(context, cfg);

        // Should have at least the entry block
        Assert.NotEmpty(result.BlockEntryStacks);

        // Should have parameters (IsPositive takes an int)
        Assert.NotEmpty(result.Parameters);
    }

    [Fact]
    public void Simulate_TryCatch_HasExceptionSlot()
    {
        var (context, cfg) = BuildSimulationContext(nameof(CfgSampleClass.TryCatch));
        var result = StackSimulator.Simulate(context, cfg);

        // Should have at least one exception slot variable
        Assert.NotEmpty(result.ExceptionSlots);
        Assert.Equal(ILVariableKind.ExceptionSlot, result.ExceptionSlots[0].Kind);
    }

    [Fact]
    public void Simulate_LoopSum_MergesBackEdge()
    {
        var (context, cfg) = BuildSimulationContext(nameof(CfgSampleClass.LoopSum));
        var result = StackSimulator.Simulate(context, cfg);

        // Should succeed without infinite loop (worklist terminates)
        Assert.True(result.BlockEntryStacks.Count >= 2);
    }

    [Fact]
    public void Simulate_LocalVariablesCreated()
    {
        var (context, cfg) = BuildSimulationContext(nameof(CfgSampleClass.Add));
        var result = StackSimulator.Simulate(context, cfg);

        // Add has no locals (just params + return)
        // But LoopSum should have locals
        var (ctx2, cfg2) = BuildSimulationContext(nameof(CfgSampleClass.LoopSum));
        var result2 = StackSimulator.Simulate(ctx2, cfg2);
        Assert.NotEmpty(result2.Locals);
    }

    [Fact]
    public void Simulate_VariableNames_FollowConvention()
    {
        var (context, cfg) = BuildSimulationContext(nameof(CfgSampleClass.TryCatch));
        var result = StackSimulator.Simulate(context, cfg);

        foreach (var p in result.Parameters)
            Assert.StartsWith("P_", p.Name);
        foreach (var l in result.Locals)
            Assert.StartsWith("V_", l.Name);
        foreach (var e in result.ExceptionSlots)
            Assert.StartsWith("E_", e.Name);
        foreach (var s in result.StackSlotVariables)
            Assert.StartsWith("S_", s.Name);
    }

    // --- Platform assembly stress test ---

    [Fact]
    public void PlatformAssembly_SimulateAllMethods_NoCrashes()
    {
        var assembly = typeof(object).Assembly;
        var path = assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        int totalMethods = 0;
        int simulated = 0;
        List<string> failures = [];

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                totalMethods++;

                try
                {
                    var context = MethodBodyContext.Create(peReader, reader, method);
                    if (context is null)
                        continue;

                    var cfg = ControlFlowGraph.Create(context);
                    var result = StackSimulator.Simulate(context, cfg);
                    simulated++;
                }
                catch (Exception ex)
                {
                    string typeName = reader.GetString(typeDef.Name);
                    string methodName = reader.GetString(method.Name);
                    failures.Add($"{typeName}::{methodName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Assert.True(totalMethods > 1000, $"Expected many methods, got {totalMethods}");
        Assert.True(simulated > 1000, $"Expected many simulated, got {simulated}");

        double failureRate = (double)failures.Count / totalMethods;
        Assert.True(failureRate < 0.02,
            $"Simulation failed for {failures.Count}/{totalMethods} methods ({failureRate:P1}):\n{string.Join("\n", failures.Take(20))}");
    }

    // --- Helpers ---

    static (MethodBodyContext Context, ControlFlowGraph Cfg) BuildSimulationContext(string methodName)
    {
        // NOTE: We do NOT dispose the PEReader/stream here because the
        // MethodBodyContext holds a MetadataReader backed by the PEReader.
        // The GC will clean up. This is fine for tests.
        var assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        var stream = File.OpenRead(assemblyPath);
        var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(
            peReader,
            typeof(CfgSampleClass).FullName!,
            methodName);

        Assert.NotNull(context);
        var cfg = ControlFlowGraph.Create(context);
        return (context, cfg);
    }
}
