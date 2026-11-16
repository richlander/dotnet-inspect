using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MethodBodyDefinitionSelectionTests
{
    [Fact]
    public void LegacyDottedIdentityCannotChooseBetweenDistinctDefinitions()
    {
        using var session = AssemblyInspectionSession.OpenPrefetched(new MemoryStream(CollidingImage()));
        Assert.Null(session.MethodBodies.ResolveMethod("N.Outer.Inner", "M", 0, publicOnly: true));
        Assert.False(session.MethodBodies.ContainsType("N.Outer.Inner"));
        Assert.Null(session.MethodBodies.ResolveMethod("n.outer.inner", "M", 0, publicOnly: true));
    }

    [Theory]
    [InlineData(0x06000001)]
    [InlineData(0x06000002)]
    public void IssuedMethodCoordinatePreservesItsDefinitionDespiteDottedCollision(int token)
    {
        using var session = AssemblyInspectionSession.OpenPrefetched(new MemoryStream(CollidingImage()));
        MethodBodySelection? selected = session.MethodBodies.ResolveMethod(
            "N.Outer.Inner", "M", 0, publicOnly: true, preferredToken: token);
        Assert.NotNull(selected);
        Assert.Equal(token, selected.MetadataToken);
        Assert.True(selected.HasBody);
        Assert.Null(session.MethodBodies.ResolveMethod("Wrong.Type", "M", 0, true, token));
        Assert.Null(session.MethodBodies.ResolveMethod("N.Outer.Inner", "Other", 0, true, token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x06000001)]
    [InlineData(0x02000005)]
    public void BodyFastPathRequiresATypeDefInItsOwnImage(int token)
    {
        using var session = AssemblyInspectionSession.OpenPrefetched(new MemoryStream(CollidingImage()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.MethodBodies.ResolveUniqueMethod(token, "M", publicOnly: false));
    }

    static byte[] CollidingImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("BodyDefinitions.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("BodyDefinitions"), new(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        var il = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(il);
        var code = new BlobBuilder();
        var instructions = new InstructionEncoder(code);
        instructions.OpCode(ILOpCode.Ret);
        int body = bodies.AddMethodBody(instructions);
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(0, r => r.Void(), _ => { });
        BlobHandle sig = metadata.GetOrAddBlob(signature);
        var field = MetadataTokens.FieldDefinitionHandle(1);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default, field, MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("N"), metadata.GetOrAddString("Outer"), default, field, MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle inner = metadata.AddTypeDefinition(TypeAttributes.NestedPublic, default, metadata.GetOrAddString("Inner"), default, field, MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(inner, outer);
        metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("N"), metadata.GetOrAddString("Outer.Inner"), default, field, MetadataTokens.MethodDefinitionHandle(2));
        for (int i = 0; i < 2; i++)
            metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static, MethodImplAttributes.IL,
                metadata.GetOrAddString("M"), sig, body, MetadataTokens.ParameterHandle(1));
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll), new MetadataRootBuilder(metadata), il);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
