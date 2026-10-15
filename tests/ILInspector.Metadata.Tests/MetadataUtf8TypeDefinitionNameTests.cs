using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using InertText;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataUtf8TypeDefinitionNameTests
{
    [Fact]
    public void Detach_CopiesOnlySelectedPartsIntoOneBacking()
    {
        using FileStream stream =
            File.OpenRead(
                typeof(MetadataUtf8TypeDefinitionNameTests)
                    .Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MetadataTypeDefinitionName name =
            ValidName(
                "ILInspector.Metadata.Tests",
                nameof(MetadataUtf8TypeDefinitionNameTests),
                nameof(NestedFixture));
        TypeDefinitionHandle handle =
            Assert.Single(
                reader.TypeDefinitions,
                candidate =>
                    MetadataTypeDefinitionName.Matches(
                        reader,
                        candidate,
                        name,
                        out _)
                    is MetadataTypeDefinitionNameMatchResult.Match);

        MetadataUtf8TypeDefinitionName detached =
            MetadataUtf8TypeDefinitionNameReader.Detach(
                peReader,
                reader,
                handle);

        Assert.Equal(
            "ILInspector.Metadata.Tests",
            detached.Namespace.Contain(TextPolicy.Field).ToString());
        Assert.Equal(
            [
                nameof(MetadataUtf8TypeDefinitionNameTests),
                nameof(NestedFixture),
            ],
            detached.Segments
                .Select(segment =>
                    segment.Contain(TextPolicy.Field).ToString()));
        MetadataContainedTypeDefinitionName contained =
            detached.Contain(TextPolicy.Field);
        Assert.Equal(
            "ILInspector.Metadata.Tests",
            contained.Namespace.ToString());
        Assert.Equal(
            [
                nameof(MetadataUtf8TypeDefinitionNameTests),
                nameof(NestedFixture),
            ],
            contained.Segments.Select(segment =>
                segment.ToString()));
        object backing = Backing(detached.Namespace);
        Assert.All(
            detached.Segments,
            segment => Assert.Same(backing, Backing(segment)));
    }

    [Fact]
    public void Contain_KeepsPresentationSeparateFromExactIdentity()
    {
        string rawSegment = new('\u202E', 700);
        byte[] rawBytes = Encoding.UTF8.GetBytes(rawSegment);
        var detached =
            new MetadataUtf8TypeDefinitionName(
                new(
                    OpaqueByteRange.Copy("Example"u8)),
                ImmutableArray.Create(
                    new MetadataUtf8Name(
                        OpaqueByteRange.Copy(rawBytes))));

        MetadataContainedTypeDefinitionName contained =
            detached.Contain(TextPolicy.Field);

        MetadataUtf8Name segment = Assert.Single(detached.Segments);
        Assert.True(segment.ValueTextEquals(rawBytes));
        Assert.Equal(4_200, Assert.Single(contained.Segments).Length);
        Assert.Equal(
            string.Concat(
                Enumerable.Repeat("\\u202E", 700)),
            Assert.Single(contained.Segments).ToString());
    }

    [Fact]
    public void ExactInput_MatchReusesOneHostOwnedBacking()
    {
        using FileStream stream =
            File.OpenRead(
                typeof(MetadataUtf8TypeDefinitionNameTests)
                    .Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MetadataTypeDefinitionName name =
            ValidName(
                "ILInspector.Metadata.Tests",
                nameof(MetadataUtf8TypeDefinitionNameTests),
                nameof(NestedFixture));
        var input = new MetadataExactTypeInput(name);
        TypeDefinitionHandle handle =
            Assert.Single(
                reader.TypeDefinitions,
                candidate =>
                    MetadataTypeDefinitionName.Matches(
                        reader,
                        candidate,
                        name,
                        out _)
                    is MetadataTypeDefinitionNameMatchResult.Match);

        MetadataExactTypeMatch match =
            Assert.IsType<MetadataExactTypeMatch>(
                MetadataUtf8TypeDefinitionNameReader.Match(
                    peReader,
                    reader,
                    handle,
                    input));

        Assert.Same(input.Utf8, match.Name);
        object backing = Backing(match.Name.Namespace);
        Assert.All(
            match.Name.Segments,
            segment => Assert.Same(backing, Backing(segment)));
    }

    [Fact]
    public void ExactInput_MismatchCannotIssueMatch()
    {
        using FileStream stream =
            File.OpenRead(
                typeof(MetadataUtf8TypeDefinitionNameTests)
                    .Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MetadataTypeDefinitionName selected =
            ValidName(
                "ILInspector.Metadata.Tests",
                nameof(MetadataUtf8TypeDefinitionNameTests),
                nameof(NestedFixture));
        TypeDefinitionHandle handle =
            Assert.Single(
                reader.TypeDefinitions,
                candidate =>
                    MetadataTypeDefinitionName.Matches(
                        reader,
                        candidate,
                        selected,
                        out _)
                    is MetadataTypeDefinitionNameMatchResult.Match);

        MetadataTypeDefinitionName[] differentNames =
        [
            ValidName(
                "Different.Namespace",
                nameof(MetadataUtf8TypeDefinitionNameTests),
                nameof(NestedFixture)),
            ValidName(
                "ILInspector.Metadata.Tests",
                "DifferentRoot",
                nameof(NestedFixture)),
            ValidName(
                "ILInspector.Metadata.Tests",
                nameof(MetadataUtf8TypeDefinitionNameTests),
                "DifferentLeaf"),
        ];

        Assert.All(
            differentNames,
            different =>
                Assert.Null(
                    MetadataUtf8TypeDefinitionNameReader.Match(
                        peReader,
                        reader,
                        handle,
                        new MetadataExactTypeInput(different))));
    }

    private static object Backing(MetadataUtf8Name name)
    {
        OpaqueByteRange range =
            (OpaqueByteRange)typeof(MetadataUtf8Name)
                .GetField(
                    "_range",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(name)!;
        return typeof(OpaqueByteRange)
            .GetField(
                "_backing",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(range)!;
    }

    private static MetadataTypeDefinitionName ValidName(
        string @namespace,
        params string[] segments) =>
        MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments])
            is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : throw new InvalidOperationException(
                    "The test Type name must be valid.");

    private sealed class NestedFixture;
}
