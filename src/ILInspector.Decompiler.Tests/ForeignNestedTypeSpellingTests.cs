using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

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

        // Keyword segments are legal identifiers via @ escaping; the declaring
        // chain must escape each segment, not emit a raw keyword (CS1001).
        var keywordLeaf = TypeRef.Definition("Asm", "N", "Outer+class");
        Assert.Equal("Outer.@class", keywordLeaf.ToDisplayString(foreignScope));
        Assert.Equal("@class", keywordLeaf.ToDisplayString(TypeRef.Definition("Asm", "N", "Outer")));
    }

    [Fact]
    public void ForeignNestedInstance_UsesTrustedMissingOuterArity()
    {
        MetadataTypeDefinitionName exact =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N",
                    ["Outer", "Inner"]))
            .Name;
        var definition = TypeRef.DefinitionWithResolution(
            "Asm",
            "N",
            "Outer+Inner",
            ValueTypeHint.Unknown,
            MetadataFactState.Unknown,
            enclosingType: null,
            definitionName: exact,
            resolutionAssembly: null,
            introducedTypeParameterCounts: [1, 0]);
        var instance = TypeRef.GenericInstance(
            definition,
            [TypeRef.CoreLib("System", "Int32")]);

        Assert.Equal(
            "Outer<int>.Inner",
            instance.ToDisplayString(TypeRef.Definition("Asm", "N", "Other")));
        Assert.False(instance.HasUnrenderableGenericArity);
    }

    // #4217: a backtick is a legal metadata-name character, so only the canonical
    // `N form is an arity suffix (MetadataNameArity owns that rule). Truncating at
    // the first backtick instead spelled `Widget`Literal` as the unrelated
    // `Widget` — a distinct type — and claimed it as Full-fidelity C#.
    [Fact]
    public void LiteralBacktickName_KeepsItsIdentityAndIsReportedUnspellable()
    {
        var foreignScope = TypeRef.Definition("Asm", "N", "Other");

        // The backtick survives into the printer's sanitized spelling, so the two
        // types stay distinct instead of both spelling `Widget`.
        var literal = TypeRef.Definition("Asm", "N", "Widget`Literal");
        Assert.Equal("Widget_Literal", literal.ToDisplayString(foreignScope));
        Assert.NotEqual(
            TypeRef.Definition("Asm", "N", "Widget").ToDisplayString(foreignScope),
            literal.ToDisplayString(foreignScope));
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(
            new LoadArgument(0, "x", literal)));

        // The canonical suffix is still stripped, and a genuine generic type
        // remains spellable.
        var generic = TypeRef.Definition("Asm", "N", "Widget`1");
        Assert.Equal("Widget", generic.ToDisplayString(foreignScope));
        Assert.False(CSharpSpellability.HasUnrepresentableMetadataName(
            new LoadArgument(0, "x", generic)));

        // Per segment: a literal backtick in the outer segment is unspellable even
        // though the leaf carries a canonical suffix.
        var nested = TypeRef.Definition("Asm", "N", "Outer`Literal+Inner`1");
        Assert.Equal("Outer`Literal.Inner<>", nested.ToDisplayString(foreignScope));
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(
            new LoadArgument(0, "x", nested)));
    }

    [Fact]
    public void ExactSegments_DistinguishLiteralPlusFromNestedType()
    {
        TypeRef literal = ExactDefinition("A+B", "A+B");
        TypeRef nested = ExactDefinition("A+B", "A", "B");
        TypeRef legacyNested = TypeRef.Definition("Asm", "N", "A+B");
        var foreignScope = TypeRef.Definition("Asm", "N", "Other");

        Assert.NotEqual(literal, nested);
        Assert.NotEqual(literal, legacyNested);
        Assert.Equal(nested, legacyNested);
        Assert.Equal(nested.GetHashCode(), legacyNested.GetHashCode());
        Assert.Equal(2, new HashSet<TypeRef> { literal, nested }.Count);
        Assert.Equal("A_B", literal.ToDisplayString(foreignScope));
        Assert.Equal("A.B", nested.ToDisplayString(foreignScope));
        Assert.True(CSharpSpellability.HasUnrepresentableMetadataName(
            new LoadArgument(0, "x", literal)));
        Assert.False(CSharpSpellability.HasUnrepresentableMetadataName(
            new LoadArgument(0, "x", nested)));
        Assert.Equal(@"N.A\+B", CSharpBodyDiff.CanonicalTypeName(literal));
        Assert.Equal("N.A+B", CSharpBodyDiff.CanonicalTypeName(nested));
        Assert.Equal(@"N.A\+B", CSharpBodyDiff.TypeIdentityKey(literal));
        Assert.Equal("N.A+B", CSharpBodyDiff.TypeIdentityKey(nested));

        TypeRef literalArrayText = ExactDefinition("A[]", "A[]");
        TypeRef arrayShape = TypeRef.SzArray(
            ExactDefinition("A", "A"));
        Assert.Equal(
            @"N.A\[\]",
            CSharpBodyDiff.CanonicalTypeName(literalArrayText));
        Assert.Equal(
            "N.A[]",
            CSharpBodyDiff.CanonicalTypeName(arrayShape));
        Assert.NotEqual(
            CSharpBodyDiff.CanonicalTypeName(literalArrayText),
            CSharpBodyDiff.CanonicalTypeName(arrayShape));
    }

    static TypeRef ExactDefinition(
        string flattenedName,
        params string[] segments)
    {
        var valid = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", [.. segments]));
        return TypeRef.DefinitionWithResolution(
            "Asm",
            "N",
            flattenedName,
            ValueTypeHint.Unknown,
            MetadataFactState.Unknown,
            enclosingType: null,
            valid.Name,
            resolutionAssembly: null);
    }
}
