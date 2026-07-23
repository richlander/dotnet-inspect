using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class TargetTypedNewPassTests
{
    static readonly ILInspector.Metadata.IAssemblyReferenceResolver RuntimeResolver =
        TestAssemblyReferenceResolvers.RuntimeAssemblies();

    static string PrintRaised(string methodName)
    {
        using var context = new MetadataContext(RuntimeResolver);
        using var source = MetadataSource.Open(typeof(TargetTypedNewFixtures).Assembly.Location, null, RuntimeResolver, context);
        var function = IrImporter.Import(source, typeof(TargetTypedNewFixtures).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void LocalDeclaration_ShortensToTargetTypedNew()
    {
        string output = PrintRaised(nameof(TargetTypedNewFixtures.LocalDeclaration));

        Assert.Contains("= new(", output);
        Assert.DoesNotContain("new StringBuilder(", output);
    }

    [Fact]
    public void FieldStore_ShortensToTargetTypedNew()
    {
        string output = PrintRaised(nameof(TargetTypedNewFixtures.FieldStore));

        Assert.Contains("= new(", output);
        Assert.DoesNotContain("new StringBuilder(", output);
    }

    [Fact]
    public void ReturnPosition_KeepsExplicitType_OutOfScopeForNow()
    {
        // Return positions are intentionally out of the v1 LHS-only scope: the type
        // is apparent from the signature but not on an assignment target, so the
        // explicit spelling is kept until a follow-up extends the transform there.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.ReturnPosition));

        Assert.Contains("return new StringBuilder(", output);
        Assert.DoesNotContain("return new(", output);
    }

    [Fact]
    public void StructLocal_ShortensToTargetTypedNew()
    {
        string output = PrintRaised(nameof(TargetTypedNewFixtures.StructLocal));

        Assert.Contains("= new(", output);
        Assert.DoesNotContain("new Box(", output);
    }

    [Fact]
    public void ArrayElementStore_ShortensToTargetTypedNew()
    {
        string output = PrintRaised(nameof(TargetTypedNewFixtures.ElementStore));

        Assert.Contains("] = new(", output);
        Assert.DoesNotContain("new Box(", output);
    }

    [Fact]
    public void InterfaceTarget_KeepsExplicitType()
    {
        // Target IList<int> is not the constructed List<int>, so `new()` would bind
        // the wrong type — the explicit spelling must stay.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.InterfaceTargetDeclines));

        Assert.Contains("new List<int>(", output);
        Assert.DoesNotContain("= new(", output);
    }

    [Fact]
    public void MultiDimArray_KeepsArrayCreation()
    {
        // A rectangular-array `newobj` has no target-typed-new form.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.MultiDimArrayDeclines));

        Assert.Contains("new int[", output);
        Assert.DoesNotContain("= new(", output);
    }

    [Fact]
    public void ArgumentPosition_KeepsExplicitType()
    {
        // An argument-position `new()` would participate in overload resolution; the
        // transform never fires there.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.ArgumentPositionDeclines));

        Assert.Contains("new StringBuilder(", output);
    }

    [Fact]
    public void BareObjectTarget_KeepsExplicitType()
    {
        // A bare `object` target admits target-typed `new()` in C#, but the raw
        // `object` type is indistinguishable at this seam from a `dynamic` place
        // (erased to `object`), where `new()` is CS8752 — so `new object()` stays
        // explicit. Conservative and free: `new object()` has no type name to drop.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.ObjectTargetDeclines));

        Assert.Contains("new object(", output);
        Assert.DoesNotContain("= new(", output);
    }

    [Fact]
    public void DynamicParameterTarget_KeepsExplicitType()
    {
        // A `dynamic` parameter is spelled `dynamic value` in the signature but the IR
        // store carries the erased `object` type; shortening `value = new object()` to
        // `value = new()` would be CS8752. The bare-object decline keeps it valid.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.DynamicParamDeclines));

        Assert.Contains("new object(", output);
        Assert.DoesNotContain("value = new()", output);
    }

    // A covariant / adversarial array element store: the `stelem` token (the value
    // stored, here `Base`) is wider than the array expression's static element type
    // (`Derived`). C# types `items[0] = new()` from the array's element type, so
    // `new()` would bind `Derived` and construct `newobj Derived`, diverging from the
    // `newobj Base` the IL performs. The element-store guard requires the array's
    // static element type to equal the `stelem` token, so this declines and keeps the
    // explicit `new Base()`. Not producible from C# source (it is CS0029), so the IR
    // is built directly.
    [Fact]
    public void CovariantElementStore_TokenWiderThanArrayElement_KeepsExplicitType()
    {
        var baseType = TypeRef.Definition("synthetic", "N", "Base");
        var derivedType = TypeRef.Definition("synthetic", "N", "Derived");
        var voidType = TypeRef.CoreLib("System", "Void");
        var intType = TypeRef.CoreLib("System", "Int32");
        var derivedArray = TypeRef.SzArray(derivedType);

        var ctor = new MethodRef(baseType, ".ctor", voidType, [], HasThis: true);
        var store = new StoreElement(
            baseType,                                     // stelem token: Base
            new LoadArgument(1, "items", derivedArray),   // array static element: Derived
            new Constant(0, intType),
            new NewObject(ctor, []));

        string output = RenderElementStore(store, derivedArray);

        Assert.Contains("new Base(", output);
        Assert.DoesNotContain("= new(", output);
    }

    static string RenderElementStore(StoreElement store, TypeRef arrayParameterType)
    {
        var block = new Block(0);
        block.Add(store);
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "Void"),
            [new Parameter("items", arrayParameterType)],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "N", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape>
            {
                [TypeRef.Definition("synthetic", "N", "Base")] = TypeShape.Reference,
                [TypeRef.Definition("synthetic", "N", "Derived")] = TypeShape.Reference,
            },
        };

        return CSharpPrinter.Print(function).Output!.Trim();
    }
}
