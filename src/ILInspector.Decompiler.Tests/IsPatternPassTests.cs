using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IsPatternPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void StatementGuard_RaisesAsNullTestToIsPattern()
    {
        // `if (o is string s)` lowers to `string s = o as string; if (s != null)`.
        // The pass folds the as-store and null test into one `is` pattern.
        var function = Raised(nameof(CfgSampleClass.IsPatternGuard));

        var pattern = Assert.Single(function.Descendants.OfType<IsPattern>());
        Assert.Equal("string", pattern.Type.ToDisplayString());
        Assert.IsType<LoadArgument>(pattern.Value);
        Assert.Empty(function.Descendants.OfType<IsInstance>());
    }

    [Fact]
    public void StatementGuard_RendersIsPatternHeader()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.IsPatternGuard))).Output;

        Assert.NotNull(output);
        Assert.Contains("if (o is string s)", output);
        Assert.DoesNotContain("as string", output);
        Assert.DoesNotContain("is not null", output);
    }

    [Fact]
    public void Conjunction_RaisesLeftOperandToIsPattern()
    {
        // `o is string s && s.Length > 0` — the pattern binds in the left
        // conjunct and is read in the right.
        var function = Raised(nameof(CfgSampleClass.IsPatternConjunction));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is string s && s.Length > 0", output);
    }

    [Fact]
    public void PropertyPattern_RecoversAsTypePatternConjunction()
    {
        // `o is string { Length: 5 }` lowers to the same as-store plus
        // `s != null && s.Length == 5`; recovered as `o is string s && ...`.
        var function = Raised(nameof(CfgSampleClass.IsPatternProperty));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("is string", output);
        Assert.Contains("&& ", output);
        Assert.Contains("== 5", output);
    }

    [Fact]
    public void AsLocalReadOnFallThroughPath_StaysFlat()
    {
        // The `as` local is read on both the matched and fall-through paths, so
        // binding it inside the pattern would leave it not definitely assigned
        // on the false path. The pass must leave the flat `as` + null test.
        var function = Raised(nameof(CfgSampleClass.AsWithoutPattern));

        Assert.Empty(function.Descendants.OfType<IsPattern>());
        Assert.Single(function.Descendants.OfType<IsInstance>());
    }

    [Fact]
    public void SideEffectingTestValue_StaysFlat()
    {
        var function = FunctionWithTestValue(new Call(
            new MethodRef(
                TypeRef.Definition("Synthetic", "Samples", "Factory"),
                "Make",
                TypeRef.CoreLib("System", "Object"),
                [],
                HasThis: false),
            isVirtual: false,
            []));

        new IsPatternPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<IsPattern>());
        Assert.Single(function.Descendants.OfType<IsInstance>());
        function.CheckInvariant();
    }

    [Fact]
    public void PropertyGetterTestValue_StaysFlat()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var getValue = new MethodRef(owner, "get_Value", TypeRef.CoreLib("System", "Object"), [], HasThis: true)
        {
            IsSpecialName = true,
        };
        var function = FunctionWithTestValue(new LoadProperty(getValue, new LoadArgument(0, "owner", owner), []));

        new IsPatternPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<IsPattern>());
        Assert.Single(function.Descendants.OfType<IsInstance>());
        function.CheckInvariant();
    }

    static IrFunction FunctionWithTestValue(IrExpression value)
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var intType = TypeRef.CoreLib("System", "Int32");
        var block = new Block();
        block.Add(new StoreLocal(0, stringType, new IsInstance(stringType, value)));
        var then = new Block();
        then.Add(new Return(new Constant(1, intType)));
        block.Add(new IfStatement(new LoadLocal(0, stringType), then, elseArm: null));
        block.Add(new Return(new Constant(0, intType)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(intType, [new Parameter("owner", TypeRef.Definition("Synthetic", "Samples", "Owner"))], HasThis: false, GenericParameterCount: 0),
            [stringType],
            body);
    }
}
