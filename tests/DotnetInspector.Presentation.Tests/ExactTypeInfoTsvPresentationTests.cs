using System.Buffers;
using System.Text;

using DotnetInspector.Presentation;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Presentation.Tests;

public class ExactTypeInfoTsvPresentationTests
{
    [Fact]
    public void Write_PreservesContainedNestedNonAsciiTypeAsUtf8()
    {
        MetadataContainedTypeDefinitionName type =
            Contained("Δοκιμή", "Outer", "內部");
        var document = new ExactTypeInfoTsvDocument(
            type,
            "class",
            ["sealed"],
            2,
            "/app/Example.dll",
            "Library");
        var output = new ArrayBufferWriter<byte>();

        ExactTypeInfoTsvPresentation.Write(
            output,
            document,
            showHeader: true);

        Assert.Equal(
            """
            field	value
            Type	Δοκιμή.Outer.內部
            Kind	class
            Modifiers	sealed
            Interfaces	2
            Library	/app/Example.dll
            Source	Library

            """,
            Encoding.UTF8.GetString(output.WrittenSpan));
    }

    [Fact]
    public void Write_UsesContainedSpellingAndHonorsNoHeader()
    {
        MetadataContainedTypeDefinitionName type =
            Contained("Example", "Bad\u202EName");
        var document = new ExactTypeInfoTsvDocument(
            type,
            "interface",
            [],
            0,
            "Example.dll",
            "Library");
        var output = new ArrayBufferWriter<byte>();

        ExactTypeInfoTsvPresentation.Write(
            output,
            document,
            showHeader: false);

        Assert.Equal(
            """
            Type	Example.Bad\u202EName
            Kind	interface
            Library	Example.dll
            Source	Library

            """,
            Encoding.UTF8.GetString(output.WrittenSpan));
    }

    [Fact]
    public void Write_SpellsGenericParametersBySegment()
    {
        var document = new ExactTypeInfoTsvDocument(
            Contained("Example", "Outer", "Inner"),
            "interface",
            [],
            0,
            "Example.dll",
            "Library")
        {
            TypeParameters =
            [
                new("out", Name("T"), ["allows ref struct"]),
                new(null, Name("U"), []),
                new(null, Name("V"), ["class?", "new()"]),
            ],
            IntroducedTypeParameterCounts = [1, 2],
        };
        var output = new ArrayBufferWriter<byte>();

        ExactTypeInfoTsvPresentation.Write(
            output,
            document,
            showHeader: false);

        Assert.Equal(
            """
            Type	Example.Outer<T>.Inner<U, V>
            Kind	interface
            Type Parameters	out T : allows ref struct, U, V : class?, new()
            Library	Example.dll
            Source	Library

            """,
            Encoding.UTF8.GetString(output.WrittenSpan));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 0, 2 })]
    [InlineData(new[] { -1, 2 })]
    public void Write_RejectsCountsThatDoNotCoverTheParameters(
        int[] counts)
    {
        var document = new ExactTypeInfoTsvDocument(
            Contained("Example", "Outer", "Inner"),
            "class",
            [],
            0,
            "Example.dll",
            "Library")
        {
            TypeParameters = [new(null, Name("T"), [])],
            IntroducedTypeParameterCounts = counts,
        };

        Assert.ThrowsAny<ArgumentException>(
            () => ExactTypeInfoTsvPresentation.Write(
                new ArrayBufferWriter<byte>(),
                document,
                showHeader: false));
    }

    private static InertUtf8String Name(string value) =>
        Contained("N", value).Segments[0];

    private static MetadataContainedTypeDefinitionName Contained(
        string @namespace,
        params string[] segments)
    {
        MetadataTypeDefinitionName type =
            MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments]) switch
            {
                MetadataTypeDefinitionNameResult.Valid valid =>
                    valid.Name,
                MetadataTypeDefinitionNameResult.Rejected rejected =>
                    throw new InvalidOperationException(
                        rejected.Rejection.ToString()),
                _ => throw new InvalidOperationException(
                    "Unexpected metadata Type name result."),
            };
        return new MetadataExactTypeInput(type)
            .Contain(TextPolicy.Field);
    }
}
