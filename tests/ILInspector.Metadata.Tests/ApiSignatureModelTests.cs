using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class ApiSignatureModelTests
{
    static readonly ApiSurface Surface;

    static ApiSignatureModelTests()
    {
        using var stream = File.OpenRead(typeof(ApiSignatureModelTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        Surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    [Fact]
    public void MethodSignatureModel_ExposesReturnTypeParametersAndDefaults()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithRefKinds));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal("string", member.SignatureModel.ReturnType);
        Assert.Equal("MethodWithRefKinds", member.SignatureModel.MemberName);
        Assert.Equal(5, member.SignatureModel.ParameterCount);
        Assert.Equal("(ref int, out string, in long, int, params byte[])", member.SignatureModel.ParameterTypesSummary);
        Assert.Equal("value", member.SignatureModel.Parameters[0].Name);
        Assert.Equal("ref", member.SignatureModel.Parameters[0].Modifier);
        Assert.Equal("int", member.SignatureModel.Parameters[0].Type);
        Assert.True(member.SignatureModel.Parameters[3].HasDefault);
    }

    [Fact]
    public void PropertySignatureModel_ExposesReturnTypeIndexerParametersAndPublicAccessors()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), "Item");

        Assert.NotNull(member.SignatureModel);
        Assert.Equal("string", member.SignatureModel.ReturnType);
        Assert.Equal("this[]", member.SignatureModel.MemberName);
        Assert.Equal("(int)", member.SignatureModel.ParameterTypesSummary);
        Assert.Equal("get", member.SignatureModel.PublicAccessorsSummary);
    }

    [Fact]
    public void FieldAndEventSignatureModels_ExposeReturnType()
    {
        var field = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.Count));
        var evt = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.Changed));

        Assert.Equal("int", field.SignatureModel?.ReturnType);
        Assert.Equal("System.EventHandler?", evt.SignatureModel?.ReturnType);
    }

    static ApiMember GetMember(string typeName, string memberName)
        => Surface.Types
            .First(type => type.Name == typeName)
            .Members
            .First(member => member.Name == memberName);
}

public sealed class ApiSignatureFixtures
{
    public int Count;

    public event EventHandler? Changed;

    public string MethodWithRefKinds(ref int value, out string text, in long source, int count = 1, params byte[] bytes)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        text = count.ToString();
        return text;
    }

    public string this[int index]
    {
        get => index.ToString();
        private set { }
    }
}
