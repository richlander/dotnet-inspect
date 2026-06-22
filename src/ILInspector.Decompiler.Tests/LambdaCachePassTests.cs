using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LambdaCachePassTests
{
    static string PrintRaised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    // The cache field name and the null guard are the two visible artifacts of an
    // uncollapsed dance; a clean collapse leaves neither.
    static void AssertCacheCollapsed(string output)
    {
        Assert.DoesNotContain("<>9__", output);
        Assert.DoesNotContain(">__", output);
        Assert.DoesNotContain("is null", output);
    }

    [Fact]
    public void CachedDelegateArgument_CollapsesDespiteInterleavedReceiver()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateArgument));

        AssertCacheCollapsed(output);
        Assert.Contains("x => x > 0", output);
        Assert.Contains("Where", output);
    }

    [Fact]
    public void CachedDelegateChain_CollapsesBothGuards()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateChain));

        AssertCacheCollapsed(output);
        Assert.Contains("x => x > 0", output);
        Assert.Contains("x => x * 2", output);
        Assert.Contains("Where", output);
        Assert.Contains("Select", output);
    }

    [Fact]
    public void StaticMethodGroupCache_Collapses()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedStaticMethodGroup));

        AssertCacheCollapsed(output);
        Assert.Contains("CacheMethodGroupIdentity", output);
        Assert.Contains("Select", output);
    }

    [Fact]
    public void FlatStaticMethodGroupCache_CollapsesBeforeStructuring()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var objectType = TypeRef.CoreLib("System", "Object");
        var funcType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`2"), [stringType, stringType]);
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var cacheHolder = TypeRef.Definition("Synthetic", "Samples", "Owner+<>O");
        var cacheField = new FieldRef(cacheHolder, "<0>__Identity", funcType);
        var identity = new MethodRef(owner, "Identity", stringType, [stringType], HasThis: false);

        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new StoreStackSlot(0, new LoadField(cacheField, null)));
        head.Add(new StoreStackSlot(1, new LoadArgument(0, "items", TypeRef.CoreLib("System", "Object"))));
        head.Add(new StoreStackSlot(2, new LoadStackSlot(0, funcType)));
        head.Add(new ConditionalBranch(new LoadStackSlot(0, funcType), 8));
        var create = new Block(4);
        create.Add(new StoreStackSlot(3, new DelegateCreation(funcType, identity, isVirtual: false, new Constant(null, objectType))));
        create.Add(new StoreField(cacheField, null, new LoadStackSlot(3, funcType)));
        create.Add(new StoreStackSlot(2, new LoadStackSlot(3, funcType)));
        var join = new Block(8);
        join.Add(new Return(new LoadStackSlot(2, funcType)));
        foreach (var block in (Block[])[head, create, join])
            container.Add(block);
        var function = new IrFunction("M", owner, new MethodSignature(funcType, [new Parameter("items", TypeRef.CoreLib("System", "Object"))], HasThis: false, GenericParameterCount: 0), [], container);

        new LambdaCachePass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());
        Assert.Empty(function.Descendants.OfType<LoadField>());
        Assert.Empty(function.Descendants.OfType<StoreField>());
        Assert.Single(function.Descendants.OfType<DelegateCreation>());
        Assert.Equal(2, function.Body.Blocks.Count);
    }
}
