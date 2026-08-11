using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class AssemblyReferenceResolverTests
{
    [Fact]
    public void SiblingResolver_ReusesSelectedDescriptorWithoutReopeningPath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sibling = Path.Combine(
            directory,
            Path.GetFileName(typeof(object).Assembly.Location));
        try
        {
            File.Copy(typeof(object).Assembly.Location, sibling);
            AssemblyReferenceIdentity identity = ReadIdentity(sibling);
            var resolver =
                new MetadataSource.SiblingAssemblyReferenceResolver(
                    Path.Combine(directory, "Owner.dll"));

            ResolvedAssemblyReference? first = resolver.Resolve(
                identity,
                AssemblyResolutionScope.Any);
            File.WriteAllText(sibling, "changed");
            ResolvedAssemblyReference? second = resolver.Resolve(
                identity,
                AssemblyResolutionScope.Any);

            Assert.NotNull(first);
            Assert.Same(first, second);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SiblingResolver_InvalidImageReturnsNull()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "Invalid.dll"),
                "not a PE image");
            var resolver =
                new MetadataSource.SiblingAssemblyReferenceResolver(
                    Path.Combine(directory, "Owner.dll"));

            ResolvedAssemblyReference? result = resolver.Resolve(
                new AssemblyReferenceIdentity(
                    "Invalid",
                    Version: null,
                    Culture: null,
                    PublicKeyToken: null),
                AssemblyResolutionScope.Any);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SiblingResolver_AssemblyReferenceNameCannotEscapeDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"resolver-{Guid.NewGuid():N}");
        string assemblyDirectory = Path.Combine(root, "app");
        Directory.CreateDirectory(assemblyDirectory);
        string payload = Path.Combine(root, "payload.dll");
        try
        {
            File.Copy(typeof(object).Assembly.Location, payload);
            var resolver =
                new MetadataSource.SiblingAssemblyReferenceResolver(
                    Path.Combine(assemblyDirectory, "Owner.dll"));

            ResolvedAssemblyReference? result = resolver.Resolve(
                new AssemblyReferenceIdentity(
                    "../payload",
                    Version: null,
                    Culture: null,
                    PublicKeyToken: null),
                AssemblyResolutionScope.Any);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SiblingResolver_RequiresCandidateMetadataIdentity()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string misleadingPath = Path.Combine(directory, "Expected.dll");
        try
        {
            File.Copy(typeof(object).Assembly.Location, misleadingPath);
            var resolver =
                new MetadataSource.SiblingAssemblyReferenceResolver(
                    Path.Combine(directory, "Owner.dll"));

            ResolvedAssemblyReference? result = resolver.Resolve(
                new AssemblyReferenceIdentity(
                    "Expected",
                    Version: null,
                    Culture: null,
                    PublicKeyToken: null),
                AssemblyResolutionScope.Any);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SiblingResolver_VersionIsDescriptiveButTokenStillBinds()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sibling = Path.Combine(
            directory,
            Path.GetFileName(typeof(object).Assembly.Location));
        try
        {
            File.Copy(typeof(object).Assembly.Location, sibling);
            AssemblyReferenceIdentity identity = ReadIdentity(sibling);
            var resolver =
                new MetadataSource.SiblingAssemblyReferenceResolver(
                    Path.Combine(directory, "Owner.dll"));

            ResolvedAssemblyReference? versionSkewed = resolver.Resolve(
                identity with
                {
                    Version = new Version(
                        identity.Version!.Major + 50,
                        0,
                        0,
                        0)
                },
                AssemblyResolutionScope.Any);
            ResolvedAssemblyReference? wrongToken = resolver.Resolve(
                identity with { PublicKeyToken = "0000000000000000" },
                AssemblyResolutionScope.Any);

            Assert.NotNull(versionSkewed);
            Assert.Null(wrongToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }
}
