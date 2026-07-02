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
        Assert.Equal("1", member.SignatureModel.Parameters[3].DefaultValueText);
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

    [Fact]
    public void ConstructorSignatureModel_ExposesParameterFacts()
    {
        var ctor = GetType(nameof(ApiSignatureFixtures)).Members
            .Where(member => member.Kind == "constructor")
            .Single(member => member.SignatureModel?.ParameterCount == 2);

        Assert.NotNull(ctor.SignatureModel);
        Assert.Equal(".ctor", ctor.SignatureModel.MemberName);
        Assert.Equal(2, ctor.SignatureModel.ParameterCount);
        Assert.Equal("(string, int)", ctor.SignatureModel.ParameterTypesSummary);
        Assert.Equal("name", ctor.SignatureModel.Parameters[0].Name);
        Assert.Equal("string", ctor.SignatureModel.Parameters[0].Type);
        Assert.False(ctor.SignatureModel.Parameters[0].HasDefault);
        Assert.Equal("count", ctor.SignatureModel.Parameters[1].Name);
        Assert.Equal("int", ctor.SignatureModel.Parameters[1].Type);
        Assert.True(ctor.SignatureModel.Parameters[1].HasDefault);
        Assert.Equal("1", ctor.SignatureModel.Parameters[1].DefaultValueText);
    }

    [Fact]
    public void CanonicalSignature_UsesStructuredSignatureModel()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithRefKinds));
        var member = new ApiMember
        {
            Name = source.Name,
            Kind = source.Kind,
            Signature = "BROKEN",
            SignatureModel = source.SignatureModel
        };

        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, member, out var canonical));

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiSignatureFixtures.MethodWithRefKinds(ref int,out string,in long,int,params byte[])",
            canonical);
    }

    [Fact]
    public void CanonicalSignature_UsesGenericMethodNameFromStructuredModel()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.GenericMethod));
        var member = new ApiMember
        {
            Name = source.Name,
            Kind = source.Kind,
            Signature = "BROKEN",
            SignatureModel = source.SignatureModel
        };

        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, member, out var canonical));

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiSignatureFixtures.GenericMethod<T>(T)",
            canonical);
        Assert.Equal("T", source.SignatureModel!.TypeParameters.Single().Name);
    }

    [Fact]
    public void MethodSignatureModel_ExposesGenericParameterConstraints()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.ConstrainedGenericMethod));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal("ConstrainedGenericMethod<TSource, TResult>", member.SignatureModel.MemberName);
        Assert.Equal("TResult", member.SignatureModel.ReturnType);
        Assert.Equal(2, member.SignatureModel.TypeParameters.Count);
        Assert.Equal("TSource", member.SignatureModel.TypeParameters[0].Name);
        Assert.Equal(["unmanaged"], member.SignatureModel.TypeParameters[0].Constraints);
        Assert.Equal("TResult", member.SignatureModel.TypeParameters[1].Name);
        Assert.Equal(["notnull", "System.IComparable<TResult>", "new()"], member.SignatureModel.TypeParameters[1].Constraints);
    }

    [Fact]
    public void MethodSignatureModel_ExposesSourceRenderableParameterAttributes()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithParameterAttribute));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterMarker(\"id\", Order = 2)"],
            member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_RendersParameterAttributeTypeAndEscapedStringArguments()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithTypeParameterAttribute));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterTypeMarker(typeof(System.Int32), Text = \"System.String\\\\Path\\nNext\")"],
            member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_RendersNestedTypeParameterAttributeArguments()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithNestedTypeParameterAttribute));

        Assert.NotNull(member.SignatureModel);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterTypeMarker(typeof(ILInspector.Metadata.Tests.ApiSignatureFixtures.Nested))"],
            member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_SkipsUnspellableGenericTypeParameterAttributeArguments()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithGenericTypeParameterAttribute));

        Assert.NotNull(member.SignatureModel);
        Assert.Empty(member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_SuppressesCompilerReservedRefReadonlyParameterAttributes()
    {
        var member = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithRefReadonlyParameter));

        Assert.NotNull(member.SignatureModel);
        Assert.Empty(member.SignatureModel.Parameters[0].Attributes);
    }

    [Fact]
    public void MethodSignatureModel_EscapesCharArguments()
    {
        var quote = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithQuoteCharAttribute));
        var backslash = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithBackslashCharAttribute));
        var newline = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithNewlineCharAttribute));

        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterCharMarker('\\'')"],
            quote.SignatureModel?.Parameters[0].Attributes);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterCharMarker('\\\\')"],
            backslash.SignatureModel?.Parameters[0].Attributes);
        Assert.Equal(
            ["ILInspector.Metadata.Tests.ParameterCharMarker('\\n')"],
            newline.SignatureModel?.Parameters[0].Attributes);
    }

    [Fact]
    public void CanonicalSignature_NormalizesMultiGenericMethodNameWhitespace()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.PairGenericMethod));
        var member = new ApiMember
        {
            Name = source.Name,
            Kind = source.Kind,
            Signature = "BROKEN",
            SignatureModel = source.SignatureModel
        };

        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, member, out var canonical));

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiSignatureFixtures.PairGenericMethod<TLeft,TRight>(TLeft,TRight)",
            canonical);
    }

    [Fact]
    public void CanonicalSignature_PreservesLegacyGenericNameSubstringDigestContract()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.Validate));

        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, source, out var canonical));

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiSignatureFixtures.Validate()",
            canonical);
    }

    [Fact]
    public void XmlDocIdentity_UsesStructuredMethodParameters()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.MethodWithRefKinds));

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, source, out var identity));

        Assert.Equal("M:ILInspector.Metadata.Tests.ApiSignatureFixtures.MethodWithRefKinds", identity.LookupKey);
        Assert.Equal(
            ["System.Int32@", "System.String@", "System.Int64@", "System.Int32", "System.Byte[]"],
            identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_MapsGenericTypeAndMethodParameters()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), nameof(ApiSignatureFixtures.PairGenericMethod));

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, source, out var identity));

        Assert.Equal("M:ILInspector.Metadata.Tests.ApiSignatureFixtures.PairGenericMethod", identity.LookupKey);
        Assert.Equal(["M0", "M1"], identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_UsesIndexerParametersForProperties()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), "Item");

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, source, out var identity));

        Assert.Equal("P:ILInspector.Metadata.Tests.ApiSignatureFixtures.Item", identity.LookupKey);
        Assert.Equal(["System.Int32"], identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_UsesHashForExplicitInterfaceMembers()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Impl" };
        var member = new ApiMember
        {
            Name = "IFoo.Bar",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature { MemberName = "IFoo.Bar" }
        };

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out var identity));

        Assert.Equal("M:Samples.Impl.IFoo#Bar", identity.LookupKey);
    }

    [Fact]
    public void XmlDocIdentity_UsesBracesForGenericExplicitInterfaceMembers()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Impl" };
        var member = new ApiMember
        {
            Name = "System.IEquatable<System.String>.Equals",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                MemberName = "System.IEquatable<System.String>.Equals",
                Parameters = [new ApiParameter { Name = "other", Type = "System.String" }]
            }
        };

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out var identity));

        Assert.Equal("M:Samples.Impl.System#IEquatable{System#String}#Equals", identity.LookupKey);
        Assert.Equal(["System.String"], identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_IncludesConversionOperatorReturnType()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Number" };
        var member = new ApiMember
        {
            Name = "op_Implicit",
            Kind = "operator",
            SignatureModel = new ApiSignature
            {
                MemberName = "op_Implicit",
                ReturnType = "int",
                Parameters = [new ApiParameter { Name = "value", Type = "Samples.Number" }]
            }
        };

        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out var identity));

        Assert.Equal("System.Int32", identity.NormalizedReturnType);
        Assert.Equal(["Samples.Number"], identity.NormalizedParameters);
    }

    [Fact]
    public void XmlDocIdentity_FallsBackWhenSignatureModelIsMissing()
    {
        var type = GetType(nameof(ApiSignatureFixtures));
        var source = GetMember(nameof(ApiSignatureFixtures), "Item");
        var member = new ApiMember
        {
            Name = source.Name,
            Kind = source.Kind,
            Signature = source.Signature
        };

        Assert.False(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out _));
    }

    [Fact]
    public void XmlDocParameterNormalization_UsesGenericMaps()
    {
        Dictionary<string, int> typeParameters = new(StringComparer.Ordinal)
        {
            ["TType"] = 0
        };
        Dictionary<string, int> methodParameters = new(StringComparer.Ordinal)
        {
            ["TMethod"] = 0
        };

        Assert.Equal("T0", ApiMemberIdentity.NormalizeXmlDocParameterType("TType", typeParameters, methodParameters));
        Assert.Equal("M0", ApiMemberIdentity.NormalizeXmlDocParameterType("TMethod", typeParameters, methodParameters));
        Assert.Equal(
            "System.Collections.Generic.Dictionary{T0,M0}",
            ApiMemberIdentity.NormalizeXmlDocParameterType(
                "System.Collections.Generic.Dictionary<TType, TMethod>",
                typeParameters,
                methodParameters));
    }

    [Fact]
    public void XmlDocSignatureParameterNormalization_StripsParameterNamesForFallback()
    {
        Assert.Equal(
            "System.Int32",
            ApiMemberIdentity.NormalizeXmlDocSignatureParameter(
                "int index",
                new Dictionary<string, int>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal)));
        Assert.Equal(
            "System.Collections.Generic.Dictionary{System.String,System.Int32}",
            ApiMemberIdentity.NormalizeXmlDocSignatureParameter(
                "System.Collections.Generic.Dictionary<string, int> values = null",
                new Dictionary<string, int>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal)));
        Assert.Equal(
            "System.Int32",
            ApiMemberIdentity.NormalizeXmlDocSignatureParameter(
                """[System.ComponentModel.DefaultValue("=")] int value = 0""",
                new Dictionary<string, int>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal)));
        Assert.Equal(
            "System.Int32@",
            ApiMemberIdentity.NormalizeXmlDocSignatureParameter(
                "[System.Runtime.InteropServices.In] ref int value",
                new Dictionary<string, int>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal)));
        Assert.Equal(
            "System.Int32",
            ApiMemberIdentity.NormalizeXmlDocSignatureParameter(
                """[System.ComponentModel.DefaultValue("=")] int value = 0""",
                new Dictionary<string, int>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("ref int", "System.Int32@")]
    [InlineData("out int", "System.Int32@")]
    [InlineData("in int", "System.Int32@")]
    [InlineData("System.Int32@", "System.Int32@")]
    [InlineData("params int[]", "System.Int32[]")]
    [InlineData("int*", "System.Int32*")]
    [InlineData("int[,]", "System.Int32[,]")]
    [InlineData("System.Int32[0:,0:]", "System.Int32[,]")]
    [InlineData("int?", "System.Nullable{System.Int32}")]
    [InlineData("System.DateTime?", "System.Nullable{System.DateTime}")]
    [InlineData("string?", "System.String")]
    public void XmlDocParameterNormalization_PreservesByRefMarkers(string input, string expected)
    {
        Assert.Equal(expected, ApiMemberIdentity.NormalizeXmlDocParameterType(input));
    }

    static ApiType GetType(string typeName)
        => Surface.Types.First(type => type.Name == typeName);

    static ApiMember GetMember(string typeName, string memberName)
        => GetType(typeName)
            .Members
            .First(member => member.Name == memberName);
}

