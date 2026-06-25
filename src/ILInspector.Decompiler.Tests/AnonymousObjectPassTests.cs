using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class AnonymousObjectPassTests
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
    public void Shorthand_RaisesAnonymousTypeConstructor()
    {
        // `new { a, b }` lowers to `new <>f__AnonymousType0<int, string>(a, b)`.
        var function = Raised(nameof(CfgSampleClass.AnonShorthand));

        var anonymous = Assert.Single(function.Descendants.OfType<AnonymousObject>());
        Assert.Equal(["a", "b"], anonymous.PropertyNames);
        Assert.Equal(2, anonymous.Values.Count);
        Assert.Empty(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void Shorthand_RendersProjectionShorthand()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.AnonShorthand))).Output;

        Assert.NotNull(output);
        Assert.Contains("new { a, b }", output);
        Assert.DoesNotContain("f__AnonymousType", output);
    }

    [Fact]
    public void NamedMembers_RenderExplicitAssignments()
    {
        // The values are named x/y but the properties are Id/Name, so the
        // shorthand cannot apply — the explicit `Name = value` form is required.
        var function = Raised(nameof(CfgSampleClass.AnonNamed));

        var anonymous = Assert.Single(function.Descendants.OfType<AnonymousObject>());
        Assert.Equal(["Id", "Name"], anonymous.PropertyNames);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("new { Id = x, Name = y }", output);
    }

    [Fact]
    public void SingleMember_Renders()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.AnonSingle))).Output;
        Assert.Contains("new { a }", output);
    }

    [Fact]
    public void MemberAccess_UsesShorthandWhenNamesMatch()
    {
        // `new { t.X, t.Y }` projects properties X/Y from the member names.
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.AnonMemberShorthand))).Output;
        Assert.Contains("new { t.X, t.Y }", output);
    }

    [Fact]
    public void NestedAnonymousMember_RaisesRecursively()
    {
        // An anonymous-object member that is itself an anonymous object lowers to a
        // nested generated constructor. The pass walks every NewObject, so the inner
        // and outer both fold back.
        var function = Raised(nameof(CfgSampleClass.AnonNested));

        Assert.Equal(2, function.Descendants.OfType<AnonymousObject>().Count());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("new { a, Inner = new { b } }", output);
    }

    [Fact]
    public void DeeplyNestedAnonymousMembers_RaiseAtEveryLevel()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.AnonDeepNested))).Output;

        Assert.Contains("new { Outer = new { a, Mid = new { b, c } } }", output);
        Assert.DoesNotContain("AnonymousType", output);
    }

    [Fact]
    public void PropertyNameCountMismatch_IsNotRaised()
    {
        var function = FunctionWithAnonymousMetadata(["a"], [new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32")), new LoadArgument(1, "b", TypeRef.CoreLib("System", "String"))]);

        new AnonymousObjectPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<AnonymousObject>());
        Assert.Single(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void EmptyPropertyNames_AreNotRaised()
    {
        var function = FunctionWithAnonymousMetadata([], [new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32"))]);

        new AnonymousObjectPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<AnonymousObject>());
        Assert.Single(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void DuplicatePropertyNames_AreNotRaisedToInvalidAnonymousObject()
    {
        var function = FunctionWithAnonymousMetadata(["a", "a"], [new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32")), new LoadArgument(1, "b", TypeRef.CoreLib("System", "String"))]);

        new AnonymousObjectPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<AnonymousObject>());
        Assert.Single(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void UnspellablePropertyName_IsNotRaisedToInvalidAnonymousObject()
    {
        var function = FunctionWithAnonymousMetadata(["bad-name"], [new Constant(1, TypeRef.CoreLib("System", "Int32"))]);

        new AnonymousObjectPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<AnonymousObject>());
        Assert.Single(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void KeywordPropertyName_IsNotRaisedUntilPrinterEscapesAnonymousMembers()
    {
        var function = FunctionWithAnonymousMetadata(["int"], [new Constant(1, TypeRef.CoreLib("System", "Int32"))]);

        new AnonymousObjectPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<AnonymousObject>());
        Assert.Single(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void GeneratedLookingTypeWithoutCapturedPropertyNames_IsNotRaised()
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "<>f__AnonymousType0`1");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", TypeRef.CoreLib("System", "Void"), [intType], HasThis: true);
        var function = FunctionReturning(new NewObject(ctor, [new LoadArgument(0, "a", intType)]));

        new AnonymousObjectPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<AnonymousObject>());
        Assert.Single(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    static IrFunction FunctionWithAnonymousMetadata(string[] propertyNames, IrExpression[] arguments)
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "<>f__AnonymousType0`2");
        var ctor = new MethodRef(
            type,
            ".ctor",
            TypeRef.CoreLib("System", "Void"),
            [.. arguments.Select(argument => argument.ResultType ?? TypeRef.CoreLib("System", "Object"))],
            HasThis: true);
        return FunctionReturning(new NewObject(ctor, arguments)
        {
            AnonymousPropertyNames = [.. propertyNames],
        });
    }

    static IrFunction FunctionReturning(IrExpression expression)
    {
        var returnType = expression.ResultType ?? TypeRef.CoreLib("System", "Object");
        var block = new Block();
        block.Add(new Return(expression));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(returnType, [new Parameter("a", TypeRef.CoreLib("System", "Int32")), new Parameter("b", TypeRef.CoreLib("System", "String"))], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
