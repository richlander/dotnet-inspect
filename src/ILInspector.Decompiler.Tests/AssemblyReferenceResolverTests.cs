using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class AssemblyReferenceResolverTests
{
    [Fact]
    public void SiblingResolver_BareOwnerPathUsesCurrentDirectory()
    {
        string source = typeof(AssemblyReferenceResolverTests).Assembly.Location;
        string candidate = Path.Combine(
            Environment.CurrentDirectory,
            Path.GetFileName(source));
        bool copied = !Path.GetFullPath(source).Equals(
            Path.GetFullPath(candidate),
            StringComparison.Ordinal);
        try
        {
            if (copied)
                File.Copy(source, candidate);

            AssemblyReferenceIdentity identity = ReadIdentity(source);
            var resolver =
                new MetadataSource.SiblingAssemblyReferenceResolver("Owner.dll");

            ResolvedAssemblyReference? result = resolver.Resolve(
                identity,
                AssemblyResolutionScope.Any);

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(candidate), result.Path);
        }
        finally
        {
            if (copied)
                File.Delete(candidate);
        }
    }

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
    public void SiblingResolver_MalformedMetadataIsTyped()
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

            MalformedMetadataRootException exception =
                Assert.Throws<MalformedMetadataRootException>(
                    () => resolver.Resolve(
                        new AssemblyReferenceIdentity(
                            "Invalid",
                            Version: null,
                            Culture: null,
                            PublicKeyToken: null),
                        AssemblyResolutionScope.Any));
            Assert.Equal(
                MetadataRootMalformedReason.UnmappableMetadataDirectory,
                exception.Reason);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SiblingResolver_UnsupportedMetadataIsTyped()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(
                Path.Combine(directory, "Unsupported.dll"),
                MetadataSourceFormatAdmissionTests
                    .BuildManagedWindowsMetadata());
            var resolver =
                new MetadataSource.SiblingAssemblyReferenceResolver(
                    Path.Combine(directory, "Owner.dll"));

            Assert.Throws<UnsupportedMetadataFormatException>(
                () => resolver.Resolve(
                    new AssemblyReferenceIdentity(
                        "Unsupported",
                        Version: null,
                        Culture: null,
                        PublicKeyToken: null),
                    AssemblyResolutionScope.Any));
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

    [Fact]
    public void PathlessDescriptor_DoesNotProbeIdentityDerivedSidecarPath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"pathless-symbols-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath =
            typeof(AssemblyReferenceResolverTests).Assembly.Location;
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        Assert.True(File.Exists(pdbPath));
        string apparentImagePath =
            Path.Combine(directory, "PathlessDescriptor.dll");
        try
        {
            File.Copy(
                pdbPath,
                Path.Combine(directory, Path.GetFileName(pdbPath)));
            File.Copy(
                pdbPath,
                Path.ChangeExtension(apparentImagePath, ".pdb"));
            AssemblyReferenceIdentity identity =
                ReadIdentity(assemblyPath) with
                {
                    Name = apparentImagePath,
                };
            ResolvedAssemblyReference descriptor =
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    () => File.OpenRead(assemblyPath),
                    AssemblyResolutionProvenance.Local("test"));
            var resolver =
                new MetadataSource.SiblingAssemblyReferenceResolver(
                    apparentImagePath);
            using MetadataSource source =
                MetadataSource.Open(
                    descriptor,
                    externalPdbPath: null,
                    resolver);

            Assert.NotNull(IrImporter.Import(
                source,
                typeof(CfgSampleClass).FullName!,
                nameof(CfgSampleClass.DoWhileSum)));
            Assert.Equal(DecompilerSymbolSource.None, source.Symbols);
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
