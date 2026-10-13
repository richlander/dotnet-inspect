using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
        Assert.Equal(name, detached.ToDefinitionName(TextPolicy.Field));
        object backing = Backing(detached.Namespace);
        Assert.All(
            detached.Segments,
            segment => Assert.Same(backing, Backing(segment)));
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
