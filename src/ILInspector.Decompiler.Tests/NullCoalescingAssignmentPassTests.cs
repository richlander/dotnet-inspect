using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class NullCoalescingAssignmentPassTests
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
    public void LocalNullAssignmentDiamond_RaisesToNullCoalescingAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignLocal));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingAssignment>());
        Assert.Equal("string", assignment.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(assignment.Value);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignLocal))).Output;

        Assert.NotNull(output);
        Assert.Contains("value ??= fallback;", output);
        Assert.Contains("return value;", output);
    }

    [Fact]
    public void LocalNullAssignmentWithExtraThenStatement_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignLocalWithExtraThenStatement));

        Assert.Empty(function.Descendants.OfType<NullCoalescingAssignment>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.DoesNotContain("??=", output);
        Assert.Contains("if (value is null)", output);
        Assert.Contains("LastValue = value.Length;", output);
    }

    [Fact]
    public void NullCoalescingOperator_RemainsExpression()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalesce));

        Assert.Single(function.Descendants.OfType<Coalesce>());
        Assert.DoesNotContain(function.Descendants.OfType<NullCoalescingAssignment>(), _ => true);
    }

    [Fact]
    public void StaticFieldNullAssignmentDiamond_RaisesToNullCoalescingFieldAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignStaticField));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingFieldAssignment>());
        Assert.Equal("CachedName", assignment.Field.Name);
        Assert.False(assignment.HasInstance);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersStaticFieldNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignStaticField))).Output;

        Assert.NotNull(output);
        Assert.Contains("CachedName ??= fallback;", output);
    }

    [Fact]
    public void InstanceFieldNullAssignmentDiamond_RaisesToNullCoalescingFieldAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignInstanceField));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingFieldAssignment>());
        Assert.Equal("Cache", assignment.Field.Name);
        Assert.True(assignment.HasInstance);
        Assert.IsType<LoadArgument>(assignment.Instance);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersInstanceFieldNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignInstanceField))).Output;

        Assert.NotNull(output);
        Assert.Contains("holder.Cache ??= fallback;", output);
    }

    [Fact]
    public void FieldNullAssignmentWithExtraThenStatement_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignFieldWithExtraThenStatement));

        Assert.Empty(function.Descendants.OfType<NullCoalescingFieldAssignment>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.DoesNotContain("??=", output);
    }

    [Fact]
    public void InstancePropertyNullAssignmentDiamond_RaisesToNullCoalescingPropertyAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignInstanceProperty));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        Assert.Equal("CacheProp", assignment.PropertyName);
        Assert.True(assignment.HasInstance);
        Assert.IsType<LoadArgument>(assignment.Instance);
        Assert.IsType<LoadArgument>(assignment.Value);
        // The setter value-spill (stack slot + dead result local) is gone.
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
        Assert.DoesNotContain(function.Descendants.OfType<StoreProperty>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersInstancePropertyNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignInstanceProperty))).Output;

        Assert.NotNull(output);
        Assert.Contains("holder.CacheProp ??= fallback;", output);
    }

    [Fact]
    public void StaticPropertyNullAssignmentDiamond_RaisesToNullCoalescingPropertyAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignStaticProperty));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        Assert.Equal("StaticNameProp", assignment.PropertyName);
        Assert.False(assignment.HasInstance);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersStaticPropertyNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignStaticProperty))).Output;

        Assert.NotNull(output);
        Assert.Contains("StaticNameProp ??= fallback;", output);
    }

    [Fact]
    public void ChainedPropertyNullAssignment_RaisesThroughSpilledReceiverLocal()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignChainedProperty));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        Assert.Equal("CacheProp", assignment.PropertyName);
        Assert.True(assignment.HasInstance);
        // csc spills the holder.Next receiver to a local that both accessors read,
        // so PlaceIdentity.SameVariable proves the place re-evaluable; the later
        // inlining pass folds that single-use local back to the field chain.
        Assert.IsType<LoadField>(assignment.Instance);

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("holder.Next.CacheProp ??= fallback;", output);
    }

    [Fact]
    public void PropertyNullAssignmentWithExtraThenStatement_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignPropertyWithExtraThenStatement));

        Assert.Empty(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.DoesNotContain("??=", output);
    }

    [Fact]
    public void PropertyNullAssignmentWithIncompatibleAccessors_IsNotRaised()
    {
        var function = FunctionWithPropertyNullAssignment(
            getterType: TypeRef.CoreLib("System", "Object"),
            setterType: TypeRef.CoreLib("System", "String"));

        new NullCoalescingAssignmentPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        Assert.Single(function.Descendants.OfType<IfStatement>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();
    }

    // #1462: an indexer whose get/set accessors are not one spellable indexer — the
    // getter return type differs from the setter value type — must not raise to a
    // `cache[k] ??= v` with no consistent C# spelling. (Covered by the
    // CompatiblePropertyAccessors gate added in #1445; pinned here for the indexer
    // shape from #1462's done-signal.)
    [Fact]
    public void IndexerNullAssignmentWithMismatchedValueType_IsNotRaised()
    {
        var function = FunctionWithIndexerNullAssignment(
            getterReturn: TypeRef.CoreLib("System", "Object"),
            setterValue: TypeRef.CoreLib("System", "String"),
            getterIndex: TypeRef.CoreLib("System", "Int32"),
            setterIndex: TypeRef.CoreLib("System", "Int32"));

        new NullCoalescingAssignmentPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        Assert.Single(function.Descendants.OfType<IfStatement>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();
    }

    // #1462: a by-ref index on one accessor but not the other is not one spellable
    // indexer; the index parameter types (including by-ref kind) must match.
    [Fact]
    public void IndexerNullAssignmentWithMismatchedIndexType_IsNotRaised()
    {
        var function = FunctionWithIndexerNullAssignment(
            getterReturn: TypeRef.CoreLib("System", "Object"),
            setterValue: TypeRef.CoreLib("System", "Object"),
            getterIndex: TypeRef.ByRef(TypeRef.CoreLib("System", "Int32")),
            setterIndex: TypeRef.CoreLib("System", "Int32"));

        new NullCoalescingAssignmentPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        Assert.Single(function.Descendants.OfType<IfStatement>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();
    }

    static IrFunction FunctionWithIndexerNullAssignment(
        TypeRef getterReturn, TypeRef setterValue, TypeRef getterIndex, TypeRef setterIndex)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "IndexerOwner");
        var intType = TypeRef.CoreLib("System", "Int32");
        var getter = new MethodRef(owner, "get_Item", getterReturn, [getterIndex], HasThis: true)
        {
            IsSpecialName = true,
        };
        var setter = new MethodRef(owner, "set_Item", TypeRef.CoreLib("System", "Void"), [setterIndex, setterValue], HasThis: true)
        {
            IsSpecialName = true,
        };

        var condition = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadProperty(getter, new LoadArgument(0, "cache", owner), [new Constant(0, intType)]),
            new Constant(null, getterReturn));
        var then = new Block();
        then.Add(new StoreProperty(
            setter,
            new LoadArgument(0, "cache", owner),
            [new Constant(0, intType)],
            new LoadArgument(1, "fallback", setterValue)));

        var block = new Block();
        block.Add(new IfStatement(condition, then, elseArm: null));
        block.Add(new Return(new Constant(null, TypeRef.CoreLib("System", "Void"))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("cache", owner), new Parameter("fallback", setterValue)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
    }

    [Fact]
    public void IndexerNullAssignmentDiamond_RaisesToNullCoalescingPropertyAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignIndexer));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        Assert.Equal("Item", assignment.PropertyName);
        Assert.True(assignment.HasInstance);
        // Receiver and index are compiler-spilled locals folded back to the
        // parameters by the inlining pass: the one re-evaluable cache[key] place.
        Assert.Single(assignment.IndexArguments);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
        Assert.DoesNotContain(function.Descendants.OfType<StoreProperty>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersIndexerNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignIndexer))).Output;

        Assert.NotNull(output);
        Assert.Contains("cache[key] ??= fallback;", output);
    }

    [Fact]
    public void ConstantIndexNullAssignment_RaisesThroughEqualConstantOperand()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignIndexerConstKey));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        Assert.IsType<Constant>(Assert.Single(assignment.IndexArguments));
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("cache[0] ??= fallback;", output);
    }

    [Fact]
    public void DictionaryIndexerNullAssignment_RaisesToNullCoalescingPropertyAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignDictionaryIndexer))).Output;

        Assert.NotNull(output);
        Assert.Contains("map[key] ??= fallback;", output);
    }

    [Fact]
    public void IndexerNullAssignmentWithDifferentConstantIndex_IsNotRaised()
    {
        var function = FunctionWithIndexerNullAssignment(getIndex: 1, setIndex: 2);

        new NullCoalescingAssignmentPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullCoalescingPropertyAssignment>());
        Assert.Single(function.Descendants.OfType<IfStatement>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();
    }

    [Fact]
    public void IndexerCompoundAssignment_RendersAsCompoundOnReevaluablePlace()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.CompoundAssignIndexer))).Output;

        Assert.NotNull(output);
        // The receiver/index stay as csc's spill locals (folding them would drop the
        // spill and change the IL); the get/set fold to one += on that place.
        Assert.Contains("[V_1] += delta;", output);
        Assert.DoesNotContain("= V_0[V_1] +", output);
    }

    [Fact]
    public void DictionaryIndexerCompoundAssignment_RendersAsCompound()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.CompoundAssignDictionaryIndexer))).Output;

        Assert.NotNull(output);
        Assert.Contains("[V_1] += delta;", output);
    }

    [Fact]
    public void IndexerCompoundAssignmentWithDifferentConstantIndex_DoesNotFold()
    {
        var function = FunctionWithIndexerCompoundAssignment(loadIndex: 1, storeIndex: 2);
        var output = CSharpPrinter.Print(function).Output;

        Assert.NotNull(output);
        Assert.DoesNotContain("+=", output);
        Assert.Contains("cache[2] = cache[1] + 3;", output);
    }

    static IrFunction FunctionWithIndexerNullAssignment(int getIndex, int setIndex)
    {
        var (owner, stringType, intType, getter, setter) = IndexerRefs(stringType: true);

        var condition = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadProperty(getter, new LoadArgument(0, "cache", owner), [new Constant(getIndex, intType)]),
            new Constant(null, stringType));
        var then = new Block();
        then.Add(new StoreProperty(
            setter,
            new LoadArgument(0, "cache", owner),
            [new Constant(setIndex, intType)],
            new LoadArgument(1, "fallback", stringType)));

        var block = new Block();
        block.Add(new IfStatement(condition, then, elseArm: null));
        block.Add(new Return(new Constant(null, stringType)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(stringType, [new Parameter("cache", owner), new Parameter("fallback", stringType)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction FunctionWithIndexerCompoundAssignment(int loadIndex, int storeIndex)
    {
        var (owner, _, intType, getter, setter) = IndexerRefs(stringType: false);

        var value = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadProperty(getter, new LoadArgument(0, "cache", owner), [new Constant(loadIndex, intType)]),
            new Constant(3, intType));
        var block = new Block();
        block.Add(new StoreProperty(
            setter,
            new LoadArgument(0, "cache", owner),
            [new Constant(storeIndex, intType)],
            value));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("cache", owner)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static (TypeRef Owner, TypeRef ValueType, TypeRef IntType, MethodRef Getter, MethodRef Setter) IndexerRefs(bool stringType)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Indexer");
        var intType = TypeRef.CoreLib("System", "Int32");
        var valueType = stringType ? TypeRef.CoreLib("System", "String") : intType;
        var getter = new MethodRef(owner, "get_Item", valueType, [intType], HasThis: true)
        {
            IsSpecialName = true,
        };
        var setter = new MethodRef(owner, "set_Item", TypeRef.CoreLib("System", "Void"), [intType, valueType], HasThis: true)
        {
            IsSpecialName = true,
        };
        return (owner, valueType, intType, getter, setter);
    }

    static IrFunction FunctionWithPropertyNullAssignment(TypeRef getterType, TypeRef setterType)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "PropertyOwner");
        var getter = new MethodRef(owner, "get_P", getterType, [], HasThis: true)
        {
            IsSpecialName = true,
        };
        var setter = new MethodRef(owner, "set_P", TypeRef.CoreLib("System", "Void"), [setterType], HasThis: true)
        {
            IsSpecialName = true,
        };

        var condition = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadProperty(getter, new LoadArgument(0, "owner", owner), []),
            new Constant(null, getterType));
        var then = new Block();
        then.Add(new StoreProperty(
            setter,
            new LoadArgument(0, "owner", owner),
            [],
            new LoadArgument(1, "fallback", setterType)));

        var block = new Block();
        block.Add(new IfStatement(condition, then, elseArm: null));
        block.Add(new Return(new Constant(null, TypeRef.CoreLib("System", "Void"))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("owner", owner), new Parameter("fallback", setterType)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