public sealed class ApiSignatureFixtures
{
    public int Count;

    public event EventHandler? Changed;

    public ApiSignatureFixtures()
    {
    }

    public ApiSignatureFixtures(string name, int count = 1)
    {
        Count = name.Length + count;
    }

    public string MethodWithRefKinds(ref int value, out string text, in long source, int count = 1, params byte[] bytes)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        text = count.ToString();
        return text;
    }

    public T GenericMethod<T>(T value) => value;

    public (TLeft Left, TRight Right) PairGenericMethod<TLeft, TRight>(TLeft left, TRight right)
        => (left, right);

    public TResult ConstrainedGenericMethod<TSource, TResult>(TSource source)
        where TSource : unmanaged
        where TResult : IComparable<TResult>, new()
        => new();

    public void Validate<TValidateOptions>()
    {
    }

    public void MethodWithParameterAttribute([ParameterMarker("id", Order = 2)] string id)
    {
    }

    public void MethodWithTypeParameterAttribute([ParameterTypeMarker(typeof(int), Text = "System.String\\Path\nNext")] string value)
    {
    }

    public void MethodWithNestedTypeParameterAttribute([ParameterTypeMarker(typeof(Nested))] string value)
    {
    }

    public void MethodWithGenericTypeParameterAttribute([ParameterTypeMarker(typeof(List<int>))] string value)
    {
    }

    public void MethodWithRefReadonlyParameter(ref readonly int value)
    {
    }

    public void MethodWithQuoteCharAttribute([ParameterCharMarker('\'')] string value)
    {
    }

    public void MethodWithBackslashCharAttribute([ParameterCharMarker('\\')] string value)
    {
    }

    public void MethodWithNewlineCharAttribute([ParameterCharMarker('\n')] string value)
    {
    }

    public sealed class Nested
    {
    }

    public string this[int index]
    {
        get => index.ToString();
        private set { }
    }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParameterMarkerAttribute : Attribute
{
    public ParameterMarkerAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public int Order { get; set; }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParameterTypeMarkerAttribute : Attribute
{
    public ParameterTypeMarkerAttribute(Type type)
    {
        Type = type;
    }

    public Type Type { get; }
    public string? Text { get; set; }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParameterCharMarkerAttribute : Attribute
{
    public ParameterCharMarkerAttribute(char value)
    {
        Value = value;
    }

    public char Value { get; }
}
