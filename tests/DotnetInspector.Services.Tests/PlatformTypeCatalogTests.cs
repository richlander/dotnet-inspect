using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class PlatformTypeCatalogTests
{
    [Theory]
    [InlineData("runtime", "1.0.0", "aspnetcore", "2.0.0")]
    [InlineData("aspnetcore", "2.0.0", "runtime", "1.0.0")]
    public void Lookup_SamePathUsesEachSuppliedPlatformIdentity(
        string firstFramework,
        string firstVersion,
        string secondFramework,
        string secondVersion)
    {
        string directory = CreateCatalogDirectory(
            typeof(PlatformTypeCatalogTests).Assembly.Location);
        try
        {
            PlatformTypeLookupCandidate first = Resolved(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    firstFramework,
                    firstVersion));
            PlatformTypeLookupCandidate second = Resolved(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    secondFramework,
                    secondVersion));

            AssertPlatform(first, firstFramework, firstVersion);
            AssertPlatform(second, secondFramework, secondVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Lookup_SamePathObservesReplacementAssemblyPopulation()
    {
        string directory = CreateCatalogDirectory(
            typeof(PlatformTypeCatalogTests).Assembly.Location);
        string assemblyPath = Path.Combine(directory, "CatalogFixture.dll");
        try
        {
            Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    "runtime",
                    "1.0.0"));

            File.Copy(
                typeof(PlatformTypeCatalog).Assembly.Location,
                assemblyPath,
                overwrite: true);

            Assert.IsType<PlatformTypeLookupOutcome.Missing>(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    "runtime",
                    "1.0.0"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Lookup_UnavailableDirectoryIsRetried()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-platform-catalog-{Guid.NewGuid():N}");
        try
        {
            var rejected = Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    "runtime",
                    "1.0.0"));
            Assert.Equal(
                PlatformTypeLookupFailureKind.CatalogUnavailable,
                rejected.Failure.Kind);

            Directory.CreateDirectory(directory);
            File.Copy(
                typeof(PlatformTypeCatalogTests).Assembly.Location,
                Path.Combine(directory, "CatalogFixture.dll"));

            Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    "runtime",
                    "1.0.0"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Lookup_EmptyDirectoryIsRetried()
    {
        string directory = Directory.CreateTempSubdirectory(
            "dotnet-inspect-platform-catalog-").FullName;
        try
        {
            var rejected = Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    "runtime",
                    "1.0.0"));
            Assert.Equal(
                PlatformTypeLookupFailureKind.CatalogUnavailable,
                rejected.Failure.Kind);

            File.Copy(
                typeof(PlatformTypeCatalogTests).Assembly.Location,
                Path.Combine(directory, "CatalogFixture.dll"));

            Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    "runtime",
                    "1.0.0"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Lookup_InvalidAssemblyIsRetried()
    {
        string directory = Directory.CreateTempSubdirectory(
            "dotnet-inspect-platform-catalog-").FullName;
        string assemblyPath = Path.Combine(directory, "CatalogFixture.dll");
        try
        {
            File.WriteAllBytes(
                assemblyPath,
                BuildInvalidForwarderAssembly());
            var rejected = Assert.IsType<PlatformTypeLookupOutcome.Rejected>(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    "runtime",
                    "1.0.0"));
            Assert.Equal(
                PlatformTypeLookupFailureKind.InvalidAssembly,
                rejected.Failure.Kind);

            File.Copy(
                typeof(PlatformTypeCatalogTests).Assembly.Location,
                assemblyPath,
                overwrite: true);

            Assert.IsType<PlatformTypeLookupOutcome.Resolved>(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    "runtime",
                    "1.0.0"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CompletedLookupDoesNotRetainCandidate()
    {
        WeakReference<PlatformTypeLookupCandidate> candidate =
            CreateWeakCandidate();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(candidate.TryGetTarget(out _));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference<PlatformTypeLookupCandidate> CreateWeakCandidate()
    {
        string directory = CreateCatalogDirectory(
            typeof(PlatformTypeCatalogTests).Assembly.Location);
        try
        {
            PlatformTypeLookupCandidate candidate = Resolved(
                PlatformTypeCatalog.Lookup(
                    typeof(PlatformTypeCatalogTests).FullName!,
                    directory,
                    "runtime",
                    "1.0.0"));
            return new WeakReference<PlatformTypeLookupCandidate>(candidate);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static string CreateCatalogDirectory(string assemblySource)
    {
        string directory = Directory.CreateTempSubdirectory(
            "dotnet-inspect-platform-catalog-").FullName;
        File.Copy(
            assemblySource,
            Path.Combine(directory, "CatalogFixture.dll"));
        return directory;
    }

    static byte[] BuildInvalidForwarderAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("InvalidForwarder.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("InvalidForwarder"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle target =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Target"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        metadata.AddExportedType(
            TypeAttributes.Public,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("InvalidForwarder"),
            target,
            typeDefinitionId: 0);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static PlatformTypeLookupCandidate Resolved(
        PlatformTypeLookupOutcome outcome) =>
        Assert.IsType<PlatformTypeLookupOutcome.Resolved>(outcome).Candidate;

    static void AssertPlatform(
        PlatformTypeLookupCandidate candidate,
        string framework,
        string version)
    {
        var provenance =
            Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
                candidate.Assembly.Provenance);
        Assert.Equal(framework, provenance.Framework);
        Assert.Equal(version, provenance.FrameworkVersion);
    }
}
