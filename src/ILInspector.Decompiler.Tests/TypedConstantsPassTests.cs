using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class TypedConstantsPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    [Fact]
    public void BoolStoredThroughByRef_RetypesConstantToBool()
    {
        // `out bool flag = true` lowers to `stind.i1` over a `ref bool`, whose opcode
        // width is sbyte. Retyping by the address's pointee type (bool), not the
        // opcode type, recovers the boolean literal — otherwise the output is
        // `flag = 1;`, which is not valid C# (CS0029).
        var function = Raised(nameof(CfgSampleClass.SetFlag));
        var store = Assert.Single(function.Descendants.OfType<StoreIndirect>());

        var constant = Assert.IsType<Constant>(store.Value);
        Assert.IsType<bool>(constant.Value);
        Assert.Equal(true, constant.Value);
    }

    [Fact]
    public void BoolStoredThroughByRef_RendersBooleanLiteral()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.SetFlag))).Output;

        Assert.NotNull(output);
        Assert.Contains("flag = true;", output);
        Assert.DoesNotContain("flag = 1;", output);
    }

    [Fact]
    public void BoolArrayElementTraffic_RendersAsBool()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.BoolArrayVisited))).Output;

        Assert.NotNull(output);
        Assert.Contains("[index] = true;", output);
        Assert.Contains("return S_256[index] ? 1 : 0;", output);
        Assert.DoesNotContain("visited[index] = 1;", output);
        Assert.DoesNotContain("visited[index] == 0", output);
    }

    [Fact]
    public void ConstantOnlyBoolSpill_RendersAsBool()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Constant(1, intType)));
        block.Add(new StoreLocal(0, boolType, new LoadStackSlot(0, intType)));
        block.Add(new Return(new LoadLocal(0, boolType)));
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [boolType], container);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var slotStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Equal(true, Assert.IsType<Constant>(slotStore.Value).Value);
        var localStore = Assert.Single(function.Descendants.OfType<StoreLocal>());
        var slotLoad = Assert.IsType<LoadStackSlot>(localStore.Value);
        Assert.NotNull(slotLoad.Type);
        Assert.Equal("Boolean", slotLoad.Type.Name);
    }
}
