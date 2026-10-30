using DotnetInspector.Presentation;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Presentation.Tests;

public class TypeShapePresentationTests
{
    [Fact]
    public void Write_GroupsMethodOverloadsByLogicalName()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new()
                {
                    Kind = "method",
                    Name = "Parse",
                    Signature = "Widget Parse(string value)",
                },
                new()
                {
                    Kind = "method",
                    Name = "Parse",
                    Signature =
                        "Widget Parse(ReadOnlySpan<char> value)",
                },
                new()
                {
                    Kind = "method",
                    Name = "Format",
                    Signature = "string Format()",
                },
            ],
        };

        string output = Render(type);

        Assert.Equal(
            """
            Widget
            └─ Methods (2 logical, 3 overloads)
               ├─ string Format()
               └─ Parse (2 overloads)

            """.ReplaceLineEndings(),
            output);
    }

    [Fact]
    public void Write_MemberLimitCountsCollapsedAndExpandedEntries()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new()
                {
                    Kind = "method",
                    Name = "Alpha",
                    Signature = "void Alpha()",
                },
                new()
                {
                    Kind = "method",
                    Name = "Alpha",
                    Signature = "void Alpha(int value)",
                },
                new()
                {
                    Kind = "method",
                    Name = "Beta",
                    Signature = "void Beta()",
                },
            ],
        };

        string collapsed = Render(
            type,
            new TypeShapePresentationPlan(MemberLimit: 1));
        string expanded = Render(
            type,
            new TypeShapePresentationPlan(
                ExpandOverloads: true,
                MemberLimit: 1));

        Assert.Equal(
            """
            Widget
            └─ Methods (1 logical, 2 overloads)
               └─ Alpha (2 overloads)

            """.ReplaceLineEndings(),
            collapsed);
        Assert.Equal(
            """
            Widget
            └─ Methods (1)
               └─ void Alpha()

            """.ReplaceLineEndings(),
            expanded);
    }

    [Fact]
    public void Write_ExpandedOperatorLimitUsesDisplayOrder()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new()
                {
                    Kind = "operator",
                    Name = "op_Addition",
                    Signature =
                        "Widget op_Addition(Widget left, Widget right)",
                },
                new()
                {
                    Kind = "operator",
                    Name = "op_Explicit",
                    Signature = "Widget op_Explicit(int value)",
                },
            ],
        };

        string output = Render(
            type,
            new TypeShapePresentationPlan(
                ExpandOverloads: true,
                MemberLimit: 1));

        Assert.Equal(
            """
            Widget
            └─ Operators (1)
               └─ Widget op_Explicit(int value)

            """.ReplaceLineEndings(),
            output);
    }

    [Theory]
    [InlineData("Handle", null, "~Handle()")]
    [InlineData(
        "GenericOuter`1.Nested",
        null,
        "~Nested()")]
    [InlineData("A+B", "A+B", @"~A\+B()")]
    public void Write_FinalizerUsesDestructorSpelling(
        string name,
        string? exactLeaf,
        string expectedDestructor)
    {
        MetadataTypeDefinitionName? definitionName =
            exactLeaf is null
                ? null
                : Assert.IsType<
                    MetadataTypeDefinitionNameResult.Valid>(
                        MetadataTypeDefinitionName.Create(
                            "",
                            [exactLeaf]))
                    .Name;
        var type = new ApiType
        {
            Name = name,
            DefinitionName = definitionName,
            Kind = "class",
            Members =
            [
                new()
                {
                    Name = "Finalize",
                    Kind = "finalizer",
                    Signature = "void Finalize()",
                    IsFinalizer = true,
                },
            ],
        };

        string output = Render(type);

        Assert.Contains(expectedDestructor, output);
        Assert.DoesNotContain("void Finalize()", output);
    }

    private static string Render(
        ApiType type,
        TypeShapePresentationPlan? plan = null)
    {
        using var writer = new StringWriter();
        TypeShapePresentation.Write(
            type,
            type.Members,
            plan ?? new TypeShapePresentationPlan(),
            writer);
        return writer.ToString();
    }
}
