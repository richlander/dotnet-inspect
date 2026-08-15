using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The three opt-in <c>var</c> spelling buckets: built-in types, apparent
/// non-built-in types, and elsewhere.
///
/// <para>
/// <c>var</c> is byte-neutral — a compile-time inference with no IL consequence — so
/// this is a spelling choice, not a lens: the emitted <c>var</c> form recompiles to
/// the exact same IL as the explicit form when the initializer's rendered natural
/// type is exactly the declared type. These tests pin the three-way partition, exact
/// positives, the either/or interaction with target-typed <c>new</c>, and close
/// conversion/contextual negatives.
/// </para>
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class VarWhenApparentTests
{
    static string AssemblyPath => typeof(VarWhenApparentTests).Assembly.Location;

    static readonly PrinterOptions VarForBuiltInTypes = new() { PreferVarForBuiltInTypes = true };
    static readonly PrinterOptions VarWhenApparent = new() { PreferVarWhenTypeApparent = true };
    static readonly PrinterOptions VarElsewhere = new() { PreferVarElsewhere = true };
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");

    static ApiType Specimen()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        return Assert.Single(api.Types, t => t.FullName == typeof(VarWhenApparentSpecimen).FullName);
    }

    static string Render(string memberName, PrinterOptions? options = null)
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == memberName);
        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null, printerOptions: options);
        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.NotNull(rendered.Text);
        return rendered.Text!;
    }

    static string RenderSynthetic(TypeRef localType, IrExpression initializer, PrinterOptions options)
    {
        var block = new Block();
        block.Add(new StoreLocal(0, localType, initializer));
        block.Add(new Return(new LoadLocal(0, localType)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "VarSpelling"),
            new MethodSignature(localType, [], HasThis: false, GenericParameterCount: 0),
            [localType],
            body);
        return CSharpPrinter.Print(function, options).Output!;
    }

    static string RenderSyntheticStackSlot(TypeRef slotType, IrExpression initializer, PrinterOptions options)
    {
        var block = new Block();
        block.Add(new StoreStackSlot(0, initializer));
        block.Add(new Return(new LoadStackSlot(0, slotType)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "VarSpelling"),
            new MethodSignature(slotType, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
        return CSharpPrinter.Print(function, options).Output!;
    }

    [Fact]
    public void ObjectCreation_Default_KeepsExplicitTypeAndShortensNew()
    {
        var text = Render(nameof(VarWhenApparentSpecimen.ObjectCreation));
        // Byte-stable default: explicit LHS type, and the always-on target-typed-new
        // shortener drops the RHS type to `new()`.
        Assert.Contains("List<int>", text);
        Assert.Contains("= new();", text);
        Assert.DoesNotContain("var ", text);
    }

    [Fact]
    public void ObjectCreation_VarOn_SpellsVarAndKeepsExplicitNew()
    {
        var text = Render(nameof(VarWhenApparentSpecimen.ObjectCreation), VarWhenApparent);
        Assert.Contains("var ", text);
        // Either/or: spelling `var` suppresses the shortener, so the RHS keeps its
        // explicit `new List<int>()`. A bare `var x = new()` would be CS8754.
        Assert.Contains("= new List<int>();", text);
        Assert.DoesNotContain("new();", text);
    }

    [Fact]
    public void ArrayCreation_VarOn_SpellsVar_ArrayShapeIsNotBuiltIn()
    {
        var defaultText = Render(nameof(VarWhenApparentSpecimen.ArrayCreation));
        Assert.Contains("int[] ", defaultText);
        Assert.DoesNotContain("var ", defaultText);

        var text = Render(nameof(VarWhenApparentSpecimen.ArrayCreation), VarWhenApparent);
        Assert.Contains("var ", text);
        Assert.Contains("= new int[4];", text);
        Assert.DoesNotContain("int[] ", text);
    }

    [Fact]
    public void ReferenceCast_VarOn_SpellsVar()
    {
        var defaultText = Render(nameof(VarWhenApparentSpecimen.ReferenceCast));
        Assert.Contains("Node ", defaultText);
        Assert.Contains("= (Node)o;", defaultText);
        Assert.DoesNotContain("var ", defaultText);

        var text = Render(nameof(VarWhenApparentSpecimen.ReferenceCast), VarWhenApparent);
        Assert.Contains("var ", text);
        // The cast still names the type, so the value keeps its exact static type.
        Assert.Contains("= (Node)o;", text);
    }

    [Fact]
    public void BuiltInObjectCreation_VarOn_DeclinesBuiltInType()
    {
        // `string` is a C# built-in keyword type, owned by the separate built-in-types
        // bucket. The apparent bucket must decline, leaving the output byte-identical
        // to the default (which still shortens to `new(...)`).
        var defaultText = Render(nameof(VarWhenApparentSpecimen.BuiltInObjectCreation));
        var text = Render(nameof(VarWhenApparentSpecimen.BuiltInObjectCreation), VarWhenApparent);
        Assert.Equal(defaultText, text);
        Assert.Contains("string ", text);
        Assert.DoesNotContain("var ", text);
    }

    [Fact]
    public void BuiltInObjectCreation_BuiltInBucket_SpellsVarAndKeepsExplicitNew()
    {
        var text = Render(nameof(VarWhenApparentSpecimen.BuiltInObjectCreation), VarForBuiltInTypes);

        Assert.Contains("var ", text);
        Assert.Contains("= new string('x', 3);", text);
        Assert.DoesNotContain("new('x', 3)", text);
    }

    [Fact]
    public void NotApparent_VarOn_Declines()
    {
        // The initializer is a plain call, so the type is not apparent — the bucket
        // declines and the output is byte-identical to the default.
        var defaultText = Render(nameof(VarWhenApparentSpecimen.NotApparent));
        var text = Render(nameof(VarWhenApparentSpecimen.NotApparent), VarWhenApparent);
        Assert.Equal(defaultText, text);
        Assert.Contains("List<int> ", text);
        Assert.DoesNotContain("var ", text);
    }

    [Fact]
    public void NotApparent_ElsewhereBucket_SpellsVar()
    {
        var text = Render(nameof(VarWhenApparentSpecimen.NotApparent), VarElsewhere);

        Assert.Contains("var ", text);
        Assert.Contains("= Make();", text);
        Assert.DoesNotContain("List<int> ", text);
    }

    [Fact]
    public void ElsewhereBucket_SpellsVarForDeclaringStackSlot()
    {
        var array = TypeRef.SzArray(Int32);
        var owner = TypeRef.Definition("Synthetic", "", "VarSpelling");
        var make = new MethodRef(owner, "Make", array, [], HasThis: false);
        var text = RenderSyntheticStackSlot(array, new Call(make, isVirtual: false, []), VarElsewhere);

        Assert.Contains("var S_0 = Make();", text);
        Assert.DoesNotContain("int[] S_0", text);
    }

    [Theory]
    [InlineData(nameof(VarWhenApparentSpecimen.BuiltInNumericWidening), "long ")]
    [InlineData(nameof(VarWhenApparentSpecimen.BuiltInConstantConversion), "byte ")]
    public void BuiltInBucket_DeclinesWhenInitializerWouldInferDifferentType(string method, string declaration)
    {
        var defaultText = Render(method);
        var text = Render(method, VarForBuiltInTypes);

        Assert.Equal(defaultText, text);
        Assert.Contains(declaration, text);
        Assert.DoesNotContain("var ", text);
    }

    [Fact]
    public void ElsewhereBucket_DeclinesReferenceConversion()
    {
        var defaultText = Render(nameof(VarWhenApparentSpecimen.ElsewhereReferenceWidening));
        var text = Render(nameof(VarWhenApparentSpecimen.ElsewhereReferenceWidening), VarElsewhere);

        Assert.Equal(defaultText, text);
        Assert.Contains("IReadOnlyCollection<int> ", text);
        Assert.DoesNotContain("var ", text);
    }

    [Fact]
    public void ElsewhereBucket_DeclinesContextuallyTypedTupleElements()
    {
        var defaultText = Render(nameof(VarWhenApparentSpecimen.TupleElementConversion));
        var text = Render(nameof(VarWhenApparentSpecimen.TupleElementConversion), VarElsewhere);

        Assert.Equal(defaultText, text);
        Assert.DoesNotContain("var ", text);
    }

    [Theory]
    [InlineData(nameof(VarWhenApparentSpecimen.DynamicParameterToObject))]
    [InlineData(nameof(VarWhenApparentSpecimen.DynamicReturnToObject))]
    public void BuiltInBucket_DeclinesDynamicErasedAsObject(string method)
    {
        var defaultText = Render(method);
        var text = Render(method, VarForBuiltInTypes);

        Assert.Equal(defaultText, text);
        Assert.Contains("object ", text);
        Assert.DoesNotContain("var ", text);
    }

    [Theory]
    [InlineData(nameof(VarWhenApparentSpecimen.NestedDynamicParameterToObjects), "List<object> ")]
    [InlineData(nameof(VarWhenApparentSpecimen.NestedDynamicReturnToObjects), "List<object> ")]
    [InlineData(nameof(VarWhenApparentSpecimen.NestedDynamicArrayToObjects), "object[] ")]
    public void ElsewhereBucket_DeclinesDynamicErasedInsideTypeShape(string method, string declaration)
    {
        var defaultText = Render(method);
        var text = Render(method, VarElsewhere);

        Assert.Equal(defaultText, text);
        Assert.Contains(declaration, text);
        Assert.DoesNotContain("var ", text);
    }

    [Fact]
    public void ApparentBucket_AllowsNestedObjectWhenSyntaxProvesStaticType()
    {
        var text = Render(nameof(VarWhenApparentSpecimen.NestedObjectCreation), VarWhenApparent);

        Assert.Contains("var ", text);
        Assert.Contains("= new List<object>();", text);
        Assert.DoesNotContain("List<object> values", text);
    }

    [Fact]
    public void BuiltInBucket_DeclinesNull()
    {
        var text = RenderSynthetic(String, new Constant(null, String), VarForBuiltInTypes);

        Assert.Contains("string V_0 = null;", text);
        Assert.DoesNotContain("var ", text);
    }

    [Fact]
    public void ElsewhereBucket_DeclinesTargetTypedCollectionExpression()
    {
        var array = TypeRef.SzArray(Int32);
        var collection = new CollectionExpression(
            Int32,
            array,
            [new Constant(1, Int32), new Constant(2, Int32)]);
        var text = RenderSynthetic(array, collection, VarElsewhere);

        Assert.Contains("int[] V_0 = [1, 2];", text);
        Assert.DoesNotContain("var ", text);
    }
}
