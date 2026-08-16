using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// #4217: a backtick is a legal metadata-name character, so a display name may
/// drop only the canonical <c>`N</c> arity suffix. Truncating at the first
/// backtick gave <c>Widget`Literal</c> the display identity of the unrelated
/// type <c>Widget</c>, and read a non-numeric suffix as no arity at all while
/// still removing it.
/// </summary>
public class TypeRefAritySpellingTests
{
    [Fact]
    public void DisplayName_StripsOnlyCanonicalAritySuffix()
    {
        Assert.Equal("Widget", TypeRef.Definition("Asm", "N", "Widget`1").ToDisplayString());
        Assert.Equal(
            "Widget`Literal",
            TypeRef.Definition("Asm", "N", "Widget`Literal").ToDisplayString());
        Assert.Equal("Widget`0", TypeRef.Definition("Asm", "N", "Widget`0").ToDisplayString());
        Assert.NotEqual(
            TypeRef.Definition("Asm", "N", "Widget").ToDisplayString(),
            TypeRef.Definition("Asm", "N", "Widget`Literal").ToDisplayString());
    }

    [Fact]
    public void DisplayName_StripsArityPerNestedSegment()
    {
        Assert.Equal(
            "Outer.Inner",
            ResolvedDefinition("Outer`1+Inner`2", "Outer`1", "Inner`2").ToDisplayString());
        Assert.Equal(
            "Outer`Literal.Inner",
            ResolvedDefinition(
                "Outer`Literal+Inner`1",
                "Outer`Literal",
                "Inner`1").ToDisplayString());
        Assert.Equal(
            "N.Outer`Literal.Inner",
            ResolvedDefinition(
                "Outer`Literal+Inner`1",
                "Outer`Literal",
                "Inner`1").ToQualifiedDisplayString());
    }

    [Fact]
    public void DisplayName_DoesNotInventStructureForAmbiguousFlatNames()
    {
        Assert.Equal(
            "A`1+B",
            TypeRef.Definition("Asm", "N", "A`1+B").ToDisplayString());
        Assert.NotEqual(
            TypeRef.Definition("Asm", "N", "A`1+B").ToDisplayString(),
            TypeRef.Definition("Asm", "N", "A+B").ToDisplayString());
    }

    [Fact]
    public void DisplayName_UsesExactResolutionSegmentsWhenAvailable()
    {
        TypeRef nested = ResolvedDefinition("A`1+B", "A`1", "B");
        TypeRef literal = ResolvedDefinition("A`1+B", "A`1+B");

        Assert.Equal("A.B", nested.ToDisplayString());
        Assert.Equal("A`1+B", literal.ToDisplayString());
        Assert.NotEqual(nested.ToDisplayString(), literal.ToDisplayString());
    }

    [Fact]
    public void GenericInstance_TakesArgumentsFromTheInnermostCanonicalArity()
    {
        var arguments = ImmutableArray.Create(TypeRef.CoreLib("System", "Int32"));

        // The innermost segment declares one parameter, so the instance spells it.
        Assert.Equal(
            "Outer.Inner<int>",
            TypeRef.GenericInstance(
                ResolvedDefinition("Outer+Inner`1", "Outer", "Inner`1"),
                arguments).ToDisplayString());

        // A literal backtick declares none, so no argument list is attached and
        // the name keeps its identity.
        Assert.Equal(
            "Outer.Inner`Literal",
            TypeRef.GenericInstance(
                ResolvedDefinition(
                    "Outer+Inner`Literal",
                    "Outer",
                    "Inner`Literal"),
                arguments).ToDisplayString());
    }

    static TypeRef ResolvedDefinition(string flattenedName, params string[] segments)
    {
        var result = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", [.. segments]));
        return TypeRef.Definition(
            "Asm",
            "N",
            flattenedName,
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                result.Name));
    }
}
