using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public class ApiMemberIdentityTests
{
    [Theory]
    [InlineData(".ctor", false, ".ctor")]
    [InlineData("op_Addition", false, "operator:op_Addition")]
    [InlineData("IFoo.Bar", false, "explicit:IFoo.Bar")]
    [InlineData("Twice", true, "extension:Twice")]
    [InlineData("M", false, "M")]
    public void GetMemberSelectorName_PreservesMemberIndexPrefixes(string metadataName, bool isExtension, string expected)
    {
        Assert.Equal(expected, ApiMemberIdentity.GetMemberSelectorName(metadataName, isExtension));
    }

    [Fact]
    public void CreateMethodAnchor_UsesMetadataCanonicalSignature()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (typeHandle, method) = FindFixtureMethod(reader);

        var anchor = ApiMemberIdentity.CreateMethodAnchor(reader, typeHandle, method);

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiMemberIdentityTests.ApiMemberIdentityFixture<T>.M<U>(System.Int32,U)",
            anchor.CanonicalSignature);
        Assert.Equal("ILInspector.Metadata.Tests.ApiMemberIdentityTests.ApiMemberIdentityFixture<T>", anchor.TypeFullName);
        Assert.Equal("M<U>", anchor.MemberName);
        Assert.StartsWith("M~", anchor.StableSelector, StringComparison.Ordinal);
        Assert.Equal(MemberAnchor.ComputeFingerprint(anchor.CanonicalSignature), anchor.Fingerprint);
    }

    static (TypeDefinitionHandle TypeHandle, MethodDefinition Method) FindFixtureMethod(MetadataReader reader)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != "ApiMemberIdentityFixture`1")
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == "M")
                    return (typeHandle, method);
            }
        }

        throw new InvalidOperationException("Fixture method not found.");
    }

    sealed class ApiMemberIdentityFixture<T>
    {
        public void M<U>(int value, U item)
        {
        }
    }
}
