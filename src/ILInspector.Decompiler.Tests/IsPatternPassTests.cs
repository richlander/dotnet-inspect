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
    public void Conjunction_FoldsRelationalToPropertyPattern()
    {
        // `o is string s && s.Length > 0` — the lone relational comparison on the
        // pattern local folds to a relational property pattern.
        var function = Raised(nameof(CfgSampleClass.IsPatternConjunction));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is string { Length: > 0 }", output);
        Assert.DoesNotContain("&&", output);
    }

    [Fact]
    public void ConjunctionVariableBound_StaysFlatChain()
    {
        // `o is string s && s.Length > n` — the comparison is against a parameter,
        // not a constant, so it has no property sub-pattern form and stays flat.
        var function = Raised(nameof(CfgSampleClass.IsPatternConjunctionVariableBound));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is string s && s.Length > n", output);
    }

    [Fact]
    public void PropertyPattern_FoldsRelationalSubpattern()
    {
        // `o is string { Length: > 5 }` lowers to `s != null && s.Length > 5`; the
        // printer folds the relational comparison into the property sub-pattern.
        var function = Raised(nameof(CfgSampleClass.IsPatternPropertyGreater));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is string { Length: > 5 }", output);
        Assert.DoesNotContain("&&", output);
        Assert.DoesNotContain(".Length > 5", output);
    }

    [Fact]
    public void PropertyPattern_FoldsRelationalLessThanOrEqual()
    {
        var function = Raised(nameof(CfgSampleClass.IsPatternPropertyAtMost));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is string { Length: <= 3 }", output);
        Assert.DoesNotContain("&&", output);
    }

    [Fact]
    public void FloatPropertyRelational_IsNotFoldedToRelationalPattern()
    {
        // Adversarial near-miss: `o is FloatHolder { Magnitude: > 1.5 }` over a
        // floating-point property must NOT round-trip into a relational property
        // sub-pattern. The ordered (`cgt`) and unordered (`cgt.un`) float compares
        // disagree on NaN, and a relational pattern fixes one answer — so the type
        // pattern is raised but the comparison stays an explicit `&&`. This pins
        // the IsFloatComparison discriminator behind TryPropertySubpattern.
        var function = Raised(nameof(CfgSampleClass.IsPatternFloatPropertyRelational));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("is FloatHolder", output);
        Assert.Contains("&&", output);
        Assert.Contains("> 1.5", output);
        Assert.DoesNotContain("{ Magnitude:", output);
    }

    [Fact]
    public void PropertyPattern_RendersPropertyPatternClause()
    {
        // `o is string { Length: 5 }` lowers to the same as-store plus
        // `s != null && s.Length == 5`; the printer folds the internal type
        // pattern + equality back to the property-pattern altitude.
        var function = Raised(nameof(CfgSampleClass.IsPatternProperty));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is string { Length: 5 }", output);
        Assert.DoesNotContain("&&", output);
        Assert.DoesNotContain(".Length == 5", output);
    }

    [Fact]
    public void MultiPropertyPattern_RendersSinglePropertyPatternClause()
    {
        var function = Raised(nameof(CfgSampleClass.IsPatternMultiProperty));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is PatternPoint { X: 1, Y: 2 }", output);
        Assert.DoesNotContain("&&", output);
        Assert.DoesNotContain(".X == 1", output);
        Assert.DoesNotContain(".Y == 2", output);
    }

    [Fact]
    public void MultiPropertyPattern_AllowsMixedRelationalAndEqualitySubpatterns()
    {
        var function = Raised(nameof(CfgSampleClass.IsPatternMultiPropertyMixed));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is PatternPoint { X: > 0, Y: 2 }", output);
        Assert.DoesNotContain("&&", output);
    }

    [Fact]
    public void RecursivePropertyDeclarationPattern_RaisesCapturedPropertyBinding()
    {
        var function = Raised(nameof(CfgSampleClass.RecursivePropertyPatternBinding));

        var pattern = Assert.Single(function.Descendants.OfType<RecursivePropertyDeclarationPattern>());
        Assert.Equal("PublicProperty", pattern.PropertyName);
        Assert.Equal("string", pattern.PatternType.ToDisplayString());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("if (value is { PublicProperty: string str })", output);
        Assert.Contains("return str.Length;", output);
        Assert.DoesNotContain("string str = default;", output);
        Assert.DoesNotContain("value.PublicProperty as string", output);
        Assert.DoesNotContain("str is not null", output);
    }

    [Fact]
    public void RecursivePropertyDeclarationPattern_WhenBoundLocalEscapes_StaysLowered()
    {
        var function = FunctionWithEscapingRecursivePropertyBinding();

        new IsPatternPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<RecursivePropertyDeclarationPattern>());
        function.CheckInvariant();
    }

    [Fact]
    public void ManualAsAndPropertyChecks_WithLocalUse_StaysFlatChain()
    {
        var function = Raised(nameof(CfgSampleClass.IsPatternManualAsAndPropertiesWithUse));

        Assert.Empty(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("point is not null && point.X == 1 && point.Y == 2", output);
        Assert.DoesNotContain("{ X: 1, Y: 2 }", output);
    }

    [Fact]
    public void DuplicatePropertyPattern_StaysFlatChain()
    {
        var function = Raised(nameof(CfgSampleClass.IsPatternDuplicateProperty));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is PatternPoint", output);
        Assert.DoesNotContain("{ X: > 0, X: < 10 }", output);
    }

    [Fact]
    public void PropertyPattern_WhenPatternLocalIsUsedInBody_StaysFlat()
    {
        var function = Raised(nameof(CfgSampleClass.IsPatternPropertyWithBindingUse));

        Assert.Empty(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.DoesNotContain("{ Length: 5 }", output);
        Assert.Contains(".Length", output);
    }

    [Fact]
    public void UnsignedPropertyComparisonLookalike_DoesNotFoldToRelationalPropertyPattern()
    {
        // `(uint)p.X > 0u` is an unsigned IL comparison. The C# relational
        // subpattern `{ X: > 0 }` would use signed `int` semantics and disagree
        // for negative X values, so the property-pattern printer must keep the
        // flat conjunction.
        var function = FunctionWithUnsignedPropertyComparison();

        var output = CSharpPrinter.Print(function).Output;

        Assert.NotNull(output);
        Assert.Contains("&&", output);
        Assert.DoesNotContain("{ X: > 0 }", output);
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

    static IrFunction FunctionWithUnsignedPropertyComparison()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var point = TypeRef.Definition("Synthetic", "Samples", "PatternPoint");
        var objectType = TypeRef.CoreLib("System", "Object");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var getX = new MethodRef(point, "get_X", intType, [], HasThis: true)
        {
            IsSpecialName = true,
        };

        var block = new Block();
        block.Add(new Return(new LogicalBinary(
            LogicalKind.And,
            new IsPattern(new LoadArgument(0, "o", objectType), point, localIndex: 0),
            new Comparison(
                ComparisonKind.GreaterThan,
                isUnsigned: true,
                new LoadProperty(getX, new LoadLocal(0, point), []),
                new Constant(0, intType)))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "UnsignedPropertyComparison",
            owner,
            new MethodSignature(boolType, [new Parameter("o", objectType)], HasThis: false, GenericParameterCount: 0),
            [point],
            body)
        {
            LocalNames = ["p"],
        };
    }

    static IrFunction FunctionWithEscapingRecursivePropertyBinding()
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var stringType = TypeRef.CoreLib("System", "String");
        var objectType = TypeRef.CoreLib("System", "Object");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var getPublicProperty = new MethodRef(owner, "get_PublicProperty", objectType, [], HasThis: true)
        {
            IsSpecialName = true,
        };
        var getLength = new MethodRef(stringType, "get_Length", intType, [], HasThis: true)
        {
            IsSpecialName = true,
        };

        var matched = new Block();
        matched.Add(new StoreLocal(
            0,
            stringType,
            new IsInstance(
                stringType,
                new LoadProperty(getPublicProperty, new LoadArgument(0, "value", owner), []))));
        matched.Add(new StoreStackSlot(
            0,
            new Comparison(
                ComparisonKind.GreaterThan,
                isUnsigned: true,
                new LoadLocal(0, stringType),
                new Constant(null, stringType))));

        var unmatched = new Block();
        unmatched.Add(new StoreStackSlot(0, new Constant(false, boolType)));

        var trueBranch = new Block();
        trueBranch.Add(new Return(new LoadProperty(getLength, new LoadLocal(0, stringType), [])));

        var block = new Block();
        block.Add(new IfStatement(new LoadArgument(0, "value", owner), matched, unmatched));
        block.Add(new IfStatement(new LoadStackSlot(0, boolType), trueBranch, elseArm: null));
        block.Add(new Return(new LoadProperty(getLength, new LoadLocal(0, stringType), [])));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "EscapingRecursivePropertyBinding",
            owner,
            new MethodSignature(intType, [new Parameter("value", owner)], HasThis: false, GenericParameterCount: 0),
            [stringType],
            body)
        {
            LocalNames = ["str"],
        };
    }
}
