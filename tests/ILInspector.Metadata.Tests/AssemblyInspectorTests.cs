using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class AssemblyInspectorTests
{
    public static IEnumerable<object[]> StrongNamedAssemblies()
    {
        yield return [typeof(System.Text.Json.JsonSerializer).Assembly];
        yield return [typeof(object).Assembly];
        yield return [typeof(System.Linq.Enumerable).Assembly];
    }

    [Theory]
    [MemberData(nameof(StrongNamedAssemblies))]
    public void ExtractAssemblyInfo_PublicKeyToken_MatchesRuntimeGroundTruth(Assembly assembly)
    {
        var expectedTokenBytes = Assert.IsType<byte[]>(AssemblyName.GetAssemblyName(assembly.Location).GetPublicKeyToken());
        Assert.NotEmpty(expectedTokenBytes);

        using var stream = File.OpenRead(assembly.Location);
        using var peReader = new PEReader(stream);

        var info = AssemblyInspector.ExtractAssemblyInfo(peReader);

        var expectedToken = Convert.ToHexString(expectedTokenBytes).ToLowerInvariant();
        Assert.Equal(expectedToken, info.PublicKeyToken);
    }

    [Fact]
    public void ExtractReferences_OpenImage_MatchesAssemblyInfoProjection()
    {
        using var stream = File.OpenRead(typeof(AssemblyInspectorTests).Assembly.Location);
        using var peReader = new PEReader(stream);

        var references = AssemblyInspector.ExtractReferences(peReader);
        var assemblyInfo = AssemblyInspector.ExtractAssemblyInfo(
            peReader,
            includeReferences: true);

        Assert.Equal(assemblyInfo.References, references);
    }

    [Fact]
    public void ExtractReferences_DerivesTokenFromFullPublicKey()
    {
        byte[] publicKey = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("ReferenceOwner.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ReferenceOwner"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("FullKey.Dependency"),
            new Version(2, 0, 0, 0),
            culture: default,
            publicKeyOrToken: metadata.GetOrAddBlob(publicKey),
            flags: AssemblyFlags.PublicKey,
            hashValue: default);
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        using var peReader = new PEReader(new MemoryStream(image.ToArray()));

        AssemblyReference reference =
            Assert.Single(AssemblyInspector.ExtractReferences(peReader));

        Assert.Equal("FullKey.Dependency", reference.Name);
        Assert.Equal(
            AssemblyReferenceIdentity.ComputePublicKeyToken(publicKey),
            reference.PublicKeyToken);
        Assert.NotEqual(
            Convert.ToHexString(publicKey).ToLowerInvariant(),
            reference.PublicKeyToken);
    }
}
