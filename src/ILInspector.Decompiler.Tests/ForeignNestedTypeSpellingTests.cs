using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #1581: a non-generic nested type referenced from a foreign scope must be
// spelled through its declaring chain (`Outer.Inner`); the bare innermost name
// `Inner` only binds inside the enclosing type. The body printer previously
// rendered every nested definition as its innermost name, emitting invalid C#
// from a foreign scope while still claiming Full fidelity.
public class ForeignNestedTypeSpellingTests
{
    static string RenderBody(string typeFullName, string method)
    {
        using var source = MetadataSource.Open(typeof(Repro.Outer).Assembly.Location);
        var function = IrImporter.Import(source, typeFullName, method);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, m => IrImporter.Import(source, m));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void ForeignNestedType_QualifiesThroughDeclaringChain()
    {
        string body = RenderBody("Repro.Other", nameof(Repro.Other.ForeignReturn));

        // From `Repro.Other`, `Inner` is not in scope: the body must qualify it.
        Assert.Equal("return new Outer.Inner { Value = 2 };", body);
        Assert.DoesNotContain("new Inner", body);
    }

    [Fact]
    public void InScopeNestedType_KeepsBareInnermostName()
    {
        string body = RenderBody("Repro.Outer", nameof(Repro.Outer.InScope));

        // Inside `Repro.Outer`, the bare innermost name binds and stays idiomatic.
        Assert.Equal("return new Inner { Value = 1 };", body);
        Assert.DoesNotContain("Outer.Inner", body);
    }

    [Fact]
    public void Spellability_ValidatesEveryNestedSegment_NotJustTheLeaf()
    {
        // A valid declaring chain has a C# spelling for every segment.
        var valid = TypeRef.Definition("Asm", "N", "Outer+Inner");
        Assert.False(CSharpSpellability.HasUnrepresentableMetadataName(new LoadArgument(0, "x", valid)));

        // An unspellable outer segment (compiler-generated `<>`-shaped name) with a
        // valid leaf must still be flagged: the foreign-scope spelling would emit
        // the whole chain, so `Outer.Inner` is not Full-fidelity C#.
        var invalidOuter = TypeRef.Definition("Asm", "N", "<>c__Outer+Inner");
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(new LoadArgument(0, "x", invalidOuter)));
    }

    [Fact]
    public void GenericOuterNestedDefinition_SpellsUnboundArityPerSegment()
    {
        var foreignScope = TypeRef.Definition("Asm", "N", "Other");

        // An open generic outer must be spelled unbound (Outer<>.Inner); a bare
        // `Outer.Inner` fails CS0305 in e.g. typeof(Outer<>.Inner).
        var genericOuter = TypeRef.Definition("Asm", "N", "Outer`1+Inner");
        Assert.Equal("Outer<>.Inner", genericOuter.ToDisplayString(foreignScope));

        var twoParamOuterGenericInner = TypeRef.Definition("Asm", "N", "Outer`2+Inner`1");
        Assert.Equal("Outer<,>.Inner<>", twoParamOuterGenericInner.ToDisplayString(foreignScope));

        // A non-generic chain is unchanged, and the bare innermost name is kept
        // when the enclosing type is the printing scope.
        var plain = TypeRef.Definition("Asm", "N", "Outer+Inner");
        Assert.Equal("Outer.Inner", plain.ToDisplayString(foreignScope));
        Assert.Equal("Inner", plain.ToDisplayString(TypeRef.Definition("Asm", "N", "Outer")));
    }
}
