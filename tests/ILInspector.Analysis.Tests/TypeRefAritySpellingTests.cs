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
        Assert.Equal(@"A`1\+B", literal.ToDisplayString());
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

        // A literal backtick declares no canonical arity, so the GenericInst's
        // supplied arguments remain visible.
        Assert.Equal(
            "Outer.Inner`Literal<int>",
            TypeRef.GenericInstance(
                ResolvedDefinition(
                    "Outer+Inner`Literal",
                    "Outer",
                    "Inner`Literal"),
                arguments).ToDisplayString());
        Assert.Equal(
            "Plain<int>",
            TypeRef.GenericInstance(
                ResolvedDefinition("Plain", "Plain"),
                arguments).ToDisplayString());
    }

    [Fact]
    public void GenericInstance_UsesValidatedNestedSegmentsAndInnermostArguments()
    {
        TypeRef[] arguments =
        [
            TypeRef.CoreLib("System", "String"),
            TypeRef.CoreLib("System", "Int32"),
        ];

        Assert.Equal(
            "N.Outer.Widget<int>",
            TypeRef.GenericInstance(
                ResolvedDefinition(
                    "Outer`1+Widget`1",
                    "Outer`1",
                    "Widget`1"),
                [.. arguments]).ToQualifiedDisplayString());
        Assert.Equal(
            "N.Outer.Widget<int>",
            TypeRef.GenericInstance(
                TypeRef.Definition(
                    "Asm",
                    "N",
                    "Outer`1+Widget`1"),
                [.. arguments]).ToQualifiedDisplayString());
    }

    [Fact]
    public void GenericInstance_PreservesAmbiguousOrMismatchedFlatNestedSpelling()
    {
        Assert.Equal(
            "N.Outer+Widget`1",
            TypeRef.GenericInstance(
                TypeRef.Definition("Asm", "N", "Outer+Widget`1"),
                [TypeRef.CoreLib("System", "Int32")])
            .ToQualifiedDisplayString());
        Assert.Equal(
            "N.Outer`1+Middle`1+Widget`1",
            TypeRef.GenericInstance(
                TypeRef.Definition(
                    "Asm",
                    "N",
                    "Outer`1+Middle`1+Widget`1"),
                [
                    TypeRef.CoreLib("System", "String"),
                    TypeRef.CoreLib("System", "Object"),
                    TypeRef.CoreLib("System", "Int32"),
                ])
            .ToQualifiedDisplayString());
    }

    [Fact]
    public void GenericInstance_CompletesExactCompilerGeneratedTerminalArity()
    {
        Assert.Equal(
            "N.Outer.<M>d__3<int, T3>",
            TypeRef.GenericInstance(
                ResolvedDefinition(
                    "Outer`1+<M>d__3`2",
                    "Outer`1",
                    "<M>d__3`2"),
                [
                    TypeRef.CoreLib("System", "String"),
                    TypeRef.CoreLib("System", "Int32"),
                ])
            .ToQualifiedDisplayString());

        Assert.Equal(
            "N.Outer`1.Widget`2",
            TypeRef.GenericInstance(
                ResolvedDefinition(
                    "Outer`1+Widget`2",
                    "Outer`1",
                    "Widget`2"),
                [
                    TypeRef.CoreLib("System", "String"),
                    TypeRef.CoreLib("System", "Int32"),
                ])
            .ToQualifiedDisplayString());
    }

    [Fact]
    public void GenericInstance_CompletesOnlyDisambiguatedFlatCompilerGeneratedArity()
    {
        Assert.Equal(
            "N.Outer.<M>d__3<int, T4>",
            TypeRef.GenericInstance(
                TypeRef.Definition(
                    "Asm",
                    "N",
                    "Outer`2+<M>d__3`2"),
                [
                    TypeRef.CoreLib("System", "String"),
                    TypeRef.CoreLib("System", "Object"),
                    TypeRef.CoreLib("System", "Int32"),
                ])
            .ToQualifiedDisplayString());

        Assert.Equal(
            "N.Outer`1+<M>d__3`2",
            TypeRef.GenericInstance(
                TypeRef.Definition(
                    "Asm",
                    "N",
                    "Outer`1+<M>d__3`2"),
                [TypeRef.CoreLib("System", "Int32")])
            .ToQualifiedDisplayString());
    }

    [Fact]
    public void GenericInstance_QualifiedDisplayKeepsTheDefinitionNamespace()
    {
        var dictionary = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "Dictionary`2"),
            [
                TypeRef.CoreLib("System", "String"),
                TypeRef.CoreLib("System", "Int32")
            ]);

        Assert.Equal(
            "System.Collections.Generic.Dictionary<string, int>",
            dictionary.ToQualifiedDisplayString());
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
