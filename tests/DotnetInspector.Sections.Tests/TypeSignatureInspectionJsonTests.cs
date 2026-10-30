using System.Text.Json;

using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeSignatureInspectionJsonTests
{
    [Fact]
    public void SerializeContent_PreservesStructuralBaseAndConstraintTypes()
    {
        Type type = typeof(SignatureTransportFixture<>);
        MetadataTypeDefinitionName name =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.ParseSerialized(type.FullName!)).Name;
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.CreateFromPath(
            type.Assembly.Location,
            AssemblyResolutionProvenance.Local("Signature transport test"));
        InspectionEnvelope<TypeSignatureInspectionOutcome> envelope =
            TypeSignatureInspectionOperation.Execute(
                assembly,
                new(
                    name,
                    new ApiSurfaceExtractionBounds(
                        0, 0, 0, 0, 1_000_000, 0),
                    new MetadataExactTypeInput(name)),
                TestContext.Current.CancellationToken);
        _ = Assert.IsType<TypeSignatureInspectionOutcome.Available>(envelope.Content);

        JsonElement json = TypeSignatureInspectionJson.SerializeContent(envelope.Content);

        Assert.Equal("available", json.GetProperty("outcome").GetString());
        JsonElement signature = json.GetProperty("signature");
        JsonElement baseType = signature.GetProperty("baseType").GetProperty("type");
        Assert.Equal("GenericInstance", baseType.GetProperty("shape").GetString());
        Assert.Equal(
            "TypeParameter",
            Assert.Single(baseType.GetProperty("arguments").EnumerateArray())
                .GetProperty("shape").GetString());
        JsonElement parameter =
            Assert.Single(signature.GetProperty("typeParameters").EnumerateArray());
        Assert.Equal("T", parameter.GetProperty("name").GetString());
        JsonElement constraint =
            Assert.Single(parameter.GetProperty("typeConstraints").EnumerateArray());
        Assert.Equal(
            "Named",
            constraint.GetProperty("type").GetProperty("shape").GetString());
        Assert.Contains("IDisposable", constraint.GetRawText(), StringComparison.Ordinal);
    }
}

public sealed class SignatureTransportFixture<T> : List<T> where T : IDisposable;
