using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The opt-in apparent-type <c>var</c> bucket
/// (<see cref="PrinterOptions.PreferVarWhenTypeApparent"/>,
/// <c>csharp_style_var_when_type_is_apparent</c>): when the declared type is apparent
/// from the initializer (object creation of the exact type, an array creation, or an
/// explicit reference cast) and is not a C# built-in keyword type, the declaration is
/// spelled <c>var</c> instead of its explicit type.
///
/// <para>
/// <c>var</c> is byte-neutral — a compile-time inference with no IL consequence — so
/// this is a spelling choice, not a lens: the emitted <c>var</c> form recompiles to
/// the exact same IL as the explicit form, because apparency guarantees the
/// initializer's static type <em>is</em> the declared type. These tests pin the
/// default (explicit, byte-stable), the opt-in rewrite for each apparent shape, the
/// either/or interaction with the target-typed-<c>new</c> shortener (never
/// <c>var x = new()</c>, which is CS8754), and the two decline cases (built-in type,
/// non-apparent initializer).
/// </para>
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class VarWhenApparentTests
{
    static string AssemblyPath => typeof(VarWhenApparentTests).Assembly.Location;

    static readonly PrinterOptions VarWhenApparent = new() { PreferVarWhenTypeApparent = true };

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
}
