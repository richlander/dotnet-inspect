using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class ResourceScannerExtractionTests
{
    [Fact]
    public void ExtractAll_PreservesValidatedNestedPaths()
    {
        using var image = CreateAssembly(("nested/data.txt", "content"u8.ToArray()));
        using var output = new TemporaryDirectory();

        var extracted = ResourceScanner.ExtractAll(image, output.Path);

        var expected = System.IO.Path.Combine(output.Path, "nested", "data.txt");
        Assert.Equal([expected], extracted);
        Assert.Equal("content", File.ReadAllText(expected));
    }

    [Fact]
    public void ExtractAll_PreservesEmptyResource()
    {
        using var image = CreateAssembly(("empty.txt", []));
        using var output = new TemporaryDirectory();

        var extracted = ResourceScanner.ExtractAll(image, output.Path);

        var expected = System.IO.Path.Combine(output.Path, "empty.txt");
        Assert.Equal([expected], extracted);
        Assert.Empty(File.ReadAllBytes(expected));
    }

    [Fact]
    public void ExtractAll_RejectsTraversalBeforeWritingAnyResource()
    {
        using var image = CreateAssembly(
            ("safe.txt", "safe"u8.ToArray()),
            ("../escaped.txt", "escaped"u8.ToArray()));
        using var parent = new TemporaryDirectory();
        var output = System.IO.Path.Combine(parent.Path, "output");
        var escaped = System.IO.Path.Combine(parent.Path, "escaped.txt");

        var exception = Assert.Throws<InvalidDataException>(
            () => ResourceScanner.ExtractAll(image, output));

        Assert.Contains("safe relative extraction path", exception.Message);
        Assert.False(Directory.Exists(output));
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public void ExtractAll_RejectsRootedResourceName()
    {
        using var parent = new TemporaryDirectory();
        var escaped = System.IO.Path.Combine(parent.Path, "escaped.txt");
        using var image = CreateAssembly((escaped, "escaped"u8.ToArray()));
        var output = System.IO.Path.Combine(parent.Path, "output");

        Assert.Throws<InvalidDataException>(() => ResourceScanner.ExtractAll(image, output));

        Assert.False(Directory.Exists(output));
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public void ExtractAll_RejectsNormalizedDestinationCollisionBeforeWriting()
    {
        using var image = CreateAssembly(
            ("nested/data.txt", "first"u8.ToArray()),
            (@"nested\data.txt", "second"u8.ToArray()));
        using var parent = new TemporaryDirectory();
        var output = System.IO.Path.Combine(parent.Path, "output");

        var exception = Assert.Throws<InvalidDataException>(
            () => ResourceScanner.ExtractAll(image, output));

        Assert.Contains("duplicate extraction path", exception.Message);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void ExtractAll_RejectsCaseFoldedDestinationCollisionBeforeWriting()
    {
        using var image = CreateAssembly(
            ("Data.txt", "first"u8.ToArray()),
            ("data.txt", "second"u8.ToArray()));
        using var parent = new TemporaryDirectory();
        var output = System.IO.Path.Combine(parent.Path, "output");

        Assert.Throws<InvalidDataException>(() => ResourceScanner.ExtractAll(image, output));

        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void ExtractAll_RejectsFileDirectoryPrefixConflictBeforeWriting()
    {
        using var image = CreateAssembly(
            ("nested", "file"u8.ToArray()),
            ("nested/data.txt", "nested"u8.ToArray()));
        using var parent = new TemporaryDirectory();
        var output = System.IO.Path.Combine(parent.Path, "output");

        var exception = Assert.Throws<InvalidDataException>(
            () => ResourceScanner.ExtractAll(image, output));

        Assert.Contains("conflicts with another resource path", exception.Message);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void ExtractAll_RefusesToOverwriteExistingFile()
    {
        using var image = CreateAssembly(("data.txt", "replacement"u8.ToArray()));
        using var output = new TemporaryDirectory();
        var destination = System.IO.Path.Combine(output.Path, "data.txt");
        File.WriteAllText(destination, "original");

        var exception = Assert.Throws<IOException>(
            () => ResourceScanner.ExtractAll(image, output.Path));

        Assert.Contains("already exists", exception.Message);
        Assert.Equal("original", File.ReadAllText(destination));
    }

    [Fact]
    public void ExtractAll_RejectsCaseFoldedExistingFileAlias()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            return;

        using var image = CreateAssembly(("data.txt", "replacement"u8.ToArray()));
        using var output = new TemporaryDirectory();
        var existing = System.IO.Path.Combine(output.Path, "Data.txt");
        File.WriteAllText(existing, "original");

        Assert.Throws<IOException>(() => ResourceScanner.ExtractAll(image, output.Path));

        Assert.Equal("original", File.ReadAllText(existing));
        Assert.False(File.Exists(System.IO.Path.Combine(output.Path, "data.txt")));
    }

    [Fact]
    public void ExtractAll_RejectsCaseFoldedExistingDirectoryAlias()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            return;

        using var image = CreateAssembly(("nested/data.txt", "content"u8.ToArray()));
        using var output = new TemporaryDirectory();
        var existing = System.IO.Path.Combine(output.Path, "Nested");
        Directory.CreateDirectory(existing);

        Assert.Throws<IOException>(() => ResourceScanner.ExtractAll(image, output.Path));

        Assert.Empty(Directory.EnumerateFiles(existing));
        Assert.False(Directory.Exists(System.IO.Path.Combine(output.Path, "nested")));
    }

    [Fact]
    public void ExtractAll_RejectsSymbolicLinkBelowOutputRoot()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var image = CreateAssembly(("nested/data.txt", "escaped"u8.ToArray()));
        using var output = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        Directory.CreateSymbolicLink(
            System.IO.Path.Combine(output.Path, "nested"),
            outside.Path);

        var exception = Assert.Throws<IOException>(
            () => ResourceScanner.ExtractAll(image, output.Path));

        Assert.Contains("symbolic link or reparse point", exception.Message);
        Assert.False(File.Exists(System.IO.Path.Combine(outside.Path, "data.txt")));
    }

    [Fact]
    public void ExtractAll_RejectsMalformedResourceDataBeforeWriting()
    {
        using var image = CreateAssembly(
            ("safe.txt", 4, "safe"u8.ToArray()),
            ("malformed.txt", 1024, "short"u8.ToArray()));
        using var parent = new TemporaryDirectory();
        var output = System.IO.Path.Combine(parent.Path, "output");

        var exception = Assert.Throws<InvalidDataException>(
            () => ResourceScanner.ExtractAll(image, output));

        Assert.Contains("malformed.txt", exception.Message);
        Assert.Contains("invalid data range", exception.Message);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("nested//data.txt")]
    [InlineData("data.txt.")]
    [InlineData("NUL")]
    [InlineData("COM\u00B9.txt")]
    [InlineData("value:stream")]
    [InlineData("wild*.txt")]
    [InlineData("question?.txt")]
    [InlineData("pipe|.txt")]
    [InlineData("line\nbreak.txt")]
    public void ExtractAll_RejectsUnsafeResourceNames(string name)
    {
        using var image = CreateAssembly((name, "content"u8.ToArray()));
        using var parent = new TemporaryDirectory();
        var output = System.IO.Path.Combine(parent.Path, "output");

        Assert.Throws<InvalidDataException>(() => ResourceScanner.ExtractAll(image, output));

        Assert.False(Directory.Exists(output));
    }

    static MemoryStream CreateAssembly(params (string Name, byte[] Content)[] resources)
        => CreateAssembly(
            resources
                .Select(resource => (resource.Name, resource.Content.Length, resource.Content))
                .ToArray());

    static MemoryStream CreateAssembly(
        params (string Name, int DeclaredSize, byte[] Content)[] resources)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ResourceFixture.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ResourceFixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var resourceData = new BlobBuilder();
        foreach (var (name, declaredSize, content) in resources)
        {
            int offset = resourceData.Count;
            resourceData.WriteInt32(declaredSize);
            resourceData.WriteBytes(content);
            metadata.AddManifestResource(
                ManifestResourceAttributes.Public,
                metadata.GetOrAddString(name),
                default,
                (uint)offset);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            managedResources: resourceData,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return new MemoryStream(image.ToArray());
    }

    sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"resource-extraction-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
